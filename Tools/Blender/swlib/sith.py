"""Ситхи: Дарт Вейдер, Дарт Мол, Дарт Сидиус, граф Дуку (Дарт Тиранус)."""

import math

from mathutils import Quaternion, Vector

from . import kit as K
from .troopers import J, T, fist, limb_between, sides

# =========================================================================== веса ткани


def skirt_weights(top_bone="Hips", top_z=1.0, knee_z=0.52, follow=0.85):
    """Полы робы: сверху — таз, ниже — постепенно за бёдрами и голенями своей стороны."""
    def fn(co):
        t = K.smooth01((top_z - co.z) / (top_z - knee_z))
        t2 = K.smooth01((knee_z - co.z) / (knee_z - 0.1)) * 0.6
        side = K.smooth01(0.5 + co.x / 0.16)
        w = {top_bone: 1.0 - t * follow}
        for s, f in (("L", side), ("R", 1.0 - side)):
            w[f"UpperLeg.{s}"] = t * follow * f * (1 - t2)
            w[f"LowerLeg.{s}"] = t * follow * f * t2
        return w
    return fn


def cape_weights(top=1.43):
    zs = [(top, "Chest"), (1.08, "Cape.1"), (0.66, "Cape.2"), (0.18, "Cape.3")]

    def fn(co):
        z = co.z
        if z >= top:
            return {"Chest": 1.0}
        for (z0, b0), (z1, b1) in zip(zs, zs[1:]):
            if z >= z1:
                t = (z0 - z) / (z0 - z1)
                return {b0: 1 - t, b1: t}
        return {"Cape.3": 1.0}
    return fn


def cape(k, mat, width=0.25, depth=0.16, flare=0.16, bottom=0.04, wrap=100, wrap_low=70, top=1.47, back=0.03):
    rows = []
    zs = [top - (top - bottom) * (i / 14) ** 1.0 for i in range(15)]
    for z in zs:
        f = (top - z)
        a = math.radians(wrap + (wrap_low - wrap) * K.smooth01(f / 0.25))
        rx = width + f * flare
        ry = depth + f * flare * 0.8
        row = []
        n = 22
        for i in range(n + 1):
            th = -a + 2 * a * i / n
            fold = 0.012 * math.sin(th * 7) * K.smooth01(f / 0.4)
            row.append(Vector((math.sin(th) * (rx + fold), math.cos(th) * (ry + fold) + back, z)))
        rows.append(row)
    return k.sheet(cape_weights(), mat, rows, thickness=0.008)


def skirt(k, mat, top_z, bottom_z, w_top, d_top, w_bot, d_bot, cy=0.0, split=0.0, bone="Hips", knee=0.52):
    """Полы робы (юбка): кольцо сечений с весами за ногами. split>0 — разрез спереди (рад)."""
    rows = []
    n = 12
    for i in range(n + 1):
        t = i / n
        z = top_z + (bottom_z - top_z) * t
        w = w_top + (w_bot - w_top) * t
        d = d_top + (d_bot - d_top) * t
        row = []
        m = 40
        for j in range(m + 1):
            th = -math.pi / 2 + split + (2 * math.pi - 2 * split) * j / m
            fold = 0.01 * math.sin(th * 9) * t
            row.append(Vector((math.cos(th) * (w + fold), math.sin(th) * (d + fold) + cy, z)))
        rows.append(row)
    return k.sheet(skirt_weights(bone, top_z, knee), mat, rows, thickness=0.008)


# =========================================================================== лицо


def bump(co, c, r, amount, direction):
    d = (co - c).length
    if d > r * 2.5:
        return co
    return co + direction * amount * math.exp(-(d / r) ** 2)


