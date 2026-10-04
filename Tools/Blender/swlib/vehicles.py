"""
Техника: AT-ST и спидер 74-Z (Империя), AT-RT и спидер BARC (Республика).
Каждая машина — меш, жёстко привязанный к костям, + клипы: Idle, Walk/Move, Fire, Death.
Кости-«розетки» для игры: Muzzle.* (точки выстрела), Seat (место водителя), Head (поворотная рубка).
Машины смотрят в -Y (в Unity — в +Z), как и персонажи.
"""

import math

import bpy
from mathutils import Euler, Matrix, Quaternion, Vector

from . import kit as K
from . import mats

FPS = 30


def vmats():
    M = mats.palette()
    M.update({
        "imp_grey": K.mat_paint("Imperial_Grey", (0.33, 0.34, 0.36), rust=0.15, dirt=0.45, rough=0.55, metallic=0.35)
        if hasattr(K, "mat_paint") else K.mat_plastic("Imperial_Grey", (0.33, 0.34, 0.36), rough=0.5, dirt=0.5),
        "imp_dark": K.mat_metal("Imperial_Dark", (0.08, 0.085, 0.09), rough=0.45, scratch=0.4),
        "rep_white": K.mat_plastic("Republic_White", (0.62, 0.62, 0.6), rough=0.45, dirt=0.5, scuff=0.5),
        "rep_olive": K.mat_plastic("Republic_Olive", (0.22, 0.24, 0.17), rough=0.55, dirt=0.4),
        "rep_red": K.mat_plastic("Republic_Red", (0.45, 0.04, 0.03), rough=0.45, dirt=0.2),
        "glass_dark": K.mat_flat("Viewport_Dark", (0.01, 0.012, 0.014), rough=0.05, coat=1.0),
        "engine_glow": K.mat_flat("Engine_Glow", (0.2, 0.4, 1.0), emission=(0.3, 0.55, 1.0), strength=8.0),
        "piston": K.mat_metal("Piston_Chrome", (0.6, 0.6, 0.62), rough=0.15, scratch=0.3),
    })
    return M


class VRig:
    """Простая FK-арматура для техники; кадры записываются так же, как у персонажей (rig2.Rig.write_actions)."""

    def __init__(self, name, bones):
        """bones: [(имя, голова, хвост, родитель)]"""
        data = bpy.data.armatures.new(name)
        self.obj = obj = bpy.data.objects.new(name, data)
        bpy.context.scene.collection.objects.link(obj)
        bpy.context.view_layer.objects.active = obj
        for o in bpy.context.selected_objects:
            o.select_set(False)
        obj.select_set(True)
        bpy.ops.object.mode_set(mode="EDIT")
        eb = {}
        for n, h, t, p in bones:
            b = data.edit_bones.new(n)
            b.head, b.tail = Vector(h), Vector(t)
            b.align_roll(Vector((0, -1, 0)) if abs(Vector(t).z - Vector(h).z) > 0.5 * (Vector(t) - Vector(h)).length else Vector((0, 0, 1)))
            eb[n] = b
        for n, h, t, p in bones:
            if p:
                eb[n].parent = eb[p]
        bpy.ops.object.mode_set(mode="POSE")
        self.rest = {b.name: b.matrix_local.copy() for b in data.bones}
        self.deform = [b[0] for b in bones]
        for pb in obj.pose.bones:
            pb.rotation_mode = "QUATERNION"
        bpy.ops.object.mode_set(mode="OBJECT")
        self.frames = {}

    def set_world_rot(self, bone, q_world, loc=None):
        """Поворот кости в пространстве арматуры относительно её покоя (кватернион мира)."""
        R = self.rest[bone].to_quaternion()
        pb = self.obj.pose.bones[bone]
        pb.rotation_quaternion = R.inverted() @ q_world @ R
        if loc is not None:
            pb.location = R.inverted() @ Vector(loc)

    def capture(self):
        bpy.context.view_layer.update()
        ev = self.obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
        return {b: ev.pose.bones[b].matrix.copy() for b in self.deform}

    def clip(self, name, n, fn):
        out = []
        for i in range(n + 1):
            for pb in self.obj.pose.bones:
                pb.matrix_basis = Matrix.Identity(4)
            fn(self, i / n)
            out.append(self.capture())
        self.frames[name] = out

    def write_actions(self):
        from .rig2 import Rig
        return Rig.write_actions(self)

    def basis_from(self, mats_):
        from .rig2 import Rig
        return Rig.basis_from(self, mats_)

    def pose_baked(self, mats_):
        for b, m in self.basis_from(mats_).items():
            self.obj.pose.bones[b].matrix_basis = m


