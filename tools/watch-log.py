#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
日志哨兵（本项目版）—— 只读 BepInEx\\LogOutput.log 的【新增行】

为什么不用 tail / 手工 grep：
  LogOutput.log 玩一轮就 1.8MB 以上（MetaMystia 联机插件刷屏），
  - 每次 grep 都重读全文件，慢且容易看到上一轮的内容，误判"已复现"；
  - tail -f 拿到的是"当前文件末尾"，日志被清空/轮转后会卡死不动；
  - 边写边抓会抓到半行（`[Info : OrderHelper] 直填食材 选中=` 这种残行），
    关键词匹配不上 → 漏报，而且看起来像"功能没跑"。

它干四件小事（全部只读，不碰游戏进程、不写游戏目录）：
  1) 基线：启动时把当前文件末尾记为起点，之前的内容不算"新的"。
  2) 增量：每轮从上次读到的字节位置继续读，不重读旧内容。
  3) 半行缓冲：最后一段没有换行符就留到下一轮，凑齐整行再判。
  4) 轮转检测：文件比记住的位置还小 = 被清空/轮转 → 位置拨回 0。

用法：
    python tools/watch-log.py                    # 盯新增行（挂 120 分钟）
    python tools/watch-log.py --minutes 30
    python tools/watch-log.py --scan             # 扫一遍历史全文就退出（复盘用）
    python tools/watch-log.py --keywords "OrderHelper,Warning"
    python tools/watch-log.py --selftest         # 自检 6 项，全过再信它