def face(k, skin, eye_mat="eye_yellow", cz=1.69, cy=-0.012, scale=1.0, matfn=None, gaunt=0.0, ears=True,
         nose=1.0, brow=1.0):
    """Голова человека. Возвращает объект головы. matfn — раскраска (татуировки Мола)."""
    sc = scale
    rx, ry, rz = 0.074 * sc, 0.097 * sc, 0.114 * sc
    C = Vector((0, cy, cz))
    eyes = [Vector((0.031 * sg, -0.083, 0.008)) * sc for sg in (1, -1)]

    def deform(co, u):
        co = Vector(co)
        if u.y > 0 and u.z > -0.2:
            co.y *= 1.06                                         # затылок
        if u.z > 0.3:
            co.z = 0.3 * rz + (co.z - 0.3 * rz) * 0.86           # макушка ниже, не «яйцо»
            co.x *= 1.04
        lower = K.smooth01((-u.z - 0.15) / 0.85)
        co.x *= 1.0 - (0.3 + 0.12 * gaunt) * lower               # челюсть уже
        if u.y > 0:
            co.y *= 1.0 - 0.35 * lower                           # под затылком — шея
        if u.y < 0:
            co.y = max(co.y, -ry * 0.9)                          # лицо плоское
            co.y -= 0.01 * sc * K.smooth01((-u.z - 0.6) / 0.4)   # подбородок
        for e in eyes:                                           # глазницы, брови, скулы
            sg = math.copysign(1, e.x)
            co = bump(co, e + Vector((0, 0.008, 0)) * sc, 0.018 * sc, -0.012 * sc, Vector((0, -1, 0)))
            co = bump(co, e + Vector((0.003 * sg, -0.004, 0.022)) * sc, 0.017 * sc, 0.006 * brow * sc, Vector((0, -1, 0.2)))
            co = bump(co, e + Vector((0.016 * sg, 0.004, -0.03)) * sc, 0.018 * sc, (0.005 - 0.009 * gaunt) * sc, Vector((0.5 * sg, -1, 0)))
        if gaunt:
            for sg in (1, -1):
                co = bump(co, Vector((0.05 * sg, -0.045, -0.05)) * sc, 0.022 * sc, -0.008 * gaunt * sc, Vector((sg, -0.4, 0)))
        return co

    head = k.sphere("Head", skin, C, 1.0, scale=(rx, ry, rz), seg=64, deform=deform, subsurf=1, matfn=matfn)
    # нос: клин от переносицы к кончику
    top = C + Vector((0, -0.086, 0.0)) * sc
    tip = C + Vector((0, -0.105 - 0.008 * (nose - 1), -0.042)) * sc
    k.lathe("Head", skin, top, tip, [(0, 0.0), (0.0, 0.008 * sc), (0.03 * sc, 0.011 * sc), (0.044 * sc, 0.014 * sc), (0.047 * sc, 0.0)],
            verts=16, flat=0.7, subsurf=1)
    k.sphere("Head", skin, tip + Vector((0, 0.004, 0.0)) * sc, 0.0095 * sc, scale=(1.5, 1.0, 0.85), seg=16)
    for sg in (1, -1):
        k.sphere("Head", skin, tip + Vector((0.009 * sg, 0.01, 0.0)) * sc, 0.007 * sc, scale=(1.0, 1.0, 0.8), seg=12)
    # губы
    k.decal("Head", "lips", head, [(-0.021 * sc, -0.5, cz - 0.062 * sc), (0.021 * sc, -0.5, cz - 0.062 * sc),
                                   (0.016 * sc, -0.5, cz - 0.07 * sc), (-0.016 * sc, -0.5, cz - 0.07 * sc)],
            n=4, thick=0.004, lift=0.0)
    # глаза и веки
    for e in eyes:
        ec = C + e + Vector((0, 0.003, 0))
        k.sphere("Head", eye_mat, ec, 0.0108 * sc, seg=20, matfn=lambda u, m=eye_mat: m if u.y < -0.8 else "eye_white")
        k.sphere("Head", skin, ec + Vector((0, 0.0005, 0.001)), 0.0118 * sc, seg=20, keep=lambda u: u.z > 0.12, solid=0.002)
        k.sphere("Head", skin, ec + Vector((0, 0.0005, 0.0)), 0.0116 * sc, seg=20, keep=lambda u: u.z < -0.5, solid=0.002)
    if ears:
        for sg in (1, -1):
            k.sphere("Head", skin, C + Vector((0.072 * sg, 0.008, -0.01)) * sc, 0.03 * sc, scale=(0.3, 0.5, 0.95),
                     rot=(0, 0, math.radians(18 * sg)), seg=16)
    return head


# =========================================================================== общее тело


