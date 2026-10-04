"""
Скелет v2 на суставах MakeHuman: корпус, шея, голова, челюсть, глаза, ключицы, руки с пальцами (по 3 фаланги),
ноги со стопой и носком, плюс Weapon.R / Blade.R* / Cape.*. Имена совместимы с Unity Humanoid
(сопоставление задаётся в Unity-скрипте импорта). Скелет переводится в T-позу, чтобы на него
ложились любые humanoid-анимации (Mixamo и т. п.).

Соглашение осей как в v1: Y — вдоль кости, Z — «вперёд» (у пальцев — наружу из ладони),
поворот +X — сгибание.
"""

import math

import bpy
from mathutils import Euler, Matrix, Quaternion, Vector

from .rig import aim_frame, blend, frame, hand  # noqa: F401  (переэкспорт для анимаций)

FWD = Vector((0, -1, 0))
UP = Vector((0, 0, 1))
FINGERS = ("Thumb", "Index", "Middle", "Ring", "Little")


def skel_from_body(body, cape=False):
    """Словарь костей: имя -> (голова, хвост, родитель, вектор оси Z)."""
    E = {k: (Vector(a), Vector(b)) for k, (a, b) in body.ends.items()}
    S = {}
    pelvis = (E["root"][0] + E["pelvis.L"][0] * 0 + E["spine05"][0]) * 0.5
    S["Root"] = (Vector((0, 0, 0)), Vector((0, 0.25, 0)), None, UP)
    hip_c = (E["upperleg01.L"][0] + E["upperleg01.R"][0]) * 0.5
    hips_head = Vector((0, hip_c.y, hip_c.z + 0.02))
    S["Hips"] = (hips_head, E["spine05"][1].copy(), "Root", FWD)
    S["Spine"] = (E["spine05"][1].copy(), E["spine03"][0].copy(), "Hips", FWD)
    S["Chest"] = (E["spine03"][0].copy(), E["spine01"][0].copy(), "Spine", FWD)
    S["UpperChest"] = (E["spine01"][0].copy(), E["neck01"][0].copy(), "Chest", FWD)
    S["Neck"] = (E["neck01"][0].copy(), E["head"][0].copy(), "UpperChest", FWD)
    S["Head"] = (E["head"][0].copy(), E["head"][1].copy(), "Neck", FWD)
    S["Jaw"] = (E["jaw"][0].copy(), E["jaw"][1].copy(), "Head", UP)
    for s in ("L", "R"):
        S[f"Eye.{s}"] = (E[f"eye.{s}"][0].copy(), E[f"eye.{s}"][0] + Vector((0, -0.025, 0)), "Head", UP)
        S[f"Shoulder.{s}"] = (E[f"clavicle.{s}"][0].copy(), E[f"upperarm01.{s}"][0].copy(), "UpperChest", FWD)
        S[f"UpperArm.{s}"] = (E[f"upperarm01.{s}"][0].copy(), E[f"lowerarm01.{s}"][0].copy(), f"Shoulder.{s}", FWD)
        S[f"LowerArm.{s}"] = (E[f"lowerarm01.{s}"][0].copy(), E[f"wrist.{s}"][0].copy(), f"UpperArm.{s}", FWD)
        wrist = E[f"wrist.{s}"][0]
        knuckle = E[f"finger3-1.{s}"][0]
        S[f"Hand.{s}"] = (wrist.copy(), knuckle.copy(), f"LowerArm.{s}", FWD)
        # нормаль ладони (наружу из ладони)
        a = E[f"finger2-1.{s}"][0] - wrist
        b = E[f"finger5-1.{s}"][0] - wrist
        n = a.cross(b).normalized()
        sign = 1 if s == "L" else -1
        if n.x * sign > 0:          # ладонь левой руки смотрит к телу (-X)
            n = -n
        for fi, fn in enumerate(FINGERS):
            for k in (1, 2, 3):
                h, t = E[f"finger{fi + 1}-{k}.{s}"]
                par = f"Hand.{s}" if k == 1 else f"{fn}{k - 1}.{s}"
                z = n if fn != "Thumb" else (n + FWD * 0.0)
                S[f"{fn}{k}.{s}"] = (h.copy(), t.copy(), par, z.copy())
        S[f"UpperLeg.{s}"] = (E[f"upperleg01.{s}"][0].copy(), E[f"lowerleg01.{s}"][0].copy(), "Hips", FWD)
        S[f"LowerLeg.{s}"] = (E[f"lowerleg01.{s}"][0].copy(), E[f"foot.{s}"][0].copy(), f"UpperLeg.{s}", FWD)
        S[f"Foot.{s}"] = (E[f"foot.{s}"][0].copy(), E[f"foot.{s}"][1].copy(), f"LowerLeg.{s}", UP)
        ft = E[f"foot.{s}"][1]
        S[f"Toes.{s}"] = (ft.copy(), ft + Vector((0, -0.06, 0)), f"Foot.{s}", UP)
    if cape:
        top = S["UpperChest"][1] + Vector((0, 0.12, -0.04))
        z = top.z
        y = top.y
        S["Cape.1"] = (Vector((0, y, z)), Vector((0, y + 0.04, z * 0.75)), "UpperChest", FWD)
        S["Cape.2"] = (Vector((0, y + 0.04, z * 0.75)), Vector((0, y + 0.08, z * 0.46)), "Cape.1", FWD)
        S["Cape.3"] = (Vector((0, y + 0.08, z * 0.46)), Vector((0, y + 0.12, 0.1)), "Cape.2", FWD)
    return S


