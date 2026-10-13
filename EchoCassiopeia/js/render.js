// Отрисовка: персонажи, карта, эффекты
'use strict';

function shade(hex, amt) {
  let c = hex.replace('#', '');
  if (c.length === 3) c = c.split('').map(x => x + x).join('');
  const n = parseInt(c, 16);
  let r = (n >> 16) & 255, g = (n >> 8) & 255, b = n & 255;
  if (amt >= 0) { r += (255 - r) * amt; g += (255 - g) * amt; b += (255 - b) * amt; }
  else { r *= 1 + amt; g *= 1 + amt; b *= 1 + amt; }
  return `rgb(${r | 0},${g | 0},${b | 0})`;
}

// Смотритель: корпус-капсула, купол шлема, антенна, ранец
// (x, y) — точка между ступнями; размер ~ 46 px при s = 1
function drawCrew(ctx, x, y, color, o = {}) {
  const s = o.scale || 1;
  const facing = o.facing || 1;
  const ph = o.walk || 0;
  ctx.save();
  ctx.translate(x, y);
  ctx.scale(s * facing, s);
  if (o.ghost) ctx.globalAlpha *= 0.45;
  const bob = Math.abs(Math.sin(ph)) * 2;

  // тень
  if (!o.ghost) {
    ctx.fillStyle = 'rgba(0,0,0,0.35)';
    ctx.beginPath(); ctx.ellipse(0, 0, 16, 5, 0, 0, Math.PI * 2); ctx.fill();
  }
  // ноги
  const lf = Math.sin(ph) * 5, rf = -Math.sin(ph) * 5;
  ctx.fillStyle = color.dark;
  if (!o.ghost) {
    roundRect(ctx, -11 + lf, -9, 9, 9, 3); ctx.fill();
    roundRect(ctx, 2 + rf, -9, 9, 9, 3); ctx.fill();
  }
  ctx.translate(0, -bob);
  // ранец
  ctx.fillStyle = color.dark;
  roundRect(ctx, -19, -32, 8, 18, 3); ctx.fill();
  ctx.fillStyle = shade(color.body, 0.35);
  ctx.fillRect(-17, -29, 4, 3);
  // корпус
  const gb = ctx.createLinearGradient(-14, 0, 14, 0);
  gb.addColorStop(0, shade(color.body, -0.25));
  gb.addColorStop(0.45, color.body);
  gb.addColorStop(1, shade(color.body, -0.35));
  ctx.fillStyle = gb;
  ctx.strokeStyle = 'rgba(0,0,0,0.65)';
  ctx.lineWidth = 2;
  roundRect(ctx, -14, -30, 28, 24, 9); ctx.fill(); ctx.stroke();
  if (o.ghost) {
    // хвост призрака
    ctx.beginPath();
    ctx.moveTo(-14, -10);
    for (let k = 0; k <= 4; k++) ctx.lineTo(-14 + k * 7, -2 + (k % 2 ? -5 : 2) + Math.sin(ph * 2 + k) * 2);
    ctx.lineTo(14, -10); ctx.closePath(); ctx.fill();
  }
  // пояс
  ctx.fillStyle = 'rgba(0,0,0,0.25)';
  ctx.fillRect(-14, -14, 28, 3);
  ctx.fillStyle = shade(color.body, 0.5);
  ctx.fillRect(4, -22, 5, 4);
  // шлем-купол
  const hg = ctx.createRadialGradient(-4, -44, 2, 0, -38, 16);
  hg.addColorStop(0, shade(color.body, 0.25));
  hg.addColorStop(1, shade(color.body, -0.15));
  ctx.fillStyle = hg;
  ctx.beginPath(); ctx.arc(0, -38, 14, 0, Math.PI * 2); ctx.fill(); ctx.stroke();
  // визор
  const vg = ctx.createLinearGradient(0, -46, 0, -32);
  if (o.echoEyes) { vg.addColorStop(0, '#3a0d4a'); vg.addColorStop(1, '#a24bff'); }
  else { vg.addColorStop(0, '#16243c'); vg.addColorStop(1, '#3d6ea8'); }
  ctx.fillStyle = vg;
  ctx.beginPath(); ctx.ellipse(4, -38, 9, 7, 0, 0, Math.PI * 2); ctx.fill();
  ctx.strokeStyle = 'rgba(0,0,0,0.6)'; ctx.lineWidth = 1.5; ctx.stroke();
  ctx.fillStyle = 'rgba(255,255,255,0.75)';
  ctx.beginPath(); ctx.ellipse(7, -41, 3.2, 1.8, -0.4, 0, Math.PI * 2); ctx.fill();
  if (o.echoEyes) {
    ctx.fillStyle = '#f3d9ff';
    ctx.fillRect(1, -39, 3, 2); ctx.fillRect(7, -39, 3, 2);
  }
  // антенна
  ctx.strokeStyle = color.dark; ctx.lineWidth = 2;
  ctx.beginPath(); ctx.moveTo(-5, -50); ctx.lineTo(-8, -58); ctx.stroke();
  ctx.fillStyle = o.echoEyes ? '#c77dff' : shade(color.body, 0.6);
  ctx.beginPath(); ctx.arc(-8, -59, 3, 0, Math.PI * 2); ctx.fill();
  ctx.restore();
}

