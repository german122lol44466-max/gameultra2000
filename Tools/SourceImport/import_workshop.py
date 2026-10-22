"""
Импорт моделей из аддонов Garry's Mod (Steam Workshop) в Unity-проект «Командир клонов».

    python Tools/SourceImport/import_workshop.py -- --content <папка распакованных аддонов> [--only CloneTrooper] [--preview]

<content> — папка, где лежат распакованные .gma (см. README в этой папке): внутри подпапки с models/ и materials/.
Результат: Assets/_Project/Art/Workshop/<Юнит>/<Юнит>.fbx + текстуры PNG + <Юнит>.json и общий units.json.

Каждый солдат получает скелет v2 (Hips/Spine/.../Weapon.R) и ВСЕ клипы: бой (anims2) и строевые стойки/походки
(anims3), запечённые процедурным ригом с IK прямо на модель (руки держат именно это оружие).
Папка Art/Workshop не попадает в git (лицензия моделей — у их авторов).
"""

import argparse
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(ROOT, "Tools", "Blender"))

import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

import blsource  # noqa: E402
from swlib import anims2, anims3, rig2, weapons  # noqa: E402

OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Workshop")

# ----------------------------------------------------------------------------- состав армий
# model — путь внутри models/, weapon — модель оружия (w_*.mdl) и хват из swlib.weapons
CW = "vjsw/galacticrepublic/clones/"
GE = "vjsw/galacticempire/"
UNITS = {
    # --- Республика
    "CloneTrooper": dict(team="Republic", title="Клон-штурмовик", role="rifleman", model=CW + "clonetrooper.mdl", weapon="DC15A"),
    "Clone501": dict(team="Republic", title="Клон 501-го", role="rifleman", model=CW + "501st_clonetrooper.mdl", weapon="DC15A"),
    "Clone212": dict(team="Republic", title="Клон 212-го", role="rifleman", model=CW + "212th_clonetrooper.mdl", weapon="DC15A"),
    "ClonePhase1": dict(team="Republic", title="Клон (Фаза I)", role="rifleman", model=CW + "phase1_clonetrooper.mdl", weapon="DC15A"),
    "CloneShock": dict(team="Republic", title="Шоковый штурмовик", role="rifleman", model=CW + "shock_clonetrooper.mdl", weapon="DC15S"),
    "CloneHeavy": dict(team="Republic", title="Тяжёлый клон", role="heavy", model=CW + "clonetrooper_heavy.mdl", weapon="Z6"),
    "CloneSharpshooter": dict(team="Republic", title="Снайпер", role="sniper", model=CW + "clonetrooper_sharpshooter.mdl", weapon="DC15X"),
    "CloneRocketeer": dict(team="Republic", title="Ракетчик", role="rocket", model=CW + "clonetrooper_rocketeer.mdl", weapon="RPS6"),
    "CloneEngineer": dict(team="Republic", title="Инженер", role="engineer", model=CW + "clonetrooper_engineer.mdl", weapon="DC15S"),
    "CloneLieutenant": dict(team="Republic", title="Лейтенант", role="officer", model=CW + "clonetrooper_lieutenant.mdl", weapon="DC15S"),
    "CloneARC": dict(team="Republic", title="ARC-солдат", role="elite", model=CW + "clonetrooper_arc.mdl", weapon="DC17M"),
    "CloneCommander": dict(team="Republic", title="Командир клонов", role="commander", model=CW + "clonetrooper_arc_commander.mdl", weapon="DC15A"),
    "CloneCommando": dict(team="Republic", title="Коммандос", role="elite", model=CW + "clone_commando.mdl", weapon="DC17M"),
    "CloneMarine": dict(team="Republic", title="Галактический морпех", role="rifleman", model=CW + "galactic_marine.mdl", weapon="DC15A"),
    # --- Империя
    "Stormtrooper": dict(team="Empire", title="Штурмовик", role="rifleman", model=GE + "stormtroopers/stormtrooper.mdl", weapon="E11"),
    "StormtrooperCaptain": dict(team="Empire", title="Капитан штурмовиков", role="officer", model=GE + "stormtroopers/stormtrooper_captain.mdl", weapon="E11"),
    "StormCommander": dict(team="Empire", title="Командир штурмовиков", role="commander", model=GE + "stormtroopers/stormtrooper_commander.mdl", weapon="E11"),
    "ImperialHeavy": dict(team="Empire", title="Тяжёлый штурмовик", role="heavy", model=GE + "stormtroopers/imperial_heavy_trooper.mdl", weapon="DLT19"),
    "Shocktrooper": dict(team="Empire", title="Шоковый штурмовик", role="rifleman", model=GE + "stormtroopers/shocktrooper.mdl", weapon="E11"),
    "ScoutTrooper": dict(team="Empire", title="Разведчик", role="sniper", model=GE + "stormtroopers/scout_trooper.mdl", weapon="E11S"),
    "DeathTrooper": dict(team="Empire", title="Штурмовик смерти", role="elite", model=GE + "stormtroopers/death_trooper.mdl", weapon="E11D"),
    "Shadowtrooper": dict(team="Empire", title="Теневой штурмовик", role="elite", model=GE + "stormtroopers/shadowtrooper.mdl", weapon="E11"),
    "ArmyTrooper": dict(team="Empire", title="Армейский солдат", role="rifleman", model=GE + "other/army_trooper.mdl", weapon="E11"),
    "ArmyRocketeer": dict(team="Empire", title="Ракетчик Империи", role="rocket", model=GE + "other/army_trooper_rocketeer.mdl", weapon="ImpRocket"),
    "ImperialOfficer": dict(team="Empire", title="Имперский офицер", role="officer", model=GE + "other/officer.mdl", weapon="SE14"),
    "ImperialEngineer": dict(team="Empire", title="Имперский инженер", role="engineer", model=GE + "other/engineer.mdl", weapon="E11"),
}

