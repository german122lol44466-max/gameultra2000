"""Оружие. Строится в «пространстве оружия»: начало — центр рукояти (где кулак правой руки),
+Y — ствол/клинок, +Z — верх оружия, +X — правый бок.

Каждая функция возвращает описание хвата: hand — матрица правой кисти, lhand — левой
(или None), blades — кости клинков (имя, матрица, длина). Детали добавляются в kit
к кости Weapon.R (клинки — к своим костям)."""

import math

from mathutils import Matrix, Vector

from .rig import hand

# правая кисть на пистолетной рукояти: кулак в начале координат, рукоять наклонена назад
RIFLE_HAND = hand((0, 1, -0.45), (0, 0.32, 1), (0, -0.065, 0.03))


def _rifle_lhand(y, z=-0.02):
    """Левая рука снизу-слева поддерживает цевьё на расстоянии y от рукояти."""
    return hand((0.85, 0.15, 0.45), (0, 1, 0), (-0.055, y - 0.02, z - 0.035))


def e11(k, bone="Weapon.R"):
    """Бластерная винтовка E-11: короткий ствол с кожухом, оптика, магазин слева, складной приклад."""
    B, D, G = "metal_dark", "metal_black", "metal_grey"
    # ствольная коробка
    k.cyl(bone, B, (0, -0.02, 0.06), (0, 0.18, 0.06), 0.022, verts=20)
    # кожух ствола с рёбрами и отверстиями
    k.cyl(bone, D, (0, 0.18, 0.06), (0, 0.36, 0.06), 0.017, verts=20)
    for i in range(7):
        y = 0.195 + i * 0.022
        k.torus(bone, B, (0, y, 0.06), 0.0175, 0.0035, axis=(0, 1, 0))
    for i in range(4):
        y = 0.2 + i * 0.04
        for s in (-1, 1):
            k.cyl(bone, "metal_black", (0.0165 * s, y, 0.06), (0.0185 * s, y, 0.06), 0.005, verts=8)
    # дульная насадка
    k.cyl(bone, B, (0, 0.36, 0.06), (0, 0.39, 0.06), 0.012, 0.014, verts=16)
    k.cyl(bone, "metal_black", (0, 0.389, 0.06), (0, 0.392, 0.06), 0.008, verts=12)
    # прицел
    k.cyl(bone, G, (0, 0.0, 0.098), (0, 0.13, 0.098), 0.011, verts=16)
    k.cyl(bone, B, (0, -0.006, 0.098), (0, 0.004, 0.098), 0.014, verts=16)
    k.cyl(bone, "lens_red", (0, -0.0065, 0.098), (0, -0.006, 0.098), 0.009, verts=16)
    k.box(bone, B, (0, 0.04, 0.083), (0.012, 0.01, 0.014))
    k.box(bone, B, (0, 0.10, 0.083), (0.012, 0.01, 0.014))
    # магазин слева (энергоячейка)
    k.cyl(bone, B, (-0.02, 0.055, 0.055), (-0.13, 0.055, 0.04), 0.011, verts=14)
    k.cyl(bone, G, (-0.13, 0.055, 0.04), (-0.137, 0.055, 0.039), 0.012, verts=14)
    # пистолетная рукоять и спуск
    k.box(bone, D, (0, -0.005, 0.0), (0.028, 0.03, 0.09), rot=(math.radians(-18), 0, 0), bevel=0.006)
    k.torus(bone, B, (0, 0.03, 0.025), 0.016, 0.0025, axis=(1, 0, 0))
    k.box(bone, B, (0, 0.03, 0.035), (0.004, 0.004, 0.015))
    # складной приклад (сложен поверх)
    k.cyl(bone, B, (0.012, -0.02, 0.075), (0.012, 0.16, 0.08), 0.0035, verts=8)
    k.cyl(bone, B, (-0.012, -0.02, 0.075), (-0.012, 0.16, 0.08), 0.0035, verts=8)
    k.box(bone, D, (0, -0.03, 0.07), (0.035, 0.015, 0.03))
    return dict(hand=RIFLE_HAND, lhand=_rifle_lhand(0.22, 0.045), blades=[], muzzle=Vector((0, 0.392, 0.06)))


