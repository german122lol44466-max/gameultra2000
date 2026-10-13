// Интерфейс, меню, сцены и главный цикл
'use strict';

const $ = id => document.getElementById(id);
const SETTINGS_KEY = 'echo_k9_settings';

function loadSettings() {
  try { return Object.assign({}, DEFAULT_SETTINGS, JSON.parse(localStorage.getItem(SETTINGS_KEY) || '{}')); }
  catch (e) { return Object.assign({}, DEFAULT_SETTINGS); }
}
function saveSettings(s) { try { localStorage.setItem(SETTINGS_KEY, JSON.stringify(s)); } catch (e) {} }

let settings = loadSettings();
let canvas, ctx, W = 0, H = 0, dpr = 1;
let menuStars = [];
let scene = { type: 'menu', t: 0 };

const UI = {
  show(id) {
    for (const s of document.querySelectorAll('.screen')) s.classList.add('hidden');
    if (id) $(id).classList.remove('hidden');
  },
  toast(msg) {
    const t = $('toast'); t.textContent = msg; t.classList.add('show');
    clearTimeout(UI._tt); UI._tt = setTimeout(() => t.classList.remove('show'), 2200);
  },
  showDeath(killer) {
    const d = $('death');
    d.classList.remove('hidden');
    const cv = $('death-canvas'), g = cv.getContext('2d');
    $('death-text').textContent = `${killer.name} оказался(ась) Эхо и скопировал(а) тебя.`;
    let t = 0;
    const anim = () => {
      t += 1 / 60;
      g.clearRect(0, 0, cv.width, cv.height);
      g.fillStyle = 'rgba(80,20,120,0.35)'; g.fillRect(0, 0, cv.width, cv.height);
      const k = Math.min(1, t / 0.6);
      drawCrew(g, 150 + k * 20, 210, killer.color, { scale: 2.4, echoEyes: true, facing: 1 });
      if (t < 0.6) drawCrew(g, 360, 210, G.me.color, { scale: 2.4, facing: -1 });
      else { g.save(); g.translate(370, 200); g.scale(2.2, 2.2); drawBody(g, 0, 0, G.me.color, t); g.restore(); }
      if (t > 0.5 && t < 0.9) { g.fillStyle = `rgba(230,200,255,${1 - (t - 0.5) / 0.4})`; g.fillRect(0, 0, cv.width, cv.height); }
      if (t < 2.6) requestAnimationFrame(anim);
      else { d.classList.add('hidden'); UI.toast('Ты стал(а) призраком: продолжай выполнять задания, чтобы помочь команде'); }
    };
    anim();
  },
  showMeetingSplash(caller, victimId, cb) {
    const s = $('splash');
    s.classList.remove('hidden');
    s.className = 'overlay ' + (victimId != null ? 'splash-body' : 'splash-button');
    $('splash-text').textContent = victimId != null ? 'ТЕЛО ОБНАРУЖЕНО!' : 'ЭКСТРЕННОЕ СОБРАНИЕ!';
    const cv = $('splash-canvas'), g = cv.getContext('2d');
    g.clearRect(0, 0, cv.width, cv.height);
    if (victimId != null) { drawBody(g, 160, 120, byId(victimId).color, 1); drawCrew(g, 70, 150, caller.color, { scale: 1.6 }); }
    else drawCrew(g, 160, 170, caller.color, { scale: 2 });
    setTimeout(() => { s.classList.add('hidden'); cb(); }, 2000);
  },
  showEject(p, cb) {
    const e = $('eject'); e.classList.remove('hidden');
    const cv = $('eject-canvas'); cv.width = W; cv.height = H;
    const g = cv.getContext('2d');
    const stars = makeStars(220, W, H);
    let text;
    if (!p) text = 'Никто не изгнан.';
    else if (G.settings.confirmEjects) text = p.isEcho ? `${p.name} — это Эхо.` : `${p.name} — не Эхо.`;
    else text = `${p.name} выброшен(а) в шлюз.`;
    const left = echoAlive().filter(q => q !== p).length;
    const sub = G.settings.confirmEjects ? `Осталось Эхо на станции: ${left}` : '';
    Sfx.eject();
    let t = 0, last = performance.now();
    const anim = (now) => {
      const dt = Math.min(0.05, (now - last) / 1000); last = now; t += dt;
      g.fillStyle = '#03040a'; g.fillRect(0, 0, W, H);
      drawStars(g, stars, W, H, t, 240);
      if (p) {
        const x = -100 + (W + 200) * Math.min(1, t / 4.5);
        g.save(); g.translate(x, H / 2); g.rotate(t * 2.2);
        drawCrew(g, 0, 40, p.color, { scale: 1.6 }); g.restore();
      }
      const n = Math.floor(Math.max(0, t - 1) * 22);
      g.fillStyle = '#fff'; g.font = `bold ${Math.round(Math.min(W / 20, 42))}px "Segoe UI", Arial`; g.textAlign = 'center';
      g.fillText(text.slice(0, n), W / 2, H / 2 + 120);
      if (n >= text.length && sub) { g.font = `${Math.round(Math.min(W / 32, 24))}px "Segoe UI", Arial`; g.fillStyle = '#b9c2dc'; g.fillText(sub, W / 2, H / 2 + 165); }
      if (t < 6) requestAnimationFrame(anim);
      else { e.classList.add('hidden'); cb(); }
    };
    requestAnimationFrame(anim);
  },
  showEnd(res) {
    hudVisible(false);
    const [title, text] = STORY.endings[res.reason];
    const e = $('end');
    UI.show('end');
    e.className = 'screen ' + (res.echoWin ? 'end-echo' : 'end-crew');
    $('end-verdict').textContent = res.meWin ? 'ПОБЕДА' : 'ПОРАЖЕНИЕ';
    $('end-title').textContent = title;
    $('end-text').textContent = text;
    const list = $('end-roles'); list.innerHTML = '';
    for (const p of G.players) {
      const d = document.createElement('div'); d.className = 'end-p' + (p.isEcho ? ' echo' : '');
      const cv = document.createElement('canvas'); cv.width = 70; cv.height = 80;
      drawCrew(cv.getContext('2d'), 35, 76, p.color, { scale: 1, echoEyes: p.isEcho, ghost: !p.alive });
      const n = document.createElement('div'); n.textContent = p.name;
      const r = document.createElement('small'); r.textContent = p.isEcho ? 'Эхо' : 'Смотритель';
      d.append(cv, n, r); list.appendChild(d);
    }
    res.meWin ? Sfx.win() : Sfx.lose();
    Sfx.humStop();
  },
};