# оружие: модель + хват swlib + характеристики (урон, темп, магазин, разброс, болт)
WEAPONS = {
    "DC15A": dict(model="weapons/w_dc15a.mdl", grip="DC15A", title="DC-15A", damage=24, rpm=330, mag=50, range=90, spread=0.9, bolt="blue", sound="dc15a"),
    "DC15S": dict(model="weapons/w_dc15s.mdl", grip="E11", title="DC-15S", damage=19, rpm=480, mag=60, range=60, spread=1.4, bolt="blue", sound="dc15s"),
    "DC17M": dict(model="weapons/w_dc17m_br.mdl", grip="E11", title="DC-17m", damage=22, rpm=520, mag=60, range=80, spread=1.0, bolt="blue", sound="dc17m"),
    "DC15X": dict(model="weapons/glcw_weps_battlefront/rep_sniper_rifle.mdl", grip="DC15A", title="DC-15x", damage=95, rpm=45, mag=5, range=200, spread=0.15, bolt="blue_heavy", sound="sniper"),
    "Z6": dict(model="weapons/glcw_weps_battlefront/rep_chaingun.mdl", grip="DLT19", title="Z-6", damage=14, rpm=900, mag=200, range=60, spread=2.2, bolt="blue", sound="z6"),
    "RPS6": dict(model="weapons/glcw_weps_battlefront/rep_rocket_launcher.mdl", grip="DLT19", title="RPS-6", damage=260, rpm=20, mag=1, range=120, spread=0.6, bolt="rocket", sound="rocket"),
    "E11": dict(model="weapons/w_e11.mdl", grip="E11", title="E-11", damage=20, rpm=400, mag=40, range=70, spread=1.3, bolt="red", sound="e11"),
    "E11S": dict(model="weapons/glcw_weps_battlefront/imp_sniper_rifle.mdl", grip="DC15A", title="E-11s", damage=90, rpm=45, mag=5, range=200, spread=0.15, bolt="red_heavy", sound="sniper"),
    "E11D": dict(model="weapons/galacticempire/e-11d/w_e-11d.mdl", grip="E11", title="E-11D", damage=26, rpm=420, mag=45, range=80, spread=0.9, bolt="red", sound="e11"),
    "DLT19": dict(model="weapons/w_dlt19.mdl", grip="DLT19", title="DLT-19", damage=17, rpm=700, mag=120, range=75, spread=1.8, bolt="red", sound="dlt19"),
    "ImpRocket": dict(model="weapons/glcw_weps_battlefront/imp_rocket_launcher.mdl", grip="DLT19", title="Ракетомёт", damage=240, rpm=20, mag=1, range=120, spread=0.6, bolt="rocket", sound="rocket"),
    "SE14": dict(model="weapons/w_se14c.mdl", grip="E11", title="SE-14C", damage=16, rpm=420, mag=30, range=45, spread=1.6, bolt="red", sound="pistol"),
}

