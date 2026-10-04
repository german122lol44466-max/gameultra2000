"""
Скелет, IK-управление, позы и запекание анимаций.

Система координат (Blender, базовый рост 1.83 м): персонаж смотрит в -Y, левая сторона персонажа — +X,
вверх — +Z. Все позы задаются в этом «пространстве персонажа» в метрах базового роста; рост
конкретного персонажа задаётся масштабом объекта арматуры.

Оси костей: Y — вдоль кости, Z — «вперёд» персонажа (для горизонтальных костей стопы — вверх).
Поэтому поворот вокруг локального X на +угол всегда сгибает кость вперёд, Y — скручивание, Z — наклон вбок.

Анимации строятся так: для каждого кадра функция клипа возвращает Pose (корпус FK, положение оружия
и рук/стоп в пространстве персонажа), мы выставляем управляющие кости, IK решает руки и ноги,
после чего итоговые матрицы деформирующих костей запоминаются. В конце управляющие кости удаляются,
а запомненные кадры записываются в действия (Actions) — в FBX уходит чистый FK-скелет.
"""

import math

import bpy
from mathutils import Euler, Matrix, Quaternion, Vector

FPS = 30
FWD = Vector((0, -1, 0))
UP = Vector((0, 0, 1))

# имя: (голова, хвост, родитель, вектор для оси Z)
BONES = {
    "Root": ((0, 0, 0), (0, 0.25, 0), None, (0, 0, 1)),
    "Hips": ((0, 0, 0.97), (0, 0, 1.07), "Root", FWD),
    "Spine": ((0, 0, 1.07), (0, 0, 1.24), "Hips", FWD),
    "Chest": ((0, 0, 1.24), (0, 0, 1.47), "Spine", FWD),
    "Neck": ((0, 0, 1.49), (0, 0, 1.59), "Chest", FWD),
    "Head": ((0, 0, 1.59), (0, 0, 1.81), "Neck", FWD),
    "Shoulder.L": ((0.03, 0, 1.43), (0.175, 0, 1.43), "Chest", FWD),
    "UpperArm.L": ((0.185, 0, 1.42), (0.205, 0.025, 1.135), "Shoulder.L", FWD),
    "LowerArm.L": ((0.205, 0.025, 1.135), (0.215, -0.005, 0.885), "UpperArm.L", FWD),
    "Hand.L": ((0.215, -0.005, 0.885), (0.218, -0.01, 0.73), "LowerArm.L", FWD),
    "UpperLeg.L": ((0.098, 0, 0.93), (0.104, -0.012, 0.515), "Hips", FWD),
    "LowerLeg.L": ((0.104, -0.012, 0.515), (0.108, 0.012, 0.09), "UpperLeg.L", FWD),
    "Foot.L": ((0.108, 0.012, 0.09), (0.108, -0.105, 0.025), "LowerLeg.L", UP),
    "Toes.L": ((0.108, -0.105, 0.025), (0.108, -0.175, 0.02), "Foot.L", UP),
}
CAPE = {
    "Cape.1": ((0, 0.13, 1.44), (0, 0.17, 1.08), "Chest", FWD),
    "Cape.2": ((0, 0.17, 1.08), (0, 0.205, 0.66), "Cape.1", FWD),
    "Cape.3": ((0, 0.205, 0.66), (0, 0.24, 0.18), "Cape.2", FWD),
}

ARM_LEN = 0.0  # заполняется в build_armature


def mirror(n):
    if n.endswith(".L"):
        return n[:-2] + ".R"
    return n


def skeleton(cape=False):
    out = {}
    src = dict(BONES)
    if cape:
        src.update(CAPE)
    for name, (h, t, p, z) in src.items():
        out[name] = (Vector(h), Vector(t), p, Vector(z))
        if name.endswith(".L"):
            out[mirror(name)] = (Vector((-h[0], h[1], h[2])), Vector((-t[0], t[1], t[2])),
                                 mirror(p) if p and p.endswith(".L") else p, Vector(z))
    return out