// ---------- HUD ----------
function hudVisible(v) { $('hud').classList.toggle('hidden', !v); }
let hudCache = {};
function setText(id, text) { if (hudCache[id] !== text) { hudCache[id] = text; $(id).textContent = text; } }
function setHTML(id, html) { if (hudCache[id] !== html) { hudCache[id] = html; $(id).innerHTML = html; } }

function updateHUD() {
  const me = G.me;
  const commsDown = G.sab && G.sab.type === 'comms';
  $('taskbar-fill').style.width = (commsDown ? 0 : G.doneTasks / Math.max(1, G.totalTasks) * 100) + '%';
  setText('taskbar-label', commsDown ? 'ПРОГРЕСС: НЕТ СВЯЗИ' : `ПРОГРЕСС ЗАДАНИЙ: ${G.doneTasks} / ${G.totalTasks}`);
  let html = '';
  if (me.isEcho) {
    const mates = G.players.filter(p => p.isEcho && p !== me).map(p => p.name).join(', ');
    html += `<div class="tl-role echo">Ты — ЭХО${mates ? '<br><small>Свои: ' + mates + '</small>' : ''}</div>`;
    html += '<div class="tl-sub">Кристаллизуй смотрителей. Фальшивые задания:</div>';
  } else html += `<div class="tl-role">${me.alive ? 'Смотритель' : 'Призрак'}</div>`;
  if (commsDown && !me.isEcho) html += '<div class="tl-item bad">Связь потеряна — задания недоступны</div>';
  else for (const t of me.tasks) {
    const s = stationById(t.st);
    html += `<div class="tl-item${t.done ? ' done' : ''}">${roomName(s.room)}: ${s.name}</div>`;
  }
  if (G.sab) {
    const f = FIXES[G.sab.type];
    html += `<div class="tl-item bad">⚠ ${G.sab.type === 'reactor' ? 'Реактор: ' + Math.ceil(G.sab.timer) + ' с' : SABOTAGE_INFO[G.sab.type].name.replace('Отключить', 'Нет').replace('Заглушить', 'Нет')} — ${roomName(f.room)}, ${f.name}</div>`;
  }
  setHTML('tasklist', html);
  setText('room-label', me.inVent ? 'Вентиляция' : roomName(roomAtPx(me.x, me.y)));
  // авария
  const al = $('sab-alert');
  if (G.sab) {
    al.classList.remove('hidden');
    setText('sab-alert', G.sab.type === 'reactor' ? `${SABOTAGE_INFO.reactor.alert}: ${Math.ceil(G.sab.timer)} с` : SABOTAGE_INFO[G.sab.type].alert);
  } else al.classList.add('hidden');
  // кнопки
  const u = nearestUse();
  const useB = $('btn-use');
  useB.disabled = !u || (u.kind === 'task' && me.isEcho);
  const labels = { vent: 'ВЛЕЗТЬ', ventexit: 'ВЫЛЕЗТИ', fix: 'ПОЧИНИТЬ', button: 'СОБРАНИЕ', task: 'ДЕЙСТВИЕ' };
  setText('btn-use-label', u ? labels[u.kind] : 'ДЕЙСТВИЕ');
  $('btn-report').disabled = !nearestBody();
  $('btn-report').classList.toggle('hidden', !me.alive);
  const kb = $('btn-kill');
  kb.classList.toggle('hidden', !me.isEcho || !me.alive);
  if (me.isEcho) {
    kb.disabled = me.killCd > 0 || !nearestKillTarget(me) || me.inVent;
    setText('btn-kill-cd', me.killCd > 0 ? Math.ceil(me.killCd) : '');
  }
  $('btn-sab').classList.toggle('hidden', !me.isEcho || !me.alive);
  if (me.isEcho) setText('btn-sab-cd', G.sabCd > 0 ? Math.ceil(G.sabCd) : '');
  // вентиляция
  const vn = $('vent-nav');
  if (me.inVent) {
    vn.classList.remove('hidden');
    const net = VENTS.filter(v => v.net === VENTS[me.ventId].net && v.id !== me.ventId);
    setHTML('vent-nav', net.map((v, i) => `<button data-i="${i}">${i + 1}: ${roomName(v.room)}</button>`).join('') + '<button data-exit="1">E: вылезти</button>');
  } else { vn.classList.add('hidden'); hudCache['vent-nav'] = ''; }
}

