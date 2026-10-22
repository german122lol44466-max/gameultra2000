"""
Окружение для «Командира клонов» (процедурно, CC0): текстуры земли (тайлящиеся, с картами нормалей), трава,
деревья, скалы и постройки (имперские стены, башни, ворота, бункер, командный центр, генератор щита;
республиканские баррикады, бункер, посадочная площадка; ящики, контейнеры, командный пункт).

    python Tools/Blender/build_environment.py

Результат: Assets/_Project/Commander/Art/Environment/*.fbx и Textures/*.png, manifest.json (материалы -> текстуры).
"""

import json
import math
import os
import random

import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "Assets", "_Project", "Commander", "Art", "Environment")
TEX = os.path.join(OUT, "Textures")
rng = np.random.default_rng(1977)


# ============================================================================ текстуры
def fbm(n, octaves=6, falloff=2.0, seed=0):
    """Тайлящийся шум: белый шум, отфильтрованный в частотной области (1/f^falloff)."""
    r = np.random.default_rng(seed)
    noise = r.standard_normal((n, n))
    f = np.fft.fft2(noise)
    ky = np.fft.fftfreq(n)[:, None]
    kx = np.fft.fftfreq(n)[None, :]
    k = np.sqrt(kx * kx + ky * ky)
    k[0, 0] = 1.0
    f *= 1.0 / (k ** (falloff * 0.5 + 0.5))
    f[0, 0] = 0
    out = np.real(np.fft.ifft2(f))
    out = (out - out.min()) / (out.max() - out.min())
    return out


