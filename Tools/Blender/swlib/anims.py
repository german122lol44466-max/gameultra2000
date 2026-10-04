"""Клипы анимаций. Каждый клип — список поз по кадрам (30 к/с), строится из ключевых поз
(словари параметров) с плавной интерполяцией Catmull-Rom, а циклы ходьбы/бега — процедурно.

Параметры позы (всё в пространстве персонажа, градусы/метры базового роста 1.83):
    hips (x,y,z)      — смещение таза          hipr (наклон, вбок, поворот)
    spine/chest/neck/head (сгиб вперёд, скручивание, наклон вбок)
    w (x,y,z, рыскание, тангаж, крен)          — положение и направление оружия
    wup (x,y,z)       — «верх» оружия (для меча — куда смотрят костяшки)
    lgrip             — 1: левая рука на оружии, 0: свободна
    lh (x,y,z)  lhf (пальцы)  lht (большой палец) — свободная левая кисть
    fl / fr (x,y,z, рыскание, наклон стопы)    — стопы
    cape (3 угла)     — плащ;  blade — длина клинка 0..1
"""

import math

from mathutils import Vector

from .rig import Pose, aim_frame, hand

FPS = 30
ANKLE = 0.09
FOOT_X = 0.108


def neutral():
    return {
        "hips": (0.0, 0.0, 0.0), "hipr": (0.0, 0.0, 0.0),
        "spine": (0.0, 0.0, 0.0), "chest": (0.0, 0.0, 0.0), "neck": (0.0, 0.0, 0.0), "head": (0.0, 0.0, 0.0),
        "w": (-0.12, -0.25, 1.0, 0.0, 0.0, 0.0), "wup": (0.0, 0.0, 1.0),
        "lgrip": 1.0, "lh": (0.2, -0.02, 0.88), "lhf": (0.0, 0.0, -1.0), "lht": (0.0, -1.0, 0.0),
        "rh": None,
        "fl": (FOOT_X, 0.012, ANKLE, 0.0, 0.0), "fr": (-FOOT_X, 0.012, ANKLE, 0.0, 0.0),
        "cape": (0.0, 0.0, 0.0), "blade": 1.0, "blade2": 1.0,
        "shl": (0.0, 0.0, 0.0), "shr": (0.0, 0.0, 0.0),
        "elbl": (0.45, 0.45, 1.0), "elbr": (-0.45, 0.45, 1.0),
    }


def to_pose(p):
    P = Pose()
    P.hips = Vector(p["hips"])
    P.hips_rot = p["hipr"]
    P.fk = {"Spine": p["spine"], "Chest": p["chest"], "Neck": p["neck"], "Head": p["head"],
            "Shoulder.L": p["shl"], "Shoulder.R": p["shr"],
            "Cape.1": (p["cape"][0], 0.0, 0.0), "Cape.2": (p["cape"][1], 0.0, 0.0), "Cape.3": (p["cape"][2], 0.0, 0.0)}
    P.scale = {"Blade.R": max(1e-3, p["blade"]), "Blade.R2": max(1e-3, p["blade2"])}
    x, y, z, yaw, pitch, roll = p["w"]
    P.weapon = aim_frame((x, y, z), yaw, pitch, roll, up=Vector(p["wup"]))
    P.lgrip = max(0.0, min(1.0, p["lgrip"]))
    P.lhand = hand(Vector(p["lhf"]), Vector(p["lht"]), Vector(p["lh"]))
    P.feet = {"L": p["fl"], "R": p["fr"]}
    P.elbow_pole = {"L": Vector(p["elbl"]), "R": Vector(p["elbr"])}
    return P


# ----------------------------------------------------------------------------- интерполяция

def _num(v):
    return isinstance(v, (int, float))


def _cr(p0, p1, p2, p3, t):
    t2, t3 = t * t, t * t * t
    return 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3)


