"""Штурмовики: белая броня поверх чёрного комбинезона. Три варианта на одном скелете."""

import math

from mathutils import Vector

from . import kit as K
from .rig import skeleton


def J(name):
    """Голова кости из скелета (для привязки деталей к суставам)."""
    return skeleton(True)[name][0]


def T(name):
    return skeleton(True)[name][1]


def fist(k, rest, side, mat="glove", size=1.0):
    """Кулак в пространстве кости кисти: Y — к костяшкам, Z — большой палец."""
    bone = f"Hand.{side}"
    old = k.xform
    k.xform = rest[bone]
    s = size
    k.box(bone, mat, (0, 0.06 * s, 0), (0.05 * s, 0.085 * s, 0.085 * s), bevel=0.016 * s, smooth=True)
    k.box(bone, mat, (0.0, 0.1 * s, 0.0), (0.045 * s, 0.03 * s, 0.08 * s), bevel=0.012 * s, smooth=True)
    k.box(bone, mat, (-0.008, 0.045 * s, 0.045 * s), (0.028 * s, 0.05 * s, 0.024 * s), bevel=0.01 * s, smooth=True)
    k.cyl(bone, mat, (0, -0.01, 0), (0, 0.025 * s, 0), 0.03 * s, 0.033 * s, verts=16)
    k.xform = old


def limb_between(k, bone, mat, r1, r2, pad=(0.0, 0.0), flat=1.0, bulge=0.0, offset=(0, 0, 0), verts=20):
    h, t = J(bone), T(bone)
    d = (t - h).normalized()
    a = h - d * pad[0] + Vector(offset)
    b = t + d * pad[1] + Vector(offset)
    return k.limb(bone, mat, a, b, r1, r2, flat=flat, bulge=bulge, verts=verts)


def sides():
    return (("L", 1), ("R", -1))


# =========================================================================== шлем

