import { useRef, useState, type ChangeEvent, type DragEvent } from 'react';
import { errorMessage } from '../../api/client';
import { useStudio, useStudioState } from '../../context';
import type { Artwork } from '../../design/model';

export const ACCEPTED_TYPES = ['image/png', 'image/jpeg', 'image/webp'];
export const MAX_BYTES = 15 * 1024 * 1024;
export const MIN_DIMENSION = 64;
const LOW_RES_PX = 1500;

/** Fast client-side checks for good UX; the server re-validates everything (type sniffing, limits). */
export async function checkImageFile(file: File): Promise<string | null> {
  if (!ACCEPTED_TYPES.includes(file.type)) return 'Please choose a PNG, JPG or WebP image.';
  if (file.size > MAX_BYTES) return 'That image is larger than 15 MB. Please choose a smaller file.';
  if (file.size === 0) return 'That file is empty.';
  try {
    const bitmap = await createImageBitmap(file);
    const tooSmall = bitmap.width < MIN_DIMENSION || bitmap.height < MIN_DIMENSION;
    bitmap.close();
    return tooSmall ? `The image is too small. Use at least ${MIN_DIMENSION} × ${MIN_DIMENSION} pixels.` : null;
  } catch {
    return "That file doesn't look like a valid image.";
  }
}

export function useArtworkUpload() {
  const { api, images } = useStudio();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [warning, setWarning] = useState<string | null>(null);

  const upload = async (file: File): Promise<Artwork | null> => {
    setError(null);
    setWarning(null);
    const problem = await checkImageFile(file);
    if (problem) {
      setError(problem);
      return null;
    }

    setBusy(true);
    try {
      const result = await api.upload(file, file.name, 'Artwork');
      images.ensure(result.id, result.url);
      const artwork: Artwork = { uploadId: result.id, url: result.url, width: result.width, height: result.height, fileName: result.fileName };
      if (result.qualityWarning || Math.max(result.width, result.height) < LOW_RES_PX) {
        setWarning(result.qualityWarning ?? 'Your image may appear blurry when printed. For best results, use a high-resolution image.');
      }
      return artwork;
    } catch (e) {
      setError(errorMessage(e));
      return null;
    } finally {
      setBusy(false);
    }
  };

  return { upload, busy, error, warning };
}

export function UploadTool() {
  const { store } = useStudio();
  const artworks = useStudioState((s) => s.artworks);
  const selected = useStudioState((s) => s.design.surfaces.flatMap((x) => x.elements).find((e) => e.id === s.selectedId));
  const { upload, busy, error, warning } = useArtworkUpload();
  const [dragging, setDragging] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  const replaceTarget = selected?.type === 'image' ? selected : null;

  const handleFile = async (file: File | undefined, replace = false) => {
    if (!file) return;
    const artwork = await upload(file);
    if (!artwork) return;
    if (replace && replaceTarget) {
      store.getState().replaceImage(replaceTarget.id, artwork);
    } else {
      store.getState().addImage(artwork);
    }
  };

  const onDrop = (e: DragEvent) => {
    e.preventDefault();
    setDragging(false);
    void handleFile(e.dataTransfer.files[0]);
  };

  const onChange = (e: ChangeEvent<HTMLInputElement>, replace: boolean) => {
    void handleFile(e.target.files?.[0], replace);
    e.target.value = '';
  };

  return (
    <div className="sls-stack">
      <label
        className={`sls-dropzone${dragging ? ' sls-dropzone--active' : ''}${busy ? ' sls-dropzone--busy' : ''}`}
        onDragOver={(e) => {
          e.preventDefault();
          setDragging(true);
        }}
        onDragLeave={() => setDragging(false)}
        onDrop={onDrop}
      >
        <input ref={inputRef} type="file" accept={ACCEPTED_TYPES.join(',')} className="sls-visually-hidden" onChange={(e) => onChange(e, false)} disabled={busy} />
        <UploadIcon />
        <strong>{busy ? 'Uploading…' : 'Drop your image here'}</strong>
        <span>or click to browse · PNG, JPG, WebP up to 15 MB</span>
      </label>

      {replaceTarget && (
        <label className="sls-btn sls-btn--secondary sls-btn--block">
          <input type="file" accept={ACCEPTED_TYPES.join(',')} className="sls-visually-hidden" onChange={(e) => onChange(e, true)} disabled={busy} />
          Replace selected image
        </label>
      )}

      <div aria-live="polite">
        {error && <p className="sls-alert sls-alert--error" role="alert">{error}</p>}
        {warning && <p className="sls-alert sls-alert--warning">{warning}</p>}
      </div>

      {Object.values(artworks).length > 0 && (
        <div>
          <h3 className="sls-subtitle">Your uploads</h3>
          <ul className="sls-thumbs">
            {Object.values(artworks).map((a) => (
              <li key={a.uploadId}>
                <button type="button" onClick={() => store.getState().addImage(a)} title={`Add ${a.fileName} again`} aria-label={`Add ${a.fileName} to the design`}>
                  <img src={a.url} alt="" loading="lazy" />
                </button>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}

function UploadIcon() {
  return (
    <svg viewBox="0 0 24 24" width="32" height="32" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M12 16V4M7 9l5-5 5 5" />
      <path d="M4 15v3a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-3" />
    </svg>
  );
}
