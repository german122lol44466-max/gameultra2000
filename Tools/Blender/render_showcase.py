"""
Общие рендеры: все персонажи вместе (состав и экшен-сцена) и витрина оружия.

    python Tools/Blender/render_showcase.py -- [--samples 64] [--only lineup,action,weapons]

Результат: docs/renders/lineup.png, action.png, weapons.png
"""

import argparse
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

import build_characters as B  # noqa: E402
from swlib import anims, character, stage  # noqa: E402
from swlib import kit as K  # noqa: E402


def args_():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--samples", type=int, default=64)
    p.add_argument("--only", default="lineup,action,weapons")
    return p.parse_args(argv)


def build_cast(poses):
    """poses: [(имя, клип, доля времени, x, y, поворот)] — строит всех в одной сцене."""
    rigs = []
    first = True
    for name, clip, t, x, y, yaw in poses:
        spec = B.CHARACTERS[name]
        rig = character.build(name, spec["body"], spec["weapon"], cape=spec["cape"], height=spec["height"], reset=first)
        first = False
        frames = B.clips_for(spec)[clip]
        p = anims.to_pose(frames[min(len(frames) - 1, int(t * (len(frames) - 1)))])
        rig.apply(p)
        rig.obj.location = (x, y, 0)
        rig.obj.rotation_euler = (0, 0, math.radians(yaw))
        rigs.append(rig)
    bpy.context.view_layer.update()
    return rigs


def lightning(start, end, seed=1, branches=3):
    """Молния Силы — светящиеся ломаные трубки (только для рендера)."""
    import random
    rnd = random.Random(seed)
    mat = K.mat_flat("FX_Lightning", (0.6, 0.75, 1.0), emission=(0.55, 0.7, 1.0), strength=40.0)
    for b in range(branches):
        pts = []
        n = 14
        for i in range(n + 1):
            t = i / n
            p = start.lerp(end, t)
            if 0 < i < n:
                p += Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1))) * 0.2 * math.sin(math.pi * t)
            pts.append(p)
        cu = bpy.data.curves.new("bolt", "CURVE")
        cu.dimensions = "3D"
        cu.bevel_depth = 0.006
        sp = cu.splines.new("POLY")
        sp.points.add(len(pts) - 1)
        for i, p in enumerate(pts):
            sp.points[i].co = (*p, 1)
        o = bpy.data.objects.new("bolt", cu)
        o.data.materials.append(mat)
        bpy.context.scene.collection.objects.link(o)
    lt = bpy.data.lights.new("bolt_light", "POINT")
    lt.energy = 300
    lt.color = (0.55, 0.7, 1.0)
    lo = bpy.data.objects.new("bolt_light", lt)
    lo.location = start.lerp(end, 0.4)
    bpy.context.scene.collection.objects.link(lo)


def blaster_bolt(a, d):
    mat = K.mat_flat("FX_Bolt", (1, 0.2, 0.1), emission=(1, 0.12, 0.05), strength=60.0)
    bpy.ops.mesh.primitive_cylinder_add(radius=0.015, depth=0.5, location=a)
    o = bpy.context.active_object
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = d.to_track_quat("Z", "Y")
    o.data.materials.append(mat)


def blade_lights(rigs):
    """Красный свет от каждого зажжённого клинка (Cycles: эмиссия + точечный свет)."""
    for r in rigs:
        for bn in ("Blade.R", "Blade.R2"):
            pb = r.obj.pose.bones.get(bn)
            if not pb or pb.scale.y < 0.5:
                continue
            m = r.obj.matrix_world @ pb.matrix
            lt = bpy.data.lights.new("blade", "POINT")
            lt.energy = 60
            lt.color = (1, 0.1, 0.05)
            lt.shadow_soft_size = 0.4
            o = bpy.data.objects.new("blade", lt)
            o.location = m @ Vector((0, 0.5, 0))
            bpy.context.scene.collection.objects.link(o)