def frame(y, z_hint, pos=(0, 0, 0)):
    """Матрица 4x4 с осью Y вдоль y и осью Z как можно ближе к z_hint."""
    y = Vector(y).normalized()
    z = Vector(z_hint)
    z = (z - y * z.dot(y))
    if z.length < 1e-6:
        z = Vector((0, 0, 1)) if abs(y.z) < 0.9 else Vector((0, -1, 0))
        z = (z - y * z.dot(y))
    z.normalize()
    x = y.cross(z).normalized()
    m = Matrix((x, y, z)).transposed().to_4x4()
    m.translation = Vector(pos)
    return m


def aim_frame(pos, yaw=0.0, pitch=0.0, roll=0.0, up=None):
    """Рамка оружия: Y — куда смотрит ствол/клинок, Z — «верх» оружия.
    yaw>0 — влево (+X), pitch>0 — вверх, roll>0 — заваливание верха оружия вправо."""
    yw, pt = math.radians(yaw), math.radians(pitch)
    d = Vector((math.sin(yw) * math.cos(pt), -math.cos(yw) * math.cos(pt), math.sin(pt)))
    m = frame(d, up if up is not None else UP, pos)
    if roll:
        r = Matrix.Rotation(math.radians(roll), 4, d)
        rot = r @ m.to_3x3().to_4x4()
        rot.translation = Vector(pos)
        m = rot
    return m


