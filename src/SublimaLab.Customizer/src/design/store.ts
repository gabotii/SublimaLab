import { createStore } from 'zustand/vanilla';
import { clampToArea, fitArtwork, normalizeRotation } from './geometry';
import {
  type Artwork,
  type DesignDocument,
  type DesignElement,
  emptyDesign,
  newId,
  type PrintArea,
  surfaceOf,
  type TextElement,
} from './model';
import { measureTextBox, type LineMeasurer, canvasMeasurer } from './text';

const HISTORY_LIMIT = 100;

export interface StudioState {
  areas: PrintArea[];
  activeArea: string;
  design: DesignDocument;
  selectedId: string | null;
  past: DesignDocument[];
  future: DesignDocument[];
  artworks: Record<string, Artwork>;
  /** Snapshot taken when a drag/resize/rotate gesture starts; committed as one undo step. */
  gestureStart: DesignDocument | null;

  setActiveArea(key: string): void;
  select(id: string | null): void;
  loadDesign(design: DesignDocument): void;
  registerArtwork(artwork: Artwork): void;
  addImage(artwork: Artwork): void;
  replaceImage(elementId: string, artwork: Artwork): void;
  addText(preset?: Partial<TextElement>): void;
  /** Updates an element. With `transient`, the change is part of an ongoing gesture (no undo step yet). */
  updateElement(id: string, patch: Partial<DesignElement>, options?: { transient?: boolean }): void;
  beginGesture(): void;
  endGesture(): void;
  removeElement(id: string): void;
  duplicateElement(id: string): void;
  moveLayer(id: string, direction: 'up' | 'down' | 'top' | 'bottom'): void;
  undo(): void;
  redo(): void;
  clearSurface(): void;
}

export type StudioStore = ReturnType<typeof createStudioStore>;

