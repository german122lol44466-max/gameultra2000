"""Одежда и детали для ситхов и джедаев: туники, штаны, сапоги, перчатки, пояса, накидки, волосы, бороды."""

import math

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.noise import noise

from . import armor as A
from . import cloth as C
from . import kit as K

FINGER = ("Thumb", "Index", "Middle", "Ring", "Little")


def base(d):
    return d.split(".")[0].rstrip("123")


def is_hand(d):
    return base(d) in ("Hand",) + FINGER


def is_head(d):
    return d in ("Head", "Jaw", "Eye.L", "Eye.R")


def body_materials(info, fn):
    """Материалы граней тела: fn(доминирующая кость, центр) -> индекс слота материала."""
    me = info.obj.data
    for p in me.polygons:
        doms = [info.dom[i] for i in p.vertices]
        dom = max(set(doms), key=doms.count)
        p.material_index = fn(dom, p.center)


def sink(info, region, depth=0.004):
    """Утопить закрытые одеждой участки тела внутрь (меньше «прострелов» кожи сквозь ткань)."""
    me = info.obj.data
    for v in me.vertices:
        if region(info.dom[v.index], v.co):
            v.co = v.co - info.no[v.index] * depth


def delete_faces(info, region):
    me = info.obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    dead = []
    for f in bm.faces:
        doms = [info.dom[v.index] for v in f.verts]
        dom = max(set(doms), key=doms.count)
        if region(dom, f.calc_center_median()):
            dead.append(f)
    # FACES_ONLY: вершины остаются (индексы не сдвигаются, info.dom/kd остаются верными);
    # висячие вершины чистятся в chars2.make после всей экипировки
    bmesh.ops.delete(bm, geom=dead, context="FACES_ONLY")
    bm.to_mesh(me)
    bm.free()


# ----------------------------------------------------------------------------- облегающие слои

def tunic(kit, info, S, mat, bottom_z, sleeve=0.85, offset=0.009, collar=0.03, neck_open=0.0, name="tunic"):
    """Рубаха/туника: корпус + рукава до доли sleeve длины предплечья, низ до bottom_z."""
    nz = Vector(S["Neck"][0]).z

    def ok(c, n, d):
        if is_head(d) or is_hand(d):
            return False
        b = base(d)
        if b in ("UpperLeg", "LowerLeg", "Foot", "Toes"):
            return c.z > bottom_z
        if b == "LowerArm":
            return A.along(S, d, c)[0] < sleeve + 0.1
        return True
    cuts = [((0, 0, nz + collar - 0.03), (0, 0, -1)), ((0, 0, bottom_z), (0, 0, 1))]
    for s in ("L", "R"):
        h, t = Vector(S[f"LowerArm.{s}"][0]), Vector(S[f"LowerArm.{s}"][1])
        p = h + (t - h) * sleeve
        cuts.append((p, -(t - h).normalized()))
    if neck_open:
        nk = Vector(S["Neck"][0])
        cuts.append((nk + Vector((0, -0.05, -neck_open)), (0, 0.4, -1)))
    return A.shell(kit, info, mat, ok, cuts, offset=offset, thick=0.003, smooth=5, smooth_f=0.35, bevel=0.0, name=name, subsurf=0)


def pants(kit, info, S, mat, top_z, bottom_t=0.85, offset=0.008, name="pants"):
    def ok(c, n, d):
        b = base(d)
        if b in ("UpperLeg", "Hips"):
            return c.z < top_z + 0.02
        if b == "LowerLeg":
            return A.along(S, d, c)[0] < bottom_t + 0.1
        return False
    cuts = [((0, 0, top_z), (0, 0, -1))]
    for s in ("L", "R"):
        h, t = Vector(S[f"LowerLeg.{s}"][0]), Vector(S[f"LowerLeg.{s}"][1])
        cuts.append((h + (t - h) * bottom_t, -(t - h).normalized()))
    return A.shell(kit, info, mat, ok, cuts, offset=offset, thick=0.003, smooth=4, smooth_f=0.35, bevel=0.0, name=name)


def boots(kit, info, S, mat, top_t=0.45, offset=0.011, flare=0.012, name="boots", sole="rubber"):
    """Сапоги: голенище по ноге + гладкий объём стопы (носок не повторяет пальцы)."""
    objs = []
    for s, sg in (("L", 1), ("R", -1)):
        h, t = Vector(S[f"LowerLeg.{s}"][0]), Vector(S[f"LowerLeg.{s}"][1])
        top = h + (t - h) * top_t

        def ok(c, n, d, s=s):
            return d == f"LowerLeg.{s}" and A.along(S, d, c)[0] > top_t - 0.1

        def infl(co, top=top):
            return flare * K.smooth01(1 - (top.z - co.z) / 0.08)
        objs.append(A.shell(kit, info, mat, ok, [(top, (t - h).normalized())], offset=offset, thick=0.004, smooth=10,
                            smooth_f=0.5, bevel=0.0, inflate=infl, name=name))
        ank = Vector(S[f"Foot.{s}"][0])
        ball = Vector(S[f"Foot.{s}"][1])
        toe = Vector(S[f"Toes.{s}"][1])
        heel = Vector((ank.x, ank.y + 0.045, 0.0))
        tip = Vector((toe.x, toe.y - 0.012, 0.0))
        mid = (heel + tip) / 2
        L = (tip - heel).length
        d = (tip - heel).normalized()
        yaw = math.atan2(d.x, -d.y)

        def shape(co, u, L=L):
            x, y, z = co
            # носок ниже и уже, пятка выше; низ — плоская подошва
            f = (-y / (L * 0.5))                      # 1 у носка, -1 у пятки
            z = z * (1.0 - 0.35 * max(0.0, f)) if z > 0 else max(z, -0.0)
            x = x * (1.0 - 0.18 * max(0.0, f) ** 2)
            return (x, y, z)
        o = kit.sphere(f"Foot.{s}", mat, (mid.x, mid.y, 0.012), 1.0, scale=(0.052, L * 0.53, 0.085), seg=36,
                       deform=shape, subsurf=1, rot=(0, 0, -yaw))
        kit.box(f"Foot.{s}", sole, (mid.x, mid.y, 0.008), (0.1, L + 0.01, 0.016), rot=(0, 0, -yaw), bevel=0.006, smooth=True)
        # голенище до щиколотки (соединяет стопу и верх)
        kit.cyl(f"LowerLeg.{s}", mat, (ank.x, ank.y + 0.01, 0.05), (ank.x, ank.y + 0.005, ank.z + 0.06), 0.052, 0.048, verts=28)
        objs.append(o)
    return objs


