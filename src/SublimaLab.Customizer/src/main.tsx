import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import type { MountOptions } from './context';
import './styles/studio.css';

export type { MountOptions };

export interface StudioHandle {
  unmount(): void;
}

/** Entry point used by the storefront: renders the studio into a host element. */
export function mount(element: HTMLElement, options: MountOptions): StudioHandle {
  const root = createRoot(element);
  root.render(
    <StrictMode>
      <App options={options} />
    </StrictMode>,
  );
  return { unmount: () => root.unmount() };
}
