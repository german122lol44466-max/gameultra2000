"""
Star Wars fan-проект — персонажи со скелетом, оружием и анимациями (Blender 4.2+).

    blender --background --python Tools/Blender/build_characters.py -- [--only DarthVader] [--preview] [--strips]
    # или без Blender-GUI:  python Tools/Blender/build_characters.py -- ...   (pip install bpy)

Результат:
    Assets/_Project/Art/Characters/<Имя>.fbx     — меш + скелет + все клипы (30 к/с, на месте)
    Assets/_Project/Art/Characters/<Имя>.glb     — то же для веб-просмотрщика
    Assets/_Project/Art/Weapons/<Оружие>.fbx     — оружие отдельно (для подбора/витрины)
    docs/renders/<Имя>.png, <Имя>_<Клип>.png    — портрет и раскадровки анимаций (--preview / --strips)

Система координат: персонаж смотрит в -Y Blender (в Unity — в +Z). Скелет общий для всех:
Root/Hips/Spine/Chest/Neck/Head, Shoulder/UpperArm/LowerArm/Hand.L/R, UpperLeg/LowerLeg/Foot/Toes.L/R,
Weapon.R (оружие в правой руке), Blade.R/Blade.R2 (клинки, масштаб по Y = зажжён/погашен), Cape.1-3 (плащ).
"""

import argparse
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

from swlib import anims, character, sith, stage, troopers  # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Characters")
OUT_W = os.path.join(ROOT, "Assets", "_Project", "Art", "Weapons")
RENDERS = os.path.join(ROOT, "docs", "renders")

CHARACTERS = {
    "Stormtrooper": dict(title="Штурмовик", body=lambda k, r: troopers.body(k, r, {}), weapon="E11", height=1.83,
                         cape=False, style="rifle"),
    "HeavyTrooper": dict(title="Тяжёлый штурмовик",
                         body=lambda k, r: troopers.body(k, r, dict(backpack=True, pauldron="pauldron_black", pauldron_side="R")),
                         weapon="DLT19", height=1.85, cape=False, style="rifle"),
    "TrooperCommander": dict(title="Командир штурмовиков",
                             body=lambda k, r: troopers.body(k, r, dict(pauldron="orange", pauldron_side="L")),
                             weapon="E11", height=1.83, cape=False, style="rifle"),
    "DarthVader": dict(title="Дарт Вейдер", body=sith.vader, weapon="Saber_Vader", height=2.02, cape=True, style="vader"),
    "DarthMaul": dict(title="Дарт Мол", body=sith.maul, weapon="Saber_Maul", height=1.75, cape=False, style="maul"),
    "DarthSidious": dict(title="Дарт Сидиус", body=sith.sidious, weapon="Saber_Sidious", height=1.73, cape=True, style="sidious"),
    "CountDooku": dict(title="Граф Дуку (Дарт Тиранус)", body=sith.dooku, weapon="Saber_Dooku", height=1.93, cape=True, style="dooku"),
}

LOOPING = {"Idle", "Walk", "Run", "Aim"}


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--only", default="")
    p.add_argument("--preview", action="store_true", help="портрет персонажа")
    p.add_argument("--strips", action="store_true", help="раскадровки клипов")
    p.add_argument("--clips", default="", help="только эти клипы в раскадровках (через запятую)")
    p.add_argument("--samples", type=int, default=48)
    p.add_argument("--no-export", action="store_true")
    p.add_argument("--weapons", action="store_true", help="экспорт оружия отдельно + витрина")
    return p.parse_args(argv)


def clips_for(spec):
    return anims.rifle_clips() if spec["style"] == "rifle" else anims.saber_clips(spec["style"])


# ----------------------------------------------------------------------------- превью

def _scene_for_preview(rig, w, h, samples):
    """Сцена превью поверх уже построенного персонажа."""
    cam = stage.setup(w, h, samples=samples)
    return cam


def portrait(rig, name, spec, args, clip="Idle", frame=20):
    s = spec["height"] / 1.83
    rig.apply(rig.poses[clip][min(frame, len(rig.poses[clip]) - 1)])
    cam = _scene_for_preview(rig, 900, 1200, args.samples)
    stage.studio((0, 0, 1.0 * s))
    stage.aim(cam, (-1.25 * s, -3.4 * s, 1.45 * s), (0, 0, 0.98 * s), 42)
    stage.render(os.path.join(RENDERS, f"{name}.png"))
    stage.aim(cam, (-0.28 * s, -0.95 * s, 1.72 * s), (0, 0, 1.66 * s), 55)
    bpy.context.scene.render.resolution_x, bpy.context.scene.render.resolution_y = 900, 900
    stage.render(os.path.join(RENDERS, f"{name}_face.png"))
    return cam


