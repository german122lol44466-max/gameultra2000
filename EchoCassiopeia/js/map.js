// Сетка карты, столкновения, видимость и поиск пути
'use strict';

// 0 — стена/космос, 1 — пол, 2 — препятствие (пол, но нельзя пройти)
const GRID = new Uint8Array(MAP_W * MAP_H);
const ROOM_AT = new Array(MAP_W * MAP_H).fill(null);

(function buildGrid() {
  const fill = (x, y, w, h, v, room) => {
    for (let j = y; j < y + h; j++)
      for (let i = x; i < x + w; i++) {
        if (i < 0 || j < 0 || i >= MAP_W || j >= MAP_H) continue;
        GRID[j * MAP_W + i] = v;
        if (room !== undefined) ROOM_AT[j * MAP_W + i] = room;
      }
  };
  for (const c of CORRIDORS) fill(c[0], c[1], c[2], c[3], 1, null);
  for (const r of ROOMS) fill(r.x, r.y, r.w, r.h, 1, r.id);
  for (const p of PROPS) fill(p.x, p.y, p.w, p.h, 2);
})();

function cellAt(i, j) {
  if (i < 0 || j < 0 || i >= MAP_W || j >= MAP_H) return 0;
  return GRID[j * MAP_W + i];
}
function isFloor(i, j) { return cellAt(i, j) !== 0; }
function isWalkable(i, j) { return cellAt(i, j) === 1; }
function roomAtPx(x, y) {
  const i = Math.floor(x / TILE), j = Math.floor(y / TILE);
  if (i < 0 || j < 0 || i >= MAP_W || j >= MAP_H) return null;
  return ROOM_AT[j * MAP_W + i];
}
function roomName(id) { const r = roomById(id); return r ? r.name : 'коридор'; }

// Пересекает ли круг непроходимую клетку
function blockedCircle(x, y, r) {
  const i0 = Math.floor((x - r) / TILE), i1 = Math.floor((x + r) / TILE);
  const j0 = Math.floor((y - r) / TILE), j1 = Math.floor((y + r) / TILE);
  for (let j = j0; j <= j1; j++)
    for (let i = i0; i <= i1; i++) {
      if (isWalkable(i, j)) continue;
      const cx = clamp(x, i * TILE, (i + 1) * TILE);
      const cy = clamp(y, j * TILE, (j + 1) * TILE);
      if ((x - cx) ** 2 + (y - cy) ** 2 < r * r) return true;
    }
  return false;
}

// Прямая видимость (препятствия обзор не закрывают)
function lineOfSight(x1, y1, x2, y2) {
  const d = Math.hypot(x2 - x1, y2 - y1);
  const steps = Math.ceil(d / 10);
  for (let s = 1; s < steps; s++) {
    const t = s / steps;
    if (!isFloor(Math.floor((x1 + (x2 - x1) * t) / TILE), Math.floor((y1 + (y2 - y1) * t) / TILE))) return false;
  }
  return true;
}

// Полигон обзора: лучи до стен
function visibilityPolygon(x, y, radius, rays = 220) {
  const pts = [];
  for (let k = 0; k < rays; k++) {
    const a = (k / rays) * Math.PI * 2;
    const dx = Math.cos(a), dy = Math.sin(a);
    let d = 0;
    const step = 6;
    while (d < radius) {
      d += step;
      if (!isFloor(Math.floor((x + dx * d) / TILE), Math.floor((y + dy * d) / TILE))) { d = Math.max(0, d - step / 2) + 14; break; }
    }
    d = Math.min(d, radius);
    pts.push(x + dx * d, y + dy * d);
  }
  return pts;
}

