"""
Сборка персонажей v2 (реалистичное тело MakeHuman, броня/одежда по телу, ткань с физикой, анимации v2).

    python Tools/Blender/build_v2.py -- [--only Stormtrooper,DarthVader] [--strips] [--clips Aim,Walk] [--portrait]

Результат — Assets/_Project/Art/CharactersV2/<Имя>.fbx/.glb и manifest_v2.json (+ рендеры в docs/renders/v2).
"""

import argparse
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

from swlib import anims2, chars2, stage  # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "CharactersV2")
RENDERS = os.path.join(ROOT, "docs", "renders", "v2")

# кости Unity Humanoid -> имена нашего скелета
HUMANOID = {
    "Hips": "Hips", "Spine": "Spine", "Chest": "Chest", "UpperChest": "UpperChest", "Neck": "Neck", "Head": "Head",
    "Jaw": "Jaw", "LeftEye": "Eye.L", "RightEye": "Eye.R",
    "LeftShoulder": "Shoulder.L", "LeftUpperArm": "UpperArm.L", "LeftLowerArm": "LowerArm.L", "LeftHand": "Hand.L",
    "RightShoulder": "Shoulder.R", "RightUpperArm": "UpperArm.R", "RightLowerArm": "LowerArm.R", "RightHand": "Hand.R",
    "LeftUpperLeg": "UpperLeg.L", "LeftLowerLeg": "LowerLeg.L", "LeftFoot": "Foot.L", "LeftToes": "Toes.L",
    "RightUpperLeg": "UpperLeg.R", "RightLowerLeg": "LowerLeg.R", "RightFoot": "Foot.R", "RightToes": "Toes.R",
}
for side, s in (("Left", "L"), ("Right", "R")):
    for f, u in (("Thumb", "Thumb"), ("Index", "Index"), ("Middle", "Middle"), ("Ring", "Ring"), ("Little", "Little")):
        for k, ph in ((1, "Proximal"), (2, "Intermediate"), (3, "Distal")):
            HUMANOID[f"{side}{f}{ph}"] = f"{u}{k}.{s}"


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--only", default="")
    p.add_argument("--strips", action="store_true")
    p.add_argument("--portrait", action="store_true")
    p.add_argument("--clips", default="")
    p.add_argument("--samples", type=int, default=24)
    p.add_argument("--no-export", action="store_true")
    p.add_argument("--vehicles", action="store_true")
    return p.parse_args(argv)


def strips(b, name, spec, args):
    from PIL import Image, ImageDraw, ImageFont
    os.makedirs(RENDERS, exist_ok=True)
    s = b.height / 1.83
    cam = stage.setup(360, 480, samples=max(8, args.samples // 2))
    stage.studio((0, 0, 1.0 * s))
    want = [c for c in args.clips.split(",") if c] or list(b.rig.frames)
    tmp = os.path.join(RENDERS, f"_tmp_{name}.png")
    try:
        font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 22)
    except OSError:
        font = ImageFont.load_default()
    for clip in want:
        frames = b.rig.frames.get(clip)
        if not frames:
            continue
        n = 6
        idx = [round(i * (len(frames) - 1) / (n - 1)) for i in range(n)]
        lying = clip.startswith("Death")
        stage.aim(cam, (-1.7 * s, -3.3 * s, (1.6 if lying else 1.3) * s), (0, 0.2 if lying else 0, (0.5 if lying else 0.95) * s), 38)
        tiles = []
        for f in idx:
            b.rig.pose_baked(frames[f])
            stage.render(tmp)
            tiles.append(Image.open(tmp).convert("RGB"))
        W, H = tiles[0].size
        sheet = Image.new("RGB", (W * n, H + 40), (8, 9, 12))
        for i, t in enumerate(tiles):
            sheet.paste(t, (i * W, 40))
        ImageDraw.Draw(sheet).text((12, 8), f"{spec['title']} — {clip}   ({len(frames) - 1} кадров)", fill=(230, 230, 235), font=font)
        sheet.save(os.path.join(RENDERS, f"{name}_{clip}.png"))
    if os.path.exists(tmp):
        os.remove(tmp)


def portrait(b, name, spec, args):
    os.makedirs(RENDERS, exist_ok=True)
    s = b.height / 1.83
    idle = b.rig.frames.get("Idle") or next(iter(b.rig.frames.values()))
    b.rig.pose_baked(idle[20 if len(idle) > 20 else 0])
    cam = stage.setup(1000, 1300, samples=args.samples)
    stage.studio((0, 0, 1.0 * s))
    stage.aim(cam, (-1.2 * s, -3.3 * s, 1.45 * s), (0, 0, 0.98 * s), 40)
    stage.render(os.path.join(RENDERS, f"{name}.png"))
    # крупный план — по фактическому положению головы в позе (в стойках голова смещена от центра)
    bpy.context.view_layer.update()
    arm = b.rig.obj
    hb = arm.pose.bones["Head"]
    hp = arm.matrix_world @ hb.head
    tip = arm.matrix_world @ hb.tail
    t = (hp + tip) / 2
    pb = arm.pose.bones
    if "Eye.L" in pb and "Eye.R" in pb:
        fwd = arm.matrix_world @ ((pb["Eye.L"].head + pb["Eye.R"].head) / 2) - t   # глаза впереди центра головы
    else:
        fwd = Vector((0, -1, 0))
    fwd.z = 0
    if fwd.length < 1e-3:
        fwd = Vector((0, -1, 0))
    fwd.normalize()
    side = Vector((-fwd.y, fwd.x, 0))
    stage.aim(cam, t + fwd * 0.85 * s - side * 0.33 * s + Vector((0, 0, 0.06 * s)), t, 55)
    bpy.context.scene.render.resolution_x = bpy.context.scene.render.resolution_y = 1000
    stage.render(os.path.join(RENDERS, f"{name}_face.png"))


def export(b, name, spec):
    os.makedirs(OUT, exist_ok=True)
    acts = b.rig.write_actions()
    arm = b.rig.obj
    for o in bpy.context.selected_objects:
        o.select_set(False)
    arm.select_set(True)
    for m in b.meshes:
        m.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT, f"{name}.fbx"), use_selection=True, object_types={"ARMATURE", "MESH"},
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", add_leaf_bones=False,
        use_armature_deform_only=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=1.0, mesh_smooth_type="FACE", path_mode="STRIP",
        use_mesh_modifiers=True)
    if os.environ.get("SW_GLB"):
        _glb(arm, acts, name)
    return _meta(b, name, spec, acts)


