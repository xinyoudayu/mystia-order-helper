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

        internal static string FoodText => Describe(FoodLike, FoodHate, true);
        internal static string BevText => Describe(BevLike, BevHate, false);

        internal static bool HasFoodPop => FoodLike.Count > 0 || FoodHate.Count > 0;
        internal static bool HasBevPop => BevLike.Count > 0 || BevHate.Count > 0;

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
