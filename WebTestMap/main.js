// Тестовая карта в браузере: ангар, все персонажи на постаментах с анимациями, патрули, стойка с оружием.
// Модели и таблица материалов берутся из Assets/_Project/Art (их собирает Tools/Blender/build_characters.py).
import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { EffectComposer } from 'three/addons/postprocessing/EffectComposer.js';
import { RenderPass } from 'three/addons/postprocessing/RenderPass.js';
import { UnrealBloomPass } from 'three/addons/postprocessing/UnrealBloomPass.js';
import { OutputPass } from 'three/addons/postprocessing/OutputPass.js';
import * as SkeletonUtils from 'three/addons/utils/SkeletonUtils.js';

const ART = '../Assets/_Project/Art/Characters/';
const params = new URLSearchParams(location.search);
const FREEZE = params.has('freeze');            // для скриншотов: заморозить позы
const SHOT = params.get('shot');                // пресет камеры для скриншота

const canvas = document.getElementById('view');
const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, preserveDrawingBuffer: true });
renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 1.1;

const scene = new THREE.Scene();
scene.background = new THREE.Color(0x05060a);
scene.fog = new THREE.Fog(0x0a0c12, 28, 90);
const camera = new THREE.PerspectiveCamera(50, 1, 0.05, 300);
camera.position.set(-2.2, 3.6, 13);
const controls = new OrbitControls(camera, canvas);
controls.target.set(-2.2, 1.1, -2);
controls.enableDamping = true;

const composer = new EffectComposer(renderer);
composer.addPass(new RenderPass(scene, camera));
const bloom = new UnrealBloomPass(new THREE.Vector2(1, 1), 0.7, 0.4, 1.6);
composer.addPass(bloom);
composer.addPass(new OutputPass());

function resize() {
  const w = innerWidth, h = innerHeight;
  renderer.setSize(w, h, false);
  composer.setSize(w, h);
  camera.aspect = w / h;
  camera.updateProjectionMatrix();
}
addEventListener('resize', resize);
resize();

// ------------------------------------------------------------------ ангар
const std = (color, metal = 0.3, rough = 0.6, emissive = 0x000000, ei = 1) =>
  new THREE.MeshStandardMaterial({ color, metalness: metal, roughness: rough, emissive, emissiveIntensity: ei });

function floorTexture() {
  const c = document.createElement('canvas');
  c.width = c.height = 512;
  const g = c.getContext('2d');
  g.fillStyle = '#3a3d44';
  g.fillRect(0, 0, 512, 512);
  for (let i = 0; i < 4000; i++) {
    const v = 52 + Math.random() * 14;
    g.fillStyle = `rgba(${v},${v + 2},${v + 6},0.35)`;
    g.fillRect(Math.random() * 512, Math.random() * 512, 3, 3);
  }
  g.strokeStyle = '#15171b'; g.lineWidth = 6;
  for (const p of [0, 256, 512]) { g.beginPath(); g.moveTo(p, 0); g.lineTo(p, 512); g.stroke(); g.beginPath(); g.moveTo(0, p); g.lineTo(512, p); g.stroke(); }
  g.strokeStyle = '#2c2f35'; g.lineWidth = 3;
  for (const p of [44, 300]) { g.beginPath(); g.moveTo(p, 0); g.lineTo(p, 512); g.stroke(); g.beginPath(); g.moveTo(0, p); g.lineTo(512, p); g.stroke(); }
  const t = new THREE.CanvasTexture(c);
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.repeat.set(20, 15);
  t.colorSpace = THREE.SRGBColorSpace;
  t.anisotropy = 8;
  return t;
}

function box(w, h, d, mat, x, y, z, cast = true) {
  const m = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), mat);
  m.position.set(x, y, z);
  m.castShadow = cast; m.receiveShadow = true;
  scene.add(m);
  return m;
}