def normal_from(h, strength=4.0):
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * strength
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * strength
    n = np.stack([-dx, dy, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return ((n * 0.5 + 0.5) * 255).astype(np.uint8)


def save(img, name):
    from PIL import Image
    os.makedirs(TEX, exist_ok=True)
    arr = np.clip(img, 0, 255).astype(np.uint8)
    Image.fromarray(arr).save(os.path.join(TEX, name))


def lerp(a, b, t):
    return a + (b - a) * t[..., None]


def ground_textures(n=1024):
    big, mid, small = fbm(n, falloff=2.2, seed=1), fbm(n, falloff=1.4, seed=2), fbm(n, falloff=0.6, seed=3)
    speck = rng.random((n, n))
    # трава
    base = lerp(np.array([62, 78, 38.0]), np.array([104, 118, 56.0]), big * 0.7 + mid * 0.3)
    dry = np.clip((mid - 0.55) * 3, 0, 1)
    base = lerp(base, np.array([128, 118, 70.0]), dry * 0.6)
    base *= (0.85 + small[..., None] * 0.3)
    base += ((speck > 0.97)[..., None] * np.array([20, 24, 6.0]))
    save(base, "ground_grass.png")
    save(normal_from(small * 0.6 + mid * 0.4, 3.0), "ground_grass_n.png")
    # земля
    base = lerp(np.array([78, 62, 44.0]), np.array([120, 98, 70.0]), big * 0.6 + small * 0.4)
    pebbles = (fbm(n, falloff=0.2, seed=9) > 0.72)
    base[pebbles] = base[pebbles] * 1.25
    save(base, "ground_dirt.png")
    save(normal_from(small * 0.7 + pebbles * 0.3, 4.0), "ground_dirt_n.png")
    # скала: слои
    y = np.linspace(0, 1, n)[:, None] * np.ones((1, n))
    strata = (np.sin((y * 28 + big * 6) * math.pi) * 0.5 + 0.5)
    rock = strata * 0.35 + mid * 0.4 + small * 0.25
    base = lerp(np.array([82, 78, 74.0]), np.array([150, 144, 134.0]), rock)
    save(base, "ground_rock.png")
    save(normal_from(rock, 6.0), "ground_rock_n.png")
    # гравий/дорога
    grav = fbm(n, falloff=0.1, seed=11)
    base = lerp(np.array([118, 108, 90.0]), np.array([168, 156, 132.0]), grav * 0.6 + big * 0.4)
    save(base, "ground_road.png")
    save(normal_from(grav, 5.0), "ground_road_n.png")


def material_textures(n=1024):
    # имперская панель: сетка 4x4 с утопленными швами
    t = np.linspace(0, 4, n, endpoint=False)
    u, v = np.meshgrid(t, t)
    fu, fv = u % 1.0, v % 1.0
    seam = np.minimum(np.minimum(fu, 1 - fu), np.minimum(fv, 1 - fv))
    groove = np.clip(seam / 0.012, 0, 1)
    inset = np.clip((np.minimum(np.minimum(fu, 1 - fu), np.minimum(fv, 1 - fv)) - 0.1) / 0.01, 0, 1)
    dirt = fbm(n, falloff=1.6, seed=21)
    h = groove * 0.6 + inset * 0.2 + dirt * 0.2
    col = lerp(np.array([48, 50, 54.0]), np.array([92, 95, 100.0]), dirt * 0.5 + inset * 0.3)
    col *= (0.55 + 0.45 * groove)[..., None]
    save(col, "imperial_panel.png")
    save(normal_from(h, 8.0), "imperial_panel_n.png")
    # светлый имперский (ангарный) металл
    col2 = lerp(np.array([118, 122, 128.0]), np.array([168, 172, 178.0]), dirt * 0.5 + inset * 0.3) * (0.6 + 0.4 * groove)[..., None]
    save(col2, "imperial_light.png")
    # дюракрит
    stains = fbm(n, falloff=1.8, seed=31)
    pores = fbm(n, falloff=0.3, seed=32)
    col = lerp(np.array([132, 126, 116.0]), np.array([182, 176, 164.0]), pores * 0.4 + stains * 0.6)
    drips = np.clip(fbm(n, falloff=1.0, seed=33) * (np.linspace(1.2, 0.2, n)[:, None]) - 0.55, 0, 1)
    col *= (1 - drips * 0.8)[..., None]
    save(col, "duracrete.png")
    save(normal_from(pores * 0.7 + stains * 0.3, 3.0), "duracrete_n.png")
    # республиканская белая броня-панели с полосой
    col = lerp(np.array([196, 198, 200.0]), np.array([236, 236, 234.0]), dirt * 0.5)
    stripe = (np.abs(v % 1.0 - 0.82) < 0.04)
    col[stripe] = np.array([186, 70, 40.0])
    col *= (0.7 + 0.3 * groove)[..., None]
    save(col, "republic_panel.png")
    save(normal_from(groove * 0.7 + dirt * 0.3, 6.0), "republic_panel_n.png")
    # кора и хвоя
    x = np.linspace(0, 1, 512)
    bark_h = fbm(512, falloff=1.2, seed=41)
    stretch = np.repeat(bark_h[:, ::8], 8, axis=1)[:, :512]
    bark = lerp(np.array([52, 40, 30.0]), np.array([96, 78, 58.0]), stretch)
    save(bark, "bark.png")
    save(normal_from(stretch, 8.0), "bark_n.png")
    leaves = lerp(np.array([30, 52, 26.0]), np.array([62, 92, 40.0]), fbm(512, falloff=0.8, seed=42))
    save(leaves, "leaves.png")
    # трава-деталь (RGBA)
    g = np.zeros((256, 256, 4))
    r = random.Random(5)
    for _ in range(46):
        x0 = r.randint(20, 236)
        hgt = r.randint(140, 250)
        lean = r.uniform(-0.35, 0.35)
        w = r.uniform(3, 6)
        c = np.array([r.randint(70, 110), r.randint(100, 140), r.randint(40, 60)])
        for yy in range(hgt):
            t_ = yy / hgt
            xc = x0 + lean * yy
            half = w * (1 - t_)
            for xx in range(int(xc - half), int(xc + half) + 1):
                if 0 <= xx < 256:
                    g[255 - yy, xx, :3] = c * (0.7 + 0.3 * t_)
                    g[255 - yy, xx, 3] = 255
    save(g, "grass_blades.png")


# ============================================================================ меши
def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


MATS = {}


def mat(name):
    if name in MATS:
        return MATS[name]
    m = bpy.data.materials.new(name)
    MATS[name] = m
    return m


def obj_from_bm(bm, name, material):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    me.materials.append(mat(material))
    return o


def box(name, size, pos=(0, 0, 0), material="imperial_panel", bevel=0.06, rot=(0, 0, 0), taper=None):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
        if taper and v.co.z > 0:
            v.co.x *= taper[0]
            v.co.y *= taper[1]
    o = obj_from_bm(bm, name, material)
    o.location = Vector(pos) + Vector((0, 0, size[2] / 2))
    o.rotation_euler = rot
    if bevel > 0:
        md = o.modifiers.new("b", "BEVEL")
        md.width = bevel
        md.segments = 2
        md.limit_method = "ANGLE"
    return o


def cyl(name, r, h, pos=(0, 0, 0), material="imperial_panel", verts=24, r2=None, bevel=0.04):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=verts, radius1=r, radius2=r2 if r2 is not None else r, depth=h)
    o = obj_from_bm(bm, name, material)
    o.location = Vector(pos) + Vector((0, 0, h / 2))
    if bevel > 0:
        md = o.modifiers.new("b", "BEVEL")
        md.width = bevel
        md.segments = 2
        md.limit_method = "ANGLE"
    return o


def sphere(name, r, pos, material, seg=24, squash=1.0):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=seg // 2, radius=r)
    for v in bm.verts:
        v.co.z *= squash
    o = obj_from_bm(bm, name, material)
    o.location = pos
    return o


def join(objs, name):
    for o in bpy.context.selected_objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        for m in list(o.modifiers):
            bpy.ops.object.modifier_apply(modifier=m.name)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    # UV: кубическая проекция, 1 повтор текстуры (4x4 панели) на 6 м
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.cube_project(cube_size=6.0, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    for p in o.data.polygons:
        p.use_smooth = False
    return o


def export(o, name):
    os.makedirs(OUT, exist_ok=True)
    for x in bpy.context.selected_objects:
        x.select_set(False)
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + ".fbx"), use_selection=True, object_types={"MESH"},
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE",
                             path_mode="STRIP")


