"""
Модель Source -> объекты Blender: меш с весами, материалы с текстурами (VTF -> PNG), скелет в соглашениях v2
(имена Hips/Spine/.../Hand.R, Y вдоль кости, Z «вперёд»), готовый к риггингу swlib.rig2.Rig.

Система координат: Source (X вперёд, Y влево, Z вверх, дюймы) -> пространство персонажа v2
(X влево, -Y вперёд, Z вверх, метры, рост приводится к базовым 1.83 м).
"""

import math
import os

import bpy
import numpy as np
from mathutils import Matrix, Vector

import srcfmt

INCH = 0.0254
BASE_HEIGHT = 1.83

# ValveBiped -> кости v2
VB = "ValveBiped.Bip01_"
MAP = {
    VB + "Pelvis": "Hips", VB + "Spine": "Spine", VB + "Spine1": "Spine", VB + "Spine2": "Chest", VB + "Spine4": "UpperChest",
    VB + "Neck1": "Neck", VB + "Head1": "Head",
}
for s in ("L", "R"):
    MAP.update({
        VB + f"{s}_Clavicle": f"Shoulder.{s}", VB + f"{s}_UpperArm": f"UpperArm.{s}", VB + f"{s}_Bicep": f"UpperArm.{s}",
        VB + f"{s}_Shoulder": f"UpperArm.{s}", VB + f"{s}_Elbow": f"LowerArm.{s}", VB + f"{s}_Forearm": f"LowerArm.{s}",
        VB + f"{s}_Ulna": f"LowerArm.{s}", VB + f"{s}_Wrist": f"LowerArm.{s}", VB + f"{s}_Hand": f"Hand.{s}",
        VB + f"{s}_Thigh": f"UpperLeg.{s}", VB + f"{s}_Calf": f"LowerLeg.{s}", VB + f"{s}_Foot": f"Foot.{s}",
        VB + f"{s}_Toe0": f"Toes.{s}",
    })
    for fi, fn in enumerate(("Thumb", "Index", "Middle", "Ring", "Little")):
        MAP[VB + f"{s}_Finger{fi}"] = f"{fn}1.{s}"
        MAP[VB + f"{s}_Finger{fi}1"] = f"{fn}2.{s}"
        MAP[VB + f"{s}_Finger{fi}2"] = f"{fn}3.{s}"

FWD = Vector((0, -1, 0))
UP = Vector((0, 0, 1))


class Imported:
    pass


def _basis(M):
    """Поворот Source -> v2 по положению плеч (лево) и Z (верх)."""
    pos = {b.name: Vector(b.world[:3, 3]) for b in M.bones}
    l, r = pos.get(VB + "L_UpperArm"), pos.get(VB + "R_UpperArm")
    if l is None or r is None:
        left = Vector((0, 1, 0))
    else:
        left = (l - r)
        left.z = 0
        left.normalize()
    up = Vector((0, 0, 1))
    fwd = left.cross(up)            # в v2: вперёд = X(лево) x Z(верх) = -Y
    # строки: куда идут оси Source в пространстве v2: v2 = R @ src
    # v2.x = left·src, v2.y = -(fwd·src), v2.z = up·src
    R = Matrix((left, -fwd, up))
    return R


def load(mdl_path, roots, tex_out, name):
    """Читает MDL, конвертирует текстуры, создаёт меш (в v2-пространстве, рост 1.83) и данные скелета."""
    M = srcfmt.read_mdl(mdl_path)
    R = _basis(M)
    Rn = np.array(R, dtype=np.float64)
    pos = M.verts["p"].astype(np.float64) @ Rn.T * INCH
    nrm = M.verts["n"].astype(np.float64) @ Rn.T
    used = sorted(set(np.concatenate([t.reshape(-1) for t, _, _ in M.strips]).tolist()))
    zmin, zmax = pos[used, 2].min(), pos[used, 2].max()
    real_h = zmax - zmin
    k = BASE_HEIGHT / real_h
    pos[:, 2] -= zmin
    pos *= k
    # кости в пространстве v2 (мировые матрицы покоя)
    bw = {}
    for b in M.bones:
        w = np.array(b.world)
        p = (Rn @ w[:3, 3]) * INCH
        p[2] -= zmin
        p *= k
        rot = Rn @ w[:3, :3]
        m = np.eye(4)
        m[:3, :3] = rot
        m[:3, 3] = p
        bw[b.name] = m
    imp = Imported()
    imp.M, imp.R, imp.k, imp.real_height, imp.zmin = M, R, k, real_h, zmin
    imp.bone_world = bw
    imp.mesh = _build_mesh(M, pos, nrm, used, name, roots, tex_out)
    return imp