def dlt19(k, bone="Weapon.R"):
    """Тяжёлый бластер DLT-19: длинный ребристый ствол, прицел, сошка, приклад."""
    B, D, G = "metal_dark", "metal_black", "metal_grey"
    k.box(bone, D, (0, 0.06, 0.06), (0.05, 0.22, 0.06), bevel=0.008)
    k.cyl(bone, B, (0, 0.17, 0.065), (0, 0.72, 0.065), 0.016, verts=18)
    for i in range(12):
        y = 0.2 + i * 0.035
        k.cyl(bone, D, (0, y, 0.065), (0, y + 0.012, 0.065), 0.024, verts=12)
    k.cyl(bone, B, (0, 0.72, 0.065), (0, 0.78, 0.065), 0.013, 0.017, verts=16)
    k.cyl(bone, "metal_black", (0, 0.779, 0.065), (0, 0.782, 0.065), 0.01, verts=12)
    # кожух сверху
    k.box(bone, B, (0, 0.33, 0.092), (0.018, 0.3, 0.012))
    # прицел
    k.cyl(bone, G, (0, 0.0, 0.118), (0, 0.15, 0.118), 0.014, verts=16)
    k.cyl(bone, "lens_red", (0, -0.001, 0.118), (0, 0.0, 0.118), 0.011, verts=16)
    k.box(bone, B, (0, 0.07, 0.1), (0.012, 0.05, 0.012))
    # энергоячейка снизу
    k.box(bone, G, (0, 0.12, 0.005), (0.035, 0.07, 0.05), bevel=0.006)
    # рукоять
    k.box(bone, D, (0, -0.005, 0.0), (0.03, 0.032, 0.095), rot=(math.radians(-18), 0, 0), bevel=0.006)
    k.torus(bone, B, (0, 0.03, 0.025), 0.017, 0.0025, axis=(1, 0, 0))
    # приклад
    k.box(bone, D, (0, -0.16, 0.045), (0.035, 0.22, 0.06), rot=(math.radians(4), 0, 0), bevel=0.01)
    k.box(bone, "rubber", (0, -0.27, 0.04), (0.04, 0.02, 0.075), bevel=0.006)
    # сошка (сложена)
    for s in (-1, 1):
        k.cyl(bone, B, (0.012 * s, 0.55, 0.045), (0.016 * s, 0.32, 0.04), 0.004, verts=8)
    # передняя рукоять для левой руки
    k.cyl(bone, D, (0, 0.34, 0.045), (0, 0.34, -0.03), 0.014, verts=14)
    lh = hand((0, 0.25, -1), (0.2, 1, 0.1), (0.0, 0.34, 0.005))
    lh = hand((1, 0.1, 0), (0, 0, 1), (-0.065, 0.34, -0.005))
    return dict(hand=RIFLE_HAND, lhand=lh, blades=[], muzzle=Vector((0, 0.782, 0.065)))


# ---------------------------------------------------------------------------- световые мечи

def _saber_hand(y):
    """Кулак вокруг рукояти: большой палец смотрит вдоль клинка."""
    return hand((0, 0, 1), (0, 1, 0), (0, y, -0.065))


def _ribs(k, bone, mat, y0, y1, r, n):
    for i in range(n):
        y = y0 + (y1 - y0) * (i + 0.5) / n
        k.torus(bone, mat, (0, y, 0), r, 0.0035, axis=(0, 1, 0))


