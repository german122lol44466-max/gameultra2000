"""
Одежда: облегающие слои (по телу) и свободная ткань, которую симулируем физикой Blender на теле в A-позе —
так получаются настоящие складки на робах, полах туник и плащах.
"""

import math

import bmesh
import bpy
from mathutils import Vector

from . import armor as A
from . import kit as K


def _grid_obj(name, rows):
    """Меш-полотно из рядов точек (сверху вниз). Возвращает объект и индексы верхнего ряда."""
    bm = bmesh.new()
    vs = [[bm.verts.new(p) for p in row] for row in rows]
    closed = (rows[0][0] - rows[0][-1]).length < 1e-6
    for a, b in zip(vs, vs[1:]):
        n = len(a)
        for i in range(n - 1):
            bm.faces.new((a[i], a[i + 1], b[i + 1], b[i]))
    if closed:
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    return o


def simulate(garment, colliders, pin_fn, frames=80, mass=0.3, stiffness=6.0, thickness=0.006, gravity=-9.81,
             shrink=0.0, self_collide=False, wind=None, bending=0.08, pin_idx=None):
    """Симулировать ткань: pin_fn(co)->вес закрепления 0..1. После симуляции форма применяется к мешу."""
    sc = bpy.context.scene
    sc.frame_start, sc.frame_end = 1, frames
    vg = garment.vertex_groups.new(name="pin")
    for v in garment.data.vertices:
        w = (1.0 if v.index in pin_idx else 0.0) if pin_idx is not None else pin_fn(v.co)
        if w > 0:
            vg.add([v.index], w, "REPLACE")
    for c in colliders:
        if not c.modifiers.get("Collision"):
            c.modifiers.new("Collision", "COLLISION")
        c.collision.thickness_outer = thickness
        c.collision.thickness_inner = 0.01
        c.collision.cloth_friction = 6.0
    m = garment.modifiers.new("Cloth", "CLOTH")
    s = m.settings
    s.quality = 8
    s.mass = mass
    s.tension_stiffness = stiffness
    s.compression_stiffness = stiffness
    s.shear_stiffness = stiffness * 0.5
    s.bending_stiffness = bending
    s.air_damping = 1.0
    s.vertex_group_mass = "pin"
    s.shrink_min = shrink
    s.effector_weights.gravity = 1.0
    cs = m.collision_settings
    cs.distance_min = thickness
    cs.use_self_collision = self_collide
    cs.collision_quality = 4
    m.point_cache.frame_start = 1
    m.point_cache.frame_end = frames
    for f in range(1, frames + 1):
        sc.frame_set(f)
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(garment.evaluated_get(dg))
    garment.modifiers.remove(m)
    old = garment.data
    garment.data = me
    bpy.data.meshes.remove(old)
    sc.frame_set(1)
    return garment


def finish_garment(kit, info, garment, mat, thick=0.004, subsurf=0, weights=None, far_bone="Hips", far=0.06):
    """Толщина, сглаживание, веса с тела (дальние от тела точки — к far_bone)."""
    for p in garment.data.polygons:
        p.use_smooth = True
    sol = garment.modifiers.new("Solid", "SOLIDIFY")
    sol.thickness = thick
    sol.offset = 0.0
    base_fn = info.weights_fn(k=6)

    def fn(co):
        if weights:
            return weights(co)
        c, i, d = info.kd.find(co)
        w = base_fn(co)
        if d > far:
            t = min(1.0, (d - far) / 0.15)
            w = {b: x * (1 - t) for b, x in w.items()}
            w[far_bone] = w.get(far_bone, 0.0) + t
        return w
    return kit._finish(garment, fn, mat, subsurf=subsurf)


def skirt(kit, info, S, mat, top_z, bottom_z, r_top=(0.17, 0.12), flare=0.35, split=0.0, frames=90, rows=30, cols=72,
          name="skirt", cy=0.0):
    """Полы робы/туники: конус от талии, закреплён по верхнему краю, свисает на ногах."""
    pts = []
    for i in range(rows + 1):
        t = i / rows
        z = top_z + (bottom_z - top_z) * t
        rx = r_top[0] + (top_z - z) * flare * 0.5
        ry = r_top[1] + (top_z - z) * flare * 0.45
        row = []
        for j in range(cols + 1):
            a = -math.pi / 2 + split + (2 * math.pi - 2 * split) * j / cols
            row.append(Vector((math.cos(a) * rx, math.sin(a) * ry + cy, z)))
        pts.append(row)
    g = _grid_obj(name, pts)
    simulate(g, [info.obj], lambda co: 1.0 if co.z > top_z - 0.015 else 0.0, frames=frames, bending=0.06)
    return finish_garment(kit, info, g, mat)


def shoulder_line(info, S, n=41, lift=0.012):
    """Линия от левого плеча через затылочную часть шеи к правому — по верхней поверхности тела."""
    nk = Vector(S["Neck"][0])
    W = abs(Vector(S["UpperArm.L"][0]).x) + 0.02
    pts = []
    for j in range(n):
        u = -1 + 2 * j / (n - 1)
        x = u * W
        y = nk.y + 0.10 * math.cos(u * math.pi / 2) + 0.02
        best = None
        for c, i, d in info.kd.find_range(Vector((x, y, nk.z - 0.05)), 0.2):
            if abs(c.x - x) < 0.012 and abs(c.y - y) < 0.03 and info.no[i].z > -0.1:
                if best is None or c.z > best.z:
                    best = c
        z = best.z if best is not None else nk.z - 0.06
        pts.append(Vector((x, y, z + lift)))
    # сгладить высоты
    for _ in range(3):
        pts = [pts[0]] + [Vector((p.x, p.y, (a.z + p.z * 2 + b.z) / 4)) for a, p, b in zip(pts, pts[1:], pts[2:])] + [pts[-1]]
    return pts


def cape(kit, info, S, mat, bottom_z, flare=0.25, frames=70, rows=34, name="cape", back_off=0.03):
    """Плащ: верхний край лежит на плечах и вокруг шеи, полотно свободно свисает и ложится складками."""
    top = shoulder_line(info, S)
    top_z = max(p.z for p in top)
    nk = Vector(S["Neck"][0])
    rows_pts = []
    for i in range(rows + 1):
        t = i / rows
        row = []
        for p in top:
            z = p.z + (bottom_z - p.z) * t
            k = K.smooth01(t * 4)                     # ниже плеч полотно уходит за спину (за руки)
            x = p.x * (1 + flare * t * 0.6)
            y_back = nk.y + 0.13 + t * flare * 0.3
            y = p.y * (1 - k) + max(p.y, y_back) * k + back_off * (1 - k)
            row.append(Vector((x, y, z)))
        rows_pts.append(row)
    g = _grid_obj(name, rows_pts)
    first = set(range(len(top)))
    simulate(g, [info.obj], None, frames=frames, mass=0.35, stiffness=7.0, bending=0.15, pin_idx=set(range(len(top))))
    return finish_garment(kit, info, g, mat, far_bone="Spine", far=0.08)


def _near_line(co, top, tol=0.012):
    return min((co - p).length for p in top) < tol


def tight(kit, info, mat, face_ok, cuts=(), offset=0.008, thick=0.003, smooth=4, inflate=None, name="tight", subsurf=1):
    """Облегающий слой (рубашка, штаны, перчатки, сапоги) — оболочка тела."""
    return A.shell(kit, info, mat, face_ok, cuts, offset=offset, thick=thick, smooth=smooth, smooth_f=0.4,
                   subsurf=subsurf, bevel=0.0, inflate=inflate, name=name)