def keyed(keys, frames, base=None, loop=False):
    """keys: [(кадр, {параметры})]. Параметры, не указанные в ключе, берутся из предыдущего ключа."""
    base = base or neutral()
    full = []
    cur = dict(base)
    for f, d in keys:
        cur = dict(cur)
        cur.update(d)
        full.append((f, cur))
    out = []
    n = len(full)
    for fr in range(frames + 1):
        i = 0
        while i < n - 1 and full[i + 1][0] <= fr:
            i += 1
        if i >= n - 1:
            out.append(dict(full[-1][1]))
            continue
        f1, a = full[i]
        f2, b = full[i + 1]
        t = (fr - f1) / max(1e-6, (f2 - f1))
        if loop:
            a0 = full[i - 1][1] if i > 0 else full[-2][1]
            b3 = full[i + 2][1] if i + 2 < n else full[1][1]
        else:
            a0 = full[i - 1][1] if i > 0 else a
            b3 = full[i + 2][1] if i + 2 < n else b
        res = {}
        for k in a:
            va, vb, v0, v3 = a[k], b[k], a0[k], b3[k]
            if va is None or vb is None:
                res[k] = vb if t > 0.5 else va
            elif _num(va):
                res[k] = _cr(v0, va, vb, v3, t)
            else:
                res[k] = tuple(_cr(q0, qa, qb, q3, t) for q0, qa, qb, q3 in zip(v0, va, vb, v3))
        out.append(res)
    return out


def over(p, **kw):
    q = dict(p)
    q.update(kw)
    return q


# ----------------------------------------------------------------------------- шаг

def gait(phase, stride, lift, duty=0.6, width=FOOT_X, bob=0.02, sway=3.0, crouch=0.0):
    """Ноги и таз для цикла шага. phase 0..1. Возвращает словарь параметров."""
    res = {}
    for key, off, sg in (("fl", 0.0, 1), ("fr", 0.5, -1)):
        f = (phase + off) % 1.0
        if f < duty:
            s = f / duty
            y = -stride / 2 + stride * s
            z = ANKLE
            pitch = 0.0
            if s > 0.75:                             # перекат на носок
                k = (s - 0.75) / 0.25
                z += 0.05 * k * k
                pitch = -25 * k
        else:
            s = (f - duty) / (1 - duty)
            e = 0.5 - 0.5 * math.cos(math.pi * s)
            y = stride / 2 - stride * e
            z = ANKLE + 0.05 * (1 - s) ** 2 + lift * math.sin(math.pi * s)
            pitch = -25 * (1 - s) ** 2 + 15 * math.sin(math.pi * s) * (1 if s > 0.5 else 0)
        res[key] = (width * sg, 0.012 + y, z, 0.0, pitch)
    c = math.cos(4 * math.pi * phase)
    res["hips"] = (math.sin(2 * math.pi * phase) * 0.012, 0.0, -0.025 - crouch - bob * 0.5 * (1 + c))
    res["hipr"] = (0.0, 0.0, -sway * math.sin(2 * math.pi * phase))
    return res


def cycle(frames, fn):
    return [fn(i / frames) for i in range(frames + 1)]


# ============================================================================= ВИНТОВКА

def rifle_poses():
    b = neutral()
    low = over(b, w=(-0.11, -0.24, 1.02, 28.0, -28.0, -12.0), lgrip=1.0,
               spine=(4.0, 0.0, 0.0), chest=(2.0, 0.0, 0.0), neck=(0.0, 0.0, 0.0), head=(-4.0, 0.0, 0.0))
    aim = over(b, w=(-0.09, -0.24, 1.32, 3.0, 1.0, 0.0), lgrip=1.0,
               hips=(0.0, 0.0, -0.03), hipr=(0.0, 0.0, -18.0), spine=(5.0, 6.0, 0.0), chest=(3.0, 8.0, 0.0),
               neck=(8.0, 2.0, -6.0), head=(6.0, 0.0, -6.0),
               fl=(0.11, -0.16, ANKLE, 18.0, 0.0), fr=(-0.13, 0.17, ANKLE, 30.0, 0.0),
               elbl=(0.35, 0.2, 0.7), elbr=(-0.55, 0.3, 1.0))
    return b, low, aim


