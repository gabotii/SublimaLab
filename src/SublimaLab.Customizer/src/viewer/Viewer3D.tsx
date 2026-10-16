import { Bounds, Center, ContactShadows, Environment, Lightformer, OrbitControls, useBounds, useGLTF, useProgress } from '@react-three/drei';
import { Canvas, useFrame, useThree } from '@react-three/fiber';
import { Component, Suspense, useEffect, useImperativeHandle, useMemo, useRef, useState, type ReactNode, type Ref } from 'react';
import * as THREE from 'three';
import { useStudio, useStudioState } from '../context';
import { surfaceOf } from '../design/model';
import { renderSurfaceToCanvas } from '../design/render';

export interface ViewerHandle {
  snapshot(): Promise<Blob | null>;
}

type Command = { type: 'front' | 'back' | 'reset' | 'zoomIn' | 'zoomOut'; id: number };

/** The parts of drei's OrbitControls this viewer uses. */
interface ControlsLike {
  target: THREE.Vector3;
  reset(): void;
  update(): void;
}

const PRODUCT_GROUP = 'sls-product';

/** Texture resolution: the longest side of a print area maps to 1024 px. */
const texturePxPerMm = (area: { widthMm: number; heightMm: number }) => 1024 / Math.max(area.widthMm, area.heightMm);

const prefersReducedMotion = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches;

function webglAvailable(): boolean {
  try {
    const canvas = document.createElement('canvas');
    return Boolean(canvas.getContext('webgl2') ?? canvas.getContext('webgl'));
  } catch {
    return false;
  }
}

export default function Viewer3D({ color, ref }: { color: string; ref?: Ref<ViewerHandle> }) {
  const { product } = useStudio();
  const modelUrl = product.customization?.modelUrl;
  const threeRef = useRef<{ gl: THREE.WebGLRenderer; scene: THREE.Scene; camera: THREE.Camera } | null>(null);
  const [command, setCommand] = useState<Command>({ type: 'reset', id: 0 });
  const [supported] = useState(webglAvailable);
  const activeArea = useStudioState((s) => s.activeArea);

  const send = (type: Command['type']) => setCommand((c) => ({ type, id: c.id + 1 }));

  // Turn the product to show the print area being edited.
  useEffect(() => {
    if (activeArea === 'back') send('back');
    else send('front');
  }, [activeArea]);

  useImperativeHandle(ref, () => ({
    snapshot: async () => {
      const three = threeRef.current;
      if (!three) return null;
      // Always photograph the front, then restore the customer's view.
      const product = three.scene.getObjectByName(PRODUCT_GROUP);
      const previous = product?.rotation.y ?? 0;
      if (product) product.rotation.y = 0;
      three.gl.render(three.scene, three.camera);

      // Centre square crop, downscaled to a small PNG for the cart thumbnail.
      const source = three.gl.domElement;
      const side = Math.min(source.width, source.height);
      const size = Math.min(640, side);
      const canvas = document.createElement('canvas');
      canvas.width = size;
      canvas.height = size;
      canvas.getContext('2d')?.drawImage(source, (source.width - side) / 2, (source.height - side) / 2, side, side, 0, 0, size, size);
      if (product) product.rotation.y = previous;
      return new Promise((resolve) => canvas.toBlob((b) => resolve(b), 'image/png'));
    },
  }));

  if (!supported || !modelUrl) {
    return (
      <div className="sls-viewer-fallback" role="status">
        <strong>3D preview unavailable</strong>
        <span>{modelUrl ? 'Your browser or device does not support WebGL.' : 'This product has no 3D model yet.'} You can still design and order in the editor.</span>
      </div>
    );
  }

  return (
    <div className="sls-viewer">
      <ViewerErrorBoundary>
        <Canvas
          shadows
          dpr={[1, 2]}
          frameloop="demand"
          camera={{ position: [0, 0.15, 1.4], fov: 35, near: 0.01, far: 50 }}
          gl={{ preserveDrawingBuffer: true, antialias: true }}
          onCreated={({ gl, scene, camera }) => {
            threeRef.current = { gl, scene, camera };
            gl.toneMapping = THREE.ACESFilmicToneMapping;
          }}
          aria-label={`3D preview of ${product.name}`}
        >
          <color attach="background" args={['#f7f6fb']} />
          <ambientLight intensity={0.55} />
          <directionalLight position={[2.5, 4, 3]} intensity={1.6} castShadow shadow-mapSize={[1024, 1024]} />
          <Environment resolution={128} frames={1}>
            <Lightformer intensity={2} position={[0, 4, 4]} scale={[6, 3, 1]} />
            <Lightformer intensity={1.2} position={[-4, 1, 2]} scale={[2, 4, 1]} />
            <Lightformer intensity={1} position={[4, 1, -2]} scale={[2, 4, 1]} />
          </Environment>
          <Suspense fallback={null}>
            <Bounds fit clip observe margin={1.25}>
              <SceneContent url={modelUrl} color={color} command={command} />
            </Bounds>
            <ContactShadows position={[0, -0.001, 0]} opacity={0.35} blur={2.4} far={1} resolution={512} frames={1} />
          </Suspense>
          <OrbitControls makeDefault enablePan={false} enableDamping dampingFactor={0.12} minPolarAngle={0.35} maxPolarAngle={Math.PI / 1.8} />
        </Canvas>
      </ViewerErrorBoundary>
      <LoadingOverlay />

      <div className="sls-viewer-controls" role="toolbar" aria-label="3D view controls">
        <button type="button" onClick={() => send('front')}>Front</button>
        <button type="button" onClick={() => send('back')}>Back</button>
        <button type="button" onClick={() => send('zoomIn')} aria-label="Zoom in">+</button>
        <button type="button" onClick={() => send('zoomOut')} aria-label="Zoom out">−</button>
        <button type="button" onClick={() => send('reset')}>Reset</button>
      </div>
      <p className="sls-viewer-hint">Drag to rotate · scroll or pinch to zoom</p>
    </div>
  );
}

