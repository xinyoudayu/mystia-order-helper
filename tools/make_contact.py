# -*- coding: utf-8 -*-
"""把素材包按角色编号拼成联系表，格子上直接标「编号 + 命名表里的名字」。
命名表里的 ? 会显示成「?（待填）」，方便一眼看出哪些还没定。
"""
import os, glob
from PIL import Image, ImageDraw, ImageFont

BASE = os.path.dirname(os.path.abspath(__file__))
SRC = r"D:\SteamLibrary\steamapps\common\Touhou Mystia Izakaya\东方夜雀食堂 头像&表情包 ~ Avatar Image\本体"
OUT = os.path.join(BASE, "contact")
CELL = 200
PAD = 10
LABEL = 46
COLS = 4
ROWS = 5
PER_SHEET = COLS * ROWS


def load_map():
    m = {}
    p = os.path.join(BASE, "命名表.txt")
    if not os.path.exists(p):
        return m
    for raw in open(p, encoding="utf-8"):
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        k, v = line.split("=", 1)
        m[k.strip()] = v.strip()
    return m


def font(size):
    for p in (r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\arial.ttf"):
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size)
            except Exception:
                pass
    return ImageFont.load_default()


def main():
    os.makedirs(OUT, exist_ok=True)
    for old in glob.glob(os.path.join(OUT, "sheet_*.png")):
        os.remove(old)

    names = load_map()
    groups = {}
    for f in sorted(glob.glob(os.path.join(SRC, "*.png"))):
        gid = os.path.basename(f).split("-")[0]
        groups.setdefault(gid, []).append(f)
    ids = sorted(groups.keys())

    for si in range(0, len(ids), PER_SHEET):
        chunk = ids[si:si + PER_SHEET]
        W = COLS * (CELL + PAD) + PAD
        H = ROWS * (CELL + LABEL + PAD) + PAD
        sheet = Image.new("RGB", (W, H), (248, 246, 242))
        d = ImageDraw.Draw(sheet)
        f_big = font(24)
        f_small = font(17)

        for i, gid in enumerate(chunk):
            files = groups[gid]
            pick = next((f for f in files if f.endswith("-01.png")), files[0])
            im = Image.open(pick).convert("RGB").resize((CELL, CELL), Image.LANCZOS)
            r, c = divmod(i, COLS)
            x = PAD + c * (CELL + PAD)
            y = PAD + r * (CELL + LABEL + PAD)
            sheet.paste(im, (x, y))

            nm = names.get(gid, "?")
            if nm in ("", "?", "？"):
                label = f"{gid}  ?"
                color = (200, 60, 60)
            else:
                label = f"{gid}  {nm}"
                color = (25, 25, 25)
            d.text((x + 2, y + CELL + 4), label, fill=color, font=f_big)
            d.text((x + 2, y + CELL + 30), f"{len(files)}张", fill=(130, 130, 130), font=f_small)

        name = os.path.join(OUT, f"sheet_{si:02d}.png")
        sheet.save(name)
        print("写出", name, sheet.size)


if __name__ == "__main__":
    main()
