// Ядро игры: состояние, управление, ИИ ботов, убийства, саботаж, победа
'use strict';

const G = {
  phase: 'menu',
  players: [], me: null, bodies: [], particles: [],
  t: 0, sab: null, sabCd: 0, meetingCd: 0,
  totalTasks: 0, doneTasks: 0,
  settings: null, keys: {}, joy: { x: 0, y: 0 },
  cam: { x: 0, y: 0, scale: 1 },
  showMap: false, toastT: 0, result: null,
};

const DEFAULT_SETTINGS = {
  name: 'Смотритель', color: 'cyan', bots: 7, echoes: 2, role: 'random',
  killCd: 30, discuss: 75, tasks: TASKS_PER_PLAYER, confirmEjects: true, speed: 1, difficulty: 'normal', volume: 0.6,
};

const KILL_RANGE = 1.6 * TILE;
const USE_RANGE = 1.35 * TILE;
const REPORT_RANGE = 2.4 * TILE;

function visionR(p) {
  if (p.isEcho) return 7.5 * TILE;
  if (G.sab && G.sab.type === 'lights') return 2.1 * TILE;
  return 5.6 * TILE;
}
function canSee(viewer, target) {
  if (!viewer.alive || !target.alive || target.inVent || viewer === target) return false;
  const r = visionR(viewer);
  if (Math.abs(viewer.x - target.x) > r || Math.abs(viewer.y - target.y) > r) return false;
  if (dist(viewer.x, viewer.y, target.x, target.y) > r) return false;
  return lineOfSight(viewer.x, viewer.y - 20, target.x, target.y - 20);
}
function canSeePoint(viewer, x, y) {
  if (!viewer.alive) return false;
  if (dist(viewer.x, viewer.y, x, y) > visionR(viewer)) return false;
  return lineOfSight(viewer.x, viewer.y - 20, x, y);
}
function crewAlive() { return G.players.filter(p => p.alive && !p.isEcho); }
function echoAlive() { return G.players.filter(p => p.alive && p.isEcho); }
function byId(id) { return G.players.find(p => p.id === id); }
function stationById(id) { return STATIONS.find(s => s.id === id); }

// ---------------- Запуск партии ----------------
const Game = {
  start(settings) {
    G.settings = Object.assign({}, DEFAULT_SETTINGS, settings);
    const S = G.settings;
    G.players = []; G.bodies = []; G.particles = [];
    G.t = 0; G.sab = null; G.sabCd = 20; G.meetingCd = 15; G.result = null;
    const colors = COLORS.slice();
    const myColor = colors.find(c => c.id === S.color) || colors[6];
    const others = shuffle(colors.filter(c => c !== myColor));
    const names = shuffle(BOT_NAMES.slice());
    const n = 1 + S.bots;
    for (let i = 0; i < n; i++) {
      const sp = SPAWNS[i % SPAWNS.length];
      G.players.push({
        id: i, name: i === 0 ? (S.name || 'Смотритель').slice(0, 12) : names[i - 1],
        color: i === 0 ? myColor : others[i - 1],
        isBot: i > 0, isEcho: false, alive: true,
        x: sp.x * TILE, y: sp.y * TILE, facing: 1, walk: 0, moving: false,
        tasks: [], killCd: 15, inVent: false, ventId: -1, scanning: false, buttonUses: 1,
        sus: {}, seen: {}, mem: [], lastRoom: 'mess', roomLog: [],
        ai: { path: null, state: 'idle', wait: rand(0.5, 2.5), think: Math.random() * 0.3, goal: null },
      });
    }
    G.me = G.players[0];
    // роли
    const echoes = Math.min(S.echoes, Math.max(1, Math.floor((n - 1) / 2)));
    let pool = G.players.slice();
    if (S.role === 'crew') pool = pool.filter(p => p !== G.me);
    shuffle(pool);
    let chosen = pool.slice(0, echoes);
    if (S.role === 'echo' && !chosen.includes(G.me)) { chosen.pop(); chosen.push(G.me); }
    chosen.forEach(p => p.isEcho = true);
    // задания
    G.totalTasks = 0; G.doneTasks = 0;
    for (const p of G.players) {
      const pool = shuffle(STATIONS.slice()).slice(0, S.tasks);
      p.tasks = pool.map(s => ({ st: s.id, done: false }));
      if (!p.isEcho) G.totalTasks += p.tasks.length;
      for (const q of G.players) if (q !== p) p.sus[q.id] = 0;
    }
    G.phase = 'play';
    G.showMap = false;
    Minigames.close();
  },

  update(dt) {
    if (G.phase !== 'play') return;
    G.t += dt;
    const me = G.me;
    if (G.sabCd > 0) G.sabCd -= dt;
    if (G.meetingCd > 0) G.meetingCd -= dt;
    for (const p of G.players) if (p.isEcho && p.alive && !p.inVent && p.killCd > 0) p.killCd -= dt;
    // саботаж реактора
    if (G.sab && G.sab.type === 'reactor') {
      G.sab.timer -= dt;
      if (Math.floor(G.sab.timer) !== G.sab.lastBeep) { G.sab.lastBeep = Math.floor(G.sab.timer); if (G.sab.lastBeep % 2 === 0) Sfx.error(); }
      if (G.sab.timer <= 0) { endGame('echo_reactor'); return; }
    }
    updateHuman(dt);
    for (const p of G.players) if (p.isBot) updateBot(p, dt);
    perception(dt);
    // частицы
    for (const pa of G.particles) { pa.x += pa.vx * dt; pa.y += pa.vy * dt; pa.life -= dt; }
    G.particles = G.particles.filter(pa => pa.life > 0);
    if (G.phase === 'play') checkWin();
  },
};