def saber_vader(k, bone="Weapon.R"):
    """Рукоять в духе Вейдера: чёрные рёбра хвата, серебряный корпус, кнопки, раструб эмиттера."""
    S, B = "metal_silver", "metal_black"
    k.cyl(bone, S, (0, -0.13, 0), (0, 0.13, 0), 0.0165, verts=24)
    k.cyl(bone, B, (0, -0.125, 0), (0, 0.0, 0), 0.0175, verts=24)
    _ribs(k, bone, "rubber", -0.12, -0.005, 0.0178, 7)
    k.cyl(bone, B, (0, 0.02, 0), (0, 0.06, 0), 0.0175, verts=24)
    k.box(bone, B, (0.0, 0.04, 0.019), (0.012, 0.03, 0.006))
    for i, c in enumerate(("btn_red", "btn_white", "btn_red")):
        k.cyl(bone, c, (0, 0.03 + i * 0.01, 0.021), (0, 0.03 + i * 0.01, 0.025), 0.003, verts=10)
    k.cyl(bone, S, (0, 0.13, 0), (0, 0.155, 0), 0.0165, 0.021, verts=24)
    k.cyl(bone, B, (0, 0.154, 0), (0, 0.156, 0), 0.016, verts=24)
    k.torus(bone, S, (0, -0.13, 0), 0.012, 0.004, axis=(1, 0, 0))
    return dict(hand=_saber_hand(-0.02), lhand=_saber_hand(-0.105), blades=[("Blade.R", Matrix.Translation((0, 0.155, 0)), 1.0)])


def saber_dooku(k, bone="Weapon.R"):
    """Изогнутая рукоять (фехтовальный хват): эмиттер наклонён относительно рукояти."""
    S, D, L = "metal_silver", "metal_black", "leather_brown"
    k.cyl(bone, L, (0, -0.10, 0), (0, 0.06, 0), 0.016, verts=20)
    _ribs(k, bone, S, -0.09, 0.05, 0.0168, 3)
    k.cyl(bone, S, (0, -0.125, 0), (0, -0.10, 0), 0.012, 0.017, verts=20)
    k.sphere(bone, S, (0, -0.128, 0), 0.012, seg=12)
    # изгиб к эмиттеру
    a = math.radians(14)
    d = Vector((0, math.cos(a), -math.sin(a)))
    p0 = Vector((0, 0.06, 0))
    k.cyl(bone, S, p0, p0 + d * 0.07, 0.0175, verts=20)
    k.torus(bone, D, p0 + d * 0.03, 0.0178, 0.003, axis=d)
    k.cyl(bone, S, p0 + d * 0.07, p0 + d * 0.085, 0.019, 0.014, verts=20)
    k.box(bone, D, (0, 0.085, 0.015), (0.01, 0.02, 0.008))
    m = Matrix.Translation(p0 + d * 0.085) @ Matrix.Rotation(-a, 4, "X")
    return dict(hand=_saber_hand(-0.02), lhand=_saber_hand(-0.105), blades=[("Blade.R", m, 1.0)])


def saber_sidious(k, bone="Weapon.R"):
    """Компактная серебристая рукоять с золотыми кольцами."""
    S, Gd, B = "metal_silver", "metal_gold", "metal_black"
    k.lathe(bone, S, (0, -0.12, 0), (0, 0.12, 0),
            [(0, 0.0), (0.0, 0.012), (0.01, 0.016), (0.07, 0.015), (0.16, 0.0165), (0.22, 0.0185), (0.24, 0.0165), (0.24, 0.0)], verts=24)
    for y in (-0.095, -0.06, 0.06, 0.1):
        k.torus(bone, Gd, (0, y, 0), 0.0162, 0.003, axis=(0, 1, 0))
    k.cyl(bone, B, (0, 0.119, 0), (0, 0.121, 0), 0.014, verts=20)
    return dict(hand=_saber_hand(-0.02), lhand=_saber_hand(-0.105), blades=[("Blade.R", Matrix.Translation((0, 0.12, 0)), 0.9)])


