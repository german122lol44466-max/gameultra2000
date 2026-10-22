"""
Чтение моделей и текстур движка Source (Garry's Mod): MDL v44-49 + VVD + VTX (dx90), VTF, VMT.
Без Blender: чистый Python + numpy. Используется import_workshop.py.

Возвращает плоские массивы: позиции/нормали/UV вершин, веса костей, треугольники с материалами, кости (имя,
родитель, мировая матрица покоя), вложения (attachments), список материалов и путей поиска.
"""

import os
import re
import struct

import numpy as np


class Reader:
    def __init__(self, data):
        self.d = data

    def i32(self, o):
        return struct.unpack_from("<i", self.d, o)[0]

    def u16(self, o):
        return struct.unpack_from("<H", self.d, o)[0]

    def f32(self, o, n=1):
        v = struct.unpack_from("<%df" % n, self.d, o)
        return v if n > 1 else v[0]

    def cstr(self, o):
        e = self.d.find(b"\0", o)
        return self.d[o:e].decode("latin-1")


class Bone:
    def __init__(self, name, parent, pos, quat, pose_to_bone):
        self.name = name
        self.parent = parent
        self.pos = pos
        self.quat = quat
        self.pose_to_bone = pose_to_bone      # 3x4: мир -> кость
        self.world = None                     # 4x4: кость -> мир (положение покоя)


class Model:
    pass


def _mat34_to44(m):
    r = np.eye(4)
    r[:3, :4] = np.array(m, dtype=np.float64).reshape(3, 4)
    return r


