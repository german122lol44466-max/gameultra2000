// Мини-игры заданий и устранения саботажа
'use strict';

const Minigames = (() => {
  let modal, body, titleEl, current = null;

  function init() {
    modal = document.getElementById('task-modal');
    body = document.getElementById('task-body');
    titleEl = document.getElementById('task-title');
    document.getElementById('task-close').addEventListener('click', () => close());
  }

  // open(type, title, onDone, ctx) — ctx: {player}
  function open(type, title, onDone, ctx = {}) {
    close();
    body.innerHTML = '';
    titleEl.textContent = title;
    modal.classList.remove('hidden');
    let finished = false;
    const done = () => {
      if (finished) return;
      finished = true;
      Sfx.taskDone();
      body.classList.add('task-success');
      setTimeout(() => { body.classList.remove('task-success'); close(); onDone(); }, 550);
    };
    const fn = GAMES[type];
    current = { type, cleanup: fn(body, done, ctx) || null, ctx };
  }

  function close() {
    if (!current) { if (modal) modal.classList.add('hidden'); return; }
    if (current.cleanup) current.cleanup();
    current = null;
    modal.classList.add('hidden');
    body.innerHTML = '';
  }

  function isOpen() { return !!current; }
  function currentType() { return current && current.type; }

  function makeCanvas(parent, w, h) {
    const c = document.createElement('canvas');
    c.width = w; c.height = h; c.className = 'mg-canvas';
    parent.appendChild(c);
    return c;
  }
  function canvasPos(c, e) {
    const r = c.getBoundingClientRect();
    return { x: (e.clientX - r.left) * c.width / r.width, y: (e.clientY - r.top) * c.height / r.height };
  }
  function loop(fn) {
    let alive = true, last = performance.now();
    const f = (t) => { if (!alive) return; const dt = Math.min(0.05, (t - last) / 1000); last = t; fn(dt, t / 1000); requestAnimationFrame(f); };
    requestAnimationFrame(f);
    return () => { alive = false; };
  }
  function hint(parent, text) {
    const p = document.createElement('div'); p.className = 'mg-hint'; p.textContent = text; parent.appendChild(p); return p;
  }

  const WIRE_COLORS = ['#e5484d', '#3e63dd', '#f5c518', '#c77dff'];

  const GAMES = {
    // Соединить провода одного цвета
    wires(el, done) {
      hint(el, 'Перетащи каждый провод к разъёму того же цвета');
      const c = makeCanvas(el, 420, 360), g = c.getContext('2d');
      const L = shuffle([0, 1, 2, 3]), R = shuffle([0, 1, 2, 3]);
      const ly = i => 60 + i * 80;
      const conn = {}; let drag = null;
      const draw = () => {
        g.fillStyle = '#1d2233'; g.fillRect(0, 0, 420, 360);
        g.fillStyle = '#2a3150'; g.fillRect(10, 20, 50, 320); g.fillRect(360, 20, 50, 320);
        for (let i = 0; i < 4; i++) {
          g.fillStyle = WIRE_COLORS[L[i]]; g.fillRect(10, ly(i) - 9, 50, 18);
          g.fillStyle = WIRE_COLORS[R[i]]; g.fillRect(360, ly(i) - 9, 50, 18);
          g.fillStyle = '#c9cfdd'; g.fillRect(58, ly(i) - 6, 10, 12); g.fillRect(352, ly(i) - 6, 10, 12);
        }
        g.lineCap = 'round'; g.lineWidth = 14;
        for (const k in conn) {
          const i = +k, j = conn[k];
          g.strokeStyle = WIRE_COLORS[L[i]];
          g.beginPath(); g.moveTo(66, ly(i)); g.lineTo(354, ly(j)); g.stroke();
        }
        if (drag) { g.strokeStyle = WIRE_COLORS[L[drag.i]]; g.beginPath(); g.moveTo(66, ly(drag.i)); g.lineTo(drag.x, drag.y); g.stroke(); }
      };
      draw();
      const down = e => {
        const p = canvasPos(c, e);
        for (let i = 0; i < 4; i++) if (conn[i] === undefined && p.x < 100 && Math.abs(p.y - ly(i)) < 30) { drag = { i, x: p.x, y: p.y }; c.setPointerCapture(e.pointerId); }
        draw();
      };
      const move = e => { if (!drag) return; const p = canvasPos(c, e); drag.x = p.x; drag.y = p.y; draw(); };
      const up = e => {
        if (!drag) return;
        const p = canvasPos(c, e);
        for (let j = 0; j < 4; j++)
          if (p.x > 310 && Math.abs(p.y - ly(j)) < 32 && R[j] === L[drag.i]) { conn[drag.i] = j; Sfx.taskStep(); }
        drag = null; draw();
        if (Object.keys(conn).length === 4) done();
      };
      c.addEventListener('pointerdown', down); c.addEventListener('pointermove', move); c.addEventListener('pointerup', up);
    },

    // Нажать числа по порядку
    sequence(el, done) {
      const h = hint(el, 'Нажимай числа от 1 до 10 по порядку');
      const grid = document.createElement('div'); grid.className = 'mg-seq';
      el.appendChild(grid);
      let next = 1;
      const nums = shuffle([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
      const btns = nums.map(n => {
        const b = document.createElement('button'); b.textContent = n; b.className = 'mg-seq-btn';
        b.addEventListener('click', () => {
          if (n === next) { b.classList.add('ok'); next++; Sfx.taskStep(); if (next > 10) done(); }
          else { Sfx.error(); next = 1; btns.forEach(x => x.classList.remove('ok')); grid.classList.add('shake'); h.textContent = 'Ошибка! Сначала…'; setTimeout(() => grid.classList.remove('shake'), 300); }
        });
        grid.appendChild(b); return b;
      });
    },

    // Удерживать кнопку, пока шкала не заполнится
    hold(el, done, ctx) {
      hint(el, ctx.holdHint || 'Удерживай кнопку (или пробел), пока шкала не заполнится');
      const wrap = document.createElement('div'); wrap.className = 'mg-hold';
      wrap.innerHTML = '<div class="mg-gauge"><div class="mg-gauge-fill"></div><div class="mg-gauge-mark"></div></div><button class="mg-hold-btn">УДЕРЖИВАТЬ</button>';
      el.appendChild(wrap);
      const fill = wrap.querySelector('.mg-gauge-fill'), btn = wrap.querySelector('.mg-hold-btn');
      let v = 0, holding = false, tick = 0;
      const on = e => { holding = true; btn.classList.add('active'); e && e.preventDefault(); };
      const off = () => { holding = false; btn.classList.remove('active'); };
      btn.addEventListener('pointerdown', on); window.addEventListener('pointerup', off);
      const kd = e => { if (e.code === 'Space') { on(e); } }, ku = e => { if (e.code === 'Space') off(); };
      window.addEventListener('keydown', kd); window.addEventListener('keyup', ku);
      const stop = loop(dt => {
        if (holding) { v += dt / (ctx.holdTime || 3); tick += dt; if (tick > 0.25) { tick = 0; Sfx.step(); } }
        fill.style.height = (Math.min(1, v) * 100) + '%';
        if (v >= 1) { stop(); done(); }
      });
      return () => { stop(); window.removeEventListener('pointerup', off); window.removeEventListener('keydown', kd); window.removeEventListener('keyup', ku); };
    },

    // Нажать «старт» и дождаться загрузки
    download(el, done) {
      const files = ['nav_core.dat', 'tifon_orbit.map', 'sig_echo_01.raw', 'crew_logs.db', 'crystal_spec.bin', 'route_earth.trk'];
      hint(el, 'Запусти передачу и не отходи от терминала');
      const w = document.createElement('div'); w.className = 'mg-dl';
      w.innerHTML = '<div class="mg-dl-file">Ожидание…</div><div class="mg-bar"><div class="mg-bar-fill"></div></div><button class="mg-btn">НАЧАТЬ</button>';
      el.appendChild(w);
      const f = w.querySelector('.mg-bar-fill'), fl = w.querySelector('.mg-dl-file'), b = w.querySelector('button');
      let v = -1, stop = null;
      b.addEventListener('click', () => {
        if (v >= 0) return; v = 0; b.disabled = true; b.textContent = 'ПЕРЕДАЧА…';
        stop = loop(dt => {
          v += dt / 5;
          f.style.width = Math.min(100, v * 100) + '%';
          fl.textContent = files[Math.min(files.length - 1, Math.floor(v * files.length))] + '  ' + Math.min(100, Math.floor(v * 100)) + '%';
          if (v >= 1) { stop(); done(); }
        });
      });
      return () => stop && stop();
    },

    // Поймать маркер в зелёной зоне три раза
    timing(el, done) {
      const h = hint(el, 'Нажми «Стабилизировать», когда маркер в зелёной зоне (3 раза)');
      const c = makeCanvas(el, 420, 140), g = c.getContext('2d');
      const b = document.createElement('button'); b.className = 'mg-btn'; b.textContent = 'СТАБИЛИЗИРОВАТЬ'; el.appendChild(b);
      let step = 0, t = 0, zone = rand(0.2, 0.8), zw = 0.2, speed = 1.3;
      const pos = () => 0.5 + 0.5 * Math.sin(t * speed * Math.PI);
      const press = () => {
        const p = pos();
        if (Math.abs(p - zone) < zw / 2) { step++; Sfx.taskStep(); zone = rand(0.15, 0.85); zw *= 0.8; speed *= 1.25; if (step >= 3) { stop(); done(); } }
        else { Sfx.error(); step = Math.max(0, step - 1); }
        h.textContent = `Стабилизировано: ${step} / 3`;
      };
      b.addEventListener('click', press);
      const kd = e => { if (e.code === 'Space') { e.preventDefault(); press(); } };
      window.addEventListener('keydown', kd);
      const stop = loop(dt => {
        t += dt;
        g.fillStyle = '#1d2233'; g.fillRect(0, 0, 420, 140);
        g.fillStyle = '#3a1f24'; g.fillRect(20, 50, 380, 40);
        g.fillStyle = '#2fbf71'; g.fillRect(20 + (zone - zw / 2) * 380, 50, zw * 380, 40);
        g.fillStyle = '#fff'; g.fillRect(20 + pos() * 380 - 3, 40, 6, 60);
        for (let k = 0; k < 3; k++) { g.fillStyle = k < step ? '#ffb547' : '#444b63'; g.beginPath(); g.arc(170 + k * 40, 120, 9, 0, Math.PI * 2); g.fill(); }
      });
      return () => { stop(); window.removeEventListener('keydown', kd); };
    },

    // Совместить волну ползунками
    wave(el, done) {
      hint(el, 'Подстрой амплитуду и частоту, чтобы волны совпали');
      const c = makeCanvas(el, 420, 220), g = c.getContext('2d');
      const ctrls = document.createElement('div'); ctrls.className = 'mg-sliders';
      ctrls.innerHTML = '<label>Амплитуда <input type="range" min="0" max="100" value="10"></label><label>Частота <input type="range" min="0" max="100" value="90"></label>';
      el.appendChild(ctrls);
      const [sa, sf] = ctrls.querySelectorAll('input');
      const A = rand(0.25, 0.95), F = rand(0.2, 0.9);
      let ok = 0, t = 0;
      const stop = loop(dt => {
        t += dt;
        const a = sa.value / 100, f = sf.value / 100;
        const match = Math.abs(a - A) < 0.07 && Math.abs(f - F) < 0.07;
        ok = match ? ok + dt : 0;
        g.fillStyle = '#0d1a22'; g.fillRect(0, 0, 420, 220);
        g.strokeStyle = 'rgba(89,199,255,0.15)'; g.lineWidth = 1;
        for (let x = 0; x < 420; x += 30) { g.beginPath(); g.moveTo(x, 0); g.lineTo(x, 220); g.stroke(); }
        const wave = (amp, fr, col, lw) => {
          g.strokeStyle = col; g.lineWidth = lw; g.beginPath();
          for (let x = 0; x <= 420; x += 3) { const y = 110 + Math.sin(x * (0.01 + fr * 0.05) + t * 3) * amp * 90; x ? g.lineTo(x, y) : g.moveTo(x, y); }
          g.stroke();
        };
        wave(A, F, 'rgba(255,255,255,0.35)', 6);
        wave(a, f, match ? '#2fbf71' : '#59c7ff', 3);
        if (ok > 0.7) { stop(); done(); }
      });
      return stop;
    },

    // Поймать летающие споры
    samples(el, done) {
      const h = hint(el, 'Поймай все светящиеся споры (кликай по ним)');
      const c = makeCanvas(el, 420, 320), g = c.getContext('2d');
      const sp = []; for (let i = 0; i < 7; i++) sp.push({ x: rand(40, 380), y: rand(40, 280), vx: rand(-90, 90), vy: rand(-90, 90), r: 18, alive: true, ph: Math.random() * 6 });
      let left = sp.length;
      c.addEventListener('pointerdown', e => {
        const p = canvasPos(c, e);
        for (const s of sp) if (s.alive && dist(p.x, p.y, s.x, s.y) < s.r + 10) { s.alive = false; left--; Sfx.taskStep(); h.textContent = `Осталось спор: ${left}`; if (!left) { stop(); done(); } break; }
      });
      const stop = loop((dt, t) => {
        g.fillStyle = '#10251a'; g.fillRect(0, 0, 420, 320);
        g.strokeStyle = 'rgba(123,224,123,0.2)'; g.lineWidth = 2; g.strokeRect(10, 10, 400, 300);
        for (const s of sp) {
          if (!s.alive) continue;
          s.x += s.vx * dt; s.y += s.vy * dt;
          if (s.x < 25 || s.x > 395) s.vx *= -1; if (s.y < 25 || s.y > 295) s.vy *= -1;
          const gr = g.createRadialGradient(s.x, s.y, 2, s.x, s.y, s.r * 1.6);
          gr.addColorStop(0, '#f4ffc8'); gr.addColorStop(0.4, '#9ef06c'); gr.addColorStop(1, 'rgba(60,160,60,0)');
          g.fillStyle = gr; g.beginPath(); g.arc(s.x, s.y, s.r * (1.4 + 0.2 * Math.sin(t * 5 + s.ph)), 0, Math.PI * 2); g.fill();
        }
      });
      return stop;
    },

    // Биоскан — стоять 6 секунд, окружающие это видят
    scan(el, done, ctx) {
      hint(el, 'Стой на платформе — идёт сканирование');
      const w = document.createElement('div'); w.className = 'mg-scan';
      w.innerHTML = '<div class="mg-scan-lines"></div><div class="mg-bar"><div class="mg-bar-fill"></div></div>';
      el.appendChild(w);
      const lines = w.querySelector('.mg-scan-lines'), f = w.querySelector('.mg-bar-fill');
      const facts = [`ID: ${ctx.player.name.toUpperCase()}`, 'Рост: 172 см', 'Пульс: 78', 'Группа крови: II+', 'Кристаллических структур: НЕ ОБНАРУЖЕНО', 'Статус: ЧЕЛОВЕК'];
      let v = 0;
      ctx.player.scanning = true;
      const stop = loop(dt => {
        v += dt / 6;
        f.style.width = Math.min(100, v * 100) + '%';
        const n = Math.min(facts.length, Math.floor(v * facts.length + 0.3));
        lines.innerHTML = facts.slice(0, n).map(s => `<div>${s}</div>`).join('');
        if (v >= 1) { stop(); ctx.player.scanning = false; done(); }
      });
      return () => { stop(); ctx.player.scanning = false; };
    },

    // Включить все рубильники (свет)
    switches(el, done) {
      hint(el, 'Включи все рубильники, чтобы вернуть свет');
      const w = document.createElement('div'); w.className = 'mg-switches'; el.appendChild(w);
      const st = [];
      for (let i = 0; i < 6; i++) st.push(Math.random() < 0.4);
      if (st.every(Boolean)) st[2] = false;
      const btns = st.map((on, i) => {
        const b = document.createElement('button'); b.className = 'mg-switch' + (on ? ' on' : '');
        b.innerHTML = '<span></span>';
        b.addEventListener('click', () => { st[i] = !st[i]; b.classList.toggle('on', st[i]); Sfx.click(); if (st.every(Boolean)) done(); });
        w.appendChild(b); return b;
      });
    },

    // Приложить ладонь и удерживать
    handprint(el, done) {
      return GAMES.hold(el, done, { holdHint: 'Приложи ладонь к сканеру и держи, пока контур не остынет', holdTime: 2.5 });
    },
  };

  return { init, open, close, isOpen, currentType };
})();