def saber_maul(k, bone="Weapon.R"):
    """Двойная рукоять-посох: две соединённые рукояти, клинки с обоих концов."""
    S, B, R = "metal_silver", "metal_black", "rubber"
    k.cyl(bone, B, (0, -0.27, 0), (0, 0.27, 0), 0.0165, verts=24)
    for s in (-1, 1):
        k.cyl(bone, S, (0, 0.2 * s, 0), (0, 0.27 * s, 0), 0.017, verts=24)
        k.cyl(bone, S, (0, 0.27 * s, 0), (0, 0.285 * s, 0), 0.017, 0.02, verts=24)
        k.cyl(bone, B, (0, 0.284 * s, 0), (0, 0.286 * s, 0), 0.0155, verts=20)
        _ribs(k, bone, R, 0.04 * s, 0.16 * s, 0.0172, 6)
        k.box(bone, S, (0, 0.18 * s, 0.018), (0.008, 0.03, 0.006))
    k.torus(bone, S, (0, 0, 0), 0.0175, 0.004, axis=(0, 1, 0))
    up = Matrix.Translation((0, 0.286, 0))
    down = Matrix.Translation((0, -0.286, 0)) @ Matrix.Rotation(math.pi, 4, "X")
    return dict(hand=_saber_hand(0.06), lhand=_saber_hand(-0.1),
                blades=[("Blade.R", up, 1.0), ("Blade.R2", down, 1.0)])


def blade(k, bone, length, mat="blade", glow=None):
    """Клинок вдоль оси Y кости (в её локальном пространстве — задаётся kit.xform)."""
    k.lathe(bone, mat, (0, 0, 0), (0, length, 0),
            [(0, 0.0), (0.0, 0.0105), (0.03, 0.0115), (length * 0.97, 0.0115), (length, 0.007), (length + 0.008, 0.0)], verts=16)
    if glow:
        k.lathe(bone, glow, (0, -0.01, 0), (0, length, 0),
                [(0, 0.0), (0.0, 0.032), (length * 0.98, 0.034), (length + 0.03, 0.0)], verts=16, caps=False)


def dc15a(k, bone="Weapon.R"):
    """DC-15A клонов: длинный ствол с кожухом, прицел, приклад, магазин снизу."""
    B, D, G, L = "metal_dark", "metal_black", "metal_grey", "metal_silver"
    k.box(bone, D, (0, 0.06, 0.06), (0.042, 0.24, 0.055), bevel=0.008)
    k.cyl(bone, G, (0, 0.18, 0.065), (0, 0.64, 0.065), 0.018, verts=20)
    for i in range(9):
        y = 0.21 + i * 0.045
        k.torus(bone, B, (0, y, 0.065), 0.0185, 0.004, axis=(0, 1, 0))
        k.box(bone, D, (0.0, y + 0.02, 0.087), (0.008, 0.02, 0.006))
    k.cyl(bone, B, (0, 0.64, 0.065), (0, 0.7, 0.065), 0.014, 0.017, verts=16)
    k.cyl(bone, "metal_black", (0, 0.699, 0.065), (0, 0.702, 0.065), 0.01, verts=12)
    k.box(bone, B, (0, 0.38, 0.042), (0.03, 0.22, 0.016), bevel=0.004)
    k.cyl(bone, G, (0, -0.01, 0.11), (0, 0.16, 0.11), 0.013, verts=16)
    k.cyl(bone, B, (0, -0.015, 0.11), (0, -0.005, 0.11), 0.016, verts=16)
    k.cyl(bone, "lens_red", (0, -0.0155, 0.11), (0, -0.015, 0.11), 0.011, verts=16)
    for y in (0.02, 0.12):
        k.box(bone, B, (0, y, 0.094), (0.014, 0.012, 0.018))
    k.box(bone, G, (0, 0.11, 0.0), (0.028, 0.06, 0.07), rot=(math.radians(8), 0, 0), bevel=0.005)
    k.box(bone, D, (0, -0.005, 0.0), (0.03, 0.032, 0.095), rot=(math.radians(-18), 0, 0), bevel=0.006)
    k.torus(bone, B, (0, 0.03, 0.025), 0.017, 0.0025, axis=(1, 0, 0))
    k.box(bone, D, (0, -0.17, 0.05), (0.034, 0.22, 0.055), rot=(math.radians(3), 0, 0), bevel=0.012)
    k.box(bone, "rubber", (0, -0.282, 0.045), (0.038, 0.016, 0.07), bevel=0.005)
    lh = hand((0.85, 0.15, 0.45), (0, 1, 0), (-0.06, 0.32, 0.015))
    return dict(hand=RIFLE_HAND, lhand=lh, blades=[], muzzle=Vector((0, 0.702, 0.065)))