const floorMat = new THREE.MeshStandardMaterial({ map: floorTexture(), metalness: 0.55, roughness: 0.5 });
const floor = new THREE.Mesh(new THREE.PlaneGeometry(80, 60), floorMat);
floor.rotation.x = -Math.PI / 2; floor.receiveShadow = true; scene.add(floor);
const wallMat = std(0x2a2d33, 0.5, 0.55), trimMat = std(0x16181c, 0.7, 0.4);
const stripMat = std(0x000000, 0, 1, 0xbfd6ff, 2.2), redMat = std(0x000000, 0, 1, 0xff2a18, 2.5);
box(80, 12, 0.5, wallMat, 0, 6, -22); box(0.5, 12, 60, wallMat, -30, 6, 0); box(0.5, 12, 60, wallMat, 30, 6, 0);
for (let i = -6; i <= 6; i++) {
  box(0.8, 12, 0.6, trimMat, i * 4.5, 6, -21.5);
  box(3, 0.12, 0.05, stripMat, i * 4.5 + 2.25, 7.5, -21.7, false);
  box(3, 0.05, 0.05, redMat, i * 4.5 + 2.25, 0.4, -21.7, false);
}
const crateMat = std(0x4a4c52, 0.4, 0.6);
let seed = 3; const rnd = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
for (let i = 0; i < 14; i++) {
  const s = 0.8 + rnd() * 0.8, x = -26 + rnd() * 52, z = -18 + rnd() * 6;
  box(s * 1.4, s, s, crateMat, x, s / 2, z).rotation.y = (rnd() - 0.5) * 0.7;
}
box(16, 0.6, 4, trimMat, 0, 0.3, -6);

scene.add(new THREE.HemisphereLight(0x9aa8c8, 0x18181c, 1.6));
const key = new THREE.DirectionalLight(0xe8eeff, 2.0);
key.position.set(-8, 14, 10);
key.castShadow = true;
key.shadow.mapSize.set(2048, 2048);
Object.assign(key.shadow.camera, { left: -20, right: 20, top: 16, bottom: -16, near: 1, far: 50 });
scene.add(key);
for (let i = -2; i <= 2; i++) {
  const l = new THREE.PointLight(0xbcd0ff, 25, 20, 1.6);
  l.position.set(i * 10, 7, -19); scene.add(l);
}
const fill = new THREE.SpotLight(0xfff0e0, 140, 40, 0.75, 0.6, 1.2); fill.position.set(-3, 9, 12); fill.target.position.set(0, 0, -3);
fill.castShadow = true; fill.shadow.mapSize.set(2048, 2048); scene.add(fill, fill.target);

// ------------------------------------------------------------------ персонажи
const loader = new GLTFLoader();
const ready = [];
const chars = [];       // { name, title, root, mixer, actions, clips, cur, t, auto, blades, weapon, handL, info }
const patrols = [];
let selected = -1;

function makeMaterials(info) {
  const out = {};
  for (const [name, m] of Object.entries(info.materials || {})) {
    const mat = new THREE.MeshStandardMaterial({
      metalness: m.metallic, roughness: Math.min(1, Math.max(0.04, 1 - m.smoothness)),
      transparent: m.alpha < 1, opacity: m.alpha,
    });
    mat.color.setRGB(m.color[0], m.color[1], m.color[2], THREE.LinearSRGBColorSpace);
    const e = m.emission || [0, 0, 0];
    if (e[0] + e[1] + e[2] > 0.01) {
      const mx = Math.max(e[0], e[1], e[2]);
      mat.emissive.setRGB(e[0] / mx, e[1] / mx, e[2] / mx, THREE.LinearSRGBColorSpace);
      mat.emissiveIntensity = Math.min(mx, 6) * (name.startsWith('Blade') ? 1.4 : 0.8);
      if (name.startsWith('Blade')) mat.color.setRGB(1, 0.92, 0.9);
    }
    out[name] = mat;
  }
  return out;
}

function prepare(root, mats) {
  const bones = {};
  root.traverse(o => {
    if (o.isMesh) {
      o.castShadow = true; o.receiveShadow = true; o.frustumCulled = false;
      const fix = m => (m && mats[m.name]) ? mats[m.name] : m;
      o.material = Array.isArray(o.material) ? o.material.map(fix) : fix(o.material);
    }
    if (o.isBone || o.type === 'Bone' || o.type === 'Object3D') bones[o.name] = o;
  });
  return bones;
}

function addBladeLights(bones) {
  const res = [];
  for (const n of ['BladeR', 'BladeR2']) {
    const b = bones[n];
    if (!b) continue;
    const l = new THREE.PointLight(0xff2a1a, 6, 4, 1.5);
    l.position.set(0, 0.5, 0);
    b.add(l);
    res.push({ bone: b, light: l });
  }
  return res;
}

