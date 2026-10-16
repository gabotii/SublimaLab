import { useStudio, useStudioState } from '../../context';
import type { DesignElement } from '../../design/model';

const EMPTY: DesignElement[] = [];

export function LayersTool() {
  const { store } = useStudio();
  const activeArea = useStudioState((s) => s.activeArea);
  const elements = useStudioState((s) => s.design.surfaces.find((x) => x.printArea === activeArea)?.elements ?? EMPTY);
  const selectedId = useStudioState((s) => s.selectedId);
  const artworks = useStudioState((s) => s.artworks);

  if (elements.length === 0) {
    return <p className="sls-muted">Nothing on this side yet. Upload an image or add text to create layers.</p>;
  }

  // Top layer first, like most design tools.
  const ordered = [...elements].reverse();
  const actions = store.getState();

  return (
    <ol className="sls-layers" aria-label="Layers, top first">
      {ordered.map((el, i) => (
        <li key={el.id} className={el.id === selectedId ? 'sls-layer sls-layer--selected' : 'sls-layer'}>
          <button type="button" className="sls-layer-name" onClick={() => actions.select(el.id)} aria-pressed={el.id === selectedId}>
            <span className="sls-layer-kind" aria-hidden="true">{el.type === 'text' ? 'T' : '▣'}</span>
            <span className="sls-layer-label">{el.type === 'text' ? el.text.replace(/\n/g, ' ') : artworks[el.uploadId]?.fileName ?? 'Image'}</span>
          </button>
          <div className="sls-layer-actions">
            <button type="button" onClick={() => actions.moveLayer(el.id, 'up')} disabled={i === 0} aria-label="Bring forward" title="Bring forward">↑</button>
            <button type="button" onClick={() => actions.moveLayer(el.id, 'down')} disabled={i === ordered.length - 1} aria-label="Send backward" title="Send backward">↓</button>
            <button type="button" onClick={() => actions.duplicateElement(el.id)} aria-label="Duplicate" title="Duplicate">⧉</button>
            <button type="button" onClick={() => actions.removeElement(el.id)} aria-label="Delete" title="Delete">✕</button>
          </div>
        </li>
      ))}
    </ol>
  );
}
