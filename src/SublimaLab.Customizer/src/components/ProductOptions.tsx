import { useMemo } from 'react';
import { useStudio } from '../context';
import { formatMoney } from '../format';

interface Props {
  variantId: string;
  onVariantChange(id: string): void;
  quantity: number;
  onQuantityChange(quantity: number): void;
}

export function ProductOptions({ variantId, onVariantChange, quantity, onQuantityChange }: Props) {
  const { product } = useStudio();
  const selected = product.variants.find((v) => v.id === variantId) ?? product.variants[0]!;

  const colors = useMemo(() => {
    const seen = new Map<string, string>();
    product.variants.forEach((v) => seen.set(v.colorName, v.colorHex));
    return [...seen.entries()].map(([name, hex]) => ({ name, hex }));
  }, [product.variants]);

  const sizes = product.variants.filter((v) => v.colorName === selected.colorName);

  const chooseColor = (name: string) => {
    const match = product.variants.find((v) => v.colorName === name && v.size === selected.size) ?? product.variants.find((v) => v.colorName === name);
    if (match) onVariantChange(match.id);
  };

  return (
    <>
      <h2 id="sls-options-title" className="sls-panel-title">Product options</h2>
      {colors.length > 1 && (
        <fieldset className="sls-fieldset">
          <legend>
            Color: <span>{selected.colorName}</span>
          </legend>
          <div className="sls-swatches">
            {colors.map((c) => (
              <button
                key={c.name}
                type="button"
                className="sls-swatch"
                style={{ background: c.hex }}
                aria-pressed={c.name === selected.colorName}
                aria-label={c.name}
                title={c.name}
                onClick={() => chooseColor(c.name)}
              />
            ))}
          </div>
        </fieldset>
      )}
      {sizes.length > 1 && (
        <fieldset className="sls-fieldset">
          <legend>
            {product.categorySlug === 't-shirts' ? 'Size' : 'Option'}: <span>{selected.size}</span>
          </legend>
          <div className="sls-chips">
            {sizes.map((v) => (
              <button key={v.id} type="button" className="sls-chip" aria-pressed={v.id === selected.id} onClick={() => onVariantChange(v.id)}>
                {v.size}
              </button>
            ))}
          </div>
        </fieldset>
      )}
      <div className="sls-row">
        <span className="sls-label" id="sls-qty-label">Quantity</span>
        <div className="sls-stepper" role="group" aria-labelledby="sls-qty-label">
          <button type="button" onClick={() => onQuantityChange(Math.max(1, quantity - 1))} disabled={quantity <= 1} aria-label="Decrease quantity">−</button>
          <output aria-live="polite">{quantity}</output>
          <button type="button" onClick={() => onQuantityChange(Math.min(100, quantity + 1))} disabled={quantity >= 100} aria-label="Increase quantity">+</button>
        </div>
      </div>
      <p className="sls-muted">
        {formatMoney(selected.price, product.currency)} each{selected.size ? ` · ${selected.colorName}, ${selected.size}` : ''}
      </p>
    </>
  );
}
