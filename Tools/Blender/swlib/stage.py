"""Сцена для рендеров: пол ангара в духе имперской базы, свет, камера."""

import math

import bpy
from mathutils import Vector

from . import kit as K


def floor_material():
    m = bpy.data.materials.new("Stage_Floor")
    nt, n, l, bsdf, coord = K._nodes(m)
    br = n.new("ShaderNodeTexBrick")
    br.inputs["Scale"].default_value = 1.0
    br.inputs["Mortar Size"].default_value = 0.012
    br.inputs["Brick Width"].default_value = 1.2
    br.inputs["Row Height"].default_value = 1.2
    br.offset = 0.0
    br.inputs["Color1"].default_value = (0.05, 0.052, 0.056, 1)
    br.inputs["Color2"].default_value = (0.07, 0.072, 0.077, 1)
    br.inputs["Mortar"].default_value = (0.01, 0.01, 0.012, 1)
    l.new(coord.outputs["Object"], br.inputs["Vector"])
    nz = K._noise(n, l, coord, 3.0)
    col = K._mix(n, l, K._range(n, l, nz.outputs["Fac"], 0.3, 0.7, 0.0, 0.35), br.outputs["Color"], (0.03, 0.03, 0.032))
    l.new(col, bsdf.inputs["Base Color"])
    l.new(K._range(n, l, nz.outputs["Fac"], 0.3, 0.7, 0.18, 0.45), bsdf.inputs["Roughness"])
    bsdf.inputs["Metallic"].default_value = 0.6
    K._bump(n, l, bsdf, br.outputs["Fac"], 0.25)
    return m


def setup(w, h, samples=64, bg=(0.004, 0.005, 0.008), floor=True, wall=True):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.cycles.use_denoising = True
    scene.cycles.max_bounces = 6
    scene.render.resolution_x = w
    scene.render.resolution_y = h
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.look = "AgX - Medium High Contrast"
    world = bpy.data.worlds.new("Space")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (*bg, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 1.0
    if floor:
        bpy.ops.mesh.primitive_plane_add(size=40)
        bpy.context.active_object.data.materials.append(floor_material())
    if wall:
        # стена с подсвеченными панелями
        bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 6, 0), rotation=(math.pi / 2, 0, 0))
        bpy.context.active_object.data.materials.append(K.mat_flat("Stage_Wall", (0.02, 0.022, 0.026), rough=0.5, metallic=0.4))
        for i in range(-4, 5):
            bpy.ops.mesh.primitive_plane_add(size=1, location=(i * 2.2, 5.98, 2.6), rotation=(math.pi / 2, 0, 0))
            o = bpy.context.active_object
            o.scale = (1.6, 0.06, 1)
            o.data.materials.append(K.mat_flat("Stage_Strip", (0.6, 0.7, 0.9), emission=(0.6, 0.75, 1.0), strength=6.0))
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    return cam


def light(name, energy, loc, target=(0, 0, 1.1), color=(1, 1, 1), size=2.0):
    d = bpy.data.lights.new(name, "AREA")
    d.energy = energy
    d.color = color
    d.size = size
    o = bpy.data.objects.new(name, d)
    o.location = loc
    o.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.collection.objects.link(o)
    return o


def studio(target=(0, 0, 1.1), scale=1.0):
    t = Vector(target)
    light("Key", 520 * scale, t + Vector((-2.6, -3.0, 2.2)), t, (1.0, 0.93, 0.85), 2.5)
    light("Rim", 700 * scale, t + Vector((2.8, 2.6, 1.8)), t, (0.55, 0.7, 1.0), 1.5)
    light("Rim2", 350 * scale, t + Vector((-2.8, 2.2, 1.5)), t, (1.0, 0.55, 0.45), 1.5)
    light("Fill", 120 * scale, t + Vector((2.8, -2.8, 0.2)), t, (0.7, 0.8, 1.0), 3.0)


def aim(cam, loc, target, lens=50):
    cam.data.lens = lens
    cam.location = loc
    cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()


def render(path):
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