// ---------------- Игрок ----------------
function updateHuman(dt) {
  const me = G.me;
  if (Minigames.isOpen()) {
    // отошли (например, после телепорта) — закрыть
    me.moving = false;
    return;
  }
  if (me.inVent) { me.moving = false; return; }
  let ax = 0, ay = 0;
  const k = G.keys;
  if (k.KeyA || k.ArrowLeft) ax -= 1;
  if (k.KeyD || k.ArrowRight) ax += 1;
  if (k.KeyW || k.ArrowUp) ay -= 1;
  if (k.KeyS || k.ArrowDown) ay += 1;
  ax += G.joy.x; ay += G.joy.y;
  const len = Math.hypot(ax, ay);
  if (len > 0.1) {
    const sp = 3.7 * TILE * G.settings.speed * (me.alive ? 1 : 1.15);
    const vx = ax / Math.max(1, len) * sp * dt, vy = ay / Math.max(1, len) * sp * dt;
    if (!me.alive) {
      me.x = clamp(me.x + vx, TILE, (MAP_W - 1) * TILE); me.y = clamp(me.y + vy, TILE, (MAP_H - 1) * TILE);
    } else {
      if (!blockedCircle(me.x + vx, me.y - 8, 13)) me.x += vx;
      if (!blockedCircle(me.x, me.y + vy - 8, 13)) me.y += vy;
    }
    if (Math.abs(ax) > 0.1) me.facing = ax > 0 ? 1 : -1;
    me.walk += dt * 12; me.moving = true;
    if (me.alive && ((me.walk / Math.PI) | 0) !== me._lastStep) { me._lastStep = (me.walk / Math.PI) | 0; Sfx.step(); }
  } else { me.moving = false; me.walk = 0; }
  const room = roomAtPx(me.x, me.y);
  if (room) me.lastRoom = room;
}

// Что можно сделать рядом
function nearestUse() {
  const me = G.me;
  if (me.inVent) return { kind: 'ventexit' };
  if (me.alive && me.isEcho) {
    for (const v of VENTS) if (dist(me.x, me.y, (v.x + 0.5) * TILE, (v.y + 0.5) * TILE) < USE_RANGE) return { kind: 'vent', vent: v };
  }
  if (me.alive && G.sab) {
    const f = FIXES[G.sab.type];
    if (dist(me.x, me.y, (f.x + 0.5) * TILE, (f.y + 0.5) * TILE) < USE_RANGE) return { kind: 'fix', fix: f };
  }
  if (me.alive && dist(me.x, me.y, BUTTON.x * TILE, BUTTON.y * TILE) < 2.1 * TILE) return { kind: 'button' };
  for (const t of me.tasks) {
    if (t.done) continue;
    const s = stationById(t.st);
    if (dist(me.x, me.y, (s.x + 0.5) * TILE, (s.y + 0.5) * TILE) < USE_RANGE) return { kind: 'task', task: t, st: s };
  }
  return null;
}
function nearestBody() {
  const me = G.me;
  if (!me.alive) return null;
  let best = null, bd = REPORT_RANGE;
  for (const b of G.bodies) { const d = dist(me.x, me.y, b.x, b.y); if (d < bd && canSeePoint(me, b.x, b.y)) { bd = d; best = b; } }
  return best;
}
function nearestKillTarget(p) {
  let best = null, bd = KILL_RANGE;
  for (const q of G.players) {
    if (q === p || !q.alive || q.isEcho || q.inVent) continue;
    const d = dist(p.x, p.y, q.x, q.y);
    if (d < bd && lineOfSight(p.x, p.y - 20, q.x, q.y - 20)) { bd = d; best = q; }
  }
  return best;
}

