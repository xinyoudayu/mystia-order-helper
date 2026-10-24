using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

using UnityEngine;

namespace MystiaOrderHelper
{
    /// <summary>
    /// 轻量耗时探针：只统计，不改任何行为。
    /// 目的：卡顿到底是「一次长卡」还是「持续低帧」，以及卡在采集 / 计算 / 绘制哪一段。
    /// 输出频率被压得很低：长帧每分钟最多 20 条，汇总每 30 秒一行。
    ///
    /// v1.5.8 增加三样东西，专门用来抓「偶发尖峰」——平均值看不出来的那部分：
    ///   · 最慢三次：每段记录自己最慢的 3 次调用（毫秒 + 帧号），尖峰直接指名道姓
    ///   · 长帧归因：长帧日志带上「上一帧最慢的是哪一段」，不用再靠两个数字相减去猜
    ///   · 计数器：缓存命中/重算这类「次数」比「耗时」更说明问题的东西单独统计
    /// </summary>
    internal static class Perf
    {
        /// <summary>由配置 Diagnostics.PerfLog 控制。</summary>
        internal static bool On = true;

        /// <summary>单帧超过这个毫秒数就记一条长帧。</summary>
        private const float LongFrameMs = 150f;

        /// <summary>最慢记录只保留这么多条（排序用的固定槽位，不分配内存）。</summary>
        private const int TopSlots = 3;

        private struct Stat
        {
            public int Count;
            public double TotalMs;
            public double MaxMs;

            // 最慢的 3 次（S1 最慢），以及各自所在帧号
            public double S1, S2, S3;
            public int F1, F2, F3;
        }

        private static readonly Dictionary<string, Stat> Stats = new();
        private static readonly Dictionary<string, int> Counters = new();

        private static float _lastFrame = -1f;
        private static float _nextDump;
        private static int _longFrameBudget;
        private static float _budgetReset;
        private static int _longFrameCount;

        /// <summary>本帧跑得最慢的一段：长帧发生时用它回答「卡在哪一步」。</summary>
        private static string _frameWorstName;
        private static double _frameWorstMs;

        // 帧间隔直方图：长帧日志只记 ≥150ms 的极端情况，但玩家感觉到的掉帧多在 33–150ms。
        // 这里按区间数帧，一局下来就能判断「卡」是插件造成的，还是游戏本身就这样。
        private static int _fps60;     // <16.7ms（60fps 以上）
        private static int _fps30;     // 16.7–33.3ms（30–60fps）
        private static int _fps20;     // 33.3–50ms（20–30fps）
        private static int _fps10;     // 50–100ms（10–20fps）
        private static int _fpsLow;    // ≥100ms

        private static double Ms(long ticks)
        {
            return (Stopwatch.GetTimestamp() - ticks) * 1000.0 / Stopwatch.Frequency;
        }

        /// <summary>Update 里每帧调一次：测帧间隔 + 每 30 秒出一次汇总。</summary>
        internal static void BeginFrame(int guests)
        {
            if (!On) return;

            var now = Time.realtimeSinceStartup;
            if (_lastFrame < 0f)
            {
                _lastFrame = now;
                _nextDump = now + 30f;
                _budgetReset = now + 60f;
                _longFrameBudget = 20;
                _frameWorstName = null;
                _frameWorstMs = 0.0;
                return;
            }

            var ms = (now - _lastFrame) * 1000f;
            _lastFrame = now;

            // 帧间隔分桶（ms 是「上一帧到这一帧」的间隔）
            if (ms < 16.7f) _fps60++;
            else if (ms < 33.3f) _fps30++;
            else if (ms < 50f) _fps20++;
            else if (ms < 100f) _fps10++;
            else _fpsLow++;

            // 单帧超过 150ms = 玩家能明显感觉到的顿一下。
            // 限流：每分钟最多 20 条，避免持续低帧把日志刷爆。
            if (ms >= LongFrameMs)
            {
                _longFrameCount++;
                if (now >= _budgetReset)
                {
                    _budgetReset = now + 60f;
                    _longFrameBudget = 20;
                }

                if (_longFrameBudget > 0)
                {
                    _longFrameBudget--;

                    // 关键一行：这帧卡了这么久，插件自己最慢的那段是哪一段、用了多久。
                    // 若「最慢」只有 0.x ms，说明这 500ms 不是插件造成的（多半是场景加载/GC）。
                    var blame = _frameWorstName == null
                        ? "插件内无记录"
                        : _frameWorstName + " " + _frameWorstMs.ToString("0.0") + "ms";

                    OrderHelperPlugin.LogSource.LogWarning(
                        $"[OrderHelper] 长帧 {ms:0}ms frame={Time.frameCount} guests={guests} 上帧最慢={blame}");
                }
            }

            // 本帧已经消费完上一帧的「最慢」记录，清零给这一帧用。
            _frameWorstName = null;
            _frameWorstMs = 0.0;

            if (now >= _nextDump)
            {
                _nextDump = now + 30f;
                Dump();
            }
        }

