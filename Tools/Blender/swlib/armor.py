"""
Детали одежды и брони, подогнанные по телу MakeHuman.

shell(): берём грани тела в нужной области, срезаем ровными плоскостями, сглаживаем (броня не повторяет
каждую мышцу), отодвигаем по нормали и придаём толщину. Веса скелета переносятся с ближайших вершин тела,
поэтому пластины двигаются вместе с телом (или жёстко с одной костью — rigid=...).
"""

import math

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.kdtree import KDTree


class BodyInfo:
    """Быстрый доступ к телу: KD-дерево, веса по вершинам, доминирующая кость."""

    def __init__(self, body_obj):
        self.obj = body_obj
        me = body_obj.data
        self.co = [v.co.copy() for v in me.vertices]
        self.no = [v.normal.copy() for v in me.vertices]
        self.kd = KDTree(len(self.co))
        for i, c in enumerate(self.co):
            self.kd.insert(c, i)
        self.kd.balance()
        names = {g.index: g.name for g in body_obj.vertex_groups}
        self.w = []
        self.dom = []
        for v in me.vertices:
            d = {names[g.group]: g.weight for g in v.groups if g.weight > 1e-4}
            self.w.append(d)
            self.dom.append(max(d, key=d.get) if d else "Hips")

    def weights_fn(self, k=4, rigid=None):
        """Функция весов для kit.bind: усреднение весов k ближайших вершин тела."""
        if rigid:
            return lambda co: {rigid: 1.0}

        def fn(co):
            acc = {}
            tot = 0.0
            for (c, i, d) in self.kd.find_n(co, k):
                wt = 1.0 / (d + 1e-3)
                tot += wt
                for b, w in self.w[i].items():
                    acc[b] = acc.get(b, 0.0) + w * wt
            return {b: w / tot for b, w in acc.items()}
        return fn

    def bone_axis(self, rig_S, bone):
        h, t, _, _ = rig_S[bone]
        return Vector(h), Vector(t)


def shell(kit, info, mat, face_ok, cuts=(), offset=0.012, thick=0.006, smooth=6, smooth_f=0.5, subsurf=0,
          bevel=0.0025, rigid=None, name="shell", inflate=None):
    """face_ok(центр, нормаль, доминирующая кость) -> bool. cuts: [(точка, нормаль)] — оставляем сторону,
    куда смотрит нормаль. inflate(co) -> добавочный отступ (например, раструб)."""
    me = info.obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    bm.faces.ensure_lookup_table()
    keep = set()
    for f in bm.faces:
        c = f.calc_center_median()
        doms = [info.dom[v.index] for v in f.verts]
        dom = max(set(doms), key=doms.count)
        if face_ok(c, f.normal, dom):
            keep.add(f.index)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.index not in keep], context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    # нормали ДО среза — от тела
    for p, n in cuts:
        p, n = Vector(p), Vector(n).normalized()
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=p, plane_no=n, clear_inner=True)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    if not bm.faces:
        bm.free()
        return None
    # отступ по нормалям тела (ближайшая вершина), затем сглаживание формы
    for v in bm.verts:
        c, i, d = info.kd.find(v.co)
        o = offset + (inflate(v.co) if inflate else 0.0)
        v.co = v.co + info.no[i] * o
    for _ in range(smooth):
        bmesh.ops.smooth_vert(bm, verts=[v for v in bm.verts if not v.is_boundary], factor=smooth_f,
                              use_axis_x=True, use_axis_y=True, use_axis_z=True)
        bmesh.ops.smooth_vert(bm, verts=[v for v in bm.verts if v.is_boundary], factor=smooth_f * 0.5,
                              use_axis_x=True, use_axis_y=True, use_axis_z=True)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    out = bpy.data.meshes.new(name)
    bm.to_mesh(out)
    bm.free()
    for p in out.polygons:
        p.use_smooth = True
    o = bpy.data.objects.new(name, out)
    bpy.context.scene.collection.objects.link(o)
    # нормали наружу: сравниваем с нормалями тела
    flip = 0
    for v in out.vertices[: min(60, len(out.vertices))]:
        c, i, d = info.kd.find(v.co)
        flip += 1 if v.normal.dot(info.no[i]) < 0 else -1
    if flip > 0:
        for p in out.polygons:
            p.flip()
    sol = o.modifiers.new("Solid", "SOLIDIFY")
    sol.thickness = thick
    sol.offset = -1.0
    sol.use_even_offset = False
    if bevel:
        bv = o.modifiers.new("Bevel", "BEVEL")
        bv.width = bevel
        bv.segments = 2
        bv.limit_method = "ANGLE"
        bv.angle_limit = math.radians(50)
    obj = kit._finish(o, info.weights_fn(rigid=rigid), mat, subsurf=subsurf)
    return obj


# ----------------------------------------------------------------------------- помощники регионов

def along(S, bone, co):
    """Параметр 0..1 положения точки вдоль кости и расстояние до оси."""
    h, t = Vector(S[bone][0]), Vector(S[bone][1])
    d = t - h
    L = d.length
    u = (co - h).dot(d) / (L * L)
    r = (co - (h + d * u)).length
    return u, r


def limb_tube(kit, info, S, bone, mat, t0, t1, offset=0.012, thick=0.006, extra_bones=(), smooth=8, rigid=None,
              side_cut=None, inflate=None, name="tube"):
    """Трубчатая накладка на сегмент конечности между долями t0..t1 длины кости."""
    h, t = Vector(S[bone][0]), Vector(S[bone][1])
    d = (t - h).normalized()
    bones = (bone,) + tuple(extra_bones)

    def ok(c, n, dom):
        if dom not in bones:
            return False
        u, r = along(S, bone, c)
        return t0 - 0.12 <= u <= t1 + 0.12
    cuts = [(h + (t - h) * t0, d), (h + (t - h) * t1, -d)]
    if side_cut:
        cuts.append(side_cut)
    return shell(kit, info, mat, ok, cuts, offset=offset, thick=thick, smooth=smooth, rigid=rigid,
                 inflate=inflate, name=name)


def head_box(info):
    """Габариты головы (вершины с доминирующей костью Head)."""
    pts = [c for c, d in zip(info.co, info.dom) if d in ("Head", "Jaw", "Eye.L", "Eye.R")]
    a = np.array([[p.x, p.y, p.z] for p in pts])
    lo, hi = a.min(axis=0), a.max(axis=0)
    return Vector(((lo + hi) / 2).tolist()), Vector((hi - lo).tolist())


def transform_parts(kit, start, M):
    """Перенести детали kit.parts[start:] матрицей M (для шлема, собранного в своих координатах)."""
    bpy.context.view_layer.update()
    for obj, _ in kit.parts[start:]:
        obj.matrix_world = M @ obj.matrix_world