// Кристаллизованное тело
function drawBody(ctx, x, y, color, t = 0) {
  ctx.save();
  ctx.translate(x, y);
  ctx.fillStyle = 'rgba(0,0,0,0.35)';
  ctx.beginPath(); ctx.ellipse(0, 4, 26, 8, 0, 0, Math.PI * 2); ctx.fill();
  ctx.fillStyle = color.body;
  ctx.strokeStyle = 'rgba(0,0,0,0.6)'; ctx.lineWidth = 2;
  roundRect(ctx, -22, -10, 30, 16, 7); ctx.fill(); ctx.stroke();
  ctx.fillStyle = color.dark;
  ctx.beginPath(); ctx.arc(14, -3, 10, 0, Math.PI * 2); ctx.fill(); ctx.stroke();
  // кристаллы
  const shards = [[-14, -10, 7, 20], [-4, -12, 6, 26], [6, -9, 5, 17], [16, -10, 6, 22], [-20, -4, 4, 12], [2, -2, 4, 12]];
  for (let k = 0; k < shards.length; k++) {
    const [sx, sy, w, h] = shards[k];
    const glow = 0.6 + 0.4 * Math.sin(t * 2 + k);
    const g = ctx.createLinearGradient(sx, sy - h, sx, sy);
    g.addColorStop(0, `rgba(240,220,255,${glow})`);
    g.addColorStop(1, 'rgba(140,70,230,0.9)');
    ctx.fillStyle = g;
    ctx.beginPath();
    ctx.moveTo(sx - w / 2, sy); ctx.lineTo(sx, sy - h); ctx.lineTo(sx + w / 2, sy); ctx.closePath();
    ctx.fill();
    ctx.strokeStyle = 'rgba(80,30,140,0.8)'; ctx.lineWidth = 1; ctx.stroke();
  }
  ctx.restore();
}

function roundRect(ctx, x, y, w, h, r) {
  ctx.beginPath();
  ctx.moveTo(x + r, y);
  ctx.arcTo(x + w, y, x + w, y + h, r);
  ctx.arcTo(x + w, y + h, x, y + h, r);
  ctx.arcTo(x, y + h, x, y, r);
  ctx.arcTo(x, y, x + w, y, r);
  ctx.closePath();
}

