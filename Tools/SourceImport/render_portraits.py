"""
Портреты юнитов для интерфейса (казарма, набор) и общий снимок строя.

    python Tools/SourceImport/render_portraits.py [-- --only CloneTrooper]

Берёт готовые Assets/_Project/Art/Workshop/<Юнит>/<Юнит>.fbx, ставит позу и рендерит (Workbench, быстро):
<Юнит>_portrait.png (256x320) и docs/renders/workshop/lineup_*.png.
"""

import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Workshop")
DOCS = os.path.join(ROOT, "docs", "renders", "workshop")

import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def setup(w, h):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "TEXTURE"
    sc.display.shading.show_cavity = True
    sc.display.shading.show_shadows = True
    sc.render.resolution_x, sc.render.resolution_y = w, h
    sc.render.film_transparent = True
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    return cam


def aim(cam, pos, target, lens):
    cam.location = pos
    cam.rotation_euler = (Vector(target) - Vector(pos)).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = lens


def load(name, x=0.0, clip="AtEase", frame=20):
    path = os.path.join(OUT, name, name + ".fbx")
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    objs = [o for o in bpy.data.objects if o not in before]
    arm = next((o for o in objs if o.type == "ARMATURE"), None)
    # текстуры: FBX без путей — подключаем PNG из папки юнита
    for o in objs:
        if o.type != "MESH":
            continue
        for slot in o.material_slots:
            m = slot.material
            if not m or not m.use_nodes:
                continue
            png = os.path.join(OUT, name, m.name.split(".")[0] + "_albedo.png")
            if not os.path.exists(png):
                continue
            nt = m.node_tree
            bsdf = nt.nodes.get("Principled BSDF")
            tex = nt.nodes.new("ShaderNodeTexImage")
            tex.image = bpy.data.images.load(png)
            if bsdf:
                nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    if arm:
        holder = bpy.data.objects.new(name + "_Holder", None)
        bpy.context.scene.collection.objects.link(holder)
        holder.location.x = x
        for o in objs:
            if o.parent is None:
                o.parent = holder
        act = bpy.data.actions.get(clip) or next((a for a in bpy.data.actions if a.name.endswith(clip)), None)
        if act:
            ad = arm.animation_data or arm.animation_data_create()
            ad.action = act
            if hasattr(ad, "action_slot") and getattr(act, "slots", None):
                ad.action_slot = act.slots[0]
        bpy.context.scene.frame_set(frame)
    return objs


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    only = argv[argv.index("--only") + 1].split(",") if "--only" in argv else []
    units = json.load(open(os.path.join(OUT, "units.json"), encoding="utf-8"))["units"]
    os.makedirs(DOCS, exist_ok=True)
    for u in units:
        name = u["name"]
        if (only and name not in only) or "--lineup" in argv:
            continue
        reset()
        cam = setup(256, 320)
        load(name, clip="AtEase", frame=30)
        h = u.get("height", 1.83)
        aim(cam, (-1.0, -3.2, h * 0.8), (0, 0, h * 0.56), 44)
        bpy.context.scene.render.filepath = os.path.join(OUT, name, name + "_portrait.png")
        bpy.ops.render.render(write_still=True)
        print("portrait", name)
    if only:
        return
    for team in ("Republic", "Empire"):
        reset()
        cam = setup(2400, 760)
        members = [u for u in units if u["team"] == team]
        gap = 1.15
        for i, u in enumerate(members):
            clips = ["Attention", "AtEase", "PortArms", "Aim", "Idle"]
            load(u["name"], x=(i - (len(members) - 1) / 2) * gap, clip=clips[i % len(clips)], frame=15)
        width = len(members) * gap
        aim(cam, (0, -width * 1.3, 1.6), (0, 0, 0.95), 50)
        bpy.ops.mesh.primitive_plane_add(size=60)
        bpy.context.scene.render.film_transparent = False
        bpy.context.scene.render.filepath = os.path.join(DOCS, f"lineup_{team}.png")
        bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    main()