def rifle_clips():
    b, low, aim = rifle_poses()
    C = {}
    # --- Idle: опущенная винтовка, дыхание, осматривается
    C["Idle"] = keyed([(0, low), (30, over(low, hips=(0, 0, -0.008), chest=(3.0, 0, 0), head=(-4.0, 12.0, 0))),
                       (60, over(low, head=(-2.0, -14.0, 0), chest=(2.5, 0, 0))), (90, low)], 90, loop=True)
    # --- Walk 1.6 м/с: 32 кадра, шаг 0.85
    def walk(t, fast=False):
        g = gait(t, 0.85, 0.06)
        p = over(low, **g)
        s = math.sin(2 * math.pi * t)
        x, y, z, yw, pt, rl = low["w"]
        p["w"] = (x, y, z + 0.01 * math.cos(4 * math.pi * t), yw + 2 * s, pt, rl)
        p["chest"] = (2.0, 3 * s, 0.0)
        p["cape"] = (-4.0, -2.0, -2.0)
        return p
    C["Walk"] = cycle(32, walk)
    # --- Run 4.5 м/с: 20 кадров, шаг 1.5 — винтовка поперёк груди
    def run(t):
        g = gait(t, 1.5, 0.16, duty=0.38, bob=0.04, sway=6.0)
        s = math.sin(2 * math.pi * t)
        p = over(b, **g, spine=(14.0, -6 * s, 0.0), chest=(4.0, -6 * s, 0.0), neck=(-8.0, 0, 0), head=(-6.0, 4 * s, 0),
                 w=(-0.08, -0.25, 1.17 + 0.015 * math.cos(4 * math.pi * t), 38.0 + 4 * s, 28.0, -20.0), lgrip=1.0,
                 cape=(-25.0, -12.0, -10.0))
        return p
    C["Run"] = cycle(20, run)
    # --- Aim: прицеливание (цикл с лёгким покачиванием)
    C["Aim"] = keyed([(0, aim), (30, over(aim, hips=(0, 0, -0.035), w=(-0.09, -0.24, 1.318, 3.5, 1.4, 0.0))), (60, aim)], 60, loop=True)
    C["AimIn"] = keyed([(0, low), (8, aim)], 8)
    # --- Fire: выстрел — отдача назад-вверх
    kick = over(aim, w=(-0.09, -0.205, 1.345, 3.0, 9.0, 2.0), chest=(0.0, 9.0, 0.0), head=(3.0, 0.0, -6.0))
    C["Fire"] = keyed([(0, aim), (2, kick), (10, aim)], 10)
    # --- Reload: левая рука вынимает энергоячейку слева и вставляет новую
    ready = over(aim, w=(-0.07, -0.26, 1.2, 25.0, 12.0, -55.0), head=(25.0, 10.0, 0), neck=(10.0, 10.0, 0))
    cell = (0.02, -0.2, 1.2)
    lhand_cell = dict(lh=cell, lhf=(-0.3, -0.6, -0.7), lht=(0.0, -1.0, 0.3))
    C["Reload"] = keyed([(0, aim), (8, ready), (14, over(ready, lgrip=0.0, **lhand_cell)),
                         (22, over(ready, lgrip=0.0, lh=(0.16, -0.05, 1.0), lhf=(0, 0, -1), lht=(0, -1, 0))),
                         (32, over(ready, lgrip=0.0, lh=(0.14, 0.02, 1.02), lhf=(0, 0, -1), lht=(0, -1, 0))),
                         (42, over(ready, lgrip=0.0, **lhand_cell)), (48, over(ready, lgrip=0.0, **lhand_cell)),
                         (54, ready), (62, aim)], 62)
    # --- Hit: попадание — отшатнуться
    hit = over(low, hips=(0.0, 0.05, -0.03), hipr=(-6.0, 0.0, 8.0), spine=(-14.0, 10.0, 4.0), chest=(-8.0, 6.0, 0.0),
               head=(-15.0, -10.0, 0.0), w=(-0.13, -0.18, 0.98, 30.0, -40.0, -12.0), fr=(-0.12, 0.15, ANKLE, 0.0, 0.0))
    C["Hit"] = keyed([(0, low), (4, hit), (20, low)], 20)
    # --- Death: падение назад
    d1 = over(hit, hips=(0.0, 0.1, -0.12), hipr=(-20.0, 0.0, 10.0), spine=(-20.0, 10.0, 0.0), chest=(-10.0, 0, 0),
              head=(-25.0, -10.0, 0.0), lgrip=0.0, lh=(0.32, 0.0, 0.95), lhf=(0.4, 0, -1), lht=(0, -1, 0),
              fl=(0.12, -0.05, ANKLE, 0, 0), fr=(-0.15, 0.25, ANKLE, 0, 0))
    d2 = over(d1, hips=(0.0, 0.42, -0.62), hipr=(-55.0, 0.0, 14.0), spine=(-10.0, 0, 0), head=(-10.0, -20.0, 0),
             w=(-0.3, 0.2, 0.55, 50.0, -10.0, -60.0), lh=(0.42, 0.25, 0.5), fl=(0.16, -0.2, ANKLE, 10, 0), fr=(-0.14, 0.0, ANKLE + 0.02, -10, 0))
    d3 = over(d2, hips=(0.0, 0.75, -0.84), hipr=(-88.0, 0.0, 10.0), spine=(-4.0, 0, 0), chest=(-4.0, 0, 0), head=(4.0, -30.0, 0),
              w=(-0.42, 0.75, 0.12, 70.0, -5.0, -85.0), lh=(0.45, 0.8, 0.1), lhf=(0.6, 0.6, 0), lht=(0, 0, 1),
              fl=(0.17, -0.25, ANKLE, 10, -40), fr=(-0.16, -0.1, ANKLE + 0.08, -15, -40))
    C["Death"] = keyed([(0, low), (6, d1), (20, d2), (32, d3), (40, over(d3, hips=(0.0, 0.76, -0.86))), (60, over(d3, hips=(0.0, 0.76, -0.86)))], 60)
    return C