def ik2(hip, target, l1, l2, knee_forward=True):
    """Аналитический 2-звенный IK в плоскости YZ: углы бедра и голени (от вертикали вниз), в радианах."""
    d = Vector((0, target.y - hip.y, target.z - hip.z))
    L = min(d.length, l1 + l2 - 1e-4)
    base = math.atan2(-d.y, -d.z)               # угол от вертикали вниз к цели (+ — вперёд, т.е. к -Y)
    a = math.acos(max(-1, min(1, (l1 * l1 + L * L - l2 * l2) / (2 * l1 * L))))
    th = base + a if knee_forward else base - a
    knee = Vector((0, hip.y - math.sin(th) * l1, hip.z - math.cos(th) * l1))
    v = Vector((0, target.y - knee.y, target.z - knee.z))
    ts = math.atan2(-v.y, -v.z)
    return th, ts


# ============================================================================ шагающие машины

def walker(name, M, P):
    """P: параметры (размеры, материалы, функция корпуса)."""
    hipz, hipx = P["hip_z"], P["hip_x"]
    l1, l2 = P["thigh"], P["shin"]
    footz = P["ankle_z"]
    bones = [("Root", (0, 0, 0), (0, 0.5, 0), None),
             ("Body", (0, 0, hipz), (0, 0, hipz + 0.5), "Root"),
             ("Head", (0, P["head_pivot"][0], P["head_pivot"][1]), (0, P["head_pivot"][0], P["head_pivot"][1] + 0.4), "Body"),
             ("Seat", P["seat"], tuple(Vector(P["seat"]) + Vector((0, 0, 0.2))), "Head" if P.get("seat_on_head") else "Body")]
    for s, sg in (("L", 1), ("R", -1)):
        hip = Vector((sg * hipx, 0, hipz))
        th, ts = ik2(hip, Vector((sg * hipx, 0, footz)), l1, l2, P["knee_forward"])
        knee = Vector((sg * hipx, hip.y - math.sin(th) * l1, hip.z - math.cos(th) * l1))
        ankle = Vector((sg * hipx, 0, footz))
        bones += [(f"Thigh.{s}", hip, knee, "Body"), (f"Shin.{s}", knee, ankle, f"Thigh.{s}"),
                  (f"Foot.{s}", ankle, ankle + Vector((0, -P["foot_len"] * 0.5, 0)), f"Shin.{s}")]
    for i, (mp) in enumerate(P["muzzles"]):
        bones.append((f"Muzzle.{i}", mp, tuple(Vector(mp) + Vector((0, -0.3, 0))), "Head"))
    r = VRig(name + "_Rig", bones)
    k = K.Kit(M)
    P["body"](k, P)
    for s, sg in (("L", 1), ("R", -1)):
        P["leg"](k, P, s, sg, r)
    mesh = K.bind(k, r.obj, name)
    mesh.parent = r.obj
    rest_ang = {}
    for s, sg in (("L", 1), ("R", -1)):
        hip = Vector((sg * hipx, 0, hipz))
        rest_ang[s] = ik2(hip, Vector((sg * hipx, 0, footz)), l1, l2, P["knee_forward"])

    def legs(rig, phase_l, phase_r, stride, lift, body_dz=0.0, duty=0.62, body_dy=0.0, sway=0.0):
        rig.obj.pose.bones["Body"].location = rig.rest["Body"].to_quaternion().inverted() @ Vector((sway, body_dy, body_dz))
        for s, sg, ph in (("L", 1, phase_l), ("R", -1, phase_r)):
            f = ph % 1.0
            if f < duty:
                u = f / duty
                y = stride * (-0.5 + u)
                z = footz
            else:
                u = (f - duty) / (1 - duty)
                e = 0.5 - 0.5 * math.cos(math.pi * u)
                y = stride * (0.5 - e)
                z = footz + lift * math.sin(math.pi * u)
            hip = Vector((sg * hipx, body_dy, hipz + body_dz))
            th, tsn = ik2(hip, Vector((sg * hipx, y, z)), l1, l2, P["knee_forward"])
            th0, ts0 = rest_ang[s]
            ax = Vector((1, 0, 0))
            q1 = Quaternion(ax, -(th - th0))
            rig.set_world_rot(f"Thigh.{s}", q1)
            # голень: относительный поворот (мир) = разность углов голени
            bpy.context.view_layer.update()
            q2w = Quaternion(ax, -(tsn - ts0))
            Rs = rig.rest[f"Shin.{s}"].to_quaternion()
            rig.obj.pose.bones[f"Shin.{s}"].rotation_quaternion = Rs.inverted() @ (q1.inverted() @ q2w) @ Rs
            # стопа параллельна земле
            Rf = rig.rest[f"Foot.{s}"].to_quaternion()
            rig.obj.pose.bones[f"Foot.{s}"].rotation_quaternion = Rf.inverted() @ q2w.inverted() @ Rf

    def idle(rig, t):
        legs(rig, 0.0, 0.0, 0.0, 0.0, body_dz=-0.03 * (1 - math.cos(2 * math.pi * t)) * 0.5)
        rig.set_world_rot("Head", Quaternion((0, 0, 1), math.radians(8 * math.sin(2 * math.pi * t))))

    def walk(rig, t):
        bob = P["bob"] * (0.5 + 0.5 * math.cos(4 * math.pi * t))
        legs(rig, t, t + 0.5, P["stride"], P["lift"], body_dz=-bob, sway=P["sway"] * math.sin(2 * math.pi * t))
        rig.set_world_rot("Body", Quaternion((0, 1, 0), math.radians(3 * math.sin(2 * math.pi * t))))
        rig.set_world_rot("Head", Quaternion((0, 1, 0), math.radians(-2.5 * math.sin(2 * math.pi * t))))

    def fire(rig, t):
        legs(rig, 0.0, 0.0, 0.0, 0.0)
        k = math.exp(-t * 6) * math.sin(min(1.0, t * 8) * math.pi)
        rig.set_world_rot("Head", Quaternion((1, 0, 0), math.radians(-4 * k)), loc=(0, 0.12 * k, 0))

    def death(rig, t):
        u = K.smooth01(t * 1.3)
        drop = (hipz - footz - 0.4) * u * 0.75
        for s, sg in (("L", 1), ("R", -1)):
            pass
        legs(rig, 0.0, 0.0, 0.0, 0.0, body_dz=-drop)
        rig.set_world_rot("Body", Quaternion((1, 0, 0), math.radians(-35 * u)) @ Quaternion((0, 1, 0), math.radians(12 * u)))
        rig.set_world_rot("Head", Quaternion((1, 0, 0), math.radians(-20 * K.smooth01(t * 2 - 0.5))))
    period = int(P["period"] * FPS)
    r.clip("Idle", 90, idle)
    r.clip("Walk", period, walk)
    r.clip("Fire", 12, fire)
    r.clip("Death", 60, death)
    r.mesh = mesh
    return r


