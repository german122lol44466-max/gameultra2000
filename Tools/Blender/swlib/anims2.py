"""
Анимации v2 для скелета MakeHuman (рост 1.83 в пространстве рига, плечи ~1.47, глаза ~1.72).

Наборы:
  rifle  — Idle, Walk, WalkAim, WalkBack, StrafeL, StrafeR, Run, Crouch, CrouchAim, Aim, AimIn, Fire, FireAuto, Reload,
           Hit, HitBack, Death_Back, Death_Forward, Death_Knees, Death_Spin, Death_Headshot, Death_Blast
  saber  — Idle, Ignite, Walk, Run, StrafeL, StrafeR, WalkBack, Attack1..4, Block, Deflect1, Deflect2, ForcePush,
           (ForceChoke / ForceLightning), Hit, HitBack, Death_* (те же 6)
В конце каждого клипа смерти тело лежит неподвижно — дальше в игре включается рэгдолл.
"""

import math

from mathutils import Vector

from .anims import _cr, cycle, keyed  # noqa: F401
from .rig import aim_frame, hand
from .rig2 import Pose

ANKLE = 0.078
FX = 0.115           # стопы по ширине
GRIP = (1.0, 0.35, 0.0, 0.7)       # правая на рукояти: пальцы сжаты, указательный на спуске
SUPPORT = (0.85, None, 0.0, 0.6)   # левая на цевье
FIST = (1.0, None, 0.0, 1.0)
OPEN = (0.12, None, 0.6, 0.1)
CLAW = (0.55, None, 0.5, 0.4)


def neutral():
    return {
        "hips": (0.0, 0.0, 0.0), "hipr": (0.0, 0.0, 0.0),
        "spine": (0.0, 0.0, 0.0), "chest": (0.0, 0.0, 0.0), "neck": (0.0, 0.0, 0.0), "head": (0.0, 0.0, 0.0),
        "w": (-0.15, -0.25, 1.05, 0.0, 0.0, 0.0), "wup": (0.0, 0.0, 1.0),
        "lgrip": 1.0, "lh": (0.24, -0.02, 0.92), "lhf": (0.0, 0.0, -1.0), "lht": (0.0, -1.0, 0.0),
        "fl": (FX, 0.0, ANKLE, 0.0, 0.0), "fr": (-FX, 0.0, ANKLE, 0.0, 0.0),
        "blade": 1.0, "blade2": 1.0,
        "shl": (0.0, 0.0, 0.0), "shr": (0.0, 0.0, 0.0),
        "elbl": (0.5, 0.5, 1.0), "elbr": (-0.5, 0.5, 1.0),
        "kl": (0.15, -0.8, 0.55), "kr": (-0.15, -0.8, 0.55),
        "rf": GRIP, "lf": SUPPORT, "jaw": 0.0,
    }


def over(p, **kw):
    q = dict(p)
    q.update(kw)
    return q


def _fing(v):
    return None if v is None else v


def to_pose(p):
    P = Pose()
    P.hips = Vector(p["hips"])
    P.hips_rot = p["hipr"]
    P.fk = {"Spine": p["spine"], "Chest": p["chest"], "Neck": p["neck"], "Head": p["head"],
            "Shoulder.L": p["shl"], "Shoulder.R": p["shr"], "Jaw": (p["jaw"], 0.0, 0.0)}
    P.scale = {"Blade.R": max(1e-3, p["blade"]), "Blade.R2": max(1e-3, p["blade2"])}
    x, y, z, yaw, pitch, roll = p["w"]
    P.weapon = aim_frame((x, y, z), yaw, pitch, roll, up=Vector(p["wup"]))
    P.lgrip = max(0.0, min(1.0, p["lgrip"]))
    P.lhand = hand(Vector(p["lhf"]), Vector(p["lht"]), Vector(p["lh"]))
    P.feet = {"L": p["fl"], "R": p["fr"]}
    P.elbow_pole = {"L": Vector(p["elbl"]), "R": Vector(p["elbr"])}
    P.knee_pole = {"L": Vector(p["kl"]), "R": Vector(p["kr"])}
    rf = tuple(p["rf"])
    lf = tuple(p["lf"])
    P.fingers = {"R": (rf[0], rf[1], rf[2], rf[3]), "L": (lf[0], lf[1], lf[2], lf[3])}
    return P


def _num_keys(p):
    """keyed() интерполирует числа и кортежи; кортежи пальцев с None — заменяем на числа."""
    q = dict(p)
    for k in ("rf", "lf"):
        a = list(q[k])
        if a[1] is None:
            a[1] = -1.0
        q[k] = tuple(a)
    return q


def _fix(p):
    for k in ("rf", "lf"):
        a = list(p[k])
        if a[1] < -0.5:
            a[1] = None
        p[k] = tuple(a)
    return p


def K(keys, frames, loop=False, base=None):
    base = _num_keys(base or neutral())
    out = keyed([(f, _num_keys(d)) for f, d in keys], frames, base=base, loop=loop)
    return [_fix(p) for p in out]