def helmet(k, color_mat="armor", lens="lens", grey="blue_grey", band="black_gloss", vent="black_gloss"):
    """Шлем штурмовика по образцу ANH: купол с чёрным обручем, широкие скошенные линзы,
    «нахмуренный» рот-шеврон с прорезями, два выпуклых фильтра внизу, ребристый вокодер, боковые прорези."""
    cz = 1.675

    def deform(co, unit):
        x, y, z = co
        front = K.smooth01(-unit.y * 1.5)
        lower = K.smooth01((0.2 - unit.z) * 1.4)
        y -= 0.018 * front * lower                # лицевая часть чуть выдвинута
        if unit.y < 0:
            y = max(y, -0.157)                    # плоское «лицо»
        if unit.z < -0.25:                        # раструб: затылок и бока
            f = (-0.25 - unit.z) / 0.75
            x *= 1.0 + 0.2 * f
            y *= 1.0 + 0.16 * f * (1.0 if unit.y > 0 else 0.15)
        z = max(z, -0.13)
        return (x, y, z)

    h = k.sphere("Head", color_mat, (0, -0.005, cz), 1.0, scale=(0.13, 0.148, 0.148), seg=56, deform=deform, subsurf=1)
    H = lambda x, z: K.surface(h, (x, -0.5, z), (0, 1, 0))
    F = lambda pts, **kw: [(x, -0.5, z) for x, z in pts]

    # --- чёрный обруч над глазами (по всему кругу)
    k.loft("Head", band, [(cz + 0.058, 0.124, 0.142, 2.0, -0.005), (cz + 0.072, 0.12, 0.138, 2.0, -0.005)], ring=48, subsurf=0,
           caps=(False, False)).modifiers.new("S", "SOLIDIFY").thickness = 0.006

    for s, sg in sides():
        # --- линзы: широкие, верх ровный, низ скошен вниз к краю
        k.decal("Head", lens, h, F([(0.012 * sg, cz + 0.05), (0.078 * sg, cz + 0.05), (0.083 * sg, cz + 0.012), (0.018 * sg, cz + 0.028)]),
                n=8, thick=0.004, lift=0.0005)
        # белая рамка вокруг линзы (утопленная линза)
        k.decal("Head", color_mat, h, F([(0.008 * sg, cz + 0.056), (0.086 * sg, cz + 0.056), (0.09 * sg, cz + 0.004), (0.014 * sg, cz + 0.021)]),
                n=8, thick=0.004, lift=-0.0006)
        # --- рот-шеврон: от центра вниз наружу
        k.decal("Head", vent, h, F([(0.0, cz - 0.022), (0.088 * sg, cz - 0.064), (0.09 * sg, cz - 0.084), (0.0, cz - 0.046)]),
                n=8, thick=0.004, lift=0.0008)
        for i in range(7):
            x0 = (0.006 + i * 0.0118) * sg
            x1 = x0 + 0.005 * sg
            zt = lambda x: cz - 0.022 - abs(x) * (0.042 / 0.088)
            k.decal("Head", grey, h, F([(x0, zt(x0) - 0.004), (x1, zt(x1) - 0.004), (x1, zt(x1) - 0.019), (x0, zt(x0) - 0.019)]),
                    n=2, thick=0.003, lift=0.0028)
        # --- выпуклые «щёки» с фильтрами
        p, nrm = H(0.05 * sg, cz - 0.105)
        pod_c = p + Vector((0.004 * sg, 0.012, -0.012))
        k.sphere("Head", color_mat, pod_c, 1.0, scale=(0.05, 0.042, 0.044), seg=28)
        fc = pod_c + Vector((0.008 * sg, -0.034, -0.016))
        d = Vector((0.12 * sg, -1.0, -0.45)).normalized()
        k.cyl("Head", vent, fc - d * 0.01, fc + d * 0.004, 0.019, verts=24)
        k.torus("Head", color_mat, fc + d * 0.004, 0.019, 0.004, axis=d)
        k.cyl("Head", "metal_grey", fc + d * 0.0, fc + d * 0.002, 0.012, verts=20)
        # --- вертикальные прорези на щеках под ушами
        for i in range(5):
            x = (0.093 + i * 0.006) * sg
            k.decal("Head", vent, h, [(x, -0.5, cz - 0.0), (x + 0.0028 * sg, -0.5, cz - 0.0),
                                      (x + 0.0028 * sg, -0.5, cz - 0.05), (x, -0.5, cz - 0.05)], n=3, thick=0.003, lift=0.0005)
        # --- наклонные прорези на нижних боках
        for i in range(7):
            z = cz - 0.075 - i * 0.0055
            k.decal("Head", vent, h, [(0.15 * sg, -0.02 - i * 0.002, z), (0.15 * sg, -0.065 - i * 0.002, z + 0.003),
                                      (0.15 * sg, -0.065 - i * 0.002, z + 0.0055), (0.15 * sg, -0.02 - i * 0.002, z + 0.0025)],
                    direction=(-sg, 0, 0), n=2, thick=0.003, lift=0.0005)
        # --- «уши»: круглые накладки с кольцом
        ear = Vector((0.128 * sg, 0.0, cz - 0.0))
        k.cyl("Head", color_mat, ear - Vector((0.014 * sg, 0, 0)), ear + Vector((0.01 * sg, 0, 0)), 0.04, 0.035, verts=28)
        k.torus("Head", grey, ear + Vector((0.009 * sg, 0, 0)), 0.026, 0.003, axis=(sg, 0, 0))
        k.cyl("Head", color_mat, ear + Vector((0.008 * sg, 0, 0)), ear + Vector((0.013 * sg, 0, 0)), 0.018, verts=20)
        # синяя полоса на затылке
        k.decal("Head", grey, h, [(0.0, 0.5, cz - 0.07), (0.125 * sg, 0.1, cz - 0.07), (0.125 * sg, 0.1, cz - 0.085), (0.0, 0.5, cz - 0.085)],
                direction=(0, -1, 0), n=8, thick=0.003)

    # --- центральный ребристый вокодер между щёк (ступенчатый, расширяется книзу)
    p, nrm = H(0.0, cz - 0.07)
    top = p + Vector((0, -0.004, 0))
    k.box("Head", vent, top + Vector((0, 0.006, -0.042)), (0.026, 0.03, 0.085), bevel=0.004, smooth=True)
    k.box("Head", vent, top + Vector((0, 0.01, -0.07)), (0.05, 0.026, 0.03), bevel=0.004, smooth=True)
    for i in range(3):
        k.box("Head", "metal_black", top + Vector((-0.0075 + i * 0.0075, -0.0105, -0.045)), (0.003, 0.004, 0.07), bevel=0.0012)
    # «нос»: валик между глаз к рту
    k.decal("Head", color_mat, h, F([(-0.012, cz + 0.04), (0.012, cz + 0.04), (0.016, cz - 0.022), (-0.016, cz - 0.022)]),
            n=6, thick=0.008, lift=0.003)
    # уплотнитель шеи
    k.cyl("Neck", "rubber", (0, 0, 1.5), (0, 0, 1.6), 0.058, 0.055, verts=24)
    return h