        internal static long Start()
        {
            return On ? Stopwatch.GetTimestamp() : 0L;
        }

        internal static void End(string name, long t0)
        {
            if (!On || t0 == 0L) return;

            var ms = Ms(t0);

            if (ms > _frameWorstMs)
            {
                _frameWorstMs = ms;
                _frameWorstName = name;
            }

            if (!Stats.TryGetValue(name, out var stat))
            {
                Stats[name] = new Stat { Count = 1, TotalMs = ms, MaxMs = ms, S1 = ms, F1 = Time.frameCount };
                return;
            }

            stat.Count++;
            stat.TotalMs += ms;
            if (ms > stat.MaxMs) stat.MaxMs = ms;

            // 维护最慢三名（插入排序，固定 3 个槽位，无分配）
            var frame = Time.frameCount;
            if (ms > stat.S1)
            {
                stat.S3 = stat.S2; stat.F3 = stat.F2;
                stat.S2 = stat.S1; stat.F2 = stat.F1;
                stat.S1 = ms;      stat.F1 = frame;
            }
            else if (ms > stat.S2)
            {
                stat.S3 = stat.S2; stat.F3 = stat.F2;
                stat.S2 = ms;      stat.F2 = frame;
            }
            else if (ms > stat.S3)
            {
                stat.S3 = ms;      stat.F3 = frame;
            }

            Stats[name] = stat;
        }

        /// <summary>计一次「事件」而不是「耗时」：缓存命中、重算次数之类。</summary>
        internal static void Count(string name)
        {
            if (!On) return;
            Counters.TryGetValue(name, out var c);
            Counters[name] = c + 1;
        }

        private static string Pct(int n, int total)
        {
            return (n * 100.0 / total).ToString("0.0") + "%";
        }

        private static void Dump()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("[OrderHelper] 性能30s | 长帧累计 ").Append(_longFrameCount)
                  .Append(" | frame=").Append(Time.frameCount);

                foreach (var kv in Stats)
                {
                    sb.Append(" | ").Append(kv.Key)
                      .Append(' ').Append(kv.Value.Count).Append("次 均")
                      .Append((kv.Value.TotalMs / kv.Value.Count).ToString("0.00"))
                      .Append("ms 峰").Append(kv.Value.MaxMs.ToString("0.0")).Append("ms");
                }

                OrderHelperPlugin.LogSource.LogInfo(sb.ToString());

                // 第二行：计数器（次数比耗时更能说明问题，比如「重算了几次」）
                if (Counters.Count > 0)
                {
                    var cb = new StringBuilder("[OrderHelper] 计数30s");
                    foreach (var kv in Counters)
                        cb.Append(" | ").Append(kv.Key).Append(' ').Append(kv.Value);

                    OrderHelperPlugin.LogSource.LogInfo(cb.ToString());
                    Counters.Clear();
                }

                // 第三行：只把「最慢三次」里超过 6ms 的段列出来，方便直接定位尖峰
                var slow = new StringBuilder();
                foreach (var kv in Stats)
                {
                    var s = kv.Value;
                    if (s.S1 < 6.0) continue;

                    if (slow.Length == 0) slow.Append("[OrderHelper] 最慢三次");
                    slow.Append(" | ").Append(kv.Key).Append(' ')
                        .Append(s.S1.ToString("0.0")).Append("ms(f").Append(s.F1).Append(')');
                    if (s.S2 >= 1.0) slow.Append('/').Append(s.S2.ToString("0.0"));
                    if (s.S3 >= 1.0) slow.Append('/').Append(s.S3.ToString("0.0"));
                }

                if (slow.Length > 0) OrderHelperPlugin.LogSource.LogInfo(slow.ToString());

                // 第四行：帧间隔分布。判断「卡」是插件拖的还是游戏本身就这样，只有这一行能回答。
                var totalFrames = _fps60 + _fps30 + _fps20 + _fps10 + _fpsLow;
                if (totalFrames > 0)
                {
                    OrderHelperPlugin.LogSource.LogInfo(
                        "[OrderHelper] 帧率30s | 共 " + totalFrames + " 帧" +
                        " | 60fps以上 " + Pct(_fps60, totalFrames) +
                        " | 30~60fps " + Pct(_fps30, totalFrames) +
                        " | 20~30fps " + Pct(_fps20, totalFrames) +
                        " | 10~20fps " + Pct(_fps10, totalFrames) +
                        " | 低于10fps " + Pct(_fpsLow, totalFrames));

                    _fps60 = 0; _fps30 = 0; _fps20 = 0; _fps10 = 0; _fpsLow = 0;
                }

                Stats.Clear();
            }
            catch
            {
                // 探针本身绝不能影响游戏
            }
        }
    }
}
