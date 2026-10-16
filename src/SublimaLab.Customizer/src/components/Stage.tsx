import { lazy, Suspense, useEffect, useImperativeHandle, useRef, useState, type Ref } from 'react';
import { useStudio, useStudioState } from '../context';
import { renderSurfaceToCanvas } from '../design/render';
import { surfaceOf } from '../design/model';
import { Editor2D } from './Editor2D';
import type { ViewerHandle } from '../viewer/Viewer3D';

// three.js and React Three Fiber load only when the 3D preview is first shown.
const Viewer3D = lazy(() => import('../viewer/Viewer3D'));

export interface StageHandle {
  /** PNG of the current design (3D view when available) for the cart preview. */
  snapshot(): Promise<Blob | null>;
}

const wideQuery = '(min-width: 1440px)';

export function Stage({ color, ref }: { color: string; ref?: Ref<StageHandle> }) {
  const { product, store, images } = useStudio();
  const areas = product.customization!.printAreas;
  const activeArea = useStudioState((s) => s.activeArea);
  const [view, setView] = useState<'design' | '3d'>('design');
  const [wide, setWide] = useState(() => window.matchMedia(wideQuery).matches);
  const [viewerRequested, setViewerRequested] = useState(false);
  const viewerRef = useRef<ViewerHandle>(null);

  useEffect(() => {
    const media = window.matchMedia(wideQuery);
    const onChange = () => setWide(media.matches);
    media.addEventListener('change', onChange);
    return () => media.removeEventListener('change', onChange);
  }, []);

  const showDesign = wide || view === 'design';
  const showViewer = wide || view === '3d';
  useEffect(() => {
    if (showViewer) setViewerRequested(true);
  }, [showViewer]);

  useImperativeHandle(ref, () => ({
    snapshot: async () => {
      const fromViewer = viewerRef.current ? await viewerRef.current.snapshot() : null;
      return fromViewer ?? flatPreview(store.getState().design, areas, color, images.map);
    },
  }));

  return (
    <div className="sls-stage">
      <div className="sls-stage-bar">
        {areas.length > 1 && (
          <div className="sls-segmented" role="tablist" aria-label="Print area">
            {areas.map((area) => (
              <button key={area.key} type="button" role="tab" aria-selected={area.key === activeArea} onClick={() => store.getState().setActiveArea(area.key)}>
                {area.name}
              </button>
            ))}
          </div>
        )}
        {!wide && (
          <div className="sls-segmented" role="tablist" aria-label="View">
            <button type="button" role="tab" aria-selected={view === 'design'} onClick={() => setView('design')}>
              Edit design
            </button>
            <button type="button" role="tab" aria-selected={view === '3d'} onClick={() => setView('3d')}>
              3D preview
            </button>
          </div>
        )}
      </div>

      <div className={`sls-stage-views${wide ? ' sls-stage-views--split' : ''}`}>
        <div className="sls-view" hidden={!showDesign}>
          <Editor2D color={color} />
        </div>
        {viewerRequested && (
          <div className="sls-view sls-view--3d" hidden={!showViewer}>
            <Suspense fallback={<div className="sls-viewer-loading" role="status"><span className="sls-spinner" /> Loading 3D preview…</div>}>
              <Viewer3D ref={viewerRef} color={color} />
            </Suspense>
          </div>
        )}
      </div>
    </div>
  );
}

/** Fallback preview when WebGL is unavailable: the first decorated surface on the product colour. */
async function flatPreview(
  design: Parameters<typeof surfaceOf>[0],
  areas: { key: string; name: string; widthMm: number; heightMm: number; shape: string }[],
  color: string,
  images: Parameters<typeof renderSurfaceToCanvas>[3],
): Promise<Blob | null> {
  const area = areas.find((a) => surfaceOf(design, a.key).elements.length > 0) ?? areas[0];
  if (!area) return null;
  const size = 640;
  const canvas = document.createElement('canvas');
  canvas.width = size;
  canvas.height = size;
  const ctx = canvas.getContext('2d');
  if (!ctx) return null;
  ctx.fillStyle = color;
  ctx.fillRect(0, 0, size, size);
  const pxPerMm = (size * 0.8) / Math.max(area.widthMm, area.heightMm);
  const surface = renderSurfaceToCanvas(surfaceOf(design, area.key), area, pxPerMm, images);
  ctx.drawImage(surface, (size - surface.width) / 2, (size - surface.height) / 2);
  return new Promise((resolve) => canvas.toBlob((b) => resolve(b), 'image/png'));
}