// Звёздное небо (для меню и сцен)
function makeStars(n, w, h) {
  const a = [];
  for (let i = 0; i < n; i++) a.push({ x: Math.random() * w, y: Math.random() * h, z: Math.random(), tw: Math.random() * 6 });
  return a;
}
function drawStars(ctx, stars, w, h, t, vx = 0) {
  for (const s of stars) {
    let x = (s.x - t * vx * (0.3 + s.z)) % w; if (x < 0) x += w;
    const a = 0.35 + 0.65 * Math.abs(Math.sin(t * 0.8 + s.tw)) * s.z;
    ctx.fillStyle = `rgba(220,230,255,${a})`;
    const r = 0.5 + s.z * 1.4;
    ctx.fillRect(x, s.y, r, r);
  }
}
function drawPlanet(ctx, x, y, r, t) {
  const ring = (a0, a1) => {
    ctx.save();
    ctx.strokeStyle = 'rgba(255,220,190,0.35)'; ctx.lineWidth = r * 0.06;
    ctx.beginPath(); ctx.ellipse(x, y, r * 1.7, r * 0.32, -0.25, a0, a1); ctx.stroke();
    ctx.restore();
  };
  ring(Math.PI, Math.PI * 2); // задняя половина кольца
  ctx.save();
  const g = ctx.createRadialGradient(x - r * 0.4, y - r * 0.4, r * 0.1, x, y, r);
  g.addColorStop(0, '#f2b880'); g.addColorStop(0.5, '#b0563c'); g.addColorStop(1, '#3a1220');
  ctx.fillStyle = g;
  ctx.beginPath(); ctx.arc(x, y, r, 0, Math.PI * 2); ctx.fill();
  ctx.clip();
  for (let k = 0; k < 9; k++) {
    ctx.fillStyle = `rgba(${k % 2 ? '255,210,170' : '90,20,40'},0.12)`;
    const yy = y - r + (k + 0.5) * (2 * r / 9) + Math.sin(t * 0.2 + k) * 4;
    ctx.fillRect(x - r, yy, r * 2, r / 12 + (k % 3) * 3);
  }
  ctx.restore();
  ring(0, Math.PI); // передняя половина
}

// ---------- Статичная карта ----------
let MAP_CANVAS = null;

function buildMapCanvas() {
  const c = document.createElement('canvas');
  c.width = MAP_W * TILE; c.height = MAP_H * TILE;
  const g = c.getContext('2d');
  g.fillStyle = '#05060c';
  g.fillRect(0, 0, c.width, c.height);
  // пол
  for (let j = 0; j < MAP_H; j++)
    for (let i = 0; i < MAP_W; i++) {
      if (!isFloor(i, j)) continue;
      const rid = ROOM_AT[j * MAP_W + i];
      const room = rid ? roomById(rid) : null;
      const base = room ? room.floor : '#262a33';
      g.fillStyle = ((i + j) & 1) ? shade(base, 0.03) : base;
      g.fillRect(i * TILE, j * TILE, TILE, TILE);
      g.strokeStyle = 'rgba(255,255,255,0.04)';
      g.strokeRect(i * TILE + 0.5, j * TILE + 0.5, TILE - 1, TILE - 1);
      if (!room) { // коридор: заклёпки
        g.fillStyle = 'rgba(255,255,255,0.06)';
        g.fillRect(i * TILE + 6, j * TILE + 6, 3, 3); g.fillRect(i * TILE + TILE - 9, j * TILE + TILE - 9, 3, 3);
      }
    }
  // стены
  for (let j = 0; j < MAP_H; j++)
    for (let i = 0; i < MAP_W; i++) {
      if (isFloor(i, j)) continue;
      let near = false;
      for (let dj = -1; dj <= 1; dj++) for (let di = -1; di <= 1; di++) if (isFloor(i + di, j + dj)) near = true;
      if (!near) continue;
      const x = i * TILE, y = j * TILE;
      g.fillStyle = '#1b2030';
      g.fillRect(x, y, TILE, TILE);
      g.fillStyle = '#2a3247';
      g.fillRect(x + 3, y + 3, TILE - 6, TILE - 6);
      // кромки к полу
      g.fillStyle = '#5b6b8f';
      if (isFloor(i, j + 1)) { g.fillRect(x, y + TILE - 6, TILE, 6); g.fillStyle = '#2d354c'; g.fillRect(x, y + TILE - 18, TILE, 12); g.fillStyle = '#5b6b8f'; }
      if (isFloor(i, j - 1)) g.fillRect(x, y, TILE, 3);
      if (isFloor(i + 1, j)) g.fillRect(x + TILE - 3, y, 3, TILE);
      if (isFloor(i - 1, j)) g.fillRect(x, y, 3, TILE);
    }
  // полосы опасности у входов в комнаты
  for (const r of ROOMS) {
    g.strokeStyle = shade(r.accent, -0.3);
    g.globalAlpha = 0.5; g.lineWidth = 3;
    g.strokeRect(r.x * TILE + 4, r.y * TILE + 4, r.w * TILE - 8, r.h * TILE - 8);
    g.globalAlpha = 1;
  }
  // препятствия
  for (const p of PROPS) drawProp(g, p);
  // вентиляция
  for (const v of VENTS) drawVent(g, (v.x + 0.5) * TILE, (v.y + 0.5) * TILE, false);
  // станции
  for (const s of STATIONS) drawStation(g, s);
  for (const k in FIXES) drawFixPanel(g, FIXES[k]);
  // названия комнат на полу
  g.textAlign = 'center'; g.textBaseline = 'middle';
  g.font = 'bold 30px "Segoe UI", Arial, sans-serif';
  for (const r of ROOMS) {
    g.fillStyle = 'rgba(255,255,255,0.07)';
    g.fillText(r.name.toUpperCase(), (r.x + r.w / 2) * TILE, (r.y + r.h - 1) * TILE);
  }
  MAP_CANVAS = c;
}