class Rig:
    """Арматура персонажа + управляющие кости."""

    def __init__(self, name="Armature", cape=False, weapon=None):
        """weapon: dict(hand=Matrix руки в пространстве оружия, lhand=Matrix левой руки или None,
        blades=[(имя, Matrix в пространстве оружия, длина)])"""
        self.weapon = weapon
        self.cape = cape
        data = bpy.data.armatures.new(name)
        self.obj = obj = bpy.data.objects.new(name, data)
        bpy.context.scene.collection.objects.link(obj)
        bpy.context.view_layer.objects.active = obj
        for o in bpy.context.selected_objects:
            o.select_set(False)
        obj.select_set(True)
        bpy.ops.object.mode_set(mode="EDIT")
        sk = skeleton(cape)
        eb = {}
        for n, (h, t, _, z) in sk.items():
            b = data.edit_bones.new(n)
            b.head, b.tail = h, t
            b.align_roll(z)
            eb[n] = b
        for n, (_, _, p, _) in sk.items():
            if p:
                eb[n].parent = eb[p]
        self.deform = list(sk.keys())

        def by_matrix(n, m, length, parent):
            b = data.edit_bones.new(n)
            b.head = (0, 0, 0)
            b.tail = (0, length, 0)
            b.matrix = m
            if parent:
                b.parent = eb[parent]
            eb[n] = b
            return b

        if weapon:
            hand_r = eb["Hand.R"].matrix.copy()
            w_rest = hand_r @ weapon["hand"].inverted()
            self.w_rest = w_rest
            by_matrix("Weapon.R", w_rest, 0.12, "Hand.R")
            self.deform.append("Weapon.R")
            for bn, bm, ln in weapon.get("blades", []):
                by_matrix(bn, w_rest @ bm, ln, "Weapon.R")
                self.deform.append(bn)
        # управляющие кости: копии кистей и стоп без родителя, полюса
        self.controls = []
        for s in ("L", "R"):
            for src, dst in ((f"Hand.{s}", f"IK_Hand.{s}"), (f"Foot.{s}", f"IK_Foot.{s}")):
                by_matrix(dst, eb[src].matrix.copy(), eb[src].length, None)
                self.controls.append(dst)
            for pn in (f"Pole_Arm.{s}", f"Pole_Leg.{s}"):
                b = data.edit_bones.new(pn)
                b.head, b.tail = (0, 0, 0), (0, 0, 0.05)
                self.controls.append(pn)
        bpy.ops.object.mode_set(mode="POSE")
        self.rest = {b.name: b.matrix_local.copy() for b in data.bones}
        for pb in obj.pose.bones:
            pb.rotation_mode = "QUATERNION"
        P = obj.pose.bones
        for s in ("L", "R"):
            for chain, tgt, pole in ((f"LowerArm.{s}", f"IK_Hand.{s}", f"Pole_Arm.{s}"),
                                     (f"LowerLeg.{s}", f"IK_Foot.{s}", f"Pole_Leg.{s}")):
                c = P[chain].constraints.new("IK")
                c.target, c.subtarget = obj, tgt
                c.pole_target, c.pole_subtarget = obj, pole
                c.pole_angle = math.radians(-90 if "Arm" in chain else 90)
                c.chain_count = 2
                c.use_tail = True
                c.use_stretch = False
            for b, tgt in ((f"Hand.{s}", f"IK_Hand.{s}"), (f"Foot.{s}", f"IK_Foot.{s}")):
                c = P[b].constraints.new("COPY_ROTATION")
                c.target, c.subtarget = obj, tgt
        self.frames = {}  # клип -> [ {кость: Matrix} ]

    # ------------------------------------------------------------------ позы
    def _set_world(self, bone, m):
        self.obj.pose.bones[bone].matrix_basis = self.rest[bone].inverted() @ m

    def hand_world(self, side, m_weapon=None, free=None, grip=1.0):
        """Мировая матрица кисти: по оружию (правая всегда, левая при grip>0) или свободная."""
        if side == "R":
            return m_weapon @ self.weapon["hand"]
        target = free
        if self.weapon and self.weapon.get("lhand") is not None and grip > 0:
            g = m_weapon @ self.weapon["lhand"]
            if free is None or grip >= 1:
                return g
            return blend(free, g, grip)
        return target

    def apply(self, p):
        """Выставить позу p (Pose)."""
        P = self.obj.pose.bones
        for pb in P:
            pb.matrix_basis = Matrix.Identity(4)
        # бёдра
        hr = self.rest["Hips"]
        rot = (Matrix.Rotation(math.radians(p.hips_rot[2]), 4, "Z") @
               Matrix.Rotation(math.radians(p.hips_rot[1]), 4, "Y") @
               Matrix.Rotation(math.radians(p.hips_rot[0]), 4, "X"))
        m = rot @ hr
        m.translation = hr.translation + Vector(p.hips)
        self._set_world("Hips", m)
        for b, r in p.fk.items():
            if b in P:
                P[b].rotation_quaternion = Euler([math.radians(a) for a in r], "XYZ").to_quaternion()
        for b, sc in p.scale.items():
            if b in P:
                P[b].scale = (1, sc, 1)
        # руки
        if self.weapon and p.weapon is not None:
            self._set_world("IK_Hand.R", self.hand_world("R", p.weapon))
            lh = self.hand_world("L", p.weapon, p.lhand, p.lgrip)
        else:
            self._set_world("IK_Hand.R", p.rhand if p.rhand is not None else self.rest["Hand.R"])
            lh = p.lhand
        self._set_world("IK_Hand.L", lh if lh is not None else self.rest["Hand.L"])
        if p.weapon is not None and p.rhand is not None:
            self._set_world("IK_Hand.R", p.rhand)
        for s, sign in (("L", 1), ("R", -1)):
            pe = p.elbow_pole.get(s) or Vector((0.45 * sign, 0.45, 1.0))
            pk = p.knee_pole.get(s) or Vector((0.12 * sign, -0.8, 0.55))
            for pn, pos in ((f"Pole_Arm.{s}", pe), (f"Pole_Leg.{s}", pk)):
                m = self.rest[pn].copy()
                m.translation = Vector(pos)
                self._set_world(pn, m)
            f = p.feet.get(s)
            fr = self.rest[f"Foot.{s}"]
            if f is None:
                self._set_world(f"IK_Foot.{s}", fr)
            else:
                x, y, z, yaw, pitch = f
                piv = fr.translation
                m = (Matrix.Rotation(math.radians(yaw), 4, "Z") @
                     Matrix.Rotation(math.radians(pitch), 4, fr.col[0].xyz) @ fr.to_3x3().to_4x4())
                m.translation = Vector((x, y, z))
                self._set_world(f"IK_Foot.{s}", m)

    def record(self, clip, poses):
        """poses — список Pose по кадрам. Запоминает итоговые матрицы деформирующих костей."""
        out = []
        self.poses = getattr(self, "poses", {})
        self.poses[clip] = poses
        for p in poses:
            self.apply(p)
            bpy.context.view_layer.update()
            ev = self.obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
            out.append({b: ev.pose.bones[b].matrix.copy() for b in self.deform})
        self.frames[clip] = out
        return out

    # ------------------------------------------------------------------ запекание
    def basis_from(self, mats):
        """Из матриц в пространстве арматуры — локальные базисы (как matrix_basis)."""
        res = {}
        for b in self.deform:
            bone = self.obj.data.bones[b]
            R = self.rest[b]
            if bone.parent:
                pn = bone.parent.name
                local = (self.rest[pn].inverted() @ R).inverted() @ mats[pn].inverted() @ mats[b]
            else:
                local = R.inverted() @ mats[b]
            res[b] = local
        return res

    def pose_static(self, mats, obj=None):
        """Поставить запечённую позу на арматуру без ограничений (для превью-копий)."""
        obj = obj or self.obj
        for b, m in self.basis_from(mats).items():
            obj.pose.bones[b].matrix_basis = m

    def strip_controls(self):
        P = self.obj.pose.bones
        for pb in P:
            for c in list(pb.constraints):
                pb.constraints.remove(c)
            pb.matrix_basis = Matrix.Identity(4)
        bpy.context.view_layer.objects.active = self.obj
        bpy.ops.object.mode_set(mode="EDIT")
        for n in self.controls:
            self.obj.data.edit_bones.remove(self.obj.data.edit_bones[n])
        bpy.ops.object.mode_set(mode="OBJECT")

    def write_actions(self):
        self.obj.animation_data_create()
        acts = []
        for clip, frames in self.frames.items():
            act = bpy.data.actions.new(clip)
            act.use_fake_user = True
            data = {b: ([], [], []) for b in self.deform}
            prev = {}
            for mats in frames:
                for b, m in self.basis_from(mats).items():
                    loc, q, sc = m.decompose()
                    if b in prev and prev[b].dot(q) < 0:
                        q.negate()
                    prev[b] = q
                    data[b][0].append(loc)
                    data[b][1].append(q)
                    data[b][2].append(sc)
            n = len(frames)
            for b, (locs, qs, scs) in data.items():
                group = act.groups.new(b)
                for path, vals, dim in (("location", locs, 3), ("rotation_quaternion", qs, 4), ("scale", scs, 3)):
                    for i in range(dim):
                        fc = act.fcurves.new(f'pose.bones["{b}"].{path}', index=i, action_group=b)
                        fc.keyframe_points.add(n)
                        co = []
                        for f, v in enumerate(vals):
                            co += [f, v[i]]
                        fc.keyframe_points.foreach_set("co", co)
                        for kp in fc.keyframe_points:
                            kp.interpolation = "LINEAR"
                        fc.update()
            acts.append(act)
        self.obj.animation_data.action = acts[0] if acts else None
        return acts


