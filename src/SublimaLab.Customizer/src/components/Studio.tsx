import { useCallback, useEffect, useRef, useState } from 'react';
import { useStudio, useStudioState } from '../context';
import { ActionBar } from './ActionBar';
import { ProductOptions } from './ProductOptions';
import { PropertiesPanel } from './PropertiesPanel';
import { Stage, type StageHandle } from './Stage';
import { ToolsPanel } from './ToolsPanel';

export interface StudioInitialState {
  variantId: string;
  quantity: number;
  cartItemId: string | null;
  designId: string | null;
  designName: string | null;
}

export function Studio({ initial }: { initial: StudioInitialState }) {
  const { store, product } = useStudio();
  const [variantId, setVariantId] = useState(initial.variantId);
  const [quantity, setQuantity] = useState(initial.quantity);
  const stageRef = useRef<StageHandle>(null);
  const canUndo = useStudioState((s) => s.past.length > 0);
  const canRedo = useStudioState((s) => s.future.length > 0);
  const variant = product.variants.find((v) => v.id === variantId) ?? product.variants[0]!;

  const onKeyDown = useCallback(
    (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null;
      const typing = target && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.tagName === 'SELECT' || target.isContentEditable);
      if (typing) return;

      const state = store.getState();
      const mod = e.ctrlKey || e.metaKey;
      if (mod && e.key.toLowerCase() === 'z') {
        e.preventDefault();
        if (e.shiftKey) {
          state.redo();
        } else {
          state.undo();
        }
      } else if (mod && e.key.toLowerCase() === 'y') {
        e.preventDefault();
        state.redo();
      } else if (state.selectedId && mod && e.key.toLowerCase() === 'd') {
        e.preventDefault();
        state.duplicateElement(state.selectedId);
      } else if (state.selectedId && (e.key === 'Delete' || e.key === 'Backspace')) {
        e.preventDefault();
        state.removeElement(state.selectedId);
      } else if (state.selectedId && e.key.startsWith('Arrow')) {
        e.preventDefault();
        const step = e.shiftKey ? 10 : 1;
        const element = state.design.surfaces.flatMap((s) => s.elements).find((el) => el.id === state.selectedId);
        if (!element) return;
        const dx = e.key === 'ArrowLeft' ? -step : e.key === 'ArrowRight' ? step : 0;
        const dy = e.key === 'ArrowUp' ? -step : e.key === 'ArrowDown' ? step : 0;
        state.updateElement(element.id, { x: element.x + dx, y: element.y + dy });
      } else if (e.key === 'Escape') {
        state.select(null);
      }
    },
    [store],
  );

  useEffect(() => {
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [onKeyDown]);

  return (
    <div className="sls">
      <header className="sls-top">
        <div>
          <p className="sls-eyebrow">Design studio</p>
          <h2 className="sls-title">{product.name}</h2>
        </div>
        <div className="sls-history" role="group" aria-label="History">
          <button type="button" className="sls-icon-btn" onClick={() => store.getState().undo()} disabled={!canUndo} aria-label="Undo" title="Undo (Ctrl+Z)">
            <UndoIcon />
          </button>
          <button type="button" className="sls-icon-btn" onClick={() => store.getState().redo()} disabled={!canRedo} aria-label="Redo" title="Redo (Ctrl+Shift+Z)">
            <UndoIcon flip />
          </button>
        </div>
      </header>

      <div className="sls-grid">
        <section className="sls-stage-area" aria-label="Preview">
          <Stage ref={stageRef} color={variant.colorHex} />
        </section>
        <section className="sls-options-area sls-panel" aria-labelledby="sls-options-title">
          <ProductOptions variantId={variantId} onVariantChange={setVariantId} quantity={quantity} onQuantityChange={setQuantity} />
        </section>
        <section className="sls-tools-area sls-panel" aria-label="Design tools">
          <ToolsPanel />
        </section>
        <section className="sls-props-area sls-panel" aria-labelledby="sls-props-title">
          <PropertiesPanel />
        </section>
      </div>

      <ActionBar variant={variant} quantity={quantity} initial={initial} snapshot={() => stageRef.current?.snapshot() ?? Promise.resolve(null)} />
    </div>
  );
}

function UndoIcon({ flip = false }: { flip?: boolean }) {
  return (
    <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" style={flip ? { transform: 'scaleX(-1)' } : undefined}>
      <path d="M9 14 4 9l5-5" />
      <path d="M4 9h10.5a5.5 5.5 0 0 1 0 11H11" />
    </svg>
  );
}