/** Plain DOM overlay (outside the canvas) showing model download progress. */
function LoadingOverlay() {
  const { active, progress } = useProgress();
  if (!active) return null;
  return (
    <div className="sls-viewer-overlay" role="status">
      <span className="sls-spinner" aria-hidden="true" /> Loading 3D model… {Math.round(progress)}%
    </div>
  );
}

/** Loads the product model once, tints it and paints each print surface with its design texture. */
function SceneContent({ url, color, command }: { url: string; color: string; command: Command }) {
  const { product, images, store } = useStudio();
  const areas = product.customization!.printAreas;
  const gltf = useGLTF(url);
  const group = useRef<THREE.Group>(null);
  const targetRotation = useRef(0);
  const invalidate = useThree((s) => s.invalidate);
  const camera = useThree((s) => s.camera);
  const controls = useThree((s) => s.controls) as unknown as ControlsLike | null;
  const bounds = useBounds();
  const [imagesVersion, setImagesVersion] = useState(0);
  const design = useStudioState((s) => s.design);

  // Per-instance clone: materials are swapped without touching the cached model.
  const { scene, textures, bodyMaterials } = useMemo(() => {
    const clone = gltf.scene.clone(true);
    const created = new Map<string, { texture: THREE.CanvasTexture; canvas: HTMLCanvasElement }>();
    const bodies: THREE.MeshStandardMaterial[] = [];

    clone.traverse((object) => {
      if (!(object instanceof THREE.Mesh)) return;
      object.castShadow = true;
      object.receiveShadow = true;

      if (object.name.startsWith('body')) {
        const material = (object.material as THREE.MeshStandardMaterial).clone();
        object.material = material;
        bodies.push(material);
      } else if (object.name.startsWith('print-')) {
        const key = (object.userData.printArea as string | undefined) ?? object.name.slice(6);
        const area = areas.find((a) => a.key === key);
        if (!area) {
          object.visible = false;
          return;
        }

        // Follow the configured print-area size if an administrator changed it.
        const modelWidth = Number(object.userData.widthMm) || area.widthMm;
        const modelHeight = Number(object.userData.heightMm) || area.heightMm;
        if (!object.userData.cylindrical) object.scale.x = area.widthMm / modelWidth;
        object.scale.y = area.heightMm / modelHeight;

        // The canvas gets its final size before the texture exists: three.js allocates immutable GPU
        // storage on first upload, so a texture must never change size afterwards.
        const canvas = document.createElement('canvas');
        const pxPerMm = texturePxPerMm(area);
        canvas.width = Math.max(1, Math.round(area.widthMm * pxPerMm));
        canvas.height = Math.max(1, Math.round(area.heightMm * pxPerMm));
        const texture = new THREE.CanvasTexture(canvas);
        texture.colorSpace = THREE.SRGBColorSpace;
        texture.anisotropy = 8;
        created.set(key, { texture, canvas });
        object.material = new THREE.MeshStandardMaterial({
          map: texture,
          transparent: true,
          roughness: 0.8,
          polygonOffset: true,
          polygonOffsetFactor: -2,
          depthWrite: false,
        });
      }
    });

    return { scene: clone, textures: created, bodyMaterials: bodies };
  }, [gltf.scene, areas]);

  useEffect(() => images.subscribe(() => setImagesVersion((v) => v + 1)), [images]);

  // Repaint textures in place when the design or loaded images change (the scene is never rebuilt).
  useEffect(() => {
    const artworks = store.getState().artworks;
    Object.values(artworks).forEach((a) => images.ensure(a.uploadId, a.url));
    for (const area of areas) {
      const entry = textures.get(area.key);
      if (!entry) continue;
      renderSurfaceToCanvas(surfaceOf(design, area.key), area, texturePxPerMm(area), images.map, entry.canvas);
      entry.texture.needsUpdate = true;
    }
    invalidate();
  }, [design, imagesVersion, textures, areas, images, store, invalidate]);

  useEffect(() => {
    bodyMaterials.forEach((m) => m.color.set(color));
    invalidate();
  }, [bodyMaterials, color, invalidate]);

  // Dispose GPU resources owned by this instance.
  useEffect(
    () => () => {
      textures.forEach(({ texture }) => texture.dispose());
      scene.traverse((object) => {
        if (object instanceof THREE.Mesh && (object.name.startsWith('print-') || object.name.startsWith('body'))) {
          (object.material as THREE.Material).dispose();
        }
      });
    },
    [scene, textures],
  );

  // Release the cached model when the viewer goes away entirely.
  useEffect(
    () => () => {
      gltf.scene.traverse((object) => {
        if (object instanceof THREE.Mesh) {
          object.geometry.dispose();
          (object.material as THREE.Material).dispose();
        }
      });
      useGLTF.clear(url);
    },
    [gltf.scene, url],
  );

  useEffect(() => {
    const distance = () => camera.position.distanceTo(controls?.target ?? new THREE.Vector3());
    switch (command.type) {
      case 'front':
        targetRotation.current = 0;
        break;
      case 'back':
        targetRotation.current = Math.PI;
        break;
      case 'reset':
        targetRotation.current = 0;
        controls?.reset();
        bounds.refresh().clip().fit();
        break;
      case 'zoomIn':
      case 'zoomOut': {
        const factor = command.type === 'zoomIn' ? 0.8 : 1.25;
        const target = controls?.target ?? new THREE.Vector3();
        const next = THREE.MathUtils.clamp(distance() * factor, 0.05, 5);
        camera.position.sub(target).setLength(next).add(target);
        controls?.update();
        break;
      }
    }
    if (prefersReducedMotion() && group.current) group.current.rotation.y = targetRotation.current;
    invalidate();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [command.id]);

  // Smoothly turn towards the requested side; keeps rendering only while moving.
  useFrame((_, delta) => {
    const g = group.current;
    if (!g) return;
    const diff = targetRotation.current - g.rotation.y;
    if (Math.abs(diff) > 0.001) {
      g.rotation.y += diff * Math.min(1, delta * 6);
      invalidate();
    }
  });

  return (
    <group ref={group} name={PRODUCT_GROUP}>
      <Center top>
        <primitive object={scene} />
      </Center>
    </group>
  );
}

class ViewerErrorBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  render() {
    if (this.state.failed) {
      return (
        <div className="sls-viewer-fallback" role="alert">
          <strong>The 3D preview couldn't load</strong>
          <span>You can keep designing in the editor; your design is not affected.</span>
        </div>
      );
    }
    return this.props.children;
  }
}