# ---------------------------------------------------------------------------- AT-ST

def atst_body(k, P):
    G, D, GL = "imp_grey", "imp_dark", "glass_dark"
    hz = P["hip_z"]
    hc = Vector((0, -0.1, hz + 1.55))         # центр кабины
    # кабина: клиновидный нос, плоская крыша
    k.loft("Head", G, [(hc.z - 0.95, 0.9, 1.25, 2.4, -0.2), (hc.z - 0.6, 1.25, 1.7, 2.6, -0.35), (hc.z + 0.1, 1.32, 1.8, 2.8, -0.4),
                       (hc.z + 0.75, 1.2, 1.6, 2.6, -0.25), (hc.z + 0.95, 0.85, 1.2, 2.2, -0.1)], ring=40, subsurf=1)
    # бронированная «маска» и смотровые щели
    k.box("Head", G, (0, hc.y - 1.85, hc.z + 0.15), (2.0, 0.5, 1.0), rot=(math.radians(18), 0, 0), bevel=0.06)
    for sg in (1, -1):
        k.box("Head", GL, (0.5 * sg, hc.y - 2.08, hc.z + 0.32), (0.55, 0.08, 0.12), rot=(math.radians(18), 0, math.radians(-8 * sg)), bevel=0.02)
        k.box("Head", G, (1.35 * sg, hc.y - 0.3, hc.z + 0.1), (0.25, 2.0, 0.9), bevel=0.05)
    # люк сверху
    k.cyl("Head", D, (0, hc.y + 0.3, hc.z + 0.95), (0, hc.y + 0.3, hc.z + 1.05), 0.45, verts=24, bevel=0.02)
    k.cyl("Head", G, (0, hc.y + 0.3, hc.z + 1.05), (0, hc.y + 0.3, hc.z + 1.12), 0.38, verts=24, bevel=0.02)
    # подбородочные спаренные пушки
    for sg in (1, -1):
        p = Vector((0.38 * sg, hc.y - 1.6, hc.z - 0.75))
        k.box("Head", D, p + Vector((0, 0.2, 0.05)), (0.3, 0.6, 0.3), bevel=0.04)
        k.cyl("Head", D, p, p + Vector((0, -0.9, 0)), 0.08, verts=16)
        k.cyl("Head", D, p + Vector((0, -0.9, 0)), p + Vector((0, -1.05, 0)), 0.1, 0.06, verts=16)
    # боковые: гранатомёт (лево) и бластер (право)
    k.cyl("Head", D, (1.55, hc.y - 0.6, hc.z - 0.1), (1.55, hc.y - 1.7, hc.z - 0.1), 0.14, verts=20)
    k.box("Head", D, (1.55, hc.y - 0.2, hc.z - 0.1), (0.35, 0.8, 0.4), bevel=0.04)
    k.cyl("Head", D, (-1.55, hc.y - 0.4, hc.z - 0.2), (-1.55, hc.y - 1.5, hc.z - 0.2), 0.07, verts=16)
    k.box("Head", D, (-1.55, hc.y - 0.1, hc.z - 0.2), (0.25, 0.6, 0.25), bevel=0.03)
    # шея и тазовый блок
    k.cyl("Body", D, (0, 0, hz + 0.1), (0, 0, hz + 0.75), 0.45, verts=24)
    k.box("Body", G, (0, 0.1, hz), (2.3, 1.2, 0.7), bevel=0.08, smooth=True)
    k.cyl("Body", D, (1.2, 0, hz), (-1.2, 0, hz), 0.32, verts=24)
    for sg in (1, -1):
        k.cyl("Body", G, (1.15 * sg, 0, hz), (1.45 * sg, 0, hz), 0.5, verts=28, bevel=0.03)
    # трубы и баки сзади
    k.cyl("Body", D, (0.5, 0.75, hz + 0.2), (-0.5, 0.75, hz + 0.2), 0.18, verts=16)