const Actions = {
  use() {
    if (G.phase !== 'play' || Minigames.isOpen()) return;
    const u = nearestUse(); const me = G.me;
    if (!u) return;
    if (u.kind === 'ventexit') return ventExit(me);
    if (u.kind === 'vent') return ventEnter(me, u.vent);
    if (u.kind === 'fix') {
      const type = G.sab.type;
      return Minigames.open(u.fix.type, u.fix.name, () => { if (G.sab && G.sab.type === type) fixSabotage(me); }, { player: me });
    }
    if (u.kind === 'button') {
      if (G.sab) return toast('Нельзя созвать собрание во время аварии!');
      if (G.meetingCd > 0) return toast(`Кнопка заблокирована ещё ${Math.ceil(G.meetingCd)} с`);
      if (me.buttonUses <= 0) return toast('Ты уже использовал(а) экстренную кнопку');
      me.buttonUses--;
      return callMeeting(me, null);
    }
    if (u.kind === 'task') {
      if (me.isEcho) return toast('Эхо не умеют чинить — просто изображай работу');
      const s = u.st;
      Minigames.open(s.type, `${roomName(s.room)}: ${s.name}`, () => completeTask(me, u.task), { player: me });
    }
  },
  report() {
    if (G.phase !== 'play') return;
    const b = nearestBody();
    if (b) { Minigames.close(); callMeeting(G.me, b.victim); }
  },
  kill() {
    const me = G.me;
    if (G.phase !== 'play' || !me.isEcho || !me.alive || me.inVent || me.killCd > 0) return;
    const t = nearestKillTarget(me);
    if (t) doKill(me, t);
  },
  sabotage(type) {
    const me = G.me;
    if (G.phase !== 'play' || !me.isEcho || !me.alive) return;
    if (G.sab) return toast('Авария уже идёт');
    if (G.sabCd > 0) return toast(`Саботаж перезаряжается: ${Math.ceil(G.sabCd)} с`);
    startSabotage(type, me);
  },
  ventMove(idx) {
    const me = G.me;
    if (!me.inVent) return;
    const net = VENTS.filter(v => v.net === VENTS[me.ventId].net && v.id !== me.ventId);
    const v = net[idx]; if (!v) return;
    me.ventId = v.id; me.x = (v.x + 0.5) * TILE; me.y = (v.y + 0.5) * TILE + 6; Sfx.vent();
  },
};

function completeTask(p, task) {
  if (task.done) return;
  task.done = true;
  if (!p.isEcho) G.doneTasks++;
}

// ---------------- Вентиляция ----------------
function ventWitnesses(p) {
  return G.players.filter(w => w !== p && w.alive && !w.isEcho && canSee(w, p));
}
function ventEnter(p, v) {
  for (const w of ventWitnesses(p)) noteVent(w, p, v.room);
  p.inVent = true; p.ventId = v.id; p.x = (v.x + 0.5) * TILE; p.y = (v.y + 0.5) * TILE + 6;
  if (p === G.me || canSee(G.me, p) || !G.me.alive) Sfx.vent();
  puff((v.x + 0.5) * TILE, (v.y + 0.5) * TILE, '#8a8fa3');
}
function ventExit(p) {
  p.inVent = false;
  const v = VENTS[p.ventId];
  for (const w of ventWitnesses(p)) noteVent(w, p, v.room);
  if (p === G.me || canSee(G.me, p)) Sfx.vent();
  puff((v.x + 0.5) * TILE, (v.y + 0.5) * TILE, '#8a8fa3');
}
function noteVent(w, p, room) {
  if (!w.isBot) return;
  w.sus[p.id] = Math.max(w.sus[p.id] || 0, 160);
  w.mem.push({ type: 'vent', who: p.id, room, t: G.t });
  if (!w.ai.reportAt) w.ai.reportAt = G.t + rand(1, 2.5);
  w.ai.button = true;
}

// ---------------- Убийство ----------------
function witnessesOf(killer, victim) {
  return G.players.filter(w => w !== killer && w !== victim && w.alive && !w.isEcho && (canSee(w, killer) || canSee(w, victim)));
}
function doKill(killer, victim) {
  const wit = witnessesOf(killer, victim);
  victim.alive = false; victim.scanning = false; victim.inVent = false;
  if (victim.isBot) victim.ai = { path: null, state: 'idle', wait: 1, think: 0 };
  G.bodies.push({ victim: victim.id, x: victim.x, y: victim.y, color: victim.color, room: roomAtPx(victim.x, victim.y) || killer.lastRoom, t: G.t });
  killer.x = victim.x; killer.y = victim.y;
  killer.killCd = G.settings.killCd;
  for (let i = 0; i < 18; i++) G.particles.push({ x: victim.x, y: victim.y - 25, vx: rand(-120, 120), vy: rand(-160, 40), life: rand(0.5, 1.1), c: pick(['#e9d5ff', '#c77dff', '#8b5cf6', '#fff']) });
  const room = roomAtPx(victim.x, victim.y) || killer.lastRoom;
  for (const w of wit) {
    if (!w.isBot) continue;
    w.sus[killer.id] = 250;
    w.mem.push({ type: 'kill', who: killer.id, victim: victim.id, room, t: G.t });
    w.ai.reportAt = G.t + rand(0.6, 1.4);
  }
  const meSees = G.me === killer || G.me === victim || canSeePoint(G.me, victim.x, victim.y) || !G.me.alive;
  if (meSees) Sfx.kill();
  if (victim === G.me) { Minigames.close(); UI.showDeath(killer); }
  checkWin();
}

