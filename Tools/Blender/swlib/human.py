"""Тело человека из MakeHuman: меш с UV, веса игрового скелета, глаза. Положения суставов — для скелета."""

import bmesh
import bpy
import numpy as np
from mathutils import Vector

from . import mh


class Body:
    """Результат: obj — меш тела (Blender-координаты, A-поза), J — суставы MH, ends — концы костей MH,
    v_mh — вершины в пространстве MH, remap — индекс MH -> индекс в меше."""

    def __init__(self, obj, v_mh, remap, xf):
        self.obj = obj
        self.v_mh = v_mh
        self.remap = remap
        self.xf = xf          # (сдвиг z, масштаб): Blender-координаты MH -> пространство рига
        self.J = {k: self.tr(v) for k, v in mh.joints(v_mh).items()}
        self.ends = {k: (self.tr(a), self.tr(b)) for k, (a, b) in mh.bone_ends(v_mh).items()}

    def landmarks(self):
        """Ориентиры лица (в пространстве рига)."""
        E = self.ends
        eye = (Vector(E["eye.L"][0]) + Vector(E["eye.R"][0])) / 2
        L = type("L", (), {})()
        L.eye_z, L.eye_y = eye.z, eye.y
        L.nose_z, L.nose_y = E["special03"][1][2], E["special03"][1][1]
        L.mouth_z = E["oris05"][1][2]
        L.lip_low_z = E["oris01"][0][2]
        L.chin_z, L.chin_y = E["jaw"][1][2], E["jaw"][1][1]
        L.top_z = E["head"][1][2]
        L.head_y = E["head"][0][1]
        return L

    def tr(self, v):
        dz, s = self.xf
        return np.array([v[0] * s, v[1] * s, (v[2] - dz) * s])

    def bone(self, mh_name):
        return Vector(self.ends[mh_name][0]), Vector(self.ends[mh_name][1])


def build(name, targets, material, groups=("body",), smooth=True, height=1.83):
    """Тело в пространстве рига: стопы на z=0, рост = height (по умолчанию базовые 1.83 м)."""
    v_mh = mh.shaped(targets)
    _, uvs, G = mh.base()
    faces = [f for g in groups for f in G[g]]
    used = sorted({vi for f in faces for vi, _ in f})
    remap = {old: i for i, old in enumerate(used)}
    vb = mh.to_blender(v_mh[used])
    minz, maxz = vb[:, 2].min(), vb[:, 2].max()
    s = height / (maxz - minz)
    xf = (minz, s)
    vb = np.stack([vb[:, 0] * s, vb[:, 1] * s, (vb[:, 2] - minz) * s], axis=1)
    me = bpy.data.meshes.new(name)
    me.from_pydata(vb.tolist(), [], [[remap[vi] for vi, _ in f] for f in faces])
    uvl = me.uv_layers.new(name="UVMap")
    li = 0
    for f in faces:
        for _, ti in f:
            uvl.data[li].uv = uvs[ti] if ti >= 0 else (0, 0)
            li += 1
    for p in me.polygons:
        p.use_smooth = smooth
    me.materials.append(material)
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    return Body(obj, v_mh, remap, xf)


def skin_weights(body, obj=None):
    """Назначить веса игровых костей мешу тела (группы вершин)."""
    obj = obj or body.obj
    for bone, d in mh.game_weights().items():
        vg = obj.vertex_groups.get(bone) or obj.vertex_groups.new(name=bone)
        by_w = {}
        for vi, w in d.items():
            j = body.remap.get(vi)
            if j is not None:
                by_w.setdefault(round(w, 4), []).append(j)
        for w, idx in by_w.items():
            vg.add(idx, w, "ADD")
    # нормализация
    me = obj.data
    for v in me.vertices:
        s = sum(g.weight for g in v.groups)
        if s > 1e-6:
            for g in v.groups:
                g.weight /= s


def eyes(body, material_iris, material_white=None, name="Eyes"):
    """Глаза MakeHuman (высокополигональные), веса — на кости Eye.L/Eye.R."""
    pv, pf, puv = mh.proxy("eyes/high-poly/high-poly.mhclo", body.v_mh)
    pv = np.array([body.tr(v) for v in pv])
    me = bpy.data.meshes.new(name)
    me.from_pydata(pv.tolist(), [], [[a for a, _ in f] for f in pf])
    if puv is not None:
        uvl = me.uv_layers.new(name="UVMap")
        li = 0
        for f in pf:
            for _, t in f:
                uvl.data[li].uv = puv[t] if t >= 0 else (0, 0)
                li += 1
    for p in me.polygons:
        p.use_smooth = True
    me.materials.append(material_iris)
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    # по стороне
    gl = obj.vertex_groups.new(name="Eye.L")
    gr = obj.vertex_groups.new(name="Eye.R")
    gl.add([v.index for v in me.vertices if v.co.x > 0], 1.0, "REPLACE")
    gr.add([v.index for v in me.vertices if v.co.x <= 0], 1.0, "REPLACE")
    return obj