// ---------- Меню ----------
function initMenu() {
  $('btn-play').addEventListener('click', () => { Sfx.unlock(); UI.show('setup'); fillSetup(); });
  $('btn-story').addEventListener('click', () => { Sfx.unlock(); UI.show('story'); playStory(); });
  $('btn-howto').addEventListener('click', () => { Sfx.unlock(); UI.show('howto'); });
  for (const b of document.querySelectorAll('[data-back]')) b.addEventListener('click', () => { Sfx.click(); UI.show('menu'); scene.type = 'menu'; });
  $('btn-start').addEventListener('click', () => { readSetup(); startGame(); });
  $('btn-again').addEventListener('click', () => startGame());
  $('btn-tomenu').addEventListener('click', () => { UI.show('menu'); scene.type = 'menu'; G.phase = 'menu'; });
  for (const b of document.querySelectorAll('button')) b.addEventListener('mouseenter', () => Sfx.hover());
}

function fillSetup() {
  $('s-name').value = settings.name;
  $('s-bots').value = settings.bots; $('s-echoes').value = settings.echoes;
  $('s-kill').value = settings.killCd; $('s-discuss').value = settings.discuss;
  $('s-tasks').value = settings.tasks; $('s-role').value = settings.role;
  $('s-diff').value = settings.difficulty; $('s-confirm').checked = settings.confirmEjects;
  $('s-volume').value = Math.round(settings.volume * 100);
  const cs = $('s-colors'); cs.innerHTML = '';
  for (const c of COLORS) {
    const b = document.createElement('button'); b.className = 'color-btn' + (c.id === settings.color ? ' sel' : '');
    b.title = c.name;
    const cv = document.createElement('canvas'); cv.width = 48; cv.height = 56;
    drawCrew(cv.getContext('2d'), 24, 54, c, { scale: 0.85 });
    b.appendChild(cv);
    b.addEventListener('click', () => { settings.color = c.id; cs.querySelectorAll('.sel').forEach(x => x.classList.remove('sel')); b.classList.add('sel'); Sfx.click(); });
    cs.appendChild(b);
  }
  const upd = () => {
    $('v-bots').textContent = $('s-bots').value; $('v-echoes').textContent = $('s-echoes').value;
    $('v-kill').textContent = $('s-kill').value + ' с'; $('v-discuss').textContent = $('s-discuss').value + ' с';
    $('v-tasks').textContent = $('s-tasks').value; $('v-volume').textContent = $('s-volume').value + '%';
    const maxE = Math.max(1, Math.floor((+$('s-bots').value) / 2));
    $('s-echoes').max = Math.min(3, maxE);
    if (+$('s-echoes').value > maxE) { $('s-echoes').value = maxE; $('v-echoes').textContent = maxE; }
    Sfx.setVolume($('s-volume').value / 100);
  };
  for (const id of ['s-bots', 's-echoes', 's-kill', 's-discuss', 's-tasks', 's-volume']) $(id).oninput = upd;
  upd();
}
function readSetup() {
  settings.name = $('s-name').value.trim() || 'Смотритель';
  settings.bots = +$('s-bots').value; settings.echoes = +$('s-echoes').value;
  settings.killCd = +$('s-kill').value; settings.discuss = +$('s-discuss').value;
  settings.tasks = +$('s-tasks').value; settings.role = $('s-role').value;
  settings.difficulty = $('s-diff').value; settings.confirmEjects = $('s-confirm').checked;
  settings.volume = $('s-volume').value / 100;
  saveSettings(settings);
}