def atst_leg(k, P, s, sg, r):
    G, D, PS = "imp_grey", "imp_dark", "piston"
    hip = Vector(r.rest[f"Thigh.{s}"].translation)
    knee = Vector(r.rest[f"Shin.{s}"].translation)
    ank = Vector(r.rest[f"Foot.{s}"].translation)
    x = hip.x + 0.25 * sg
    k.lathe(f"Thigh.{s}", G, Vector((x, hip.y, hip.z)), Vector((x, knee.y, knee.z)),
            [(0, 0.32), (0.4, 0.38), ((knee - hip).length - 0.4, 0.28), ((knee - hip).length, 0.22)], verts=8, flat=0.7, subsurf=1)
    k.cyl(f"Shin.{s}", D, (x - 0.35 * sg, knee.y, knee.z), (x + 0.35 * sg, knee.y, knee.z), 0.3, verts=24)
    k.lathe(f"Shin.{s}", G, Vector((x, knee.y, knee.z)), Vector((x, ank.y, ank.z)),
            [(0, 0.24), (0.3, 0.3), ((ank - knee).length - 0.5, 0.22), ((ank - knee).length, 0.2)], verts=8, flat=0.8, subsurf=1)
    # гидравлика
    k.cyl(f"Shin.{s}", PS, (x + 0.32 * sg, knee.y + 0.25, knee.z - 0.3), (x + 0.32 * sg, ank.y + 0.2, ank.z + 0.5), 0.06, verts=12)
    k.cyl(f"Thigh.{s}", D, (x + 0.32 * sg, hip.y + 0.25, hip.z - 0.3), (x + 0.32 * sg, knee.y + 0.25, knee.z + 0.4), 0.09, verts=12)
    # стопа с когтями
    k.cyl(f"Foot.{s}", D, (x - 0.28 * sg, ank.y, ank.z), (x + 0.28 * sg, ank.y, ank.z), 0.26, verts=20)
    k.box(f"Foot.{s}", G, (x, ank.y - 0.2, 0.25), (1.0, 1.5, 0.35), bevel=0.08, smooth=True)
    for i, dx in enumerate((-0.32, 0.0, 0.32)):
        k.box(f"Foot.{s}", D, (x + dx, ank.y - 1.05, 0.12), (0.22, 0.6, 0.22), rot=(math.radians(-12), 0, 0), bevel=0.05)
    k.box(f"Foot.{s}", D, (x, ank.y + 0.75, 0.12), (0.3, 0.5, 0.22), bevel=0.05)