def gait(phase, stride, lift, duty=0.6, width=FX, bob=0.02, sway=3.0, crouch=0.0, side=0.0, back=False):
    """Ноги и таз. side — шаг вбок (м, +влево), back — шаг назад."""
    res = {}
    for key, off, sg in (("fl", 0.0, 1), ("fr", 0.5, -1)):
        f = (phase + off) % 1.0
        if f < duty:
            s = f / duty
            prog = -0.5 + s
            z = ANKLE
            pitch = 0.0
            if s > 0.72 and not back:
                k = (s - 0.72) / 0.28
                z += 0.045 * k * k
                pitch = -28 * k
        else:
            s = (f - duty) / (1 - duty)
            e = 0.5 - 0.5 * math.cos(math.pi * s)
            prog = 0.5 - e
            z = ANKLE + 0.045 * (1 - s) ** 2 * (0 if back else 1) + lift * math.sin(math.pi * s)
            pitch = (-28 * (1 - s) ** 2 + 18 * math.sin(math.pi * s) * (1 if s > 0.5 else 0)) * (0 if back else 1)
        dy = stride * prog * (-1 if back else 1)
        dx = -side * prog
        res[key] = (width * sg + dx, dy, z, 0.0, pitch)
    c = math.cos(4 * math.pi * phase)
    res["hips"] = (math.sin(2 * math.pi * phase) * 0.014, 0.0, -0.022 - crouch - bob * 0.5 * (1 + c))
    res["hipr"] = (0.0, 0.0, -sway * math.sin(2 * math.pi * phase))
    return res


# ============================================================================ ВИНТОВКА

def rifle_poses():
    b = neutral()
    low = over(b, w=(-0.12, -0.27, 1.12, 30.0, -32.0, -10.0), spine=(4.0, 0.0, 0.0), chest=(3.0, 0.0, 0.0), head=(-4.0, 0.0, 0.0),
               fl=(FX, -0.03, ANKLE, 6.0, 0.0), fr=(-FX, 0.04, ANKLE, 14.0, 0.0))
    aim = over(b, w=(-0.075, -0.31, 1.49, 2.0, 0.0, 0.0), hips=(0.0, 0.0, -0.035), hipr=(0.0, 0.0, -22.0),
               spine=(6.0, 8.0, 0.0), chest=(4.0, 12.0, 0.0), neck=(8.0, 0.0, -6.0), head=(8.0, 2.0, -10.0),
               fl=(0.13, -0.16, ANKLE, 22.0, 0.0), fr=(-0.12, 0.17, ANKLE, 38.0, 0.0),
               elbl=(0.3, 0.1, 0.8), elbr=(-0.6, 0.35, 1.1), rf=(1.0, 0.3, 0.0, 0.7))
    crouch = over(aim, hips=(0.0, 0.05, -0.43), hipr=(10.0, 0.0, -25.0), spine=(14.0, 8.0, 0.0),
                  fl=(0.14, -0.3, ANKLE, 20.0, 0.0), fr=(-0.12, 0.25, ANKLE + 0.02, 40.0, -55.0),
                  kl=(0.3, -1.0, 0.5), kr=(-0.2, -1.0, 0.2), w=(-0.075, -0.33, 1.08, 2.0, 0.0, 0.0))
    crouch_low = over(crouch, w=(-0.12, -0.27, 0.72, 30.0, -32.0, -10.0), head=(-2.0, 0, 0), neck=(0, 0, 0), chest=(4.0, 0, 0))
    return b, low, aim, crouch, crouch_low


