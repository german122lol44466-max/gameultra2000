"""Геометрия из примитивов и процедурные материалы. Каждая деталь привязана к кости
(жёстко) или к функции весов (ткань: плащ, полы робы)."""

import math

import bmesh
import bpy
from mathutils import Matrix, Vector


# =========================================================================== материалы

def tag(m, color, rough, metallic=0.0, emission=None, strength=0.0, alpha=1.0):
    """Параметры материала для движка (Unity/веб): цвет, гладкость, металл, свечение."""
    m["sw"] = {"color": [round(c, 4) for c in color[:3]], "smoothness": round(1.0 - rough, 3), "metallic": metallic,
               "emission": [round(c * strength, 3) for c in emission[:3]] if emission else [0, 0, 0], "alpha": alpha}
    return m


def _nodes(mat):
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return nt, nt.nodes, nt.links, bsdf, nt.nodes.new("ShaderNodeTexCoord")


def _noise(n, l, coord, scale, detail=6.0, rough=0.55, stretch=None):
    t = n.new("ShaderNodeTexNoise")
    t.inputs["Scale"].default_value = scale
    t.inputs["Detail"].default_value = detail
    t.inputs["Roughness"].default_value = rough
    src = coord.outputs["Object"]
    if stretch is not None:
        m = n.new("ShaderNodeMapping")
        m.inputs["Scale"].default_value = stretch
        l.new(src, m.inputs["Vector"])
        src = m.outputs[0]
    l.new(src, t.inputs["Vector"])
    return t


def _range(n, l, src, a, b, c=0.0, d=1.0):
    r = n.new("ShaderNodeMapRange")
    r.inputs["From Min"].default_value = a
    r.inputs["From Max"].default_value = b
    r.inputs["To Min"].default_value = c
    r.inputs["To Max"].default_value = d
    l.new(src, r.inputs["Value"])
    return r.outputs[0]


def _mix(n, l, fac, a, b):
    m = n.new("ShaderNodeMix")
    m.data_type = "RGBA"
    if isinstance(fac, (int, float)):
        m.inputs["Factor"].default_value = fac
    else:
        l.new(fac, m.inputs["Factor"])
    for sock, v in (("A_Color", a), ("B_Color", b)):
        inp = next(i for i in m.inputs if i.identifier == sock)
        if isinstance(v, tuple):
            inp.default_value = (*v[:3], 1)
        else:
            l.new(v, inp)
    return next(o for o in m.outputs if o.identifier == "Result_Color")


def _bump(n, l, bsdf, height, strength):
    b = n.new("ShaderNodeBump")
    b.inputs["Strength"].default_value = strength
    b.inputs["Distance"].default_value = 0.01
    l.new(height, b.inputs["Height"])
    l.new(b.outputs["Normal"], bsdf.inputs["Normal"])


def mat_plastic(name, color, rough=0.28, dirt=0.25, scuff=0.35, coat=0.0):
    """Глянцевый пластик брони: лёгкие пятна, потёртости, пыль снизу."""
    m = bpy.data.materials.new(name)
    nt, n, l, bsdf, coord = _nodes(m)
    tone = _noise(n, l, coord, 4.0)
    base = _mix(n, l, _range(n, l, tone.outputs["Fac"], 0.3, 0.7), tuple(c * 0.93 for c in color), color)
    sc = _noise(n, l, coord, 18.0, detail=10, rough=0.7)
    scm = _range(n, l, sc.outputs["Fac"], 0.66, 0.75, 0.0, scuff)
    scuffed = _mix(n, l, scm, base, tuple(c * 0.55 + 0.02 for c in color))
    sep = n.new("ShaderNodeSeparateXYZ")
    l.new(coord.outputs["Object"], sep.inputs[0])
    low = _range(n, l, sep.outputs["Z"], 0.35, 0.0, 0.0, dirt)
    col = _mix(n, l, low, scuffed, (0.16, 0.14, 0.12))
    l.new(col, bsdf.inputs["Base Color"])
    rr = _range(n, l, sc.outputs["Fac"], 0.6, 0.8, rough, min(1.0, rough + 0.25))
    l.new(rr, bsdf.inputs["Roughness"])
    bsdf.inputs["Coat Weight"].default_value = coat
    bsdf.inputs["Coat Roughness"].default_value = 0.08
    tag(m, color, rough)
    return m