# =========================================================================== тело

def body(k, rest, v):
    A, S = "armor", "suit"
    # --- комбинезон
    k.loft("Hips", S, [(0.86, 0.1, 0.08, 2.5), (0.93, 0.15, 0.1, 2.8), (1.02, 0.155, 0.1, 3), (1.08, 0.14, 0.095, 3)])
    k.loft("Spine", S, [(1.07, 0.135, 0.09, 3), (1.18, 0.14, 0.095, 3), (1.26, 0.16, 0.1, 3)])
    k.loft("Chest", S, [(1.24, 0.155, 0.1, 3), (1.36, 0.18, 0.11, 3), (1.45, 0.17, 0.1, 3), (1.5, 0.075, 0.065, 2)])
    k.cyl("Neck", S, (0, 0, 1.48), (0, 0, 1.6), 0.052, 0.048, verts=20)
    for s, sg in sides():
        limb_between(k, f"UpperArm.{s}", S, 0.05, 0.04, pad=(0.03, 0.02))
        limb_between(k, f"LowerArm.{s}", S, 0.04, 0.032, pad=(0.02, 0.0))
        limb_between(k, f"UpperLeg.{s}", S, 0.078, 0.056, pad=(0.04, 0.03))
        limb_between(k, f"LowerLeg.{s}", S, 0.055, 0.04, pad=(0.02, 0.0), bulge=0.006)
        k.sphere(f"LowerArm.{s}", S, J(f"LowerArm.{s}"), 0.041)
        k.sphere(f"LowerLeg.{s}", S, J(f"LowerLeg.{s}"), 0.06)
        fist(k, rest, s)

    # --- нагрудник (обхватывает корпус) и спина
    k.loft("Chest", A, [(1.24, 0.17, 0.118, 3.2, -0.006), (1.31, 0.2, 0.135, 3.4, -0.012), (1.39, 0.212, 0.138, 3.6, -0.012),
                        (1.45, 0.195, 0.12, 3.4, -0.004), (1.495, 0.1, 0.08, 2.4, 0.0)], caps=(True, True))
    # грудные пластины-рельеф
    for s, sg in sides():
        k.box("Chest", A, (0.078 * sg, -0.137, 1.378), (0.115, 0.03, 0.105), rot=(math.radians(-6), 0, math.radians(8 * sg)), bevel=0.014, smooth=True)
    # пульт на груди
    k.box("Chest", "grey", (-0.055, -0.15, 1.31), (0.05, 0.012, 0.03), bevel=0.003)
    for i, m in enumerate(("btn_blue", "btn_red", "btn_blue")):
        k.box("Chest", m, (-0.07 + i * 0.015, -0.156, 1.312), (0.008, 0.004, 0.008), bevel=0.001)
    # живот: сегменты
    for i, z in enumerate((1.11, 1.16, 1.21)):
        k.box("Spine", A, (0, -0.1 + i * -0.004, z), (0.23 - i * 0.004, 0.035, 0.046), bevel=0.012, smooth=True)
    k.box("Spine", A, (0, 0.098, 1.17), (0.24, 0.035, 0.13), bevel=0.014, smooth=True)
    # ключичный воротник
    k.torus("Chest", A, (0, -0.004, 1.49), 0.085, 0.016, scale=(1.05, 0.95, 1))
    # --- пояс
    k.loft("Hips", A, [(0.99, 0.165, 0.11, 3.2), (1.06, 0.165, 0.11, 3.2)], subsurf=1)
    k.box("Hips", A, (0, -0.118, 1.025), (0.13, 0.02, 0.06), bevel=0.006)
    for i in range(4):
        k.box("Hips", "blue_grey", (-0.045 + i * 0.03, -0.13, 1.025), (0.018, 0.006, 0.04), bevel=0.002)
    for s, sg in sides():
        k.box("Hips", A, (0.14 * sg, -0.06, 1.0), (0.05, 0.06, 0.07), rot=(0, 0, math.radians(30 * sg)), bevel=0.008)
    # термальный детонатор за спиной
    k.cyl("Hips", "metal_silver", (-0.13, 0.1, 1.02), (0.0, 0.12, 1.02), 0.026, verts=20)
    k.cyl("Hips", "metal_black", (-0.07, 0.122, 1.02), (-0.05, 0.124, 1.02), 0.028, verts=20)
    # гульфик
    k.box("Hips", A, (0, -0.09, 0.9), (0.12, 0.04, 0.12), rot=(math.radians(-10), 0, 0), bevel=0.02, smooth=True)
    k.box("Hips", A, (0, 0.1, 0.92), (0.22, 0.03, 0.12), rot=(math.radians(8), 0, 0), bevel=0.02, smooth=True)

    # --- руки
    for s, sg in sides():
        sh = J(f"UpperArm.{s}")
        # наплечник
        k.sphere(f"UpperArm.{s}", A, sh + Vector((0.012 * sg, 0, -0.005)), 1.0, scale=(0.078, 0.082, 0.066), seg=24,
                 deform=lambda co, u: (co.x, co.y, max(co.z, -0.02)))
        limb_between(k, f"UpperArm.{s}", A, 0.06, 0.053, pad=(-0.07, -0.05), flat=0.92)
        limb_between(k, f"LowerArm.{s}", A, 0.052, 0.043, pad=(-0.03, -0.035), bulge=0.004)
        # бёдра, колени, голени
        limb_between(k, f"UpperLeg.{s}", A, 0.095, 0.074, pad=(-0.05, -0.06), offset=(0, -0.006, 0), bulge=0.004)
        k.sphere(f"LowerLeg.{s}", A, J(f"LowerLeg.{s}") + Vector((0, -0.045, 0.0)), 1.0, scale=(0.05, 0.03, 0.055))
        limb_between(k, f"LowerLeg.{s}", A, 0.068, 0.054, pad=(-0.06, -0.03), bulge=0.006)
        # ботинки
        an = J(f"Foot.{s}")
        k.box(f"Foot.{s}", A, (an.x, an.y - 0.04, 0.055), (0.095, 0.2, 0.09), bevel=0.03, smooth=True)
        k.box(f"Toes.{s}", A, (an.x, an.y - 0.15, 0.035), (0.09, 0.08, 0.055), bevel=0.022, smooth=True)
        k.box(f"Foot.{s}", "black_gloss", (an.x, an.y - 0.06, 0.008), (0.1, 0.22, 0.016), bevel=0.005)
        k.cyl(f"LowerLeg.{s}", A, (an.x, an.y, 0.2), (an.x, an.y, 0.08), 0.052, 0.058, verts=20)

    helmet(k)

    # --- варианты
    if v.get("pauldron"):
        s, sg = v["pauldron_side"], (1 if v["pauldron_side"] == "L" else -1)
        sh = J(f"UpperArm.{s}")
        k.sphere(f"UpperArm.{s}", v["pauldron"], sh + Vector((0.02 * sg, 0, 0.012)), 1.0, scale=(0.095, 0.1, 0.075), seg=28,
                 deform=lambda co, u: (co.x, co.y, max(co.z, -0.01)))
        k.cyl("Chest", v["pauldron"], (0.1 * sg, -0.12, 1.43), (0.1 * sg, 0.12, 1.43), 0.006, verts=8)
    if v.get("backpack"):
        k.box("Chest", "grey", (0, 0.17, 1.3), (0.24, 0.1, 0.26), bevel=0.02, smooth=True)
        k.box("Chest", "metal_dark", (0, 0.225, 1.3), (0.18, 0.02, 0.2), bevel=0.006)
        for i in range(3):
            k.cyl("Chest", "metal_silver", (-0.06 + i * 0.06, 0.17, 1.43), (-0.06 + i * 0.06, 0.17, 1.48), 0.012, verts=12)
        k.cyl("Chest", "black_gloss", (0.11, 0.17, 1.22), (0.16, 0.0, 1.06), 0.008, verts=8)