def deaths(stand, weapon_drop=True, lying_hand=(0.42, 0.25, 0.1)):
    """Шесть вариантов смерти; последняя поза — тело на земле (дальше рэгдолл)."""
    D = {}
    lh_free = dict(lgrip=0.0, lh=(0.32, 0.0, 0.95), lhf=(0.4, 0.0, -1.0), lht=(0.0, -1.0, 0.0), lf=OPEN)
    # --- назад
    d1 = over(over(stand, **lh_free), hips=(0.0, 0.1, -0.1), hipr=(-18.0, 0.0, 8.0), spine=(-18.0, 8.0, 0.0), chest=(-12.0, 0, 0),
              head=(-25.0, -10.0, 0.0), fl=(0.13, -0.08, ANKLE, 0, 0), fr=(-0.15, 0.25, ANKLE, 10, 0))
    d2 = over(d1, hips=(0.0, 0.45, -0.62), hipr=(-55.0, 0.0, 12.0), spine=(-10.0, 0, 0), head=(-12.0, -18.0, 0),
              w=(-0.32, 0.25, 0.55, 50.0, -10.0, -60.0), lh=(0.42, 0.28, 0.5), fl=(0.16, -0.25, ANKLE, 10, 0), fr=(-0.14, -0.05, ANKLE + 0.02, -10, 0),
              rf=(0.3, None, 0.3, 0.3))
    d3 = over(d2, hips=(0.0, 0.8, -0.88), hipr=(-88.0, 0.0, 8.0), spine=(-3.0, 0, 0), chest=(-3.0, 0, 0), head=(6.0, -28.0, 0),
              w=(-0.45, 0.8, 0.1, 70.0, -5.0, -85.0), lh=lying_hand, lhf=(0.6, 0.6, 0), lht=(0, 0, 1),
              fl=(0.17, -0.3, ANKLE, 12, -40), fr=(-0.16, -0.12, ANKLE + 0.06, -15, -40), kr=(-0.3, -0.6, 0.6))
    D["Death_Back"] = K([(0, stand), (6, d1), (20, d2), (31, d3), (40, over(d3, hips=(0.0, 0.81, -0.9))), (55, over(d3, hips=(0.0, 0.81, -0.9)))], 55)
    # --- вперёд, лицом вниз
    f1 = over(over(stand, **lh_free), hips=(0.0, -0.06, -0.08), hipr=(15.0, 0.0, -5.0), spine=(20.0, 0, 0), chest=(10.0, 0, 0), head=(10.0, 0, 0),
              fl=(0.12, -0.1, ANKLE, 0, 0), fr=(-0.13, 0.18, ANKLE + 0.04, 0, -40))
    f2 = over(f1, hips=(0.0, -0.3, -0.55), hipr=(45.0, 0.0, -8.0), spine=(15.0, 0, 0), w=(-0.35, -0.55, 0.4, -20.0, -30.0, 40.0),
              lh=(0.35, -0.6, 0.4), lhf=(0, -0.5, -1), fl=(0.13, -0.05, ANKLE, 0, -20), fr=(-0.13, 0.25, ANKLE + 0.1, 0, -70), rf=(0.4, None, 0.3, 0.4))
    f3 = over(f2, hips=(0.0, -0.7, -0.84), hipr=(86.0, 0.0, -10.0), spine=(4.0, 0, 0), chest=(2.0, 0, 0), head=(-35.0, 25.0, 0),
              w=(-0.5, -1.0, 0.06, -60.0, -2.0, 80.0), lh=(0.4, -1.05, 0.08), lhf=(0.2, -1, 0), lht=(0, 0, 1),
              fl=(0.15, 0.25, ANKLE + 0.02, 0, -75), fr=(-0.14, 0.3, ANKLE + 0.02, 0, -80))
    D["Death_Forward"] = K([(0, stand), (7, f1), (19, f2), (28, f3), (34, over(f3, hips=(0.0, -0.71, -0.86))), (50, over(f3, hips=(0.0, -0.71, -0.86)))], 50)
    # --- на колени, затем лицом вперёд
    k1 = over(over(stand, **lh_free), hips=(0.0, 0.0, -0.08), spine=(10.0, 0, 0), head=(5.0, 0, 0))
    k2 = over(k1, hips=(0.0, 0.05, -0.47), hipr=(5.0, 0, 0), spine=(18.0, 0, 0), chest=(8.0, 0, 0), head=(-15.0, 0, 0),
              fl=(0.13, -0.2, ANKLE + 0.06, 0, -80), fr=(-0.13, -0.18, ANKLE + 0.06, 0, -80), kl=(0.2, -1.0, 0.1), kr=(-0.2, -1.0, 0.1),
              w=(-0.3, -0.25, 0.55, 0.0, -70.0, 0.0), lh=(0.28, -0.25, 0.55), lhf=(0, -0.3, -1))
    k3 = over(k2, hips=(0.0, -0.05, -0.5), spine=(30.0, 0, 0), chest=(15.0, 0, 0), head=(25.0, 0, 0))
    k4 = over(f3, hips=(0.0, -0.55, -0.86))
    D["Death_Knees"] = K([(0, stand), (6, k1), (16, k2), (30, k3), (42, k4), (60, k4)], 60)
    # --- разворот и падение на бок
    s1 = over(over(stand, **lh_free), hipr=(0.0, 0.0, 45.0), spine=(0.0, 20.0, 8.0), chest=(0, 15.0, 0), head=(0, 20.0, 0),
              fl=(0.1, 0.05, ANKLE, 40, 0), fr=(-0.15, 0.0, ANKLE + 0.03, 30, -30))
    s2 = over(s1, hips=(0.15, 0.1, -0.5), hipr=(-20.0, 50.0, 110.0), spine=(5.0, 10.0, 15.0), lh=(0.5, 0.0, 0.4),
              w=(-0.2, 0.3, 0.5, 100.0, -20.0, 0.0), rf=(0.3, None, 0.3, 0.3))
    s3 = over(s2, hips=(0.3, 0.2, -0.86), hipr=(-10.0, 85.0, 140.0), spine=(5.0, 0.0, 10.0), chest=(0, 0, 5.0), head=(0, 0, -20.0),
              lh=(0.55, 0.25, 0.08), w=(-0.1, 0.6, 0.06, 120.0, 0.0, 85.0),
              fl=(0.6, 0.6, ANKLE, 120, 0), fr=(0.35, 0.75, ANKLE + 0.08, 110, -30))
    D["Death_Spin"] = K([(0, stand), (8, s1), (20, s2), (30, s3), (50, s3)], 50)
    # --- выстрел в голову: голова назад, ноги подкашиваются, обмяк
    h1 = over(over(stand, **lh_free), head=(-35.0, 0, 0), neck=(-20.0, 0, 0), chest=(-8.0, 0, 0), hips=(0.0, 0.03, -0.05))
    h2 = over(h1, hips=(0.0, 0.08, -0.5), hipr=(0.0, 0, 5.0), spine=(25.0, 0, 0), chest=(10.0, 0, 0), head=(30.0, 10.0, 0),
              fl=(0.14, -0.1, ANKLE + 0.04, 0, -60), fr=(-0.12, 0.0, ANKLE + 0.04, 0, -60), kl=(0.3, -1, 0.2), kr=(-0.3, -1, 0.2),
              w=(-0.35, -0.1, 0.4, 0.0, -60.0, 30.0), lh=(0.33, -0.1, 0.4))
    h3 = over(d3, hips=(0.0, 0.55, -0.88))
    D["Death_Headshot"] = K([(0, stand), (3, h1), (14, h2), (26, h3), (45, h3)], 45)
    # --- отброшен взрывом
    b1 = over(over(stand, **lh_free), hips=(0.0, 0.35, 0.15), hipr=(-35.0, 0, 0), spine=(-25.0, 0, 0), head=(-30.0, 0, 0),
              fl=(0.2, 0.0, 0.35, 0, 20), fr=(-0.2, 0.15, 0.45, 0, 30), lh=(0.5, 0.2, 1.4), lhf=(0.5, 0, 1),
              w=(-0.4, 0.0, 1.3, 40.0, 40.0, 0.0), rf=OPEN)
    b2 = over(b1, hips=(0.0, 1.2, -0.3), hipr=(-75.0, 0, 10.0), fl=(0.25, 0.2, 0.5, 0, 0), fr=(-0.2, 0.5, 0.4, 0, 0))
    b3 = over(d3, hips=(0.0, 1.7, -0.88), lh=(0.5, 1.6, 0.08), w=(-0.5, 1.7, 0.1, 70.0, -5.0, -85.0),
              fl=(0.2, 0.7, ANKLE, 12, -40), fr=(-0.18, 0.95, ANKLE + 0.04, -15, -40))
    D["Death_Blast"] = K([(0, stand), (5, b1), (14, b2), (24, b3), (30, over(b3, hips=(0.0, 1.75, -0.9))), (50, over(b3, hips=(0.0, 1.75, -0.9)))], 50)
    return D