export function createStudioStore(areas: PrintArea[], measure: LineMeasurer = canvasMeasurer) {
  return createStore<StudioState>()((set, get) => {
    const area = (key = get().activeArea) => areas.find((a) => a.key === key) ?? areas[0]!;

    /** Applies a change to the active surface and records an undo step. */
    const commit = (change: (design: DesignDocument) => DesignDocument, selectedId?: string | null) => {
      const { design, past } = get();
      const next = change(design);
      if (next === design) return;
      set({
        design: next,
        past: [...past, design].slice(-HISTORY_LIMIT),
        future: [],
        ...(selectedId !== undefined ? { selectedId } : {}),
      });
    };

    const mapSurface = (design: DesignDocument, key: string, map: (elements: DesignElement[]) => DesignElement[]): DesignDocument => ({
      ...design,
      surfaces: design.surfaces.map((s) => (s.printArea === key ? { ...s, elements: map(s.elements) } : s)),
    });

    const findSurfaceKey = (design: DesignDocument, id: string) => design.surfaces.find((s) => s.elements.some((e) => e.id === id))?.printArea;

    const withTextBox = (el: TextElement): TextElement => ({ ...el, ...measureTextBox(el, measure) });

    return {
      areas,
      activeArea: areas[0]?.key ?? 'front',
      design: emptyDesign(areas),
      selectedId: null,
      past: [],
      future: [],
      artworks: {},
      gestureStart: null,

      setActiveArea: (key) => set({ activeArea: key, selectedId: null }),

      select: (id) => set({ selectedId: id }),

      loadDesign: (design) => set({ design, past: [], future: [], selectedId: null }),

      registerArtwork: (artwork) => set((s) => ({ artworks: { ...s.artworks, [artwork.uploadId]: artwork } })),

      addImage: (artwork) => {
        get().registerArtwork(artwork);
        const target = area();
        const size = fitArtwork(artwork, target);
        const element: DesignElement = {
          type: 'image',
          id: newId('img'),
          uploadId: artwork.uploadId,
          x: target.widthMm / 2,
          y: target.heightMm / 2,
          rotation: 0,
          ...size,
        };
        commit((d) => mapSurface(d, target.key, (els) => [...els, element]), element.id);
      },

      replaceImage: (elementId, artwork) => {
        get().registerArtwork(artwork);
        const key = findSurfaceKey(get().design, elementId);
        if (!key) return;
        commit((d) =>
          mapSurface(d, key, (els) =>
            els.map((e) => {
              if (e.id !== elementId || e.type !== 'image') return e;
              // Keep the width and position, adopt the new image's proportions.
              const height = e.width * (artwork.height / artwork.width);
              return clampToArea({ ...e, uploadId: artwork.uploadId, height }, area(key));
            }),
          ),
        );
      },

      addText: (preset) => {
        const target = area();
        const base: TextElement = withTextBox({
          type: 'text',
          id: newId('txt'),
          text: 'Your text',
          fontFamily: 'Poppins',
          fontSize: Math.round(Math.max(12, Math.min(48, target.widthMm / 6))),
          color: '#1F2330',
          bold: true,
          italic: false,
          align: 'Center',
          letterSpacing: 0,
          x: target.widthMm / 2,
          y: target.heightMm * 0.3,
          width: 0,
          height: 0,
          rotation: 0,
          ...preset,
        });
        const element = clampToArea(base, target);
        commit((d) => mapSurface(d, target.key, (els) => [...els, element]), element.id);
      },

      updateElement: (id, patch, options) => {
        const { design } = get();
        const key = findSurfaceKey(design, id);
        if (!key) return;
        const change = (d: DesignDocument) =>
          mapSurface(d, key, (els) =>
            els.map((e) => {
              if (e.id !== id) return e;
              let next = { ...e, ...patch } as DesignElement;
              if (patch.rotation !== undefined) next.rotation = normalizeRotation(next.rotation);
              if (next.type === 'text' && ('text' in patch || 'fontFamily' in patch || 'fontSize' in patch || 'bold' in patch || 'italic' in patch || 'letterSpacing' in patch)) {
                next = withTextBox(next);
              }
              return clampToArea(next, area(key));
            }),
          );

        if (options?.transient) {
          set({ design: change(design) });
        } else {
          commit(change);
        }
      },

      beginGesture: () => set({ gestureStart: get().design }),

      endGesture: () => {
        const { gestureStart, design, past } = get();
        if (gestureStart && gestureStart !== design) {
          set({ past: [...past, gestureStart].slice(-HISTORY_LIMIT), future: [], gestureStart: null });
        } else {
          set({ gestureStart: null });
        }
      },

      removeElement: (id) => {
        const key = findSurfaceKey(get().design, id);
        if (!key) return;
        commit((d) => mapSurface(d, key, (els) => els.filter((e) => e.id !== id)), null);
      },

      duplicateElement: (id) => {
        const { design } = get();
        const key = findSurfaceKey(design, id);
        const source = key ? surfaceOf(design, key).elements.find((e) => e.id === id) : undefined;
        if (!key || !source) return;
        const copy = clampToArea({ ...source, id: newId(source.type === 'image' ? 'img' : 'txt'), x: source.x + 8, y: source.y + 8 }, area(key));
        commit((d) => mapSurface(d, key, (els) => [...els, copy]), copy.id);
      },

      moveLayer: (id, direction) => {
        const key = findSurfaceKey(get().design, id);
        if (!key) return;
        commit((d) =>
          mapSurface(d, key, (els) => {
            const index = els.findIndex((e) => e.id === id);
            const target = { up: index + 1, down: index - 1, top: els.length - 1, bottom: 0 }[direction];
            if (index < 0 || target < 0 || target >= els.length || target === index) return els;
            const next = [...els];
            const [moved] = next.splice(index, 1);
            next.splice(target, 0, moved!);
            return next;
          }),
        );
      },

      undo: () => {
        const { past, design, future } = get();
        const previous = past.at(-1);
        if (!previous) return;
        set({ design: previous, past: past.slice(0, -1), future: [design, ...future], selectedId: keepSelection(previous, get().selectedId) });
      },

      redo: () => {
        const { past, design, future } = get();
        const next = future[0];
        if (!next) return;
        set({ design: next, past: [...past, design], future: future.slice(1), selectedId: keepSelection(next, get().selectedId) });
      },

      clearSurface: () => commit((d) => mapSurface(d, get().activeArea, (els) => (els.length === 0 ? els : [])), null),
    };
  });
}

const keepSelection = (design: DesignDocument, id: string | null) =>
  id && design.surfaces.some((s) => s.elements.some((e) => e.id === id)) ? id : null;