def build(name, fn):
    reset()
    MATS.clear()
    parts = fn()
    o = join(parts, name)
    export(o, name)
    return {"name": name, "materials": [m.name for m in o.data.materials],
            "size": [round(x, 2) for x in o.dimensions]}


# ---------------------------------------------------------------- постройки Империи
def imp_wall():
    P = [box("w", (10, 1.2, 4.2), material="imperial_panel", taper=(1, 0.75))]
    P.append(box("cap", (10.2, 1.0, 0.35), (0, 0, 4.2), material="imperial_light"))
    for x in (-4.6, 0, 4.6):
        P.append(box("b", (0.9, 2.0, 3.4), (x, -0.4, 0), material="imperial_panel", taper=(0.7, 0.4)))
    P.append(box("strip", (9.6, 0.05, 0.12), (0, -0.62, 3.4), material="glow_red", bevel=0))
    return P


def imp_barrier():
    return [box("bar", (4, 0.9, 1.15), material="imperial_panel", taper=(0.95, 0.45)),
            box("cap", (4.05, 0.35, 0.12), (0, 0, 1.12), material="imperial_light", bevel=0.02)]


def imp_tower():
    P = [box("base", (4.5, 4.5, 9), material="imperial_panel", taper=(0.75, 0.75))]
    P.append(box("deck", (6.5, 6.5, 0.6), (0, 0, 9), material="imperial_light"))
    P.append(box("cab", (4.2, 4.2, 2.6), (0, 0, 9.6), material="imperial_panel"))
    P.append(box("win", (4.25, 4.25, 0.4), (0, 0, 10.9), material="glow_red", bevel=0))
    P.append(box("roof", (5.0, 5.0, 0.5), (0, 0, 12.2), material="imperial_light", taper=(0.7, 0.7)))
    P.append(cyl("ant", 0.08, 4, (1.6, 1.6, 12.6), material="imperial_light", verts=8, bevel=0))
    return P


