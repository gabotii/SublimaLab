// Loads the React design studio on demand and mounts it into a Blazor-rendered element.
// The storefront never downloads the studio (or three.js) until a customer opens /customize.
const handles = new Map();
let studioModule;

function ensureStylesheet() {
  if (document.querySelector('link[data-sl-customizer]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = new URL('../customizer/customizer.css', import.meta.url).href;
  link.dataset.slCustomizer = '';
  document.head.append(link);
}

export async function mount(elementId, options, dotnet) {
  const element = document.getElementById(elementId);
  if (!element) return;
  ensureStylesheet();
  studioModule ??= await import(new URL('../customizer/customizer.js', import.meta.url).href);
  unmount(element.id);

  // React renders into a container owned by this script, never into a Blazor-managed element,
  // so neither renderer ever touches the other's DOM nodes.
  const container = document.createElement('div');
  container.className = 'studio-root';
  element.replaceChildren(container);

  const handle = studioModule.mount(container, {
    ...options,
    onCartUpdated: (message) => dotnet.invokeMethodAsync('OnCartUpdated', message),
    onRequireLogin: (returnUrl) => dotnet.invokeMethodAsync('OnRequireLogin', returnUrl),
  });
  handles.set(element.id, {
    // Deferred: Blazor may dispose the page from inside a studio callback (e.g. after "Add to cart"),
    // and React cannot unmount a root synchronously while it is still rendering.
    unmount: () => setTimeout(() => {
      handle.unmount();
      container.remove();
    }),
  });
}

export function unmount(elementId) {
  const handle = handles.get(elementId);
  if (handle) {
    handle.unmount();
    handles.delete(elementId);
  }
}