def _flip_tris(tris, pos, nrm):
    a, b, c = pos[tris[:, 0]], pos[tris[:, 1]], pos[tris[:, 2]]
    fn = np.cross(b - a, c - a)
    vn = nrm[tris[:, 0]] + nrm[tris[:, 1]] + nrm[tris[:, 2]]
    agree = (fn * vn).sum(1)
    if (agree < 0).sum() > (agree > 0).sum():
        return tris[:, [0, 2, 1]]
    return tris


def _build_mesh(M, pos, nrm, used, name, roots, tex_out):
    remap = {v: i for i, v in enumerate(used)}
    allt, mats = [], []
    skin = M.skins[0] if M.skins else list(range(len(M.textures)))
    for tris, mi, _ in M.strips:
        allt.append(tris)
        tex = skin[mi] if mi < len(skin) else mi
        mats.append(np.full(len(tris), tex, np.int64))
    tris = np.concatenate(allt)
    tmat = np.concatenate(mats)
    tris = _flip_tris(tris, pos, nrm)
    # убрать вырожденные/дубли
    keep = (tris[:, 0] != tris[:, 1]) & (tris[:, 1] != tris[:, 2]) & (tris[:, 0] != tris[:, 2])
    tris, tmat = tris[keep], tmat[keep]
    lt = np.vectorize(remap.get)(tris)
    me = bpy.data.meshes.new(name)
    verts = pos[used]
    me.vertices.add(len(verts))
    me.vertices.foreach_set("co", verts.astype(np.float32).reshape(-1))
    me.loops.add(len(lt) * 3)
    me.loops.foreach_set("vertex_index", lt.astype(np.int32).reshape(-1))
    me.polygons.add(len(lt))
    me.polygons.foreach_set("loop_start", (np.arange(len(lt)) * 3).astype(np.int32))
    me.polygons.foreach_set("loop_total", np.full(len(lt), 3, np.int32))
    uv = M.verts["uv"][used].astype(np.float32).copy()
    uv[:, 1] = 1.0 - uv[:, 1]
    lay = me.uv_layers.new(name="UVMap")
    lay.data.foreach_set("uv", uv[lt.reshape(-1)].reshape(-1))
    # материалы: только реально используемые
    tex_ids = sorted(set(tmat.tolist()))
    slot = {}
    infos = []
    for t in tex_ids:
        tname = M.textures[t] if t < len(M.textures) else f"mat{t}"
        info = material_info(M, roots, tname, tex_out)
        mat = make_material(info)
        me.materials.append(mat)
        slot[t] = len(me.materials) - 1
        infos.append(info)
    me.polygons.foreach_set("material_index", np.array([slot[t] for t in tmat], np.int32))
    me.update()
    me.validate()
    n = nrm[used]
    n = n / np.maximum(1e-8, np.linalg.norm(n, axis=1, keepdims=True))
    try:
        me.normals_split_custom_set_from_vertices(n.tolist())
    except Exception as e:
        print("normals:", e)
    for p in me.polygons:
        p.use_smooth = True
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    # веса: по костям Source (группы по исходным именам, переименуем при привязке)
    bw = M.verts["w"][used]
    bi = M.verts["b"][used]
    nb = M.verts["nb"][used]
    groups = {}
    for vi in range(len(used)):
        for j in range(int(nb[vi])):
            w = float(bw[vi][j])
            if w <= 0:
                continue
            bn = M.bones[int(bi[vi][j])].name
            groups.setdefault(bn, []).append((vi, w))
    for bn, lst in groups.items():
        g = obj.vertex_groups.new(name=bn)
        for vi, w in lst:
            g.add([vi], w, "ADD")
    _INFOS[obj.name] = infos
    return obj


_INFOS = {}


def infos_of(obj):
    return _INFOS.get(obj.name, [])


# ============================================================================ материалы

def _png(img, path):
    from PIL import Image
    Image.fromarray(img).save(path)