ATST = dict(hip_z=4.6, hip_x=1.25, thigh=2.35, shin=2.5, ankle_z=0.7, foot_len=1.8, knee_forward=True,
            head_pivot=(0.0, 5.15), seat=(0, -0.3, 6.1), seat_on_head=True, muzzles=[(0.38, -2.8, 5.4), (-0.38, -2.8, 5.4), (1.55, -1.9, 6.05)],
            stride=2.6, lift=0.7, period=2.4, bob=0.12, sway=0.15, body=atst_body, leg=atst_leg)


# ---------------------------------------------------------------------------- AT-RT

def atrt_body(k, P):
    W, O, R, D = "rep_white", "rep_olive", "rep_red", "imp_dark"
    hz = P["hip_z"]
    k.box("Body", O, (0, 0.05, hz + 0.25), (0.95, 0.85, 0.55), bevel=0.06, smooth=True)
    k.box("Body", W, (0, -0.25, hz + 0.6), (0.85, 0.5, 0.35), rot=(math.radians(-15), 0, 0), bevel=0.05, smooth=True)
    k.box("Body", R, (0, -0.48, hz + 0.6), (0.6, 0.05, 0.12), rot=(math.radians(-15), 0, 0), bevel=0.01)
    # площадка водителя и поручни
    k.box("Body", D, (0, 0.2, hz + 0.55), (0.7, 0.6, 0.06), bevel=0.02)
    for sg in (1, -1):
        k.cyl("Body", D, (0.35 * sg, -0.15, hz + 0.6), (0.3 * sg, -0.35, hz + 1.1), 0.025, verts=10)
    k.cyl("Body", D, (0.3, -0.35, hz + 1.1), (-0.3, -0.35, hz + 1.1), 0.025, verts=10)
    # пушка под носом
    k.box("Head", D, (0, -0.4, hz - 0.05), (0.3, 0.5, 0.25), bevel=0.04)
    k.cyl("Head", D, (0, -0.65, hz - 0.05), (0, -1.55, hz - 0.05), 0.07, verts=16)
    k.cyl("Head", D, (0, -1.55, hz - 0.05), (0, -1.68, hz - 0.05), 0.09, 0.06, verts=16)
    # миномёт сбоку
    k.cyl("Body", O, (0.6, 0.0, hz + 0.3), (0.6, -0.6, hz + 0.6), 0.1, verts=16)
    k.cyl("Body", D, (1.3, 0, hz), (-1.3, 0, hz), 0.12, verts=16)