def saber_obiwan(k, bone="Weapon.R"):
    """Рукоять Оби-Вана: серебро, чёрные накладки хвата, раструб эмиттера, кольцо навершия."""
    S, B = "metal_silver", "metal_black"
    k.lathe(bone, S, (0, -0.135, 0), (0, 0.135, 0),
            [(0, 0.0), (0.0, 0.014), (0.015, 0.017), (0.03, 0.0165), (0.17, 0.0165), (0.2, 0.019), (0.235, 0.0175),
             (0.25, 0.021), (0.27, 0.021), (0.27, 0.0)], verts=28)
    for i in range(6):
        y = -0.1 + i * 0.022
        k.box(bone, B, (0.0, y, 0.0), (0.038, 0.012, 0.038), bevel=0.004, smooth=True)
    k.box(bone, B, (0.0, 0.06, 0.018), (0.012, 0.03, 0.006))
    k.cyl(bone, "btn_red", (0, 0.055, 0.02), (0, 0.055, 0.023), 0.003, verts=10)
    k.torus(bone, S, (0, -0.135, 0), 0.01, 0.003, axis=(1, 0, 0))
    return dict(hand=_saber_hand(-0.03), lhand=_saber_hand(-0.11), blades=[("Blade.R", Matrix.Translation((0, 0.135, 0)), 1.0)])


def saber_mace(k, bone="Weapon.R"):
    """Рукоять Мейса Винду: электрум (золото) и чёрные рёбра."""
    Gd, B = "metal_gold", "metal_black"
    k.lathe(bone, Gd, (0, -0.13, 0), (0, 0.13, 0),
            [(0, 0.0), (0.0, 0.015), (0.02, 0.0175), (0.2, 0.0165), (0.22, 0.019), (0.26, 0.0185), (0.26, 0.0)], verts=28)
    _ribs(k, bone, B, -0.1, 0.04, 0.0172, 8)
    k.box(bone, B, (0.0, 0.08, 0.018), (0.01, 0.04, 0.006))
    return dict(hand=_saber_hand(-0.03), lhand=_saber_hand(-0.11), blades=[("Blade.R", Matrix.Translation((0, 0.13, 0)), 1.0)])


def saber_anakin(k, bone="Weapon.R"):
    """Рукоять Энакина (ROTS): серебро, ребристый чёрный хват, «крылья» у эмиттера."""
    S, B = "metal_silver", "metal_black"
    k.lathe(bone, S, (0, -0.14, 0), (0, 0.14, 0),
            [(0, 0.0), (0.0, 0.013), (0.02, 0.0165), (0.18, 0.0165), (0.2, 0.0185), (0.26, 0.0185), (0.28, 0.0195), (0.28, 0.0)], verts=28)
    _ribs(k, bone, B, -0.11, 0.03, 0.0175, 9)
    for sg in (1, -1):
        k.box(bone, S, (0.02 * sg, 0.11, 0.0), (0.008, 0.035, 0.012), bevel=0.002)
    k.box(bone, B, (0.0, 0.06, 0.019), (0.01, 0.025, 0.005))
    return dict(hand=_saber_hand(-0.04), lhand=_saber_hand(-0.12), blades=[("Blade.R", Matrix.Translation((0, 0.14, 0)), 1.0)])


WEAPONS = {
    "E11": e11,
    "DLT19": dlt19,
    "Saber_Vader": saber_vader,
    "Saber_Dooku": saber_dooku,
    "Saber_Sidious": saber_sidious,
    "Saber_Maul": saber_maul,
    "DC15A": dc15a,
    "Saber_ObiWan": saber_obiwan,
    "Saber_Mace": saber_mace,
    "Saber_Anakin": saber_anakin,
}