function drawProp(g, p) {
  const x = p.x * TILE, y = p.y * TILE, w = p.w * TILE, h = p.h * TILE;
  g.save();
  if (p.type === 'core') {
    g.fillStyle = '#2a1416'; roundRect(g, x + 4, y + 4, w - 8, h - 8, 18); g.fill();
    const cx = x + w / 2, cy = y + h / 2;
    const rg = g.createRadialGradient(cx, cy, 5, cx, cy, w / 2);
    rg.addColorStop(0, '#fff3c4'); rg.addColorStop(0.35, '#ff8a3d'); rg.addColorStop(1, 'rgba(120,20,10,0)');
    g.fillStyle = rg; g.beginPath(); g.arc(cx, cy, w / 2 - 6, 0, Math.PI * 2); g.fill();
    g.strokeStyle = '#6b3b2a'; g.lineWidth = 6; g.beginPath(); g.arc(cx, cy, w / 2 - 10, 0, Math.PI * 2); g.stroke();
  } else if (p.type === 'engine') {
    g.fillStyle = '#3d3a44'; roundRect(g, x + 2, y + 6, w - 4, h - 12, 10); g.fill();
    g.fillStyle = '#56525e'; for (let k = 0; k < 5; k++) g.fillRect(x + 12 + k * 26, y + 12, 10, h - 24);
    g.fillStyle = '#ffb547'; g.fillRect(x + w - 16, y + h / 2 - 6, 10, 12);
  } else if (p.type === 'button') {
    g.fillStyle = '#4a4f63'; g.beginPath(); g.arc(x + w / 2, y + h / 2, w / 2 - 6, 0, Math.PI * 2); g.fill();
    g.fillStyle = '#9aa0b8'; g.beginPath(); g.arc(x + w / 2, y + h / 2, w / 2 - 14, 0, Math.PI * 2); g.fill();
    g.fillStyle = '#e5484d'; g.beginPath(); g.arc(x + w / 2, y + h / 2 - 3, 16, 0, Math.PI * 2); g.fill();
    g.fillStyle = '#ff9a9d'; g.beginPath(); g.arc(x + w / 2 - 5, y + h / 2 - 8, 5, 0, Math.PI * 2); g.fill();
  } else if (p.type === 'table') {
    g.fillStyle = '#5a6078'; g.beginPath(); g.arc(x + w / 2, y + h / 2, w / 2 - 6, 0, Math.PI * 2); g.fill();
    g.fillStyle = '#6e7592'; g.beginPath(); g.arc(x + w / 2, y + h / 2 - 3, w / 2 - 10, 0, Math.PI * 2); g.fill();
  } else if (p.type === 'bench') {
    g.fillStyle = '#4e4766'; roundRect(g, x + 2, y + 6, w - 4, h - 10, 8); g.fill();
    g.fillStyle = '#6f6690'; roundRect(g, x + 6, y + 6, w - 12, h - 22, 6); g.fill();
    for (let k = 0; k < 4; k++) { g.fillStyle = ['#c58bff', '#59c7ff', '#7be07b', '#f06aa8'][k]; g.fillRect(x + 20 + k * 52, y + 14, 12, 18); }
  } else if (p.type === 'crate') {
    g.fillStyle = '#7a5c34'; g.fillRect(x + 3, y + 3, w - 6, h - 6);
    g.strokeStyle = '#4b381f'; g.lineWidth = 4; g.strokeRect(x + 5, y + 5, w - 10, h - 10);
    g.beginPath(); g.moveTo(x + 5, y + 5); g.lineTo(x + w - 5, y + h - 5); g.stroke();
  } else if (p.type === 'plant') {
    g.fillStyle = '#4a3b2a'; g.beginPath(); g.arc(x + w / 2, y + h / 2, w / 2 - 8, 0, Math.PI * 2); g.fill();
    for (let k = 0; k < 8; k++) {
      const a = k / 8 * Math.PI * 2;
      g.fillStyle = k % 2 ? '#3fae5a' : '#69d27e';
      g.beginPath(); g.ellipse(x + w / 2 + Math.cos(a) * 18, y + h / 2 + Math.sin(a) * 18, 16, 8, a, 0, Math.PI * 2); g.fill();
    }
    g.fillStyle = '#e8ff9a'; g.beginPath(); g.arc(x + w / 2, y + h / 2, 7, 0, Math.PI * 2); g.fill();
  } else if (p.type === 'console') {
    g.fillStyle = '#2f3a55'; roundRect(g, x + 2, y + 4, w - 4, h - 4, 6); g.fill();
    g.fillStyle = '#9fd8ff'; g.fillRect(x + 12, y + 10, w - 24, h - 22);
    g.fillStyle = '#2b3348'; for (let k = 0; k < 6; k++) g.fillRect(x + 20 + k * 20, y + 14, 8, 4);
  } else if (p.type === 'dish') {
    g.fillStyle = '#3a4658'; g.beginPath(); g.arc(x + w / 2, y + h / 2, w / 2 - 4, 0, Math.PI * 2); g.fill();
    g.strokeStyle = '#8fb3d9'; g.lineWidth = 3;
    for (let k = 1; k < 4; k++) { g.beginPath(); g.arc(x + w / 2, y + h / 2, k * 11, 0, Math.PI * 2); g.stroke(); }
  } else if (p.type === 'bed') {
    g.fillStyle = '#d6e4ea'; roundRect(g, x + 4, y + 6, w - 8, h - 12, 8); g.fill();
    g.fillStyle = '#5ff2c2'; g.fillRect(x + 10, y + 12, 18, h - 24);
  }
  g.restore();
}

