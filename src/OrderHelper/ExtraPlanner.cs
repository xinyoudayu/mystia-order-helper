using System;
using System.Collections.Generic;

using GameData.Core.Collections;
using GameData.RunTime.Common;

namespace MystiaOrderHelper
{
    /// <summary>一道菜的「加料方案」：加了哪些食材、加完命中多少、成本售价多少。</summary>
    internal sealed class ExtraPlan
    {
        /// <summary>加料的食材 id（按添加顺序；空数组表示不需要加料）。</summary>
        public int[] Ids = System.Array.Empty<int>();

        /// <summary>加料显示名，已拼成一行（"猪肉、洋葱"）。空串表示不加料。</summary>
        public string Names = "";

        public int LikeHits;
        public int HateHits;

        /// <summary>菜品售价（加料不改变售价，只看菜品本身）。</summary>
        public int Price;

        /// <summary>这道菜的总成本：菜谱自带食材 + 加料，都按食材价值算。</summary>
        public int Cost;

        /// <summary>利润 = 售价 − 总成本。</summary>
        public int Profit => Price - Cost;

        /// <summary>做完这一道后，用到的最紧张那样食材还剩几份（越大越安全）。</summary>
        public int MinRemain = int.MaxValue;

        /// <summary>加料后是否达到目标分（凑满粉评所需的菜分）。</summary>
        public bool Reached;

        /// <summary>
        /// 没加料的原因（"加料会做成黑暗料理"）。空串表示正常。用来告诉玩家
        /// 「不是没得加，是加了会变黑暗料理所以故意不加」。
        /// </summary>
        public string Note = "";

        /// <summary>因为会和菜谱冲突而被排除掉的候选食材数（日志用）。</summary>
        public int BlockedCount;

        public int Net => LikeHits - HateHits;
    }

    /// <summary>
    /// 加料规划：在「菜谱自带 tag」之外，把手上食材的 tag 也算进评价，并给出一个能到粉评、
    /// 又尽量省钱的加料组合。
    ///
    /// 三条游戏规则（决定了整个算法长什么样）：
    ///   1. 一锅最多 5 个食材，**包含菜谱自带的**。所以加料槽 = 5 − 菜谱食材数，
    ///      菜谱本身要 5 个食材的菜根本加不了料。
    ///   2. 加料不改变售价（售价只看菜品），只是多消耗食材 —— 所以加料越多利润越低，
    ///      能少加就少加，这里按「够分就停」来搜。
    ///   3. 加料的 tag 会和菜品 tag 合并参与评价；重复的 tag 只算一次，
    ///      所以一份食材只在它带来**新**喜好标签时才有价值。
    ///
    /// 排序原则：先要能到粉评，再看利润（售价 − 食材成本），最后看余量
    /// （做完之后最紧张的那样食材还剩几份）—— 别把某种食材吃到见底。
    /// </summary>
    internal static class ExtraPlanner
    {
        /// <summary>游戏上限：一次下锅最多 5 个食材（含菜谱自带）。</summary>
        internal const int SlotCap = 5;

        private sealed class IngMeta
        {
            public string Name;
            public int[] Tags;
            public int Value;
        }

        private static readonly Dictionary<int, IngMeta> Cache = new();

        /// <summary>库存快照。整局游戏里库存会变（做菜、买菜），所以每 0.4 秒最多重读一次。</summary>
        private static Dictionary<int, int> _stock;
        private static float _stockAt = -999f;
        private static bool _stockWarned;

        /// <summary>
        /// 只打一次样：把第一个算出来的售价/成本写进日志，方便回头核对数值是否合理。
        /// 分「有加料」和「没加料」两条 —— 只记第一条的话，第一条恰好没有加料就白记了
        /// （v1.5.7 就是这样：唯一的样例是「诗礼银杏 加料=无」，看不出加料数字对不对）。
        /// </summary>
        private static bool _sampleFilled;
        private static bool _sampleEmpty;

        /// <summary>冲突表样例最多打 3 条，避免日志刷屏。</summary>
        private static int _conflictSamples;

        private static string JoinIds(int[] ids)
        {
            if (ids == null || ids.Length == 0) return "";
            var parts = new string[ids.Length];
            for (var i = 0; i < ids.Length; i++) parts[i] = ids[i].ToString();
            return string.Join(",", parts);
        }

        /// <summary>
        /// 把游戏的 TagRules 打一份样（只打一次）。它不是本次判定的依据，
        /// 但它是「tag 之间是否互斥」最可能的原始表——先记下来，确认语义后下一版可以接进来。
        /// </summary>
        private static bool _tagRulesLogged;