退出：Ctrl-C。
"""

import argparse
import os
import re
import sys
import time
import tempfile

# ---------------------------------------------------------------- 默认值

# 本机实测路径；换机器/换游戏只改这一行，或每次传 --log。
默认日志 = r"D:\SteamLibrary\steamapps\common\Touhou Mystia Izakaya\BepInEx\LogOutput.log"

# 这些词是本项目的"出事了/成功了"信号，换游戏时必须整套换掉。
# ★ 教训：不要把宽泛的插件名（OrderHelper）放进来 —— 它 5 秒一条心跳，
#   实测全命中 6978 条，等于没过滤。抓"信号"，不抓"来源"。
默认关键词 = [
    "[Warning",         # BepInEx 告警（含插件内 Warning）
    "[Error",
    "[Fatal",
    "挂补丁",            # 启动时数一遍补丁数量对不对（已挂补丁 + 挂补丁失败）
    "直填食材",          # 一键制作的核心成功信号：选中=N 可制作组合=M
    "推荐框客人",        # 每 5 秒的状态行，用来判断"客人不刷新"
    "失败",              # 任何 "xxx 失败"
    "异常",
]

# 已知刷屏源不预置；需要时 --exclude 手动加（例如 "MetaMystia"）。
默认输出 = os.path.join(".tmp", "watch-log.txt")


# ---------------------------------------------------------------- 核心

class 哨兵:
    """只按字节增量读日志。所有状态（位置、半行）都在实例里，方便自检。"""

    def __init__(self, 路径, 关键词, 从开头=False, 排除=()):
        self.路径 = 路径
        self.规则 = re.compile("|".join(re.escape(k) for k in 关键词), re.IGNORECASE)
        self.排除 = (re.compile("|".join(re.escape(k) for k in 排除), re.IGNORECASE)
                   if 排除 else None)
        self.位置 = 0            # 已读到的字节位置
        self.残行 = b""          # 上一轮没写完的半行
        self.轮转过 = False

        if not 从开头 and os.path.isfile(路径):
            self.位置 = os.path.getsize(路径)   # ★ 机制 1：基线

    def 拉取(self):
        """读一次新增内容，返回命中的完整行。没有新内容返回 []。"""
        if not os.path.isfile(self.路径):
            return []

        try:
            大小 = os.path.getsize(self.路径)
        except OSError:
            return []

        # ★ 机制 4：日志变小说明被清空或轮转，位置必须拨回 0，
        #   否则永远读到空，哨兵看着"在跑"其实已经死了。
        if 大小 < self.位置:
            self.轮转过 = True
            self.位置 = 0
            self.残行 = b""

        if 大小 == self.位置:
            return []

        # 每轮重开文件：轮转后旧句柄可能还指着被替换掉的那个文件。
        try:
            with open(self.路径, "rb") as f:
                f.seek(self.位置)
                块 = f.read()
                self.位置 = f.tell()
        except OSError:
            return []

        数据 = self.残行 + 块
        切点 = 数据.rfind(b"\n")

        # ★ 机制 3：没有换行 = 这行还没写完，留着，别判。
        if 切点 < 0:
            self.残行 = 数据
            return []

        self.残行 = 数据[切点 + 1:]
        # 按字节切、再整体解码，中文不会被劈成半个字。
        文本 = 数据[:切点 + 1].decode("utf-8", "replace")

        命中 = []
        for 行 in 文本.splitlines():
            if not 行.strip():
                continue
            if self.排除 is not None and self.排除.search(行):
                continue
            if self.规则.search(行):
                命中.append(行)
        return 命中


def 现在():
    return time.strftime("%H:%M:%S")


def 写结果(路径, 行们):
    目录 = os.path.dirname(路径)
    if 目录 and not os.path.isdir(目录):
        os.makedirs(目录, exist_ok=True)
    with open(路径, "w", encoding="utf-8") as w:
        w.write("# 日志哨兵抓到 %d 条 %s\n" % (len(行们), time.strftime("%Y-%m-%d %H:%M:%S")))
        for x in 行们:
            w.write(x + "\n")


def 签名(行):
    """去掉时间戳/指针/frame 号，只留"这句话本身"。
    同一句异常刷 954 遍，时间戳不同会被当成 954 个问题；去噪后是一个问题 ×954。"""
    行 = re.sub(r"\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:\.\d+)?", "<时间>", 行)
    行 = re.sub(r"\d{2}:\d{2}:\d{2}(?:\.\d+)?", "<时间>", 行)
    行 = re.sub(r"0x[0-9A-Fa-f]+", "<ptr>", 行)
    行 = re.sub(r"frame=\d+", "frame=<n>", 行)
    return 行


def 重复摘要(行们, 前=5):
    """同一行刷几百遍是常态（本例 954 条一模一样的 interop 异常）。
    只看条数会以为"到处都在炸"，看唯一行才看得清到底几个问题。"""
    from collections import Counter
    c = Counter(签名(x) for x in 行们)
    重 = [(n, l) for l, n in c.items() if n > 1]
    if not 重:
        return
    重.sort(reverse=True)
    print("---- 重复最多的 %d 类（同类只算一个问题）：" % min(前, len(重)))
    for n, l in 重[:前]:
        print("  ×%-5d %s" % (n, l[:120]))


def 跟随(日志, 关键词, 输出, 每几秒, 几分钟, 排除=()):
    s = 哨兵(日志, 关键词, 排除=排除)
    if not os.path.isfile(日志):
        print("[%s] 找不到日志：%s" % (现在(), 日志))
        print("        → 路径写错是最常见的坑；先开一次游戏确认它在长。")
        return 1

    总数 = []
    print("[%s] 哨兵挂上：%s" % (现在(), 日志))
    print("[%s] 基线 %d 字节，只看这之后的新行；每 %d 秒看一眼，挂 %d 分钟。"
          % (现在(), s.位置, 每几秒, 几分钟))
    print("[%s] 现在可以让用户去游戏里复现了。Ctrl-C 停。" % 现在())

    死线 = time.time() + 几分钟 * 60
    try:
        while time.time() < 死线:
            time.sleep(每几秒)
            for 行 in s.拉取():
                总数.append(行)
                print("[%s] %s" % (现在(), 行))
            if s.轮转过:
                print("[%s] 日志被清空/轮转，已从头继续跟。" % 现在())
                s.轮转过 = False
            if 总数:
                写结果(输出, 总数)
    except KeyboardInterrupt:
        print("\n[%s] 手动停了。" % 现在())

    if 总数:
        写结果(输出, 总数)
    重复摘要(总数)
    print("[%s] 哨兵收工：共抓到 %d 条。结果：%s" % (现在(), len(总数), 输出))
    return 0


def 扫描(日志, 关键词, 输出, 排除=()):
    """一次性把整个文件过一遍（复盘用）。同类行合并成 ×N，不然 954 行没法看。"""
    if not os.path.isfile(日志):
        print("找不到日志：%s" % 日志)
        return 1
    s = 哨兵(日志, 关键词, 从开头=True, 排除=排除)
    行们 = s.拉取()

    from collections import Counter
    计数 = Counter(签名(x) for x in 行们)
    for 行, n in 计数.items():
        print(("×%-5d " % n if n > 1 else "       ") + 行)

    if 行们:
        写结果(输出, 行们)
    print("---- 全文 %d 字节，命中 %d 条 / 唯一 %d 类。结果：%s"
          % (s.位置, len(行们), len(计数), 输出))
    return 0


# ---------------------------------------------------------------- 自检

def 自检():
    """★ 判据本身也会错：先证明这个哨兵不误报、不漏报，再拿它的结论当证据。"""
    结果 = []

    def 项(名, 通过, 备注=""):
        结果.append((名, 通过, 备注))

    目录 = tempfile.mkdtemp(prefix="watchlog-")
    路径 = os.path.join(目录, "LogOutput.log")
    关键词 = ["OrderHelper", "直填食材"]

    with open(路径, "wb") as f:
        f.write("[Info :OrderHelper] OLD 旧的一行\n".encode("utf-8"))

    # ① 基线：已有内容不算新
    s = 哨兵(路径, 关键词)
    项("① 基线不误报旧行", s.拉取() == [])

    # ② 新行能抓到
    with open(路径, "ab") as f:
        f.write("[Info :OrderHelper] NEW 新的一行\n".encode("utf-8"))
    命中 = s.拉取()
    项("② 新行能抓到", len(命中) == 1 and "NEW" in 命中[0])

    # ③ 半行不报，补全后才报（BepInEx 边写边抓的经典残行）
    with open(路径, "ab") as f:
        f.write("[Info :OrderHelper] 直填食材 选中=4".encode("utf-8"))
    项("③ 半行先不报", s.拉取() == [])
    with open(路径, "ab") as f:
        f.write(" 可制作组合=3\n".encode("utf-8"))
    命中 = s.拉取()
    项("③-2 补全后才报", len(命中) == 1 and "可制作组合=3" in 命中[0])

    # ④ 中文字节边界不被劈坏
    with open(路径, "ab") as f:
        f.write("直填食材 选中=6\n".encode("utf-8"))
    命中 = s.拉取()
    项("④ 中文不碎", len(命中) == 1 and "选中=6" in 命中[0] and "\ufffd" not in 命中[0])

    # ⑤ 日志被清空/轮转后不卡死
    with open(路径, "wb") as f:
        f.write(b"")
    s.拉取()
    with open(路径, "ab") as f:
        f.write("[Info :OrderHelper] AFTER 轮转后\n".encode("utf-8"))
    命中 = s.拉取()
    项("⑤ 轮转后继续跟", len(命中) == 1 and "AFTER" in 命中[0])

    try:
        os.remove(路径)
        os.rmdir(目录)
    except OSError:
        pass

    全过 = True
    for 名, 通过, _ in 结果:
        # 不用 emoji：Windows 默认 cp936 控制台打不出，会直接抛 UnicodeEncodeError。
        print(("  [OK] " if 通过 else "  [NG] ") + 名)
        全过 = 全过 and 通过
    print("---- %d/%d 通过" % (sum(1 for _, p, _ in 结果 if p), len(结果)))
    return 0 if 全过 else 1


# ---------------------------------------------------------------- 入口

def main():
    ap = argparse.ArgumentParser(description="只读新增行的日志哨兵（本项目版）")
    ap.add_argument("--log", dest="日志", default=默认日志, help="日志路径")
    ap.add_argument("--keywords", dest="关键词", default="",
                    help="逗号分隔，覆盖默认关键词")
    ap.add_argument("--exclude", dest="排除", default="",
                    help="逗号分隔，命中这些词的行丢弃（例如 MetaMystia 刷屏）")
    ap.add_argument("--out", dest="输出", default=默认输出, help="命中结果存哪儿")
    ap.add_argument("--interval", dest="每几秒", type=int, default=3)
    ap.add_argument("--minutes", dest="几分钟", type=int, default=120)
    ap.add_argument("--scan", action="store_true", help="扫一遍全文就退出")
    ap.add_argument("--selftest", action="store_true", help="自检 6 项")
    a = ap.parse_args()

    if a.selftest:
        return 自检()

    关键词 = [x.strip() for x in a.关键词.split(",") if x.strip()] or 默认关键词
    排除 = [x.strip() for x in a.排除.split(",") if x.strip()]
    if a.scan:
        return 扫描(a.日志, 关键词, a.输出, 排除)
    return 跟随(a.日志, 关键词, a.输出, a.每几秒, a.几分钟, 排除)


if __name__ == "__main__":
    sys.exit(main())