def material_info(M, roots, tname, out_dir):
    vmt = srcfmt.find_material(roots, M.cddirs, tname)
    info = dict(name=os.path.basename(tname).lower(), vmt=None, albedo=None, normal=None, mask=None, emission=None,
                alpha="opaque", smooth=0.35, metallic=0.0, color=[1, 1, 1])
    if not vmt:
        print("  нет VMT:", tname)
        return info
    p = srcfmt.parse_vmt(vmt)
    if p.get("__shader") == "patch" and "include" in p:
        for root in roots:
            ip = os.path.join(root, p["include"].lower().replace("\\", "/"))
            if os.path.exists(ip):
                q = srcfmt.parse_vmt(ip)
                q.update({k: v for k, v in p.items() if k not in ("__shader", "include")})
                p = q
                break
    info["vmt"] = os.path.basename(vmt)
    os.makedirs(out_dir, exist_ok=True)
    base = p.get("$basetexture")
    rgba = None
    if base:
        tp = srcfmt.find_texture(roots, base)
        if tp:
            try:
                rgba = srcfmt.read_vtf(tp)
            except Exception as e:
                print("  VTF", tp, e)
    translucent = p.get("$translucent") == "1"
    alphatest = p.get("$alphatest") == "1"
    if rgba is not None:
        img = rgba.copy()
        if translucent or alphatest:
            info["alpha"] = "blend" if translucent else "cutout"
        a_mask = p.get("$basemapalphaphongmask") == "1" or p.get("$basealphaenvmapmask") == "1"
        mask = rgba[..., 3] if a_mask else None
        if not (translucent or alphatest):
            img[..., 3] = 255
        fn = info["name"] + "_albedo.png"
        _png(img, os.path.join(out_dir, fn))
        info["albedo"] = fn
    else:
        mask = None
    bump = p.get("$bumpmap") or p.get("$normalmap")
    if bump:
        tp = srcfmt.find_texture(roots, bump)
        if tp:
            try:
                n = srcfmt.read_vtf(tp)
                if mask is None and p.get("$phong") == "1" and not p.get("$basemapalphaphongmask"):
                    mask = n[..., 3]
                nn = n.copy()
                nn[..., 1] = 255 - nn[..., 1]          # DirectX (Y-) -> OpenGL (Y+), как ждёт Unity
                nn[..., 3] = 255
                fn = info["name"] + "_normal.png"
                _png(nn[..., :3].copy(), os.path.join(out_dir, fn))
                info["normal"] = fn
            except Exception as e:
                print("  VTF", tp, e)
    em = p.get("$envmapmask")
    if mask is None and em:
        tp = srcfmt.find_texture(roots, em)
        if tp:
            try:
                mask = srcfmt.read_vtf(tp)[..., 0]
            except Exception as e:
                print("  VTF", tp, e)
    if mask is not None:
        # Unity Standard: металличность в R, гладкость в A
        sm = np.zeros(mask.shape + (4,), np.uint8)
        sm[..., 3] = (0.15 * 255 + mask.astype(np.float32) * 0.7).astype(np.uint8)
        fn = info["name"] + "_mask.png"
        _png(sm, os.path.join(out_dir, fn))
        info["mask"] = fn
        info["smooth"] = 0.8
    si = p.get("$selfillum") == "1"
    if si and rgba is not None:
        emi = rgba.copy()
        msk = rgba[..., 3:4].astype(np.float32) / 255.0
        smt = p.get("$selfillummask")
        if smt:
            tp = srcfmt.find_texture(roots, smt)
            if tp:
                try:
                    msk = srcfmt.read_vtf(tp)[..., :1].astype(np.float32) / 255.0
                except Exception:
                    pass
        if msk.shape[:2] == emi.shape[:2]:
            emi[..., :3] = (emi[..., :3].astype(np.float32) * msk).astype(np.uint8)
            emi[..., 3] = 255
            fn = info["name"] + "_emission.png"
            _png(emi, os.path.join(out_dir, fn))
            info["emission"] = fn
    c = p.get("$color2") or p.get("$color")
    if c:
        try:
            vals = [float(x) for x in c.strip("[]{} ").split()]
            if len(vals) == 3:
                if max(vals) > 1.5:
                    vals = [v / 255 for v in vals]
                info["color"] = vals
        except ValueError:
            pass
    info["dir"] = out_dir
    return info