let storyTimer = null;
function playStory() {
  const box = $('story-text'); box.innerHTML = '';
  let i = 0;
  clearInterval(storyTimer);
  const next = () => {
    if (i >= STORY.prologue.length) { clearInterval(storyTimer); return; }
    const p = document.createElement('p'); p.textContent = STORY.prologue[i++]; box.appendChild(p);
    requestAnimationFrame(() => p.classList.add('in'));
  };
  next();
  storyTimer = setInterval(next, 2600);
}

function startGame() {
  Sfx.unlock(); Sfx.setVolume(settings.volume);
  Game.start(settings);
  hudCache = {};
  // экран роли
  UI.show('intro');
  const me = G.me;
  $('intro').className = 'screen ' + (me.isEcho ? 'intro-echo' : 'intro-crew');
  $('intro-role').textContent = me.isEcho ? 'ЭХО' : 'СМОТРИТЕЛЬ';
  $('intro-text').textContent = me.isEcho ? STORY.echoIntro : STORY.crewIntro;
  const n = G.players.filter(p => p.isEcho).length;
  $('intro-count').textContent = `Среди нас ${n} ${n === 1 ? 'Эхо' : 'Эхо'}`;
  const cv = $('intro-canvas'), g = cv.getContext('2d');
  g.clearRect(0, 0, cv.width, cv.height);
  const pool = me.isEcho ? G.players.filter(p => p.isEcho && p !== me) : G.players.filter(p => p !== me);
  const show = pool.slice(); show.splice(Math.floor(pool.length / 2), 0, me);
  const k = show.length;
  show.forEach((p, i) => {
    const off = i - (k - 1) / 2;
    const sc = p === me ? 1.9 : 1.3;
    drawCrew(g, cv.width / 2 + off * (me.isEcho ? 130 : 70) * (p === me ? 1 : 1), 250 - (p === me ? 0 : 20) + Math.abs(off) * 6, p.color, { scale: sc, echoEyes: me.isEcho, facing: off < 0 ? 1 : -1 });
  });
  G.phase = 'intro';
  me.isEcho ? Sfx.sabotage() : Sfx.taskDone();
  setTimeout(() => {
    UI.show(null);
    hudVisible(true);
    G.phase = 'play';
    G.cam.x = me.x; G.cam.y = me.y;
    Sfx.humStart();
    UI.toast(me.isEcho ? 'Q — кристаллизовать, F — саботаж, E у вентиляции — влезть' : 'WASD — ходить, E — действие, R — сообщить о теле, M — карта');
  }, 3800);
}

