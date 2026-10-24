#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
把一局游戏的 BepInEx 日志压成一页报告。

只关心插件自己写的行（都带 [OrderHelper] 前缀），其余是游戏和别的 mod 的噪音。

用法：
    python tools\\run-report.py                    # 读游戏默认日志，出报告
    python tools\\run-report.py --log 路径.log
    python tools\\run-report.py --save baseline.json    # 存一份基准
    python tools\\run-report.py --compare baseline.json # 和基准比，给出前后差值

设计口径：
    · 性能日志是「每 30 秒一行」，一行里带多段的 次数/均值/峰值。
      报告按段聚合所有窗口，给出中位均值与峰最大值——**峰值才是玩家感觉到的卡顿**，
      均值只是背景噪音，两者必须分开看。
    · 「最慢三次」行是尖峰归因：哪个段、多少毫秒、第几帧。
    · 长帧行带「上帧最慢=…」，用来判断这一卡是不是插件造成的。
"""

import argparse
import json
import os
import re
import statistics
import sys

DEFAULT_LOG = r"D:\SteamLibrary\steamapps\common\Touhou Mystia Izakaya\BepInEx\LogOutput.log"

# [Info   :    夜雀点单助手] [OrderHelper] 性能30s | 长帧累计 25 | frame=844681 | 头像 46次 均1.68ms 峰14.1ms | ...
RE_PERF = re.compile(r"性能30s \| 长帧累计 (\d+) \| frame=(\d+)(.*)$")
RE_SEG = re.compile(r"([^\s|]+) (\d+)次 均([\d.]+)ms 峰([\d.]+)ms")
RE_COUNT = re.compile(r"计数30s(.*)$")
RE_COUNTER = re.compile(r"([^\s|]+) (\d+)")
RE_SLOW = re.compile(r"最慢三次(.*)$")
RE_SLOW_ITEM = re.compile(r"([^\s|]+) ([\d.]+)ms\(f(\d+)\)(?:/([\d.]+))?(?:/([\d.]+))?")
RE_LONG = re.compile(r"长帧 (\d+)ms frame=(\d+) guests=(\d+)(?: 上帧最慢=(.*))?$")
RE_DIRECT = re.compile(
    r"直填食材 food=(\d+) 写入=(\d+) 加料=(\d+)"
    r"(?: 游戏接受的加料=(-?\d+))? 选中=(\d+) 可制作组合=(\d+)")
RE_FPS = re.compile(r"帧率30s \| 共 (\d+) 帧 \| 60fps以上 ([\d.]+)% \| 30~60fps ([\d.]+)% "
                    r"\| 20~30fps ([\d.]+)% \| 10~20fps ([\d.]+)% \| 低于10fps ([\d.]+)%")

# 「上帧最慢」里插件这段超过这个毫秒数，才算「这一卡有插件的份」
BLAME_MS = 10.0


def read_lines(path):
    with open(path, "r", encoding="utf-8", errors="replace") as f:
        return f.read().splitlines()


def ts_of(line):
    """BepInEx 行里带 [HH:MM:SS.mmm] 的时候取出来，没有就返回空。"""
    m = re.search(r"\[(\d{2}:\d{2}:\d{2})\.\d+\]", line)
    return m.group(1) if m else ""


def analyse(path, whole=False):
    lines = read_lines(path)

    # BepInEx 默认每局覆盖日志，但万一以后改成追加，两局的数据混在一起就没法看。
    # 这里一律从「最后一次插件加载」那行开始算，保证报告只描述最近这一局。
    if not whole:
        start = 0
        for i, line in enumerate(lines):
            if "已加载（F8 主面板" in line:
                start = i
        if start:
            lines = lines[start:]

    ours = [l for l in lines if "[OrderHelper]" in l]

    r = {
        "log": path,
        "total_lines": len(lines),
        "plugin_lines": len(ours),
        "first_ts": "",
        "last_ts": "",
        "version": "",
        "scenes": 0,
        "toast_shown": 0,
        "toast_end": 0,
        "windows": 0,
        "long_frames": [],          # (ms, frame, guests, blame_text, blame_ms)
        "segments": {},             # name -> [avgs], [peaks]
        "slowest": {},              # name -> [(ms, frame)]
        "counters": {},             # name -> total
        "extra_samples": [],
        "conflict_samples": [],
        "tagrules": [],
        "extras_blocked": 0,
        "direct": [],               # (food, write, extra, selected, output)
        "fps": {"frames": 0, "60": 0, "30": 0, "20": 0, "10": 0, "low": 0},
        "warnings": {},
    }

    for line in lines:
        # 启动那行没有 [OrderHelper] 前缀（是插件名本身打的），所以单独在所有行里找
        if not r["version"]:
            m = re.search(r"夜雀点单助手 v([\d.]+) 已加载", line)
            if m:
                r["version"] = m.group(1)

    for line in ours:
        t = ts_of(line)
        if t:
            if not r["first_ts"]:
                r["first_ts"] = t
            r["last_ts"] = t
        if "版本提示已显示" in line:
            r["toast_shown"] += 1
        elif "版本提示结束" in line:
            r["toast_end"] += 1
        elif "场景加载:" in line:
            r["scenes"] += 1

        m = RE_PERF.search(line)
        if m:
            r["windows"] += 1
            for name, cnt, avg, peak in RE_SEG.findall(m.group(3)):
                s = r["segments"].setdefault(name, {"avgs": [], "peaks": [], "samples": 0})
                s["avgs"].append(float(avg))
                s["peaks"].append(float(peak))
                s["samples"] += int(cnt)
            continue

        m = RE_COUNT.search(line)
        if m:
            for name, num in RE_COUNTER.findall(m.group(1)):
                r["counters"][name] = r["counters"].get(name, 0) + int(num)
            continue

        m = RE_SLOW.search(line)
        if m:
            for name, ms, frame, s2, s3 in RE_SLOW_ITEM.findall(m.group(1)):
                bucket = r["slowest"].setdefault(name, [])
                bucket.append((float(ms), int(frame), float(s2) if s2 else 0.0,
                               float(s3) if s3 else 0.0))
            continue

        m = RE_LONG.search(line)
        if m:
            ms, frame, guests, blame = int(m.group(1)), int(m.group(2)), int(m.group(3)), m.group(4)
            blame_ms = 0.0
            if blame:
                bm = re.search(r"([\d.]+)ms", blame)
                if bm:
                    blame_ms = float(bm.group(1))
            r["long_frames"].append((ms, frame, guests, blame or "插件内无记录", blame_ms))
            continue

        if "加料核算样例" in line:
            r["extra_samples"].append(line.split("[OrderHelper] ")[-1])
        elif "加料冲突表样例" in line:
            r["conflict_samples"].append(line.split("[OrderHelper] ")[-1])
        elif "TagRules 样例" in line:
            r["tagrules"].append(line.split("[OrderHelper] ")[-1])
        elif "加料后一个组合都匹配不上，已退掉" in line:
            mm = re.search(r"已退掉 (\d+) 份", line)
            r["extras_blocked"] += int(mm.group(1)) if mm else 1
        elif "加料会让配方重复，已撤回" in line:
            # v1.5.7/v1.5.8 的旧日志，留着以便对比历史
            mm = re.search(r"已撤回 (\d+) 份", line)
            r["extras_blocked"] += int(mm.group(1)) if mm else 1

        m = RE_DIRECT.search(line)
        if m:
            g = m.groups()
            # 旧版日志没有「游戏接受的加料」字段，缺就用 -1 表示未采集
            r["direct"].append((
                int(g[0]), int(g[1]), int(g[2]),
                int(g[3]) if g[3] is not None else -1,
                int(g[4]), int(g[5]),
            ))
            continue

        m = RE_FPS.search(line)
        if m:
            total = int(m.group(1))
            r["fps"]["frames"] += total
            for key, idx in (("60", 2), ("30", 3), ("20", 4), ("10", 5), ("low", 6)):
                r["fps"][key] += int(round(float(m.group(idx)) * total / 100.0))
            continue

        for kw in ("失败", "异常", "不可用", "为空"):
            if kw in line:
                key = line.split("[OrderHelper] ")[-1]
                key = re.sub(r"\d+", "#", key)[:80]
                r["warnings"][key] = r["warnings"].get(key, 0) + 1
                break

    return r


def fmt_ms(v):
    return f"{v:.2f}" if v < 10 else f"{v:.1f}"


def report(r):
    out = []
    a = out.append

    a("=" * 72)
    a("本次运行概览")
    a("=" * 72)
    a(f"日志      : {r['log']}")
    a(f"规模      : {r['total_lines']:,} 行（插件自己 {r['plugin_lines']:,} 行）")
    if r["windows"]:
        a(f"运行时长  : 约 {r['windows'] * 30 // 60} 分 {r['windows'] * 30 % 60} 秒（按 30 秒汇总行推算）")
    if r["first_ts"]:
        a(f"时间段    : {r['first_ts']} ~ {r['last_ts']}")
    a(f"插件版本  : {('v' + r['version']) if r['version'] else '未识别'}")
    a(f"场景加载  : {r['scenes']} 次")

    toast = f"已显示 {r['toast_shown']} 次 / 结束 {r['toast_end']} 次"
    verdict = "符合「每次启动只弹一次」" if r["toast_shown"] == 1 else "★ 异常，应为 1 次"
    a(f"版本提示  : {toast}  → {verdict}")

    a("")
    a("=" * 72)
    a(f"性能：{r['windows']} 个 30 秒窗口")
    a("=" * 72)

    if not r["segments"]:
        a("（没有性能数据：请确认配置 [Diagnostics] PerfLog = true）")
    else:
        a(f"{'段':<14}{'样本':>8}{'中位均':>9}{'均范围':>16}{'峰最大':>9}{'峰>20ms':>9}")
        a("-" * 72)
        # 按「峰最大」降序：谁最容易让玩家感觉到卡，谁排前面
        rows = sorted(r["segments"].items(), key=lambda kv: -max(kv[1]["peaks"]))
        for name, s in rows:
            avgs, peaks = s["avgs"], s["peaks"]
            med = statistics.median(avgs)
            a(f"{name:<14}{s['samples']:>8}{fmt_ms(med):>9}"
              f"{fmt_ms(min(avgs)) + '~' + fmt_ms(max(avgs)):>16}"
              f"{max(peaks):>8.1f}{sum(1 for p in peaks if p > 20):>9}")

    if r["counters"]:
        a("")
        a("计数（次数比耗时更能说明问题）")
        a("-" * 72)
        hit = r["counters"].get("命中", 0)
        redo = r["counters"].get("重算", 0)
        parts = [f"{k} {v}" for k, v in sorted(r["counters"].items(), key=lambda kv: -kv[1])]
        a("  " + " ｜ ".join(parts))
        if hit + redo > 0:
            a(f"  采集缓存命中率：{hit / (hit + redo) * 100:.0f}%（{hit} 命中 / {redo} 重算）")

    if r["slowest"]:
        a("")
        a("最慢三次（尖峰归因，只列超过 6ms 的段）")
        a("-" * 72)
        rows = sorted(r["slowest"].items(), key=lambda kv: -max(x[0] for x in kv[1]))
        for name, samples in rows:
            top = max(samples, key=lambda x: x[0])
            a(f"{name:<14}{top[0]:>7.1f}ms  frame={top[1]}"
              + (f"   次慢 {top[2]:.1f} / {top[3]:.1f}" if top[3] > 0 else ""))

    a("")
    a("=" * 72)
    a(f"长帧：{len(r['long_frames'])} 次（单帧 ≥150ms）")
    a("=" * 72)

    if r["long_frames"]:
        ours = [f for f in r["long_frames"] if f[4] >= BLAME_MS]
        a(f"其中「插件这段 ≥{BLAME_MS:.0f}ms」的：{len(ours)} 次 —— 这些才需要继续优化")
        a(f"其余 {len(r['long_frames']) - len(ours)} 次插件内没有重活，基本是场景加载 / GC / 别的 mod")
        a("")
        for ms, frame, guests, blame, blame_ms in r["long_frames"][:15]:
            flag = "★" if blame_ms >= BLAME_MS else " "
            a(f"{flag} {ms:>5}ms frame={frame:<8} guests={guests:<3} {blame}")
        if len(r["long_frames"]) > 15:
            a(f"  …另有 {len(r['long_frames']) - 15} 条")

    a("")
    a("=" * 72)
    a("帧率分布（整局累计）")
    a("=" * 72)

    fps = r["fps"]
    if fps["frames"] == 0:
        a("（没有数据：这一版之前没有帧率统计）")
    else:
        total = fps["frames"]
        bars = (("60fps 以上", "60"), ("30~60fps", "30"), ("20~30fps", "20"),
                ("10~20fps", "10"), ("低于 10fps", "low"))
        for label, key in bars:
            n = fps[key]
            pct = n * 100.0 / total
            a(f"  {label:<12}{n:>8} 帧 {pct:>6.1f}%  " + "█" * int(pct / 2))
        smooth = fps["60"] * 100.0 / total
        a("")
        if smooth >= 95:
            a("  → 全程几乎满帧，插件没有拖累游戏")
        elif smooth >= 85:
            a("  → 偶有掉帧，若体感不明显可不动；若明显，重点看上面的「最慢三次」")
        else:
            a("  → 掉帧偏多，先对照「最慢三次」：插件各段都很小就说明是游戏本身/别的 mod")

    a("")
    a("=" * 72)
    a("加料 / 黑暗料理")
    a("=" * 72)

    for s in r["extra_samples"][:3]:
        a("  " + s)
    for s in r["conflict_samples"][:4]:
        a("  " + s)
    for s in r["tagrules"][:2]:
        a("  " + s[:200])
    if r["extras_blocked"]:
        a(f"  加料后一个组合都匹配不上而退掉的份数：{r['extras_blocked']} 份")

    if r["direct"]:
        a("")
        n = len(r["direct"])
        with_extra = sum(1 for d in r["direct"] if d[2] > 0)
        accepted = sum(1 for d in r["direct"] if d[3] > 0)
        failed = [d for d in r["direct"] if d[4] == 0 and d[5] == 0]
        a(f"一键制作：{n} 次，其中带加料 {with_extra} 次，"
          f"游戏真正接受的加料 {accepted} 次，写入后「选中=0 且无组合」{len(failed)} 次")
        for d in failed[:5]:
            a(f"  ★ 失败样例 food={d[0]} 写入={d[1]} 加料={d[2]}")

    if r["warnings"]:
        a("")
        a("=" * 72)
        a("警告 / 异常")
        a("=" * 72)
        for k, v in sorted(r["warnings"].items(), key=lambda kv: -kv[1])[:12]:
            a(f"  ×{v:<4} {k}")

    return "\n".join(out)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--log", default=DEFAULT_LOG)
    ap.add_argument("--all", action="store_true", help="不切会话，整份日志都算")
    ap.add_argument("--save", help="把本次关键指标存成 json 基准")
    ap.add_argument("--compare", help="和之前存的基准比")
    args = ap.parse_args()

    if not os.path.exists(args.log):
        print(f"找不到日志：{args.log}")
        return 1

    r = analyse(args.log, whole=args.all)
    print(report(r))

    snapshot = {
        "version": r["version"],
        "windows": r["windows"],
        "segments": {k: {"med": statistics.median(v["avgs"]), "peak": max(v["peaks"])}
                     for k, v in r["segments"].items()},
        "counters": r["counters"],
        "long_frames": len(r["long_frames"]),
        "long_frames_ours": sum(1 for f in r["long_frames"] if f[4] >= BLAME_MS),
    }

    if args.save:
        with open(args.save, "w", encoding="utf-8") as f:
            json.dump(snapshot, f, ensure_ascii=False, indent=2)
        print(f"\n基准已保存：{args.save}")

    if args.compare:
        with open(args.compare, "r", encoding="utf-8") as f:
            base = json.load(f)
        print("\n" + "=" * 72)
        print(f"对比基准：v{base.get('version', '?')} → v{r['version'] or '?'}")
        print("=" * 72)
        print(f"{'段':<14}{'中位均(前→后)':>22}{'峰(前→后)':>22}")
        print("-" * 72)
        for name, cur in sorted(snapshot["segments"].items()):
            b = base.get("segments", {}).get(name)
            if not b:
                continue
            print(f"{name:<14}{b['med']:>9.2f} → {cur['med']:<8.2f}"
                  f"{b['peak']:>9.1f} → {cur['peak']:<8.1f}")
        print(f"\n长帧 {base.get('long_frames', 0)} → {len(r['long_frames'])}"
              f"（其中插件有责 {base.get('long_frames_ours', 0)} → {snapshot['long_frames_ours']}）")

    return 0


if __name__ == "__main__":
    sys.exit(main())