# ============================================================================= СВЕТОВОЙ МЕЧ

def saber_poses(style):
    b = neutral()
    two = style in ("vader", "maul")
    W = lambda x, y, z, yaw, pitch, roll=0.0: (x, y, z, yaw, pitch, roll)
    guard = over(b, w=W(-0.06, -0.3, 1.04, 10.0, 62.0), wup=(0.0, -0.3, 1.0) if False else (0.0, -1.0, 0.2),
                 lgrip=1.0 if two else 0.0,
                 hips=(0.0, 0.0, -0.05), hipr=(0.0, 0.0, -15.0), spine=(4.0, 6.0, 0.0), chest=(2.0, 6.0, 0.0), head=(-2.0, 4.0, 0.0),
                 fl=(0.12, -0.2, ANKLE, 12.0, 0.0), fr=(-0.14, 0.2, ANKLE, 28.0, 0.0),
                 lh=(0.28, -0.05, 1.02), lhf=(0.1, -0.5, -0.8), lht=(0.0, -1.0, 0.3))
    if style == "maul":
        guard = over(guard, w=W(-0.02, -0.3, 1.1, 75.0, 25.0), wup=(0.0, -1.0, 0.3), lgrip=1.0)
    if style == "dooku":
        # фехтовальная стойка: клинок вперёд, левая рука отведена назад-вверх
        guard = over(guard, w=W(-0.14, -0.36, 1.12, 8.0, 20.0, 10.0), wup=(0.0, 0.0, 1.0), lgrip=0.0,
                     hipr=(0.0, 0.0, -35.0), spine=(2.0, 10.0, 0), chest=(0.0, 15.0, 0), head=(0, -15.0, 0),
                     fl=(0.06, 0.18, ANKLE, 60.0, 0.0), fr=(-0.12, -0.22, ANKLE, 10.0, 0.0),
                     lh=(0.3, 0.15, 1.45), lhf=(0.3, 0.3, 1.0), lht=(0.0, -1.0, 0.0), elbl=(0.6, 0.3, 1.5))
    if style == "sidious":
        guard = over(guard, w=W(-0.12, -0.32, 1.02, 15.0, 35.0, 0.0), wup=(0.0, -1.0, 0.4), lgrip=0.0,
                     spine=(12.0, 6.0, 0.0), chest=(6.0, 6.0, 0.0), neck=(-6.0, 0, 0), head=(-6.0, 4.0, 0.0),
                     lh=(0.26, -0.18, 1.08), lhf=(0.2, -0.8, 0.2), lht=(0.0, 0.0, 1.0))
    return b, guard


