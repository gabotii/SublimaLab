// Generates the product GLB models used by the 3D preview (written to the storefront's wwwroot/models).
//
// Conventions read by the viewer:
//   - mesh "body"        : the product material, tinted with the selected colour at runtime
//   - mesh "detail"      : untinted parts (metal rings...)
//   - mesh "print-<key>" : a print surface whose UVs span exactly the print area (0..1),
//                          with node extras { printArea, widthMm, heightMm } so the viewer can rescale
//                          it when an administrator changes the print-area size.
// Units are metres; +Z faces the camera in the "front" view.
//
//   npm run models
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import * as THREE from 'three';
import { mergeVertices, toCreasedNormals } from 'three/addons/utils/BufferGeometryUtils.js';

const outDir = fileURLToPath(new URL('../../SublimaLab.Blazor/wwwroot/models/', import.meta.url));
fs.mkdirSync(outDir, { recursive: true });

// ---------------------------------------------------------------- GLB writer

function writeGlb(file, parts, rootRotationY = 0) {
  const chunks = [];
  let byteLength = 0;
  const bufferViews = [];
  const accessors = [];

  const addView = (typed, target) => {
    const pad = (4 - (byteLength % 4)) % 4;
    if (pad) { chunks.push(new Uint8Array(pad)); byteLength += pad; }
    const bytes = new Uint8Array(typed.buffer, typed.byteOffset, typed.byteLength);
    chunks.push(bytes);
    bufferViews.push({ buffer: 0, byteOffset: byteLength, byteLength: bytes.byteLength, target });
    byteLength += bytes.byteLength;
    return bufferViews.length - 1;
  };

  const addAccessor = (array, itemSize, componentType, target, withBounds) => {
    const view = addView(array, target);
    const count = array.length / itemSize;
    const accessor = { bufferView: view, componentType, count, type: { 1: 'SCALAR', 2: 'VEC2', 3: 'VEC3' }[itemSize] };
    if (withBounds) {
      const min = Array(itemSize).fill(Infinity);
      const max = Array(itemSize).fill(-Infinity);
      for (let i = 0; i < array.length; i++) {
        const c = i % itemSize;
        min[c] = Math.min(min[c], array[i]);
        max[c] = Math.max(max[c], array[i]);
      }
      Object.assign(accessor, { min, max });
    }
    accessors.push(accessor);
    return accessors.length - 1;
  };

  const materials = [];
  const materialIndex = new Map();
  const meshes = [];
  const nodes = [{ name: 'Product', children: [], rotation: quaternionY(rootRotationY) }];

  for (const part of parts) {
    let geometry = part.geometry;
    if (!geometry.attributes.normal) geometry.computeVertexNormals();
    if (!geometry.index) geometry = mergeToIndexed(geometry);

    const position = new Float32Array(geometry.attributes.position.array);
    const normal = new Float32Array(geometry.attributes.normal.array);
    const uv = geometry.attributes.uv ? new Float32Array(geometry.attributes.uv.array) : new Float32Array((position.length / 3) * 2);
    const indexSource = geometry.index.array;
    const index = position.length / 3 > 65535 ? new Uint32Array(indexSource) : new Uint16Array(indexSource);

    const attributes = {
      POSITION: addAccessor(position, 3, 5126, 34962, true),
      NORMAL: addAccessor(normal, 3, 5126, 34962, false),
      TEXCOORD_0: addAccessor(uv, 2, 5126, 34962, false),
    };
    const indices = addAccessor(index, 1, index instanceof Uint32Array ? 5125 : 5123, 34963, false);

    const key = part.material.name;
    if (!materialIndex.has(key)) {
      materials.push({
        name: key,
        doubleSided: part.material.doubleSided ?? false,
        alphaMode: part.material.alpha ? 'BLEND' : 'OPAQUE',
        pbrMetallicRoughness: {
          baseColorFactor: part.material.color,
          roughnessFactor: part.material.roughness ?? 0.8,
          metallicFactor: part.material.metallic ?? 0,
        },
      });
      materialIndex.set(key, materials.length - 1);
    }

    meshes.push({ name: part.name, primitives: [{ attributes, indices, material: materialIndex.get(key) }] });
    const node = { name: part.name, mesh: meshes.length - 1 };
    if (part.position) node.translation = part.position;
    if (part.rotationY) node.rotation = quaternionY(part.rotationY);
    if (part.extras) node.extras = part.extras;
    nodes.push(node);
    nodes[0].children.push(nodes.length - 1);
  }

  const json = {
    asset: { version: '2.0', generator: 'SublimaLab generate-models.mjs' },
    scene: 0,
    scenes: [{ name: 'Scene', nodes: [0] }],
    nodes,
    meshes,
    materials,
    accessors,
    bufferViews,
    buffers: [{ byteLength: align4(byteLength) }],
  };

  const jsonBytes = padBytes(new TextEncoder().encode(JSON.stringify(json)), 0x20);
  const bin = new Uint8Array(align4(byteLength));
  let offset = 0;
  for (const chunk of chunks) { bin.set(chunk, offset); offset += chunk.byteLength; }

  const total = 12 + 8 + jsonBytes.byteLength + 8 + bin.byteLength;
  const glb = new Uint8Array(total);
  const view = new DataView(glb.buffer);
  view.setUint32(0, 0x46546c67, true); // "glTF"
  view.setUint32(4, 2, true);
  view.setUint32(8, total, true);
  view.setUint32(12, jsonBytes.byteLength, true);
  view.setUint32(16, 0x4e4f534a, true); // "JSON"
  glb.set(jsonBytes, 20);
  const binHeader = 20 + jsonBytes.byteLength;
  view.setUint32(binHeader, bin.byteLength, true);
  view.setUint32(binHeader + 4, 0x004e4942, true); // "BIN"
  glb.set(bin, binHeader + 8);

  fs.writeFileSync(path.join(outDir, file), glb);
  console.log(`${file.padEnd(24)} ${(total / 1024).toFixed(1)} KB, ${parts.length} meshes`);
}

