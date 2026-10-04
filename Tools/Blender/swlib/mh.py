"""
Реалистичное тело на основе MakeHuman (базовый меш hm08, таргеты, скелет и веса — CC0,
https://github.com/makehumancommunity/makehuman). Файлы скачиваются один раз в Tools/Blender/makehuman.

Пространство MakeHuman: Y — вверх, +Z — лицо, единица — дециметр. В Blender переводим в
(x, -z, y) * 0.1: персонаж смотрит в -Y, левая сторона — +X, рост в метрах.
"""

import json
import os
import urllib.request

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(HERE, "..", "makehuman")
URL = "https://raw.githubusercontent.com/makehumancommunity/makehuman/master/makehuman/data/"


def fetch(rel):
    path = os.path.join(DATA, rel)
    if not os.path.exists(path):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with urllib.request.urlopen(URL + rel.replace("|", "%7C"), timeout=60) as r, open(path, "wb") as f:
            f.write(r.read())
    return path


def to_blender(v):
    v = np.asarray(v, dtype=np.float64)
    return np.stack([v[..., 0], -v[..., 2], v[..., 1]], axis=-1) * 0.1


# ----------------------------------------------------------------------------- базовый меш

_BASE = None


def base():
    """Вершины (все, включая помощники), UV и грани по группам."""
    global _BASE
    if _BASE is not None:
        return _BASE
    verts, uvs, groups = [], [], {}
    cur = None
    with open(fetch("3dobjs/base.obj")) as f:
        for line in f:
            if line.startswith("v "):
                verts.append([float(x) for x in line.split()[1:4]])
            elif line.startswith("vt "):
                uvs.append([float(x) for x in line.split()[1:3]])
            elif line.startswith("g "):
                cur = line.split()[1]
                groups.setdefault(cur, [])
            elif line.startswith("f "):
                face = []
                for tok in line.split()[1:]:
                    p = tok.split("/")
                    face.append((int(p[0]) - 1, int(p[1]) - 1 if len(p) > 1 and p[1] else -1))
                groups[cur].append(face)
    _BASE = (np.array(verts), np.array(uvs), groups)
    return _BASE


def load_target(name):
    """name — путь таргета без расширения, например 'macrodetails/caucasian-male-young'."""
    idx, d = [], []
    with open(fetch(f"targets/{name}.target")) as f:
        for line in f:
            if not line.strip() or line.startswith("#"):
                continue
            p = line.split()
            idx.append(int(p[0]))
            d.append([float(p[1]), float(p[2]), float(p[3])])
    return np.array(idx, dtype=np.int64), np.array(d)


def shaped(targets):
    """Вершины базового меша с применёнными таргетами [(имя, вес)] (в пространстве MakeHuman)."""
    v = base()[0].copy()
    for name, w in targets:
        if abs(w) < 1e-6:
            continue
        if "|" in name:
            # двусторонний модификатор: 'head/head-age-decr|incr' с весом -1..1
            stem, pair = name.rsplit("/", 1)
            a, b = pair.split("|")
            left, right = a, a.rsplit("-", 1)[0] + "-" + b
            name, w = (f"{stem}/{right}", w) if w > 0 else (f"{stem}/{left}", -w)
        i, d = load_target(name)
        if len(i):
            v[i] += d * w
    return v


def macro(gender="male", age="young", muscle=0.5, weight=0.5, height=0.5, race="caucasian", proportions=1.0):
    """Макро-параметры MakeHuman -> список таргетов. muscle/weight/height 0..1 (0.5 — среднее)."""
    def split(x, lo, mid, hi):
        if x >= 0.5:
            return [(mid, 1 - (x - 0.5) * 2), (hi, (x - 0.5) * 2)]
        return [(lo, 1 - x * 2), (mid, x * 2)]
    ts = [(f"macrodetails/{race}-{gender}-{age}", 1.0)]
    for m, wm in split(muscle, "minmuscle", "averagemuscle", "maxmuscle"):
        for wt, ww in split(weight, "minweight", "averageweight", "maxweight"):
            if wm * ww > 1e-4:
                ts.append((f"macrodetails/universal-{gender}-{age}-{m}-{wt}", wm * ww))
                if abs(height - 0.5) > 1e-3:
                    hn = "maxheight" if height > 0.5 else "minheight"
                    ts.append((f"macrodetails/height/{gender}-{age}-{m}-{wt}-{hn}", wm * ww * abs(height - 0.5) * 2))
                if proportions > 0 and age in ("young",):
                    ts.append((f"macrodetails/proportions/{gender}-{age}-{m}-{wt}-idealproportions", wm * ww * proportions))
    return ts


# ----------------------------------------------------------------------------- скелет и веса

_SKEL = None


def skeleton():
    global _SKEL
    if _SKEL is None:
        with open(fetch("rigs/default.mhskel")) as f:
            s = json.load(f)
        with open(fetch("rigs/default_weights.mhw")) as f:
            w = json.load(f)["weights"]
        _SKEL = (s, w)
    return _SKEL


def joints(v_mh):
    """Положения суставов MakeHuman (в Blender-координатах) для уже деформированных вершин."""
    s, _ = skeleton()
    vb = to_blender(v_mh)
    return {name: vb[idx].mean(axis=0) for name, idx in s["joints"].items()}


def bone_ends(v_mh):
    s, _ = skeleton()
    J = joints(v_mh)
    return {n: (J[b["head"]], J[b["tail"]]) for n, b in s["bones"].items()}


