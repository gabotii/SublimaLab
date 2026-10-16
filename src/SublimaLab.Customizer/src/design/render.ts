import type { DesignElement, PrintArea, Surface, TextElement } from './model';
import { cssFont, LINE_HEIGHT, PT_TO_MM } from './text';

export type ImageSource = CanvasImageSource & { width: number; height: number };

/**
 * Draws one print surface. This single renderer feeds the 2D editor, the 3D texture and the
 * cart preview, so what customers edit is exactly what they see on the product.
 */
export function renderSurface(
  ctx: CanvasRenderingContext2D,
  surface: Surface,
  area: PrintArea,
  pxPerMm: number,
  images: ReadonlyMap<string, ImageSource>,
): void {
  const width = area.widthMm * pxPerMm;
  const height = area.heightMm * pxPerMm;
  ctx.save();
  ctx.clearRect(0, 0, width, height);

  if (area.shape === 'Ellipse') {
    ctx.beginPath();
    ctx.ellipse(width / 2, height / 2, width / 2, height / 2, 0, 0, Math.PI * 2);
    ctx.clip();
  }

  for (const element of surface.elements) {
    ctx.save();
    ctx.translate(element.x * pxPerMm, element.y * pxPerMm);
    ctx.rotate((element.rotation * Math.PI) / 180);
    drawElement(ctx, element, pxPerMm, images);
    ctx.restore();
  }

  ctx.restore();
}

function drawElement(ctx: CanvasRenderingContext2D, element: DesignElement, pxPerMm: number, images: ReadonlyMap<string, ImageSource>) {
  const w = element.width * pxPerMm;
  const h = element.height * pxPerMm;

  if (element.type === 'image') {
    const image = images.get(element.uploadId);
    if (image && image.width > 0) {
      ctx.imageSmoothingQuality = 'high';
      ctx.drawImage(image, -w / 2, -h / 2, w, h);
    } else {
      // Placeholder while the image loads.
      ctx.fillStyle = 'rgba(124, 58, 237, 0.12)';
      ctx.fillRect(-w / 2, -h / 2, w, h);
    }
    return;
  }

  drawText(ctx, element, pxPerMm, w, h);
}

function drawText(ctx: CanvasRenderingContext2D, el: TextElement, pxPerMm: number, w: number, h: number) {
  const px = el.fontSize * PT_TO_MM * pxPerMm;
  const lines = el.text.split('\n');
  const lineHeight = px * LINE_HEIGHT;
  const padding = pxPerMm;

  ctx.font = cssFont(el, px);
  ctx.fillStyle = el.color;
  ctx.textBaseline = 'middle';
  if ('letterSpacing' in ctx) {
    (ctx as CanvasRenderingContext2D & { letterSpacing: string }).letterSpacing = `${el.letterSpacing * px}px`;
  }

  const x = el.align === 'Left' ? -w / 2 + padding : el.align === 'Right' ? w / 2 - padding : 0;
  ctx.textAlign = el.align === 'Left' ? 'left' : el.align === 'Right' ? 'right' : 'center';

  const top = -h / 2 + padding / 2;
  lines.forEach((line, i) => {
    ctx.fillText(line, x, top + lineHeight * (i + 0.5));
  });
}

/** Renders a surface into a new canvas at a given resolution (used for textures and previews). */
export function renderSurfaceToCanvas(surface: Surface, area: PrintArea, pxPerMm: number, images: ReadonlyMap<string, ImageSource>, canvas?: HTMLCanvasElement): HTMLCanvasElement {
  const target = canvas ?? document.createElement('canvas');
  const width = Math.max(1, Math.round(area.widthMm * pxPerMm));
  const height = Math.max(1, Math.round(area.heightMm * pxPerMm));
  if (target.width !== width) target.width = width;
  if (target.height !== height) target.height = height;
  const ctx = target.getContext('2d');
  if (ctx) renderSurface(ctx, surface, area, pxPerMm, images);
  return target;
}