def mat_cloth(name, color, rough=0.85, weave=120.0, dirt=0.2):
    m = bpy.data.materials.new(name)
    nt, n, l, bsdf, coord = _nodes(m)
    wave = n.new("ShaderNodeTexWave")
    wave.inputs["Scale"].default_value = weave
    wave.inputs["Distortion"].default_value = 2.0
    l.new(coord.outputs["Object"], wave.inputs["Vector"])
    tone = _noise(n, l, coord, 6.0, detail=3)
    a = _mix(n, l, _range(n, l, wave.outputs["Fac"], 0, 1), tuple(c * 0.8 for c in color), color)
    b = _mix(n, l, _range(n, l, tone.outputs["Fac"], 0.35, 0.7), tuple(c * 0.85 for c in color), a)
    sep = n.new("ShaderNodeSeparateXYZ")
    l.new(coord.outputs["Object"], sep.inputs[0])
    low = _range(n, l, sep.outputs["Z"], 0.3, 0.0, 0.0, dirt)
    l.new(_mix(n, l, low, b, (0.09, 0.08, 0.07)), bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Sheen Weight"].default_value = 0.3 if max(color) > 0.1 else 0.0
    _bump(n, l, bsdf, wave.outputs["Fac"], 0.08)
    tag(m, color, rough)
    return m


def mat_leather(name, color, rough=0.42):
    m = bpy.data.materials.new(name)
    nt, n, l, bsdf, coord = _nodes(m)
    t = _noise(n, l, coord, 60.0, detail=8, rough=0.6)
    l.new(_mix(n, l, _range(n, l, t.outputs["Fac"], 0.4, 0.6), tuple(c * 0.8 for c in color), color), bsdf.inputs["Base Color"])
    l.new(_range(n, l, t.outputs["Fac"], 0.3, 0.7, rough - 0.12, rough + 0.12), bsdf.inputs["Roughness"])
    _bump(n, l, bsdf, t.outputs["Fac"], 0.05)
    tag(m, color, rough)
    return m


def mat_metal(name, color, rough=0.3, scratch=0.5):
    m = bpy.data.materials.new(name)
    nt, n, l, bsdf, coord = _nodes(m)
    sc = _noise(n, l, coord, 3.0, detail=2, stretch=(80.0, 3.0, 80.0))
    scm = _range(n, l, sc.outputs["Fac"], 0.62, 0.7, 0.0, scratch)
    l.new(_mix(n, l, scm, color, tuple(min(1, c * 1.6 + 0.08) for c in color)), bsdf.inputs["Base Color"])
    l.new(_range(n, l, sc.outputs["Fac"], 0.5, 0.75, rough, rough * 0.4), bsdf.inputs["Roughness"])
    bsdf.inputs["Metallic"].default_value = 1.0
    tag(m, color, rough, 1.0)
    return m


def mat_flat(name, color, rough=0.5, metallic=0.0, emission=None, strength=0.0, coat=0.0):
    m = bpy.data.materials.new(name)
    nt, n, l, bsdf, coord = _nodes(m)
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Coat Weight"].default_value = coat
    if emission:
        bsdf.inputs["Emission Color"].default_value = (*emission, 1)
        bsdf.inputs["Emission Strength"].default_value = strength
    tag(m, color, rough, metallic, emission, strength)
    return m


def mat_skin(name, color, wrinkles=0.3, rough=0.48, pores=0.08, redness=0.25):
    """Кожа: подповерхностное рассеяние, поры, лёгкая неравномерность тона."""
    m = bpy.data.materials.new(name)
    nt, n, l, bsdf, coord = _nodes(m)
    t = _noise(n, l, coord, 18.0, detail=6, rough=0.6)
    w = _noise(n, l, coord, 22.0, detail=4, stretch=(1.5, 1.5, 5.0))
    p = _noise(n, l, coord, 700.0, detail=2, rough=0.5)
    red = tuple(min(1.0, c * (1.0 + redness * (1.6 if i == 0 else -0.3))) for i, c in enumerate(color))
    base = _mix(n, l, _range(n, l, t.outputs["Fac"], 0.35, 0.7), tuple(c * 0.88 for c in color), color)
    blot = _noise(n, l, coord, 6.0, detail=3)
    l.new(_mix(n, l, _range(n, l, blot.outputs["Fac"], 0.5, 0.75, 0.0, 0.5), base, red), bsdf.inputs["Base Color"])
    l.new(_range(n, l, p.outputs["Fac"], 0.3, 0.7, rough - 0.08, rough + 0.12), bsdf.inputs["Roughness"])
    bsdf.inputs["Subsurface Weight"].default_value = 0.35
    bsdf.inputs["Subsurface Radius"].default_value = (1.0, 0.35, 0.2)
    bsdf.inputs["Subsurface Scale"].default_value = 0.012
    bsdf.inputs["Specular IOR Level"].default_value = 0.45
    add = n.new("ShaderNodeMath")
    add.operation = "ADD"
    mw = n.new("ShaderNodeMath")
    mw.operation = "MULTIPLY"
    mw.inputs[1].default_value = wrinkles
    l.new(w.outputs["Fac"], mw.inputs[0])
    mp = n.new("ShaderNodeMath")
    mp.operation = "MULTIPLY"
    mp.inputs[1].default_value = pores * 3
    l.new(p.outputs["Fac"], mp.inputs[0])
    l.new(mw.outputs[0], add.inputs[0])
    l.new(mp.outputs[0], add.inputs[1])
    _bump(n, l, bsdf, add.outputs[0], 0.12)
    tag(m, color, rough)
    return m


def mat_hair(name, color, rough=0.38):
    """Волосы: тонкие пряди (растянутый шум), блики, вариация оттенка."""
    m = bpy.data.materials.new(name)
    nt, n, l, bsdf, coord = _nodes(m)
    st = _noise(n, l, coord, 4.0, detail=8, rough=0.7, stretch=(90.0, 90.0, 4.0))
    l.new(_mix(n, l, _range(n, l, st.outputs["Fac"], 0.3, 0.7), tuple(c * 0.55 for c in color), tuple(min(1, c * 1.35) for c in color)),
          bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Coat Weight"].default_value = 0.2
    bsdf.inputs["Sheen Weight"].default_value = 0.4
    _bump(n, l, bsdf, st.outputs["Fac"], 0.45)
    tag(m, color, rough)
    return m


def mat_blade(name, color):
    """Клинок светового меча: белое ядро + цветное свечение по краю (френель)."""
    m = bpy.data.materials.new(name)
    nt, n, l, bsdf, coord = _nodes(m)
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    lw = n.new("ShaderNodeLayerWeight")
    lw.inputs["Blend"].default_value = 0.35
    pw = n.new("ShaderNodeMath")
    pw.operation = "POWER"
    pw.inputs[1].default_value = 0.25
    l.new(lw.outputs["Facing"], pw.inputs[0])
    col = _mix(n, l, pw.outputs[0], (1.0, 0.9, 0.86), color)
    l.new(col, bsdf.inputs["Emission Color"])
    bsdf.inputs["Emission Strength"].default_value = 18.0
    m["emissive"] = list(color)
    tag(m, (1.0, 1.0, 1.0), 0.0, 0.0, color, 6.0)
    return m


def mat_glow(name, color, strength=6.0):
    """Мягкий ореол вокруг клинка (только для рендеров)."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs["Color"].default_value = (*color, 1)
    lw = nt.nodes.new("ShaderNodeLayerWeight")
    lw.inputs["Blend"].default_value = 0.6
    pw = nt.nodes.new("ShaderNodeMath")
    pw.operation = "POWER"
    pw.inputs[1].default_value = 3.0
    inv = nt.nodes.new("ShaderNodeMath")
    inv.operation = "SUBTRACT"
    inv.inputs[0].default_value = 1.0
    nt.links.new(lw.outputs["Facing"], inv.inputs[1])
    nt.links.new(inv.outputs[0], pw.inputs[0])
    st = nt.nodes.new("ShaderNodeMath")
    st.operation = "MULTIPLY"
    st.inputs[1].default_value = strength
    nt.links.new(pw.outputs[0], st.inputs[0])
    nt.links.new(st.outputs[0], em.inputs["Strength"])
    tr = nt.nodes.new("ShaderNodeBsdfTransparent")
    add = nt.nodes.new("ShaderNodeAddShader")
    nt.links.new(em.outputs[0], add.inputs[0])
    nt.links.new(tr.outputs[0], add.inputs[1])
    nt.links.new(add.outputs[0], out.inputs["Surface"])
    m.blend_method = "BLEND"
    tag(m, color, 1.0, 0.0, color, 2.0, alpha=0.3)
    return m


# =========================================================================== геометрия

def smooth01(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


class Kit:
    def __init__(self, mats):
        self.M = mats
        self.parts = []  # (obj, bone | функция весов)
        self.xform = None  # матрица для деталей оружия (пространство оружия -> арматура)

    def _finish(self, obj, bone, mat, bevel=0.0, smooth=True, subsurf=0, sharp=None):
        obj.data.materials.clear()
        obj.data.materials.append(self.M[mat])
        if bevel > 0:
            b = obj.modifiers.new("Bevel", "BEVEL")
            b.width = bevel
            b.segments = 2
            b.limit_method = "ANGLE"
            b.angle_limit = math.radians(35)
        if subsurf:
            s = obj.modifiers.new("Sub", "SUBSURF")
            s.levels = subsurf
            s.render_levels = subsurf
        if smooth:
            for p in obj.data.polygons:
                p.use_smooth = True
        if self.xform is not None:
            obj.matrix_world = self.xform @ obj.matrix_basis
        self.parts.append((obj, bone))
        return obj

    def _new(self, name, bm):
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        o = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(o)
        return o

    # --- примитивы
    def sphere(self, bone, mat, loc, r, scale=(1, 1, 1), rot=(0, 0, 0), seg=24, deform=None, subsurf=0,
               keep=None, matfn=None, solid=0.0):
        """Деформируемая сфера. keep(unit) — оставить вершину; matfn(unit_центра_грани) — имя материала грани."""
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=max(8, seg // 2), radius=1.0)
        unit = {v: v.co.copy() for v in bm.verts}
        if keep:
            bmesh.ops.delete(bm, geom=[v for v in bm.verts if not keep(unit[v])], context="VERTS")
        names = []
        if matfn:
            for f in bm.faces:
                c = sum((unit[v] for v in f.verts), Vector()) / len(f.verts)
                nm = matfn(c)
                if nm not in names:
                    names.append(nm)
                f.material_index = names.index(nm)
        sc = Vector(scale) * r
        for v in bm.verts:
            co = Vector((v.co.x * sc.x, v.co.y * sc.y, v.co.z * sc.z))
            if deform:
                co = Vector(deform(co, unit[v]))
            v.co = co
        o = self._new("sphere", bm)
        idx = [p.material_index for p in o.data.polygons]
        o.rotation_euler = rot
        o.location = loc
        if solid:
            so = o.modifiers.new("Solid", "SOLIDIFY")
            so.thickness = solid
        o = self._finish(o, bone, names[0] if names else mat, subsurf=subsurf)
        if names:
            o.data.materials.clear()
            for nm in names:
                o.data.materials.append(self.M[nm])
            o.data.polygons.foreach_set("material_index", idx)
        return o

    def box(self, bone, mat, loc, size, rot=(0, 0, 0), bevel=0.006, smooth=False):
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0)
        for v in bm.verts:
            v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
        o = self._new("box", bm)
        o.rotation_euler = rot
        o.location = loc
        o = self._finish(o, bone, mat, bevel=bevel, smooth=smooth)
        if smooth:
            o.modifiers.new("WN", "WEIGHTED_NORMAL")
        return o

    def cyl(self, bone, mat, a, b, r1, r2=None, verts=24, bevel=0.0, caps=True, smooth=True):
        """Цилиндр/конус от точки a к точке b."""
        a, b = Vector(a), Vector(b)
        d = b - a
        r2 = r1 if r2 is None else r2
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=caps, segments=verts, radius1=r1, radius2=r2, depth=d.length)
        o = self._new("cyl", bm)
        o.rotation_mode = "QUATERNION"
        o.rotation_quaternion = d.to_track_quat("Z", "Y")
        o.location = (a + b) / 2
        return self._finish(o, bone, mat, bevel=bevel, smooth=smooth)

    def torus(self, bone, mat, loc, R, r, axis=(0, 0, 1), scale=(1, 1, 1)):
        bm = bmesh.new()
        segs, rs = 32, 10
        rings = []
        for i in range(segs):
            u = 2 * math.pi * i / segs
            ring = []
            for j in range(rs):
                v = 2 * math.pi * j / rs
                x = (R + r * math.cos(v)) * math.cos(u) * scale[0]
                y = (R + r * math.cos(v)) * math.sin(u) * scale[1]
                z = r * math.sin(v) * scale[2]
                ring.append(bm.verts.new((x, y, z)))
            rings.append(ring)
        for i in range(segs):
            a, b = rings[i], rings[(i + 1) % segs]
            for j in range(rs):
                k = (j + 1) % rs
                bm.faces.new((a[j], b[j], b[k], a[k]))
        o = self._new("torus", bm)
        o.rotation_mode = "QUATERNION"
        o.rotation_quaternion = Vector(axis).to_track_quat("Z", "Y")
        o.location = loc
        return self._finish(o, bone, mat)

    def limb(self, bone, mat, a, b, r1, r2, flat=1.0, verts=20, bulge=0.0):
        """Сегмент конечности: скруглённый конус с утолщением посередине."""
        a, b = Vector(a), Vector(b)
        L = (b - a).length
        st = []
        for i in range(9):
            t = i / 8
            r = r1 + (r2 - r1) * t + bulge * math.sin(math.pi * t)
            cap = math.sqrt(max(0.0, 1 - (abs(t - 0.5) * 2) ** 8))
            st.append((t * L, r * (0.55 + 0.45 * cap)))
        return self.lathe(bone, mat, a, b, st, verts=verts, flat=flat)

    def lathe(self, bone, mat, a, b, profile, verts=24, flat=1.0, caps=True, subsurf=0, twist_axis=None):
        """Тело вращения вдоль a->b. profile: [(расстояние от a, радиус)]."""
        a, b = Vector(a), Vector(b)
        bm = bmesh.new()
        rings = []
        for (z, r) in profile:
            ring = []
            for i in range(verts):
                t = 2 * math.pi * i / verts
                ring.append(bm.verts.new((math.cos(t) * r, math.sin(t) * r * flat, z)))
            rings.append(ring)
        for ra, rb in zip(rings, rings[1:]):
            for i in range(verts):
                j = (i + 1) % verts
                bm.faces.new((ra[i], ra[j], rb[j], rb[i]))
        if caps:
            if profile[0][1] > 1e-5:
                bm.faces.new(list(reversed(rings[0])))
            if profile[-1][1] > 1e-5:
                bm.faces.new(rings[-1])
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        o = self._new("lathe", bm)
        d = b - a
        o.rotation_mode = "QUATERNION"
        o.rotation_quaternion = d.to_track_quat("Z", twist_axis or "Y")
        o.location = a
        return self._finish(o, bone, mat, subsurf=subsurf)

    def loft(self, bone, mat, stations, ring=32, caps=(True, True), subsurf=1, open_back=None):
        """Сечения-суперэллипсы вдоль Z: [(z, полуширина, полуглубина, степень, сдвиг_y[, сдвиг_x])]."""
        bm = bmesh.new()
        rings = []
        for st in stations:
            z, w, d, p = st[:4]
            cy = st[4] if len(st) > 4 else 0.0
            cx = st[5] if len(st) > 5 else 0.0
            r = []
            for i in range(ring):
                t = 2 * math.pi * i / ring
                c, s = math.cos(t), math.sin(t)
                x = math.copysign(abs(c) ** (2 / p), c) * w + cx
                y = math.copysign(abs(s) ** (2 / p), s) * d + cy
                r.append(bm.verts.new((x, y, z)))
            rings.append(r)
        for a, b in zip(rings, rings[1:]):
            for i in range(ring):
                j = (i + 1) % ring
                bm.faces.new((a[i], a[j], b[j], b[i]))
        if caps[0]:
            bm.faces.new(list(reversed(rings[0])))
        if caps[1]:
            bm.faces.new(rings[-1])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        o = self._new("loft", bm)
        return self._finish(o, bone, mat, subsurf=subsurf)

    def sheet(self, bone, mat, rows, thickness=0.006, subsurf=1):
        """Полотно (плащ, полы): rows — список рядов точек сверху вниз. Утолщается solidify."""
        bm = bmesh.new()
        vs = [[bm.verts.new(p) for p in row] for row in rows]
        for a, b in zip(vs, vs[1:]):
            for i in range(len(a) - 1):
                bm.faces.new((a[i], a[i + 1], b[i + 1], b[i]))
        o = self._new("sheet", bm)
        s = o.modifiers.new("Solid", "SOLIDIFY")
        s.thickness = thickness
        s.offset = 0.0
        return self._finish(o, bone, mat, subsurf=subsurf)

    def decal(self, bone, mat, surf, corners, direction=(0, 1, 0), lift=0.0018, thick=0.003, n=8):
        """Накладка, повторяющая поверхность surf. corners — 4 точки-начала лучей (по кругу),
        лучи идут по direction; сетка n x n проецируется на поверхность и утолщается."""
        cs = [Vector(c) for c in corners]
        bm = bmesh.new()
        grid = []
        for i in range(n + 1):
            u = i / n
            row = []
            for j in range(n + 1):
                v = j / n
                o = (cs[0] * (1 - u) + cs[1] * u) * (1 - v) + (cs[3] * (1 - u) + cs[2] * u) * v
                hit, nor = surface(surf, o, direction)
                row.append(bm.verts.new(hit + nor * lift))
            grid.append(row)
        for i in range(n):
            for j in range(n):
                bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        o = self._new("decal", bm)
        sol = o.modifiers.new("Solid", "SOLIDIFY")
        sol.thickness = thick
        sol.offset = 0.0
        return self._finish(o, bone, mat, bevel=0.0)

    def text(self, bone, mat, body, loc, size, rot):
        cu = bpy.data.curves.new("txt", "FONT")
        cu.body = body
        cu.size = size
        cu.align_x = "CENTER"
        cu.extrude = 0.001
        t = bpy.data.objects.new("txt", cu)
        bpy.context.scene.collection.objects.link(t)
        t.location = loc
        t.rotation_euler = rot
        bpy.context.view_layer.update()
        me = bpy.data.meshes.new_from_object(t.evaluated_get(bpy.context.evaluated_depsgraph_get()))
        o = bpy.data.objects.new("txtm", me)
        bpy.context.scene.collection.objects.link(o)
        o.matrix_world = t.matrix_world
        bpy.data.objects.remove(t)
        return self._finish(o, bone, mat, smooth=False)


def surface(obj, origin, direction):
    """Точка на поверхности объекта (луч из origin по direction) — для посадки деталей."""
    from mathutils.bvhtree import BVHTree
    mw = obj.matrix_basis if obj.parent is None else obj.matrix_world
    verts = [mw @ v.co for v in obj.data.vertices]
    tree = BVHTree.FromPolygons(verts, [p.vertices[:] for p in obj.data.polygons])
    hit, normal, _, _ = tree.ray_cast(Vector(origin), Vector(direction).normalized())
    if hit is None:
        return Vector(origin), Vector(direction).normalized() * -1
    return hit, normal


def bind(kit, arm_obj, name, mirror_axis=None):
    """Применить модификаторы, раздать веса, объединить детали в один меш со скиннингом."""
    objs = []
    bpy.context.view_layer.update()
    for obj, bone in kit.parts:
        dg = bpy.context.evaluated_depsgraph_get()
        me = bpy.data.meshes.new_from_object(obj.evaluated_get(dg))
        me.transform(obj.matrix_world)
        nobj = bpy.data.objects.new(obj.name, me)
        bpy.context.scene.collection.objects.link(nobj)
        if callable(bone):
            groups = {}
            for v in me.vertices:
                for bn, w in bone(v.co).items():
                    if w > 1e-4:
                        if bn not in groups:
                            groups[bn] = nobj.vertex_groups.new(name=bn)
                        groups[bn].add([v.index], w, "REPLACE")
        else:
            nobj.vertex_groups.new(name=bone).add(list(range(len(me.vertices))), 1.0, "REPLACE")
        objs.append(nobj)
        bpy.data.objects.remove(obj)
    for o in bpy.context.selected_objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    body = bpy.context.active_object
    body.name = name
    body.data.name = name
    body.parent = arm_obj
    mod = body.modifiers.new("Armature", "ARMATURE")
    mod.object = arm_obj
    kit.parts = []
    return body
