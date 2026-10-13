// Собрание: обсуждение, реплики ботов, голосование
'use strict';

const Meeting = (() => {
  let M = null;
  let el = {};

  function init() {
    el.root = document.getElementById('meeting');
    el.title = document.getElementById('m-title');
    el.sub = document.getElementById('m-sub');
    el.grid = document.getElementById('m-grid');
    el.chat = document.getElementById('m-chat');
    el.input = document.getElementById('m-input');
    el.send = document.getElementById('m-send');
    el.skip = document.getElementById('m-skip');
    el.timer = document.getElementById('m-timer');
    el.quick = document.getElementById('m-quick');
    el.send.addEventListener('click', sendChat);
    el.input.addEventListener('keydown', e => { e.stopPropagation(); if (e.key === 'Enter') sendChat(); });
    el.input.addEventListener('keyup', e => e.stopPropagation());
    el.skip.addEventListener('click', () => humanVote('skip'));
  }

  const rq = s => `«${s}»`;

  function cred(listener, speaker) {
    return clamp(1 - (listener.sus[speaker.id] || 0) / 120, 0, 1.3);
  }

  function start(caller, victimId) {
    const victim = victimId != null ? byId(victimId) : null;
    const body = victim ? G.bodies.find(b => b.victim === victimId) : null;
    M = {
      caller, victim, room: body ? body.room : null,
      timeLeft: G.settings.discuss, votes: {}, queue: [], done: false, t: 0, selected: null,
      voteAt: {}, revealT: 0,
    };
    el.root.classList.remove('hidden');
    el.title.textContent = victim ? 'ОБНАРУЖЕНО ТЕЛО' : 'ЭКСТРЕННОЕ СОБРАНИЕ';
    el.sub.textContent = victim
      ? `${caller.name} нашёл(ла) кристаллизованное тело: ${victim.name} (${roomName(M.room)})`
      : `${caller.name} нажал(а) экстренную кнопку`;
    el.chat.innerHTML = '';
    el.input.value = '';
    el.skip.disabled = !G.me.alive;
    el.input.disabled = !G.me.alive;
    el.input.placeholder = G.me.alive ? 'Напиши что-нибудь… (упомяни имя, чтобы обвинить)' : 'Призраки не могут говорить';
    buildGrid();
    buildQuick();
    planStatements();
    const alive = G.players.filter(p => p.alive && p.isBot);
    const span = Math.max(10, G.settings.discuss - 6);
    for (const p of alive) M.voteAt[p.id] = rand(span * 0.35, span);
    sys(victim ? `Тело: ${victim.name}. Отсек: ${roomName(M.room)}.` : 'Собрание созвано экстренной кнопкой.');
    if (!G.me.alive) sys('Ты — призрак. Ты можешь только наблюдать.');
  }

  function buildGrid() {
    el.grid.innerHTML = '';
    for (const p of G.players) {
      if (p.ejected && !p.alive) { /* изгнанных показываем зачёркнутыми */ }
      const card = document.createElement('div');
      card.className = 'm-card' + (p.alive ? '' : ' dead') + (p === G.me ? ' me' : '');
      card.dataset.id = p.id;
      const cv = document.createElement('canvas'); cv.width = 64; cv.height = 64;
      const g = cv.getContext('2d');
      if (p.alive) drawCrew(g, 32, 60, p.color, { scale: 0.9, echoEyes: G.me.isEcho && p.isEcho });
      else drawBody(g, 32, 40, p.color, 0);
      const nm = document.createElement('div'); nm.className = 'm-name';
      nm.textContent = p.name + (p === G.me ? ' (ты)' : '');
      if (G.me.isEcho && p.isEcho) nm.style.color = '#ff7b7b';
      const badges = document.createElement('div'); badges.className = 'm-badges';
      const voted = document.createElement('div'); voted.className = 'm-voted'; voted.textContent = 'ПРОГОЛОСОВАЛ';
      const conf = document.createElement('div'); conf.className = 'm-confirm';
      conf.innerHTML = '<button class="yes">✔</button><button class="no">✖</button>';
      card.append(cv, nm, badges, voted, conf);
      if (p === M.caller) { const mg = document.createElement('div'); mg.className = 'm-mega'; mg.textContent = M.victim ? '⚠' : '📢'; card.append(mg); }
      card.addEventListener('click', e => {
        if (!G.me.alive || M.votes[G.me.id] !== undefined || !p.alive || M.done) return;
        if (e.target.classList.contains('yes')) { humanVote(p.id); return; }
        if (e.target.classList.contains('no')) { card.classList.remove('selecting'); return; }
        el.grid.querySelectorAll('.selecting').forEach(c => c.classList.remove('selecting'));
        card.classList.add('selecting');
        Sfx.click();
      });
      el.grid.appendChild(card);
    }
  }

  function buildQuick() {
    el.quick.innerHTML = '';
    const phrases = ['Где тело?', 'Я делал(а) задания', 'Кто был рядом?', 'Пропускаем', 'Я видел(а) подозрительное'];
    for (const ph of phrases) {
      const b = document.createElement('button'); b.textContent = ph;
      b.addEventListener('click', () => { if (!G.me.alive) return; el.input.value = ph; sendChat(); });
      el.quick.appendChild(b);
    }
  }

  function card(id) { return el.grid.querySelector(`.m-card[data-id="${id}"]`); }

  function sys(text) {
    const d = document.createElement('div'); d.className = 'm-msg sys'; d.textContent = text;
    el.chat.appendChild(d); el.chat.scrollTop = el.chat.scrollHeight;
  }
  function post(p, text) {
    const d = document.createElement('div'); d.className = 'm-msg';
    const cv = document.createElement('canvas'); cv.width = 32; cv.height = 32;
    drawCrew(cv.getContext('2d'), 16, 31, p.color, { scale: 0.45 });
    const tx = document.createElement('div');
    tx.innerHTML = `<b style="color:${p.color.body}">${p.name}</b><span></span>`;
    tx.querySelector('span').textContent = text;
    d.append(cv, tx);
    if (!p.alive) d.classList.add('ghost');
    el.chat.appendChild(d); el.chat.scrollTop = el.chat.scrollHeight;
    Sfx.chat();
  }

  // Реплика с влиянием на подозрения слушателей
  function say(p, text, effects = [], delay = 0) {
    M.queue.push({ at: M.t + delay, p, text, effects });
  }
  function applyEffects(speaker, effects) {
    for (const ef of effects) {
      const tgt = byId(ef.target);
      for (const l of G.players) {
        if (!l.isBot || !l.alive || l === speaker || l.id === ef.target || l.isEcho) continue;
        l.sus[ef.target] = (l.sus[ef.target] || 0) + ef.amount * cred(l, speaker);
      }
      // обвинённый бот защищается
      if (ef.amount > 15 && tgt && tgt.isBot && tgt.alive && Math.random() < 0.75) {
        const lines = [`Это не я! Я был(а) в отсеке ${rq(roomName(tgt.lastRoom))}.`, `Чего? Я вообще делал(а) задания.`, `${speaker.name}, ты сам(а) подозрительно себя ведёшь.`, 'Не я. Проверьте лучше других.'];
        const line = pick(lines);
        say(tgt, line, line.includes(speaker.name) ? [{ target: speaker.id, amount: 8 }] : [], rand(1.2, 2.5));
      }
    }
  }

  function planStatements() {
    const speakers = G.players.filter(p => p.isBot && p.alive);
    shuffle(speakers);
    const reporter = M.caller;
    if (reporter.isBot) { speakers.splice(speakers.indexOf(reporter), 1); speakers.unshift(reporter); }
    let delay = 1.2;
    const crew = G.players.filter(p => p.alive && !p.isEcho);
    for (const b of speakers) {
      const lines = [];
      if (b === reporter && M.victim) lines.push([`Тело ${M.victim.name} в отсеке ${rq(roomName(M.room))}. Весь в кристаллах…`, []]);
      if (b === reporter && !M.victim) lines.push(['Это я нажал(а) кнопку.', []]);
      if (!b.isEcho) {
        const kill = b.mem.find(m => m.type === 'kill');
        const vent = b.mem.find(m => m.type === 'vent');
        if (kill) lines.push([`${byId(kill.who).name} — Эхо! Я видел(а), как ${byId(kill.who).name} кристаллизовал(а) ${byId(kill.victim).name}!`, [{ target: kill.who, amount: 90 }]]);
        else if (vent) lines.push([`${byId(vent.who).name} вылез(ла) из вентиляции в отсеке ${rq(roomName(vent.room))}! Голосуем!`, [{ target: vent.who, amount: 80 }]]);
        else {
          let near = null;
          if (M.room) for (const id in b.seen) {
            const s = b.seen[id], p = byId(+id);
            if (p && p.alive && p !== b && s.room === M.room && G.t - s.t < 25) { near = p; break; }
          }
          if (near) { b.sus[near.id] += 35; lines.push([`Я видел(а) ${near.name} в отсеке ${rq(roomName(M.room))} незадолго до этого.`, [{ target: near.id, amount: 22 }]]); }
          const sc = b.mem.find(m => m.type === 'scan' && byId(m.who).alive);
          if (sc && Math.random() < 0.8) lines.push([`${byId(sc.who).name} прошёл(ла) биоскан у меня на глазах — точно человек.`, [{ target: sc.who, amount: -60 }]]);
          if (!near && !sc) {
            let comp = null;
            for (const id in b.seen) { const s = b.seen[id], p = byId(+id); if (p && p.alive && G.t - s.t < 12 && Math.random() < 0.6) { comp = p; break; } }
            if (comp) lines.push([`Я был(а) с ${comp.name} в отсеке ${rq(roomName(b.seen[comp.id].room))}.`, [{ target: comp.id, amount: -10 }]]);
            else lines.push([pick([`Я был(а) в отсеке ${rq(roomName(b.lastRoom))}, ничего не видел(а).`, 'Ничего не видел(а). Кто где был?', `Делал(а) задания в ${rq(roomName(b.lastRoom))}.`, 'Пусть каждый скажет, где был.']), []]);
          }
        }
      } else {
        // Эхо врёт
        b.voteTarget = null;
        const accusers = crew.filter(c => c.isBot && (c.sus[b.id] || 0) > 40);
        if (Math.random() < 0.5 * ({ easy: 0.6, normal: 1, hard: 1.4 }[G.settings.difficulty] || 1) && crew.length) {
          let tgt = accusers.length ? pick(accusers) : pick(crew);
          if (!G.me.isEcho && G.me.alive && Math.random() < 0.3) tgt = G.me;
          b.voteTarget = tgt.id;
          lines.push([pick([`Я видел(а) ${tgt.name} рядом с отсеком ${rq(roomName(M.room || pick(ROOMS).id))}. Странно себя вёл(а).`, `${tgt.name} ходил(а) за мной хвостом. Подозрительно.`, `А где был(а) ${tgt.name}? Я ${tgt.name} давно не видел(а) на заданиях.`]), [{ target: tgt.id, amount: 22 }]]);
        } else lines.push([pick([`Я был(а) в отсеке ${rq(pick(ROOMS).name)}, чинил(а) там всё.`, 'Ничего не видел(а), честно.', 'Давайте не спешить с выводами.', 'Может, пропустим? Мало информации.']), []]);
      }
      for (const [text, eff] of lines) { say(b, text, eff, delay); delay += rand(1.6, 3.2); }
    }
  }

  function sendChat() {
    const me = G.me;
    if (!M || M.done || !me.alive) return;
    const text = el.input.value.trim().slice(0, 140);
    if (!text) return;
    el.input.value = '';
    post(me, text);
    const low = text.toLowerCase();
    const effects = [];
    for (const p of G.players) {
      if (p === me || !p.alive) continue;
      const nm = p.name.toLowerCase();
      if (low.includes(nm.slice(0, Math.max(3, nm.length - 1)))) {
        const defend = /чист|не он|не она|невинов|был со мной|была со мной|скан/.test(low);
        effects.push({ target: p.id, amount: defend ? -25 : 18 });
      }
    }
    if (effects.length) applyEffects(me, effects);
    if (/пропус|скип|skip/.test(low)) M.skipBias = (M.skipBias || 0) + 10;
    // кто-то отвечает
    if (Math.random() < 0.6 && !effects.length) {
      const r = pick(G.players.filter(p => p.isBot && p.alive));
      if (r) say(r, pick(['Хм, ' + me.name + ', а ты сам(а) где был(а)?', 'Согласен(на).', 'Нужно больше информации.', 'Ок, слушаю.', 'Давайте голосовать по фактам.']), [], rand(1, 2));
    } else if (effects.length && effects[0].amount > 0) {
      const tgt = effects[0].target;
      const agree = G.players.find(p => p.isBot && p.alive && p.id !== tgt && (p.sus[tgt] || 0) > 25);
      if (agree) say(agree, `Поддерживаю, ${byId(tgt).name} ведёт себя странно.`, [{ target: tgt, amount: 10 }], rand(2.5, 4));
    }
  }

  function castVote(p, target) {
    if (M.votes[p.id] !== undefined) return;
    M.votes[p.id] = target;
    const c = card(p.id); if (c) c.classList.add('has-voted');
    Sfx.vote();
    if (Object.keys(M.votes).length >= G.players.filter(q => q.alive).length && M.timeLeft > 2.5) M.timeLeft = 2.5;
  }
  function humanVote(target) {
    if (!M || M.done || !G.me.alive || M.votes[G.me.id] !== undefined) return;
    el.grid.querySelectorAll('.selecting').forEach(c => c.classList.remove('selecting'));
    castVote(G.me, target);
    el.skip.disabled = true;
    // голос игрока немного влияет на ботов
    if (target !== 'skip') for (const l of G.players) if (l.isBot && l.alive && !l.isEcho && l.id !== target && M.votes[l.id] === undefined) l.sus[target] = (l.sus[target] || 0) + 10 * cred(l, G.me);
  }

  function botVote(b) {
    const alive = G.players.filter(p => p.alive && p !== b);
    const tally = {};
    for (const k in M.votes) { const v = M.votes[k]; if (v !== 'skip') tally[v] = (tally[v] || 0) + 1; }
    if (b.isEcho) {
      if (b.voteTarget != null && byId(b.voteTarget).alive) return castVote(b, b.voteTarget);
      let lead = null, lv = 0;
      for (const k in tally) { const p = byId(+k); if (tally[k] > lv && !p.isEcho) { lv = tally[k]; lead = +k; } }
      if (lead != null) return castVote(b, lead);
      const crew = alive.filter(p => !p.isEcho);
      let best = null, bs = -1e9;
      for (const c of crew) { let s = 0; for (const l of G.players) if (l.isBot && !l.isEcho && l.alive) s += l.sus[c.id] || 0; if (s > bs) { bs = s; best = c; } }
      return castVote(b, bs > 30 && best ? best.id : 'skip');
    }
    let best = null, bs = -1e9;
    for (const p of alive) { let s = (b.sus[p.id] || 0) + (tally[p.id] || 0) * 6; if (s > bs) { bs = s; best = p; } }
    const thr = 35 + (M.skipBias || 0);
    if (best && bs >= thr) return castVote(b, best.id);
    if (best && bs >= thr - 12 && Math.random() < 0.4) return castVote(b, best.id);
    castVote(b, 'skip');
  }

  function update(dt) {
    if (!M) return;
    M.t += dt;
    if (M.done) {
      M.revealT -= dt;
      if (M.revealT <= 0) finish();
      return;
    }
    M.timeLeft -= dt;
    el.timer.textContent = `Голосование закончится через ${Math.max(0, Math.ceil(M.timeLeft))} с`;
    while (M.queue.length && M.queue[0].at <= M.t) {
      const q = M.queue.shift();
      if (!q.p.alive) continue;
      post(q.p, q.text);
      applyEffects(q.p, q.effects);
      M.queue.sort((a, b) => a.at - b.at);
    }
    for (const id in M.voteAt) {
      if (M.votes[id] === undefined && M.t >= M.voteAt[id]) botVote(byId(+id));
    }
    if (M.timeLeft <= 0) reveal();
  }

  function reveal() {
    M.done = true;
    for (const p of G.players) if (p.alive && M.votes[p.id] === undefined) M.votes[p.id] = 'skip';
    const count = {}; let skip = 0;
    for (const k in M.votes) {
      const v = M.votes[k], voter = byId(+k);
      const dot = document.createElement('span'); dot.className = 'm-dot'; dot.style.background = voter.color.body; dot.title = voter.name;
      if (v === 'skip') { skip++; document.getElementById('m-skip-dots').appendChild(dot); }
      else { count[v] = (count[v] || 0) + 1; const c = card(v); if (c) c.querySelector('.m-badges').appendChild(dot); }
    }
    let top = null, tv = 0, tie = false;
    for (const k in count) { if (count[k] > tv) { tv = count[k]; top = +k; tie = false; } else if (count[k] === tv) tie = true; }
    if (tie || skip >= tv) top = null;
    M.result = top;
    el.timer.textContent = top == null ? (tie ? 'Ничья — никто не изгнан' : 'Большинство пропустило голосование') : `Изгнан(а): ${byId(top).name}`;
    M.revealT = 3.5;
  }

  function finish() {
    const res = M.result;
    M = null;
    el.root.classList.add('hidden');
    document.getElementById('m-skip-dots').innerHTML = '';
    UI.showEject(res != null ? byId(res) : null, () => afterMeeting(res));
  }

  return { init, start, update, active: () => !!M };
})();
