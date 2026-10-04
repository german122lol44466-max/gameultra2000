"""
Персонажи v2 на реалистичном теле MakeHuman.

Сторона ИМПЕРИЯ/СИТХИ: штурмовик, тяжёлый штурмовик, командир, Дарт Вейдер, Дарт Мол, Дарт Сидиус, граф Дуку.
Сторона РЕСПУБЛИКА/ДЖЕДАИ: клон-штурмовик, клон-командир, тяжёлый клон, Оби-Ван Кеноби, Мейс Винду, Энакин Скайуокер.
"""

import math

import bmesh
import bpy
from mathutils import Matrix, Vector

from . import armor as A
from . import cloth as C
from . import human, mh, mats, rig2, wear as Wr, weapons
from . import kit as K
from . import sith as S1
from . import troopers2 as T2


def mat_eye(name, hue=0.0, sat=1.0, val=1.0, glow=0.0):
    """Глаз MakeHuman с настоящей текстурой радужки; цвет меняется сдвигом тона."""
    m = bpy.data.materials.new(name)
    nt, n, l, bsdf, coord = K._nodes(m)
    img = bpy.data.images.load(mh.fetch("eyes/materials/brown_eye.png"), check_existing=True)
    tex = n.new("ShaderNodeTexImage")
    tex.image = img
    uv = n.new("ShaderNodeUVMap")
    l.new(uv.outputs["UV"], tex.inputs["Vector"])
    hsv = n.new("ShaderNodeHueSaturation")
    hsv.inputs["Hue"].default_value = 0.5 + hue
    hsv.inputs["Saturation"].default_value = sat
    hsv.inputs["Value"].default_value = val
    l.new(tex.outputs["Color"], hsv.inputs["Color"])
    l.new(hsv.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.05
    bsdf.inputs["Coat Weight"].default_value = 1.0
    if glow:
        l.new(hsv.outputs["Color"], bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = glow
    K.tag(m, (0.3, 0.2, 0.1), 0.05)
    m["texture"] = "brown_eye.png"
    return m


def hair(k, info, S, head, mat, region, **kw):
    return Wr.hair_shell(k, info, S, mat, region, center=head[0], **kw)


# ---- стандартные области волос по ориентирам лица L
def r_brows(L):
    return lambda c, n: L.eye_z + 0.011 < c.z < L.eye_z + 0.024 and 0.012 < abs(c.x) < 0.052 and n.y < -0.35


def r_moustache(L):
    return lambda c, n: L.mouth_z + 0.003 < c.z < L.nose_z - 0.01 and abs(c.x) < 0.03 and n.y < -0.3


def r_beard(L, cheeks=True, full=True):
    def f(c, n):
        if c.y > L.head_y + 0.01:
            return False
        if abs(c.x) < 0.024 and c.z > L.lip_low_z - 0.007 and c.z < L.mouth_z + 0.004 and n.y < -0.3:
            return False                                   # губы открыты
        if c.z < L.chin_z - (0.045 if full else 0.02):
            return False
        if c.z < L.mouth_z - 0.002:
            return True
        return cheeks and abs(c.x) > 0.045 and c.z < L.eye_z - 0.045
    return f


def r_scalp(L, front=0.06, sides=True, back_low=None):
    def f(c, n):
        if c.z > L.eye_z + front and (c.y > L.eye_y + 0.01 or c.z > L.eye_z + front + 0.02):
            return True
        if sides and abs(c.x) > 0.06 and c.z > L.eye_z - 0.005 and c.y > L.eye_y + 0.045:
            return True
        if c.y > L.head_y + 0.015 and c.z > (back_low if back_low is not None else L.chin_z + 0.02):
            return True
        return False
    return f


class Built:
    pass


def _drop_loose(obj):
    """Убрать вершины/рёбра, оставшиеся без граней после удаления скрытых участков тела."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.delete(bm, geom=[e for e in bm.edges if not e.link_faces], context="EDGES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(obj.data)
    bm.free()


def make(name, spec):
    """Собрать персонажа по спецификации. Возвращает Built(rig, meshes, grip, height, ...)."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = 30
    M = mats.palette()
    eye = spec.get("eye")
    if eye is not None:
        M["eyeball"] = mat_eye(name + "_Eye", *eye)
    targets = mh.macro(**spec["macro"]) + [mh.mod(*m) for m in spec.get("mods", [])]
    skin = spec.get("skin", "skin_light")
    body = human.build(name + "_Body", targets, M[skin])
    for extra in spec.get("body_mats", []):
        body.obj.data.materials.append(M[extra])
    human.skin_weights(body)
    S = rig2.skel_from_body(body)
    info = A.BodyInfo(body.obj)
    head = A.head_box(info)
    k = K.Kit(M)
    meshes = [body.obj]
    eyes = None
    if eye is not None:
        eyes = human.eyes(body, M["eyeball"], name=name + "_Eyes")
        meshes.append(eyes)
    spec["gear"](k, info, S, head, body)
    _drop_loose(body.obj)
    info2 = A.BodyInfo(body.obj)          # тело могло измениться (удаление/утапливание)
    arm = rig2.build_armature(name + "_Rig", S)
    gear = K.bind(k, arm, name + "_Gear") if k.parts else None
    if gear:
        meshes.append(gear)
    rig2.bind(meshes, arm)
    rig2.to_tpose(arm, meshes)
    # оружие — после T-позы, в пространстве кости Weapon.R
    wfn = weapons.WEAPONS[spec["weapon"]]

    class _P:
        def __getattr__(self, n):
            return lambda *a, **kw: None
    grip = wfn(_P())
    rig = rig2.Rig(arm, grip)
    wk = K.Kit(M)
    wk.xform = rig.rest["Weapon.R"]
    wfn(wk)
    for bn, bm, ln in grip["blades"]:
        wk.xform = rig.rest[bn]
        weapons.blade(wk, bn, ln, mat=spec.get("blade", "blade"))
    wk.xform = None
    wmesh = K.bind(wk, arm, name + "_Weapon")
    meshes.append(wmesh)
    s = spec["height"] / 1.83
    arm.scale = (s, s, s)
    b = Built()
    b.rig, b.meshes, b.grip, b.height, b.body, b.S = rig, meshes, grip, spec["height"], body, S
    return b


# ============================================================================ штурмовики и клоны

def _trooper_body_mats(info):
    Wr.body_materials(info, lambda d, c: 1 if Wr.is_hand(d) else 0)


def trooper_gear(variant):
    def gear(k, info, S, head, body):
        _trooper_body_mats(info)
        T2.strip_body(info)
        info = A.BodyInfo(body.obj)
        T2.stormtrooper(k, info, S, variant, head=head)
        Wr.delete_faces(info, lambda d, c: Wr.base(d) in ("Foot", "Toes"))
        if variant.get("pauldron"):
            s = variant["pauldron_side"]
            sg = 1 if s == "L" else -1
            sh = Vector(S[f"UpperArm.{s}"][0])
            nrm = Vector((0.55 * sg, 0.0, 0.85)).normalized()

            def bell(co, u, nrm=nrm):
                dd = co.dot(nrm)
                return co - nrm * (dd + 0.004) if dd < -0.004 else co
            k.sphere(f"UpperArm.{s}", variant["pauldron"], sh + Vector((0.018 * sg, 0.0, 0.016)), 1.0,
                     scale=(0.1, 0.105, 0.085), seg=32, deform=bell, subsurf=1, solid=0.006)
        if variant.get("backpack"):
            c = Vector(S["Chest"][1]) + Vector((0, 0.17, -0.05))
            k.box("UpperChest", "grey", c, (0.25, 0.1, 0.27), bevel=0.02, smooth=True)
            k.box("UpperChest", "metal_dark", c + Vector((0, 0.055, 0)), (0.19, 0.02, 0.21), bevel=0.006)
            for i in range(3):
                k.cyl("UpperChest", "metal_silver", c + Vector((-0.06 + i * 0.06, 0, 0.13)), c + Vector((-0.06 + i * 0.06, 0, 0.18)), 0.012, verts=12)
        for st in variant.get("stripes", []):
            pass
    return gear


def clone_gear(color=None):
    def gear(k, info, S, head, body):
        _trooper_body_mats(info)
        T2.strip_body(info)
        info = A.BodyInfo(body.obj)
        T2.stormtrooper(k, info, S, {"helmet": "clone"}, head=head)
        Wr.delete_faces(info, lambda d, c: Wr.base(d) in ("Foot", "Toes"))
        if color:
            # цветные метки: наплечники и полоса на шлеме/груди
            for s, sg in (("L", 1), ("R", -1)):
                sh = Vector(S[f"UpperArm.{s}"][0])
                nrm = Vector((0.55 * sg, 0.0, 0.85)).normalized()

                def bell(co, u, nrm=nrm):
                    dd = co.dot(nrm)
                    return co - nrm * (dd + 0.012) if dd < -0.012 else co
                k.sphere(f"UpperArm.{s}", color, sh + Vector((0.012 * sg, 0.0, 0.008)), 1.0, scale=(0.08, 0.087, 0.072), seg=32,
                         deform=bell, subsurf=1, solid=0.006)
    return gear


# ============================================================================ ситхи

def _fit_helmet(k, start, head, ref_c, ref_size, scale=1.02, dz=0.012):
    hc, hs = head
    s = hs.z / ref_size * scale
    M = Matrix.Translation(hc + Vector((0, -0.004, dz))) @ Matrix.Scale(s, 4) @ Matrix.Translation(-ref_c)
    A.transform_parts(k, start, M)
    for i in range(start, len(k.parts)):
        k.parts[i] = (k.parts[i][0], "Head")


def vader_gear(k, info, S, head, body):
    T2.strip_body(info, glove_mat_index=1)
    info = A.BodyInfo(body.obj)
    nz = Vector(S["Neck"][0]).z
    chest = Vector(S["Chest"][0])
    hz = Vector(S["Hips"][0]).z
    Wr.tunic(k, info, S, "vader_leather", bottom_z=hz - 0.12, sleeve=0.86, offset=0.012, collar=0.02)
    Wr.pants(k, info, S, "vader_cloth", top_z=hz, bottom_t=0.6)
    Wr.boots(k, info, S, "boot", top_t=0.22, offset=0.013)
    Wr.gloves(k, info, "glove", S=S, cuff_t=0.72, offset=0.004)
    # наплечная броня («нагрудник-ворот») и наплечники
    sx = abs(Vector(S["UpperArm.L"][0]).x)
    A.shell(k, info, "vader_gloss", lambda c, n, d: (not Wr.is_hand(d)) and c.z > chest.z + 0.1 and abs(c.x) < sx + 0.05 and not Wr.is_head(d)
            and Wr.base(d) not in ("LowerArm",),
            [((0, 0, chest.z + 0.12), (0, 0, 1)), ((0, 0, nz + 0.05), (0, 0, -1))], offset=0.03, thick=0.008, smooth=14, name="cowl")
    for s, sg in (("L", 1), ("R", -1)):
        A.limb_tube(k, info, S, f"LowerArm.{s}", "vader_gloss", 0.15, 0.7, offset=0.02, name="gauntlet")
    # пульт и пояс
    y = Wr.front_y(info, 0.0, chest.z + 0.07)
    k.box("Chest", "metal_grey", (0, y - 0.03, chest.z + 0.06), (0.14, 0.025, 0.1), bevel=0.006)
    for i, mn in enumerate(("btn_red", "btn_green", "btn_blue", "btn_red", "btn_white")):
        k.box("Chest", mn, (-0.05 + i * 0.025, y - 0.045, chest.z + 0.085), (0.014, 0.006, 0.012), bevel=0.002)
    for i in range(6):
        k.box("Chest", "metal_black", (-0.05 + i * 0.02, y - 0.044, chest.z + 0.045), (0.006, 0.004, 0.028), bevel=0.001)
    bz = Vector(S["Spine"][0]).z - 0.02
    Wr.belt(k, info, S, "vader_gloss", bz, h=0.075, offset=0.03)
    yb = Wr.front_y(info, 0.0, bz) - 0.035
    for sg in (1, -1):
        k.box("Hips", "metal_silver", (0.065 * sg, yb - 0.006, bz), (0.055, 0.022, 0.06), bevel=0.004)
        for j in range(3):
            k.box("Hips", "metal_black", ((0.048 + j * 0.015) * sg, yb - 0.018, bz + 0.005), (0.006, 0.004, 0.03), bevel=0.001)
    C.skirt(k, info, S, "vader_cloth", top_z=bz + 0.02, bottom_z=0.08, r_top=(0.19, 0.14), flare=0.5, split=0.0)
    C.cape(k, info, S, "vader_cloth", bottom_z=0.04, flare=0.35)
    Wr.hide_covered(info, keep_head=False, keep_hands=False)
    start = len(k.parts)
    S1.vader_helmet(k)
    _fit_helmet(k, start, head, Vector((0, 0.0, 1.69)), 0.215, scale=1.03, dz=0.008)


def maul_tattoo2(u, local):
    """Татуировка Мола по координатам относительно центра головы (local) и нормали."""
    x, y, z = local
    ax = abs(x)
    if y > 0.02:
        return 1 if math.sin(math.atan2(x, y) * 7) > 0.5 and z > -0.02 else 0
    if ax < 0.012 and z > 0.02:
        return 1
    if z > 0.05 and math.sin(ax * 140 + z * 40) > 0.55:
        return 1
    if ((ax - 0.032) / 0.026) ** 2 + ((z - 0.012) / 0.02) ** 2 < 1:
        return 1
    if ax < 0.013 and -0.04 < z < 0.0:
        return 1
    if ax < 0.03 and -0.07 < z < -0.045:
        return 1
    if ax < 0.008 and z < -0.07:
        return 1
    if ax > 0.055 and z < 0.0:
        return 1
    return 0


def maul_gear(k, info, S, head, body):
    hc, hs = head
    me = body.obj.data
    # татуировка: черный/красный по граням головы и шеи, руки — перчатки
    def fn(d, c):
        if Wr.is_hand(d):
            return 2
        if Wr.is_head(d) or d == "Neck":
            loc = c - hc
            return 1 if maul_tattoo2(None, loc) else 0
        return 1
    Wr.body_materials(info, fn)
    hz = Vector(S["Hips"][0]).z
    Wr.tunic(k, info, S, "maul_cloth", bottom_z=hz - 0.1, sleeve=0.9, offset=0.01, collar=0.035)
    Wr.pants(k, info, S, "maul_cloth", top_z=hz, bottom_t=0.7)
    Wr.boots(k, info, S, "boot", top_t=0.5)
    Wr.gloves(k, info, "glove", S=S, cuff_t=0.85)
    bz = Vector(S["Spine"][0]).z - 0.01
    Wr.belt(k, info, S, "maul_cloth2", bz, h=0.09, offset=0.022)
    Wr.belt(k, info, S, "leather_dark", bz - 0.03, h=0.03, offset=0.03)
    C.skirt(k, info, S, "maul_cloth", top_z=bz - 0.03, bottom_z=0.42, r_top=(0.185, 0.135), flare=0.35, split=0.45)
    Wr.hide_covered(info)
    # рожки по черепу
    import random
    rnd = random.Random(5)
    for i in range(11):
        a = -1.1 + 2.2 * (i / 10)
        u = Vector((math.sin(a) * 0.75, math.cos(a) * 0.35 - 0.2, 0.75 + 0.2 * math.cos(a * 1.5))).normalized()
        p = hc + Vector((u.x * hs.x * 0.5, u.y * hs.y * 0.5, u.z * hs.z * 0.5))
        L = 0.018 + 0.012 * rnd.random() + (0.01 if abs(a) < 0.3 else 0)
        k.cyl("Head", "horn", p - u * 0.006, p + u * L, 0.0065, 0.0012, verts=12)


def sidious_gear(k, info, S, head, body):
    Wr.body_materials(info, lambda d, c: 0)
    hz = Vector(S["Hips"][0]).z
    Wr.tunic(k, info, S, "robe_black", bottom_z=hz - 0.1, sleeve=0.92, offset=0.012, collar=0.0)
    C.skirt(k, info, S, "robe_black", top_z=Vector(S["Spine"][1]).z, bottom_z=0.02, r_top=(0.2, 0.15), flare=0.55)
    C.cape(k, info, S, "robe_black", bottom_z=0.02, flare=0.5)
    Wr.hide_covered(info, keep_hands=True, feet=False)
    # широкие рукава мантии (раструбы от локтя)
    for s, sg in (("L", 1), ("R", -1)):
        h, t = Vector(S[f"LowerArm.{s}"][0]), Vector(S[f"LowerArm.{s}"][1])
        d = (t - h)
        k.lathe(f"LowerArm.{s}", "robe_black", h - d * 0.05, t + d * 0.05,
                [(0, 0.06), (d.length * 0.4, 0.075), (d.length * 0.9, 0.105), (d.length * 1.1, 0.12)], verts=28, caps=False, subsurf=1)
    # капюшон: оболочка головы, открытая спереди
    hc, hs = head
    sc = Vector((hs.x * 0.5 + 0.035, hs.y * 0.5 + 0.035, hs.z * 0.5 + 0.03))
    k.sphere("Head", "robe_black", hc + Vector((0, 0.012, 0.012)), 1.0, scale=sc, seg=40, subsurf=1, solid=0.008,
             keep=lambda u: not (u.y < -0.25 and u.z < 0.62 and abs(u.x) < 0.8),
             deform=lambda co, u: (co.x * (1 + 0.15 * max(0, -u.z)), co.y, co.z - (0.07 if u.z < -0.5 and u.y > -0.3 else 0)))
    hair(k, info, S, head, "brows_dark", r_brows(body.landmarks()), thick=0.002, noise_amp=0.0015, name="brows")


def dooku_gear(k, info, S, head, body):
    Wr.body_materials(info, lambda d, c: 0)
    hz = Vector(S["Hips"][0]).z
    Wr.tunic(k, info, S, "dooku_cloth", bottom_z=hz - 0.15, sleeve=0.9, offset=0.01, collar=0.03)
    Wr.pants(k, info, S, "dooku_cloth", top_z=hz, bottom_t=0.6)
    Wr.boots(k, info, S, "boot", top_t=0.3)
    Wr.gloves(k, info, "leather_black", S=S, cuff_t=0.86)
    bz = Vector(S["Spine"][0]).z - 0.01
    Wr.belt(k, info, S, "leather_brown", bz, h=0.05, offset=0.02, buckle="metal_silver")
    C.skirt(k, info, S, "dooku_cloth2", top_z=bz - 0.02, bottom_z=0.35, r_top=(0.18, 0.13), flare=0.35, split=0.4)
    C.cape(k, info, S, "dooku_cape", bottom_z=0.05, flare=0.4)
    Wr.hide_covered(info)
    # цепь-застёжка плаща
    nz = Vector(S["Neck"][0]).z
    y = Wr.front_y(info, 0.0, nz - 0.07) - 0.02
    for sg in (1, -1):
        k.cyl("UpperChest", "metal_silver", (0.1 * sg, y + 0.01, nz - 0.06), (0.1 * sg, y - 0.005, nz - 0.06), 0.017, verts=20)
    for i in range(9):
        x = -0.09 + i * 0.0225
        z = nz - 0.07 - 0.025 * math.cos(x / 0.09 * math.pi / 2)
        k.torus("UpperChest", "metal_silver", (x, y - 0.004, z), 0.007, 0.002, axis=(0, 1 if i % 2 else 0, 0 if i % 2 else 1))
    # седые волосы назад и короткая борода
    L = body.landmarks()
    hair(k, info, S, head, "hair_grey", r_scalp(L, front=0.07), thick=0.01, noise_amp=0.004, name="hair")
    hair(k, info, S, head, "hair_grey", r_beard(L, cheeks=False, full=False), thick=0.006, noise_amp=0.003, name="beard", bone="Jaw")
    hair(k, info, S, head, "hair_grey", r_moustache(L), thick=0.004, noise_amp=0.002, name="moustache")
    hair(k, info, S, head, "hair_grey", r_brows(L), thick=0.003, noise_amp=0.002, name="brows")


# ============================================================================ джедаи

def jedi_base(k, info, S, head, body, under, tabard, pants, boots, belt, skirt=True, skirt_mat=None):
    Wr.body_materials(info, lambda d, c: 0)
    hz = Vector(S["Hips"][0]).z
    Wr.tunic(k, info, S, under, bottom_z=hz - 0.12, sleeve=0.88, offset=0.011, collar=0.025, neck_open=0.0)
    Wr.tabard(k, info, S, tabard, bottom_z=hz - 0.06, width=0.075, offset=0.018)
    Wr.pants(k, info, S, pants, top_z=hz, bottom_t=0.65)
    Wr.boots(k, info, S, boots, top_t=0.25)
    bz = Vector(S["Spine"][0]).z - 0.01
    Wr.belt(k, info, S, tabard, bz + 0.01, h=0.09, offset=0.026)     # оби (широкий пояс-кушак)
    Wr.belt(k, info, S, belt, bz - 0.005, h=0.035, offset=0.034, buckle="metal_silver")
    yb = Wr.front_y(info, 0.0, bz) - 0.04
    for sg, x in ((1, 0.08), (-1, 0.08), (1, 0.13), (-1, 0.13)):
        k.box("Hips", belt, (x * sg, yb + 0.012 * (x > 0.1), bz - 0.01), (0.03, 0.022, 0.045), bevel=0.005, smooth=True)
    if skirt:
        C.skirt(k, info, S, skirt_mat or under, top_z=bz - 0.02, bottom_z=0.4, r_top=(0.18, 0.13), flare=0.3, split=0.35)
    Wr.hide_covered(info)


def obiwan_gear(k, info, S, head, body):
    jedi_base(k, info, S, head, body, "jedi_cream", "jedi_tan", "jedi_cream", "boot", "leather_mid")
    L = body.landmarks()
    hair(k, info, S, head, "hair_auburn", r_scalp(L, front=0.055), thick=0.012, noise_amp=0.005, name="hair")
    hair(k, info, S, head, "hair_auburn", r_beard(L), thick=0.007, noise_amp=0.003, name="beard", bone="Jaw")
    hair(k, info, S, head, "hair_auburn", r_moustache(L), thick=0.005, noise_amp=0.002, name="moustache")
    hair(k, info, S, head, "hair_auburn", r_brows(L), thick=0.003, noise_amp=0.002, name="brows")


def mace_gear(k, info, S, head, body):
    jedi_base(k, info, S, head, body, "jedi_tan", "jedi_brown", "jedi_tan", "boot", "leather_mid")
    hair(k, info, S, head, "brows_dark", r_brows(body.landmarks()), thick=0.002, noise_amp=0.001, name="brows")


def anakin_gear(k, info, S, head, body):
    jedi_base(k, info, S, head, body, "jedi_dark", "leather_black", "jedi_dark", "boot", "leather_black",
              skirt_mat="jedi_dark")
    Wr.gloves(k, info, "leather_black", S=S, cuff_t=0.85)
    L = body.landmarks()
    hair(k, info, S, head, "hair_brown", r_scalp(L, front=0.045, back_low=L.chin_z - 0.03), thick=0.016, noise_amp=0.008,
         name="hair", volume=1.3)
    hair(k, info, S, head, "brows_dark", r_brows(L), thick=0.002, noise_amp=0.001, name="brows")


# ============================================================================ спецификации

MALE = dict(gender="male")
CHARACTERS = {
    # ---------------- Империя / ситхи
    "Stormtrooper": dict(side="Empire", title="Штурмовик", role="soldier", weapon="E11", height=1.83, skin="suit",
                         body_mats=["glove"], macro=dict(MALE, age="young", muscle=0.85, weight=0.6, height=0.55),
                         gear=trooper_gear({})),
    "HeavyTrooper": dict(side="Empire", title="Тяжёлый штурмовик", role="heavy", weapon="DLT19", height=1.86, skin="suit",
                         body_mats=["glove"], macro=dict(MALE, age="young", muscle=0.8, weight=0.6, height=0.6),
                         gear=trooper_gear(dict(backpack=True, pauldron="pauldron_black", pauldron_side="R"))),
    "TrooperCommander": dict(side="Empire", title="Командир штурмовиков", role="officer", weapon="E11", height=1.83, skin="suit",
                             body_mats=["glove"], macro=dict(MALE, age="young", muscle=0.85, weight=0.6, height=0.55),
                             gear=trooper_gear(dict(pauldron="orange", pauldron_side="L"))),
    "DarthVader": dict(side="Empire", title="Дарт Вейдер", role="sith", style="vader", weapon="Saber_Vader", height=2.02,
                       skin="vader_leather", body_mats=["glove"], macro=dict(MALE, age="young", muscle=0.9, weight=0.65, height=0.9),
                       gear=vader_gear),
    "DarthMaul": dict(side="Empire", title="Дарт Мол", role="sith", style="maul", weapon="Saber_Maul", height=1.75,
                      skin="maul_red2", body_mats=["maul_black2", "glove"], eye=(-0.42, 1.6, 1.3, 0.4),
                      macro=dict(MALE, age="young", muscle=0.85, weight=0.35, height=0.4),
                      mods=[("head", "head-oval", 0.4), ("eyebrows", "eyebrows-trans", 0.6, "backward|forward"),
                            ("forehead", "forehead-nubian", 0.5), ("cheek", "l-cheek-bones", 0.7), ("cheek", "r-cheek-bones", 0.7),
                            ("chin", "chin-prominent", 0.4)],
                      gear=maul_gear),
    "DarthSidious": dict(side="Empire", title="Дарт Сидиус", role="sith", style="sidious", weapon="Saber_Sidious", height=1.73,
                         skin="skin_sidious", eye=(-0.42, 1.8, 1.2, 0.5), macro=dict(MALE, age="old", muscle=0.3, weight=0.2, height=0.35),
                         mods=[("head", "head-age", 1.0), ("cheek", "l-cheek-volume", -0.8), ("cheek", "r-cheek-volume", -0.8),
                               ("eyes", "l-eye-bag", 1.0, "decr|incr"), ("eyes", "r-eye-bag", 1.0, "decr|incr"),
                               ("nose", "nose-hump", 0.6), ("chin", "chin-prominent", 0.3)],
                         gear=sidious_gear),
    "CountDooku": dict(side="Empire", title="Граф Дуку", role="sith", style="dooku", weapon="Saber_Dooku", height=1.93,
                       skin="skin_aged", eye=(0.0, 0.6, 0.8, 0.0), macro=dict(MALE, age="old", muscle=0.45, weight=0.35, height=0.8),
                       mods=[("head", "head-age", 0.7), ("nose", "nose-hump", 0.7), ("nose", "nose-scale-vert", 0.4),
                             ("cheek", "l-cheek-volume", -0.4), ("cheek", "r-cheek-volume", -0.4), ("chin", "chin-prominent", 0.5)],
                       gear=dooku_gear),
    # ---------------- Республика / джедаи
    "CloneTrooper": dict(side="Republic", title="Клон-штурмовик", role="soldier", weapon="DC15A", height=1.83, skin="suit",
                         body_mats=["glove"], macro=dict(MALE, age="young", muscle=0.85, weight=0.6, height=0.55),
                         gear=clone_gear("clone_blue")),
    "CloneHeavy": dict(side="Republic", title="Тяжёлый клон", role="heavy", weapon="DLT19", height=1.83, skin="suit",
                       body_mats=["glove"], macro=dict(MALE, age="young", muscle=0.8, weight=0.6, height=0.55),
                       gear=clone_gear("clone_blue")),
    "CloneCommander": dict(side="Republic", title="Клон-командир", role="officer", weapon="DC15A", height=1.83, skin="suit",
                           body_mats=["glove"], macro=dict(MALE, age="young", muscle=0.85, weight=0.6, height=0.55),
                           gear=clone_gear("clone_orange")),
    "ObiWan": dict(side="Republic", title="Оби-Ван Кеноби", role="jedi", style="obiwan", weapon="Saber_ObiWan", height=1.82,
                   blade="blade_blue", skin="skin_light", eye=(0.12, 0.5, 1.0, 0.0),
                   macro=dict(MALE, age="young", muscle=0.6, weight=0.45, height=0.5),
                   mods=[("head", "head-age", 0.35), ("nose", "nose-hump", 0.3), ("chin", "chin-width", 0.2)],
                   gear=obiwan_gear),
    "MaceWindu": dict(side="Republic", title="Мейс Винду", role="jedi", style="mace", weapon="Saber_Mace", height=1.88,
                      blade="blade_purple", skin="skin_dark", eye=(0.0, 0.8, 0.6, 0.0),
                      macro=dict(MALE, age="young", muscle=0.75, weight=0.5, height=0.7, race="african"),
                      mods=[("head", "head-age", 0.45), ("head", "head-oval", 0.3)],
                      gear=mace_gear),
    "Anakin": dict(side="Republic", title="Энакин Скайуокер", role="jedi", style="anakin", weapon="Saber_Anakin", height=1.88,
                   blade="blade_blue", skin="skin_light", eye=(0.18, 0.6, 1.0, 0.0),
                   macro=dict(MALE, age="young", muscle=0.7, weight=0.4, height=0.7),
                   mods=[("chin", "chin-prominent", 0.3), ("cheek", "l-cheek-bones", 0.3), ("cheek", "r-cheek-bones", 0.3)],
                   gear=anakin_gear),
}
