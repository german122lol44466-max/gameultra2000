"""Сборка персонажа: скелет + оружие + меш со скиннингом."""

import bpy
from mathutils import Matrix

from . import kit as K
from . import mats, weapons
from .rig import Rig


class _Probe:
    """Пустой kit: позволяет узнать хват оружия, не создавая геометрию."""

    def __getattr__(self, name):
        return lambda *a, **kw: None


def build(name, body_fn, weapon, cape=False, height=1.83, extra=None, reset=True):
    if reset:
        bpy.ops.wm.read_factory_settings(use_empty=True)
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.scene.render.fps = 30
    M = mats.palette()
    if extra:
        M.update(extra())
    wfn = weapons.WEAPONS[weapon]
    grip = wfn(_Probe())
    rig = Rig(name + "_Rig", cape=cape, weapon=grip)
    bpy.ops.object.mode_set(mode="OBJECT")
    k = K.Kit(M)
    body_fn(k, rig.rest)
    # оружие — в пространстве кости Weapon.R
    k.xform = rig.rest["Weapon.R"]
    wfn(k)
    for bn, bm, ln in grip["blades"]:
        k.xform = rig.rest[bn]
        weapons.blade(k, bn, ln)
    k.xform = None
    body = K.bind(k, rig.obj, name)
    s = height / 1.83
    rig.obj.scale = (s, s, s)
    rig.name = name
    rig.grip = grip
    rig.body = body
    return rig


def weapon_only(weapon, glow=False):
    """Отдельная модель оружия (для FBX и витрины) в собственном пространстве."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    M = mats.palette()
    k = K.Kit(M)
    grip = weapons.WEAPONS[weapon](k)
    for bn, bm, ln in grip["blades"]:
        k.xform = bm
        weapons.blade(k, bn, ln, glow="glow" if glow else None)
    k.xform = None
    objs = []
    for o, _ in k.parts:
        objs.append(o)
    return objs, grip
