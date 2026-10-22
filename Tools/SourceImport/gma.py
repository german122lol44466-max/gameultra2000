"""
Распаковка аддонов Garry's Mod (.gma, в т. ч. старых LZMA-сжатых из Workshop).

    python Tools/SourceImport/gma.py <файл.gma> <папка> [префиксы путей...]
"""

import lzma
import os
import struct
import sys


def cstr(f):
    b = bytearray()
    while True:
        c = f.read(1)
        if c in (b"\0", b""):
            break
        b += c
    return b.decode("utf-8", "replace")


def extract(path, out, filt=None):
    with open(path, "rb") as f:
        magic = f.read(4)
    if magic != b"GMAD":
        with open(path, "rb") as f:
            data = lzma.decompress(f.read(), format=lzma.FORMAT_ALONE)
        path = path + ".dec"
        with open(path, "wb") as f:
            f.write(data)
    with open(path, "rb") as f:
        assert f.read(4) == b"GMAD", "не GMA"
        ver = f.read(1)[0]
        f.read(16)
        if ver > 1:
            while cstr(f):
                pass
        name = cstr(f)
        cstr(f)
        cstr(f)
        f.read(4)
        entries = []
        while struct.unpack("<I", f.read(4))[0] != 0:
            n = cstr(f)
            size, = struct.unpack("<q", f.read(8))
            f.read(4)
            entries.append((n, size))
        done = 0
        for n, size in entries:
            data = f.read(size)
            if filt and not any(n.lower().startswith(p) for p in filt):
                continue
            p = os.path.join(out, n.lower())
            os.makedirs(os.path.dirname(p), exist_ok=True)
            with open(p, "wb") as o:
                o.write(data)
            done += 1
        print(f"{name}: {len(entries)} файлов, распаковано {done}")


if __name__ == "__main__":
    extract(sys.argv[1], sys.argv[2], sys.argv[3:] or None)
