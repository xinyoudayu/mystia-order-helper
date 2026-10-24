# -*- coding: utf-8 -*-
"""按「命名表.txt」把「东方夜雀食堂 头像简化」里的像素头像抠背景、裁正、缩放、改名，
导入 mod 的外部头像目录（BepInEx\\config\\orderhelper-portraits）。

命名表格式（每行）：
    本体/33-01 = 茨木华扇        # 行尾 # 是注释，可以是猜测
值为 ? 表示还没确定，跳过。

用法：
    python import_portraits.py            # 只导出命名表里已确定的
    python import_portraits.py --dry      # 只看会导出什么，不写文件
"""
import os
import sys
from collections import Counter, deque
from PIL import Image

BASE = os.path.dirname(os.path.abspath(__file__))
ROOT = r"E:\game\mode\东方夜雀食堂\my mod\东方夜雀食堂 头像简化"
MAP = os.path.join(BASE, "命名表.txt")
DEST = r"D:\SteamLibrary\steamapps\common\Touhou Mystia Izakaya\BepInEx\config\orderhelper-portraits"
OUT_SIZE = 128
TOL = 26          # 背景色容差
PAD = 0.02        # 裁到内容后四周留白比例


def load_map():
    """返回 [(相对路径, 显示名, 备注)]，只含已确定的。"""
    rows = []
    if not os.path.exists(MAP):
        print("找不到命名表:", MAP)
        return rows
    for raw in open(MAP, encoding="utf-8"):
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        left, _, right = line.partition("=")
        # 去掉行尾注释
        note = ""
        if "#" in right:
            right, _, note = right.partition("#")
        rel = left.strip().replace("/", os.sep)
        name = right.strip()
        note = note.strip()
        if not rel or name in ("", "?", "？"):
            continue
        rows.append((rel, name, note))
    return rows


def cut_background(im):
    """从四条边泛洪抠掉纯色背景，返回带 alpha 的图。"""
    im = im.convert("RGBA")
    w, h = im.size
    px = im.load()

    cnt = Counter()
    for x in range(w):
        cnt[px[x, 0][:3]] += 1
        cnt[px[x, h - 1][:3]] += 1
    for y in range(h):
        cnt[px[0, y][:3]] += 1
        cnt[px[w - 1, y][:3]] += 1
    bg = cnt.most_common(1)[0][0]

    def near(c):
        return (abs(c[0] - bg[0]) <= TOL and abs(c[1] - bg[1]) <= TOL
                and abs(c[2] - bg[2]) <= TOL)

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
        r, g, b, _ = px[x, y]
        px[x, y] = (r, g, b, 0)
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if 0 <= nx < w and 0 <= ny < h and not seen[ny * w + nx] and near(px[nx, ny]):
                seen[ny * w + nx] = 1
                q.append((nx, ny))
    return im


def center_square(im):
    """裁到不透明内容的外框，再补成正方形（补透明），不留大片空白。"""
    bbox = im.split()[3].getbbox()
    if not bbox:
        return im
    im = im.crop(bbox)
    w, h = im.size
    side = int(max(w, h) * (1 + PAD * 2))
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.paste(im, ((side - w) // 2, (side - h) // 2))
    return canvas


def find_duplicates(rows):
    """同一个显示名出现在多行时，后面的会覆盖前面的图 —— 必须先报出来。"""
    seen = {}
    dups = []
    for rel, name, _ in rows:
        if name in seen:
            dups.append((name, seen[name], rel))
        else:
            seen[name] = rel
    return dups


def main():
    dry = "--dry" in sys.argv
    rows = load_map()
    if not rows:
        print("命名表里没有已确定的条目")
        return

    dups = find_duplicates(rows)
    if dups:
        print("!! 重名警告：%d 组同名，后写的会覆盖前面的图" % len(dups))
        for name, first, later in dups:
            print("   %s : %s 被 %s 覆盖" % (name, first, later))
        print()

    if not dry:
        os.makedirs(DEST, exist_ok=True)

    ok, miss = 0, []
    for rel, name, note in rows:
        src = os.path.join(ROOT, rel)
        if not os.path.exists(src) and not src.lower().endswith(".png"):
            # 命名表里允许省掉 .png（例如 本体/07- 就是文件 07-.png）
            src += ".png"
        if not os.path.exists(src):
            miss.append((rel, "源文件不存在"))
            continue
        im = center_square(cut_background(Image.open(src)))
        im = im.resize((OUT_SIZE, OUT_SIZE), Image.LANCZOS)
        dst = os.path.join(DEST, name + ".png")
        if not dry:
            im.save(dst)
        print("  %-22s -> %s.png" % (rel, name))
        ok += 1

    print("\n完成 %d 张%s -> %s" % (ok, "（演练，未写盘）" if dry else "", DEST))
    if miss:
        print("失败 %d 条：" % len(miss))
        for rel, why in miss:
            print("   ", rel, why)


if __name__ == "__main__":
    main()
