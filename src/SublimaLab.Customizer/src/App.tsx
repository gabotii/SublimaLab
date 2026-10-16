import { useEffect, useMemo, useState } from 'react';
import { ApiClient, errorMessage } from './api/client';
import type { ProductDetail } from './api/types';
import { Studio, type StudioInitialState } from './components/Studio';
import { type MountOptions, StudioContext, type StudioContextValue } from './context';
import { ImageCache, loadStudioFonts } from './design/assets';
import { type Artwork, fromApiDocument, type ImageElement } from './design/model';
import { createStudioStore } from './design/store';

type BootState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; context: StudioContextValue; initial: StudioInitialState };

/** Loads everything the studio needs (product, design being edited, fonts) before showing it. */
export function App({ options }: { options: MountOptions }) {
  const api = useMemo(() => new ApiClient(), []);
  const [state, setState] = useState<BootState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;
    const images = new ImageCache();

    (async () => {
      try {
        const product = await api.getProduct(options.productSlug);
        const customization = product.customization;
        if (!product.isCustomizable || !customization) {
          throw new Error('This product cannot be customized.');
        }

        const fonts = loadStudioFonts(customization.fonts);
        const store = createStudioStore(customization.printAreas);
        const initial: StudioInitialState = { variantId: pickVariant(product, options.variantId), quantity: 1, cartItemId: null, designId: null, designName: null };

        if (options.cartItemId) {
          const cart = await api.getCart();
          const line = cart.items.find((i) => i.id === options.cartItemId && i.design);
          if (line?.design) {
            await fonts;
            store.getState().loadDesign(fromApiDocument(line.design, customization.printAreas));
            Object.assign(initial, { cartItemId: line.id, quantity: line.quantity, variantId: line.variant?.id ?? initial.variantId });
          }
        } else if (options.designId) {
          const saved = await api.getDesign(options.designId);
          await fonts;
          store.getState().loadDesign(fromApiDocument(saved.design, customization.printAreas));
          Object.assign(initial, { designId: saved.id, designName: saved.name });
        }

        await Promise.all([fonts, restoreArtworks(api, store.getState().design.surfaces.flatMap((s) => s.elements), store, images)]);

        if (!cancelled) {
          setState({ status: 'ready', context: { store, api, product, images, options }, initial });
        }
      } catch (error) {
        if (!cancelled) {
          setState({ status: 'error', message: error instanceof Error && !(error as { code?: string }).code ? error.message : errorMessage(error) });
        }
      }
    })();

    return () => {
      cancelled = true;
      images.clear();
    };
  }, [api, options]);

  if (state.status === 'loading') {
    return (
      <div className="sls-boot" role="status">
        <span className="sls-spinner" aria-hidden="true" />
        <p>Opening the design studio…</p>
      </div>
    );
  }

  if (state.status === 'error') {
    return (
      <div className="sls-boot sls-boot--error" role="alert">
        <p><strong>The design studio couldn't open.</strong></p>
        <p>{state.message}</p>
      </div>
    );
  }

  return (
    <StudioContext.Provider value={state.context}>
      <Studio initial={state.initial} />
    </StudioContext.Provider>
  );
}

function pickVariant(product: ProductDetail, requested?: string | null): string {
  return product.variants.find((v) => v.id === requested)?.id ?? product.variants.find((v) => v.size === 'M')?.id ?? product.variants[0]!.id;
}

/** Reloads metadata for artwork referenced by a design that is being edited. */
async function restoreArtworks(api: ApiClient, elements: { type: string }[], store: ReturnType<typeof createStudioStore>, images: ImageCache) {
  const uploadIds = [...new Set(elements.filter((e): e is ImageElement => e.type === 'image').map((e) => e.uploadId))];
  await Promise.all(
    uploadIds.map(async (id) => {
      try {
        const upload = await api.getUpload(id);
        const artwork: Artwork = { uploadId: upload.id, url: upload.url, width: upload.width, height: upload.height, fileName: upload.fileName };
        store.getState().registerArtwork(artwork);
        images.ensure(upload.id, upload.url);
      } catch {
        // The image is no longer accessible; server validation will flag it when saving.
      }
    }),
  );
}