// ---------------- Саботаж ----------------
function startSabotage(type, by) {
  G.sab = { type, timer: type === 'reactor' ? 45 : 0, fixers: [], lastBeep: 99, by: by.id };
  G.sabCd = 40;
  Sfx.sabotage();
  // назначить ремонтников из ботов
  const fx = FIXES[type];
  const cands = G.players.filter(p => p.isBot && p.alive && !p.isEcho)
    .sort((a, b) => dist(a.x, a.y, fx.x * TILE, fx.y * TILE) - dist(b.x, b.y, fx.x * TILE, fx.y * TILE));
  const nFix = type === 'reactor' ? 2 : 1;
  G.sab.fixers = cands.slice(0, nFix).map(p => p.id);
  for (const id of G.sab.fixers) { const p = byId(id); p.ai.state = 'idle'; p.ai.path = null; p.ai.think = rand(0.5, 2.5); }
}
function fixSabotage(by) {
  if (!G.sab) return;
  G.sab = null;
  Sfx.taskDone();
  toast(`${by === G.me ? 'Ты устранил(а)' : by.name + ' устраняет'} аварию`);
}

// ---------------- Восприятие ботов ----------------
let percT = 0;
function perception(dt) {
  percT += dt;
  if (percT < 0.25) return;
  percT = 0;
  for (const b of G.players) {
    if (!b.isBot || !b.alive) continue;
    const room = roomAtPx(b.x, b.y);
    if (room && room !== b.lastRoom) { b.lastRoom = room; b.roomLog.push({ room, t: G.t }); if (b.roomLog.length > 12) b.roomLog.shift(); }
    for (const p of G.players) {
      if (p === b || !canSee(b, p)) continue;
      b.seen[p.id] = { t: G.t, room: roomAtPx(p.x, p.y) || b.lastRoom, x: p.x, y: p.y };
      if (p.scanning && !b.isEcho) { b.sus[p.id] = Math.min(b.sus[p.id], -80); if (!b.mem.some(m => m.type === 'scan' && m.who === p.id)) b.mem.push({ type: 'scan', who: p.id, t: G.t }); }
    }
    // заметить тело
    if (!b.ai.reportAt) {
      for (const body of G.bodies) {
        if (canSeePoint(b, body.x, body.y)) {
          if (b.isEcho) {
            // Эхо иногда «находит» тело, чтобы выглядеть честно
            if (!body.echoChecked) { body.echoChecked = true; if (Math.random() < 0.25) b.ai.reportAt = G.t + rand(1, 3); }
          } else { b.ai.reportAt = G.t + rand(0.4, 1.1); b.ai.reportBody = body.victim; }
          break;
        }
      }
    }
  }
}

// ---------------- ИИ ботов ----------------
function botSpeed(p) { return (p.alive ? 3.2 : 3.6) * TILE * G.settings.speed; }

function goTo(p, x, y) {
  const path = findPath(p.x, p.y, x, y);
  p.ai.path = path && path.length ? path : null;
  return !!p.ai.path;
}

function followPath(p, dt) {
  const path = p.ai.path;
  if (!path || !path.length) { p.moving = false; return true; }
  let step = botSpeed(p) * dt;
  while (step > 0 && path.length) {
    const n = path[0];
    const dx = n.x - p.x, dy = n.y - p.y, d = Math.hypot(dx, dy);
    if (d <= step) { p.x = n.x; p.y = n.y; path.shift(); step -= d; }
    else { p.x += dx / d * step; p.y += dy / d * step; if (Math.abs(dx) > 1) p.facing = dx > 0 ? 1 : -1; step = 0; }
  }
  p.moving = true; p.walk += dt * 11;
  if (!path.length) { p.ai.path = null; p.moving = false; p.walk = 0; return true; }
  return false;
}

function nextTask(p) {
  const todo = p.tasks.filter(t => !t.done);
  if (!todo.length) return null;
  todo.sort((a, b) => {
    const sa = stationById(a.st), sb = stationById(b.st);
    return dist(p.x, p.y, sa.x * TILE, sa.y * TILE) - dist(p.x, p.y, sb.x * TILE, sb.y * TILE);
  });
  return Math.random() < 0.7 ? todo[0] : pick(todo);
}