        private static void LogTagRulesOnce()
        {
            if (_tagRulesLogged) return;
            _tagRulesLogged = true;

            try
            {
                var rules = DataBaseCore.TagRules;
                if (rules == null) return;

                var text = new System.Text.StringBuilder("[OrderHelper] TagRules 样例：");
                var shown = 0;
                var total = 0;
                var e = rules.GetEnumerator();
                while (e.MoveNext())
                {
                    total++;
                    if (shown >= 8) continue;

                    var key = e.Current.Key;
                    var value = e.Current.Value;
                    text.Append(key).Append("=>[");
                    if (value != null)
                    {
                        for (var i = 0; i < value.Length; i++)
                        {
                            if (i > 0) text.Append(',');
                            text.Append(value[i]);
                        }
                    }
                    text.Append("] ");
                    shown++;
                }

                text.Append("(共 ").Append(total).Append(" 条)");
                OrderHelperPlugin.LogSource?.LogInfo(text.ToString());
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource?.LogInfo("[OrderHelper] TagRules 读取失败: " + e.Message);
            }
        }

        internal static void Invalidate()
        {
            Cache.Clear();
            _stock = null;
            _stockAt = -999f;
            _sampleFilled = false;
            _sampleEmpty = false;
            // 注意：_conflictSamples 不在场景切换时重置——整局最多打 3 条，
            // 否则每开一天店就重复刷一遍同样的表。
        }

        private static IngMeta MetaOf(int id)
        {
            if (Cache.TryGetValue(id, out var cached)) return cached;

            try
            {
                var ingredient = DataBaseCore.RefIngredient(id);
                if (ingredient == null) return null;

                var meta = new IngMeta { Name = RecipeIndex.IngredientName(id) };
                meta.Tags = RecipeIndex.CopyInts(ingredient.Tags);

                try { meta.Value = ingredient.TrueValue; }
                catch { meta.Value = 0; }
                if (meta.Value <= 0)
                {
                    try { meta.Value = ingredient.BaseValue; }
                    catch { meta.Value = 0; }
                }

                Cache[id] = meta;
                return meta;
            }
            catch (Exception e)
            {
                if (!_stockWarned)
                {
                    _stockWarned = true;
                    OrderHelperPlugin.LogSource?.LogWarning("[OrderHelper] 读取食材定义失败: " + e.Message);
                }

                return null;
            }
        }

        /// <summary>
        /// 库存快照（食材 id → 份数）。做菜后库存会变，0.4 秒内的复用是安全的：
        /// 面板本身 0.5 秒才刷新一次客人数据。
        /// </summary>
        internal static Dictionary<int, int> Stock()
        {
            var now = UnityEngine.Time.realtimeSinceStartup;
            if (_stock != null && now - _stockAt < 0.4f) return _stock;

            var copy = new Dictionary<int, int>();
            try
            {
                var stock = RunTimeStorage.Ingredients;
                if (stock != null)
                {
                    var e = stock.GetEnumerator();
                    while (e.MoveNext()) copy[e.Current.Key] = e.Current.Value;
                }
            }
            catch (Exception ex)
            {
                if (!_stockWarned)
                {
                    _stockWarned = true;
                    OrderHelperPlugin.LogSource?.LogWarning("[OrderHelper] 读取食材库存失败: " + ex.Message);
                }
            }

            _stock = copy;
            _stockAt = now;
            return copy;
        }