def imp_gate():
    P = [box("l", (3.5, 2.4, 7), (-5.25, 0, 0), material="imperial_panel", taper=(0.85, 0.7)),
         box("r", (3.5, 2.4, 7), (5.25, 0, 0), material="imperial_panel", taper=(0.85, 0.7)),
         box("top", (14, 2.6, 1.8), (0, 0, 6.2), material="imperial_light"),
         box("sign", (6.6, 0.1, 0.5), (0, -1.35, 6.8), material="glow_red", bevel=0)]
    return P


def imp_bunker():
    P = [box("body", (12, 8, 4), material="imperial_panel", taper=(0.85, 0.7))]
    P.append(box("top", (9, 4.8, 0.6), (0, 0.4, 4), material="imperial_light"))
    P.append(box("slit", (8, 0.1, 0.35), (0, -3.6, 2.6), material="glow_red", bevel=0))
    P.append(box("door", (2.4, 0.2, 2.6), (0, 4.0, 0), material="imperial_light", bevel=0.02))
    return P


def command_center():
    P = [box("t1", (32, 24, 7), material="imperial_panel", taper=(0.92, 0.9))]
    P.append(box("t2", (24, 18, 6), (0, 0, 7), material="imperial_light", taper=(0.88, 0.85)))
    P.append(box("t3", (14, 10, 6), (0, 0, 13), material="imperial_panel", taper=(0.8, 0.8)))
    P.append(box("bridge", (10, 2, 1.2), (0, -5.4, 16.5), material="glow_red", bevel=0))
    P.append(cyl("mast", 0.5, 10, (0, 0, 19), material="imperial_light", verts=12))
    P.append(sphere("dish", 3.0, (4, 2, 21), "imperial_light", seg=20, squash=0.35))
    for sx in (-1, 1):
        P.append(box("fin", (1.2, 18, 9), (sx * 16.5, 0, 0), material="imperial_panel", taper=(0.4, 0.8)))
    P.append(box("door", (8, 0.4, 4.5), (0, -12.1, 0), material="imperial_light"))
    P.append(box("lamp", (7.6, 0.1, 0.3), (0, -12.35, 4.7), material="glow_red", bevel=0))
    return P


def shield_generator():
    P = [cyl("base", 5, 2.2, material="imperial_panel", verts=32)]
    P.append(cyl("ring", 4.2, 1.2, (0, 0, 2.2), material="imperial_light", verts=32, r2=3.4))
    P.append(cyl("core", 1.8, 7, (0, 0, 3.4), material="glow_blue", verts=24, r2=1.2, bevel=0))
    for i in range(4):
        a = i * math.pi / 2 + math.pi / 4
        P.append(box("pylon", (1.0, 1.0, 9), (math.cos(a) * 3.3, math.sin(a) * 3.3, 2.2), material="imperial_panel", taper=(0.4, 0.4)))
    P.append(sphere("cap", 1.6, (0, 0, 10.8), "imperial_light", seg=16, squash=0.6))
    return P


def turret_platform():
    return [cyl("plat", 2.2, 0.8, material="imperial_panel", verts=16),
            box("sand", (4.6, 0.7, 1.2), (0, -2.1, 0), material="imperial_panel", taper=(0.9, 0.5))]


