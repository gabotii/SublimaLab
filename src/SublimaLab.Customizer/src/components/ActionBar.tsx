import { useState } from 'react';
import { errorMessage } from '../api/client';
import type { Variant } from '../api/types';
import { useStudio, useStudioState } from '../context';
import { effectiveDpi, isInsideArea } from '../design/geometry';
import { allElements, isEmptyDesign, toApiDocument, usedAreaCount } from '../design/model';
import { formatMoney } from '../format';
import type { StudioInitialState } from './Studio';

interface Props {
  variant: Variant;
  quantity: number;
  initial: StudioInitialState;
  snapshot(): Promise<Blob | null>;
}

export function ActionBar({ variant, quantity, initial, snapshot }: Props) {
  const { api, product, store, options } = useStudio();
  const customization = product.customization!;
  const design = useStudioState((s) => s.design);
  const artworks = useStudioState((s) => s.artworks);
  const [busy, setBusy] = useState<'cart' | 'save' | null>(null);
  const [message, setMessage] = useState<{ kind: 'error' | 'success'; text: string } | null>(null);
  const [designId, setDesignId] = useState(initial.designId);
  const [naming, setNaming] = useState(false);
  const [designName, setDesignName] = useState(initial.designName ?? `My ${product.name}`);

  // Estimate only: the server recalculates the real price when the item is added.
  const areasUsed = usedAreaCount(design);
  const designFee = areasUsed === 0 ? 0 : customization.baseFee + customization.additionalPrintAreaFee * Math.max(areasUsed - 1, 0);
  const total = (variant.price + designFee) * quantity;

  const outOfBounds = design.surfaces.some((s) => {
    const area = customization.printAreas.find((a) => a.key === s.printArea);
    return area ? s.elements.some((e) => !isInsideArea(e, area)) : false;
  });
  const lowDpiCount = allElements(design).filter((e) => {
    const artwork = e.type === 'image' ? artworks[e.uploadId] : undefined;
    return artwork && e.type === 'image' && effectiveDpi(artwork, e) < customization.minRecommendedDpi;
  }).length;
  const empty = isEmptyDesign(design);

  const uploadPreview = async (): Promise<string | null> => {
    try {
      const blob = await snapshot();
      if (!blob) return null;
      const preview = await api.upload(blob, 'design-preview.png', 'Preview');
      return preview.id;
    } catch {
      return null; // A missing thumbnail must never block the purchase.
    }
  };

  const addToCart = async () => {
    setBusy('cart');
    setMessage(null);
    try {
      const previewUploadId = await uploadPreview();
      const body = { variantId: variant.id, quantity, design: toApiDocument(design), previewUploadId };
      if (initial.cartItemId) {
        await api.updateCartItem(initial.cartItemId, body);
        options.onCartUpdated('Your design was updated in the cart.');
      } else {
        await api.addToCart({ productId: product.id, ...body });
        options.onCartUpdated(`${product.name} with your design was added to the cart.`);
      }
    } catch (e) {
      setMessage({ kind: 'error', text: errorMessage(e) });
    } finally {
      setBusy(null);
    }
  };

  const saveDesign = async () => {
    if (!options.isAuthenticated) {
      options.onRequireLogin(window.location.pathname + window.location.search);
      return;
    }

    setBusy('save');
    setMessage(null);
    try {
      const previewUploadId = await uploadPreview();
      const saved = await api.saveDesign({ productId: product.id, name: designName.trim() || `My ${product.name}`, design: toApiDocument(design), previewUploadId }, designId);
      setDesignId(saved.id);
      setNaming(false);
      setMessage({ kind: 'success', text: `Saved "${saved.name}" to your account.` });
      store.getState().select(null);
    } catch (e) {
      setMessage({ kind: 'error', text: errorMessage(e) });
    } finally {
      setBusy(null);
    }
  };

  return (
    <footer className="sls-actions">
      <div className="sls-actions-summary">
        <span className="sls-price">{formatMoney(total, product.currency)}</span>
        <span className="sls-muted">
          {quantity} × ({formatMoney(variant.price, product.currency)}{designFee > 0 ? ` + ${formatMoney(designFee, product.currency)} printing` : ''}) · estimate
        </span>
      </div>

      <div className="sls-actions-status" aria-live="polite">
        {message && <p className={`sls-alert sls-alert--${message.kind}`} role={message.kind === 'error' ? 'alert' : 'status'}>{message.text}</p>}
        {!message && outOfBounds && <p className="sls-alert sls-alert--error">Move everything inside the printable area to continue.</p>}
        {!message && !outOfBounds && lowDpiCount > 0 && <p className="sls-alert sls-alert--warning">{lowDpiCount === 1 ? 'One image' : `${lowDpiCount} images`} may print blurry.</p>}
        {!message && !outOfBounds && empty && <p className="sls-muted">Add artwork or text to personalize your {product.name.toLowerCase()}.</p>}
      </div>

      <div className="sls-actions-buttons">
        {naming ? (
          <form
            className="sls-save-form"
            onSubmit={(e) => {
              e.preventDefault();
              void saveDesign();
            }}
          >
            <label className="sls-visually-hidden" htmlFor="sls-design-name">Design name</label>
            <input id="sls-design-name" className="sls-input" value={designName} maxLength={80} onChange={(e) => setDesignName(e.target.value)} autoFocus />
            <button type="submit" className="sls-btn sls-btn--secondary" disabled={busy !== null}>{busy === 'save' ? 'Saving…' : 'Save'}</button>
            <button type="button" className="sls-btn sls-btn--ghost" onClick={() => setNaming(false)}>Cancel</button>
          </form>
        ) : (
          <button
            type="button"
            className="sls-btn sls-btn--secondary"
            onClick={() => (options.isAuthenticated ? setNaming(true) : options.onRequireLogin(window.location.pathname + window.location.search))}
            disabled={busy !== null || empty || outOfBounds}
          >
            {designId ? 'Save changes' : 'Save design'}
          </button>
        )}
        <button type="button" className="sls-btn sls-btn--primary sls-btn--lg" onClick={() => void addToCart()} disabled={busy !== null || empty || outOfBounds}>
          {busy === 'cart' ? 'Adding…' : initial.cartItemId ? 'Update cart item' : 'Add to cart'}
        </button>
      </div>
    </footer>
  );
}