const align4 = (n) => Math.ceil(n / 4) * 4;
function padBytes(bytes, fill) {
  const padded = new Uint8Array(align4(bytes.byteLength)).fill(fill);
  padded.set(bytes);
  return padded;
}
function quaternionY(angle) {
  return [0, Math.sin(angle / 2), 0, Math.cos(angle / 2)];
}
// Smooth shading on curved surfaces, crisp edges where faces meet at a sharp angle, shared vertices.
function smooth(geometry, creaseDegrees = 35) {
  return mergeVertices(toCreasedNormals(geometry, THREE.MathUtils.degToRad(creaseDegrees)), 1e-5);
}

function mergeToIndexed(geometry) {
  const count = geometry.attributes.position.count;
  geometry.setIndex([...Array(count).keys()]);
  return geometry;
}

// ---------------------------------------------------------------- materials

const body = (hex = 0xffffff, roughness = 0.85) => {
  const c = new THREE.Color(hex);
  return { name: 'body', color: [c.r, c.g, c.b, 1], roughness };
};
const metal = { name: 'detail', color: [0.72, 0.74, 0.78, 1], roughness: 0.35, metallic: 0.9 };
const print = { name: 'print', color: [1, 1, 1, 1], roughness: 0.75, alpha: true };

function printPlane(key, widthMm, heightMm, z, rotationY = 0, y = 0) {
  return {
    name: `print-${key}`,
    geometry: new THREE.PlaneGeometry(widthMm / 1000, heightMm / 1000, 1, 1),
    material: print,
    position: [0, y, z],
    rotationY,
    extras: { printArea: key, widthMm, heightMm },
  };
}

// ---------------------------------------------------------------- T-shirt

function tshirt() {
  // Silhouette traced from the storefront sprite (240-unit viewBox), scaled so the body is ~52 cm wide.
  const s = 0.52 / 114;
  const p = (x, y) => [(x - 120) * s, -(y - 122) * s];
  const shape = new THREE.Shape();
  shape.moveTo(...p(80, 30));
  shape.lineTo(...p(60, 38));
  shape.lineTo(...p(22, 62));
  shape.lineTo(...p(39, 101));
  shape.lineTo(...p(63, 91));
  shape.lineTo(...p(63, 214));
  shape.lineTo(...p(177, 214));
  shape.lineTo(...p(177, 91));
  shape.lineTo(...p(201, 101));
  shape.lineTo(...p(218, 62));
  shape.lineTo(...p(180, 38));
  shape.lineTo(...p(160, 30));
  shape.bezierCurveTo(...p(152, 44), ...p(138, 52), ...p(120, 52));
  shape.bezierCurveTo(...p(102, 52), ...p(88, 44), ...p(80, 30));

  const depth = 0.05;
  const bevel = 0.035;
  const geometry = new THREE.ExtrudeGeometry(shape, {
    depth,
    bevelEnabled: true,
    bevelThickness: bevel,
    bevelSize: 0.02,
    bevelSegments: 10,
    curveSegments: 16,
  });
  geometry.translate(0, 0, -depth / 2);
  const face = depth / 2 + bevel + 0.0008;

  writeGlb('tshirt.glb', [
    { name: 'body', geometry: smooth(geometry), material: body(0xffffff, 0.92) },
    printPlane('front', 300, 400, face, 0, -0.01),
    printPlane('back', 300, 400, -face, Math.PI, -0.01),
  ]);
}

