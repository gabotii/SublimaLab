import { useEffect, useLayoutEffect, useRef, useState, type PointerEvent as ReactPointerEvent } from 'react';
import { useStudio, useStudioState } from '../context';
import { isInsideArea, maxScaleWithin } from '../design/geometry';
import type { DesignElement } from '../design/model';
import { renderSurface } from '../design/render';

type Gesture =
  | { kind: 'move'; id: string; startX: number; startY: number; originX: number; originY: number }
  | { kind: 'resize'; id: string; centerX: number; centerY: number; startDistance: number; element: DesignElement }
  | { kind: 'rotate'; id: string; centerX: number; centerY: number };

/**
 * The print area at real proportions. The surface is drawn by the shared canvas renderer;
 * transparent boxes on top handle selection and pointer/touch/keyboard manipulation.
 */
export function Editor2D({ color }: { color: string }) {
  const { product, store, images } = useStudio();
  const areas = product.customization!.printAreas;
  const activeArea = useStudioState((s) => s.activeArea);
  const area = areas.find((a) => a.key === activeArea) ?? areas[0]!;
  const elements = useStudioState((s) => s.design.surfaces.find((x) => x.printArea === activeArea)?.elements ?? NO_ELEMENTS);
  const selectedId = useStudioState((s) => s.selectedId);
  const artworks = useStudioState((s) => s.artworks);

  const containerRef = useRef<HTMLDivElement>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const gesture = useRef<Gesture | null>(null);
  const [scale, setScale] = useState(1);
  const [imagesVersion, setImagesVersion] = useState(0);

  // Fit the print area (plus a product margin) into the available space.
  useLayoutEffect(() => {
    const container = containerRef.current;
    if (!container) return;
    const measure = () => {
      const width = container.clientWidth;
      const maxHeight = Math.max(260, Math.min(window.innerHeight * 0.62, 640));
      setScale(Math.max(0.2, Math.min((width * 0.82) / area.widthMm, (maxHeight * 0.86) / area.heightMm)));
    };
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(container);
    return () => observer.disconnect();
  }, [area.widthMm, area.heightMm]);

  useEffect(() => images.subscribe(() => setImagesVersion((v) => v + 1)), [images]);

  useEffect(() => {
    Object.values(artworks).forEach((a) => images.ensure(a.uploadId, a.url));
  }, [artworks, images]);

  // Redraw whenever the surface, scale or loaded images change.
  useEffect(() => {
    const canvas = canvasRef.current;
    const ctx = canvas?.getContext('2d');
    if (!canvas || !ctx) return;
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    canvas.width = Math.round(area.widthMm * scale * dpr);
    canvas.height = Math.round(area.heightMm * scale * dpr);
    renderSurface(ctx, { printArea: area.key, elements }, area, scale * dpr, images.map);
  }, [elements, area, scale, images, imagesVersion]);

  const outOfBounds = elements.filter((e) => !isInsideArea(e, area));
  const areaWidth = area.widthMm * scale;
  const areaHeight = area.heightMm * scale;
  const pad = Math.max(24, areaWidth * 0.08);

  const toMm = (clientX: number, clientY: number) => {
    const rect = canvasRef.current!.getBoundingClientRect();
    return { x: (clientX - rect.left) / scale, y: (clientY - rect.top) / scale };
  };

  const startGesture = (e: ReactPointerEvent, next: Gesture) => {
    e.preventDefault();
    e.stopPropagation();
    (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
    gesture.current = next;
    store.getState().select(next.id);
    store.getState().beginGesture();
  };

  const onElementPointerDown = (e: ReactPointerEvent, el: DesignElement) => {
    const p = toMm(e.clientX, e.clientY);
    startGesture(e, { kind: 'move', id: el.id, startX: p.x, startY: p.y, originX: el.x, originY: el.y });
  };

  const onResizePointerDown = (e: ReactPointerEvent, el: DesignElement) => {
    const p = toMm(e.clientX, e.clientY);
    startGesture(e, { kind: 'resize', id: el.id, centerX: el.x, centerY: el.y, startDistance: Math.hypot(p.x - el.x, p.y - el.y), element: el });
  };

  const onRotatePointerDown = (e: ReactPointerEvent, el: DesignElement) => {
    startGesture(e, { kind: 'rotate', id: el.id, centerX: el.x, centerY: el.y });
  };

  const onPointerMove = (e: ReactPointerEvent) => {
    const g = gesture.current;
    if (!g) return;
    const p = toMm(e.clientX, e.clientY);
    const { updateElement } = store.getState();

    if (g.kind === 'move') {
      updateElement(g.id, { x: g.originX + (p.x - g.startX), y: g.originY + (p.y - g.startY) }, { transient: true });
    } else if (g.kind === 'resize') {
      const factor = Math.min(Math.hypot(p.x - g.centerX, p.y - g.centerY) / Math.max(g.startDistance, 1), maxScaleWithin(g.element, area));
      const el = g.element;
      if (el.type === 'image') {
        updateElement(g.id, { width: Math.max(5, el.width * factor), height: Math.max(5, el.height * factor) }, { transient: true });
      } else {
        updateElement(g.id, { fontSize: Math.min(400, Math.max(4, el.fontSize * factor)) }, { transient: true });
      }
    } else {
      let angle = (Math.atan2(p.y - g.centerY, p.x - g.centerX) * 180) / Math.PI + 90;
      if (e.shiftKey) angle = Math.round(angle / 15) * 15;
      updateElement(g.id, { rotation: angle }, { transient: true });
    }
  };

  const onPointerUp = () => {
    if (gesture.current) {
      gesture.current = null;
      store.getState().endGesture();
    }
  };

  return (
    <div className="sls-editor" ref={containerRef}>
      <div
        className="sls-garment"
        style={{ background: color, padding: pad, borderRadius: area.shape === 'Ellipse' ? '50%' : 24 }}
        onPointerDown={() => store.getState().select(null)}
      >
        <div
          className={`sls-print-area${outOfBounds.length > 0 ? ' sls-print-area--error' : ''}${area.shape === 'Ellipse' ? ' sls-print-area--ellipse' : ''}`}
          style={{ width: areaWidth, height: areaHeight }}
          onPointerMove={onPointerMove}
          onPointerUp={onPointerUp}
          onPointerCancel={onPointerUp}
        >
          <canvas ref={canvasRef} className="sls-canvas" style={{ width: areaWidth, height: areaHeight }} aria-hidden="true" />
          {elements.map((el) => (
            <ElementBox
              key={el.id}
              element={el}
              scale={scale}
              selected={el.id === selectedId}
              outOfBounds={!isInsideArea(el, area)}
              label={el.type === 'text' ? `Text: ${el.text}` : `Image: ${artworks[el.uploadId]?.fileName ?? 'artwork'}`}
              onPointerDown={onElementPointerDown}
              onResizePointerDown={onResizePointerDown}
              onRotatePointerDown={onRotatePointerDown}
              onFocus={() => store.getState().select(el.id)}
            />
          ))}
          {elements.length === 0 && (
            <div className="sls-empty" aria-hidden="true">
              <strong>{area.name}</strong>
              <span>Upload artwork or add text to start</span>
            </div>
          )}
        </div>
      </div>
      <p className={`sls-area-caption${outOfBounds.length > 0 ? ' sls-area-caption--error' : ''}`} role="status" aria-live="polite">
        {outOfBounds.length > 0
          ? `${outOfBounds.length === 1 ? 'An element extends' : `${outOfBounds.length} elements extend`} outside the printable area. Make it smaller or rotate it back.`
          : `${area.name} printable area: ${area.widthMm / 10} × ${area.heightMm / 10} cm. Drag to move, use the corner to resize and the top handle to rotate.`}
      </p>
    </div>
  );
}

const NO_ELEMENTS: DesignElement[] = [];

interface ElementBoxProps {
  element: DesignElement;
  scale: number;
  selected: boolean;
  outOfBounds: boolean;
  label: string;
  onPointerDown(e: ReactPointerEvent, el: DesignElement): void;
  onResizePointerDown(e: ReactPointerEvent, el: DesignElement): void;
  onRotatePointerDown(e: ReactPointerEvent, el: DesignElement): void;
  onFocus(): void;
}

function ElementBox({ element, scale, selected, outOfBounds, label, onPointerDown, onResizePointerDown, onRotatePointerDown, onFocus }: ElementBoxProps) {
  return (
    <div
      className={`sls-el${selected ? ' sls-el--selected' : ''}${outOfBounds ? ' sls-el--error' : ''}`}
      style={{
        left: element.x * scale,
        top: element.y * scale,
        width: element.width * scale,
        height: element.height * scale,
        transform: `translate(-50%, -50%) rotate(${element.rotation}deg)`,
      }}
      role="button"
      tabIndex={0}
      aria-label={`${label}. Use arrow keys to move, Delete to remove.`}
      aria-pressed={selected}
      onPointerDown={(e) => onPointerDown(e, element)}
      onFocus={onFocus}
    >
      {selected && (
        <>
          <span className="sls-handle sls-handle--rotate" onPointerDown={(e) => onRotatePointerDown(e, element)} aria-hidden="true" title="Rotate (hold Shift to snap)" />
          <span className="sls-handle sls-handle--resize" onPointerDown={(e) => onResizePointerDown(e, element)} aria-hidden="true" title="Resize" />
        </>
      )}
    </div>
  );
}