def strips(rig, name, spec, args, cam=None):
    """Раскадровка: 6 кадров клипа в ряд (рендер каждого кадра, склейка Pillow)."""
    from PIL import Image, ImageDraw, ImageFont
    s = spec["height"] / 1.83
    if cam is None:
        cam = _scene_for_preview(rig, 360, 480, max(10, args.samples // 3))
        stage.studio((0, 0, 1.0 * s))
    sc = bpy.context.scene
    sc.render.resolution_x, sc.render.resolution_y = 360, 480
    sc.cycles.samples = max(10, args.samples // 3)
    want = [c for c in args.clips.split(",") if c] or list(rig.frames)
    tmp = os.path.join(RENDERS, f"_tmp_{name}.png")
    try:
        font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 22)
    except OSError:
        font = ImageFont.load_default()
    for clip in want:
        frames = rig.poses.get(clip)
        if not frames:
            continue
        n = 6
        idx = [round(i * (len(frames) - 1) / (n - 1)) for i in range(n)]
        lying = clip == "Death"
        stage.aim(cam, (-1.6 * s, -3.2 * s, 1.3 * s if not lying else 1.6 * s), (0, 0.2 if lying else 0, 0.9 * s if not lying else 0.5 * s), 40)
        tiles = []
        for f in idx:
            rig.apply(frames[f])
            stage.render(tmp)
            tiles.append(Image.open(tmp).convert("RGB"))
        W, H = tiles[0].size
        sheet = Image.new("RGB", (W * n, H + 40), (8, 9, 12))
        for i, t in enumerate(tiles):
            sheet.paste(t, (i * W, 40))
        d = ImageDraw.Draw(sheet)
        d.text((12, 8), f"{spec['title']} — {clip}   ({len(frames) - 1} кадров, 30 к/с)", fill=(230, 230, 235), font=font)
        sheet.save(os.path.join(RENDERS, f"{name}_{clip}.png"))
    if os.path.exists(tmp):
        os.remove(tmp)


# ----------------------------------------------------------------------------- экспорт

def export(rig, name):
    os.makedirs(OUT, exist_ok=True)
    rig.strip_controls()
    acts = rig.write_actions()
    arm, body = rig.obj, rig.body
    # маркеры для выравнивания в движке
    for o in bpy.context.selected_objects:
        o.select_set(False)
    arm.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT, f"{name}.fbx"), use_selection=True, object_types={"ARMATURE", "MESH"},
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", add_leaf_bones=False,
        use_armature_deform_only=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, mesh_smooth_type="FACE", path_mode="STRIP")
    # glTF для веб-просмотра: каждое действие — отдельная анимация
    arm.animation_data.action = None
    for a in acts:
        tr = arm.animation_data.nla_tracks.new()
        tr.name = a.name
        st = tr.strips.new(a.name, 0, a)
        st.name = a.name
        tr.mute = True
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, f"{name}.glb"), use_selection=True, export_animations=True,
                              export_animation_mode="NLA_TRACKS", export_force_sampling=True, export_yup=True,
                              export_apply=False, export_skins=True, export_morph=False)
    used = {}
    for slot in body.material_slots:
        m = slot.material
        if m and "sw" in m:
            used[m.name] = dict(m["sw"].to_dict())
    meta = {"name": name, "materials": used, "clips": {a.name: {"frames": int(a.frame_range[1]), "loop": a.name in LOOPING} for a in acts}}
    return meta


def build_one(name, spec, args):
    print(f"=== {name}")
    rig = character.build(name, spec["body"], spec["weapon"], cape=spec["cape"], height=spec["height"])
    for clip, frames in clips_for(spec).items():
        rig.record(clip, [anims.to_pose(p) for p in frames])
    cam = None
    if args.preview or args.strips:
        os.makedirs(RENDERS, exist_ok=True)
        if args.preview:
            cam = portrait(rig, name, spec, args)
        if args.strips:
            strips(rig, name, spec, args, cam)
    meta = None
    if not args.no_export:
        meta = export(rig, name)
        meta.update(title=spec["title"], weapon=spec["weapon"], height=spec["height"], style=spec["style"])
    return meta


def export_weapons(args):
    os.makedirs(OUT_W, exist_ok=True)
    for wname in ("E11", "DLT19", "Saber_Vader", "Saber_Maul", "Saber_Sidious", "Saber_Dooku"):
        objs, grip = character.weapon_only(wname)
        bpy.context.view_layer.update()
        for o in bpy.context.selected_objects:
            o.select_set(False)
        for o in objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = objs[0]
        bpy.ops.object.join()
        j = bpy.context.active_object
        j.name = wname
        bpy.ops.export_scene.fbx(filepath=os.path.join(OUT_W, f"{wname}.fbx"), use_selection=True, object_types={"MESH"},
                                 apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE",
                                 path_mode="STRIP", use_mesh_modifiers=True)


def main():
    args = parse_args()
    os.makedirs(OUT, exist_ok=True)
    for name, spec in CHARACTERS.items():
        if args.only and name not in args.only.split(","):
            continue
        meta = build_one(name, spec, args)
        if meta:
            with open(os.path.join(OUT, f"{name}.json"), "w", encoding="utf-8") as f:
                json.dump(meta, f, ensure_ascii=False, indent=1)
    # общий манифест из файлов всех персонажей (порядок — как в CHARACTERS)
    manifest = {}
    for name in CHARACTERS:
        p = os.path.join(OUT, f"{name}.json")
        if os.path.exists(p):
            with open(p, encoding="utf-8") as f:
                manifest[name] = json.load(f)
    with open(os.path.join(OUT, "characters.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=1)
    # тот же манифест в виде массивов — для JsonUtility в Unity
    arr = {"characters": []}
    for name, m in manifest.items():
        arr["characters"].append({
            "name": name, "title": m.get("title", name), "weapon": m.get("weapon", ""), "height": m.get("height", 1.83),
            "style": m.get("style", ""),
            "clips": [{"name": c, "frames": v["frames"], "loop": v["loop"]} for c, v in m["clips"].items()],
            "materials": [dict(name=k, **v) for k, v in m.get("materials", {}).items()],
        })
    with open(os.path.join(OUT, "characters_unity.json"), "w", encoding="utf-8") as f:
        json.dump(arr, f, ensure_ascii=False, indent=1)
    if args.weapons:
        export_weapons(args)


if __name__ == "__main__":
    main()