def _glb(arm, acts, name):
    arm.animation_data.action = None
    for a in acts:
        tr = arm.animation_data.nla_tracks.new()
        tr.name = a.name
        st = tr.strips.new(a.name, 0, a)
        st.name = a.name
        tr.mute = True
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, f"{name}.glb"), use_selection=True, export_animations=True,
                              export_animation_mode="NLA_TRACKS", export_force_sampling=True, export_yup=True,
                              export_apply=True, export_skins=True, export_morph=False)


def _meta(b, name, spec, acts):
    used = {}
    for m in b.meshes:
        for slot in m.material_slots:
            mt = slot.material
            if mt and "sw" in mt:
                used[mt.name] = dict(mt["sw"].to_dict())
    return {
        "name": name, "title": spec["title"], "side": spec["side"], "role": spec["role"], "style": spec.get("style", "rifle"),
        "weapon": spec["weapon"], "height": spec["height"], "humanoid": HUMANOID,
        "clips": [{"name": a.name, "frames": int(a.frame_range[1]), "loop": a.name in anims2.LOOPING} for a in acts],
        "materials": [dict(name=k, **v) for k, v in used.items()],
    }


def export_vehicle(name):
    from swlib import vehicles as V
    r, spec = V.build(name)
    acts = r.write_actions()
    arm = r.obj
    for o in bpy.context.selected_objects:
        o.select_set(False)
    arm.select_set(True)
    r.mesh.select_set(True)
    bpy.context.view_layer.objects.active = arm
    path = os.path.join(OUT, "..", "VehiclesV2")
    os.makedirs(path, exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(path, f"{name}.fbx"), use_selection=True, object_types={"ARMATURE", "MESH"},
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", add_leaf_bones=False,
        use_armature_deform_only=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, mesh_smooth_type="FACE", path_mode="STRIP")
    used = {}
    for slot in r.mesh.material_slots:
        mt = slot.material
        if mt and "sw" in mt:
            used[mt.name] = dict(mt["sw"].to_dict())
    loops = {"Idle", "Walk", "Move"}
    meta = {k: v for k, v in spec.items() if k != "params"}
    meta.update(name=name, clips=[{"name": a.name, "frames": int(a.frame_range[1]), "loop": a.name in loops} for a in acts],
                materials=[dict(name=k, **v) for k, v in used.items()],
                muzzles=[f"Muzzle.{i}" for i in range(len(spec["params"]["muzzles"]))],
                stride=spec["params"].get("stride", 0), period=spec["params"].get("period", 1))
    with open(os.path.join(path, f"{name}.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, ensure_ascii=False, indent=1)
    return meta


def main():
    args = parse_args()
    if args.vehicles:
        from swlib import vehicles as V
        allv = [export_vehicle(n) for n in V.VEHICLES]
        with open(os.path.join(OUT, "..", "VehiclesV2", "vehicles_v2.json"), "w", encoding="utf-8") as f:
            json.dump({"vehicles": allv}, f, ensure_ascii=False, indent=1)
        return
    os.makedirs(OUT, exist_ok=True)
    for name, spec in chars2.CHARACTERS.items():
        if args.only and name not in args.only.split(","):
            continue
        print("===", name)
        b = chars2.make(name, spec)
        for clip, frames in anims2.clips_for(spec).items():
            if args.clips and args.no_export and clip not in args.clips.split(","):
                continue
            b.rig.record(clip, [anims2.to_pose(p) for p in frames])
        b.rig.strip_controls()
        if args.portrait:
            portrait(b, name, spec, args)
        if args.strips:
            strips(b, name, spec, args)
        if not args.no_export:
            for pb in b.rig.obj.pose.bones:
                pb.matrix_basis.identity()
            meta = export(b, name, spec)
            with open(os.path.join(OUT, f"{name}.json"), "w", encoding="utf-8") as f:
                json.dump(meta, f, ensure_ascii=False, indent=1)
    allm = []
    for name in chars2.CHARACTERS:
        p = os.path.join(OUT, f"{name}.json")
        if os.path.exists(p):
            with open(p, encoding="utf-8") as f:
                allm.append(json.load(f))
    with open(os.path.join(OUT, "manifest_v2.json"), "w", encoding="utf-8") as f:
        json.dump({"characters": allm}, f, ensure_ascii=False, indent=1)


if __name__ == "__main__":
    main()