def suit_body(k, rest, mat_torso, mat_arms, mat_legs, glove="glove", boots="boot", bulk=1.0, boot_top=0.42):
    b = bulk
    k.loft("Hips", mat_legs, [(0.86, 0.1 * b, 0.08 * b, 2.5), (0.93, 0.15 * b, 0.1 * b, 2.8), (1.02, 0.155 * b, 0.1 * b, 3), (1.08, 0.14 * b, 0.095 * b, 3)])
    k.loft("Spine", mat_torso, [(1.06, 0.14 * b, 0.095 * b, 3), (1.18, 0.145 * b, 0.1 * b, 3), (1.26, 0.165 * b, 0.105 * b, 3)])
    k.loft("Chest", mat_torso, [(1.24, 0.16 * b, 0.105 * b, 3), (1.34, 0.19 * b, 0.12 * b, 3.2), (1.43, 0.19 * b, 0.11 * b, 3.2),
                                (1.48, 0.12, 0.08, 2.4), (1.51, 0.06, 0.055, 2)])
    for s, sg in sides():
        limb_between(k, f"UpperArm.{s}", mat_arms, 0.058 * b, 0.045 * b, pad=(0.04, 0.02))
        limb_between(k, f"LowerArm.{s}", mat_arms, 0.046 * b, 0.036 * b, pad=(0.02, 0.0))
        k.sphere(f"LowerArm.{s}", mat_arms, J(f"LowerArm.{s}"), 0.046 * b)
        k.sphere(f"UpperArm.{s}", mat_arms, J(f"UpperArm.{s}"), 0.062 * b)
        limb_between(k, f"UpperLeg.{s}", mat_legs, 0.085 * b, 0.06 * b, pad=(0.04, 0.03))
        limb_between(k, f"LowerLeg.{s}", mat_legs, 0.06 * b, 0.042 * b, pad=(0.02, 0.0), bulge=0.006)
        k.sphere(f"LowerLeg.{s}", mat_legs, J(f"LowerLeg.{s}"), 0.062 * b)
        fist(k, rest, s, glove)
        an = J(f"Foot.{s}")
        # сапоги
        k.lathe(f"LowerLeg.{s}", boots, (an.x, an.y, 0.06), (an.x, an.y + 0.004, boot_top),
                [(0, 0.052), (0.1, 0.05), (boot_top - 0.12, 0.058), (boot_top - 0.06, 0.064), (boot_top - 0.06, 0.0)], verts=24)
        k.box(f"Foot.{s}", boots, (an.x, an.y - 0.05, 0.05), (0.09, 0.2, 0.085), bevel=0.03, smooth=True)
        k.box(f"Toes.{s}", boots, (an.x, an.y - 0.15, 0.033), (0.085, 0.08, 0.05), bevel=0.022, smooth=True)
        k.box(f"Foot.{s}", "rubber", (an.x, an.y - 0.06, 0.008), (0.095, 0.24, 0.016), bevel=0.005)


def belt(k, mat, z=1.02, h=0.06, w=0.165, d=0.112, buckle="metal_silver"):
    k.loft("Hips", mat, [(z - h / 2, w, d, 3.2), (z + h / 2, w, d, 3.2)], subsurf=1)
    k.box("Hips", buckle, (0, -d - 0.008, z), (0.05, 0.012, h * 0.9), bevel=0.004)


# =========================================================================== Вейдер