// ---------- Ввод ----------
function initInput() {
  window.addEventListener('keydown', e => {
    if (e.target.tagName === 'INPUT') return;
    G.keys[e.code] = true;
    if (G.phase !== 'play') return;
    if (e.code === 'Escape') { if (Minigames.isOpen()) Minigames.close(); else if (G.showMap) toggleMap(false); else if (!$('sab-menu').classList.contains('hidden')) $('sab-menu').classList.add('hidden'); }
    if (Minigames.isOpen()) return;
    if (e.code === 'KeyE' || e.code === 'Enter') Actions.use();
    if (e.code === 'KeyR') Actions.report();
    if (e.code === 'KeyQ') Actions.kill();
    if (e.code === 'KeyF') toggleSabMenu();
    if (e.code === 'KeyM' || e.code === 'Tab') { e.preventDefault(); toggleMap(); }
    if (G.me.inVent && /^Digit[1-9]$/.test(e.code)) Actions.ventMove(+e.code.slice(5) - 1);
  });
  window.addEventListener('keyup', e => { G.keys[e.code] = false; });
  window.addEventListener('blur', () => { G.keys = {}; });
  $('btn-use').addEventListener('click', () => Actions.use());
  $('btn-report').addEventListener('click', () => Actions.report());
  $('btn-kill').addEventListener('click', () => Actions.kill());
  $('btn-sab').addEventListener('click', () => toggleSabMenu());
  $('btn-map').addEventListener('click', () => toggleMap());
  $('minimap').addEventListener('click', () => toggleMap(false));
  $('vent-nav').addEventListener('click', e => {
    const b = e.target.closest('button'); if (!b) return;
    if (b.dataset.exit) Actions.use(); else Actions.ventMove(+b.dataset.i);
  });
  for (const b of document.querySelectorAll('#sab-menu [data-sab]')) b.addEventListener('click', () => { Actions.sabotage(b.dataset.sab); $('sab-menu').classList.add('hidden'); });
  $('sab-close').addEventListener('click', () => $('sab-menu').classList.add('hidden'));

  // сенсорный джойстик
  const zone = $('joy-zone'), knob = $('joy-knob'), base = $('joy-base');
  let jid = null, jx = 0, jy = 0;
  zone.addEventListener('pointerdown', e => {
    jid = e.pointerId; jx = e.clientX; jy = e.clientY; zone.setPointerCapture(jid);
    base.style.left = (jx - 60) + 'px'; base.style.top = (jy - 60) + 'px'; base.classList.add('on');
  });
  zone.addEventListener('pointermove', e => {
    if (e.pointerId !== jid) return;
    let dx = e.clientX - jx, dy = e.clientY - jy; const d = Math.hypot(dx, dy), m = 50;
    if (d > m) { dx = dx / d * m; dy = dy / d * m; }
    knob.style.transform = `translate(${dx}px,${dy}px)`;
    G.joy.x = dx / m; G.joy.y = dy / m;
  });
  const end = e => { if (e.pointerId !== jid) return; jid = null; G.joy.x = G.joy.y = 0; knob.style.transform = ''; base.classList.remove('on'); };
  zone.addEventListener('pointerup', end); zone.addEventListener('pointercancel', end);
}