def make_material(info):
    m = bpy.data.materials.new(info["name"])
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    if info.get("albedo"):
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = bpy.data.images.load(os.path.join(info["dir"], info["albedo"]))
        nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        if info["alpha"] != "opaque":
            nt.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
    if info.get("normal"):
        t2 = nt.nodes.new("ShaderNodeTexImage")
        t2.image = bpy.data.images.load(os.path.join(info["dir"], info["normal"]))
        t2.image.colorspace_settings.name = "Non-Color"
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(t2.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    bsdf.inputs["Roughness"].default_value = 1.0 - info.get("smooth", 0.35)
    m["sw_info"] = {k: (v if v is not None else "") for k, v in info.items() if k != "dir"}
    return m


# ============================================================================ скелет v2

def skeleton(imp):
    """Описание костей для swlib.rig2.build_armature: имя -> (голова, хвост, родитель, ось Z)."""
    W = imp.bone_world
    P = {n: Vector(m[:3, 3]) for n, m in W.items()}

    def g(n):
        return P.get(VB + n)

    S = {}
    S["Root"] = (Vector((0, 0, 0)), Vector((0, 0.25, 0)), None, UP)
    hip_c = (g("L_Thigh") + g("R_Thigh")) * 0.5
    spine = g("Spine")
    S["Hips"] = (Vector((0, hip_c.y, hip_c.z + 0.02)), spine.copy(), "Root", FWD)
    s2 = g("Spine2") or g("Spine1")
    s4 = g("Spine4") or (s2 + g("Neck1")) * 0.5
    S["Spine"] = (spine.copy(), s2.copy(), "Hips", FWD)
    S["Chest"] = (s2.copy(), s4.copy(), "Spine", FWD)
    S["UpperChest"] = (s4.copy(), g("Neck1").copy(), "Chest", FWD)
    S["Neck"] = (g("Neck1").copy(), g("Head1").copy(), "UpperChest", FWD)
    head = g("Head1")
    S["Head"] = (head.copy(), head + Vector((0, 0, 0.2)), "Neck", FWD)
    for s, sx in (("L", 1), ("R", -1)):
        S[f"Shoulder.{s}"] = (g(f"{s}_Clavicle").copy(), g(f"{s}_UpperArm").copy(), "UpperChest", FWD)
        S[f"UpperArm.{s}"] = (g(f"{s}_UpperArm").copy(), g(f"{s}_Forearm").copy(), f"Shoulder.{s}", FWD)
        S[f"LowerArm.{s}"] = (g(f"{s}_Forearm").copy(), g(f"{s}_Hand").copy(), f"UpperArm.{s}", FWD)
        wrist = g(f"{s}_Hand")
        mid = g(f"{s}_Finger2")
        idx = g(f"{s}_Finger1")
        lit = g(f"{s}_Finger4")
        th = g(f"{s}_Finger0")
        if mid is None:
            d = (wrist - g(f"{s}_Forearm")).normalized()
            mid = wrist + d * 0.09
        fdir = (mid - wrist).normalized()
        # направление большого пальца (ось Z кисти в v2) и нормаль ладони (ось Z пальцев)
        if th is not None:
            t = th - wrist
            t = (t - fdir * t.dot(fdir)).normalized()
        else:
            t = FWD.copy()
        palm = fdir.cross(t) if s == "L" else t.cross(fdir)
        S[f"Hand.{s}"] = (wrist.copy(), mid.copy(), f"LowerArm.{s}", t)
        for fi, fn in enumerate(("Thumb", "Index", "Middle", "Ring", "Little")):
            j = [g(f"{s}_Finger{fi}"), g(f"{s}_Finger{fi}1"), g(f"{s}_Finger{fi}2")]
            if any(x is None for x in j):
                continue
            seg = (j[2] - j[1])
            tip = j[2] + seg * 0.8
            pts = j + [tip]
            for kk in range(3):
                par = f"Hand.{s}" if kk == 0 else f"{fn}{kk}.{s}"
                z = palm if fn != "Thumb" else palm
                h, tl = pts[kk].copy(), pts[kk + 1].copy()
                if (tl - h).length < 1e-3:
                    tl = h + fdir * 0.02
                S[f"{fn}{kk + 1}.{s}"] = (h, tl, par, z.copy())
        S[f"UpperLeg.{s}"] = (g(f"{s}_Thigh").copy(), g(f"{s}_Calf").copy(), "Hips", FWD)
        S[f"LowerLeg.{s}"] = (g(f"{s}_Calf").copy(), g(f"{s}_Foot").copy(), f"UpperLeg.{s}", FWD)
        toe = g(f"{s}_Toe0")
        foot = g(f"{s}_Foot")
        if toe is None:
            toe = foot + Vector((0, -0.12, -0.05))
        S[f"Foot.{s}"] = (foot.copy(), toe.copy(), f"LowerLeg.{s}", UP)
        S[f"Toes.{s}"] = (toe.copy(), toe + Vector((0, -0.06, 0)), f"Foot.{s}", UP)
    return S


def remap_groups(obj, M):
    """Группы вершин Source -> кости v2 (немаппленные кости — к ближайшему маппленному предку)."""
    parent = {b.name: (M.bones[b.parent].name if b.parent >= 0 else None) for b in M.bones}

    def target(n):
        while n is not None:
            if n in MAP:
                return MAP[n]
            n = parent.get(n)
        return "Hips"
    import collections
    acc = collections.defaultdict(lambda: collections.defaultdict(float))
    for g in obj.vertex_groups:
        pass
    me = obj.data
    gi = {g.index: g.name for g in obj.vertex_groups}
    for v in me.vertices:
        for ge in v.groups:
            acc[target(gi[ge.group])][v.index] += ge.weight
    for g in list(obj.vertex_groups):
        obj.vertex_groups.remove(g)
    for bn, d in acc.items():
        g = obj.vertex_groups.new(name=bn)
        for vi, w in d.items():
            g.add([vi], min(1.0, w), "REPLACE")


def ankle_height(S):
    return 0.5 * (S["Foot.L"][0].z + S["Foot.R"][0].z)


# ============================================================================ оружие

def load_weapon(mdl_path, roots, tex_out, name, frame_len=None):
    """Оружие (w_*.mdl) в рамке Weapon.R: Y — ствол, Z — верх, начало — у рукояти (как в swlib.weapons)."""
    M = srcfmt.read_mdl(mdl_path)
    pos = M.verts["p"].astype(np.float64) * INCH
    nrm = M.verts["n"].astype(np.float64)
    used = sorted(set(np.concatenate([t.reshape(-1) for t, _, _ in M.strips]).tolist()))
    P = pos[used]
    bmap = {b.name: b for b in M.bones}
    hand = None
    for bn in (VB + "R_Hand", "ValveBiped.Anim_Attachment_RH", "ValveBiped.weapon_bone"):
        if bn in bmap:
            hand = np.array(bmap[bn].world)[:3, 3] * INCH
            break
    muzzle = None
    for an, bone, local in M.attachments:
        if an.lower() in ("muzzle", "muzzle_flash", "1"):
            bwm = np.array(M.bones[bone].world) if 0 <= bone < len(M.bones) else np.eye(4)
            muzzle = (bwm @ local)[:3, 3] * INCH
            break
    c = P.mean(0)
    X = P - c
    _, _, vt = np.linalg.svd(X, full_matrices=False)
    ax = vt[0]
    proj = X @ ax
    if hand is None:
        hand = c - ax * (proj.max() - proj.min()) * 0.15
    if muzzle is not None:
        if np.dot(muzzle - hand, ax) < 0:
            ax = -ax
    else:
        far_pos, far_neg = proj.max() - np.dot(hand - c, ax), np.dot(hand - c, ax) - proj.min()
        if far_neg > far_pos:
            ax = -ax
    proj = (P - hand) @ ax
    front = proj.max()
    if muzzle is None:
        sel = P[proj > front - 0.03]
        muzzle = sel.mean(0)
    # «верх»: ось ствола проходит над рукоятью
    line_pt = muzzle
    off = hand - line_pt
    off = off - ax * np.dot(off, ax)
    up = -off / max(1e-6, np.linalg.norm(off)) if np.linalg.norm(off) > 0.005 else np.array([0, 0, 1.0])
    up = up - ax * np.dot(up, ax)
    up /= np.linalg.norm(up)
    x = np.cross(ax, up)
    Rw = np.stack([x, ax, up])                     # строки — оси рамки
    loc = (pos - hand) @ Rw.T
    loc[:, 1] -= 0.065                             # кисть v2 (RIFLE_HAND) — 6.5 см позади начала рамки
    loc[:, 2] += 0.03 - 0.0
    nloc = nrm @ Rw.T
    mz = (muzzle - hand) @ Rw.T + np.array([0, -0.065, 0.03])
    obj = _build_mesh(M, loc, nloc, used, name, roots, tex_out)
    length = float(proj.max() - proj.min())
    return obj, Vector(mz.tolist()), length


def load_static(mdl_path, roots, tex_out, name):
    """Неанимированная модель (техника, турели, постройки) в положении покоя: Source -> v2 (вперёд -Y), метры."""
    M = srcfmt.read_mdl(mdl_path)
    R = np.array([[0, 1, 0], [-1, 0, 0], [0, 0, 1]], dtype=np.float64)     # X вперёд -> -Y, Y влево -> +X
    pos = M.verts["p"].astype(np.float64)
    # вершины VVD — в пространстве модели (поза покоя), веса не нужны
    pos = pos @ R.T * INCH
    nrm = M.verts["n"].astype(np.float64) @ R.T
    used = sorted(set(np.concatenate([t.reshape(-1) for t, _, _ in M.strips]).tolist()))
    obj = _build_mesh(M, pos, nrm, used, name, roots, tex_out)
    for g in list(obj.vertex_groups):
        obj.vertex_groups.remove(g)
    P = pos[used]
    return obj, P.min(0), P.max(0)
