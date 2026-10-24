# -*- coding: utf-8 -*-
"""把「头像简化」目录里的图按目录切成带编号标注的联系表，供逐张识别。"""
import os, glob, math
from PIL import Image, ImageDraw, ImageFont

ROOT = r"E:\game\mode\东方夜雀食堂\my mod\东方夜雀食堂 头像简化"
OUT = os.path.join(ROOT, "_sheets")
os.makedirs(OUT, exist_ok=True)

CELL = 230          # 图区边长
LABEL = 34          # 标号条高度
COLS = 4
ROWS = 3
PAD = 6

FONT = None
for p in [r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\simhei.ttf", r"C:\Windows\Fonts\arial.ttf"]:
    if os.path.exists(p):
        FONT = ImageFont.truetype(p, 20)
        break
if FONT is None:
    FONT = ImageFont.load_default()


def make_sheet(files, title, out_path):
    n = len(files)
    rows = max(1, math.ceil(n / COLS))
    W = COLS * (CELL + PAD) + PAD
    H = rows * (CELL + LABEL + PAD) + PAD + 36
    sheet = Image.new("RGB", (W, H), (245, 242, 236))
    d = ImageDraw.Draw(sheet)
    d.text((PAD, 8), title, fill=(60, 60, 60), font=FONT)
    for i, fp in enumerate(files):
        c, r = i % COLS, i // COLS
        x = PAD + c * (CELL + PAD)
        y = 36 + PAD + r * (CELL + LABEL + PAD)
        try:
            im = Image.open(fp).convert("RGB")
            im = im.resize((CELL, CELL), Image.LANCZOS)
            sheet.paste(im, (x, y))
        except Exception as e:
            d.text((x + 4, y + 4), "ERR", fill=(255, 0, 0), font=FONT)
        d.rectangle([x, y, x + CELL - 1, y + CELL - 1], outline=(200, 200, 200))
        label = os.path.basename(fp).replace(".png", "")
        d.rectangle([x, y + CELL, x + CELL - 1, y + CELL + LABEL - 1], fill=(30, 30, 30))
        tw = d.textlength(label, font=FONT)
        d.text((x + (CELL - tw) / 2, y + CELL + 5), label, fill=(255, 255, 255), font=FONT)
    sheet.save(out_path)
    return out_path


manifest = []
for sub in ["本体", "DLC1", "DLC2", "DLC3", "DLC4", "DLC5", "DLCMUSIC"]:
    d = os.path.join(ROOT, sub)
    if not os.path.isdir(d):
        continue
    files = sorted(glob.glob(os.path.join(d, "*.png")))
    # 每张最多 12 个
    chunks = [files[i:i + COLS * ROWS] for i in range(0, len(files), COLS * ROWS)]
    for idx, chunk in enumerate(chunks):
        suffix = "" if len(chunks) == 1 else "_%d" % (idx + 1)
        name = "sheet_%s%s.png" % (sub, suffix)
        p = make_sheet(chunk, "%s  (%d)" % (sub, idx + 1), os.path.join(OUT, name))
        manifest.append((p, len(chunk)))

for p, n in manifest:
    print(n, p)