def holo_post_imp():
    P = [cyl("pad", 3.2, 0.3, material="imperial_light", verts=8)]
    P.append(box("console", (2.2, 1.2, 1.1), (0, 0, 0.3), material="imperial_panel", taper=(0.8, 0.6)))
    P.append(cyl("emit", 0.35, 0.3, (0, 0, 1.4), material="glow_white", verts=12, bevel=0))
    P.append(cyl("mast", 0.12, 6, (1.4, 0, 0.3), material="imperial_light", verts=8, bevel=0))
    P.append(box("flag", (0.05, 1.2, 1.8), (1.4, 0.65, 4.2), material="glow_white", bevel=0))
    return P


# ---------------------------------------------------------------- Республика и общее
def rep_barricade():
    P = []
    for i in range(5):
        a = (i - 2) * 0.22
        P.append(box("seg", (0.95, 0.6, 1.3), (math.sin(a) * 4, -math.cos(a) * 4 + 4, 0), material="republic_panel", rot=(0, 0, a), taper=(0.9, 0.6)))
    return P


def rep_bunker():
    P = [box("body", (8, 6, 3.2), material="republic_panel")]
    P.append(cyl("roof", 3.1, 8, (0, 0, 3.2), material="republic_panel", verts=20))
    P[-1].rotation_euler = (0, math.pi / 2, 0)
    P[-1].location = (0, 0, 3.2)
    P.append(box("door", (2, 0.2, 2.4), (0, -3.05, 0), material="duracrete", bevel=0.02))
    P.append(box("light", (6, 0.1, 0.15), (0, -3.1, 2.9), material="glow_blue", bevel=0))
    return P


def landing_pad():
    bm = bmesh.new()
    bmesh.ops.create_circle(bm, cap_ends=True, segments=6, radius=12)
    bmesh.ops.extrude_face_region(bm, geom=bm.faces[:])
    for v in bm.verts:
        if v.co.z == 0 and any(f for f in v.link_faces):
            pass
    o = obj_from_bm(bm, "pad", "duracrete")
    o.scale = (1, 1, 1)
    sol = o.modifiers.new("s", "SOLIDIFY")
    sol.thickness = 0.5
    P = [o]
    for i in range(6):
        a = i * math.pi / 3 + math.pi / 6
        P.append(box("lamp", (0.6, 0.6, 0.25), (math.cos(a) * 10.5, math.sin(a) * 10.5, 0), material="glow_blue", bevel=0))
    P.append(box("mark", (8, 0.6, 0.06), (0, 0, 0.02), material="glow_white", bevel=0))
    P.append(box("mark2", (0.6, 8, 0.06), (0, 0, 0.02), material="glow_white", bevel=0))
    return P


def rep_post():
    P = [cyl("pad", 3.0, 0.3, material="republic_panel", verts=16)]
    P.append(box("table", (2.0, 2.0, 1.0), (0, 0, 0.3), material="duracrete", taper=(0.7, 0.7)))
    P.append(cyl("emit", 0.4, 0.2, (0, 0, 1.3), material="glow_white", verts=12, bevel=0))
    P.append(cyl("mast", 0.1, 6, (1.5, 0, 0.3), material="republic_panel", verts=8, bevel=0))
    P.append(box("flag", (0.05, 1.2, 1.8), (1.5, 0.65, 4.2), material="glow_white", bevel=0))
    return P


def crate():
    return [box("c", (1.3, 1.3, 1.2), material="imperial_light", bevel=0.08)]


def container():
    P = [box("c", (6, 2.4, 2.6), material="imperial_panel", bevel=0.05)]
    for x in (-2.9, 2.9):
        P.append(box("rim", (0.25, 2.5, 2.7), (x, 0, -0.05), material="imperial_light", bevel=0.02))
    return P


