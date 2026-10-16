import type { TextElement } from './model';

export const PT_TO_MM = 25.4 / 72;
export const LINE_HEIGHT = 1.2;
const PADDING_MM = 1;

export type TextStyle = Pick<TextElement, 'text' | 'fontFamily' | 'fontSize' | 'bold' | 'italic' | 'letterSpacing'>;

/** Measures the advance width of a single line at a given pixel font size. */
export type LineMeasurer = (line: string, font: string) => number;

export const cssFont = (style: Pick<TextStyle, 'fontFamily' | 'bold' | 'italic'>, px: number): string =>
  `${style.italic ? 'italic ' : ''}${style.bold ? '700' : '400'} ${px}px "${style.fontFamily}", sans-serif`;

let sharedContext: CanvasRenderingContext2D | null = null;

export const canvasMeasurer: LineMeasurer = (line, font) => {
  sharedContext ??= document.createElement('canvas').getContext('2d');
  if (!sharedContext) return line.length * 0.6 * parseFloat(font.match(/(\d+(?:\.\d+)?)px/)?.[1] ?? '10');
  sharedContext.font = font;
  return sharedContext.measureText(line).width;
};

/** The text box in millimetres for a style: widest line × number of lines, plus a little padding. */
export function measureTextBox(style: TextStyle, measure: LineMeasurer = canvasMeasurer): { width: number; height: number } {
  // Measure at a fixed reference size, then scale: text metrics are linear in font size.
  const referencePx = 100;
  const font = cssFont(style, referencePx);
  const lines = style.text.split('\n');
  const mmPerPx = (style.fontSize * PT_TO_MM) / referencePx;

  const widest = Math.max(
    ...lines.map((line) => measure(line, font) + style.letterSpacing * referencePx * Math.max(line.length - 1, 0)),
    referencePx * 0.3,
  );

  return {
    width: widest * mmPerPx + PADDING_MM * 2,
    height: lines.length * referencePx * LINE_HEIGHT * mmPerPx + PADDING_MM,
  };
}

/** Font size (pt) that makes the text box approximately the requested width. */
export function fontSizeForWidth(style: TextStyle, targetWidthMm: number, measure: LineMeasurer = canvasMeasurer): number {
  const current = measureTextBox(style, measure).width;
  return style.fontSize * (targetWidthMm / current);
}
