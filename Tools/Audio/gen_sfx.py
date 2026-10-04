"""Процедурные звуковые эффекты (без чужих ассетов) -> Assets/_Project/Resources/SW_Audio/*.wav
Запуск: python Tools/Audio/gen_sfx.py   (нужен numpy)"""
import os
import wave
import numpy as np

SR = 22050
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "_Project", "Resources", "SW_Audio")
rng = np.random.default_rng(7)


def t(d):
    return np.arange(int(SR * d)) / SR


def env(n, a=0.005, r=None, curve=3.0):
    """Атака a секунд, затем экспоненциальный спад (curve — скорость)."""
    x = np.ones(n)
    na = max(1, int(a * SR))
    x[:na] = np.linspace(0, 1, na)
    k = np.linspace(0, 1, n - na)
    x[na:] = np.exp(-curve * k) if r is None else np.clip(1 - k / r, 0, 1)
    return x


def chirp(f0, f1, d, kind="exp"):
    tt = t(d)
    if kind == "exp":
        f = f0 * (f1 / f0) ** (tt / d)
    else:
        f = f0 + (f1 - f0) * tt / d
    return np.sin(2 * np.pi * np.cumsum(f) / SR), f


def lp(x, a):
    """Простой однополюсный ФНЧ (a 0..1, меньше — глуше)."""
    y = np.zeros_like(x)
    acc = 0.0
    for i, v in enumerate(x):
        acc += a * (v - acc)
        y[i] = acc
    return y


def lp_fast(x, cutoff):
    a = 1 - np.exp(-2 * np.pi * cutoff / SR)
    from itertools import accumulate
    out = np.fromiter(accumulate(x, lambda acc, v: acc + a * (v - acc)), float, len(x))
    return out


def hp_fast(x, cutoff):
    return x - lp_fast(x, cutoff)


def noise(d):
    return rng.uniform(-1, 1, int(SR * d))


def reverb(x, amt=0.25, d=0.9):
    """Короткий «пустынный» хвост: несколько затухающих отражений."""
    y = np.concatenate([x, np.zeros(int(SR * d))])
    for delay, g in ((0.031, 0.5), (0.047, 0.4), (0.071, 0.33), (0.113, 0.25), (0.167, 0.18), (0.241, 0.12)):
        k = int(delay * SR)
        y[k:k + len(x)] += x * g * amt
    return y


def norm(x, peak=0.89):
    m = np.max(np.abs(x)) or 1
    return x / m * peak


def save(name, x, peak=0.89):
    x = norm(x, peak)
    os.makedirs(OUT, exist_ok=True)
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((x * 32767).astype(np.int16).tobytes())
    print(name, f"{len(x) / SR:.2f}s")


# ---------------------------------------------------------------- бластеры: «пружинный» дисперсионный свист
def blaster(f0, f1, d, body=0.0, grit=0.08):
    n = int(SR * d)
    s = np.zeros(n)
    for k, (m, g) in enumerate(((1.0, 1.0), (1.51, 0.45), (2.03, 0.25), (0.5, 0.35))):
        c, _ = chirp(f0 * m, f1 * m, d)
        s += c * g
    s += 0.3 * np.sin(2 * np.pi * 37 * t(d)) * s                      # металлическая модуляция
    s += grit * hp_fast(noise(d), 2500) * env(n, 0.001, curve=25)      # щелчок
    if body:
        s += body * np.sin(2 * np.pi * np.cumsum(np.linspace(140, 45, n)) / SR) * env(n, 0.002, curve=8)
    return reverb(s * env(n, 0.002, curve=4.5), 0.3, 0.5)


save("blaster", blaster(2600, 260, 0.26))
save("blaster_heavy", blaster(1700, 170, 0.34, body=0.5, grit=0.12))
save("cannon", blaster(900, 70, 0.6, body=1.2, grit=0.3), 0.95)

# ---------------------------------------------------------------- попадание / рикошет
n = int(SR * 0.22)
x = hp_fast(noise(0.22), 900) * env(n, 0.001, curve=18) + 0.6 * np.sin(2 * np.pi * np.cumsum(np.linspace(260, 60, n)) / SR) * env(n, 0.001, curve=14)
save("impact", reverb(x, 0.2, 0.3))

# ---------------------------------------------------------------- взрыв
d = 2.2
n = int(SR * d)
boom = lp_fast(noise(d), 160) * 6 * env(n, 0.004, curve=3.2)
crack = hp_fast(noise(d), 1500) * env(n, 0.001, curve=14)
rumble = np.sin(2 * np.pi * np.cumsum(np.linspace(70, 28, n)) / SR) * env(n, 0.01, curve=4)
debris = lp_fast(noise(d), 1200) * env(n, 0.05, curve=5) * (rng.random(n) > 0.997) * 4
save("explosion", reverb(boom + 0.7 * crack + 0.9 * rumble + debris, 0.35, 1.0), 0.97)


# ---------------------------------------------------------------- световой меч
def hum(d, f=92.0, wob=0.0):
    tt = t(d)
    ph = 2 * np.pi * f * tt
    if wob:
        ph += wob * np.sin(2 * np.pi * 0.5 * tt)
    s = np.sin(ph) + 0.55 * np.sin(2 * ph + 0.3) + 0.35 * np.sin(3 * ph) + 0.2 * np.sin(4 * ph + 1)
    s += 0.35 * np.sin(2 * np.pi * (f + 1.5) * tt)                     # биения второго «мотора»
    s += 0.12 * np.sign(np.sin(ph)) * 0.5                                 # жужжание кинескопа
    s += 0.05 * lp_fast(noise(d), 3000)
    return s