def rifle_clips():
    b, low, aim, crouch, crouch_low = rifle_poses()
    C = {}
    C["Idle"] = K([(0, low), (30, over(low, hips=(0, 0, -0.008), chest=(4.5, 0, 0), head=(-4.0, 14.0, 0))),
                   (60, over(low, head=(-2.0, -16.0, 0), chest=(3.5, 0, 0))), (90, low)], 90, loop=True)

    def loco(base, stride, lift, duty, bob, sway, rifle_bob=0.012, **g):
        def f(t):
            gg = gait(t, stride, lift, duty=duty, bob=bob, sway=sway, **g)
            p = over(base, **gg)
            s = math.sin(2 * math.pi * t)
            x, y, z, yw, pt, rl = base["w"]
            p["w"] = (x, y, z + rifle_bob * math.cos(4 * math.pi * t) + gg["hips"][2] * 0.8, yw + 1.5 * s, pt, rl)
            p["hipr"] = (base["hipr"][0], 0.0, base["hipr"][2] + gg["hipr"][2])
            p["chest"] = (base["chest"][0], base["chest"][1] + 3 * s, 0.0)
            return p
        return f
    C["Walk"] = cycle(32, loco(low, 0.78, 0.06, 0.6, 0.02, 4.0))
    C["WalkAim"] = cycle(36, loco(aim, 0.6, 0.05, 0.62, 0.012, 2.0, rifle_bob=0.004))
    C["WalkBack"] = cycle(36, loco(aim, 0.5, 0.05, 0.62, 0.012, 2.0, rifle_bob=0.004, back=True))
    C["StrafeL"] = cycle(32, loco(aim, 0.08, 0.05, 0.6, 0.012, 1.0, rifle_bob=0.004, side=0.4))
    C["StrafeR"] = cycle(32, loco(aim, 0.08, 0.05, 0.6, 0.012, 1.0, rifle_bob=0.004, side=-0.4))

    def run(t):
        g = gait(t, 1.45, 0.16, duty=0.38, bob=0.04, sway=7.0)
        s = math.sin(2 * math.pi * t)
        return over(b, **g, spine=(14.0, -6 * s, 0.0), chest=(5.0, -7 * s, 0.0), neck=(-8.0, 0, 0), head=(-6.0, 5 * s, 0),
                    w=(-0.06, -0.27, 1.25 + 0.015 * math.cos(4 * math.pi * t) + g["hips"][2], 40.0 + 4 * s, 30.0, -20.0),
                    elbr=(-0.5, 0.1, 0.9))
    C["Run"] = cycle(20, run)
    C["Aim"] = K([(0, aim), (30, over(aim, hips=(0, 0, -0.04), w=(-0.075, -0.31, 1.487, 2.4, 0.4, 0.0))), (60, aim)], 60, loop=True)
    C["AimIn"] = K([(0, low), (7, aim)], 7)
    C["Crouch"] = K([(0, crouch_low), (30, over(crouch_low, hips=(0, 0.05, -0.44))), (60, crouch_low)], 60, loop=True)
    C["CrouchAim"] = K([(0, crouch), (30, over(crouch, hips=(0, 0.05, -0.435))), (60, crouch)], 60, loop=True)
    kick = over(aim, w=(-0.075, -0.28, 1.51, 2.0, 7.0, 2.0), chest=(2.0, 13.0, 0.0), head=(5.0, 2.0, -10.0), rf=(1.0, 0.9, 0, 0.7))
    C["Fire"] = K([(0, aim), (2, kick), (9, aim)], 9)
    k2 = over(aim, w=(-0.075, -0.29, 1.5, 2.5, 4.0, 1.0), rf=(1.0, 0.9, 0, 0.7))
    C["FireAuto"] = K([(0, aim), (2, k2), (4, over(aim, rf=(1.0, 0.9, 0, 0.7))), (6, k2), (8, aim)], 8, loop=True)
    ready = over(aim, w=(-0.06, -0.28, 1.3, 25.0, 12.0, -55.0), head=(25.0, 10.0, 0), neck=(12.0, 8.0, 0))
    cell = dict(lh=(0.03, -0.24, 1.3), lhf=(-0.3, -0.6, -0.7), lht=(0.0, -1.0, 0.3), lf=FIST)
    away = dict(lh=(0.18, -0.06, 1.05), lhf=(0, 0, -1), lht=(0, -1, 0), lf=CLAW)
    C["Reload"] = K([(0, aim), (8, ready), (14, over(ready, lgrip=0.0, **cell)), (22, over(ready, lgrip=0.0, **away)),
                     (32, over(ready, lgrip=0.0, lh=(0.17, 0.0, 1.07), lhf=(0, 0, -1), lht=(0, -1, 0), lf=FIST)),
                     (42, over(ready, lgrip=0.0, **cell)), (48, over(ready, lgrip=0.0, **cell)), (54, ready), (62, aim)], 62)
    hit = over(low, hips=(0.0, 0.05, -0.03), hipr=(-6.0, 0.0, 8.0), spine=(-14.0, 10.0, 4.0), chest=(-8.0, 6.0, 0.0),
               head=(-15.0, -10.0, 0.0), w=(-0.14, -0.2, 1.06, 30.0, -40.0, -12.0), fr=(-0.13, 0.15, ANKLE, 0.0, 0.0))
    C["Hit"] = K([(0, low), (4, hit), (20, low)], 20)
    hitb = over(low, hips=(0.0, -0.05, -0.04), hipr=(8.0, 0, -6.0), spine=(16.0, -8.0, 0), head=(12.0, 8.0, 0), fl=(FX, -0.15, ANKLE, 0, 0))
    C["HitBack"] = K([(0, low), (4, hitb), (20, low)], 20)
    # езда: сидя на спидере (Ride) и стоя в AT-RT (Drive); руки на рулях, оружие вдоль руля
    ride = over(b, hips=(0.0, 0.08, -0.38), hipr=(15.0, 0, 0), spine=(18.0, 0, 0), chest=(6.0, 0, 0), neck=(-14.0, 0, 0), head=(-8.0, 0, 0),
                fl=(0.2, -0.38, 0.42, 10.0, 10.0), fr=(-0.2, -0.38, 0.42, -10.0, 10.0), kl=(0.4, -1.0, 0.7), kr=(-0.4, -1.0, 0.7),
                w=(-0.2, -0.5, 0.98, 0.0, 0.0, 0.0), lgrip=0.0, lh=(0.2, -0.47, 0.98), lhf=(0, -1, 0), lht=(-0.4, 0, 1), lf=FIST,
                rf=(1.0, 0.8, 0, 0.8), elbl=(0.6, 0.2, 1.0), elbr=(-0.6, 0.2, 1.0))
    C["Ride"] = K([(0, ride), (30, over(ride, hips=(0.0, 0.08, -0.385), chest=(7.0, 0, 0))), (60, ride)], 60, loop=True)
    drive = over(b, hips=(0.0, 0.03, -0.12), spine=(10.0, 0, 0), head=(-6.0, 0, 0), fl=(0.16, -0.1, ANKLE, 10.0, 0.0),
                 fr=(-0.16, 0.05, ANKLE, -5.0, 0.0), w=(-0.18, -0.42, 1.15, 0.0, -10.0, 0.0), lgrip=0.0,
                 lh=(0.18, -0.4, 1.15), lhf=(0, -1, 0), lht=(-0.4, 0, 1), lf=FIST)
    C["Drive"] = K([(0, drive), (30, over(drive, hips=(0.0, 0.03, -0.13))), (60, drive)], 60, loop=True)
    C.update(deaths(low))
    return C