def vader_helmet(k):
    cz = 1.69
    B, M = "vader_gloss", "vader_mask"

    def dome(co, u):
        x, y, z = co
        # плавный раструб назад и в стороны
        if u.z < 0.0:
            f = K.smooth01(-u.z / 0.9)
            back = K.smooth01(u.y * 1.5 + 0.6)
            x *= 1.0 + 0.75 * f * back
            y *= 1.0 + 0.55 * f * back
            z = z - 0.15 * f * back
        if u.y < -0.3 and u.z < 0.25:             # спереди купол открыт под маску
            y = max(y, -0.118)
        return (x, y, z)

    h = k.sphere("Head", B, (0, 0.0, cz), 1.0, scale=(0.13, 0.145, 0.142), seg=56, deform=dome, subsurf=1,
                 keep=lambda u: u.z > -0.98)
    # гребень по центру купола
    k.decal("Head", B, h, [(-0.006, -0.12, 2.0), (0.006, -0.12, 2.0), (0.006, 0.13, 2.0), (-0.006, 0.13, 2.0)],
            direction=(0, 0, -1), n=12, thick=0.012, lift=0.004)

    # маска: клиновидное лицо (V-образный нос), скулы
    def mask(co, u):
        x, y, z = co
        y = max(y, -0.15 + abs(x) * 0.62 + max(0.0, -(z + 0.02)) * 0.15)
        if u.z < -0.5:
            x *= 1.0 - 0.25 * (-0.5 - u.z) / 0.5
        return (x, y, z)

    m = k.sphere("Head", M, (0, -0.02, cz - 0.03), 1.0, scale=(0.112, 0.13, 0.115), seg=56, deform=mask, subsurf=1)
    F = lambda pts: [(x, -0.5, z) for x, z in pts]
    for s, sg in sides():
        # линзы — большие, скошенные треугольники
        k.decal("Head", "vader_lens", m, F([(0.012 * sg, cz + 0.01), (0.073 * sg, cz + 0.022), (0.075 * sg, cz - 0.022), (0.022 * sg, cz - 0.02)]),
                n=8, thick=0.006, lift=0.0)
        # скулы
        k.decal("Head", M, m, F([(0.03 * sg, cz - 0.03), (0.09 * sg, cz - 0.02), (0.085 * sg, cz - 0.04), (0.035 * sg, cz - 0.05)]),
                n=6, thick=0.01, lift=0.004)
        # «клыки» вдоль рта
        k.decal("Head", "metal_silver", m, F([(0.026 * sg, cz - 0.06), (0.032 * sg, cz - 0.058), (0.05 * sg, cz - 0.12), (0.044 * sg, cz - 0.122)]),
                n=6, thick=0.004, lift=0.002)
        # боковые болты
        k.cyl("Head", "metal_silver", (0.105 * sg, -0.06, cz - 0.06), (0.112 * sg, -0.065, cz - 0.06), 0.006, verts=10)
    # треугольная решётка рта
    k.decal("Head", "metal_grey", m, F([(-0.03, cz - 0.055), (0.03, cz - 0.055), (0.004, cz - 0.118), (-0.004, cz - 0.118)]),
            n=8, thick=0.006, lift=0.002)
    for i in range(7):
        x = -0.021 + i * 0.007
        zb = cz - 0.06 - (0.03 - abs(x)) * 1.9
        k.decal("Head", "metal_black", m, F([(x - 0.0015, cz - 0.06), (x + 0.0015, cz - 0.06), (x + 0.0015, zb), (x - 0.0015, zb)]),
                n=2, thick=0.004, lift=0.0045)
    # подбородок
    k.box("Head", M, (0, -0.105, cz - 0.135), (0.05, 0.03, 0.03), bevel=0.01, smooth=True)
    return h


def vader(k, rest):
    suit_body(k, rest, "vader_leather", "vader_leather", "vader_cloth", glove="glove", boots="vader_gloss", bulk=1.08, boot_top=0.48)
    # наплечный «панцирь» и воротник
    k.loft("Chest", "vader_gloss", [(1.33, 0.205, 0.125, 2.6, -0.005), (1.40, 0.24, 0.14, 2.8, 0.0), (1.46, 0.235, 0.135, 2.8, 0.0),
                                    (1.51, 0.15, 0.1, 2.4), (1.535, 0.085, 0.075, 2.0)], subsurf=1)
    for s, sg in sides():
        k.sphere(f"UpperArm.{s}", "vader_gloss", J(f"UpperArm.{s}") + Vector((0.015 * sg, 0, 0.0)), 1.0,
                 scale=(0.085, 0.095, 0.07), seg=28, deform=lambda co, u: (co.x, co.y, max(co.z, -0.025)))
        # наручи
        limb_between(k, f"LowerArm.{s}", "vader_gloss", 0.05, 0.042, pad=(-0.04, -0.02))
    # нагрудный пульт
    k.box("Chest", "metal_grey", (0, -0.15, 1.31), (0.13, 0.025, 0.1), bevel=0.006)
    for i, mname in enumerate(("btn_red", "btn_green", "btn_blue", "btn_red")):
        k.box("Chest", mname, (-0.045 + i * 0.03, -0.165, 1.335), (0.016, 0.006, 0.014), bevel=0.002)
    for i in range(5):
        k.box("Chest", "metal_black", (-0.04 + i * 0.02, -0.164, 1.29), (0.006, 0.004, 0.03), bevel=0.001)
    for sg in (1, -1):
        k.box("Chest", "metal_silver", (0.11 * sg, -0.135, 1.34), (0.025, 0.012, 0.025), bevel=0.004)
    # пояс с двумя блоками
    belt(k, "vader_gloss", z=1.03, h=0.075, w=0.17, d=0.117)
    for sg in (1, -1):
        k.box("Hips", "metal_silver", (0.06 * sg, -0.128, 1.035), (0.05, 0.02, 0.06), bevel=0.004)
        for j in range(3):
            k.box("Hips", "metal_black", ((0.045 + j * 0.015) * sg, -0.139, 1.04), (0.006, 0.004, 0.03), bevel=0.001)
    skirt(k, "vader_cloth", 1.0, 0.12, 0.17, 0.12, 0.25, 0.2, cy=0.0, split=0.0)
    cape(k, "vader_cloth", width=0.25, depth=0.165, flare=0.2, wrap=105, wrap_low=80, top=1.5, back=0.02)
    vader_helmet(k)


