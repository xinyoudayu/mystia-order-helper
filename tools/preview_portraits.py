# -*- coding: utf-8 -*-
"""预览：把命名表里已确定的头像按处理后的样子排成一张对照图（米色纸底），
顺便标出超宽/贴边这类容易出问题的。"""
import os
from PIL import Image, ImageDraw, ImageFont
import import_portraits as ip

OUT = os.path.join(ip.ROOT, "_preview.png")
CELL = 110
GAP = 6
COLS = 10

FONT = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 16)
FONT_S = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 14)
FONT_XS = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 12)
FONT_RC = ImageFont.truetype(r"C:\Windows\Fonts\msyhbd.ttc", 22)


def name_font(name):
    """名字越长字号越小，避免超出格子被截断。"""
    n = len(name)
    if n <= 5:
        return FONT
    if n <= 7:
        return FONT_S
    return FONT_XS


def main():
    rows = ip.load_map()
    tiles = []
    seen_rel = set()          # 同一张源图挂了两个名字（兼容译名）时只画第一格
    for rel, name, note in rows:
        if rel in seen_rel:
            continue
        src = os.path.join(ip.ROOT, rel)
        if not os.path.exists(src) and not src.lower().endswith(".png"):
            src += ".png"
        if not os.path.exists(src):
            continue
        seen_rel.add(rel)
        im = ip.center_square(ip.cut_background(Image.open(src)))
        im = im.resize((CELL, CELL), Image.LANCZOS)
        tiles.append((name, im))

    rows_n = (len(tiles) + COLS - 1) // COLS
    W = COLS * (CELL + GAP) + GAP
    H = rows_n * (CELL + 24 + GAP) + GAP
    sheet = Image.new("RGB", (W, H), (245, 242, 236))
    d = ImageDraw.Draw(sheet)
    for i, (name, im) in enumerate(tiles):
        c, r = i % COLS, i // COLS
        x = GAP + c * (CELL + GAP)
        y = GAP + r * (CELL + 24 + GAP)
        bg = Image.new("RGB", (CELL, CELL), (245, 242, 236))
        bg.paste(im, (0, 0), im)
        sheet.paste(bg, (x, y))
        d.rectangle([x, y, x + CELL - 1, y + CELL - 1], outline=(210, 200, 185))
        d.text((x + 3, y + CELL + 4), name, fill=(50, 50, 50), font=name_font(name))
    sheet.save(OUT)
    print("预览:", OUT, len(tiles), "张")

    # 第二份：左上角标「行.列」，方便用户按坐标报错（不影响上面那张干净的效果图）
    rc = sheet.copy()
    d2 = ImageDraw.Draw(rc)
    for i in range(len(tiles)):
        c, r = i % COLS, i // COLS
        x = GAP + c * (CELL + GAP)
        y = GAP + r * (CELL + 24 + GAP)
        tag = "%d.%d" % (r + 1, c + 1)
        try:
            d2.text((x + 2, y + 1), tag, fill=(200, 30, 30), font=FONT_RC,
                    stroke_width=2, stroke_fill=(255, 255, 255))
        except TypeError:
            d2.text((x + 2, y + 1), tag, fill=(200, 30, 30), font=FONT_RC)
    out2 = OUT.replace("_preview.png", "_preview_rc.png")
    rc.save(out2)
    print("带坐标:", out2)


if __name__ == "__main__":
    main()