function pedestal(x, y, z, sith) {
  const p = new THREE.Mesh(new THREE.CylinderGeometry(1.3, 1.3, 0.16, 48), std(0x18191c, 0.85, 0.3));
  p.position.set(x, y + 0.08, z); p.receiveShadow = true; scene.add(p);
  const r = new THREE.Mesh(new THREE.TorusGeometry(1.32, 0.025, 8, 64), sith ? redMat : stripMat);
  r.rotation.x = Math.PI / 2; r.position.set(x, y + 0.16, z); scene.add(r);
}

const tagsEl = document.getElementById('tags');

async function load() {
  const manifest = await (await fetch(ART + 'characters.json')).json();
  const names = Object.keys(manifest);
  const sith = names.filter(n => manifest[n].style !== 'rifle');
  const troopers = names.filter(n => manifest[n].style === 'rifle');
  const gltfs = {};
  await Promise.all(names.map(async n => { gltfs[n] = await loader.loadAsync(ART + n + '.glb'); }));
  const place = (row, z, y) => row.forEach((n, i) => {
    const x = (i - (row.length - 1) / 2) * 4.2;
    pedestal(x, y, z, manifest[n].style !== 'rifle');
    spawn(n, manifest[n], gltfs[n], new THREE.Vector3(x, y + 0.16, z), true);
  });
  place(sith, 0, 0);
  place(troopers, -6, 0.6);
  // патрули
  const loop = [[-14, 6], [14, 6], [14, -12], [-14, -12]].map(([x, z]) => new THREE.Vector3(x, 0, z));
  const big = [[-22, 10], [-22, -14], [22, -14], [22, 10]].map(([x, z]) => new THREE.Vector3(x, 0, z));
  if (gltfs.Stormtrooper) { addPatrol(manifest.Stormtrooper, gltfs.Stormtrooper, loop, 0, 1.6, 'Walk'); addPatrol(manifest.Stormtrooper, gltfs.Stormtrooper, loop, 2, 1.6, 'Walk'); }
  if (gltfs.TrooperCommander) addPatrol(manifest.TrooperCommander, gltfs.TrooperCommander, loop, 1, 1.6, 'Walk');
  if (gltfs.HeavyTrooper) addPatrol(manifest.HeavyTrooper, gltfs.HeavyTrooper, big, 0, 4.5, 'Run');
  buildPanel();
  applyParams();
  document.getElementById('loading').remove();
  window.__ready = true;
}

function spawn(name, info, gltf, pos, show) {
  const root = SkeletonUtils.clone(gltf.scene);
  const bones = prepare(root, makeMaterials(info));
  root.position.copy(pos);
  scene.add(root);
  const mixer = new THREE.AnimationMixer(root);
  const actions = {};
  for (const clip of gltf.animations) actions[clip.name] = mixer.clipAction(clip);
  const clips = Object.keys(info.clips).filter(c => actions[c]);
  const c = { name, title: info.title || name, root, mixer, actions, clips, info, cur: -1, t: 0, auto: true,
              blades: addBladeLights(bones), weapon: bones.WeaponR, handL: bones.HandL, fired: false };
  if (show) {
    const tag = document.createElement('div');
    tag.className = 'tag';
    tag.innerHTML = `<b>${c.title}</b><i></i>`;
    tagsEl.appendChild(tag);
    c.tag = tag;
    chars.push(c);
    play(c, Math.max(0, clips.indexOf('Idle')));
  }
  return c;
}

function addPatrol(info, gltf, pts, start, speed, clip) {
  const c = spawn(info.name, info, gltf, pts[start].clone(), false);
  const a = c.actions[clip];
  a.play(); a.time = Math.random() * a.getClip().duration;
  patrols.push({ c, pts, target: (start + 1) % pts.length, speed });
}

function play(c, i) {
  i = ((i % c.clips.length) + c.clips.length) % c.clips.length;
  const next = c.actions[c.clips[i]];
  const prev = c.cur >= 0 ? c.actions[c.clips[c.cur]] : null;
  next.reset();
  const loop = c.info.clips[c.clips[i]].loop;
  next.setLoop(loop ? THREE.LoopRepeat : THREE.LoopOnce, Infinity);
  next.clampWhenFinished = !loop;
  next.play();
  if (prev && prev !== next) prev.crossFadeTo(next, 0.12, false);
  c.cur = i; c.t = 0; c.fired = false;
  if (c.tag) c.tag.querySelector('i').textContent = c.clips[i];
  refreshPanel();
}