def read_mdl(path):
    """Модель целиком. path — .mdl; рядом должны лежать .vvd и .dx90.vtx (или .vtx)."""
    data = open(path, "rb").read()
    R = Reader(data)
    if data[:4] != b"IDST":
        raise ValueError("не MDL: " + path)
    version = R.i32(4)
    M = Model()
    M.path = path
    M.version = version
    M.name = R.cstr(12)
    # ---- кости
    nb, bi = R.i32(156), R.i32(160)
    bsize = 216
    bones = []
    for i in range(nb):
        o = bi + i * bsize
        name = R.cstr(o + R.i32(o))
        parent = R.i32(o + 4)
        pos = R.f32(o + 32, 3)
        quat = R.f32(o + 44, 4)
        p2b = R.f32(o + 96, 12)
        bones.append(Bone(name, parent, pos, quat, p2b))
    for b in bones:
        m = _mat34_to44(b.pose_to_bone)
        b.world = np.linalg.inv(m)
    M.bones = bones
    # ---- вложения (attachments)
    na, ai = R.i32(240), R.i32(244)
    atts = []
    for i in range(na if 0 <= na < 256 else 0):
        o = ai + i * 92
        name = R.cstr(o + R.i32(o))
        bone = R.i32(o + 8)
        local = _mat34_to44(R.f32(o + 12, 12))
        atts.append((name, bone, local))
    M.attachments = atts
    # ---- текстуры и пути
    nt, ti = R.i32(204), R.i32(208)
    M.textures = []
    for i in range(nt):
        o = ti + i * 64
        M.textures.append(R.cstr(o + R.i32(o)).replace("\\", "/"))
    ncd, cdi = R.i32(212), R.i32(216)
    M.cddirs = [R.cstr(R.i32(cdi + i * 4)).replace("\\", "/") for i in range(ncd)]
    nsr, nsf, si = R.i32(220), R.i32(224), R.i32(228)
    M.skins = [[R.u16(si + (f * nsr + r) * 2) for r in range(nsr)] for f in range(nsf)]
    # ---- части тела
    nbp, bpi = R.i32(232), R.i32(236)
    parts = []
    for p in range(nbp):
        o = bpi + p * 16
        pname = R.cstr(o + R.i32(o))
        nm, base, mi = R.i32(o + 4), R.i32(o + 8), R.i32(o + 12)
        models = []
        for m in range(nm):
            mo = o + mi + m * 148
            mname = R.cstr(mo).strip()
            nmesh, meshi = R.i32(mo + 72), R.i32(mo + 76)
            nverts, vidx = R.i32(mo + 80), R.i32(mo + 84)
            meshes = []
            for k in range(nmesh):
                so = mo + meshi + k * 116
                meshes.append(dict(material=R.i32(so), nverts=R.i32(so + 8), voffset=R.i32(so + 12)))
            models.append(dict(name=mname, meshes=meshes, nverts=nverts, vstart=vidx // 48))
        parts.append(dict(name=pname, models=models))
    M.bodyparts = parts
    base = os.path.splitext(path)[0]
    M.verts = read_vvd(base + ".vvd")
    vtx = None
    for ext in (".dx90.vtx", ".dx80.vtx", ".sw.vtx", ".vtx"):
        if os.path.exists(base + ext):
            vtx = base + ext
            break
    if vtx is None:
        raise FileNotFoundError("нет VTX для " + path)
    M.strips = read_vtx(vtx, M)
    return M


def read_vvd(path):
    data = open(path, "rb").read()
    R = Reader(data)
    if data[:4] != b"IDSV":
        raise ValueError("не VVD: " + path)
    nlod = R.i32(12)
    nl0 = R.i32(16)
    nfix, fixi, vstart = R.i32(48), R.i32(52), R.i32(56)
    dt = np.dtype([("w", "<f4", 3), ("b", "u1", 3), ("nb", "u1"), ("p", "<f4", 3), ("n", "<f4", 3), ("uv", "<f4", 2)])
    total = (len(data) - vstart) // 48
    tan = R.i32(60)
    count = (tan - vstart) // 48 if tan > vstart else total
    allv = np.frombuffer(data, dtype=dt, count=count, offset=vstart)
    if nfix:
        idx = []
        for i in range(nfix):
            lod, src, n = struct.unpack_from("<iii", data, fixi + i * 12)
            if lod >= 0:
                idx.extend(range(src, src + n))
        allv = allv[np.array(idx, dtype=np.int64)]
    return allv


def read_vtx(path, M):
    """Треугольники LOD0: список (индексы вершин VVD [n,3], индекс материала) по всем мешам моделей
    первой (по умолчанию) модели каждой части тела."""
    data = open(path, "rb").read()
    R = Reader(data)
    nbp, bpo = R.i32(28), R.i32(32)
    out = []
    for p in range(nbp):
        po = bpo + p * 8
        nm, mo = R.i32(po), R.i32(po + 4)
        for m in range(nm):
            if p >= len(M.bodyparts) or m >= len(M.bodyparts[p]["models"]):
                continue
            if m != 0:
                continue                           # только модель «по умолчанию» части тела
            mdl_model = M.bodyparts[p]["models"][m]
            mho = po + mo + m * 8
            nlod, lodo = R.i32(mho), R.i32(mho + 4)
            if nlod <= 0:
                continue
            lo = mho + lodo                        # LOD0
            nmesh, mesho = R.i32(lo), R.i32(lo + 4)
            for k in range(nmesh):
                if k >= len(mdl_model["meshes"]):
                    break
                mm = mdl_model["meshes"][k]
                meo = lo + mesho + k * 9
                nsg, sgo = R.i32(meo), R.i32(meo + 4)
                tris = _strip_groups(R, meo + sgo, nsg, mdl_model["vstart"] + mm["voffset"])
                if tris is not None and len(tris):
                    out.append((tris, mm["material"], M.bodyparts[p]["name"]))
    return out


def _strip_groups(R, o, nsg, vbase):
    for sgsize in (25, 33):
        try:
            res = []
            ok = True
            for g in range(nsg):
                go = o + g * sgsize
                nv, vo, ni, io = R.i32(go), R.i32(go + 4), R.i32(go + 8), R.i32(go + 12)
                if nv < 0 or ni < 0 or ni % 3 or nv > 200000:
                    ok = False
                    break
                vt = np.frombuffer(R.d, dtype=np.dtype([("bwi", "u1", 3), ("nb", "u1"), ("orig", "<u2"), ("bid", "u1", 3)]),
                                   count=nv, offset=go + vo)
                ix = np.frombuffer(R.d, dtype="<u2", count=ni, offset=go + io)
                if ni and ix.max() >= nv:
                    ok = False
                    break
                res.append(vt["orig"][ix].astype(np.int64).reshape(-1, 3) + vbase)
            if ok:
                return np.concatenate(res) if res else np.zeros((0, 3), np.int64)
        except struct.error:
            continue
        except ValueError:
            continue
    return None


# ============================================================================ VTF

def _dxt_block_colors(c0, c1, alpha_mode):
    def rgb(c):
        r = ((c >> 11) & 31) * 255 // 31
        g = ((c >> 5) & 63) * 255 // 63
        b = (c & 31) * 255 // 31
        return np.stack([r, g, b], -1).astype(np.int32)
    a, b = rgb(c0), rgb(c1)
    four = (c0 > c1) | (not alpha_mode)
    c2 = np.where(four[..., None], (2 * a + b) // 3, (a + b) // 2)
    c3 = np.where(four[..., None], (a + 2 * b) // 3, 0)
    return np.stack([a, b, c2, c3], -2), four


def decode_dxt(buf, w, h, fmt):
    bw, bh = max(1, (w + 3) // 4), max(1, (h + 3) // 4)
    n = bw * bh
    if fmt == "dxt1":
        blk = np.frombuffer(buf, dtype=np.uint8, count=n * 8).reshape(n, 8)
        cb = blk
    else:
        blk = np.frombuffer(buf, dtype=np.uint8, count=n * 16).reshape(n, 16)
        cb = blk[:, 8:]
    c0 = cb[:, 0].astype(np.int32) | (cb[:, 1].astype(np.int32) << 8)
    c1 = cb[:, 2].astype(np.int32) | (cb[:, 3].astype(np.int32) << 8)
    pal, four = _dxt_block_colors(c0, c1, fmt == "dxt1")
    bits = cb[:, 4].astype(np.uint32) | (cb[:, 5].astype(np.uint32) << 8) | (cb[:, 6].astype(np.uint32) << 16) | (cb[:, 7].astype(np.uint32) << 24)
    idx = (bits[:, None] >> (2 * np.arange(16, dtype=np.uint32))) & 3
    rgb = np.take_along_axis(pal, idx[..., None].astype(np.int64), axis=1)          # n,16,3
    alpha = np.full((n, 16), 255, np.int32)
    if fmt == "dxt1":
        alpha = np.where((~four[:, None]) & (idx == 3), 0, 255)
    elif fmt == "dxt3":
        ab = blk[:, :8]
        nib = np.stack([ab & 15, ab >> 4], -1).reshape(n, 16)
        alpha = nib.astype(np.int32) * 17
    elif fmt == "dxt5":
        a0 = blk[:, 0].astype(np.int32)
        a1 = blk[:, 1].astype(np.int32)
        ab = np.zeros(n, np.uint64)
        for i in range(6):
            ab |= blk[:, 2 + i].astype(np.uint64) << np.uint64(8 * i)
        ai = ((ab[:, None] >> (np.uint64(3) * np.arange(16, dtype=np.uint64))) & np.uint64(7)).astype(np.int64)
        tab = np.zeros((n, 8), np.int32)
        tab[:, 0], tab[:, 1] = a0, a1
        gt = a0 > a1
        for i in range(2, 8):
            six = ((8 - i) * a0 + (i - 1) * a1) // 7
            four_ = np.where(i < 6, ((6 - i) * a0 + (i - 1) * a1) // 5, 0 if i == 6 else 255)
            tab[:, i] = np.where(gt, six, four_)
        alpha = np.take_along_axis(tab, ai, axis=1)
    px = np.concatenate([rgb, alpha[..., None]], -1).reshape(bh, bw, 4, 4, 4)
    img = px.transpose(0, 2, 1, 3, 4).reshape(bh * 4, bw * 4, 4)[:h, :w]
    return img.astype(np.uint8)


FMT = {0: ("rgba", 4), 1: ("abgr", 4), 2: ("rgb", 3), 3: ("bgr", 3), 4: ("rgb565", 2), 12: ("bgra", 4), 13: ("dxt1", 0.5),
       14: ("dxt3", 1), 15: ("dxt5", 1), 16: ("bgrx", 4), 20: ("dxt1", 0.5), 5: ("i8", 1), 6: ("ia88", 2), 8: ("a8", 1),
       24: ("rgba16f", 8), 22: ("uv88", 2), 11: ("argb", 4), 17: ("bgr565", 2)}


def _size(fmt, w, h):
    name, bpp = FMT[fmt]
    if name.startswith("dxt"):
        return max(1, (w + 3) // 4) * max(1, (h + 3) // 4) * (8 if name == "dxt1" else 16)
    return int(w * h * bpp)


def read_vtf(path):
    """RGBA uint8 [h,w,4] самого крупного мипа первого кадра."""
    data = open(path, "rb").read()
    if data[:4] != b"VTF\0":
        raise ValueError("не VTF: " + path)
    vmaj, vmin = struct.unpack_from("<II", data, 4)
    hsize = struct.unpack_from("<I", data, 12)[0]
    w, h = struct.unpack_from("<HH", data, 16)
    flags = struct.unpack_from("<I", data, 20)[0]
    frames = struct.unpack_from("<H", data, 24)[0]
    fmt = struct.unpack_from("<i", data, 52)[0]
    mips = data[56]
    lfmt = struct.unpack_from("<i", data, 57)[0]
    lw, lh = data[61], data[62]
    if fmt not in FMT:
        raise ValueError("формат VTF %d не поддержан" % fmt)
    faces = 6 if flags & 0x4000 else 1
    sizes = [_size(fmt, max(1, w >> m), max(1, h >> m)) for m in range(mips)]
    if vmaj == 7 and vmin >= 3:
        nres = struct.unpack_from("<I", data, 68)[0]
        off = None
        for i in range(nres):
            tag = data[80 + i * 8: 83 + i * 8]
            if tag == b"\x30\x00\x00":
                off = struct.unpack_from("<I", data, 84 + i * 8)[0]
        if off is None:
            raise ValueError("нет данных изображения")
    else:
        off = hsize + (_size(lfmt, lw, lh) if lfmt >= 0 and lw else 0)
    # мипы от мелкого к крупному; у каждого — кадры и грани
    for m in range(mips - 1, 0, -1):
        off += sizes[m] * frames * faces
    buf = data[off: off + sizes[0]]
    name = FMT[fmt][0]
    if name.startswith("dxt"):
        return decode_dxt(buf, w, h, name)
    a = np.frombuffer(buf, dtype=np.uint8)
    if name in ("rgba", "abgr", "bgra", "bgrx", "argb"):
        a = a.reshape(h, w, 4)
        order = {"rgba": [0, 1, 2, 3], "abgr": [3, 2, 1, 0], "bgra": [2, 1, 0, 3], "bgrx": [2, 1, 0, 3], "argb": [1, 2, 3, 0]}[name]
        a = a[..., order].copy()
        if name == "bgrx":
            a[..., 3] = 255
        return a
    if name in ("rgb", "bgr"):
        a = a.reshape(h, w, 3)
        if name == "bgr":
            a = a[..., ::-1]
        return np.concatenate([a, np.full((h, w, 1), 255, np.uint8)], -1)
    if name in ("rgb565", "bgr565"):
        c = np.frombuffer(buf, dtype="<u2").reshape(h, w).astype(np.int32)
        r, g, b = ((c >> 11) & 31) * 255 // 31, ((c >> 5) & 63) * 255 // 63, (c & 31) * 255 // 31
        if name == "bgr565":
            r, b = b, r
        return np.stack([r, g, b, np.full_like(r, 255)], -1).astype(np.uint8)
    if name == "i8":
        a = a.reshape(h, w)
        return np.stack([a, a, a, np.full_like(a, 255)], -1)
    if name == "ia88":
        a = a.reshape(h, w, 2)
        return np.stack([a[..., 0]] * 3 + [a[..., 1]], -1)
    if name == "a8":
        a = a.reshape(h, w)
        return np.stack([np.full_like(a, 255)] * 3 + [a], -1)
    if name == "uv88":
        a = a.reshape(h, w, 2)
        return np.stack([a[..., 0], a[..., 1], np.full_like(a[..., 0], 255), np.full_like(a[..., 0], 255)], -1)
    if name == "rgba16f":
        f = np.frombuffer(buf, dtype="<f2").reshape(h, w, 4).astype(np.float32)
        f = np.clip(f / (1 + f), 0, 1) ** (1 / 2.2)
        return (f * 255).astype(np.uint8)
    raise ValueError(name)


# ============================================================================ VMT

def parse_vmt(path):
    """Плоский словарь параметров VMT (ключи в нижнем регистре) + шейдер."""
    txt = open(path, "rb").read().decode("latin-1")
    txt = re.sub(r"//[^\n]*", "", txt)
    toks = re.findall(r'"([^"]*)"|([{}])|([^\s{}"]+)', txt)
    flat = [a or b or c for a, b, c in toks]
    res = {}
    shader = flat[0].lower() if flat else ""
    depth = 0
    i = 1
    while i < len(flat):
        t = flat[i]
        if t == "{":
            depth += 1
            i += 1
            continue
        if t == "}":
            depth -= 1
            i += 1
            continue
        if i + 1 < len(flat) and flat[i + 1] == "{":
            i += 1                       # вложенный блок (Proxies и т. п.) — его ключи не нужны
            skip = 0
            while i < len(flat):
                if flat[i] == "{":
                    skip += 1
                elif flat[i] == "}":
                    skip -= 1
                    if skip == 0:
                        break
                i += 1
            i += 1
            continue
        if depth == 1 and i + 1 < len(flat):
            res[t.lower()] = flat[i + 1]
            i += 2
            continue
        i += 1
    res["__shader"] = shader
    return res


def find_material(root_dirs, cddirs, tex):
    """Ищет VMT материала по путям из MDL в корнях контента (materials/...)."""
    tex = tex.lower().replace("\\", "/")
    for cd in cddirs + [""]:
        cd = cd.lower().strip("/")
        for root in root_dirs:
            p = os.path.join(root, "materials", cd, tex + ".vmt")
            if os.path.exists(p):
                return p
    # иногда имя уже содержит путь
    for root in root_dirs:
        p = os.path.join(root, "materials", tex + ".vmt")
        if os.path.exists(p):
            return p
    return None


def find_texture(root_dirs, name):
    name = name.lower().replace("\\", "/").strip("/")
    if name.endswith(".vtf"):
        name = name[:-4]
    for root in root_dirs:
        p = os.path.join(root, "materials", name + ".vtf")
        if os.path.exists(p):
            return p
    return None