def saber_clips(style):
    b, guard = saber_poses(style)
    two = style in ("vader", "maul")
    C = {}
    hunch = 10.0 if style == "sidious" else 0.0
    if hunch:
        b = over(b, spine=(hunch, 0, 0), neck=(-6.0, 0, 0))
    # меч опущен (не зажжён) — исходное положение для зажигания
    low = over(b, w=(-0.2, -0.06, 0.95, 0.0, -80.0, 0.0), wup=(0.0, -1.0, 0.0), lgrip=0.0, blade=0.0, blade2=0.0,
               lh=(0.21, -0.01, 0.88), lhf=(0, 0, -1), lht=(0, -1, 0))
    # --- Idle (в стойке, клинок зажжён)
    breathe = over(guard, hips=(guard["hips"][0], 0, guard["hips"][2] - 0.008), chest=(guard["chest"][0] + 1.5,) + guard["chest"][1:])
    C["Idle"] = keyed([(0, guard), (45, breathe), (90, guard)], 90, loop=True)
    # --- Ignite: поднять рукоять и зажечь клинок
    lift = over(low, w=(-0.12, -0.22, 1.1, 0.0, 40.0, 0.0), wup=(0.0, -1.0, 0.3), blade=0.0, blade2=0.0)
    lit = over(lift, blade=1.0, blade2=1.0)
    C["Ignite"] = keyed([(0, low), (12, lift), (16, lit), (30, over(guard, blade=1.0)), (40, guard)], 40)
    # --- Walk / Run
    def walk(t):
        g = gait(t, 0.8, 0.06)
        s = math.sin(2 * math.pi * t)
        p = over(guard, **g)
        x, y, z, yw, pt, rl = guard["w"]
        p["w"] = (x, y, z + 0.01 * math.cos(4 * math.pi * t), yw + 2 * s, pt, rl)
        p["hipr"] = (0.0, 0.0, g["hipr"][2])
        p["fl"], p["fr"] = g["fl"], g["fr"]
        p["spine"] = (guard["spine"][0], 3 * s, 0.0)
        p["cape"] = (-5.0 - 3 * abs(s), -3.0, -3.0 + 3 * s)
        if not two and style != "dooku":
            p["lh"] = (0.24, -0.02 - 0.08 * s, 0.9)
        if style == "dooku":
            p["lh"] = (0.22, 0.02 + 0.06 * s, 0.9)
            p["lhf"], p["lht"] = (0, 0, -1), (0, -1, 0)
        return p
    C["Walk"] = cycle(32, walk)

    def run(t):
        g = gait(t, 1.5, 0.16, duty=0.38, bob=0.04, sway=6.0)
        s = math.sin(2 * math.pi * t)
        p = over(b, **g, spine=(16.0, -6 * s, 0.0), chest=(4.0, -6 * s, 0.0), neck=(-10.0, 0, 0), head=(-6.0, 4 * s, 0),
                 w=(-0.24 + 0.05 * s, 0.05 - 0.12 * s, 1.0, 0.0, -45.0 + 12 * s, 0.0), wup=(0.0, -1.0, 0.0), lgrip=0.0,
                 lh=(0.24, -0.02 + 0.2 * s, 1.0 + 0.06 * abs(s)), lhf=(0.0, -0.5 * s, -0.8), lht=(0, -1, 0),
                 cape=(-35.0 + 5 * s, -15.0, -12.0 + 8 * s))
        return p
    C["Run"] = cycle(20, run)

    # --- Атаки: комбо из трёх ударов
    W = guard["w"]
    lg = 1.0 if two else 0.0
    if style == "dooku":
        lg = 0.0
    def sw(x, y, z, yaw, pitch, roll, up, **kw):
        kw.setdefault("lgrip", lg)
        return over(guard, w=(x, y, z, yaw, pitch, roll), wup=up, **kw)
    # 1: диагональный удар сверху-справа вниз-влево
    a1_up = sw(-0.2, -0.05, 1.45, -40.0, 110.0, 0.0, (0.5, 0.0, -1.0), hipr=(0, 0, -30.0), chest=(-4.0, -15.0, 0), spine=(-2.0, -10.0, 0))
    a1_hit = sw(0.14, -0.42, 0.98, 50.0, -30.0, 0.0, (-0.5, -0.3, 0.8), hipr=(0, 0, 20.0), chest=(10.0, 20.0, 0), spine=(12.0, 10.0, 0),
                hips=(0, -0.08, -0.1), fl=(0.13, -0.35, ANKLE, 10.0, 0.0), fr=(-0.15, 0.25, ANKLE, 30.0, -10.0))
    C["Attack1"] = keyed([(0, guard), (8, a1_up), (14, a1_hit), (22, over(a1_hit, chest=(6.0, 15.0, 0))), (32, guard)], 32)
    # 2: горизонтальный удар слева направо
    a2_wind = sw(0.22, -0.12, 1.2, 100.0, 10.0, 90.0, (0.0, 0.0, 1.0), hipr=(0, 0, 25.0), chest=(0.0, 25.0, 0), spine=(0, 10.0, 0))
    a2_hit = sw(-0.25, -0.35, 1.16, -70.0, 5.0, -90.0, (0.0, 0.0, 1.0), hipr=(0, 0, -35.0), chest=(4.0, -25.0, 0), spine=(4.0, -12.0, 0),
                hips=(0, -0.05, -0.08), fl=(0.14, -0.3, ANKLE, 10.0, 0.0))
    C["Attack2"] = keyed([(0, guard), (9, a2_wind), (15, a2_hit), (22, over(a2_hit, chest=(4.0, -30.0, 0))), (34, guard)], 34)
    # 3: мощный удар сверху вниз с выпадом (у Мола — вращение посоха)
    a3_up = sw(-0.05, 0.02, 1.55, 0.0, 120.0, 0.0, (0.0, 1.0, 0.0), spine=(-12.0, 0, 0), chest=(-8.0, 0, 0), head=(5.0, 0, 0),
               hips=(0, 0.04, -0.02))
    a3_hit = sw(-0.04, -0.5, 0.85, 0.0, -35.0, 0.0, (0.0, -1.0, 0.0), spine=(25.0, 0, 0), chest=(12.0, 0, 0), head=(-20.0, 0, 0),
                hips=(0, -0.15, -0.22), hipr=(8.0, 0, -10.0), fl=(0.13, -0.5, ANKLE, 5.0, 0.0), fr=(-0.15, 0.3, ANKLE, 20.0, -30.0))
    if style == "maul":
        spin = [sw(-0.0, -0.32, 1.15, 90.0 + a, 10.0, 0.0, (0.0, 0.0, 1.0), hipr=(0, 0, -10.0 + a * 0.1)) for a in (0, 120, 240, 360, 480)]
        C["Attack3"] = keyed([(0, guard)] + [(4 + i * 4, s) for i, s in enumerate(spin)] + [(26, a3_hit), (36, a3_hit), (48, guard)], 48)
    else:
        C["Attack3"] = keyed([(0, guard), (12, a3_up), (18, a3_hit), (28, a3_hit), (42, guard)], 42)
    # --- Block: блок удара сверху
    blk = sw(-0.05, -0.32, 1.42, 80.0, 18.0, 0.0, (0.0, -1.0, 1.0), spine=(-4.0, 0, 0), chest=(-4.0, 0, 0), head=(-10.0, 0, 0),
             lgrip=1.0 if two else 0.0, lh=(0.24, -0.3, 1.38), lhf=(0, -0.3, 1), lht=(-1, 0, 0))
    C["Block"] = keyed([(0, guard), (6, blk), (14, over(blk, hips=(0, 0.04, -0.08))), (24, blk), (34, guard)], 34)

    # --- Сила: толчок (все), удушение (Вейдер), молнии (Сидиус)
    push_w = over(guard, lgrip=0.0)
    if two:
        push_w = over(guard, lgrip=0.0, w=(-0.2, -0.12, 1.02, 0.0, 70.0, 0.0), wup=(0.0, -1.0, 0.2))
    p_wind = over(push_w, lh=(0.18, 0.05, 1.3), lhf=(0.0, 0.0, 1.0), lht=(-1.0, 0, 0), chest=(-4.0, 20.0, 0), hipr=(0, 0, 10.0))
    p_out = over(push_w, lh=(0.12, -0.55, 1.38), lhf=(0.0, 0.0, 1.0), lht=(-1.0, 0, 0), chest=(8.0, -15.0, 0), spine=(6.0, -8.0, 0),
                 hips=(0, -0.1, -0.08), fl=(0.13, -0.38, ANKLE, 0.0, 0.0), elbl=(0.6, 0.2, 1.1))
    C["ForcePush"] = keyed([(0, guard), (12, p_wind), (17, p_out), (30, p_out), (44, guard)], 44)
    if style == "vader":
        choke = over(push_w, lh=(0.16, -0.5, 1.5), lhf=(0.1, -0.6, 0.8), lht=(-1.0, 0, 0), head=(4.0, -8.0, 0), chest=(0.0, -10.0, 0),
                     hipr=(0, 0, 0.0), elbl=(0.6, 0.3, 1.0))
        squeeze = over(choke, lh=(0.16, -0.5, 1.56), lhf=(0.3, -0.4, 0.8))
        C["ForceChoke"] = keyed([(0, guard), (14, choke), (30, squeeze), (60, squeeze), (66, over(squeeze, lh=(0.16, -0.48, 1.62))),
                                 (90, guard)], 90)
    if style == "sidious":
        lt = over(b, w=(-0.24, -0.04, 0.98, 0.0, -75.0, 0.0), wup=(0.0, -1.0, 0.0), lgrip=0.0, blade=0.0,
                  lh=(0.16, -0.5, 1.3), lhf=(0.0, -1.0, 0.1), lht=(0.0, 0.0, 1.0),
                  spine=(18.0, 0, 0), chest=(8.0, 0, 0), neck=(-12.0, 0, 0), head=(-10.0, 0, 0), hips=(0, -0.04, -0.06),
                  fl=(0.13, -0.25, ANKLE, 10.0, 0.0), fr=(-0.13, 0.15, ANKLE, 20.0, 0.0), elbl=(0.6, 0.0, 1.2))
        C["ForceLightning"] = keyed([(0, guard), (12, lt), (20, over(lt, lh=(0.17, -0.52, 1.32))), (28, lt), (36, over(lt, lh=(0.15, -0.52, 1.28))),
                                     (44, lt), (60, lt), (75, guard)], 75)
    # --- Hit / Death
    hit = over(guard, hips=(0.0, 0.06, -0.04), hipr=(-6.0, 0.0, 10.0), spine=(-12.0, 10.0, 4.0), chest=(-8.0, 8.0, 0.0),
               head=(-14.0, -10.0, 0.0), fr=(-0.13, 0.3, ANKLE, 20.0, 0.0))
    C["Hit"] = keyed([(0, guard), (4, hit), (22, guard)], 22)
    kneel = over(guard, hips=(0.0, 0.0, -0.45), hipr=(10.0, 0, 0), spine=(20.0, 0, 0), chest=(10.0, 0, 0), head=(-10.0, 0, 0),
                 w=(-0.3, -0.15, 0.6, 0.0, -60.0, 0.0), wup=(0.0, -1.0, 0.0), lgrip=0.0, lh=(0.22, -0.2, 0.75), lhf=(0, -0.5, -1), lht=(0, -1, 0),
                 fl=(0.12, -0.3, ANKLE, 0.0, 0.0), fr=(-0.12, 0.25, ANKLE + 0.06, 0.0, -60.0), blade=1.0)
    down = over(kneel, hips=(0.0, -0.45, -0.82), hipr=(80.0, 0.0, -10.0), spine=(5.0, 0, 0), chest=(0.0, 0, 0), head=(-30.0, 20.0, 0),
                w=(-0.35, -0.75, 0.08, -60.0, -5.0, 80.0), lh=(0.35, -0.9, 0.08), lhf=(0.3, -0.5, 0), lht=(0, 0, 1),
                fl=(0.14, 0.2, ANKLE, 0.0, 0.0), fr=(-0.12, 0.3, ANKLE + 0.02, 0.0, -70.0), blade=0.0, blade2=0.0,
                cape=(-60.0, 15.0, 10.0))
    C["Death"] = keyed([(0, guard), (8, hit), (22, kneel), (30, kneel), (42, down), (60, down)], 60)
    return C


def base_style(name):
    return {"Vader": "vader", "Maul": "maul", "Sidious": "sidious", "Dooku": "dooku"}.get(name)