function drawVent(g, x, y, open) {
  g.save();
  g.fillStyle = '#1a1d24'; roundRect(g, x - 18, y - 12, 36, 24, 4); g.fill();
  g.strokeStyle = '#596173'; g.lineWidth = 2; g.stroke();
  g.fillStyle = '#596173';
  if (!open) for (let k = -2; k <= 2; k++) g.fillRect(x - 14, y + k * 4 - 1, 28, 2);
  g.restore();
}

function drawStation(g, s) {
  const x = (s.x + 0.5) * TILE, y = (s.y + 0.5) * TILE;
  const room = roomById(s.room);
  g.save();
  if (s.type === 'scan') {
    g.strokeStyle = room.accent; g.lineWidth = 3;
    g.fillStyle = 'rgba(95,242,194,0.15)';
    g.beginPath(); g.ellipse(x, y + 6, 22, 12, 0, 0, Math.PI * 2); g.fill(); g.stroke();
  } else {
    g.fillStyle = '#20263a'; roundRect(g, x - 16, y - 20, 32, 26, 4); g.fill();
    g.strokeStyle = '#4b5778'; g.lineWidth = 2; g.stroke();
    g.fillStyle = room.accent; g.globalAlpha = 0.8; g.fillRect(x - 11, y - 15, 22, 13); g.globalAlpha = 1;
    g.fillStyle = '#4b5778'; g.fillRect(x - 4, y + 6, 8, 10);
  }
  g.restore();
}

