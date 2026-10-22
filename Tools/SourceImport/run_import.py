"""
Всё одной командой: скачать аддоны из Workshop, распаковать и сконвертировать модели/звуки в Unity-проект.

    python Tools/SourceImport/run_import.py [--skip-download] [--work <папка>]

Нужно: Node.js 18+ (для скачивания), Python 3.11 с модулем bpy 4.5 (pip install bpy==4.5.*), numpy, pillow.
Workshop: Clone Wars Era SNPCs, Galactic Civil War SNPCs, Star Wars Weapons (+ VJ Base и частицы — не обязательны).
"""

import argparse
import glob
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
ITEMS = {"cw": "3658522900", "gcw": "3468429737", "weapons": "183549197"}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--work", default=os.path.join(ROOT, "Workshop"))
    ap.add_argument("--skip-download", action="store_true")
    a = ap.parse_args()
    dl = os.path.join(a.work, "download")
    ex = os.path.join(a.work, "content")
    os.makedirs(dl, exist_ok=True)
    if not a.skip_download:
        if not os.path.exists(os.path.join(HERE, "node_modules", "steam-user")):
            subprocess.check_call(["npm", "install", "--prefix", HERE, "steam-user@5"], shell=os.name == "nt")
        subprocess.check_call(["node", os.path.join(HERE, "workshop_download.js"), dl] + list(ITEMS.values()))
    sys.path.insert(0, HERE)
    import gma
    for name, wid in ITEMS.items():
        files = glob.glob(os.path.join(dl, wid, "*.gma"))
        if not files:
            print("нет", wid)
            continue
        gma.extract(files[0], os.path.join(ex, name))
    py = sys.executable
    imp = os.path.join(HERE, "import_workshop.py")
    for extra in ([], ["--props"], ["--sounds"]):
        subprocess.check_call([py, imp, "--", "--content", ex] + extra)
    subprocess.check_call([py, os.path.join(HERE, "render_portraits.py")])
    print("\nГотово. Откройте проект в Unity и выполните: Star Wars → Командир клонов → Собрать игру (всё)")


if __name__ == "__main__":
    main()