# ============================================================================ СВЕТОВОЙ МЕЧ

def saber_poses(style):
    b = neutral()
    two = style in ("vader", "maul", "anakin", "mace")
    W = lambda x, y, z, yaw, pitch, roll=0.0: (x, y, z, yaw, pitch, roll)
    guard = over(b, w=W(-0.05, -0.33, 1.1, 10.0, 62.0), wup=(0.0, -1.0, 0.2), lgrip=1.0 if two else 0.0,
                 hips=(0.0, 0.0, -0.06), hipr=(0.0, 0.0, -18.0), spine=(5.0, 6.0, 0.0), chest=(3.0, 8.0, 0.0), head=(-3.0, 6.0, 0.0),
                 fl=(0.13, -0.22, ANKLE, 14.0, 0.0), fr=(-0.15, 0.22, ANKLE, 32.0, 0.0),
                 lh=(0.3, -0.06, 1.06), lhf=(0.1, -0.5, -0.8), lht=(0.0, -1.0, 0.3), rf=(1.0, None, 0.0, 0.9),
                 lf=(1.0, None, 0.0, 0.9) if two else CLAW)
    if style == "maul":
        guard = over(guard, w=W(-0.02, -0.33, 1.15, 75.0, 25.0), wup=(0.0, -1.0, 0.3), lgrip=1.0)
    if style in ("dooku",):
        guard = over(guard, w=W(-0.14, -0.4, 1.18, 8.0, 20.0, 10.0), wup=(0.0, 0.0, 1.0), lgrip=0.0,
                     hipr=(0.0, 0.0, -38.0), spine=(2.0, 12.0, 0), chest=(0.0, 16.0, 0), head=(0, -18.0, 0),
                     fl=(0.06, 0.2, ANKLE, 62.0, 0.0), fr=(-0.13, -0.25, ANKLE, 12.0, 0.0),
                     lh=(0.33, 0.16, 1.5), lhf=(0.3, 0.3, 1.0), lht=(0.0, -1.0, 0.0), elbl=(0.6, 0.3, 1.5), lf=OPEN)
    if style == "obiwan":
        # Соресу: клинок вперёд-вверх, левая рука вытянута вперёд ладонью
        guard = over(guard, w=W(-0.1, -0.36, 1.2, 15.0, 45.0, 0.0), wup=(0.0, -1.0, 0.5), lgrip=0.0,
                     lh=(0.15, -0.42, 1.25), lhf=(0.0, -0.6, 0.8), lht=(-1.0, 0, 0), lf=OPEN, elbl=(0.6, 0.0, 1.0))
    if style == "sidious":
        guard = over(guard, w=W(-0.13, -0.34, 1.06, 15.0, 35.0, 0.0), wup=(0.0, -1.0, 0.4), lgrip=0.0,
                     spine=(14.0, 6.0, 0.0), chest=(7.0, 6.0, 0.0), neck=(-8.0, 0, 0), head=(-6.0, 4.0, 0.0),
                     lh=(0.28, -0.2, 1.12), lhf=(0.2, -0.8, 0.2), lht=(0.0, 0.0, 1.0), lf=CLAW)
    return b, guard, two