// ------------------------------------------------------------------ эффекты
const bolts = [];
const boltGeo = new THREE.CapsuleGeometry(0.018, 0.4, 4, 8);
const boltMat = new THREE.MeshBasicMaterial({ color: new THREE.Color(4, 0.5, 0.3) });
function fireBolt(c) {
  if (!c.weapon) return;
  const dir = new THREE.Vector3(0, 1, 0).applyQuaternion(c.weapon.getWorldQuaternion(new THREE.Quaternion())).normalize();
  const pos = c.weapon.getWorldPosition(new THREE.Vector3()).addScaledVector(dir, 0.45 * c.root.scale.y);
  const m = new THREE.Mesh(boltGeo, boltMat);
  m.position.copy(pos);
  m.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), dir);
  const l = new THREE.PointLight(0xff3020, 5, 3, 1.5); m.add(l);
  scene.add(m);
  bolts.push({ m, v: dir.multiplyScalar(40), life: 1.2 });
}

const lightningMat = new THREE.LineBasicMaterial({ color: new THREE.Color(2.2, 2.6, 4) });
function lightning(c, on) {
  if (!on) { if (c.bolt) c.bolt.visible = false; return; }
  if (!c.bolt) {
    c.bolt = new THREE.Group();
    for (let k = 0; k < 4; k++) c.bolt.add(new THREE.Line(new THREE.BufferGeometry(), lightningMat));
    c.boltLight = new THREE.PointLight(0x9ab8ff, 30, 8, 1.5);
    c.bolt.add(c.boltLight);
    scene.add(c.bolt);
  }
  c.bolt.visible = true;
  const a = (c.handL || c.root).getWorldPosition(new THREE.Vector3());
  const fwd = new THREE.Vector3(0, 0, 1).applyQuaternion(c.root.quaternion);
  const b = a.clone().addScaledVector(fwd, 4.5).add(new THREE.Vector3(0, -0.3, 0));
  for (const line of c.bolt.children) {
    if (!line.isLine) continue;
    const pts = [];
    for (let i = 0; i <= 22; i++) {
      const t = i / 22, p = a.clone().lerp(b, t);
      if (i > 0 && i < 22) p.add(new THREE.Vector3(Math.random() - 0.5, Math.random() - 0.5, Math.random() - 0.5).multiplyScalar(0.45 * Math.sin(Math.PI * t)));
      pts.push(p);
    }
    line.geometry.setFromPoints(pts);
  }
  c.boltLight.position.copy(a.clone().lerp(b, 0.4));
}

// ------------------------------------------------------------------ интерфейс
const listEl = document.getElementById('list');
const autoEl = document.getElementById('auto');
autoEl.onchange = () => chars.forEach(c => (c.auto = autoEl.checked));

function buildPanel() {
  listEl.innerHTML = '';
  chars.forEach((c, i) => {
    const b = document.createElement('button');
    b.className = 'char'; b.textContent = c.title;
    b.onclick = () => select(i);
    const clips = document.createElement('div');
    clips.className = 'clips';
    c.clips.forEach((n, k) => {
      const cb = document.createElement('span');
      cb.className = 'clip'; cb.textContent = n;
      cb.onclick = () => { c.auto = false; play(c, k); };
      clips.appendChild(cb);
    });
    c.ui = { b, clips };
    listEl.append(b, clips);
  });
  refreshPanel();
}

function refreshPanel() {
  chars.forEach((c, i) => {
    if (!c.ui) return;
    c.ui.b.classList.toggle('sel', i === selected);
    [...c.ui.clips.children].forEach((el, k) => el.classList.toggle('on', k === c.cur));
  });
}

let fly = null;
function select(i) {
  selected = i;
  const c = chars[i];
  const p = c.root.position, s = c.root.scale.y;
  fly = { pos: new THREE.Vector3(p.x - 1.4, p.y + 1.6 * s, p.z + 4.2 * s), target: new THREE.Vector3(p.x, p.y + 1.05 * s, p.z) };
  refreshPanel();
}

