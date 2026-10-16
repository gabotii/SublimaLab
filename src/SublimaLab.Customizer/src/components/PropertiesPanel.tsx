import { useStudio, useStudioState } from '../context';
import { effectiveDpi, isInsideArea } from '../design/geometry';
import type { DesignElement, TextAlign, TextElement } from '../design/model';

export function PropertiesPanel() {
  const { store, product } = useStudio();
  const selected = useStudioState((s) => s.design.surfaces.flatMap((x) => x.elements).find((e) => e.id === s.selectedId) ?? null);
  const areaKey = useStudioState((s) => s.design.surfaces.find((x) => x.elements.some((e) => e.id === s.selectedId))?.printArea ?? s.activeArea);
  const artworks = useStudioState((s) => s.artworks);
  const customization = product.customization!;
  const area = customization.printAreas.find((a) => a.key === areaKey) ?? customization.printAreas[0]!;

  if (!selected) {
    return (
      <>
        <h2 id="sls-props-title" className="sls-panel-title">Properties</h2>
        <p className="sls-muted">Select an element on the design to adjust its position, size, rotation and style.</p>
        <ul className="sls-tips">
          <li>Drag to move · corner handle to resize · top handle to rotate</li>
          <li>Arrow keys nudge by 1 mm (Shift: 10 mm)</li>
          <li>Ctrl+Z undo · Ctrl+Shift+Z redo · Ctrl+D duplicate</li>
        </ul>
      </>
    );
  }

  const actions = store.getState();
  const update = (patch: Partial<DesignElement>) => actions.updateElement(selected.id, patch);
  const artwork = selected.type === 'image' ? artworks[selected.uploadId] : undefined;
  const dpi = artwork && selected.type === 'image' ? effectiveDpi(artwork, selected) : null;
  const lowDpi = dpi !== null && dpi < customization.minRecommendedDpi;

  return (
    <>
      <h2 id="sls-props-title" className="sls-panel-title">{selected.type === 'text' ? 'Text' : 'Image'} properties</h2>

      {!isInsideArea(selected, area) && (
        <p className="sls-alert sls-alert--error" role="alert">This element is outside the printable area. Make it smaller or reduce the rotation.</p>
      )}

      {selected.type === 'text' && <TextProperties element={selected} fonts={customization.fonts} onChange={update} />}

      {selected.type === 'image' && dpi !== null && (
        <div className={`sls-quality ${lowDpi ? 'sls-quality--low' : 'sls-quality--good'}`} role="status">
          <strong>{lowDpi ? 'Low print quality' : 'Great print quality'}</strong>
          <span>
            {dpi} DPI at this size{lowDpi ? ` (we recommend ${customization.minRecommendedDpi}+). Your image may appear blurry when printed. For best results, use a high-resolution image or make it smaller.` : '.'}
          </span>
        </div>
      )}

      <div className="sls-grid-2">
        <NumberField label="X (mm)" value={selected.x} onChange={(x) => update({ x })} />
        <NumberField label="Y (mm)" value={selected.y} onChange={(y) => update({ y })} />
        {selected.type === 'image' ? (
          <NumberField
            label="Width (mm)"
            value={selected.width}
            min={5}
            onChange={(width) => update({ width, height: width * (selected.height / selected.width) })}
          />
        ) : (
          <NumberField label="Size (pt)" value={selected.fontSize} min={4} max={400} onChange={(fontSize) => update({ fontSize })} />
        )}
        <NumberField label="Rotation (°)" value={selected.rotation} min={-180} max={180} onChange={(rotation) => update({ rotation })} />
      </div>

      <label className="sls-field">
        <span className="sls-label">Rotation</span>
        <input type="range" min={-180} max={180} step={1} value={selected.rotation} onChange={(e) => update({ rotation: Number(e.target.value) })} />
      </label>

      <div className="sls-button-row" role="group" aria-label="Arrange">
        <button type="button" className="sls-btn sls-btn--secondary" onClick={() => actions.moveLayer(selected.id, 'top')}>Bring to front</button>
        <button type="button" className="sls-btn sls-btn--secondary" onClick={() => actions.moveLayer(selected.id, 'bottom')}>Send to back</button>
        <button type="button" className="sls-btn sls-btn--secondary" onClick={() => update({ x: area.widthMm / 2 })}>Center</button>
      </div>
      <div className="sls-button-row">
        <button type="button" className="sls-btn sls-btn--secondary" onClick={() => actions.duplicateElement(selected.id)}>Duplicate</button>
        <button type="button" className="sls-btn sls-btn--danger" onClick={() => actions.removeElement(selected.id)}>Delete</button>
      </div>
    </>
  );
}

function TextProperties({ element, fonts, onChange }: { element: TextElement; fonts: string[]; onChange(patch: Partial<TextElement>): void }) {
  return (
    <div className="sls-stack">
      <label className="sls-field">
        <span className="sls-label">Text</span>
        <textarea className="sls-input" rows={2} maxLength={120} value={element.text} onChange={(e) => onChange({ text: e.target.value || ' ' })} />
      </label>
      <label className="sls-field">
        <span className="sls-label">Font</span>
        <select className="sls-input" value={element.fontFamily} onChange={(e) => onChange({ fontFamily: e.target.value })}>
          {fonts.map((f) => (
            <option key={f} value={f}>{f}</option>
          ))}
        </select>
      </label>
      <div className="sls-row">
        <label className="sls-color">
          <input type="color" value={element.color} onChange={(e) => onChange({ color: e.target.value.toUpperCase() })} aria-label="Text color" />
          <span>{element.color}</span>
        </label>
        <div className="sls-toggle-group" role="group" aria-label="Style">
          <button type="button" aria-pressed={element.bold} onClick={() => onChange({ bold: !element.bold })} aria-label="Bold" style={{ fontWeight: 800 }}>B</button>
          <button type="button" aria-pressed={element.italic} onClick={() => onChange({ italic: !element.italic })} aria-label="Italic" style={{ fontStyle: 'italic' }}>I</button>
        </div>
      </div>
      <div className="sls-toggle-group" role="group" aria-label="Alignment">
        {(['Left', 'Center', 'Right'] as TextAlign[]).map((align) => (
          <button key={align} type="button" aria-pressed={element.align === align} onClick={() => onChange({ align })}>{align}</button>
        ))}
      </div>
      <label className="sls-field">
        <span className="sls-label">Letter spacing</span>
        <input type="range" min={-0.2} max={1} step={0.01} value={element.letterSpacing} onChange={(e) => onChange({ letterSpacing: Number(e.target.value) })} />
      </label>
    </div>
  );
}

function NumberField({ label, value, onChange, min, max }: { label: string; value: number; onChange(value: number): void; min?: number; max?: number }) {
  return (
    <label className="sls-field">
      <span className="sls-label">{label}</span>
      <input
        className="sls-input"
        type="number"
        inputMode="decimal"
        step={1}
        min={min}
        max={max}
        value={Math.round(value * 10) / 10}
        onChange={(e) => {
          const next = Number(e.target.value);
          if (Number.isFinite(next)) onChange(min !== undefined || max !== undefined ? Math.min(max ?? Infinity, Math.max(min ?? -Infinity, next)) : next);
        }}
      />
    </label>
  );
}
