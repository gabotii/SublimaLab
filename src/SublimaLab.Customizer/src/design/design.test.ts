import { describe, expect, it } from 'vitest';
import { clampToArea, effectiveDpi, fitArtwork, isInsideArea, maxScaleWithin, normalizeRotation, rotatedBounds } from './geometry';
import { type DesignElement, emptyDesign, fromApiDocument, type PrintArea, type TextElement, toApiDocument } from './model';
import { createStudioStore } from './store';
import { measureTextBox, type LineMeasurer } from './text';

const front: PrintArea = { key: 'front', name: 'Front', widthMm: 300, heightMm: 400, shape: 'Rectangle' };
const back: PrintArea = { key: 'back', name: 'Back', widthMm: 300, heightMm: 400, shape: 'Rectangle' };

// Deterministic measurer for jsdom (no canvas): 0.5em per character.
const measure: LineMeasurer = (line, font) => line.length * 0.5 * Number(/(\d+)px/.exec(font)?.[1] ?? 10);

const image = (overrides: Partial<DesignElement> = {}): DesignElement => ({ type: 'image', id: 'i1', uploadId: 'u1', x: 150, y: 200, width: 100, height: 50, rotation: 0, ...overrides }) as DesignElement;

describe('geometry', () => {
  it('computes rotated bounds like the server', () => {
    expect(rotatedBounds(image())).toEqual({ left: 100, top: 175, right: 200, bottom: 225 });
    const rotated = rotatedBounds(image({ rotation: 90 }));
    expect(rotated.left).toBeCloseTo(125);
    expect(rotated.top).toBeCloseTo(150);
  });

  it('detects elements outside the print area, with tolerance', () => {
    expect(isInsideArea(image(), front)).toBe(true);
    expect(isInsideArea(image({ x: 40 }), front)).toBe(false);
    expect(isInsideArea(image({ x: 50.4 }), front)).toBe(true); // 0.5 mm tolerance
  });

  it('clamps elements back inside the area', () => {
    const clamped = clampToArea(image({ x: 290, y: -20 }), front);
    expect(clamped.x).toBe(250);
    expect(clamped.y).toBe(25);
    expect(isInsideArea(clamped, front)).toBe(true);
  });

  it('centres elements that are larger than the area instead of pushing them out', () => {
    const huge = clampToArea(image({ width: 500, x: 10 }), front);
    expect(huge.x).toBe(150);
    expect(isInsideArea(huge, front)).toBe(false);
  });

  it('fits new artwork within the area keeping its aspect ratio', () => {
    expect(fitArtwork({ width: 2000, height: 1000 }, front)).toEqual({ width: 240, height: 120 });
    const tall = fitArtwork({ width: 1000, height: 4000 }, front);
    expect(tall.height).toBeCloseTo(320);
    expect(tall.width).toBeCloseTo(80);
  });

  it('limits scaling to what fits', () => {
    expect(maxScaleWithin(image(), front)).toBe(3);
  });

  it('computes effective print resolution', () => {
    // 600 px over 200 mm ≈ 76 DPI (same as the server test).
    expect(effectiveDpi({ width: 600, height: 750 }, { width: 200, height: 250 })).toBe(76);
  });

  it('normalises rotation to -180..180', () => {
    expect(normalizeRotation(270)).toBe(-90);
    expect(normalizeRotation(-190)).toBe(170);
    expect(normalizeRotation(45.04)).toBe(45);
  });
});

describe('design document', () => {
  it('serialises for the API without empty surfaces and with rounded numbers', () => {
    const design = emptyDesign([front, back]);
    design.surfaces[0]!.elements.push(image({ x: 150.123456, rotation: 12.3456 }));
    const api = toApiDocument(design);
    expect(api.surfaces).toHaveLength(1);
    expect(api.surfaces[0]!.elements[0]).toMatchObject({ type: 'image', x: 150.12, rotation: 12.3 });
  });

  it('restores API documents onto every configured print area', () => {
    const restored = fromApiDocument({ schemaVersion: 1, surfaces: [{ printArea: 'BACK', elements: [image()] }] }, [front, back]);
    expect(restored.surfaces.map((s) => s.printArea)).toEqual(['front', 'back']);
    expect(restored.surfaces[1]!.elements).toHaveLength(1);
  });
});