def ruin():
    P = []
    r = random.Random(7)
    for i in range(6):
        h = r.uniform(1.2, 4.0)
        P.append(box("chunk", (r.uniform(1.2, 2.6), r.uniform(0.6, 1.2), h), (i * 1.6 - 4, r.uniform(-0.5, 0.5), 0),
                     material="duracrete", rot=(r.uniform(-0.1, 0.1), r.uniform(-0.15, 0.15), r.uniform(-0.3, 0.3))))
    return P


# ---------------------------------------------------------------- природа
def pine(seed):
    r = random.Random(seed)
    h = r.uniform(9, 15)
    P = [cyl("trunk", 0.28, h * 0.5, material="bark", verts=8, r2=0.18, bevel=0)]
    tiers = 5
    for i in range(tiers):
        t = i / tiers
        rad = (1 - t) * r.uniform(2.6, 3.4) + 0.4
        z = h * (0.22 + t * 0.62)
        P.append(cyl("tier", rad, h * 0.3, (r.uniform(-0.1, 0.1), r.uniform(-0.1, 0.1), z), material="leaves", verts=9, r2=0.15, bevel=0))
    return P


def broadleaf(seed):
    r = random.Random(seed)
    h = r.uniform(6, 10)
    P = [cyl("trunk", 0.32, h * 0.6, material="bark", verts=8, r2=0.22, bevel=0)]
    for i in range(r.randint(4, 6)):
        P.append(sphere("crown", r.uniform(1.6, 2.6), (r.uniform(-1.4, 1.4), r.uniform(-1.4, 1.4), h * r.uniform(0.6, 0.9)), "leaves", seg=10, squash=0.8))
    return P


def rock(seed):
    r = random.Random(seed)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=3, radius=1.0)
    off = [Vector((r.uniform(-1, 1), r.uniform(-1, 1), r.uniform(-1, 1))) for _ in range(6)]
    for v in bm.verts:
        d = 0.0
        for o in off:
            d += math.sin(v.co.dot(o) * 3.1 + seed) * 0.08
        v.co *= 1.0 + d
        v.co.z = max(v.co.z * r.uniform(0.55, 0.9), -0.2)
    o = obj_from_bm(bm, "rock", "rock")
    o.scale = (r.uniform(1.0, 1.6), r.uniform(0.8, 1.3), 1.0)
    return [o]


def main():
    ground_textures()
    material_textures()
    items = []
    builds = {
        "ImpWall": imp_wall, "ImpBarrier": imp_barrier, "ImpTower": imp_tower, "ImpGate": imp_gate, "ImpBunker": imp_bunker,
        "CommandCenter": command_center, "ShieldGenerator": shield_generator, "TurretPlatform": turret_platform,
        "ImpPost": holo_post_imp, "RepBarricade": rep_barricade, "RepBunker": rep_bunker, "LandingPad": landing_pad,
        "RepPost": rep_post, "Crate": crate, "Container": container, "Ruin": ruin,
    }
    for i in range(3):
        builds[f"Pine{i}"] = (lambda s=i: pine(s + 10))
        builds[f"Tree{i}"] = (lambda s=i: broadleaf(s + 20))
    for i in range(4):
        builds[f"Rock{i}"] = (lambda s=i: rock(s + 30))
    for name, fn in builds.items():
        print("===", name)
        items.append(build(name, fn))
    textures = {
        "imperial_panel": ["imperial_panel.png", "imperial_panel_n.png"], "imperial_light": ["imperial_light.png", "imperial_panel_n.png"],
        "duracrete": ["duracrete.png", "duracrete_n.png"], "republic_panel": ["republic_panel.png", "republic_panel_n.png"],
        "bark": ["bark.png", "bark_n.png"], "leaves": ["leaves.png", None], "rock": ["ground_rock.png", "ground_rock_n.png"],
    }
    with open(os.path.join(OUT, "manifest.json"), "w", encoding="utf-8") as f:
        json.dump({"items": items, "textures": textures}, f, ensure_ascii=False, indent=1)


if __name__ == "__main__":
    main()