# MH-кость -> игровая кость (все неуказанные отходят к родителю по цепочке)
FACE = ("special", "temporalis", "oculi", "levator", "oris", "orbicularis", "risorius", "tongue")


def game_bone(mh):
    side = ""
    if mh.endswith(".L") or mh.endswith(".R"):
        side = mh[-2:]
    b = mh[:-2] if side else mh
    m = {
        "root": "Hips", "pelvis": "Hips",
        "spine05": "Spine", "spine04": "Spine", "spine03": "Chest", "spine02": "Chest", "breast": "Chest",
        "spine01": "UpperChest", "neck01": "Neck", "neck02": "Neck", "neck03": "Neck", "head": "Head", "jaw": "Jaw",
        "clavicle": "Shoulder", "shoulder01": "Shoulder", "upperarm01": "UpperArm", "upperarm02": "UpperArm",
        "lowerarm01": "LowerArm", "lowerarm02": "LowerArm", "wrist": "Hand",
        "metacarpal1": "Hand", "metacarpal2": "Hand", "metacarpal3": "Hand", "metacarpal4": "Hand",
        "upperleg01": "UpperLeg", "upperleg02": "UpperLeg", "lowerleg01": "LowerLeg", "lowerleg02": "LowerLeg",
        "foot": "Foot", "eye": "Eye",
    }
    if b in m:
        g = m[b]
    elif b.startswith("finger"):
        f, k = b[6:].split("-")
        g = ["Thumb", "Index", "Middle", "Ring", "Little"][int(f) - 1] + k
    elif b.startswith("toe"):
        g = "Toes"
    elif b.startswith(FACE):
        g = "Jaw" if b.startswith(("tongue", "levator06", "oris01", "oris02", "oris03", "oris04", "oris05", "oris06", "oris07")) and False else "Head"
    else:
        g = "Head"
    if g in ("Hips", "Spine", "Chest", "UpperChest", "Neck", "Head", "Jaw"):
        return g
    return g + side


def game_weights(n_body_verts=None):
    """Веса MakeHuman, сведённые к игровым костям: dict кость -> dict(вершина -> вес)."""
    _, w = skeleton()
    out = {}
    for mhb, lst in w.items():
        g = game_bone(mhb)
        d = out.setdefault(g, {})
        for vi, wt in lst:
            d[vi] = d.get(vi, 0.0) + wt
    return out


# ----------------------------------------------------------------------------- прокси (глаза и др.)

def proxy(rel_mhclo, v_mh):
    """Подгонка прокси-меша (.mhclo) к деформированному телу. Возвращает (вершины Blender, грани, uv-грани)."""
    path = fetch(rel_mhclo)
    folder = os.path.dirname(rel_mhclo)
    scales = {}
    refs = []
    obj_file = None
    reading = False
    with open(path) as f:
        for line in f:
            p = line.split()
            if not p or p[0].startswith("#"):
                continue
            if p[0] in ("x_scale", "y_scale", "z_scale"):
                scales[p[0][0]] = (int(p[1]), int(p[2]), float(p[3]))
            elif p[0] == "obj_file":
                obj_file = p[1]
            elif p[0] == "verts":
                reading = True
            elif reading and len(p) >= 9 and p[0].isdigit():
                refs.append(([int(p[0]), int(p[1]), int(p[2])], [float(x) for x in p[3:6]], [float(x) for x in p[6:9]]))
            elif reading and len(p) == 1 and p[0].isdigit():
                refs.append(([int(p[0])] * 3, [1.0, 0, 0], [0, 0, 0]))
            elif reading and p[0] in ("material", "delete_verts", "uuid", "name"):
                reading = False

    def sc(axis):
        a, b, s = scales[axis]
        return abs(v_mh[a][["x", "y", "z"].index(axis)] - v_mh[b][["x", "y", "z"].index(axis)]) / s
    S = np.array([sc("x"), sc("y"), sc("z")])
    pv = np.array([v_mh[i[0]] * w[0] + v_mh[i[1]] * w[1] + v_mh[i[2]] * w[2] + np.array(o) * S for i, w, o in refs])
    # геометрия прокси
    pverts, puv, pfaces = [], [], []
    with open(fetch(os.path.join(folder, obj_file))) as f:
        for line in f:
            if line.startswith("vt "):
                puv.append([float(x) for x in line.split()[1:3]])
            elif line.startswith("f "):
                pfaces.append([(int(t.split("/")[0]) - 1, int(t.split("/")[1]) - 1 if "/" in t and t.split("/")[1] else -1)
                               for t in line.split()[1:]])
    return to_blender(pv), pfaces, np.array(puv) if puv else None


_MODS = None


def mod(group, target, value, pair=None):
    """Лицевой/телесный модификатор MakeHuman: ('nose', 'nose-hump', 0.6) -> таргет с весом.
    pair='down|up' — если у таргета несколько вариантов (например nose-trans)."""
    global _MODS
    if _MODS is None:
        with open(fetch("modifiers/modeling_modifiers.json")) as f:
            _MODS = json.load(f)
    if pair is None:
        for g in _MODS:
            if g["group"] == group:
                for m in g["modifiers"]:
                    if m.get("target") == target and "min" in m:
                        pair = f"{m['min']}|{m['max']}"
                        break
    if pair is None:
        return (f"{group}/{target}", value)
    lo, hi = pair.split("|")
    return (f"{group}/{target}-{lo}|{hi}", value)
