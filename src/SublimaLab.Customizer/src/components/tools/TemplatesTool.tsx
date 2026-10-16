import { useStudio } from '../../context';
import type { TextElement } from '../../design/model';

/** `scale` is relative to a base size of a quarter of the print-area width. */
type Preset = Partial<TextElement> & { yShare: number; scale?: number };

interface Template {
  name: string;
  description: string;
  items: Preset[];
}

/** Ready-made text layouts. Positions are relative to the print area so they suit every product. */
export const TEMPLATES: Template[] = [
  {
    name: 'Team jersey',
    description: 'Player name and a big number',
    items: [
      { text: 'SUBLIMA', fontFamily: 'Bebas Neue', bold: false, color: '#1F2330', letterSpacing: 0.08, yShare: 0.2, scale: 1 },
      { text: '10', fontFamily: 'Bebas Neue', bold: false, color: '#7C3AED', yShare: 0.55, scale: 3.2 },
    ],
  },
  {
    name: 'Birthday',
    description: 'A fun celebration message',
    items: [
      { text: 'Happy Birthday', fontFamily: 'Pacifico', bold: false, color: '#EC4899', yShare: 0.32, scale: 0.75 },
      { text: 'ALEX · 30', fontFamily: 'Montserrat', bold: true, color: '#1F2330', letterSpacing: 0.15, yShare: 0.5, scale: 0.55 },
    ],
  },
  {
    name: 'Quote',
    description: 'An elegant two-line quote',
    items: [
      { text: 'Make it\nyours.', fontFamily: 'Playfair Display', bold: true, italic: true, color: '#1F2330', yShare: 0.45, scale: 1.1 },
    ],
  },
  {
    name: 'Brand mark',
    description: 'Logo-style lettering',
    items: [
      { text: 'STUDIO', fontFamily: 'Montserrat', bold: true, color: '#0891B2', letterSpacing: 0.3, yShare: 0.38, scale: 0.8 },
      { text: 'est. 2026', fontFamily: 'Roboto Mono', bold: false, color: '#565C70', yShare: 0.5, scale: 0.35 },
    ],
  },
];

export function TemplatesTool() {
  const { store, product } = useStudio();
  const fonts = product.customization!.fonts;

  const apply = (template: Template) => {
    const state = store.getState();
    const area = state.areas.find((a) => a.key === state.activeArea)!;
    for (const item of template.items) {
      const { yShare, scale = 1, ...preset } = item;
      const fontFamily = preset.fontFamily && fonts.includes(preset.fontFamily) ? preset.fontFamily : fonts[0];
      // Font size in points: a quarter of the area width (mm → pt) times the preset scale.
      const fontSize = Math.max(4, Math.min(400, (area.widthMm / 4 / 0.3528) * 0.35 * scale));
      state.addText({ ...preset, fontFamily, fontSize, x: area.widthMm / 2, y: area.heightMm * yShare });
    }
  };

  return (
    <ul className="sls-template-list">
      {TEMPLATES.map((t) => (
        <li key={t.name}>
          <button type="button" className="sls-template" onClick={() => apply(t)}>
            <strong>{t.name}</strong>
            <span>{t.description}</span>
          </button>
        </li>
      ))}
    </ul>
  );
}
