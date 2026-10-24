#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
把 LogOutput.log 里的性能探针读成一张表。

只做两件事：把「性能30s」窗口按时间排开，把「长帧」按毫秒排序。
不猜原因，只回答三个问题：
  1. 卡顿是一次长卡，还是持续低帧？   → 看长帧条数 vs 窗口 fps
  2. 卡在采集、绘制，还是注入？        → 看三个指标的峰值
  3. 涨在客人多的时候吗？              → 看 guests 与峰值的对应

用法：
    python tools/perf-report.py                # 读默认游戏日志
    python tools/perf-report.py --top 15       # 多列几行
    python tools/perf-report.py --selftest     # 自检判据（先跑这个）
"""

import argparse
import os
import re
import sys

默认日志 = r"D:\SteamLibrary\steamapps\common\Touhou Mystia Izakaya\BepInEx\LogOutput.log"

# 「性能30s | 长帧累计 N | frame=F | 采集 60次 均3.10ms 峰18.0ms | 绘制 9000次 均0.42ms 峰9.8ms」
窗口头 = re.compile(r"性能30s \| 长帧累计 (\d+) \| frame=(\d+)")
# 每个埋点：名字 + 次数 + 均 + 峰
埋点 = re.compile(r"\|\s*(\S+?)\s+(\d+)次\s+均([\d.]+)ms\s+峰([\d.]+)ms")
# 「长帧 312ms frame=12345 guests=6」
长帧行 = re.compile(r"长帧 (\d+)ms frame=(\d+) guests=(\d+)")
# 心跳：「心跳 frame=N visible=True guests=6」——用来判断窗口落在什么场景
心跳 = re.compile(r"心跳 frame=(\d+) visible=(\w+) guests=(\d+)")


def 读日志(路径):
    """日志是 UTF-8，个别行可能被截断，统一 errors='replace'。"""
    with open(路径, "r", encoding="utf-8", errors="replace") as f:
        return f.readlines()


def 解析(行们):
    窗口们 = []
    长帧们 = []
    心跳们 = []
    废行 = 0

    for 行 in 行们:
        m = 窗口头.search(行)
        if m:
            项 = {"累计长帧": int(m.group(1)), "frame": int(m.group(2)), "埋点": {}, "原文": 行.strip()}
            for 名, 次, 均, 峰 in 埋点.findall(行):
                项["埋点"][名] = {"次数": int(次), "均": float(均), "峰": float(峰)}
            if not 项["埋点"]:
                # 只有头没有指标：说明这一轮什么都没跑（例如游戏在主菜单）
                项["埋点"] = {}
            窗口们.append(项)
            continue

        m = 长帧行.search(行)
        if m:
            长帧们.append({"ms": int(m.group(1)), "frame": int(m.group(2)), "guests": int(m.group(3))})
            continue

        m = 心跳.search(行)
        if m:
            心跳们.append({"frame": int(m.group(1)), "visible": m.group(2) == "True", "guests": int(m.group(3))})
            continue

        if "性能30s" in 行 or "长帧" in 行:
            废行 += 1  # 含关键词但解析不了 = 格式变了，必须让人看见

    return 窗口们, 长帧们, 心跳们, 废行


def 近似fps(窗口们):
    """
    两次 dump 相隔约 30 秒（Perf 里 _nextDump = now + 30）。
    帧差 / 30 就是这段的平均帧率。dump 有轻微漂移，所以给了 ≈。
    只对相邻且帧号递增的窗口算。
    """
    结果 = [None] * len(窗口们)
    for i in range(1, len(窗口们)):
        df = 窗口们[i]["frame"] - 窗口们[i - 1]["frame"]
        if df > 0:
            结果[i] = df / 30.0
    return 结果


def 场景(窗口们, 心跳们):
    """用最近的、不晚于该窗口帧号的心跳判断场景。心跳每 1800 帧一条。"""
    结果 = []
    for w in 窗口们:
        近 = None
        for h in 心跳们:
            if h["frame"] <= w["frame"]:
                近 = h
            else:
                break
        结果.append(近)
    return 结果


def 打印主表(窗口们, 长帧们, 心跳们, top):
    if not 窗口们:
        print("没有解析到任何「性能30s」窗口。")
        print("可能原因：这一局没玩到营业 / 探针被关（config 里 Diagnostics.PerfLog）/ 日志不是这一版。")
        return 1

    fps = 近似fps(窗口们)
    场景们 = 场景(窗口们, 心跳们)

    print("=" * 118)
    print("窗口表（每个「性能30s」一行；fps 是相邻两窗口的帧差/30，近似值）")
    print("=" * 118)
    print("%-4s %-10s %-13s %-6s %-5s %-24s %-24s %-20s"
          % ("#", "frame", "场景", "≈fps", "长帧", "采集 次数/均/峰", "绘制 次数/均/峰", "注入 次数/均/峰"))

    最差 = []
    for i, w in enumerate(窗口们):
        s = 场景们[i]
        场 = "%d人%s" % (s["guests"], "·显" if s["visible"] else "·隐") if s else "?"
        长 = sum(1 for L in 长帧们 if 窗口们[i - 1]["frame"] < L["frame"] <= w["frame"]) if i > 0 else 0

        def 格(name):
            d = w["埋点"].get(name)
            if not d:
                return "-"
            return "%d/%.2f/%.1f" % (d["次数"], d["均"], d["峰"])

        采集峰 = w["埋点"].get("采集", {}).get("峰", 0)
        绘制峰 = w["埋点"].get("绘制", {}).get("峰", 0)
        最差.append((max(采集峰, 绘制峰), i))

        print("%-4d %-10d %-13s %-6s %-5d %-24s %-24s %-20s"
              % (i, w["frame"], 场,
                 ("%.0f" % fps[i]) if fps[i] else "-",
                 长, 格("采集"), 格("绘制"), 格("注入")))

    print()
    print("=" * 118)
    print("最慢的 %d 个窗口（按 采集/绘制 峰值排序）" % min(top, len(窗口们)))
    print("=" * 118)
    最差.sort(reverse=True)
    for 峰, i in 最差[:top]:
        w = 窗口们[i]
        s = 场景们[i]
        场 = "%d人%s" % (s["guests"], "·显" if s["visible"] else "·隐") if s else "?"
        print("  窗口 %-3d frame=%-8d %-10s ≈%s fps | %s"
              % (i, w["frame"], 场, ("%.0f" % fps[i]) if fps[i] else "-",
                 "  ".join("%s 峰%.1fms" % (k, v["峰"]) for k, v in w["埋点"].items())))

    print()
    print("=" * 118)
    print("长帧（单帧 >= 150ms）：共 %d 条" % len(长帧们))
    print("=" * 118)
    if 长帧们:
        for L in sorted(长帧们, key=lambda x: -x["ms"])[:top]:
            print("  %5dms  frame=%-8d guests=%d" % (L["ms"], L["frame"], L["guests"]))
        分布 = {}
        for L in 长帧们:
            分布[L["guests"]] = 分布.get(L["guests"], 0) + 1
        print("  按客人数分布：", "  ".join("%d人×%d" % (g, n) for g, n in sorted(分布.items())))
    else:
        print("  没有长帧。如果玩家仍感觉卡，说明卡顿是持续低帧（看 fps 列），不是顿挫。")

    # 结论提示写在最后，避免先入为主
    print()
    print("-" * 118)
    峰值 = {k: max((w["埋点"].get(k, {}).get("峰", 0) for w in 窗口们), default=0) for k in ("采集", "绘制", "注入")}
    print("三个埋点各自的历史峰值： " + "  ".join("%s %.1fms" % (k, v) for k, v in 峰值.items() if v))
    域 = max(峰值, key=峰值.get) if any(峰值.values()) else None
    if 域:
        print("→ 峰值最高的是「%s」。单帧 16.7ms 是 60fps 的预算，超过它的埋点才值得优化。" % 域)
    return 0


# ---------------------------------------------------------------- 自检
def 自检():
    样例 = [
        "[Info   :    夜雀点单助手] [OrderHelper] 性能30s | 长帧累计 0 | frame=60000 | 采集 60次 均3.10ms 峰18.0ms | 绘制 9000次 均0.42ms 峰9.8ms\n",
        "[Warning:    夜雀点单助手] [OrderHelper] 长帧 312ms frame=61300 guests=6\n",
        "[Info   :    夜雀点单助手] [OrderHelper] 性能30s | 长帧累计 1 | frame=61800 | 采集 58次 均4.00ms 峰120.5ms | 绘制 8700次 均0.55ms 峰33.0ms | 注入 1次 均80.00ms 峰80.0ms\n",
        "[Info   :    夜雀点单助手] [OrderHelper] 心跳 frame=61000 visible=True guests=6\n",
        "[Info   :    夜雀点单助手] [OrderHelper] 性能30s | 长帧累计 1 | frame=1\n",  # 无埋点
        "[Info   :    夜雀点单助手] [OrderHelper] 性能30s 格式变了 | 长帧累计 x\n",   # 应记废行
    ]
    窗口们, 长帧们, 心跳们, 废行 = 解析(样例)
    项 = []

    项.append(("解析出 3 个窗口", len(窗口们) == 3))
    项.append(("长帧 312ms 被抓到", len(长帧们) == 1 and 长帧们[0]["ms"] == 312))
    项.append(("长帧能读出 guests=6", 长帧们 and 长帧们[0]["guests"] == 6))
    项.append(("埋点解析出 采集/绘制", 窗口们[0]["埋点"].get("采集", {}).get("峰") == 18.0
               and 窗口们[0]["埋点"].get("绘制", {}).get("次数") == 9000))
    项.append(("第二个窗口解析出 注入", 窗口们[1]["埋点"].get("注入", {}).get("均") == 80.0))
    项.append(("无埋点的窗口不崩", 窗口们[2]["埋点"] == {}))

    fps = 近似fps(窗口们)
    项.append(("fps 用帧差/30", fps[1] == (61800 - 60000) / 30.0))
    项.append(("首窗口 fps 为 None", fps[0] is None))
    项.append(("帧号回退时不瞎算 fps", fps[2] is None))

    场景们 = 场景(窗口们, 心跳们)
    项.append(("场景按帧号向前找心跳", 场景们[0] is None and 场景们[1]["guests"] == 6))

    项.append(("格式变的行记为废行", 废行 == 1))

    全过 = True
    for 名, 通过 in 项:
        print(("  ✅ " if 通过 else "  ❌ ") + 名)
        全过 = 全过 and 通过
    print("---- 自检 %d 项，%s" % (len(项), "全过" if 全过 else "有失败，别信报告"))
    return 0 if 全过 else 2


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--log", default=默认日志)
    ap.add_argument("--top", type=int, default=10)
    ap.add_argument("--selftest", action="store_true")
    a = ap.parse_args()

    if a.selftest:
        return 自检()

    if not os.path.isfile(a.log):
        print("找不到日志：%s" % a.log)
        return 1

    窗口们, 长帧们, 心跳们, 废行 = 解析(读日志(a.log))
    if 废行:
        print("⚠ 有 %d 行含关键词但解析不了，可能是日志格式变了，结论要打折。\n" % 废行)
    return 打印主表(窗口们, 长帧们, 心跳们, a.top)


if __name__ == "__main__":
    sys.exit(main())