function updateBot(p, dt) {
  const ai = p.ai;
  if (p.inVent) {
    ai.wait -= dt;
    if (ai.wait <= 0) {
      // переместиться и вылезти, если никто не смотрит
      if (ai.ventHops > 0) {
        const net = VENTS.filter(v => v.net === VENTS[p.ventId].net && v.id !== p.ventId);
        const v = pick(net); p.ventId = v.id; p.x = (v.x + 0.5) * TILE; p.y = (v.y + 0.5) * TILE + 6; ai.ventHops--; ai.wait = rand(0.6, 1.2);
      } else if (ventWitnesses(p).length === 0 || ai.ventWaited > 4) { ventExit(p); ai.state = 'idle'; ai.wait = rand(0.3, 1); }
      else { ai.ventWaited = (ai.ventWaited || 0) + 0.5; ai.wait = 0.5; }
    }
    return;
  }

  // движение
  if (ai.path) followPath(p, dt);
  else p.moving = false;

  ai.think -= dt;
  if (ai.think > 0) return;
  ai.think = 0.2 + Math.random() * 0.15;

  // доклад
  if (p.alive && ai.reportAt && G.t >= ai.reportAt) {
    ai.reportAt = 0;
    const seenBody = G.bodies.find(b => canSeePoint(p, b.x, b.y)) || (ai.reportBody != null && G.bodies.find(b => b.victim === ai.reportBody));
    if (seenBody) { callMeeting(p, seenBody.victim); return; }
    if (ai.button && !G.sab && p.buttonUses > 0) { ai.button = false; ai.state = 'button'; goTo(p, BUTTON.x * TILE, (BUTTON.y + 1.8) * TILE); }
  }
  if (ai.state === 'button') {
    if (!ai.path) {
      if (dist(p.x, p.y, BUTTON.x * TILE, BUTTON.y * TILE) < 2.4 * TILE && !G.sab && G.meetingCd <= 0 && p.buttonUses > 0) { p.buttonUses--; callMeeting(p, null); return; }
      ai.state = 'idle';
    }
    return;
  }

  if (p.isEcho && p.alive) return echoThink(p, dt);
  crewThink(p, dt);
}

function crewThink(p) {
  const ai = p.ai;
  // ремонт аварии
  if (p.alive && G.sab && G.sab.fixers.includes(p.id)) {
    const f = FIXES[G.sab.type];
    const fx = (f.x + 0.5) * TILE, fy = (f.y + 0.5) * TILE;
    if (ai.state !== 'fix') { ai.state = 'fix'; ai.fixT = 0; goTo(p, fx, fy); return; }
    if (!ai.path) {
      if (dist(p.x, p.y, fx, fy) > USE_RANGE) { goTo(p, fx, fy); return; }
      ai.fixT += 0.3;
      if (ai.fixT > (G.sab.type === 'reactor' ? 3.5 : 4.5)) { fixSabotage(p); ai.state = 'idle'; }
    }
    return;
  }
  if (ai.state === 'fix') ai.state = 'idle';

  if (ai.state === 'task') {
    if (ai.path) return;
    const s = stationById(ai.task.st);
    if (s.visual && p.alive) p.scanning = true;
    ai.wait -= 0.3;
    if (ai.wait <= 0) { p.scanning = false; completeTask(p, ai.task); ai.state = 'idle'; ai.wait = rand(0.3, 1.5); }
    return;
  }
  if (ai.state === 'wander') {
    if (ai.path) return;
    ai.wait -= 0.3; if (ai.wait > 0) return;
    ai.state = 'idle';
  }
  // выбрать дело
  const t = nextTask(p);
  if (t) {
    const s = stationById(t.st);
    const sp = standPoint(s.x + 0.5, s.y + 0.5);
    if (goTo(p, sp.x, sp.y) || dist(p.x, p.y, sp.x, sp.y) < 10) {
      ai.state = 'task'; ai.task = t; ai.wait = s.time * rand(1.1, 1.6) * (p.alive ? 1 : 1.3);
    }
    return;
  }
  const rp = randomRoomPoint();
  goTo(p, rp.x, rp.y); ai.state = 'wander'; ai.wait = rand(2, 6);
}

