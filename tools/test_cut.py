# -*- coding: utf-8 -*-
"""测试：把像素头像的纯色背景抠成透明。从四条边的连通区域泛洪，色差阈值内算背景。"""
import os, sys
from collections import deque
from PIL import Image

SRC = r"D:\SteamLibrary\steamapps\common\Touhou Mystia Izakaya\东方夜雀食堂 头像&表情包 ~ Avatar Image\本体\33-01.png"
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "test_cut.png")

def cut(path, tol=26):
    im = Image.open(path).convert("RGBA")
    w, h = im.size
    px = im.load()

    # 背景色 = 四条边像素里出现最多的颜色
    from collections import Counter
    cnt = Counter()
    for x in range(w):
        cnt[px[x, 0][:3]] += 1
        cnt[px[x, h - 1][:3]] += 1
    for y in range(h):
        cnt[px[0, y][:3]] += 1
        cnt[px[w - 1, y][:3]] += 1
    bg = cnt.most_common(1)[0][0]
    print("背景色", bg, "占比", round(cnt.most_common(1)[0][1] / (2 * w + 2 * h), 3))

    def near(c):
        return abs(c[0] - bg[0]) <= tol and abs(c[1] - bg[1]) <= tol and abs(c[2] - bg[2]) <= tol

    seen = bytearray(w * h)
    q = deque()
    for x in range(w):
        for y in (0, h - 1):
            if not seen[y * w + x] and near(px[x, y]):
                seen[y * w + x] = 1
                q.append((x, y))
    for y in range(h):
        for x in (0, w - 1):
            if not seen[y * w + x] and near(px[x, y]):
                seen[y * w + x] = 1
                q.append((x, y))

    while q:
        x, y = q.popleft()
        x0, y0, x1, y1 = px[x, y]
        px[x, y] = (x0, y0, x1, 0)
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if 0 <= nx < w and 0 <= ny < h and not seen[ny * w + nx] and near(px[nx, ny]):
                seen[ny * w + nx] = 1
                q.append((nx, ny))

    return im

if __name__ == "__main__":
    im = cut(SRC)
    im.save(OUT)
    # 同时给一张放在米色底上的预览（模拟面板纸色）
    prev = Image.new("RGBA", im.size, (243, 233, 214, 255))
    prev.alpha_composite(im)
    prev.convert("RGB").save(OUT.replace(".png", "_paper.png"))
    print("写出", OUT)