def atrt_leg(k, P, s, sg, r):
    W, O, D, PS = "rep_white", "rep_olive", "imp_dark", "piston"
    hip = Vector(r.rest[f"Thigh.{s}"].translation)
    knee = Vector(r.rest[f"Shin.{s}"].translation)
    ank = Vector(r.rest[f"Foot.{s}"].translation)
    x = hip.x
    k.lathe(f"Thigh.{s}", O, Vector((x, hip.y, hip.z)), Vector((x, knee.y, knee.z)),
            [(0, 0.14), (0.2, 0.17), ((knee - hip).length - 0.15, 0.12), ((knee - hip).length, 0.1)], verts=8, flat=0.7, subsurf=1)
    k.cyl(f"Shin.{s}", D, (x - 0.15, knee.y, knee.z), (x + 0.15, knee.y, knee.z), 0.13, verts=20)
    k.lathe(f"Shin.{s}", W, Vector((x, knee.y, knee.z)), Vector((x, ank.y, ank.z)),
            [(0, 0.11), (0.15, 0.13), ((ank - knee).length - 0.2, 0.09), ((ank - knee).length, 0.08)], verts=8, flat=0.8, subsurf=1)
    k.cyl(f"Shin.{s}", PS, (x + 0.13 * sg, knee.y - 0.1, knee.z - 0.15), (x + 0.13 * sg, ank.y - 0.05, ank.z + 0.2), 0.03, verts=10)
    k.cyl(f"Foot.{s}", D, (x - 0.1, ank.y, ank.z), (x + 0.1, ank.y, ank.z), 0.1, verts=16)
    k.box(f"Foot.{s}", D, (x, ank.y - 0.05, ank.z - 0.08), (0.18, 0.2, 0.2), bevel=0.03)
    k.box(f"Foot.{s}", D, (x, ank.y - 0.12, 0.1), (0.42, 0.75, 0.16), bevel=0.04, smooth=True)
    for dx in (-0.12, 0.12):
        k.box(f"Foot.{s}", D, (x + dx, ank.y - 0.55, 0.06), (0.1, 0.3, 0.1), bevel=0.025)


ATRT = dict(hip_z=2.0, hip_x=0.42, thigh=1.05, shin=1.1, ankle_z=0.32, foot_len=0.8, knee_forward=False,
            head_pivot=(0.0, 1.95), seat=(0, 0.15, 2.6), seat_on_head=False, muzzles=[(0, -1.75, 1.95)],
            stride=1.3, lift=0.35, period=1.15, bob=0.05, sway=0.04, body=atrt_body, leg=atrt_leg)


# ============================================================================ спидеры