function echoThink(p) {
  const ai = p.ai;
  const diff = { easy: 0.35, normal: 0.6, hard: 1 }[G.settings.difficulty] || 0.6;
  // саботаж
  if (!G.sab && G.sabCd <= 0 && Math.random() < 0.012 * diff) {
    const types = ['lights', 'reactor', 'comms'];
    startSabotage(pick(types), p);
  }
  // охота
  if (p.killCd <= 0 && G.meetingCd < 12) {
    if (ai.state === 'hunt') {
      const tgt = byId(ai.target);
      if (!tgt || !tgt.alive || tgt.inVent || G.t > ai.huntUntil) { ai.state = 'idle'; ai.path = null; }
      else {
        const wit = witnessesOf(p, tgt);
        if (wit.length) { ai.state = 'idle'; ai.path = null; ai.cool = G.t + 2; }
        else if (dist(p.x, p.y, tgt.x, tgt.y) < KILL_RANGE * 0.9) {
          doKill(p, tgt);
          afterKill(p);
          return;
        } else goTo(p, tgt.x, tgt.y);
        return;
      }
    }
    if (!(ai.cool > G.t)) {
      const cands = G.players.filter(q => q.alive && !q.isEcho && !q.inVent && canSee(p, q) && dist(p.x, p.y, q.x, q.y) < 6 * TILE);
      for (const q of shuffle(cands)) {
        if (witnessesOf(p, q).length === 0 && Math.random() < 0.55 * diff) {
          ai.state = 'hunt'; ai.target = q.id; ai.huntUntil = G.t + 7; goTo(p, q.x, q.y); return;
        }
      }
    }
  }
  if (ai.state === 'flee') {
    if (ai.path) return;
    if (ai.vent != null) {
      const v = VENTS[ai.vent]; ai.vent = null;
      if (dist(p.x, p.y, (v.x + 0.5) * TILE, (v.y + 0.5) * TILE) < USE_RANGE && ventWitnesses(p).length === 0) {
        ventEnter(p, v); ai.ventHops = 1; ai.wait = rand(0.8, 1.5); ai.ventWaited = 0; return;
      }
    }
    ai.state = 'idle';
  }
  if (ai.state === 'fake') {
    if (ai.path) return;
    ai.wait -= 0.3; if (ai.wait > 0) return;
    ai.state = 'idle';
  }
  if (ai.state === 'stalk') {
    const tgt = byId(ai.target);
    if (!tgt || !tgt.alive || G.t > ai.until) { ai.state = 'idle'; }
    else { if (!ai.path || Math.random() < 0.3) goTo(p, tgt.x + rand(-60, 60), tgt.y + rand(-60, 60)); return; }
  }
  if (ai.state === 'hunt') return;
  // выбор занятия
  const r = Math.random();
  if (r < 0.4 * diff) {
    const crew = G.players.filter(q => q.alive && !q.isEcho);
    if (crew.length) { ai.state = 'stalk'; ai.target = pick(crew).id; ai.until = G.t + rand(8, 15); return; }
  }
  const st = pick(STATIONS.filter(s => !s.visual));
  const sp = standPoint(st.x + 0.5, st.y + 0.5);
  goTo(p, sp.x, sp.y); ai.state = 'fake'; ai.wait = rand(2.5, 6);
}

function afterKill(p) {
  const ai = p.ai;
  const body = G.bodies[G.bodies.length - 1];
  if (Math.random() < 0.15) { ai.reportAt = G.t + rand(2, 4); ai.state = 'fake'; ai.wait = 4; ai.path = null; body.selfReport = p.id; return; }
  // ближайшая вентиляция
  let best = null, bd = 6 * TILE;
  for (const v of VENTS) { const d = dist(p.x, p.y, (v.x + 0.5) * TILE, (v.y + 0.5) * TILE); if (d < bd) { bd = d; best = v; } }
  ai.state = 'flee';
  if (best && Math.random() < 0.8) { ai.vent = best.id; goTo(p, (best.x + 0.5) * TILE, (best.y + 0.5) * TILE); }
  else { ai.vent = null; const rp = randomRoomPoint(); goTo(p, rp.x, rp.y); }
}

// ---------------- Собрание ----------------
function callMeeting(caller, victimId) {
  if (G.phase !== 'play') return;
  Minigames.close();
  for (const p of G.players) { p.scanning = false; if (p.inVent) { p.inVent = false; } }
  G.phase = 'meeting';
  G.sab = null;
  if (victimId != null) Sfx.report(); else Sfx.alarm();
  UI.showMeetingSplash(caller, victimId, () => Meeting.start(caller, victimId));
}

function afterMeeting(ejectedId) {
  if (ejectedId != null) { const p = byId(ejectedId); p.alive = false; p.ejected = true; }
  G.bodies = [];
  const alive = G.players.filter(p => p.alive);
  alive.forEach((p, i) => { const sp = SPAWNS[i % SPAWNS.length]; p.x = sp.x * TILE; p.y = sp.y * TILE; });
  for (const p of G.players) {
    p.killCd = G.settings.killCd; p.inVent = false;
    p.ai = { path: null, state: 'idle', wait: rand(0.5, 2), think: rand(0, 1), goal: null };
    for (const k in p.sus) p.sus[k] *= 0.6;
    p.mem = p.mem.filter(m => m.type === 'scan');
  }
  G.sabCd = 20; G.meetingCd = 15; G.sab = null;
  G.phase = 'play';
  checkWin();
}

