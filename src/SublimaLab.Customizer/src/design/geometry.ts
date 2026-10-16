import type { Artwork, DesignElement, ImageElement, PrintArea } from './model';

/** Same tolerance the server uses for placement checks (absorbs floating point rounding). */
export const BOUNDS_TOLERANCE_MM = 0.5;

export interface Bounds {
  left: number;
  top: number;
  right: number;
  bottom: number;
}

/** Axis-aligned bounding box of a rotated element (identical to the server's calculation). */
export function rotatedBounds(el: Pick<DesignElement, 'x' | 'y' | 'width' | 'height' | 'rotation'>): Bounds {
  const radians = (el.rotation * Math.PI) / 180;
  const cos = Math.abs(Math.cos(radians));
  const sin = Math.abs(Math.sin(radians));
  const halfW = (el.width * cos + el.height * sin) / 2;
  const halfH = (el.width * sin + el.height * cos) / 2;
  return { left: el.x - halfW, top: el.y - halfH, right: el.x + halfW, bottom: el.y + halfH };
}

export function isInsideArea(el: DesignElement, area: PrintArea, tolerance = BOUNDS_TOLERANCE_MM): boolean {
  const b = rotatedBounds(el);
  return b.left >= -tolerance && b.top >= -tolerance && b.right <= area.widthMm + tolerance && b.bottom <= area.heightMm + tolerance;
}

/**
 * Moves an element back inside the print area. If the element is larger than the area
 * (after rotation) it is centred instead, and {@link isInsideArea} keeps reporting the problem.
 */
export function clampToArea<T extends DesignElement>(el: T, area: PrintArea): T {
  const b = rotatedBounds(el);
  const halfW = (b.right - b.left) / 2;
  const halfH = (b.bottom - b.top) / 2;
  const x = halfW * 2 > area.widthMm ? area.widthMm / 2 : Math.min(Math.max(el.x, halfW), area.widthMm - halfW);
  const y = halfH * 2 > area.heightMm ? area.heightMm / 2 : Math.min(Math.max(el.y, halfH), area.heightMm - halfH);
  return x === el.x && y === el.y ? el : { ...el, x, y };
}

/** Size for newly added artwork: fits within a share of the area, keeping the image's aspect ratio. */
export function fitArtwork(artwork: Pick<Artwork, 'width' | 'height'>, area: PrintArea, share = 0.8): { width: number; height: number } {
  const aspect = artwork.width / artwork.height;
  let width = area.widthMm * share;
  let height = width / aspect;
  if (height > area.heightMm * share) {
    height = area.heightMm * share;
    width = height * aspect;
  }
  return { width, height };
}

/** Largest size an element can scale to (keeping proportions) while its rotated box fits the area. */
export function maxScaleWithin(el: DesignElement, area: PrintArea): number {
  const b = rotatedBounds({ ...el, x: 0, y: 0 });
  return Math.min(area.widthMm / (b.right - b.left), area.heightMm / (b.bottom - b.top));
}

/** Effective print resolution of an image at its placed physical size. */
export function effectiveDpi(artwork: Pick<Artwork, 'width' | 'height'>, el: Pick<ImageElement, 'width' | 'height'>): number {
  const horizontal = artwork.width / (el.width / 25.4);
  const vertical = artwork.height / (el.height / 25.4);
  return Math.floor(Math.min(horizontal, vertical));
}

export const normalizeRotation = (degrees: number): number => {
  let r = degrees % 360;
  if (r > 180) r -= 360;
  if (r < -180) r += 360;
  return Math.round(r * 10) / 10;
};