function applyParams() {
  // ?char=DarthVader&clip=Attack1&t=0.45&freeze — поза для скриншота
  for (const c of chars) {
    const want = params.get('clip_' + c.name) || (params.get('char') === c.name ? params.get('clip') : params.get('all'));
    if (want && c.clips.includes(want)) {
      c.auto = false;
      play(c, c.clips.indexOf(want));
      const a = c.actions[want];
      const t = parseFloat(params.get('t_' + c.name) || params.get('t') || '0.4');
      if (FREEZE) { c.mixer.stopAllAction(); a.reset(); a.play(); }
      a.time = t * a.getClip().duration;
      c.mixer.update(0);
    }
  }
  const shots = {
    wide: [[-2.4, 4.2, 13.5], [-2.4, 1.1, -2.5]],
    sith: [[-2.0, 2.0, 8.6], [-1.6, 1.1, 0]],
    troopers: [[-2.6, 3.0, 0.4], [-1.0, 1.4, -6]],
    weapons: [[0, 1.9, 2.2], [0, 1.1, -2.5]],
    patrol: [[-9, 2.2, 13], [-6, 1.0, 5]],
    overview: [[-16, 10, 18], [0, 0.5, -4]],
  };
  if (SHOT && shots[SHOT]) {
    camera.position.set(...shots[SHOT][0]);
    controls.target.set(...shots[SHOT][1]);
  }
  const focus = params.get('focus');
  if (focus) {
    const i = chars.findIndex(c => c.name === focus);
    if (i >= 0) {
      select(i);
      camera.position.copy(fly.pos); controls.target.copy(fly.target); fly = null;
    }
  }
}

// ------------------------------------------------------------------ цикл
const clock = new THREE.Clock();
const tmp = new THREE.Vector3();
function frame() {
  const dt = Math.min(clock.getDelta(), 0.05);
  const step = FREEZE ? 0 : dt;
  for (const c of chars) {
    c.t += step;
    c.mixer.update(step);
    const clip = c.clips[c.cur];
    const dur = c.actions[clip].getClip().duration;
    const loop = c.info.clips[clip].loop;
    if (c.auto && !FREEZE && c.t > (loop ? Math.max(4, dur) : dur + 0.6)) play(c, c.cur + 1);
    if (clip === 'Fire' && !c.fired && c.t > 0.02) { c.fired = true; fireBolt(c); }
    const tclip = FREEZE ? c.actions[clip].time : c.t;
    lightning(c, clip === 'ForceLightning' && tclip > 0.4 && tclip < 2.1);
    for (const bl of c.blades) bl.light.intensity = 6 * Math.min(1, bl.bone.scale.y) * (0.9 + 0.1 * Math.random());
  }
  for (const p of patrols) {
    const c = p.c;
    c.mixer.update(step);
    const to = tmp.copy(p.pts[p.target]).sub(c.root.position); to.y = 0;
    if (to.length() < 0.3) { p.target = (p.target + 1) % p.pts.length; continue; }
    const yaw = Math.atan2(to.x, to.z);
    let d = yaw - c.root.rotation.y;
    d = Math.atan2(Math.sin(d), Math.cos(d));
    c.root.rotation.y += Math.sign(d) * Math.min(Math.abs(d), 4 * step);
    c.root.position.x += Math.sin(c.root.rotation.y) * p.speed * step;
    c.root.position.z += Math.cos(c.root.rotation.y) * p.speed * step;
  }
  for (let i = bolts.length - 1; i >= 0; i--) {
    const b = bolts[i];
    b.m.position.addScaledVector(b.v, dt);
    if ((b.life -= dt) <= 0) { scene.remove(b.m); bolts.splice(i, 1); }
  }
  if (fly) {
    const k = 1 - Math.exp(-5 * dt);
    camera.position.lerp(fly.pos, k);
    controls.target.lerp(fly.target, k);
    if (camera.position.distanceTo(fly.pos) < 0.02) fly = null;
  }
  controls.update();
  composer.render();
  // подписи
  for (const c of chars) {
    tmp.copy(c.root.position); tmp.y += 2.3 * c.root.scale.y;
    tmp.project(camera);
    const vis = tmp.z < 1 && Math.abs(tmp.x) < 1.1 && Math.abs(tmp.y) < 1.1;
    c.tag.style.display = vis ? '' : 'none';
    if (vis) {
      c.tag.style.left = ((tmp.x + 1) / 2 * innerWidth) + 'px';
      c.tag.style.top = ((1 - tmp.y) / 2 * innerHeight) + 'px';
    }
  }
  requestAnimationFrame(frame);
}

load().catch(e => { document.getElementById('loading').textContent = 'Ошибка загрузки: ' + e.message + ' (откройте через start_server)'; console.error(e); });
frame();
