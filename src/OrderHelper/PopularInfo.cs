using System;
using System.Collections.Generic;
using System.Text;

using GameData.Core.Collections.NightSceneUtility;
using GameData.RunTime.Common;

using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace MystiaOrderHelper
{
    /// <summary>
    /// 今日流行（风向）：全局流行标签表 + 标签名解析。
    /// 标签名借用 SpecialGuest.GetFoodTagText / GetBevTagText，解析不到时退回 #id。
    /// </summary>
    internal static class PopularInfo
    {
        internal static List<int> FoodLike = new();
        internal static List<int> FoodHate = new();
        internal static List<int> BevLike = new();
        internal static List<int> BevHate = new();

        private static readonly Dictionary<int, string> FoodNames = new();
        private static readonly Dictionary<int, string> BevNames = new();
        private static bool _warned;

        /// <summary>最近一次见到的稀客数据，用来按需把标签 id 翻成名字。</summary>
        private static SpecialGuest _sample;

        /// <summary>标签 id → 中文名（解析不到时退回 #id），供「命中标签」展示用。</summary>
        internal static string TagName(int tag, bool food)
        {
            if (tag < 0) return "";

            var cache = food ? FoodNames : BevNames;
            if (cache.TryGetValue(tag, out var cached)) return cached;

            var sample = _sample;
            if (sample == null) return "#" + tag;

            try
            {
                var text = food ? sample.GetFoodTagText(tag) : sample.GetBevTagText(tag);
                if (!string.IsNullOrEmpty(text))
                {
                    cache[tag] = text;
                    return text;
                }
            }
            catch
            {
                // 解析失败就退回 #id
            }

            return "#" + tag;
        }

        // 今日流行的展示文本。★ 必须缓存：OnGUI 每帧要读两次（Layout + Repaint），
        // 原来每次访问都新建 StringBuilder + 一串字符串，是每帧固定的一笔 GC 垃圾。
        // 因此这里存的是 Refresh（0.5 秒一次）算好的结果，OnGUI 只负责贴出来。

        /// <summary>主面板那一行「今日流行｜菜：… 酒：…」。</summary>
        internal static string BothText { get; private set; } = "今日流行｜菜：无    酒：无";

        /// <summary>推荐框顶部那一行（料理框 / 酒水框各用一条）。</summary>
        internal static string FoodLine { get; private set; } = "今日流行｜菜：无";
        internal static string BevLine { get; private set; } = "今日流行｜酒：无";

        private static string _foodText = "无";
        private static string _bevText = "无";


        internal static void Refresh(SpecialGuest sample)
        {
            try
            {
                if (sample != null) _sample = sample;
                FoodLike = ToList(RunTimePlayerData.PopLikeFoodTags);
                FoodHate = ToList(RunTimePlayerData.PopHateFoodTags);
                BevLike = ToList(RunTimePlayerData.PopLikeBevTags);
                BevHate = ToList(RunTimePlayerData.PopHateBevTags);

                if (sample != null)
                {
                    CacheNames(FoodLike, FoodNames, true, sample);
                    CacheNames(FoodHate, FoodNames, true, sample);
                    CacheNames(BevLike, BevNames, false, sample);
                    CacheNames(BevHate, BevNames, false, sample);
                }

                _foodText = Describe(FoodLike, FoodHate, true);
                _bevText = Describe(BevLike, BevHate, false);
                BothText = "今日流行｜菜：" + _foodText + "    酒：" + _bevText;
                FoodLine = "今日流行｜菜：" + _foodText;
                BevLine = "今日流行｜酒：" + _bevText;
            }
            catch (Exception e)
            {
                if (_warned) return;
                _warned = true;
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 读取流行失败: " + e.Message);
            }
        }

        private static void CacheNames(List<int> tags, Dictionary<int, string> cache, bool food, SpecialGuest sample)
        {
            foreach (var tag in tags)
            {
                if (cache.ContainsKey(tag)) continue;

                try
                {
                    var text = food ? sample.GetFoodTagText(tag) : sample.GetBevTagText(tag);
                    if (!string.IsNullOrEmpty(text))
                        cache[tag] = text;
                }
                catch
                {
                    // 单个标签解析失败就退回 #id
                }
            }
        }

        private static string Describe(List<int> like, List<int> hate, bool food)
        {
            if (like.Count == 0 && hate.Count == 0) return "无";

            var sb = new StringBuilder();
            if (like.Count > 0)
            {
                sb.Append("爱 ");
                AppendNames(sb, like, food);
            }

            if (hate.Count > 0)
            {
                if (sb.Length > 0) sb.Append("｜");
                sb.Append("厌 ");
                AppendNames(sb, hate, food);
            }

            return sb.ToString();
        }

        private static void AppendNames(StringBuilder sb, List<int> tags, bool food)
        {
            var cache = food ? FoodNames : BevNames;
            for (var i = 0; i < tags.Count; i++)
            {
                if (i > 0) sb.Append('、');
                sb.Append(cache.TryGetValue(tags[i], out var name) ? name : "#" + tags[i]);
            }
        }

        private static List<int> ToList(Il2CppSystem.Collections.Generic.List<int> tags)
        {
            var list = new List<int>();
            if (tags == null) return list;

            for (var i = 0; i < tags.Count; i++)
                list.Add(tags[i]);

            return list;
        }

        private static List<int> ToList(Il2CppStructArray<int> tags)
        {
            var list = new List<int>();
            if (tags == null) return list;

            for (var i = 0; i < tags.Length; i++)
                list.Add(tags[i]);

            return list;
        }
    }
}