# =========================================================================== Мол


def maul_tattoo(u):
    x, y, z = u
    if y > 0.25:                                          # затылок: красный с чёрными полосами
        return "maul_black" if math.sin(math.atan2(x, y) * 7) > 0.55 and z > -0.3 else "maul_red"
    ax = abs(x)
    if ax < 0.09 and z > 0.15:
        return "maul_black"                               # полоса по центру лба
    if z > 0.35 and math.sin(ax * 22 + z * 6) > 0.6:
        return "maul_black"                               # изогнутые полосы на лбу
    if ((ax - 0.36) / 0.3) ** 2 + ((z - 0.04) / 0.2) ** 2 < 1:
        return "maul_black"                               # глазницы
    if ax < 0.18 and -0.45 < z < 0.1:
        return "maul_black"                               # нос
    if ax < 0.42 and -0.72 < z < -0.45:
        return "maul_black"                               # рот
    if ax < 0.1 and z < -0.72:
        return "maul_black"                               # подбородок по центру
    if ax > 0.72 and z < 0.1:
        return "maul_black"                               # скулы сзади
    return "maul_red"


def maul(k, rest):
    suit_body(k, rest, "maul_cloth", "maul_cloth", "maul_cloth", glove="glove", boots="boot", bulk=1.0, boot_top=0.4)
    # туника с высоким воротом и запахом
    k.loft("Chest", "maul_cloth", [(1.26, 0.168, 0.11, 3), (1.36, 0.195, 0.125, 3.2), (1.44, 0.19, 0.115, 3.2),
                                   (1.5, 0.08, 0.07, 2), (1.58, 0.06, 0.058, 2)], subsurf=1)
    k.box("Chest", "maul_cloth2", (0.03, -0.122, 1.36), (0.02, 0.01, 0.2), rot=(0, math.radians(-20), 0), bevel=0.004)
    belt(k, "leather_dark", z=1.04, h=0.07, w=0.162, d=0.11, buckle="leather_dark")
    k.loft("Hips", "maul_cloth2", [(1.0, 0.168, 0.115, 3), (1.08, 0.168, 0.115, 3)], subsurf=1)
    skirt(k, "maul_cloth", 1.02, 0.5, 0.17, 0.12, 0.23, 0.16, split=0.35)
    # голова: лысая, татуировки, рожки
    head = face(k, "maul_red", "eye_yellow", cz=1.69, matfn=maul_tattoo, gaunt=0.3)
    for i, (ax, az) in enumerate([(0, 0.95), (0.35, 0.85), (-0.35, 0.85), (0.6, 0.65), (-0.6, 0.65), (0.2, 0.62), (-0.2, 0.62),
                                  (0.75, 0.35), (-0.75, 0.35), (0.0, 0.7)]):
        u = Vector((ax, -0.25 + 0.5 * (i % 3) * 0.3, az)).normalized()
        p = Vector((u.x * 0.081, u.y * 0.098 - 0.012, 1.69 + u.z * 0.113))
        k.cyl("Head", "horn", p - u * 0.004, p + u * (0.022 + 0.006 * (i % 2)), 0.007, 0.0015, verts=10)
    k.cyl("Neck", "maul_black", (0, 0, 1.5), (0, 0, 1.62), 0.045, 0.043, verts=20)


# =========================================================================== Сидиус


