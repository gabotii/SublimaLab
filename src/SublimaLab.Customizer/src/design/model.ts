// Mirrors the server's design document schema (docs/api.md, "Design document schema (v1)").
// Coordinates are millimetres from the print area's top-left corner; x/y is the element centre.

export type TextAlign = 'Left' | 'Center' | 'Right';

interface ElementBase {
  id: string;
  x: number;
  y: number;
  width: number;
  height: number;
  rotation: number;
}

export interface ImageElement extends ElementBase {
  type: 'image';
  uploadId: string;
}

export interface TextElement extends ElementBase {
  type: 'text';
  text: string;
  fontFamily: string;
  fontSize: number;
  color: string;
  bold: boolean;
  italic: boolean;
  align: TextAlign;
  letterSpacing: number;
}

export type DesignElement = ImageElement | TextElement;

export interface Surface {
  printArea: string;
  elements: DesignElement[];
}

export interface DesignDocument {
  schemaVersion: 1;
  surfaces: Surface[];
}

export interface PrintArea {
  key: string;
  name: string;
  widthMm: number;
  heightMm: number;
  shape: 'Rectangle' | 'Ellipse' | string;
}

/** Metadata about an uploaded image, needed to render it and to compute print resolution. */
export interface Artwork {
  uploadId: string;
  url: string;
  width: number;
  height: number;
  fileName: string;
}

export const emptyDesign = (areas: PrintArea[]): DesignDocument => ({
  schemaVersion: 1,
  surfaces: areas.map((a) => ({ printArea: a.key, elements: [] })),
});

export const surfaceOf = (design: DesignDocument, areaKey: string): Surface =>
  design.surfaces.find((s) => s.printArea === areaKey) ?? { printArea: areaKey, elements: [] };

export const allElements = (design: DesignDocument): DesignElement[] => design.surfaces.flatMap((s) => s.elements);

export const usedAreaCount = (design: DesignDocument): number => design.surfaces.filter((s) => s.elements.length > 0).length;

export const isEmptyDesign = (design: DesignDocument): boolean => allElements(design).length === 0;

const round = (n: number, digits = 2) => Math.round(n * 10 ** digits) / 10 ** digits;

/** The document sent to the API: rounded numbers and no empty surfaces. */
export function toApiDocument(design: DesignDocument): DesignDocument {
  return {
    schemaVersion: 1,
    surfaces: design.surfaces
      .filter((s) => s.elements.length > 0)
      .map((s) => ({
        printArea: s.printArea,
        elements: s.elements.map((e) => ({
          ...e,
          x: round(e.x),
          y: round(e.y),
          width: round(Math.max(e.width, 1)),
          height: round(Math.max(e.height, 1)),
          rotation: round(e.rotation, 1),
          ...(e.type === 'text' ? { fontSize: round(e.fontSize, 1), letterSpacing: round(e.letterSpacing, 2) } : {}),
        })),
      })),
  };
}

/** Restores a document coming from the API, making sure every print area has a surface. */
export function fromApiDocument(document: DesignDocument, areas: PrintArea[]): DesignDocument {
  return {
    schemaVersion: 1,
    surfaces: areas.map((area) => ({
      printArea: area.key,
      elements: document.surfaces.find((s) => s.printArea.toLowerCase() === area.key.toLowerCase())?.elements.map((e) => ({ ...e })) ?? [],
    })),
  };
}

let counter = 0;
export const newId = (prefix: string) => `${prefix}-${Date.now().toString(36)}${(counter++).toString(36)}`;
