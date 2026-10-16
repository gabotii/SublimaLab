import { createContext, useContext } from 'react';
import { useStore } from 'zustand';
import type { ApiClient } from './api/client';
import type { ProductDetail } from './api/types';
import type { ImageCache } from './design/assets';
import type { StudioState, StudioStore } from './design/store';

/** Options passed by the host page (the Blazor storefront) when mounting the studio. */
export interface MountOptions {
  productSlug: string;
  variantId?: string | null;
  /** Edit the customization of an existing cart line. */
  cartItemId?: string | null;
  /** Open a design saved to the customer's account. */
  designId?: string | null;
  isAuthenticated: boolean;
  onCartUpdated: (message: string) => void;
  onRequireLogin: (returnUrl: string) => void;
}

export interface StudioContextValue {
  store: StudioStore;
  api: ApiClient;
  product: ProductDetail;
  images: ImageCache;
  options: MountOptions;
}

export const StudioContext = createContext<StudioContextValue | null>(null);

export function useStudio(): StudioContextValue {
  const value = useContext(StudioContext);
  if (!value) throw new Error('useStudio must be used inside the studio.');
  return value;
}

/** Subscribes to a slice of the studio state; components re-render only when that slice changes. */
export function useStudioState<T>(selector: (state: StudioState) => T): T {
  return useStore(useStudio().store, selector);
}
