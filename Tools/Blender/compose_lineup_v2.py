"""Сводная картинка docs/renders/v2/lineup_v2.png из портретов (после build_v2.py --portrait)."""
import os
from PIL import Image, ImageDraw, ImageFont

D = os.path.join(os.path.dirname(__file__), "..", "..", "docs", "renders", "v2")
ROWS = [("ИМПЕРИЯ / СИТХИ", ["Stormtrooper", "HeavyTrooper", "TrooperCommander", "DarthVader", "DarthMaul", "DarthSidious", "CountDooku"]),
        ("РЕСПУБЛИКА / ДЖЕДАИ", ["CloneTrooper", "CloneHeavy", "CloneCommander", "ObiWan", "MaceWindu", "Anakin"])]
W, H, TOP = 400, 720, 44
try:
    font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 26)
    small = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 18)
except OSError:
    font = small = ImageFont.load_default()
sheet = Image.new("RGB", (W * 7, (H + TOP) * 2), (8, 9, 12))
dr = ImageDraw.Draw(sheet)
for r, (title, names) in enumerate(ROWS):
    y0 = r * (H + TOP)
    dr.text((14, y0 + 8), title, fill=(255, 120, 110) if r == 0 else (140, 190, 255), font=font)
    for i, n in enumerate(names):
        p = os.path.join(D, f"{n}.png")
        if not os.path.exists(p):
            continue
        im = Image.open(p).convert("RGB").crop((200, 230, 800, 1310)).resize((W, H), Image.LANCZOS)
        sheet.paste(im, (i * W, y0 + TOP))
        dr.text((i * W + 10, y0 + TOP + H - 30), n, fill=(220, 220, 228), font=small)
sheet.save(os.path.join(D, "lineup_v2.png"))
print("ok")