GRIPS = {"DC15A": weapons.dc15a, "E11": weapons.e11, "DLT19": weapons.dlt19}

ROLE_STATS = {
    "rifleman": dict(health=100, speed=1.0, cost=60),
    "heavy": dict(health=140, speed=0.85, cost=110),
    "sniper": dict(health=80, speed=1.0, cost=100),
    "rocket": dict(health=100, speed=0.9, cost=130),
    "engineer": dict(health=100, speed=1.0, cost=80),
    "officer": dict(health=120, speed=1.0, cost=120),
    "elite": dict(health=160, speed=1.1, cost=180),
    "commander": dict(health=250, speed=1.05, cost=0),
}


class _Null:
    def __getattr__(self, n):
        return lambda *a, **kw: None


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--content", required=True, help="папка с распакованными аддонами (в каждой подпапке models/, materials/)")
    p.add_argument("--only", default="")
    p.add_argument("--preview", action="store_true", help="картинки клипов в docs/renders/workshop")
    p.add_argument("--clips", default="", help="только эти клипы (для отладки)")
    p.add_argument("--no-export", action="store_true")
    p.add_argument("--props", action="store_true", help="только техника/постройки")
    p.add_argument("--sounds", action="store_true", help="только звуки")
    return p.parse_args(argv)


def content_roots(base):
    roots = []
    for d in sorted(os.listdir(base)):
        p = os.path.join(base, d)
        if os.path.isdir(os.path.join(p, "models")) or os.path.isdir(os.path.join(p, "materials")):
            roots.append(p)
    if os.path.isdir(os.path.join(base, "models")):
        roots.append(base)
    return roots


def find_model(roots, rel):
    for r in roots:
        p = os.path.join(r, "models", rel)
        if os.path.exists(p):
            return p
    return None


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = 30


def all_clips():
    C = anims2.rifle_clips()
    C.update(anims3.formation_clips())
    return C


def shift_feet(frames, dz):
    """Анимации рассчитаны на лодыжку на высоте 0.078 м; у моделей в ботинках она выше."""
    if abs(dz) < 1e-4:
        return frames
    out = []
    for p in frames:
        q = dict(p)
        for k in ("fl", "fr"):
            x, y, z, yaw, pitch = q[k]
            q[k] = (x, y, z + dz, yaw, pitch)
        out.append(q)
    return out