def saber_clips(style):
    b, guard, two = saber_poses(style)
    C = {}
    hunch = 12.0 if style == "sidious" else 0.0
    if hunch:
        b = over(b, spine=(hunch, 0, 0), neck=(-6.0, 0, 0))
    low = over(b, w=(-0.22, -0.06, 0.98, 0.0, -80.0, 0.0), wup=(0.0, -1.0, 0.0), lgrip=0.0, blade=0.0, blade2=0.0,
               lh=(0.24, -0.01, 0.92), lhf=(0, 0, -1), lht=(0, -1, 0), lf=(0.3, None, 0.1, 0.2), rf=(1.0, None, 0.0, 0.9))
    breathe = over(guard, hips=(guard["hips"][0], 0, guard["hips"][2] - 0.008), chest=(guard["chest"][0] + 1.5,) + guard["chest"][1:])
    C["Idle"] = K([(0, guard), (45, breathe), (90, guard)], 90, loop=True)
    lift = over(low, w=(-0.12, -0.24, 1.15, 0.0, 40.0, 0.0), wup=(0.0, -1.0, 0.3))
    C["Ignite"] = K([(0, low), (12, lift), (16, over(lift, blade=1.0, blade2=1.0)), (30, over(guard, blade=1.0)), (40, guard)], 40)

    def loco(stride, lift_, duty, bob, sway, n, **g):
        def f(t):
            gg = gait(t, stride, lift_, duty=duty, bob=bob, sway=sway, **g)
            s = math.sin(2 * math.pi * t)
            p = over(guard, **gg)
            x, y, z, yw, pt, rl = guard["w"]
            p["w"] = (x, y, z + gg["hips"][2] * 0.8, yw + 2 * s, pt, rl)
            p["hipr"] = (0.0, 0.0, guard["hipr"][2] + gg["hipr"][2])
            p["spine"] = (guard["spine"][0], 3 * s, 0.0)
            return p
        return f
    C["Walk"] = cycle(32, loco(0.75, 0.06, 0.6, 0.02, 4.0, 32))
    C["WalkBack"] = cycle(34, loco(0.5, 0.05, 0.62, 0.012, 2.0, 34, back=True))
    C["StrafeL"] = cycle(30, loco(0.06, 0.05, 0.6, 0.012, 1.0, 30, side=0.42))
    C["StrafeR"] = cycle(30, loco(0.06, 0.05, 0.6, 0.012, 1.0, 30, side=-0.42))

    def run(t):
        g = gait(t, 1.5, 0.17, duty=0.38, bob=0.04, sway=7.0)
        s = math.sin(2 * math.pi * t)
        return over(b, **g, spine=(17.0, -6 * s, 0.0), chest=(5.0, -6 * s, 0.0), neck=(-10.0, 0, 0), head=(-6.0, 4 * s, 0),
                    w=(-0.26 + 0.05 * s, 0.05 - 0.13 * s, 1.03 + g["hips"][2], 0.0, -45.0 + 12 * s, 0.0), wup=(0.0, -1.0, 0.0), lgrip=0.0,
                    lh=(0.26, -0.02 + 0.22 * s, 1.03 + 0.06 * abs(s)), lhf=(0.0, -0.5 * s, -0.8), lht=(0, -1, 0), lf=(0.6, None, 0, 0.5),
                    rf=(1.0, None, 0, 0.9))
    C["Run"] = cycle(20, run)

    lg = 1.0 if two else 0.0

    def sw(x, y, z, yaw, pitch, roll, up, **kw):
        kw.setdefault("lgrip", lg)
        if lg:
            kw.setdefault("lf", (1.0, None, 0, 0.9))
        return over(guard, w=(x, y, z, yaw, pitch, roll), wup=up, **kw)
    a1_up = sw(-0.22, -0.06, 1.52, -40.0, 110.0, 0.0, (0.5, 0.0, -1.0), hipr=(0, 0, -32.0), chest=(-4.0, -15.0, 0), spine=(-2.0, -10.0, 0))
    a1_hit = sw(0.15, -0.46, 1.02, 50.0, -30.0, 0.0, (-0.5, -0.3, 0.8), hipr=(0, 0, 22.0), chest=(10.0, 20.0, 0), spine=(12.0, 10.0, 0),
                hips=(0, -0.09, -0.11), fl=(0.14, -0.38, ANKLE, 10.0, 0.0), fr=(-0.16, 0.27, ANKLE, 30.0, -10.0))
    C["Attack1"] = K([(0, guard), (8, a1_up), (13, a1_hit), (21, over(a1_hit, chest=(6.0, 15.0, 0))), (31, guard)], 31)
    a2_wind = sw(0.24, -0.13, 1.25, 100.0, 10.0, 90.0, (0.0, 0.0, 1.0), hipr=(0, 0, 26.0), chest=(0.0, 25.0, 0), spine=(0, 10.0, 0))
    a2_hit = sw(-0.27, -0.38, 1.2, -70.0, 5.0, -90.0, (0.0, 0.0, 1.0), hipr=(0, 0, -36.0), chest=(4.0, -25.0, 0), spine=(4.0, -12.0, 0),
                hips=(0, -0.06, -0.09), fl=(0.15, -0.33, ANKLE, 10.0, 0.0))
    C["Attack2"] = K([(0, guard), (9, a2_wind), (14, a2_hit), (21, over(a2_hit, chest=(4.0, -30.0, 0))), (33, guard)], 33)
    a3_up = sw(-0.05, 0.02, 1.6, 0.0, 120.0, 0.0, (0.0, 1.0, 0.0), spine=(-12.0, 0, 0), chest=(-8.0, 0, 0), head=(5.0, 0, 0), hips=(0, 0.04, -0.02))
    a3_hit = sw(-0.04, -0.54, 0.9, 0.0, -35.0, 0.0, (0.0, -1.0, 0.0), spine=(25.0, 0, 0), chest=(12.0, 0, 0), head=(-20.0, 0, 0),
                hips=(0, -0.16, -0.24), hipr=(8.0, 0, -10.0), fl=(0.14, -0.53, ANKLE, 5.0, 0.0), fr=(-0.16, 0.32, ANKLE, 20.0, -30.0))
    if style == "maul":
        spin = [sw(-0.0, -0.35, 1.2, 90.0 + a, 10.0, 0.0, (0.0, 0.0, 1.0), hipr=(0, 0, -10.0 + a * 0.1)) for a in (0, 120, 240, 360, 480)]
        C["Attack3"] = K([(0, guard)] + [(4 + i * 4, s) for i, s in enumerate(spin)] + [(26, a3_hit), (36, a3_hit), (48, guard)], 48)
    else:
        C["Attack3"] = K([(0, guard), (12, a3_up), (18, a3_hit), (28, a3_hit), (42, guard)], 42)
    # 4: колющий выпад
    a4_back = sw(-0.12, -0.05, 1.2, 0.0, 5.0, 0.0, (0, 0, 1), hipr=(0, 0, -30.0), chest=(0, -10.0, 0))
    a4_hit = sw(-0.02, -0.75, 1.25, 0.0, 0.0, 0.0, (0, 0, 1), hips=(0, -0.22, -0.16), hipr=(6.0, 0, -20.0), spine=(12, 0, 0),
                fl=(0.14, -0.6, ANKLE, 5.0, 0.0), fr=(-0.16, 0.35, ANKLE, 25.0, -35.0), elbr=(-0.5, -0.1, 1.0))
    C["Attack4"] = K([(0, guard), (8, a4_back), (13, a4_hit), (22, a4_hit), (34, guard)], 34)
    blk = sw(-0.05, -0.35, 1.48, 80.0, 18.0, 0.0, (0.0, -1.0, 1.0), spine=(-4.0, 0, 0), chest=(-4.0, 0, 0), head=(-10.0, 0, 0),
             lgrip=1.0 if two else 0.0, lh=(0.26, -0.32, 1.44), lhf=(0, -0.3, 1), lht=(-1, 0, 0))
    C["Block"] = K([(0, guard), (6, blk), (14, over(blk, hips=(0, 0.04, -0.09))), (24, blk), (34, guard)], 34)
    # отражение болтов: быстрые короткие движения клинком
    d1 = sw(-0.18, -0.36, 1.32, -35.0, 60.0, 0.0, (0.3, -1.0, 0.2))
    d2 = sw(0.12, -0.38, 1.2, 40.0, 55.0, 0.0, (-0.3, -1.0, 0.2), hipr=(0, 0, 5.0))
    C["Deflect1"] = K([(0, guard), (4, d1), (8, d1), (16, guard)], 16)
    C["Deflect2"] = K([(0, guard), (4, d2), (8, d2), (16, guard)], 16)
    push_w = over(guard, lgrip=0.0)
    if two:
        push_w = over(guard, lgrip=0.0, w=(-0.22, -0.12, 1.06, 0.0, 70.0, 0.0), wup=(0.0, -1.0, 0.2))
    p_wind = over(push_w, lh=(0.2, 0.05, 1.36), lhf=(0.0, 0.0, 1.0), lht=(-1.0, 0, 0), chest=(-4.0, 20.0, 0), hipr=(0, 0, 10.0), lf=CLAW)
    p_out = over(push_w, lh=(0.13, -0.6, 1.44), lhf=(0.0, 0.0, 1.0), lht=(-1.0, 0, 0), chest=(8.0, -15.0, 0), spine=(6.0, -8.0, 0),
                 hips=(0, -0.1, -0.09), fl=(0.14, -0.4, ANKLE, 0.0, 0.0), elbl=(0.6, 0.2, 1.1), lf=OPEN)
    C["ForcePush"] = K([(0, guard), (12, p_wind), (17, p_out), (30, p_out), (44, guard)], 44)
    if style == "vader":
        choke = over(push_w, lh=(0.18, -0.55, 1.56), lhf=(0.1, -0.6, 0.8), lht=(-1.0, 0, 0), head=(4.0, -8.0, 0), chest=(0.0, -10.0, 0),
                     elbl=(0.6, 0.3, 1.0), lf=CLAW)
        sq = over(choke, lh=(0.18, -0.55, 1.62), lf=(0.85, None, 0, 0.7))
        C["ForceChoke"] = K([(0, guard), (14, choke), (30, sq), (60, sq), (66, over(sq, lh=(0.18, -0.53, 1.68))), (90, guard)], 90)
    if style == "sidious":
        lt = over(b, w=(-0.26, -0.04, 1.0, 0.0, -75.0, 0.0), wup=(0.0, -1.0, 0.0), lgrip=0.0, blade=0.0,
                  lh=(0.17, -0.55, 1.35), lhf=(0.0, -1.0, 0.1), lht=(0.0, 0.0, 1.0), lf=CLAW,
                  spine=(18.0, 0, 0), chest=(8.0, 0, 0), neck=(-12.0, 0, 0), head=(-10.0, 0, 0), hips=(0, -0.04, -0.07),
                  fl=(0.14, -0.27, ANKLE, 10.0, 0.0), fr=(-0.14, 0.16, ANKLE, 20.0, 0.0), elbl=(0.6, 0.0, 1.2))
        C["ForceLightning"] = K([(0, guard), (12, lt), (20, over(lt, lh=(0.18, -0.57, 1.37))), (28, lt), (36, over(lt, lh=(0.16, -0.57, 1.33))),
                                 (44, lt), (60, lt), (75, guard)], 75)
    hit = over(guard, hips=(0.0, 0.06, -0.05), hipr=(-6.0, 0.0, 10.0), spine=(-12.0, 10.0, 4.0), chest=(-8.0, 8.0, 0.0),
               head=(-14.0, -10.0, 0.0), fr=(-0.14, 0.32, ANKLE, 20.0, 0.0))
    C["Hit"] = K([(0, guard), (4, hit), (22, guard)], 22)
    hitb = over(guard, hips=(0.0, -0.05, -0.05), hipr=(8.0, 0, -6.0), spine=(16.0, -8.0, 0), head=(12.0, 8.0, 0))
    C["HitBack"] = K([(0, guard), (4, hitb), (22, guard)], 22)
    stand = over(guard)
    D = deaths(stand)
    for k, frames in D.items():
        for i, p in enumerate(frames):
            t = i / max(1, len(frames) - 1)
            p["blade"] = max(0.0, 1.0 - t * 2.5)
            p["blade2"] = p["blade"]
    C.update(D)
    return C


def clips_for(spec):
    return rifle_clips() if spec.get("role") in ("soldier", "heavy", "officer") else saber_clips(spec.get("style", "vader"))


LOOPING = {"Ride", "Drive", "Idle", "Walk", "WalkAim", "WalkBack", "StrafeL", "StrafeR", "Run", "Aim", "Crouch", "CrouchAim", "FireAuto"}