def gloves(kit, info, mat, offset=0.003, cuff_t=0.82, S=None, name="gloves"):
    def ok(c, n, d):
        if is_hand(d):
            return True
        if S and base(d) == "LowerArm":
            return A.along(S, d, c)[0] > cuff_t - 0.1
        return False
    cuts = []
    if S:
        for s in ("L", "R"):
            h, t = Vector(S[f"LowerArm.{s}"][0]), Vector(S[f"LowerArm.{s}"][1])
            cuts.append((h + (t - h) * cuff_t, (t - h).normalized()))
    return A.shell(kit, info, mat, ok, cuts, offset=offset, thick=0.0015, smooth=1, smooth_f=0.2, bevel=0.0, name=name)


def belt(kit, info, S, mat, z, h=0.05, offset=0.02, buckle=None, name="belt"):
    o = A.shell(kit, info, mat, lambda c, n, d: (not is_hand(d)) and base(d) not in ("LowerArm", "UpperArm") and abs(c.z - z) < h,
                [((0, 0, z - h / 2), (0, 0, 1)), ((0, 0, z + h / 2), (0, 0, -1))],
                offset=offset, thick=0.005, smooth=6, smooth_f=0.5, bevel=0.0015, name=name)
    if buckle:
        y = front_y(info, 0.0, z) - offset - 0.006
        kit.box("Hips", buckle, (0, y, z), (0.055, 0.012, h * 0.9), bevel=0.003)
    return o


def front_y(info, x, z, tol=0.02):
    best = None
    for c, i, d in info.kd.find_range(Vector((x, -0.1, z)), 0.3):
        if abs(c.x - x) < tol and abs(c.z - z) < tol and info.no[i].y < -0.2:
            if best is None or c.y < best:
                best = c.y
    return best if best is not None else -0.12


def tabard(kit, info, S, mat, bottom_z, width=0.09, offset=0.016, name="tabard"):
    """Накидка джедая: две полосы ткани через плечи — спереди внахлёст, сзади до пояса."""
    nz = Vector(S["Neck"][0]).z
    objs = []
    for sg in (1, -1):
        def ok(c, n, d, sg=sg):
            if is_hand(d) or is_head(d) or base(d) in ("UpperArm", "LowerArm"):
                return False
            if c.z > nz + 0.02 or c.z < bottom_z - 0.02:
                return False
            # полоса: от плеча (x=sg*0.12) к центру внизу спереди
            t = (nz - c.z) / max(0.01, (nz - bottom_z))
            cx = sg * (0.11 - 0.13 * t) if c.y < 0 else sg * 0.1
            return abs(c.x - cx) < width
        objs.append(A.shell(kit, info, mat, ok, [((0, 0, bottom_z), (0, 0, 1))], offset=offset, thick=0.003, smooth=8,
                            smooth_f=0.5, bevel=0.0, name=name))
    return objs


# ----------------------------------------------------------------------------- волосы и бороды

def hair_shell(kit, info, S, mat, region, thick=0.012, noise_amp=0.006, length_back=0.0, name="hair", bone="Head",
               volume=1.0, center=None):
    """Волосы/борода: оболочка выбранной области головы с объёмом и шумом прядей.
    region(p, n) — p относительно центра головы (center)."""
    head = Vector(center) if center is not None else Vector(S["Head"][0])

    def ok(c, n, d):
        return (is_head(d) or d == "Neck") and region(c, n)
    infl = lambda co: (thick * volume) + noise_amp * noise(co * 45.0)
    o = A.shell(kit, info, mat, ok, (), offset=0.002, thick=0.003, smooth=14, smooth_f=0.5, bevel=0.0, subsurf=1,
                inflate=infl, name=name, rigid=None if bone == "Head" else bone)
    return o


def hide_covered(info, keep_head=True, keep_hands=True, depth=0.009, feet=True):
    """Тело под одеждой утапливаем, стопы внутри сапог удаляем — кожа не проступает сквозь ткань."""
    def covered(d, c):
        if keep_head and (is_head(d) or d == "Neck"):
            return False
        if keep_hands and is_hand(d):
            return False
        return True
    sink(info, covered, depth)
    # корпус под одеждой удаляем совсем (кроме окрестности воротника и манжет)
    nz = max(c.z for c, d in zip(info.co, info.dom) if d == "Neck") - 0.09
    delete_faces(info, lambda d, c: covered(d, c) and base(d) in ("Spine", "Chest", "UpperChest", "Hips", "UpperArm", "Shoulder", "UpperLeg")
                 and c.z < nz)
    if feet:
        # стопы целиком (и по костям, и по высоте — пальцы иногда весят на голень), чтобы не торчали из сапог
        delete_faces(info, lambda d, c: base(d) in ("Foot", "Toes") or (c.z < 0.11 and not is_hand(d)))