def build_unit(name, spec, roots, args):
    reset()
    mdl = find_model(roots, spec["model"])
    if not mdl:
        print("  !! нет модели", spec["model"])
        return None
    udir = os.path.join(OUT, name)
    os.makedirs(udir, exist_ok=True)
    imp = blsource.load(mdl, roots, udir, name + "_Body")
    body = imp.mesh
    S = blsource.skeleton(imp)
    arm = rig2.build_armature(name + "_Rig", S)
    blsource.remap_groups(body, imp.M)
    rig2.bind([body], arm)
    rig2.to_tpose(arm, [body])
    wspec = WEAPONS[spec["weapon"]]
    gfn = GRIPS[wspec["grip"]]
    grip = gfn(_Null())
    rig = rig2.Rig(arm, grip)
    # модель оружия в рамке Weapon.R
    meshes = [body]
    muzzle = grip.get("muzzle", Vector((0, 0.7, 0.065)))
    wmdl = find_model(roots, wspec["model"])
    if wmdl:
        wobj, muzzle, wlen = blsource.load_weapon(wmdl, roots, udir, name + "_Weapon")
        wobj.matrix_world = rig.rest["Weapon.R"] @ Matrix.Identity(4)
        for g in list(wobj.vertex_groups):
            wobj.vertex_groups.remove(g)
        g = wobj.vertex_groups.new(name="Weapon.R")
        g.add(list(range(len(wobj.data.vertices))), 1.0, "REPLACE")
        bpy.context.view_layer.update()
        # запечь положение покоя в вершины (меш в пространстве арматуры)
        wobj.data.transform(wobj.matrix_world)
        wobj.matrix_world = Matrix.Identity(4)
        rig2.bind([wobj], arm)
        meshes.append(wobj)
    else:
        print("  !! нет модели оружия", wspec["model"])
    dz = blsource.ankle_height(S) - anims2.ANKLE
    clips = all_clips()
    want = [c for c in args.clips.split(",") if c]
    for clip, frames in clips.items():
        if want and clip not in want:
            continue
        rig.record(clip, [anims2.to_pose(p) for p in shift_feet(frames, dz)])
    rig.strip_controls()
    s = imp.real_height / blsource.BASE_HEIGHT if 1.5 < imp.real_height < 2.3 else 1.0
    arm.scale = (s, s, s)
    if args.preview:
        preview(rig, meshes, name, spec, want)
    if args.no_export:
        return None
    for pb in rig.obj.pose.bones:
        pb.matrix_basis.identity()
    acts = rig.write_actions()
    for o in bpy.context.selected_objects:
        o.select_set(False)
    arm.select_set(True)
    for m in meshes:
        m.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(udir, f"{name}.fbx"), use_selection=True, object_types={"ARMATURE", "MESH"},
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", add_leaf_bones=False,
        use_armature_deform_only=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=1.0, mesh_smooth_type="FACE", path_mode="STRIP",
        use_custom_props=False)
    mats = []
    seen = set()
    for m in meshes:
        for info in blsource.infos_of(m):
            if info["name"] in seen:
                continue
            seen.add(info["name"])
            mats.append({k: v for k, v in info.items() if k not in ("dir",)})
    looping = anims2.LOOPING | anims3.FORMATION_LOOPING
    meta = dict(name=name, title=spec["title"], team=spec["team"], role=spec["role"], height=round(s * blsource.BASE_HEIGHT, 3),
                weapon=dict(id=spec["weapon"], **{k: v for k, v in wspec.items() if k not in ("model", "grip")},
                            muzzle=[round(x, 4) for x in muzzle]),
                stats=ROLE_STATS[spec["role"]], source=spec["model"],
                clips=[dict(name=a.name, frames=int(a.frame_range[1]), loop=a.name in looping) for a in acts],
                materials=mats)
    with open(os.path.join(udir, f"{name}.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, ensure_ascii=False, indent=1)
    return meta


def preview(rig, meshes, name, spec, want):
    """Раскадровка клипов (Workbench, быстро) — для проверки поз."""
    from PIL import Image, ImageDraw
    out = os.path.join(ROOT, "docs", "renders", "workshop")
    os.makedirs(out, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "TEXTURE"
    sc.render.resolution_x, sc.render.resolution_y = 300, 400
    world = bpy.data.worlds.new("W")
    sc.world = world
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    bpy.ops.mesh.primitive_plane_add(size=20)
    tmp = os.path.join(out, "_tmp.png")
    clips = want or list(rig.frames)
    for clip in clips:
        frames = rig.frames.get(clip)
        if not frames:
            continue
        n = 6
        idx = [round(i * (len(frames) - 1) / (n - 1)) for i in range(n)]
        lying = clip.startswith(("Death", "Prone", "GoProne", "StandUp"))
        for view, campos in (("front", (-1.9, -3.4, 1.5 if lying else 1.3)), ("side", (-3.8, -0.4, 1.4 if lying else 1.2))):
            target = Vector((0, 0.1 if lying else 0, 0.45 if lying else 0.95))
            cam.location = campos
            d = target - Vector(campos)
            cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
            cam.data.lens = 60
            tiles = []
            for f in idx:
                rig.pose_baked(frames[f])
                bpy.context.view_layer.update()
                sc.render.filepath = tmp
                bpy.ops.render.render(write_still=True)
                tiles.append(Image.open(tmp).convert("RGB"))
            W, H = tiles[0].size
            sheet = Image.new("RGB", (W * n, H + 30), (20, 20, 24))
            for i, t in enumerate(tiles):
                sheet.paste(t, (i * W, 30))
            ImageDraw.Draw(sheet).text((8, 8), f"{name} - {clip} ({view})", fill=(240, 240, 240))
            sheet.save(os.path.join(out, f"{name}_{clip}_{view}.png"))
    if os.path.exists(tmp):
        os.remove(tmp)


def main():
    args = parse_args()
    roots = content_roots(args.content)
    print("контент:", roots)
    os.makedirs(OUT, exist_ok=True)
    only = [x for x in args.only.split(",") if x]
    for name, spec in UNITS.items():
        if only and name not in only:
            continue
        print("===", name)
        try:
            build_unit(name, spec, roots, args)
        except Exception as e:
            import traceback
            traceback.print_exc()
            print("  !! ошибка", name, e)
    allm = []
    for name in UNITS:
        p = os.path.join(OUT, name, f"{name}.json")
        if os.path.exists(p):
            with open(p, encoding="utf-8") as f:
                allm.append(json.load(f))
    with open(os.path.join(OUT, "units.json"), "w", encoding="utf-8") as f:
        json.dump(dict(units=allm, weapons=WEAPONS), f, ensure_ascii=False, indent=1)




# ----------------------------------------------------------------------------- техника и постройки (статичные)
PROPS = {
    "RepSentryGun": "vjsw/galacticrepublic/misc/sentry_gun.mdl",
    "EmpSentryGun": "vjsw/galacticempire/misc/sentry_gun.mdl",
    "ATTE": "vjsw/galacticrepublic/vehicles/atte.mdl",
    "ATXT": "vjsw/galacticrepublic/vehicles/atxt.mdl",
    "ATAT": "vjsw/galacticempire/vehicles/at-at.mdl",
    "ATST": "vjsw/galacticempire/vehicles/at-st.mdl",
    "ProbeDroid": "vjsw/galacticempire/droids/probe_droid.mdl",
    "ThermalDetonator": "vsw/thermaldetonator.mdl",
}


def build_prop(name, rel, roots):
    reset()
    mdl = find_model(roots, rel)
    if not mdl:
        print("  !! нет модели", rel)
        return None
    pdir = os.path.join(OUT, "Props", name)
    os.makedirs(pdir, exist_ok=True)
    obj, lo, hi = blsource.load_static(mdl, roots, pdir, name)
    for o in bpy.context.selected_objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(filepath=os.path.join(pdir, f"{name}.fbx"), use_selection=True, object_types={"MESH"},
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE",
                             path_mode="STRIP", use_custom_props=False)
    meta = dict(name=name, source=rel, size=[round(float(x), 3) for x in (hi - lo)],
                materials=[{k: v for k, v in i.items() if k != "dir"} for i in blsource.infos_of(obj)])
    with open(os.path.join(pdir, f"{name}.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, ensure_ascii=False, indent=1)
    return meta


# ----------------------------------------------------------------------------- звуки
SOUND_FILES = {
    "w_dc15a": "vj_sw_clone_wars_era/weapons/dc-15a/fire.wav",
    "w_dc15s": "vj_sw_clone_wars_era/weapons/dc-15s/fire.wav",
    "w_dc17m": "vj_sw_clone_wars_era/weapons/dc-17m/fire.wav",
    "w_sniper": "vj_sw_clone_wars_era/weapons/sniperrifle/fire.wav",
    "w_z6": "vj_sw_clone_wars_era/weapons/rotaryblastercannon/fire.wav",
    "w_rocket": "vj_sw_clone_wars_era/weapons/rocketlauncher/fire.wav",
    "w_distant": "vj_sw_clone_wars_era/weapons/shared/distant.wav",
    "w_grenade_throw": "vj_sw_clone_wars_era/weapons/grenade/throw.wav",
    "w_e11": "vj_sw_galactic_civil_war/weapons/e-11/fire_e11.wav",
    "w_e11d": "vj_sw_galactic_civil_war/weapons/e-11d/fire.wav",
    "w_dlt19": "vj_sw_galactic_civil_war/weapons/dlt-19/fire_dlt19.wav",
    "w_pistol": "weapons/se14c_fire.wav",
    "w_detonator": "weapons/sw_detonator.wav",
    "r_dc15a": "weapons/dc15a_reload.wav", "r_dc15s": "weapons/dc15s_reload.wav", "r_dc17m": "weapons/dc17m_br_reload.wav",
    "r_e11": "weapons/e11_reload.wav", "r_dlt19": "weapons/dlt19_reload.wav", "r_z6": "weapons/dlt19_reload.wav",
    "r_sniper": "weapons/dc17m_sn_reload.wav", "r_rocket": "weapons/dc17m_at_reload.wav", "r_pistol": "weapons/se14c_reload.wav",
}
VOICE_DIRS = {"vc": "vj_sw_clone_wars_era/snpcs/clonetrooper", "vs": "vj_sw_galactic_civil_war/snpcs/stormtrooper"}
VOICE_ALIASES = {"alert_": "alert", "killed_enemy_": "killedenemy", "man_down_": "mandown", "grenade_sight_": "grenadespotted",
                 "friendly_fire_": "friendlyfire", "heal_ally_": "healally", "combat_": "combat", "death_": "death", "pain_": "pain",
                 "suppressing_": "suppressing"}
VOICE_CATS = ("alert", "combat", "danger", "death", "friendlyfire", "grenadespotted", "killedenemy", "mandown", "pain", "suppressing")


def copy_sounds(base):
    import re
    import shutil
    out = os.path.join(OUT, "Sounds")
    os.makedirs(out, exist_ok=True)
    sound_roots = [os.path.join(base, d, "sound") for d in sorted(os.listdir(base)) if os.path.isdir(os.path.join(base, d, "sound"))]

    def find(rel):
        for r in sound_roots:
            p = os.path.join(r, rel)
            if os.path.exists(p):
                return p
        return None
    n = 0
    for name, rel in SOUND_FILES.items():
        p = find(rel)
        if p:
            shutil.copy(p, os.path.join(out, name + os.path.splitext(p)[1]))
            n += 1
    for prefix, rel in VOICE_DIRS.items():
        d = find(rel)
        if not d:
            continue
        counts = {}
        for f in sorted(os.listdir(d)):
            stem, ext = os.path.splitext(f)
            cat = None
            for a, c in VOICE_ALIASES.items():
                if stem.startswith(a):
                    cat = c
            if cat is None:
                cat = re.sub(r"\d+$", "", stem)
            if cat not in VOICE_CATS:
                continue
            counts[cat] = counts.get(cat, 0) + 1
            shutil.copy(os.path.join(d, f), os.path.join(out, f"{prefix}_{cat}_{counts[cat]}{ext}"))
            n += 1
    print("звуков:", n)


def main_props():
    args = parse_args()
    roots = content_roots(args.content)
    allp = []
    for name, rel in PROPS.items():
        if args.only and name not in args.only.split(","):
            continue
        print("=== prop", name)
        try:
            m = build_prop(name, rel, roots)
            if m:
                allp.append(m)
        except Exception as e:
            import traceback
            traceback.print_exc()
            print("  !! ошибка", name, e)
    with open(os.path.join(OUT, "Props", "props.json"), "w", encoding="utf-8") as f:
        json.dump(dict(props=allp), f, ensure_ascii=False, indent=1)


if __name__ == "__main__":
    if "--props" in sys.argv:
        main_props()
    elif "--sounds" in sys.argv:
        copy_sounds(parse_args().content)
    else:
        main()