def lineup(a):
    names = list(B.CHARACTERS)
    order = ["HeavyTrooper", "Stormtrooper", "DarthMaul", "DarthVader", "DarthSidious", "CountDooku", "TrooperCommander"]
    poses = [(n, "Idle", 0.2, (i - 3) * 1.15, 0.25 * abs(i - 3), 0) for i, n in enumerate(order) if n in names]
    rigs = build_cast(poses)
    blade_lights(rigs)
    cam = stage.setup(2400, 1100, samples=a.samples)
    stage.studio((0, 0, 1.1), scale=1.6)
    stage.aim(cam, (0, -8.6, 2.0), (0, 0, 1.0), 42)
    stage.render(os.path.join(B.RENDERS, "lineup.png"))


def action(a):
    poses = [
        ("DarthVader", "ForceChoke", 0.4, 0.2, 0.0, 180),
        ("DarthMaul", "Attack3", 0.35, -1.9, 0.6, 165),
        ("DarthSidious", "ForceLightning", 0.4, 1.9, 0.5, 195),
        ("CountDooku", "Attack1", 0.45, -0.9, -1.0, 175),
        ("Stormtrooper", "Fire", 0.15, -0.8, 4.4, -10),
        ("HeavyTrooper", "Aim", 0.3, 1.4, 4.8, 15),
        ("TrooperCommander", "Hit", 0.2, 0.4, 3.6, 0),
    ]
    rigs = build_cast(poses)
    blade_lights(rigs)
    # молния Сидиуса — из его левой руки вперёд
    sid = rigs[2]
    hand = sid.obj.matrix_world @ sid.obj.pose.bones["Hand.L"].tail
    fwd = (sid.obj.matrix_world.to_3x3() @ Vector((0, -1, 0))).normalized()
    lightning(hand, hand + fwd * 2.6 + Vector((0, 0, -0.3)), seed=4, branches=4)
    # болт бластера штурмовика
    st = rigs[4]
    wm = st.obj.matrix_world @ st.obj.pose.bones["Weapon.R"].matrix
    d = (wm.to_3x3() @ Vector((0, 1, 0))).normalized()
    blaster_bolt(wm @ Vector((0, 1.4, 0.06)), d)
    cam = stage.setup(2400, 1300, samples=a.samples)
    stage.studio((0.0, 2.0, 1.1), scale=1.4)
    stage.aim(cam, (-6.8, 3.2, 2.1), (0.3, 1.9, 0.95), 30)
    stage.render(os.path.join(B.RENDERS, "action.png"))


def weapons(a):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    from swlib import mats, weapons as W
    M = mats.palette()
    k = K.Kit(M)
    names = ["E11", "DLT19", "Saber_Vader", "Saber_Maul", "Saber_Sidious", "Saber_Dooku"]
    for i, wn in enumerate(names):
        # в ряд сверху вниз, ствол/клинок вправо
        base = (Vector((0, 0, 0.75 - i * 0.28)))
        from mathutils import Matrix
        xf = Matrix.Translation(base + Vector(({"Saber_Maul": 0.0, "E11": -0.4, "DLT19": -0.45}.get(wn, -0.55), 0, 0))) @ Matrix.Rotation(math.radians(-90), 4, "Z")
        k.xform = xf
        grip = W.WEAPONS[wn](k)
        for bn, bm, ln in grip["blades"]:
            k.xform = xf @ bm
            W.blade(k, bn, ln * (0.75 if wn != "Saber_Maul" else 0.6), glow="glow")
    k.xform = None
    cam = stage.setup(1800, 1400, samples=a.samples, wall=False, floor=False)
    stage.studio((0, 0, 0.1), scale=0.5)
    stage.aim(cam, (0.05, -3.9, 0.05), (0.05, 0, 0.02), 44)
    stage.render(os.path.join(B.RENDERS, "weapons.png"))


def main():
    a = args_()
    os.makedirs(B.RENDERS, exist_ok=True)
    only = a.only.split(",")
    if "lineup" in only:
        lineup(a)
    if "action" in only:
        action(a)
    if "weapons" in only:
        weapons(a)


if __name__ == "__main__":
    main()