        /// <summary>
        /// 给一道菜挑加料。targetPoints 是「这道菜需要拿几分」（= 4 − 酒的分数），
        /// 拿到就停手，不多加一份。
        /// </summary>
        internal static ExtraPlan Plan(
            int foodId,
            int recipeId,
            Recipe recipe,
            int[] required,
            int[] foodTags,
            IList<int> likes,
            IList<int> hates,
            int targetPoints,
            Dictionary<int, int> stock)
        {
            if (recipe == null) return null;

            var plan = new ExtraPlan();

            // 当前菜品的标签集合：菜谱自带 tag 起步，加料往里并。
            var tags = new List<int>(foodTags ?? System.Array.Empty<int>());
            plan.LikeHits = CountHits(tags, likes);
            plan.HateHits = CountHits(tags, hates);

            // 黑暗料理的判定依据只有一个可靠来源：菜品自己的 BanTags（禁止的 tag）。
            //   BanTags —— 加料带进这些 tag 会做成黑暗料理，绝对不能碰
            //
            // 这里**故意不看 AdditiveTags**：它是「加料之后这道菜实际获得的 tag」（运行时状态，
            // 基础数据上通常是空的），不是「允许加的 tag 白名单」。拿它当白名单会把正常加料全砍掉。
            // 它只出现在诊断日志里，用来核对语义。
            var avoid = OrderHelperPlugin.AvoidDarkCooking != null && OrderHelperPlugin.AvoidDarkCooking.Value;
            var banTags = avoid ? RecipeIndex.FoodBanTagsOf(foodId) : System.Array.Empty<int>();
            var additiveSeen = avoid ? RecipeIndex.FoodAdditiveTagsOf(foodId) : System.Array.Empty<int>();

            // 菜谱食材先占位：同一种食材要几份就占几份，剩下的才轮到加料。
            var used = new Dictionary<int, int>();
            if (required != null)
            {
                foreach (var id in required)
                    used[id] = used.TryGetValue(id, out var n) ? n + 1 : 1;
            }

            var slots = SlotCap - (required?.Length ?? 0);
            if (slots < 0) slots = 0;

            var picked = new List<int>();
            // 被冲突闸门挡掉的食材（按 id 去重：贪心每轮都会重新扫一遍，不能重复计数）。
            var blocked = new HashSet<int>();
            if (slots > 0 && stock != null)
            {
                // 够分就停；加一份也不涨分的食材直接跳过。
                while (picked.Count < slots && plan.Net < targetPoints)
                {
                    var bestId = -1;
                    var bestDelta = 0;
                    var bestLike = 0;
                    var bestHate = 0;
                    var bestValue = int.MaxValue;
                    var bestRemain = -1;

                    foreach (var pair in stock)
                    {
                        var id = pair.Key;
                        var have = pair.Value;
                        if (have <= 0) continue;

                        used.TryGetValue(id, out var taken);
                        // 这一份已经被菜谱占满，就不能再加（同一份食材不能下锅两次）。
                        if (have - taken <= 0) continue;

                        var meta = MetaOf(id);
                        if (meta == null || meta.Tags.Length == 0) continue;

                        // 闸门：这份食材带进来的「新 tag」会不会让这道菜变成黑暗料理。
                        // 已存在的 tag 不算（那是菜品本身的味道，加料没改变它）。
                        if (WouldConflict(meta.Tags, tags, banTags))
                        {
                            blocked.Add(id);
                            continue;
                        }

                        var addLike = 0;
                        var addHate = 0;
                        foreach (var tag in meta.Tags)
                        {
                            if (Contains(tags, tag)) continue;   // 重复 tag 不加分
                            if (Contains(likes, tag)) addLike++;
                            if (Contains(hates, tag)) addHate++;
                        }

                        var delta = addLike - addHate;
                        if (delta <= 0) continue;

                        var remain = have - taken - 1;
                        var better = bestId < 0
                                     || delta > bestDelta
                                     || (delta == bestDelta && meta.Value < bestValue)
                                     || (delta == bestDelta && meta.Value == bestValue && remain > bestRemain);

                        if (!better) continue;

                        bestId = id;
                        bestDelta = delta;
                        bestLike = addLike;
                        bestHate = addHate;
                        bestValue = meta.Value;
                        bestRemain = remain;
                    }

                    if (bestId < 0) break;

                    var pickedMeta = MetaOf(bestId);
                    if (pickedMeta != null)
                    {
                        foreach (var tag in pickedMeta.Tags)
                            if (!Contains(tags, tag)) tags.Add(tag);
                    }

                    plan.LikeHits += bestLike;
                    plan.HateHits += bestHate;
                    picked.Add(bestId);
                    used[bestId] = used.TryGetValue(bestId, out var now) ? now + 1 : 1;
                }
            }

            // 闸门 3（已取消）：v1.5.7 曾在这里挡「加料会把菜加成另一道菜」。
            // 现在不做这件事——游戏会把所有能做的菜一起列出来，玩家自己点要做哪道，
            // 手滑点错是玩家自己的选择，不该由插件替他把加料砍掉（实测被砍掉 85 份，
            // 74 次直接砍到「加料=无」，等于加料功能整个失效）。
            // 剩下的闸门只有一道：BanTags 黑暗料理（加进去会变黑暗料理的食材仍然拒绝）。
            plan.BlockedCount = blocked.Count;

            plan.Reached = plan.Net >= targetPoints;
            plan.Ids = picked.ToArray();
            plan.Names = NamesOf(picked);

            // 想加料却一份都没加成功：只可能是全被黑暗料理闸门挡掉了。告诉玩家原因，
            // 免得看着「明明能加料却没推荐」以为插件坏了。
            if (picked.Count == 0 && slots > 0 && plan.BlockedCount > 0 && plan.Net < targetPoints)
                plan.Note = "加料会做成黑暗料理";

            // 售价：加料不改变售价，只看菜品本身（TrueValue 取不到就退回 BaseValue）。
            plan.Price = RecipeIndex.FoodValueOfFood(foodId);

            // 成本：菜谱食材 + 加料，都按食材价值 × 份数。
            var cost = 0;
            var minRemain = int.MaxValue;
            foreach (var pair in used)
            {
                var meta = MetaOf(pair.Key);
                cost += (meta != null ? meta.Value : 0) * pair.Value;

                var have = 0;
                if (stock != null) stock.TryGetValue(pair.Key, out have);
                var left = have - pair.Value;
                if (left < minRemain) minRemain = left;
            }

            plan.Cost = cost;
            plan.MinRemain = minRemain == int.MaxValue ? 0 : Math.Max(0, minRemain);

            var wantSample = plan.Names.Length > 0 ? !_sampleFilled : !_sampleEmpty;
            if (wantSample)
            {
                if (plan.Names.Length > 0) _sampleFilled = true;
                else _sampleEmpty = true;

                OrderHelperPlugin.LogSource?.LogInfo(
                    "[OrderHelper] 加料核算样例：菜=" + RecipeIndex.FoodNameOfFood(foodId) +
                    " 售价=" + plan.Price + " 成本=" + plan.Cost + " 利润=" + plan.Profit +
                    " 加料=" + (plan.Names.Length > 0 ? plan.Names : "无") +
                    " 净分=" + plan.Net + "/需" + targetPoints + " 余量=" + plan.MinRemain);
            }

            // BanTags 的语义是「按字段名推断」的，第一次算到有内容的菜时记一条，
            // 方便回头核对：禁用表里如果出现明显的「正常 tag」，说明语义理解错了。
            if (_conflictSamples < 3)
            {
                _conflictSamples++;
                OrderHelperPlugin.LogSource?.LogInfo(
                    "[OrderHelper] 加料冲突表样例：菜=" + RecipeIndex.FoodNameOfFood(foodId) +
                    " 菜品tag=[" + JoinIds(foodTags) + "]" +
                    " 禁用tag=[" + JoinIds(banTags) + "]" +
                    " (AdditiveTags 仅参考=[" + JoinIds(additiveSeen) + "])" +
                    " 挡住=" + plan.BlockedCount + "份");
            }

            if (OrderHelperPlugin.AvoidDarkCooking != null && OrderHelperPlugin.AvoidDarkCooking.Value)
                LogTagRulesOnce();

            return plan;
        }