def speeder(name, M, P):
    bones = [("Root", (0, 0, 0), (0, 0.5, 0), None),
             ("Body", (0, 0, P["hover"]), (0, 0.5, P["hover"]), "Root"),
             ("Head", (0, -0.4, P["hover"] + 0.2), (0, -0.9, P["hover"] + 0.2), "Body"),
             ("Seat", P["seat"], tuple(Vector(P["seat"]) + Vector((0, 0, 0.2))), "Body")]
    for i, mp in enumerate(P["muzzles"]):
        bones.append((f"Muzzle.{i}", mp, tuple(Vector(mp) + Vector((0, -0.3, 0))), "Body"))
    for s in ("L", "R"):
        bones.append((f"Vane.{s}", P["vane"][s], tuple(Vector(P["vane"][s]) + Vector((0, -0.3, 0))), "Body"))
    r = VRig(name + "_Rig", bones)
    k = K.Kit(M)
    P["body"](k, P)
    mesh = K.bind(k, r.obj, name)
    mesh.parent = r.obj

    def body(rig, z=0.0, pitch=0.0, roll=0.0, yaw=0.0, vane=0.0):
        rig.set_world_rot("Body", Quaternion((0, 0, 1), math.radians(yaw)) @ Quaternion((0, 1, 0), math.radians(roll)) @
                          Quaternion((1, 0, 0), math.radians(pitch)), loc=(0, 0, z))
        for s, sg in (("L", 1), ("R", -1)):
            rig.set_world_rot(f"Vane.{s}", Quaternion((0, 0, 1), math.radians(vane * sg)))

    def idle(rig, t):
        body(rig, z=0.04 * math.sin(2 * math.pi * t), pitch=1.5 * math.sin(2 * math.pi * t + 1), roll=1.0 * math.sin(4 * math.pi * t))

    def move(rig, t):
        body(rig, z=-0.05 + 0.03 * math.sin(4 * math.pi * t), pitch=-5 + 1.5 * math.sin(4 * math.pi * t), roll=4 * math.sin(2 * math.pi * t),
             vane=8 * math.sin(2 * math.pi * t))

    def fire(rig, t):
        k_ = math.exp(-t * 6) * math.sin(min(1.0, t * 8) * math.pi)
        body(rig, pitch=2.5 * k_, z=0.02 * k_)

    def death(rig, t):
        u = K.smooth01(t * 1.4)
        body(rig, z=-(P["hover"] - 0.25) * u, pitch=-25 * u + 18 * u * u, roll=70 * u, yaw=40 * u)
    r.clip("Idle", 60, idle)
    r.clip("Move", 40, move)
    r.clip("Fire", 10, fire)
    r.clip("Death", 45, death)
    r.mesh = mesh
    return r


def bike74z_body(k, P):
    G, D, GL = "imp_grey", "imp_dark", "glass_dark"
    h = P["hover"]
    # корпус двигателя (сзади) и сиденье
    k.box("Body", G, (0, 0.55, h), (0.62, 1.2, 0.55), bevel=0.08, smooth=True)
    k.box("Body", D, (0, 1.2, h + 0.02), (0.55, 0.15, 0.45), bevel=0.04)
    for sg in (1, -1):
        k.cyl("Body", "engine_glow", (0.15 * sg, 1.28, h + 0.02), (0.15 * sg, 1.3, h + 0.02), 0.12, verts=16)
    k.box("Body", "leather_dark" if "leather_dark" in k.M else D, (0, 0.35, h + 0.33), (0.36, 0.6, 0.1), bevel=0.04, smooth=True)
    # длинная «вилка» вперёд и рулевые лопасти
    for sg in (1, -1):
        k.cyl("Body", D, (0.1 * sg, 0.0, h + 0.05), (0.1 * sg, -1.6, h - 0.02), 0.05, verts=12)
    k.box("Body", G, (0, -0.25, h + 0.15), (0.3, 0.7, 0.25), rot=(math.radians(-6), 0, 0), bevel=0.05, smooth=True)
    k.box("Body", D, (0, -0.1, h + 0.42), (0.6, 0.06, 0.06), bevel=0.01)          # руль
    k.box("Body", GL, (0, -0.25, h + 0.35), (0.2, 0.25, 0.04), bevel=0.01)
    for s, sg in (("L", 1), ("R", -1)):
        v = Vector(P["vane"][s])
        k.box(f"Vane.{s}", G, v + Vector((0, -0.25, 0)), (0.05, 0.6, 0.35), bevel=0.02)
    # пушка снизу
    k.cyl("Body", D, (0, -0.2, h - 0.2), (0, -0.85, h - 0.2), 0.04, verts=12)
    # подножки
    for sg in (1, -1):
        k.box("Body", D, (0.25 * sg, 0.1, h - 0.1), (0.2, 0.3, 0.04), bevel=0.01)


