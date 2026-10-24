# -*- coding: utf-8 -*-
"""放大 4 张图，做成大图便于细看。用法: python zoom.py 本体/04-01.png ..."""
import os, sys
from PIL import Image, ImageDraw, ImageFont

ROOT = r"E:\game\mode\东方夜雀食堂\my mod\东方夜雀食堂 头像简化"
OUT = os.path.join(ROOT, "_zoom")
os.makedirs(OUT, exist_ok=True)

CELL = 380
LABEL = 36
PAD = 8

FONT = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 22)


def sheet(paths, out_name):
    n = len(paths)
    W = 2 * (CELL + PAD) + PAD
    H = ((n + 1) // 2) * (CELL + LABEL + PAD) + PAD
    im = Image.new("RGB", (W, H), (250, 248, 244))
    d = ImageDraw.Draw(im)
    for i, rel in enumerate(paths):
        c, r = i % 2, i // 2
        x = PAD + c * (CELL + PAD)
        y = PAD + r * (CELL + LABEL + PAD)
        p = os.path.join(ROOT, rel.replace("/", os.sep))
        src = Image.open(p).convert("RGB").resize((CELL, CELL), Image.NEAREST)
        im.paste(src, (x, y))
        d.rectangle([x, y, x + CELL - 1, y + CELL - 1], outline=(180, 180, 180))
        d.rectangle([x, y + CELL, x + CELL - 1, y + CELL + LABEL - 1], fill=(25, 25, 25))
        d.text((x + 8, y + CELL + 7), rel, fill=(255, 255, 255), font=FONT)
    out = os.path.join(OUT, out_name)
    im.save(out)
    print(out)


if __name__ == "__main__":
    args = sys.argv[1:]
    batch = 4
    for i in range(0, len(args), batch):
        chunk = args[i:i + batch]
        name = "zoom_%s.png" % os.path.basename(chunk[0]).replace(".png", "")
        sheet(chunk, name)