        private static string NamesOf(List<int> ids)
        {
            if (ids == null || ids.Count == 0) return "";

            var names = new List<string>();
            foreach (var id in ids)
            {
                var meta = MetaOf(id);
                names.Add(meta != null ? meta.Name : "#" + id);
            }

            return string.Join("、", names.ToArray());
        }

        /// <summary>
        /// 这份食材带进来的「新 tag」会不会把菜做成黑暗料理。
        ///
        /// 只看新 tag：菜品本来就有的 tag 是它的固有味道，加料并不会把它变成禁忌；
        /// 真正危险的是「加进去之后菜里多出了禁用的味道」。
        /// banTags 命中即冲突（游戏数据里菜品明确禁止的味道）。
        /// </summary>
        private static bool WouldConflict(int[] incoming, List<int> existing, int[] banTags)
        {
            if (banTags == null || banTags.Length == 0) return false;
            if (incoming == null) return false;

            for (var i = 0; i < incoming.Length; i++)
            {
                var tag = incoming[i];
                if (Contains(existing, tag)) continue;          // 菜品本来就有，不算新引入
                if (Contains(banTags, tag)) return true;
            }

            return false;
        }

        /// <summary>数 tags 里有几个在 list 里（重复 tag 只算一次，因为 tags 是集合）。</summary>
        private static int CountHits(List<int> tags, IList<int> list)
        {
            if (tags == null || list == null) return 0;

            var count = 0;
            foreach (var tag in list)
            {
                if (!Contains(tags, tag)) continue;
                count++;
            }

            return count;
        }

        private static bool Contains(List<int> tags, int tag)
        {
            for (var i = 0; i < tags.Count; i++)
                if (tags[i] == tag) return true;
            return false;
        }

        private static bool Contains(IList<int> list, int tag)
        {
            if (list == null) return false;
            for (var i = 0; i < list.Count; i++)
                if (list[i] == tag) return true;
            return false;
        }

        /// <summary>数组版本：禁用/许可表都是 int[]，单独一条避免走接口派发。</summary>
        private static bool Contains(int[] list, int tag)
        {
            if (list == null) return false;
            for (var i = 0; i < list.Length; i++)
                if (list[i] == tag) return true;
            return false;
        }
    }
}