describe('text measurement', () => {
  it('scales linearly with font size and grows with letter spacing', () => {
    const style = { text: 'Hello', fontFamily: 'Inter', fontSize: 20, bold: false, italic: false, letterSpacing: 0 };
    const small = measureTextBox(style, measure);
    const big = measureTextBox({ ...style, fontSize: 40 }, measure);
    const spaced = measureTextBox({ ...style, letterSpacing: 0.5 }, measure);
    expect(big.width - 2).toBeCloseTo((small.width - 2) * 2);
    expect(spaced.width).toBeGreaterThan(small.width);
  });

  it('adds height for each line', () => {
    const style = { text: 'One', fontFamily: 'Inter', fontSize: 20, bold: false, italic: false, letterSpacing: 0 };
    expect(measureTextBox({ ...style, text: 'One\nTwo' }, measure).height).toBeGreaterThan(measureTextBox(style, measure).height * 1.8);
  });
});

describe('studio store', () => {
  const artwork = { uploadId: 'u1', url: '/api/uploads/u1/content', width: 2000, height: 1000, fileName: 'art.png' };

  it('adds artwork centred on the active print area and selects it', () => {
    const store = createStudioStore([front, back], measure);
    store.getState().addImage(artwork);
    const { design, selectedId } = store.getState();
    const el = design.surfaces[0]!.elements[0]!;
    expect(el).toMatchObject({ type: 'image', x: 150, y: 200, width: 240, height: 120 });
    expect(selectedId).toBe(el.id);
  });

  it('adds text to the surface being edited, measured and inside the area', () => {
    const store = createStudioStore([front, back], measure);
    store.getState().setActiveArea('back');
    store.getState().addText({ text: 'Team' });
    const el = store.getState().design.surfaces[1]!.elements[0] as TextElement;
    expect(el.text).toBe('Team');
    expect(el.width).toBeGreaterThan(0);
    expect(isInsideArea(el, back)).toBe(true);
  });

  it('undoes and redoes changes', () => {
    const store = createStudioStore([front], measure);
    store.getState().addImage(artwork);
    const id = store.getState().selectedId!;
    store.getState().updateElement(id, { rotation: 30 });

    store.getState().undo();
    expect(store.getState().design.surfaces[0]!.elements[0]!.rotation).toBe(0);
    store.getState().undo();
    expect(store.getState().design.surfaces[0]!.elements).toHaveLength(0);
    store.getState().redo();
    store.getState().redo();
    expect(store.getState().design.surfaces[0]!.elements[0]!.rotation).toBe(30);
  });

  it('records a whole drag gesture as a single undo step', () => {
    const store = createStudioStore([front], measure);
    store.getState().addImage(artwork);
    const id = store.getState().selectedId!;
    const before = store.getState().past.length;

    store.getState().beginGesture();
    for (let x = 151; x < 170; x++) store.getState().updateElement(id, { x }, { transient: true });
    store.getState().endGesture();

    expect(store.getState().past.length).toBe(before + 1);
    store.getState().undo();
    expect(store.getState().design.surfaces[0]!.elements[0]!.x).toBe(150);
  });

  it('keeps moved elements inside the print area', () => {
    const store = createStudioStore([front], measure);
    store.getState().addImage(artwork);
    const id = store.getState().selectedId!;
    store.getState().updateElement(id, { x: 1000, y: -500 });
    const el = store.getState().design.surfaces[0]!.elements[0]!;
    expect(isInsideArea(el, front)).toBe(true);
  });

  it('duplicates, reorders and deletes layers', () => {
    const store = createStudioStore([front], measure);
    store.getState().addImage(artwork);
    store.getState().addText({ text: 'Top' });
    const textId = store.getState().selectedId!;
    store.getState().moveLayer(textId, 'bottom');
    expect(store.getState().design.surfaces[0]!.elements[0]!.id).toBe(textId);

    store.getState().duplicateElement(textId);
    expect(store.getState().design.surfaces[0]!.elements).toHaveLength(3);

    store.getState().removeElement(textId);
    expect(store.getState().design.surfaces[0]!.elements.some((e) => e.id === textId)).toBe(false);
    expect(store.getState().selectedId).toBeNull();
  });

  it('replaces an image while keeping its width and adopting the new proportions', () => {
    const store = createStudioStore([front], measure);
    store.getState().addImage(artwork);
    const id = store.getState().selectedId!;
    store.getState().replaceImage(id, { ...artwork, uploadId: 'u2', width: 1000, height: 1000 });
    expect(store.getState().design.surfaces[0]!.elements[0]).toMatchObject({ uploadId: 'u2', width: 240, height: 240 });
  });
});