function toggleSabMenu() {
  if (!G.me.isEcho || !G.me.alive) return;
  $('sab-menu').classList.toggle('hidden');
}
function toggleMap(v) {
  G.showMap = v === undefined ? !G.showMap : v;
  $('minimap').classList.toggle('hidden', !G.showMap);
}
function drawMapOverlay() {
  const cv = $('minimap-canvas');
  const w = Math.min(W * 0.9, 1100), h = Math.min(H * 0.8, w * MAP_H / MAP_W);
  if (cv.width !== (w | 0)) { cv.width = w | 0; cv.height = h | 0; }
  const g = cv.getContext('2d');
  g.clearRect(0, 0, cv.width, cv.height);
  const me = G.me;
  const commsDown = G.sab && G.sab.type === 'comms';
  drawMinimap(g, cv.width, cv.height, {
    me,
    tasks: (!me.isEcho && !commsDown) ? me.tasks.filter(t => !t.done).map(t => stationById(t.st)) : null,
    sab: G.sab ? G.sab.type : null, t: G.t,
    vents: me.isEcho,
    players: !me.alive ? G.players.filter(p => p.alive && p !== me) : null,
  });
}

// ---------- Главный цикл ----------
function resize() {
  dpr = Math.min(2, window.devicePixelRatio || 1);
  W = window.innerWidth; H = window.innerHeight;
  canvas.width = W * dpr; canvas.height = H * dpr;
  canvas.style.width = W + 'px'; canvas.style.height = H + 'px';
}

let last = performance.now();
function frame(now) {
  const dt = Math.min(0.05, (now - last) / 1000); last = now;
  scene.t += dt;
  ctx.setTransform(1, 0, 0, 1, 0, 0);
  if (G.phase === 'play' || G.phase === 'meeting' || G.phase === 'end') {
    if (G.phase === 'play') Game.update(dt);
    if (G.phase === 'meeting') Meeting.update(dt);
    const cw = canvas.width, ch = canvas.height;
    ctx.save();
    renderWorld(ctx, cw, ch);
    ctx.restore();
    if (G.phase === 'play') { updateHUD(); if (G.showMap) drawMapOverlay(); }
  } else {
    drawMenuBg(dt);
  }
  requestAnimationFrame(frame);
}

const menuCrew = [];
function drawMenuBg() {
  const cw = canvas.width, ch = canvas.height, t = scene.t;
  if (menuStars.length === 0 || menuStars._w !== cw) { menuStars = makeStars(300, cw, ch); menuStars._w = cw; }
  if (!menuCrew.length) for (let i = 0; i < 6; i++) menuCrew.push({ c: COLORS[(i * 3) % COLORS.length], x: Math.random(), y: Math.random(), r: Math.random() * 6, vr: rand(-0.6, 0.6), vx: rand(0.01, 0.03) });
  ctx.fillStyle = '#04050b'; ctx.fillRect(0, 0, cw, ch);
  drawStars(ctx, menuStars, cw, ch, t, 12 * dpr);
  drawPlanet(ctx, cw * 0.8, ch * 0.78, Math.min(cw, ch) * 0.36, t);
  for (const m of menuCrew) {
    const x = ((m.x + t * m.vx) % 1.2 - 0.1) * cw, y = (m.y * 0.8 + 0.1) * ch + Math.sin(t + m.r) * 20;
    ctx.save(); ctx.translate(x, y); ctx.rotate(m.r + t * m.vr);
    drawCrew(ctx, 0, 30, m.c, { scale: 1.1 * dpr, echoEyes: m === menuCrew[2] });
    ctx.restore();
  }
}

window.addEventListener('load', () => {
  canvas = $('game'); ctx = canvas.getContext('2d');
  resize(); window.addEventListener('resize', resize);
  buildMapCanvas();
  Minigames.init();
  Meeting.init();
  initMenu();
  initInput();
  Sfx.setVolume(settings.volume);
  UI.show('menu');
  hudVisible(false);
  requestAnimationFrame(frame);
  window.__echo = { G, Game, Actions, Meeting };
});