# петля 2 с: целое число периодов 92 и 93.5 Гц -> бесшовная
save("saber_hum", hum(2.0, 92.0) * 0.8, 0.6)
n = int(SR * 0.75)
on = hum(0.75) * np.linspace(0.2, 1, n) ** 0.5
ph = np.cumsum(np.linspace(180, 92, n)) / SR
on = 0.6 * on + 0.6 * np.sin(2 * np.pi * ph * 2) * env(n, 0.01, curve=3) + 0.5 * hp_fast(noise(0.75), 2000) * env(n, 0.001, curve=9)
save("saber_on", reverb(on * env(n, 0.005, r=1.15), 0.2, 0.3))
off = hum(0.6)[::-1] * np.linspace(1, 0, int(SR * 0.6)) ** 1.5
save("saber_off", off)


def swing(d, f0, f1):
    n = int(SR * d)
    ph = 2 * np.pi * np.cumsum(np.concatenate([np.linspace(f0, f1, n // 2), np.linspace(f1, f0 * 0.9, n - n // 2)])) / SR
    s = np.sin(ph) + 0.5 * np.sin(2 * ph) + 0.3 * np.sin(3 * ph) + 0.25 * hp_fast(noise(d), 1500) * np.hanning(n)
    return s * np.hanning(n) ** 0.7


save("saber_swing1", swing(0.42, 95, 165))
save("saber_swing2", swing(0.5, 90, 140))
d = 0.7
n = int(SR * d)
cl = hp_fast(noise(d), 1200) * env(n, 0.001, curve=7) * (1 + 0.8 * (rng.random(n) > 0.92))
cl += 0.8 * np.sin(2 * np.pi * np.cumsum(np.linspace(1400, 500, n)) / SR) * env(n, 0.001, curve=10)
cl += 0.5 * hum(d, 120) * env(n, 0.001, curve=5)
save("saber_clash", reverb(cl, 0.25, 0.4))
d = 0.35
n = int(SR * d)
df = hp_fast(noise(d), 1800) * env(n, 0.001, curve=14) + 0.9 * np.sin(2 * np.pi * np.cumsum(np.linspace(2400, 700, n)) / SR) * env(n, 0.001, curve=9)
save("deflect", reverb(df, 0.25, 0.3))

# ---------------------------------------------------------------- Сила
d = 1.0
n = int(SR * d)
w = lp_fast(noise(d), 500) * np.hanning(n) * 4 + 0.5 * np.sin(2 * np.pi * np.cumsum(np.linspace(60, 140, n)) / SR) * np.hanning(n)
save("force_push", reverb(w, 0.3, 0.6))
d = 1.6
n = int(SR * d)
cr = hp_fast(noise(d), 2500) * (rng.random(n) > 0.9) * 2
cr += hp_fast(noise(d), 600) * 0.4
cr += 0.5 * np.sign(np.sin(2 * np.pi * 120 * t(d))) * (rng.random(n) > 0.5)
cr *= 0.6 + 0.4 * np.sin(2 * np.pi * 13 * t(d)) ** 2
save("lightning", cr * np.concatenate([np.linspace(0, 1, 800), np.ones(n - 1600), np.linspace(1, 0, 800)]), 0.7)

# ---------------------------------------------------------------- техника
d = 2.0
tt = t(d)
eng = np.sin(2 * np.pi * 70 * tt) + 0.6 * np.sin(2 * np.pi * 140 * tt + 0.4) + 0.4 * np.sign(np.sin(2 * np.pi * 35 * tt)) * 0.5
eng += 0.35 * lp_fast(noise(d), 900)
eng *= 0.8 + 0.2 * np.sin(2 * np.pi * 7 * tt)
save("speeder", eng, 0.6)
d = 0.7
n = int(SR * d)
st = lp_fast(noise(d), 140) * 8 * env(n, 0.003, curve=7) + 0.6 * np.sin(2 * np.pi * np.cumsum(np.linspace(90, 40, n)) / SR) * env(n, 0.003, curve=6)
st += 0.35 * np.sin(2 * np.pi * 820 * tt[:n]) * env(n, 0.001, curve=20) + 0.25 * np.sin(2 * np.pi * 1310 * tt[:n]) * env(n, 0.001, curve=25)
st += 0.2 * np.sin(2 * np.pi * np.cumsum(np.linspace(300, 500, n)) / SR) * np.hanning(n)       # сервопривод
save("walker_step", reverb(st, 0.2, 0.4))

# ---------------------------------------------------------------- солдаты
d = 0.55
n = int(SR * d)
cl = np.zeros(n)
for at, f, g in ((0.02, 2600, 1.0), (0.05, 1800, 0.6), (0.33, 3100, 1.0), (0.36, 2200, 0.7)):
    k = int(at * SR)
    m = int(0.04 * SR)
    cl[k:k + m] += g * np.sin(2 * np.pi * f * t(0.04)) * env(m, 0.0005, curve=30) + g * 0.6 * hp_fast(noise(0.04), 3000) * env(m, 0.0005, curve=40)
save("reload", cl, 0.6)
d = 0.45
n = int(SR * d)
fall = lp_fast(noise(d), 250) * 5 * env(n, 0.003, curve=9) + 0.4 * hp_fast(noise(d), 2000) * env(n, 0.002, curve=25)
save("body_fall", fall, 0.7)