// ---------------- Победа ----------------
function checkWin() {
  if (G.phase === 'end') return;
  const echo = echoAlive().length, crew = crewAlive().length;
  if (echo === 0) return endGame('crew_eject');
  if (echo >= crew) return endGame('echo_kill');
  if (G.doneTasks >= G.totalTasks) return endGame('crew_tasks');
}
function endGame(reason) {
  if (G.phase === 'end') return;
  G.phase = 'end';
  Minigames.close();
  const echoWin = reason.startsWith('echo');
  G.result = { reason, echoWin, meWin: echoWin === G.me.isEcho };
  UI.showEnd(G.result);
}

// ---------------- Эффекты ----------------
function puff(x, y, c) {
  for (let i = 0; i < 10; i++) G.particles.push({ x, y, vx: rand(-60, 60), vy: rand(-80, 10), life: rand(0.3, 0.7), c });
}
function toast(msg) { UI.toast(msg); }

// ---------------- Отрисовка мира ----------------
let fogCanvas = null;
function renderWorld(ctx, W, H) {
  const me = G.me;
  const sc = H / (14.5 * TILE) * Math.max(1, Math.min(1.25, W / H / 1.5));
  G.cam.scale = sc;
  G.cam.x += (me.x - G.cam.x) * 0.25; G.cam.y += (me.y - 30 - G.cam.y) * 0.25;
  if (Math.abs(me.x - G.cam.x) > 400 || Math.abs(me.y - G.cam.y) > 400) { G.cam.x = me.x; G.cam.y = me.y - 30; }
  const cx = G.cam.x, cy = G.cam.y;
  ctx.setTransform(1, 0, 0, 1, 0, 0);
  ctx.fillStyle = '#05060c'; ctx.fillRect(0, 0, W, H);
  ctx.setTransform(sc, 0, 0, sc, W / 2 - cx * sc, H / 2 - cy * sc);
  const vw = W / sc, vh = H / sc;
  const sx = Math.max(0, cx - vw / 2), sy = Math.max(0, cy - vh / 2);
  ctx.drawImage(MAP_CANVAS, sx, sy, Math.min(vw + 2, MAP_CANVAS.width - sx), Math.min(vh + 2, MAP_CANVAS.height - sy), sx, sy, Math.min(vw + 2, MAP_CANVAS.width - sx), Math.min(vh + 2, MAP_CANVAS.height - sy));

  const t = G.t;
  const pulse = 0.5 + 0.5 * Math.sin(t * 5);
  // подсветка своих заданий
  const commsDown = G.sab && G.sab.type === 'comms';
  if (!me.isEcho && !commsDown) {
    for (const tk of me.tasks) {
      if (tk.done) continue;
      const s = stationById(tk.st);
      ctx.strokeStyle = `rgba(255,216,74,${0.4 + 0.5 * pulse})`; ctx.lineWidth = 3;
      ctx.strokeRect(s.x * TILE + 6, s.y * TILE + 2, TILE - 12, TILE - 8);
    }
  }
  if (G.sab) {
    const f = FIXES[G.sab.type];
    ctx.strokeStyle = `rgba(255,60,60,${0.4 + 0.6 * pulse})`; ctx.lineWidth = 4;
    ctx.strokeRect(f.x * TILE + 4, f.y * TILE, TILE - 8, TILE - 4);
  }
  if (me.isEcho && me.alive) {
    for (const v of VENTS) if (dist(me.x, me.y, (v.x + 0.5) * TILE, (v.y + 0.5) * TILE) < 3 * TILE) {
      ctx.strokeStyle = `rgba(199,125,255,${0.4 + 0.5 * pulse})`; ctx.lineWidth = 3;
      roundRect(ctx, (v.x + 0.5) * TILE - 20, (v.y + 0.5) * TILE - 14, 40, 28, 5); ctx.stroke();
    }
  }
  // кнопка — свечение
  ctx.fillStyle = `rgba(229,72,77,${0.15 + 0.15 * pulse})`;
  ctx.beginPath(); ctx.arc(BUTTON.x * TILE, BUTTON.y * TILE, 40, 0, Math.PI * 2); ctx.fill();

  const ghostView = !me.alive;
  // тела
  for (const b of G.bodies) if (ghostView || canSeePoint(me, b.x, b.y)) drawBody(ctx, b.x, b.y, b.color, t);
  // персонажи
  const list = G.players.filter(p => {
    if (p.ejected) return false;
    if (p === me) return !me.inVent;
    if (p.inVent) return false;
    if (!p.alive) return ghostView;
    return ghostView || canSee(me, p);
  }).sort((a, b) => a.y - b.y);
  for (const p of list) {
    if (p.scanning) {
      ctx.save();
      ctx.strokeStyle = `rgba(95,242,194,${0.5 + 0.4 * pulse})`; ctx.lineWidth = 3;
      for (let k = 0; k < 3; k++) { const yy = p.y - ((t * 60 + k * 20) % 60); ctx.beginPath(); ctx.ellipse(p.x, yy, 20, 7, 0, 0, Math.PI * 2); ctx.stroke(); }
      ctx.restore();
    }
    drawCrew(ctx, p.x, p.y, p.color, { facing: p.facing, walk: p.moving ? p.walk : 0, ghost: !p.alive, echoEyes: me.isEcho && p.isEcho && p.alive });
    // имя
    ctx.font = 'bold 13px "Segoe UI", Arial';
    ctx.textAlign = 'center';
    const red = me.isEcho && p.isEcho;
    ctx.lineWidth = 3; ctx.strokeStyle = 'rgba(0,0,0,0.85)';
    ctx.strokeText(p.name, p.x, p.y - 70);
    ctx.fillStyle = red ? '#ff6b6b' : '#ffffff';
    ctx.fillText(p.name, p.x, p.y - 70);
  }
  // частицы
  for (const pa of G.particles) { ctx.globalAlpha = Math.min(1, pa.life * 2); ctx.fillStyle = pa.c; ctx.fillRect(pa.x - 2, pa.y - 2, 4, 4); }
  ctx.globalAlpha = 1;

  // туман войны
  if (!fogCanvas) fogCanvas = document.createElement('canvas');
  if (fogCanvas.width !== W || fogCanvas.height !== H) { fogCanvas.width = W; fogCanvas.height = H; }
  const fg = fogCanvas.getContext('2d');
  fg.setTransform(1, 0, 0, 1, 0, 0);
  fg.globalCompositeOperation = 'source-over';
  fg.clearRect(0, 0, W, H);
  if (me.alive) {
    fg.fillStyle = 'rgba(3,4,10,0.94)'; fg.fillRect(0, 0, W, H);
    fg.setTransform(sc, 0, 0, sc, W / 2 - cx * sc, H / 2 - cy * sc);
    fg.globalCompositeOperation = 'destination-out';
    const R = visionR(me);
    const poly = visibilityPolygon(me.x, me.y - 20, R);
    const gr = fg.createRadialGradient(me.x, me.y - 20, R * 0.55, me.x, me.y - 20, R);
    gr.addColorStop(0, 'rgba(0,0,0,1)'); gr.addColorStop(1, 'rgba(0,0,0,0)');
    fg.fillStyle = gr;
    fg.beginPath(); fg.moveTo(poly[0], poly[1]);
    for (let k = 2; k < poly.length; k += 2) fg.lineTo(poly[k], poly[k + 1]);
    fg.closePath(); fg.fill();
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.drawImage(fogCanvas, 0, 0);
  } else {
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.fillStyle = 'rgba(30,20,60,0.18)'; ctx.fillRect(0, 0, W, H);
  }
  ctx.setTransform(1, 0, 0, 1, 0, 0);
  // реактор — красная пульсация
  if (G.sab && G.sab.type === 'reactor') {
    ctx.fillStyle = `rgba(255,0,0,${0.08 + 0.12 * pulse})`; ctx.fillRect(0, 0, W, H);
  }
  // стрелка к аварии
  if (G.sab && me.alive) {
    const f = FIXES[G.sab.type];
    drawEdgeArrow(ctx, W, H, (f.x + 0.5) * TILE, (f.y + 0.5) * TILE, '#ff4040', sc);
  }
  // в вентиляции
  if (me.inVent) {
    ctx.fillStyle = 'rgba(10,8,20,0.55)'; ctx.fillRect(0, 0, W, H);
  }
}

function drawEdgeArrow(ctx, W, H, wx, wy, color, sc) {
  const sx = W / 2 + (wx - G.cam.x) * sc, sy = H / 2 + (wy - G.cam.y) * sc;
  if (sx > 40 && sx < W - 40 && sy > 40 && sy < H - 40) return;
  const a = Math.atan2(sy - H / 2, sx - W / 2);
  const m = 46;
  const ex = clamp(sx, m, W - m), ey = clamp(sy, m, H - m);
  ctx.save(); ctx.translate(ex, ey); ctx.rotate(a);
  ctx.fillStyle = color; ctx.beginPath(); ctx.moveTo(18, 0); ctx.lineTo(-10, -12); ctx.lineTo(-4, 0); ctx.lineTo(-10, 12); ctx.closePath(); ctx.fill();
  ctx.restore();
}