def barc_body(k, P):
    W, R, D, GL = "rep_white", "rep_red", "imp_dark", "glass_dark"
    h = P["hover"]
    k.box("Body", D, (0, 0.6, h), (0.7, 1.5, 0.5), bevel=0.08, smooth=True)
    k.box("Body", W, (0, 0.6, h + 0.1), (0.72, 1.3, 0.36), bevel=0.06, smooth=True)
    k.box("Body", R, (0, 0.6, h + 0.29), (0.3, 1.0, 0.02), bevel=0.005)
    k.box("Body", "leather_dark" if "leather_dark" in k.M else D, (0, 0.55, h + 0.33), (0.36, 0.55, 0.1), bevel=0.04, smooth=True)
    for sg in (1, -1):
        # передние двигатели-«клыки»
        k.cyl("Body", D, (0.22 * sg, -0.2, h), (0.22 * sg, -1.6, h - 0.05), 0.12, 0.09, verts=20)
        k.cyl("Body", "engine_glow", (0.22 * sg, -1.6, h - 0.05), (0.22 * sg, -1.62, h - 0.05), 0.08, verts=16)
        # боковые пушки
        k.cyl("Body", D, (0.42 * sg, 0.2, h - 0.05), (0.42 * sg, -0.9, h - 0.05), 0.04, verts=12)
        k.box("Body", D, (0.3 * sg, 0.15, h - 0.15), (0.2, 0.3, 0.04), bevel=0.01)
    k.box("Body", D, (0, -0.05, h + 0.42), (0.65, 0.05, 0.05), bevel=0.01)
    k.box("Body", GL, (0, -0.15, h + 0.36), (0.25, 0.2, 0.04), bevel=0.01)
    for s, sg in (("L", 1), ("R", -1)):
        v = Vector(P["vane"][s])
        k.box(f"Vane.{s}", W, v + Vector((0, 0.2, 0)), (0.05, 0.5, 0.3), bevel=0.02)


BIKE74Z = dict(hover=0.95, seat=(0, 0.35, 1.32), muzzles=[(0, -0.9, 0.75)], vane={"L": (0.12, -1.55, 0.95), "R": (-0.12, -1.55, 0.95)},
               body=bike74z_body)
BARC = dict(hover=0.9, seat=(0, 0.55, 1.27), muzzles=[(0.42, -0.95, 0.85), (-0.42, -0.95, 0.85)],
            vane={"L": (0.36, 1.2, 1.0), "R": (-0.36, 1.2, 1.0)}, body=barc_body)

VEHICLES = {
    "ATST": dict(side="Empire", title="AT-ST", kind="walker", params=ATST, health=1500, speed=2.2, crew="Stormtrooper", crew_clip="Drive",
                 hidden_crew=True),
    "SpeederBike74Z": dict(side="Empire", title="Спидер 74-Z", kind="speeder", params=BIKE74Z, health=300, speed=14.0, crew="Stormtrooper",
                           crew_clip="Ride"),
    "ATRT": dict(side="Republic", title="AT-RT", kind="walker", params=ATRT, health=700, speed=2.3, crew="CloneTrooper", crew_clip="Drive"),
    "BARCSpeeder": dict(side="Republic", title="Спидер BARC", kind="speeder", params=BARC, health=350, speed=14.0, crew="CloneTrooper",
                        crew_clip="Ride"),
}


def build(name):
    spec = VEHICLES[name]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = FPS
    M = vmats()
    if spec["kind"] == "walker":
        return walker(name, M, spec["params"]), spec
    return speeder(name, M, spec["params"]), spec