// ---------------------------------------------------------------- Mug

function mug() {
  const radius = 0.042;
  const height = 0.095;
  const wall = 0.004;
  const profile = [
    new THREE.Vector2(0, 0),
    new THREE.Vector2(radius - 0.003, 0),
    new THREE.Vector2(radius, 0.003),
    new THREE.Vector2(radius, height),
    new THREE.Vector2(radius - wall, height),
    new THREE.Vector2(radius - wall, 0.008),
    new THREE.Vector2(0, 0.008),
  ];
  const cup = new THREE.LatheGeometry(profile, 96);
  cup.translate(0, -height / 2, 0);

  const handle = new THREE.TorusGeometry(0.027, 0.0068, 20, 48, Math.PI);
  handle.rotateZ(-Math.PI / 2);
  handle.translate(radius - 0.002, 0.002, 0);

  // Print wraps around the mug, leaving the gap at the handle (+X).
  const wrapMm = 200;
  const wrapHeightMm = 85;
  const printRadius = radius + 0.0006;
  const thetaLength = wrapMm / 1000 / printRadius;
  const gap = Math.PI * 2 - thetaLength;
  const wrap = new THREE.CylinderGeometry(printRadius, printRadius, wrapHeightMm / 1000, 96, 1, true, Math.PI / 2 + gap / 2, thetaLength);

  writeGlb('mug.glb', [
    { name: 'body', geometry: cup, material: body(0xffffff, 0.35) },
    { name: 'body', geometry: handle, material: body(0xffffff, 0.35) },
    { name: 'print-wrap', geometry: wrap, material: { ...print, doubleSided: false }, extras: { printArea: 'wrap', widthMm: wrapMm, heightMm: wrapHeightMm, cylindrical: true } },
  ], Math.PI / 2); // print centre faces the camera; the handle sits behind
}

// ---------------------------------------------------------------- Keychains

function keychain(file, shape, widthMm, heightMm, holeY) {
  const depth = 0.003;
  const bevel = 0.0008;
  const geometry = new THREE.ExtrudeGeometry(shape, { depth, bevelEnabled: true, bevelThickness: bevel, bevelSize: 0.0008, bevelSegments: 3, curveSegments: 12 });
  geometry.translate(0, 0, -depth / 2);
  const face = depth / 2 + bevel + 0.0003;

  const ring = new THREE.TorusGeometry(0.012, 0.0016, 12, 48);
  ring.rotateY(Math.PI / 2);
  ring.translate(0, holeY + 0.014, 0);
  const link = new THREE.TorusGeometry(0.0035, 0.0009, 8, 24);
  link.rotateY(Math.PI / 2);
  link.translate(0, holeY + 0.002, 0);

  writeGlb(file, [
    { name: 'body', geometry: smooth(geometry), material: body(0xffffff, 0.25) },
    { name: 'detail', geometry: ring, material: metal },
    { name: 'detail', geometry: link, material: metal },
    printPlane('front', widthMm, heightMm, face),
    printPlane('back', widthMm, heightMm, -face, Math.PI),
  ]);
}

function roundedRect(width, height, radius) {
  const shape = new THREE.Shape();
  const x = -width / 2;
  const y = -height / 2;
  shape.moveTo(x + radius, y);
  shape.lineTo(x + width - radius, y);
  shape.quadraticCurveTo(x + width, y, x + width, y + radius);
  shape.lineTo(x + width, y + height - radius);
  shape.quadraticCurveTo(x + width, y + height, x + width - radius, y + height);
  shape.lineTo(x + radius, y + height);
  shape.quadraticCurveTo(x, y + height, x, y + height - radius);
  shape.lineTo(x, y + radius);
  shape.quadraticCurveTo(x, y, x + radius, y);
  return shape;
}

function withHole(shape, y) {
  const hole = new THREE.Path();
  hole.absarc(0, y, 0.0022, 0, Math.PI * 2, true);
  shape.holes.push(hole);
  return shape;
}

tshirt();
mug();
keychain('acrylic-keychain.glb', withHole(roundedRect(0.054, 0.076, 0.008), 0.031), 50, 70, 0.031);
keychain('square-keychain.glb', withHole(roundedRect(0.054, 0.054, 0.009), 0.022), 50, 50, 0.022);
const circle = new THREE.Shape();
circle.absarc(0, 0, 0.027, 0, Math.PI * 2, false);
keychain('round-keychain.glb', withHole(circle, 0.0215), 50, 50, 0.0215);