def blend(a, b, t):
    la, qa, _ = a.decompose()
    lb, qb, _ = b.decompose()
    m = qa.slerp(qb, t).to_matrix().to_4x4()
    m.translation = la.lerp(lb, t)
    return m


class Pose:
    """Поза в пространстве персонажа (метры базового роста, градусы)."""

    def __init__(self):
        self.hips = Vector((0, 0, 0))       # смещение таза
        self.hips_rot = (0.0, 0.0, 0.0)     # наклон вперёд, вбок, поворот
        self.fk = {}                        # кость -> (сгиб вперёд, скручивание, наклон вбок)
        self.scale = {}                     # кость -> масштаб по длине (клинки)
        self.weapon = None                  # Matrix рамки оружия
        self.rhand = None                   # Matrix правой кисти (если без оружия)
        self.lhand = None                   # Matrix левой кисти (свободной)
        self.lgrip = 1.0                    # 1 — левая на оружии, 0 — свободна
        self.feet = {}                      # "L"/"R" -> (x, y, z, рыскание, наклон)
        self.elbow_pole = {}
        self.knee_pole = {}


def hand(fingers, thumb, wrist):
    """Матрица кисти: Y — от запястья к костяшкам, Z — куда смотрит большой палец."""
    return frame(fingers, thumb, wrist)
