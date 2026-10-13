// Процедурный звук (WebAudio) — без внешних файлов
'use strict';

const Sfx = (() => {
  let ac = null, master = null, hum = null;
  let volume = 0.6;
  function ensure() {
    if (ac) return ac;
    try {
      ac = new (window.AudioContext || window.webkitAudioContext)();
      master = ac.createGain(); master.gain.value = volume; master.connect(ac.destination);
    } catch (e) { ac = null; }
    return ac;
  }
  function tone(freq, dur, type = 'sine', vol = 0.3, slide = 0, delay = 0) {
    if (!ensure()) return;
    const t = ac.currentTime + delay;
    const o = ac.createOscillator(), g = ac.createGain();
    o.type = type; o.frequency.setValueAtTime(freq, t);
    if (slide) o.frequency.exponentialRampToValueAtTime(Math.max(20, freq + slide), t + dur);
    g.gain.setValueAtTime(0.0001, t);
    g.gain.exponentialRampToValueAtTime(vol, t + 0.01);
    g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
    o.connect(g); g.connect(master);
    o.start(t); o.stop(t + dur + 0.05);
  }
  function noise(dur, vol = 0.3, filt = 1200, delay = 0) {
    if (!ensure()) return;
    const t = ac.currentTime + delay;
    const len = Math.floor(ac.sampleRate * dur);
    const buf = ac.createBuffer(1, len, ac.sampleRate);
    const d = buf.getChannelData(0);
    for (let i = 0; i < len; i++) d[i] = (Math.random() * 2 - 1) * (1 - i / len);
    const src = ac.createBufferSource(); src.buffer = buf;
    const f = ac.createBiquadFilter(); f.type = 'lowpass'; f.frequency.value = filt;
    const g = ac.createGain(); g.gain.value = vol;
    src.connect(f); f.connect(g); g.connect(master);
    src.start(t);
  }
  return {
    unlock() { ensure(); if (ac && ac.state === 'suspended') ac.resume(); },
    setVolume(v) { volume = v; if (master) master.gain.value = v; },
    click() { tone(660, 0.06, 'square', 0.08); },
    hover() { tone(880, 0.03, 'sine', 0.04); },
    step() { noise(0.05, 0.05, 500); },
    taskStep() { tone(520, 0.08, 'triangle', 0.15); tone(780, 0.1, 'triangle', 0.12, 0, 0.06); },
    taskDone() { [523, 659, 784, 1046].forEach((f, i) => tone(f, 0.18, 'triangle', 0.18, 0, i * 0.08)); },
    error() { tone(160, 0.25, 'sawtooth', 0.15, -60); },
    kill() {
      noise(0.35, 0.5, 2500);
      [1800, 2400, 3100, 2700].forEach((f, i) => tone(f, 0.5, 'sine', 0.12, -400, 0.05 + i * 0.05));
      tone(90, 0.5, 'sawtooth', 0.25, -50);
    },
    vent() { noise(0.3, 0.35, 700); tone(200, 0.2, 'square', 0.06, -120); },
    alarm() { for (let i = 0; i < 4; i++) { tone(880, 0.22, 'square', 0.12, -300, i * 0.5); tone(660, 0.22, 'square', 0.12, 300, i * 0.5 + 0.25); } },
    sabotage() { for (let i = 0; i < 3; i++) tone(320, 0.35, 'sawtooth', 0.12, -100, i * 0.4); },
    report() { tone(240, 0.6, 'sawtooth', 0.2, 400); noise(0.5, 0.2, 900, 0.1); },
    vote() { tone(440, 0.08, 'square', 0.1); tone(330, 0.1, 'square', 0.1, 0, 0.08); },
    eject() { noise(1.6, 0.4, 400); tone(120, 1.4, 'sine', 0.2, -80); },
    chat() { tone(1200, 0.04, 'sine', 0.05); },
    win() { [392, 523, 659, 784, 1046].forEach((f, i) => tone(f, 0.4, 'triangle', 0.18, 0, i * 0.15)); },
    lose() { [523, 466, 392, 311, 262].forEach((f, i) => tone(f, 0.45, 'sawtooth', 0.12, 0, i * 0.18)); },
    humStart() {
      if (!ensure() || hum) return;
      const o = ac.createOscillator(), o2 = ac.createOscillator(), g = ac.createGain();
      o.type = 'sine'; o.frequency.value = 55; o2.type = 'sine'; o2.frequency.value = 82.5;
      g.gain.value = 0.035;
      o.connect(g); o2.connect(g); g.connect(master); o.start(); o2.start();
      hum = { o, o2, g };
    },
    humStop() { if (hum) { hum.o.stop(); hum.o2.stop(); hum = null; } },
  };
})();
