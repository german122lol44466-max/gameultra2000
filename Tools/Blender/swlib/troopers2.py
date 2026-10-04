"""Штурмовик и клон v2: тело MakeHuman в чёрном комбинезоне + пластины брони, подогнанные по телу."""

import math

import bmesh
import bpy
from mathutils import Matrix, Vector

from . import armor as A
from . import kit as K
from . import troopers as T1


def strip_body(info, glove_mat_index=1, delete_head=True):
    """Тело: кисти — материал перчаток, голову (внутри шлема) удаляем."""
    me = info.obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    dead = []
    for f in bm.faces:
        doms = [info.dom[v.index] for v in f.verts]
        dom = max(set(doms), key=doms.count)
        base = dom.split(".")[0].rstrip("123")
        if base in ("Hand", "Thumb", "Index", "Middle", "Ring", "Little"):
            f.material_index = glove_mat_index
        if delete_head and dom in ("Head", "Jaw", "Eye.L", "Eye.R") and f.calc_center_median().z > info_neck_z(info) + 0.04:
            dead.append(f)
    bmesh.ops.delete(bm, geom=dead, context="FACES_ONLY")   # индексы вершин не сдвигаются (info.dom остаётся верным)
    bm.to_mesh(me)
    bm.free()


def info_neck_z(info):
    zs = [c.z for c, d in zip(info.co, info.dom) if d == "Neck"]
    return sum(zs) / len(zs)


ARMS = ("UpperArm", "LowerArm", "Hand", "Shoulder")


def _arm(d):
    b = d.split(".")[0].rstrip("123")
    return b in ARMS or b in ("Thumb", "Index", "Middle", "Ring", "Little")


