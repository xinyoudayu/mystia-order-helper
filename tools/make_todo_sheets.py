# -*- coding: utf-8 -*-
"""生成「待确认」对照图：只排命名表里还是 ? 的条目，格子上写编号 + 我的猜测。
分两张：主线（本体/DLC1/DLC2）和 其余（DLC3/4/5/MUSIC）。"""
import os
import re
from PIL import Image, ImageDraw, ImageFont

ROOT = r"E:\game\mode\东方夜雀食堂\my mod\东方夜雀食堂 头像简化"
MAP = os.path.join(os.path.dirname(os.path.abspath(__file__)), "命名表.txt")
CELL = 230
LABEL = 58
COLS = 4
PAD = 6
FONT = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 18)
FONT_S = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 14)
FONT_RC = ImageFont.truetype(r"C:\Windows\Fonts\msyhbd.ttc", 30)


def parse():
    groups = {"main": [], "rest": []}
    for raw in open(MAP, encoding="utf-8"):
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        left, _, right = line.partition("=")
        note = ""
        if "#" in right:
            right, _, note = right.partition("#")
        rel = left.strip()
        val = right.strip()
        if val not in ("?", "？"):
            continue
        hint = note.strip()
        hint = re.sub(r"^【[^】]*】", "", hint).strip()
        hint = hint.replace("；", " ").replace("，", " ")[:26]
        key = "main" if rel.split("/")[0] in ("本体", "DLC1", "DLC2") else "rest"
        groups[key].append((rel, hint))
    return groups


def sheet(items, title, out):
    rows = (len(items) + COLS - 1) // COLS
    W = COLS * (CELL + PAD) + PAD
    H = rows * (CELL + LABEL + PAD) + PAD + 30
    im = Image.new("RGB", (W, H), (248, 245, 240))
    d = ImageDraw.Draw(im)
    d.text((PAD, 6), title, fill=(40, 40, 40), font=FONT)
    for i, (rel, hint) in enumerate(items):
        c, r = i % COLS, i // COLS
        x = PAD + c * (CELL + PAD)
        y = 30 + PAD + r * (CELL + LABEL + PAD)
        p = os.path.join(ROOT, rel.replace("/", os.sep))
        if not os.path.exists(p):
            p += ".png"
        src = Image.open(p).convert("RGB").resize((CELL, CELL), Image.LANCZOS)
        im.paste(src, (x, y))
        d.rectangle([x, y, x + CELL - 1, y + CELL - 1], outline=(200, 190, 175))
        # 左上角标「行.列」，方便用户按坐标报名字
        tag = "%d.%d" % (r + 1, c + 1)
        try:
            d.text((x + 6, y + 4), tag, fill=(200, 30, 30), font=FONT_RC,
                   stroke_width=3, stroke_fill=(255, 255, 255))
        except TypeError:
            d.text((x + 6, y + 4), tag, fill=(200, 30, 30), font=FONT_RC)
        d.rectangle([x, y + CELL, x + CELL - 1, y + CELL + LABEL - 1], fill=(28, 28, 28))
        d.text((x + 6, y + CELL + 4), rel, fill=(255, 210, 120), font=FONT)
        # 提示文字按 13 字折两行，避免超出格子宽度
        txt = hint or "?"
        lines = [txt[i:i + 13] for i in range(0, min(len(txt), 26), 13)]
        for k, t in enumerate(lines):
            d.text((x + 6, y + CELL + 26 + k * 16), t, fill=(200, 200, 200), font=FONT_S)
    im.save(out)
    print(out, len(items))


def main():
    g = parse()
    sheet(g["main"], "待确认（本体 / DLC1 / DLC2）—— 编号 + 我的猜测", os.path.join(ROOT, "_todo_main.png"))
    sheet(g["rest"], "待确认（DLC3 / DLC4 / DLC5 / MUSIC）", os.path.join(ROOT, "_todo_rest.png"))


if __name__ == "__main__":
    main()
