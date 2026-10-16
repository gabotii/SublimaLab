import { useState } from 'react';
import { useStudio } from '../../context';

export function TextTool() {
  const { store, product } = useStudio();
  const fonts = product.customization!.fonts;
  const [text, setText] = useState('');
  const [font, setFont] = useState(fonts.includes('Poppins') ? 'Poppins' : fonts[0] ?? 'Inter');

  const add = () => {
    store.getState().addText({ text: text.trim() || 'Your text', fontFamily: font });
    setText('');
  };

  return (
    <div className="sls-stack">
      <label className="sls-field">
        <span className="sls-label">Text</span>
        <textarea className="sls-input" rows={2} maxLength={120} value={text} onChange={(e) => setText(e.target.value)} placeholder="Type your text" />
      </label>
      <fieldset className="sls-fieldset">
        <legend>Font</legend>
        <div className="sls-font-grid">
          {fonts.map((f) => (
            <button key={f} type="button" className="sls-font-option" style={{ fontFamily: `"${f}", sans-serif` }} aria-pressed={f === font} onClick={() => setFont(f)}>
              {f}
            </button>
          ))}
        </div>
      </fieldset>
      <button type="button" className="sls-btn sls-btn--primary sls-btn--block" onClick={add}>
        Add text
      </button>
      <p className="sls-muted">Select text on the design to change its color, size, style and rotation.</p>
    </div>
  );
}