def stormtrooper(kit, info, S, v=None, head=None):
    """Пластины брони. S — скелет в A-позе (rig2.skel_from_body), head — (центр, размер) головы до удаления."""
    v = v or {}
    W = v.get("armor", "armor")
    G = "blue_grey"
    chest_c = Vector(S["Chest"][0])
    spine_c = Vector(S["Spine"][0])
    neck_z = Vector(S["Neck"][0]).z
    hips_z = Vector(S["Hips"][0]).z
    up_z = Vector(S["UpperChest"][0]).z
    belt_hi = spine_c.z + 0.02
    belt_lo = hips_z - 0.03
    TORSO = ("Chest", "UpperChest", "Spine")

    # --- нагрудник (спереди) и спина
    sx = abs(Vector(S["UpperArm.L"][0]).x)
    def torso_front(c, n, d):
        return (not _arm(d)) and c.y < chest_c.y + 0.02 and abs(c.x) < sx and chest_c.z - 0.06 < c.z < neck_z + 0.02
    A.shell(kit, info, W, torso_front,
            cuts=[((0, 0, neck_z - 0.035), (0, 0, -1)), ((0, 0, chest_c.z - 0.01), (0, 0, 1)),
                  ((sx - 0.055, 0, 0), (-1, 0, 0)), ((-sx + 0.055, 0, 0), (1, 0, 0)),
                  ((0.07, 0, neck_z - 0.02), (-0.5, 0, -1)), ((-0.07, 0, neck_z - 0.02), (0.5, 0, -1))],
            offset=0.024, thick=0.007, smooth=14, name="chest")
    def torso_back(c, n, d):
        return (not _arm(d)) and c.y > chest_c.y + 0.0 and abs(c.x) < sx and belt_hi < c.z < neck_z + 0.02
    A.shell(kit, info, W, torso_back,
            cuts=[((0, 0, neck_z - 0.04), (0, 0, -1)), ((0, 0, belt_hi + 0.025), (0, 0, 1)),
                  ((sx - 0.06, 0, 0), (-1, 0, 0)), ((-sx + 0.06, 0, 0), (1, 0, 0)),
                  ((0.07, 0, neck_z - 0.03), (-0.5, 0, -1)), ((-0.07, 0, neck_z - 0.03), (0.5, 0, -1))],
            offset=0.024, thick=0.007, smooth=14, name="back")
    # --- пресс: три сегмента
    zs = [belt_hi + 0.01, belt_hi + 0.055, belt_hi + 0.1, chest_c.z - 0.025]
    for i in range(3):
        A.shell(kit, info, W, lambda c, n, d: (not _arm(d)) and c.y < spine_c.y - 0.02 and abs(c.x) < 0.16,
                cuts=[((0, 0, zs[i] + 0.004), (0, 0, 1)), ((0, 0, zs[i + 1] - 0.004), (0, 0, -1)),
                      ((0.12 - i * 0.004, 0, 0), (-1, 0, 0)), ((-0.12 + i * 0.004, 0, 0), (1, 0, 0))],
                offset=0.018 + i * 0.002, thick=0.006, smooth=8, name=f"ab{i}")
    # --- пояс (вокруг талии), гульфик, задняя пластина
    A.shell(kit, info, W, lambda c, n, d: (not _arm(d)) and belt_lo - 0.03 < c.z < belt_hi + 0.03,
            cuts=[((0, 0, belt_lo), (0, 0, 1)), ((0, 0, belt_hi), (0, 0, -1))],
            offset=0.03, thick=0.008, smooth=8, name="belt")
    crotch = min(Vector(S["UpperLeg.L"][0]).z, hips_z) - 0.09
    A.shell(kit, info, W, lambda c, n, d: d in ("Hips", "UpperLeg.L", "UpperLeg.R") and c.y < Vector(S["Hips"][0]).y - 0.02,
            cuts=[((0, 0, belt_lo - 0.005), (0, 0, -1)), ((0, 0, crotch), (0, 0, 1)),
                  ((0.07, 0, 0), (-1, 0, 0)), ((-0.07, 0, 0), (1, 0, 0))],
            offset=0.02, thick=0.007, smooth=8, name="cod")
    A.shell(kit, info, W, lambda c, n, d: d in ("Hips", "UpperLeg.L", "UpperLeg.R") and c.y > Vector(S["Hips"][0]).y + 0.03,
            cuts=[((0, 0, belt_lo - 0.005), (0, 0, -1)), ((0, 0, crotch + 0.05), (0, 0, 1)),
                  ((0.13, 0, 0), (-1, 0, 0)), ((-0.13, 0, 0), (1, 0, 0))],
            offset=0.022, thick=0.007, smooth=8, name="butt")
    for s, sg in (("L", 1), ("R", -1)):
        sh = Vector(S[f"UpperArm.{s}"][0])
        # наплечник: верх плеча
        # наплечник-колпак: верхняя часть эллипсоида над плечевым суставом
        nrm = Vector((0.55 * sg, 0.0, 0.85)).normalized()

        def bell(co, u, nrm=nrm):
            dd = co.dot(nrm)
            if dd < -0.012:
                co = co - nrm * (dd + 0.012)
            return co
        kit.sphere(f"UpperArm.{s}", W, sh + Vector((0.012 * sg, 0.0, 0.006)), 1.0, scale=(0.078, 0.085, 0.07), seg=32,
                   deform=bell, subsurf=1, solid=0.006)
        A.limb_tube(kit, info, S, f"UpperArm.{s}", W, 0.33, 0.82, offset=0.016, name="bicep")
        A.limb_tube(kit, info, S, f"LowerArm.{s}", W, 0.12, 0.78, offset=0.014, name="forearm")
        A.limb_tube(kit, info, S, f"UpperLeg.{s}", W, 0.1, 0.8, offset=0.02, name="thigh")
        A.limb_tube(kit, info, S, f"LowerLeg.{s}", W, 0.13, 0.78, offset=0.017, name="shin")
        kn = Vector(S[f"LowerLeg.{s}"][0])
        A.shell(kit, info, W, lambda c, n, d, s=s, kn=kn: d in (f"LowerLeg.{s}", f"UpperLeg.{s}") and n.y < -0.35 and abs(c.z - kn.z) < 0.06,
                offset=0.022, thick=0.007, smooth=6, name="knee")
    from . import wear as Wr
    Wr.boots(kit, info, S, v.get("boot", W), top_t=0.72, offset=0.016, flare=0.004, sole="rubber")

    # --- детали: пульт на груди, кнопки пресса, подсумки на поясе, детонатор
    ch = Vector((-0.06, chest_c.y - 0.15, chest_c.z + 0.08))
    k = kit
    k.box("Chest", "grey", (ch.x, _front(info, ch) - 0.004, ch.z), (0.05, 0.012, 0.032), bevel=0.003)
    for i, m in enumerate(("btn_blue", "btn_red", "btn_blue", "btn_white")):
        p = Vector((ch.x - 0.016 + i * 0.011, 0, ch.z + 0.002))
        k.box("Chest", m, (p.x, _front(info, ch) - 0.011, p.z), (0.007, 0.004, 0.007), bevel=0.001)
    bz = (belt_lo + belt_hi) / 2
    for i in range(4):
        x = -0.06 + i * 0.04
        y = _front(info, Vector((x, -0.3, bz))) - 0.034
        k.box("Hips", G, (x, y, bz), (0.026, 0.008, 0.035), bevel=0.002)
    for sg in (1, -1):
        p = Vector((0.15 * sg, -0.02, bz))
        k.box("Hips", W, (p.x, p.y - 0.06, bz - 0.005), (0.05, 0.05, 0.06), rot=(0, 0, math.radians(25 * sg)), bevel=0.008, smooth=True)
    k.cyl("Hips", "metal_silver", (-0.11, 0.14, bz), (0.02, 0.15, bz), 0.024, verts=24)
    k.cyl("Hips", "metal_black", (-0.05, 0.155, bz), (-0.03, 0.157, bz), 0.026, verts=24)
    # шея: уплотнитель
    nz = info_neck_z(info)
    k.cyl("Neck", "rubber", (0, Vector(S["Neck"][0]).y, nz - 0.04), (0, Vector(S["Neck"][0]).y, nz + 0.06), 0.06, 0.056, verts=28)

    # --- шлем из v1, подогнанный под голову
    hc, hs = head if head else A.head_box(info)
    start = len(kit.parts)
    if v.get("helmet", "storm") == "storm":
        T1.helmet(kit)
        ref_c, ref_size = Vector((0, -0.01, 1.70)), 0.215   # центр и высота головы, под которые строился шлем
    else:
        clone_helmet(kit)
        ref_c, ref_size = Vector((0, -0.01, 1.70)), 0.215
    s = hs.z / ref_size * 0.9      # габарит головы MH включает шею/подбородок — шлем не должен быть «бочкой»
    M = Matrix.Translation(hc + Vector((0, -0.004, 0.012))) @ Matrix.Scale(s, 4) @ Matrix.Translation(-ref_c)
    A.transform_parts(kit, start, M)
    for i in range(start, len(kit.parts)):
        kit.parts[i] = (kit.parts[i][0], "Head")


