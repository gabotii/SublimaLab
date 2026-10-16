import type { ImageSource } from './render';

/** Loads uploaded images once and shares them between the editor, the 3D textures and previews. */
export class ImageCache {
  private readonly images = new Map<string, HTMLImageElement>();
  private readonly listeners = new Set<() => void>();

  get map(): ReadonlyMap<string, ImageSource> {
    return this.images;
  }

  ensure(uploadId: string, url: string): void {
    if (this.images.has(uploadId)) return;
    const image = new Image();
    image.decoding = 'async';
    image.onload = () => this.listeners.forEach((l) => l());
    image.src = url;
    this.images.set(uploadId, image);
  }

  subscribe(listener: () => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  clear(): void {
    this.images.clear();
    this.listeners.clear();
  }
}

const FONT_FAMILIES: Record<string, string> = {
  Inter: 'Inter:ital,wght@0,400;0,700;1,400;1,700',
  Poppins: 'Poppins:ital,wght@0,400;0,700;1,400;1,700',
  Montserrat: 'Montserrat:ital,wght@0,400;0,700;1,400;1,700',
  'Playfair Display': 'Playfair+Display:ital,wght@0,400;0,700;1,400;1,700',
  'Roboto Mono': 'Roboto+Mono:ital,wght@0,400;0,700;1,400;1,700',
  'Bebas Neue': 'Bebas+Neue',
  Pacifico: 'Pacifico',
  'Permanent Marker': 'Permanent+Marker',
};

/** Adds the stylesheet for the studio fonts (allowed by the storefront CSP) and resolves when they are ready. */
export async function loadStudioFonts(fonts: readonly string[]): Promise<void> {
  const families = fonts.map((f) => FONT_FAMILIES[f]).filter((f): f is string => Boolean(f));
  if (families.length > 0 && !document.querySelector('link[data-sl-fonts]')) {
    const link = document.createElement('link');
    link.rel = 'stylesheet';
    link.dataset.slFonts = '';
    link.href = `https://fonts.googleapis.com/css2?${families.map((f) => `family=${f}`).join('&')}&display=swap`;
    document.head.append(link);
    await new Promise<void>((resolve) => {
      link.onload = () => resolve();
      link.onerror = () => resolve();
    });
  }

  await Promise.allSettled(fonts.flatMap((f) => [document.fonts.load(`400 24px "${f}"`), document.fonts.load(`700 24px "${f}"`)]));
}