// A* по клеткам, 8 направлений без срезания углов
function findPath(sx, sy, tx, ty) {
  let si = Math.floor(sx / TILE), sj = Math.floor(sy / TILE);
  let ti = Math.floor(tx / TILE), tj = Math.floor(ty / TILE);
  if (!isWalkable(ti, tj)) { const n = nearestWalkable(ti, tj); if (!n) return null; ti = n[0]; tj = n[1]; }
  if (!isWalkable(si, sj)) { const n = nearestWalkable(si, sj); if (!n) return null; si = n[0]; sj = n[1]; }
  const N = MAP_W * MAP_H;
  const g = new Float32Array(N).fill(Infinity);
  const came = new Int32Array(N).fill(-1);
  const closed = new Uint8Array(N);
  const start = sj * MAP_W + si, goal = tj * MAP_W + ti;
  const open = [start];
  g[start] = 0;
  const h = (k) => { const i = k % MAP_W, j = (k / MAP_W) | 0; const dx = Math.abs(i - ti), dy = Math.abs(j - tj); return Math.max(dx, dy) + 0.414 * Math.min(dx, dy); };
  const f = new Float32Array(N).fill(Infinity);
  f[start] = h(start);
  while (open.length) {
    let bi = 0;
    for (let k = 1; k < open.length; k++) if (f[open[k]] < f[open[bi]]) bi = k;
    const cur = open[bi];
    open[bi] = open[open.length - 1]; open.pop();
    if (cur === goal) break;
    if (closed[cur]) continue;
    closed[cur] = 1;
    const ci = cur % MAP_W, cj = (cur / MAP_W) | 0;
    for (let dj = -1; dj <= 1; dj++)
      for (let di = -1; di <= 1; di++) {
        if (!di && !dj) continue;
        const ni = ci + di, nj = cj + dj;
        if (!isWalkable(ni, nj)) continue;
        if (di && dj && (!isWalkable(ci + di, cj) || !isWalkable(ci, cj + dj))) continue;
        const nk = nj * MAP_W + ni;
        if (closed[nk]) continue;
        // держимся подальше от стен: штраф у краёв
        let pen = 0;
        if (!isWalkable(ni + 1, nj) || !isWalkable(ni - 1, nj) || !isWalkable(ni, nj + 1) || !isWalkable(ni, nj - 1)) pen = 0.6;
        const ng = g[cur] + (di && dj ? 1.414 : 1) + pen;
        if (ng < g[nk]) { g[nk] = ng; came[nk] = cur; f[nk] = ng + h(nk); open.push(nk); }
      }
  }
  if (came[goal] === -1 && goal !== start) return null;
  const path = [];
  let k = goal;
  while (k !== -1 && k !== start) { path.push({ x: (k % MAP_W + 0.5) * TILE, y: (((k / MAP_W) | 0) + 0.5) * TILE }); k = came[k]; }
  path.reverse();
  if (path.length) { path[path.length - 1] = { x: tx, y: ty }; }
  return path;
}

function nearestWalkable(i, j) {
  for (let r = 1; r < 6; r++)
    for (let dj = -r; dj <= r; dj++)
      for (let di = -r; di <= r; di++)
        if (isWalkable(i + di, j + dj)) return [i + di, j + dj];
  return null;
}

// Точка рядом со станцией, на которой можно стоять
function standPoint(tx, ty) {
  const i = Math.floor(tx), j = Math.floor(ty);
  if (isWalkable(i, j)) return { x: (i + 0.5) * TILE, y: (j + 0.5) * TILE };
  const n = nearestWalkable(i, j);
  return { x: (n[0] + 0.5) * TILE, y: (n[1] + 0.5) * TILE };
}

function randomRoomPoint(roomId) {
  const r = roomId ? roomById(roomId) : pick(ROOMS);
  for (let t = 0; t < 40; t++) {
    const i = r.x + 1 + ((Math.random() * (r.w - 2)) | 0);
    const j = r.y + 1 + ((Math.random() * (r.h - 2)) | 0);
    if (isWalkable(i, j)) return { x: (i + 0.5) * TILE, y: (j + 0.5) * TILE, room: r.id };
  }
  return { x: (r.x + r.w / 2) * TILE, y: (r.y + r.h / 2) * TILE, room: r.id };
}