function drawFixPanel(g, f) {
  const x = (f.x + 0.5) * TILE, y = (f.y + 0.5) * TILE;
  g.save();
  g.fillStyle = '#2b2020'; roundRect(g, x - 15, y - 18, 30, 30, 4); g.fill();
  g.strokeStyle = '#8a4a4a'; g.lineWidth = 2; g.stroke();
  g.fillStyle = '#f5c518';
  g.beginPath(); g.moveTo(x, y - 12); g.lineTo(x + 9, y + 5); g.lineTo(x - 9, y + 5); g.closePath(); g.fill();
  g.fillStyle = '#2b2020'; g.fillRect(x - 1, y - 6, 2, 6); g.fillRect(x - 1, y + 1, 2, 2);
  g.restore();
}

// Мини-карта
function drawMinimap(ctx, w, h, opts) {
  const sc = Math.min(w / (MAP_W * TILE), h / (MAP_H * TILE));
  const ox = (w - MAP_W * TILE * sc) / 2, oy = (h - MAP_H * TILE * sc) / 2;
  const cs = TILE * sc;
  ctx.save();
  ctx.translate(ox, oy);
  for (let j = 0; j < MAP_H; j++)
    for (let i = 0; i < MAP_W; i++) {
      if (!isFloor(i, j)) continue;
      const rid = ROOM_AT[j * MAP_W + i];
      ctx.fillStyle = rid ? 'rgba(140,170,230,0.55)' : 'rgba(110,125,160,0.4)';
      ctx.fillRect(i * cs, j * cs, cs + 0.5, cs + 0.5);
    }
  ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
  ctx.font = `bold ${Math.max(10, cs * 0.9)}px "Segoe UI", Arial`;
  for (const r of ROOMS) {
    ctx.fillStyle = 'rgba(255,255,255,0.9)';
    ctx.fillText(r.name, (r.x + r.w / 2) * cs, (r.y + r.h / 2) * cs);
  }
  if (opts.tasks) for (const s of opts.tasks) {
    ctx.fillStyle = '#ffd84a';
    ctx.beginPath(); ctx.arc((s.x + 0.5) * cs, (s.y + 0.5) * cs, cs * 0.45, 0, Math.PI * 2); ctx.fill();
    ctx.strokeStyle = '#000'; ctx.lineWidth = 1; ctx.stroke();
  }
  if (opts.sab) {
    const f = FIXES[opts.sab];
    if ((opts.t * 2 | 0) % 2) {
      ctx.fillStyle = '#ff3b3b';
      ctx.beginPath(); ctx.arc((f.x + 0.5) * cs, (f.y + 0.5) * cs, cs * 0.8, 0, Math.PI * 2); ctx.fill();
    }
  }
  if (opts.vents) for (const v of VENTS) {
    ctx.strokeStyle = '#c77dff'; ctx.lineWidth = 2;
    ctx.strokeRect((v.x + 0.2) * cs, (v.y + 0.25) * cs, cs * 0.6, cs * 0.5);
  }
  if (opts.players) for (const p of opts.players) {
    ctx.fillStyle = p.color.body;
    ctx.beginPath(); ctx.arc(p.x * sc, p.y * sc, cs * 0.55, 0, Math.PI * 2); ctx.fill();
    ctx.strokeStyle = '#fff'; ctx.lineWidth = 1.5; ctx.stroke();
  }
  if (opts.me) {
    ctx.fillStyle = opts.me.color.body;
    ctx.beginPath(); ctx.arc(opts.me.x * sc, opts.me.y * sc, cs * 0.8, 0, Math.PI * 2); ctx.fill();
    ctx.strokeStyle = '#fff'; ctx.lineWidth = 2.5; ctx.stroke();
  }
  ctx.restore();
}