def _front(info, p):
    """y передней поверхности тела в точке (x, z) — для посадки деталей."""
    hit = None
    for c, i, d in info.kd.find_range(Vector((p.x, p.y, p.z)), 0.25):
        if abs(c.x - p.x) < 0.02 and abs(c.z - p.z) < 0.02 and info.no[i].y < -0.3:
            if hit is None or c.y < hit:
                hit = c.y
    return (hit if hit is not None else p.y) - 0.022


def clone_helmet(kit):
    """Шлем клона (Phase II): купол, Т-образный визор, «гребень», антенный блок."""
    cz = 1.70

    def deform(co, u):
        x, y, z = co
        front = K.smooth01(-u.y * 1.5)
        lower = K.smooth01((0.1 - u.z) * 1.3)
        y -= 0.02 * front * lower
        if u.y < 0:
            y = max(y, -0.152)
        if u.z < -0.3:
            f = (-0.3 - u.z) / 0.7
            x *= 1.0 + 0.12 * f
        z = max(z, -0.13)
        return (x, y, z)
    W = "armor"
    h = kit.sphere("Head", W, (0, -0.005, cz), 1.0, scale=(0.128, 0.146, 0.146), seg=56, deform=deform, subsurf=1)
    F = lambda pts: [(x, -0.5, z) for x, z in pts]
    # Т-визор: горизонталь + вертикаль
    kit.decal("Head", "lens", h, F([(-0.085, cz + 0.035), (0.085, cz + 0.035), (0.08, cz + 0.008), (-0.08, cz + 0.008)]), n=10, thick=0.006, lift=0.0)
    kit.decal("Head", "lens", h, F([(-0.017, cz + 0.012), (0.017, cz + 0.012), (0.014, cz - 0.075), (-0.014, cz - 0.075)]), n=8, thick=0.006, lift=0.0)
    # гребень по центру и щёки
    kit.decal("Head", W, h, [(-0.008, -0.12, 2.0), (0.008, -0.12, 2.0), (0.008, 0.1, 2.0), (-0.008, 0.1, 2.0)],
              direction=(0, 0, -1), n=10, thick=0.01, lift=0.004)
    for sg in (1, -1):
        kit.decal("Head", "blue_grey", h, F([(0.03 * sg, cz - 0.03), (0.09 * sg, cz - 0.02), (0.085 * sg, cz - 0.09), (0.035 * sg, cz - 0.1)]),
                  n=6, thick=0.004, lift=0.001)
        kit.cyl("Head", W, (0.128 * sg, -0.01, cz - 0.01), (0.14 * sg, -0.01, cz - 0.01), 0.035, 0.03, verts=24)
    for i in range(6):
        x = -0.02 + i * 0.008
        kit.decal("Head", "black_gloss", h, F([(x - 0.0025, cz - 0.085), (x + 0.0025, cz - 0.085), (x + 0.0025, cz - 0.11), (x - 0.0025, cz - 0.11)]),
                  n=2, thick=0.003, lift=0.002)
    kit.box("Head", "grey", (0.115, 0.02, cz + 0.06), (0.02, 0.05, 0.03), bevel=0.004)