def build_armature(name, S):
    data = bpy.data.armatures.new(name)
    obj = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(obj)
    _activate(obj)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = {}
    for n, (h, t, _, z) in S.items():
        b = data.edit_bones.new(n)
        b.head, b.tail = h, t
        b.align_roll(z)
        eb[n] = b
    for n, (_, _, p, _) in S.items():
        if p:
            eb[n].parent = eb[p]
    bpy.ops.object.mode_set(mode="OBJECT")
    data.display_type = "STICK"
    return obj


def _activate(obj):
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    for o in bpy.context.selected_objects:
        o.select_set(False)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)


def bind(meshes, arm):
    for m in meshes:
        m.parent = arm
        mod = m.modifiers.get("Armature") or m.modifiers.new("Armature", "ARMATURE")
        mod.object = arm
        # модификатор скелета — первым (до subsurf и т. п.)
        while m.modifiers.find("Armature") > 0:
            bpy.context.view_layer.objects.active = m
            bpy.ops.object.modifier_move_up(modifier="Armature")


def to_tpose(arm, meshes):
    """Повернуть руки горизонтально (ладони вниз) и сделать это положением покоя (меши запекаются)."""
    _activate(arm)
    bpy.ops.object.mode_set(mode="POSE")
    P = arm.pose.bones
    for pb in P:
        pb.rotation_mode = "QUATERNION"
    for s, sx in (("L", 1), ("R", -1)):
        for b in (f"UpperArm.{s}", f"LowerArm.{s}", f"Hand.{s}"):
            bpy.context.view_layer.update()
            pb = P[b]
            cur = pb.matrix.copy()
            y = cur.col[1].xyz.normalized()
            # локоть чуть согнут вперёд — иначе IK не знает, куда сгибать прямую руку
            want = Vector((sx, -0.14, 0)).normalized() if b.startswith(("LowerArm", "Hand")) else Vector((sx, 0, 0))
            q = y.rotation_difference(want)
            m = q.to_matrix().to_4x4() @ cur.to_3x3().to_4x4()
            m.translation = cur.translation
            pb.matrix = m
    bpy.context.view_layer.update()
    bpy.ops.object.mode_set(mode="OBJECT")
    # запечь деформацию в меши
    for m in meshes:
        _activate(m)
        mods = [md for md in m.modifiers]
        # сохраняем остальные модификаторы: применяем только Armature
        bpy.ops.object.modifier_apply(modifier="Armature")
    _activate(arm)
    bpy.ops.object.mode_set(mode="POSE")
    bpy.ops.pose.armature_apply(selected=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    bind(meshes, arm)


# ============================================================================ риг с управлением

FINGER_CURL = {  # градусы на фалангу при полном сжатии (основная, средняя, ногтевая)
    "Thumb": (15, 35, 40), "Index": (75, 95, 55), "Middle": (80, 100, 60), "Ring": (85, 100, 60), "Little": (90, 100, 60)}


class Rig:
    """Управляющие кости поверх готовой арматуры (после T-позы)."""

    def __init__(self, arm, weapon=None, deform_extra=()):
        self.obj = obj = arm
        self.weapon = weapon
        data = arm.data
        _activate(arm)
        bpy.ops.object.mode_set(mode="EDIT")
        eb = data.edit_bones
        self.deform = [b.name for b in eb]

        def by_matrix(n, m, length, parent):
            b = eb.new(n)
            b.head = (0, 0, 0)
            b.tail = (0, length, 0)
            b.matrix = m
            if parent:
                b.parent = eb[parent]
            return b

        if weapon:
            w_rest = eb["Hand.R"].matrix.copy() @ weapon["hand"].inverted()
            self.w_rest = w_rest
            by_matrix("Weapon.R", w_rest, 0.12, "Hand.R")
            self.deform.append("Weapon.R")
            for bn, bm, ln in weapon.get("blades", []):
                by_matrix(bn, w_rest @ bm, ln, "Weapon.R")
                self.deform.append(bn)
        self.controls = []
        for s in ("L", "R"):
            for src, dst in ((f"Hand.{s}", f"IK_Hand.{s}"), (f"Foot.{s}", f"IK_Foot.{s}")):
                by_matrix(dst, eb[src].matrix.copy(), eb[src].length, None)
                self.controls.append(dst)
            for pn in (f"Pole_Arm.{s}", f"Pole_Leg.{s}"):
                b = eb.new(pn)
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
        bpy.ops.object.mode_set(mode="OBJECT")
        self.frames = {}
        self.poses = {}
        self._calibrate_poles()

    def _calibrate_poles(self):
        """Подбор pole_angle для каждой цепи (зависит от ролла костей после T-позы)."""
        P = self.obj.pose.bones
        for s, sx in (("L", 1), ("R", -1)):
            for chain, up, tgt, pole, ppos, tpos in (
                    (f"LowerArm.{s}", f"UpperArm.{s}", f"IK_Hand.{s}", f"Pole_Arm.{s}",
                     Vector((0.45 * sx, 0.5, 1.0)), Vector((0.25 * sx, -0.25, 1.15))),
                    (f"LowerLeg.{s}", f"UpperLeg.{s}", f"IK_Foot.{s}", f"Pole_Leg.{s}",
                     Vector((0.12 * sx, -0.8, 0.55)), None)):
                best = None
                for ang in range(-180, 180, 15):
                    P[chain].constraints[0].pole_angle = math.radians(ang)
                    for pb in P:
                        pb.matrix_basis = Matrix.Identity(4)
                    m = self.rest[pole].copy()
                    m.translation = ppos
                    self._set_world(pole, m)
                    if tpos is not None:
                        mh = self.rest[tgt].copy()
                        mh.translation = tpos
                        self._set_world(tgt, mh)
                    else:
                        mf = self.rest[tgt].copy()
                        mf.translation = mf.translation + Vector((0, -0.12, 0.25))
                        self._set_world(tgt, mf)
                    bpy.context.view_layer.update()
                    ev = self.obj.evaluated_get(bpy.context.evaluated_depsgraph_get()).pose.bones
                    a, mid, b = ev[up].head, ev[chain].head, ev[chain].tail
                    ax = (b - a).normalized()

                    def perp(v):
                        v = v - a
                        return (v - ax * v.dot(ax)).normalized()
                    score = perp(mid).dot(perp(ppos))
                    if best is None or score > best[0]:
                        best = (score, ang)
                P[chain].constraints[0].pole_angle = math.radians(best[1])
        for pb in P:
            pb.matrix_basis = Matrix.Identity(4)

    # ------------------------------------------------------------------ позы
    def _set_world(self, bone, m):
        self.obj.pose.bones[bone].matrix_basis = self.rest[bone].inverted() @ m

    def hand_world(self, side, m_weapon=None, free=None, grip=1.0):
        if side == "R":
            return m_weapon @ self.weapon["hand"]
        if self.weapon and self.weapon.get("lhand") is not None and grip > 0:
            g = m_weapon @ self.weapon["lhand"]
            if free is None or grip >= 1:
                return g
            return blend(free, g, grip)
        return free

    def _fingers(self, side, curl, trigger=None, spread=0.0, thumb=None):
        P = self.obj.pose.bones
        for fn in FINGERS:
            c = curl if not (fn == "Index" and trigger is not None) else trigger
            if fn == "Thumb" and thumb is not None:
                c = thumb
            angs = FINGER_CURL[fn]
            for k in (1, 2, 3):
                b = f"{fn}{k}.{side}"
                if b not in P:
                    continue
                sp = 0.0
                if k == 1 and fn != "Thumb":
                    sp = {"Index": -8, "Middle": 0, "Ring": 6, "Little": 12}[fn] * spread
                P[b].rotation_quaternion = Euler((math.radians(angs[k - 1] * c), 0, math.radians(sp)), "XYZ").to_quaternion()

    def apply(self, p):
        P = self.obj.pose.bones
        for pb in P:
            pb.matrix_basis = Matrix.Identity(4)
        hr = self.rest["Hips"]
        rot = (Matrix.Rotation(math.radians(p.hips_rot[2]), 4, "Z") @
               Matrix.Rotation(math.radians(p.hips_rot[1]), 4, "Y") @
               Matrix.Rotation(math.radians(p.hips_rot[0]), 4, "X"))
        m = rot @ hr
        m.translation = hr.translation + Vector(p.hips)
        self._set_world("Hips", m)
        fk = dict(p.fk)
        if "Chest" in fk and "UpperChest" not in fk:
            c = fk["Chest"]
            fk["Chest"] = tuple(x * 0.55 for x in c)
            fk["UpperChest"] = tuple(x * 0.45 for x in c)
        for b, r in fk.items():
            if b in P:
                P[b].rotation_quaternion = Euler([math.radians(a) for a in r], "XYZ").to_quaternion()
        for b, sc in p.scale.items():
            if b in P:
                P[b].scale = (1, sc, 1)
        # пальцы
        for s in ("L", "R"):
            f = p.fingers.get(s, (1.0, None, 0.0, None))
            self._fingers(s, *f)
        # руки
        if self.weapon and p.weapon is not None:
            self._set_world("IK_Hand.R", self.hand_world("R", p.weapon))
            lh = self.hand_world("L", p.weapon, p.lhand, p.lgrip)
        else:
            self._set_world("IK_Hand.R", p.rhand if p.rhand is not None else self.rest["Hand.R"])
            lh = p.lhand
        self._set_world("IK_Hand.L", lh if lh is not None else self.rest["Hand.L"])
        if p.rhand is not None:
            self._set_world("IK_Hand.R", p.rhand)
        # ключица помогает руке: часть поворота от «руки в стороны» к направлению на кисть
        for s in ("L", "R"):
            tgt = (self.rest[f"IK_Hand.{s}"] @ P[f"IK_Hand.{s}"].matrix_basis).translation
            sh = self.rest[f"UpperArm.{s}"].translation
            d = (tgt - sh)
            if d.length < 1e-4:
                continue
            rest_dir = self.rest[f"UpperArm.{s}"].col[1].xyz.normalized()
            q = rest_dir.rotation_difference(d.normalized())
            ang = min(q.angle * 0.18, math.radians(14))
            if ang < 1e-3:
                continue
            qw = Quaternion(q.axis, ang)
            R = self.rest[f"Shoulder.{s}"].to_quaternion()
            P[f"Shoulder.{s}"].rotation_quaternion = R.inverted() @ qw @ R
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
                m = (Matrix.Rotation(math.radians(yaw), 4, "Z") @
                     Matrix.Rotation(math.radians(pitch), 4, fr.col[0].xyz) @ fr.to_3x3().to_4x4())
                m.translation = Vector((x, y, z))
                self._set_world(f"IK_Foot.{s}", m)
                # носок остаётся на земле при подъёме пятки
                if pitch < 0 and f"Toes.{s}" in P:
                    P[f"Toes.{s}"].rotation_quaternion = Euler((math.radians(pitch * 0.9), 0, 0), "XYZ").to_quaternion()

    def record(self, clip, poses):
        out = []
        self.poses[clip] = poses
        for p in poses:
            self.apply(p)
            bpy.context.view_layer.update()
            ev = self.obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
            mats = {b: ev.pose.bones[b].matrix.copy() for b in self.deform}
            self._distribute_twist(mats)
            out.append(mats)
        self.frames[clip] = out
        return out

    def _distribute_twist(self, mats, k=0.5):
        """Половину скручивания кисти передаём предплечью (меньше «конфетной обёртки» на запястье)."""
        for s in ("L", "R"):
            la, ha = f"LowerArm.{s}", f"Hand.{s}"
            if la not in mats or ha not in mats:
                continue
            rel = (self.rest[la].inverted() @ self.rest[ha])
            local = (mats[la] @ rel).inverted() @ mats[ha]         # поворот кисти относительно «покоя на предплечье»
            q = local.to_quaternion()
            # скручивание вокруг оси Y кисти (разложение swing-twist)
            ang = 2.0 * math.atan2(q.y, q.w)
            if ang > math.pi:
                ang -= 2 * math.pi
            elif ang < -math.pi:
                ang += 2 * math.pi
            ax_world = (mats[la].to_3x3() @ Vector((0, 1, 0))).normalized()
            R = Matrix.Rotation(ang * k, 4, ax_world)
            m = mats[la].copy()
            t = m.translation.copy()
            m = R @ m
            m.translation = t
            mats[la] = m

    # ------------------------------------------------------------------ запекание (как в v1)
    def basis_from(self, mats):
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

    def pose_baked(self, mats):
        """Поставить запечённую позу (после strip_controls — без ограничений)."""
        for b, m in self.basis_from(mats).items():
            self.obj.pose.bones[b].matrix_basis = m

    def strip_controls(self):
        P = self.obj.pose.bones
        for pb in P:
            for c in list(pb.constraints):
                pb.constraints.remove(c)
            pb.matrix_basis = Matrix.Identity(4)
        _activate(self.obj)
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
                act.groups.new(b)
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


class Pose:
    def __init__(self):
        self.hips = Vector((0, 0, 0))
        self.hips_rot = (0.0, 0.0, 0.0)
        self.fk = {}
        self.scale = {}
        self.weapon = None
        self.rhand = None
        self.lhand = None
        self.lgrip = 1.0
        self.feet = {}
        self.elbow_pole = {}
        self.knee_pole = {}
        self.fingers = {}   # "L"/"R" -> (сжатие 0..1, указательный или None, разведение, большой или None)