def sidious(k, rest):
    suit_body(k, rest, "robe_black", "robe_black", "robe_black", glove="skin_pale", boots="boot", bulk=0.95, boot_top=0.3)
    # роба до пола
    skirt(k, "robe_black", 1.24, 0.03, 0.2, 0.14, 0.3, 0.26, bone="Spine", knee=0.55)
    k.loft("Chest", "robe_black", [(1.22, 0.2, 0.14, 2.6), (1.36, 0.215, 0.14, 2.8), (1.45, 0.205, 0.13, 2.8), (1.52, 0.12, 0.1, 2.2)], subsurf=1)
    # широкие рукава
    for s, sg in sides():
        h, t = J(f"UpperArm.{s}"), T(f"UpperArm.{s}")
        k.lathe(f"UpperArm.{s}", "robe_black", h + Vector((0, 0, 0.02)), t,
                [(0, 0.07), (0.15, 0.075), (0.3, 0.085)], verts=24, caps=False, subsurf=1)
        h2, t2 = J(f"LowerArm.{s}"), T(f"LowerArm.{s}")
        d = (t2 - h2).normalized()
        k.lathe(f"LowerArm.{s}", "robe_black", h2 - d * 0.03, t2 + d * 0.02,
                [(0, 0.08), (0.12, 0.1), (0.27, 0.125), (0.3, 0.13)], verts=24, caps=False, subsurf=1)
    # плащ с капюшоном
    cape(k, "robe_black", width=0.24, depth=0.16, flare=0.15, wrap=115, wrap_low=88, top=1.5, back=0.02)
    face(k, "skin_pale", "eye_yellow", cz=1.68, gaunt=1.0, nose=1.15, brow=1.6)
    # капюшон: оболочка с вырезом спереди
    k.sphere("Head", "robe_black", (0, 0.005, 1.7), 1.0, scale=(0.118, 0.13, 0.14), seg=40, subsurf=1, solid=0.008,
             keep=lambda u: not (u.y < -0.35 and u.z < 0.62 and abs(u.x) < 0.78),
             deform=lambda co, u: (co.x, co.y - 0.02 * K.smooth01(u.z), co.z - (0.06 if u.z < -0.6 else 0) * (1 if u.y > -0.2 else 0)))


# =========================================================================== Дуку


def dooku(k, rest):
    suit_body(k, rest, "dooku_cloth", "dooku_cloth", "dooku_cloth", glove="glove", boots="boot", bulk=0.98, boot_top=0.48)
    k.loft("Chest", "dooku_cloth", [(1.26, 0.168, 0.11, 3), (1.36, 0.195, 0.125, 3.2), (1.44, 0.19, 0.115, 3.2),
                                    (1.5, 0.09, 0.075, 2), (1.56, 0.062, 0.058, 2)], subsurf=1)
    # туника с запахом и полами
    k.box("Chest", "dooku_cloth2", (0.0, -0.124, 1.36), (0.02, 0.01, 0.22), rot=(0, math.radians(25), 0), bevel=0.004)
    belt(k, "leather_brown", z=1.03, h=0.05, w=0.16, d=0.108, buckle="metal_silver")
    skirt(k, "dooku_cloth2", 1.01, 0.42, 0.168, 0.118, 0.23, 0.17, split=0.3)
    # плащ с цепью-застёжкой
    cape(k, "dooku_cape", width=0.25, depth=0.16, flare=0.17, wrap=112, wrap_low=82, top=1.5, back=0.02)
    for sg in (1, -1):
        k.cyl("Chest", "metal_silver", (0.11 * sg, -0.1, 1.46), (0.115 * sg, -0.112, 1.46), 0.016, verts=16)
    for i in range(9):
        x = -0.1 + i * 0.025
        z = 1.44 - 0.03 * math.cos(x / 0.1 * math.pi / 2)
        k.torus("Chest", "metal_silver", (x, -0.125, z), 0.008, 0.0022, axis=(0, 1 if i % 2 else 0, 0 if i % 2 else 1))
    head = face(k, "skin_old", "eye_brown", cz=1.69, gaunt=0.45, nose=1.15, brow=1.2)
    # седые волосы зачёсаны назад
    k.sphere("Head", "hair_white", (0, 0.004, 1.705), 1.0, scale=(0.087, 0.104, 0.112), seg=40, subsurf=1,
             keep=lambda u: (u.z > 0.15 and u.y > -0.55) or (u.y > 0.25 and u.z > -0.55) or (abs(u.x) > 0.85 and u.z > -0.2 and u.y > -0.1),
             solid=0.01, deform=lambda co, u: (co.x, co.y + 0.006 * K.smooth01(u.z), co.z))
    # короткая борода и усы
    k.sphere("Head", "hair_white", (0, -0.068, 1.616), 1.0, scale=(0.036, 0.024, 0.026), seg=28, subsurf=1,
             keep=lambda u: u.y < 0.1)
    for sg in (1, -1):
        k.cyl("Head", "hair_white", (0.004 * sg, -0.104, 1.636), (0.024 * sg, -0.096, 1.624), 0.005, 0.003, verts=10)
