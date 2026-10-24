using System;
using System.Collections.Generic;

using GameData.Core.Collections;
using GameData.RunTime.Common;

namespace MystiaOrderHelper
{
    internal sealed class DishCandidate
    {
        public string Name = "?";
        public string Cooker = "";
        public bool CanCook;
        public string Missing = "";

        /// <summary>菜品来源（Find 时填充）。</summary>
        public Recipe Recipe;

        /// <summary>菜谱 id（Find 时填充，查缓存用）。</summary>
        public int RecipeId = -1;

        /// <summary>酒水来源（FindBeverages 时填充）。</summary>
        public Sellable Sellable;

        /// <summary>酒水 id（FindBeverages 时填充，查库存用）。</summary>
        public int SellableId = -1;

        /// <summary>推荐指数 0-100：按游戏评价分折算（粉评=95、橙评=80、绿评=60、紫评=40、黑评=10）。</summary>
        public int Score;

        /// <summary>菜品命中客人喜好标签的数量（含点单需求那个标签）。</summary>
        public int LikeHits;

        /// <summary>命中厌恶标签数量。</summary>
        public int HateHits;

        /// <summary>本项在游戏评价里的分数：喜好命中数 - 厌恶命中数。</summary>
        public int Points;

        /// <summary>排序用的粗评分（0-100，来自 ScoreOf）。ApplyRatings 覆盖 Score 前先存这里，供同分排序。</summary>
        public int BaseScore;

        /// <summary>游戏评价合计分：本项 + 最佳搭配合计（粉评门槛 4 分）。</summary>
        public int TotalPoints;

        /// <summary>预计能拿到的评价：粉评/橙评/绿评/紫评/黑评。</summary>
        public string Rating = "";

        /// <summary>指数构成说明（日志用）。</summary>
        public string ScoreNote = "";

        /// <summary>是否命中客人点单要求的标签/菜品。</summary>
        public bool RequestHit = true;

        /// <summary>命中的客人喜好标签 id（按命中顺序，展示用）。</summary>
        public List<int> HitTags = new();

        /// <summary>推荐框按钮文字（收集阶段预生成，避免每帧重复拼字符串）。</summary>
        public string ButtonLabel = "";

        /// <summary>第二行明细：菜品是食材清单，酒水留空（收集阶段预生成）。</summary>
        public string DetailText = "";

        /// <summary>按钮上真正显示的整段文字（含义见上：收集阶段预生成，别在 OnGUI 里拼）。</summary>
        public string FullLabel = "";

        /// <summary>
        /// 加料方案（v1.5.5）：菜谱 tag 之外再补哪些食材、补完能到几分、利润和余量。
        /// 为空表示这道菜不需要 / 不能加料（例如菜谱本身就要 5 个食材，没有槽位）。
        /// </summary>
        public ExtraPlan Plan;

        /// <summary>加料显示名（"猪肉、洋葱"），没加料时为空串。</summary>
        public string ExtraText = "";

        /// <summary>这道菜的利润（售价 − 食材成本，含加料）。没有方案时为 0。</summary>
        public int Profit;

        /// <summary>
        /// 单价（v1.6.1）：菜/酒各自的售价，预算判断用。
        /// 和 Profit 分开是因为含义不同：菜是「售价−成本」，酒没有食材成本所以两者相等；
        /// 而判断「客人付不付得起」必须用**售价**，不能用利润（利润小不代表便宜）。
        /// 取不到时为 0（预算判断会把 0 当「不知道」，不拦）。
        /// </summary>
        public int Price;

        /// <summary>做完之后最紧张的那样食材还剩几份（越大越安全）。</summary>
        public int MinRemain;
    }

    /// <summary>
    /// 把「需求」翻译成「现在能做的菜」。
    /// 需求有两种可能：某个菜品 ID（精确），或某个标签（匹配菜品标签集合）。
    /// </summary>
    internal static class RecipeIndex
    {
        // ── 静态数据缓存 ────────────────────────────────────────────────────────
        // 菜名、标签、食材、厨具类型在整局游戏里都不会变，但原来每 0.5 秒、每位客人
        // 都要把整张食谱表穿进 IL2CPP 重读一遍（food.Tags 是数组、food.Text.Name 是字符串，
        // 每次访问都要 marshal + 分配）。实测 5 位客人时一次采集 30ms、峰值 101ms，
        // 就是这些重复读取堆出来的。这里按 id 缓存一份纯 C# 副本，热路径上不再进 IL2CPP。
        private sealed class ItemMeta
        {
            public string Name;
            public int[] Tags;

            /// <summary>菜品/酒水的价值（售价）。加料规划算利润要用；取不到时为 0。</summary>
            public int Value;

            /// <summary>
            /// 菜品「禁止的 tag」：加料带进这些 tag 会做成黑暗料理。
            /// 字段缺失或读不到时是空数组，表示这道菜没有禁用表（闸门自动失效）。
            /// </summary>
            public int[] BanTags;

            /// <summary>
            /// 菜品「认可的加料 tag」：非空时，加料只能引入这里面的 tag。
            /// 空数组表示游戏没给这张表，此时只按 BanTags 判定。
            /// </summary>
            public int[] AdditiveTags;

            /// <summary>原始对象（酒水一键制作要拿它）。与 RecipeCache 一样随场景切换清空。</summary>
            public Sellable Item;
        }

        private sealed class RecipeMeta
        {
            public Recipe Recipe;
            public int FoodId;
            public ItemMeta Food;
            public int[] Ingredients;
            public string Cooker;
        }

        private static readonly Dictionary<int, ItemMeta> FoodCache = new();
        private static readonly Dictionary<int, ItemMeta> BevCache = new();
        private static readonly Dictionary<int, RecipeMeta> RecipeCache = new();
        private static readonly Dictionary<int, string> IngredientNameCache = new();

        /// <summary>整份饮品 id 表（第一次用到时取一次，之后纯 C# 遍历）。</summary>
        private static List<int> _bevIds;

        /// <summary>已解锁菜谱 id 表。清空后重新取一次。</summary>
        private static int[] _recipeIds;
        private static int _recipeIdsCount = -1;

        /// <summary>场景切换时清空：游戏可能重载数据表，指针也可能被复用。</summary>
        internal static void Invalidate()
        {
            FoodCache.Clear();
            BevCache.Clear();
            RecipeCache.Clear();
            IngredientNameCache.Clear();
            _bevIds = null;
            _recipeIds = null;
            _recipeIdsCount = -1;
            ExtraPlanner.Invalidate();
        }

        /// <summary>菜品的标签（按菜品 id，纯 C# 副本；加料规划用，查不到返回空数组）。</summary>
        internal static int[] FoodTagsOfFood(int foodId)
        {
            return FoodMetaOf(foodId)?.Tags ?? System.Array.Empty<int>();
        }

        /// <summary>菜品的售价（按菜品 id；查不到返回 0）。</summary>
        internal static int FoodValueOfFood(int foodId)
        {
            return FoodMetaOf(foodId)?.Value ?? 0;
        }

        /// <summary>菜品的显示名（日志用）。</summary>
        internal static string FoodNameOfFood(int foodId)
        {
            return FoodMetaOf(foodId)?.Name ?? ("菜品 #" + foodId);
        }

        /// <summary>菜谱自带食材 id 表（缓存副本，加料规划用；查不到返回空数组）。</summary>
        internal static int[] RecipeIngredients(int recipeId)
        {
            var meta = RecipeMetaOf(recipeId);
            return meta?.Ingredients ?? System.Array.Empty<int>();
        }

        internal static int[] CopyInts(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> src)
        {
            if (src == null) return System.Array.Empty<int>();
            var copy = new int[src.Length];
            for (var i = 0; i < src.Length; i++) copy[i] = src[i];
            return copy;
        }

        /// <summary>IL2CPP 的 List&lt;int&gt; 同样要复制成纯 C# 数组，热路径上不再碰它。</summary>
        internal static int[] CopyInts(Il2CppSystem.Collections.Generic.List<int> src)
        {
            if (src == null) return System.Array.Empty<int>();
            var count = src.Count;
            if (count <= 0) return System.Array.Empty<int>();
            var copy = new int[count];
            for (var i = 0; i < count; i++) copy[i] = src[i];
            return copy;
        }

        /// <summary>菜品禁止的 tag（加料带进来会做成黑暗料理）。查不到返回空数组。</summary>
        internal static int[] FoodBanTagsOf(int foodId)
        {
            return FoodMetaOf(foodId)?.BanTags ?? System.Array.Empty<int>();
        }

        /// <summary>菜品认可的加料 tag（非空时作为白名单）。查不到返回空数组。</summary>
        internal static int[] FoodAdditiveTagsOf(int foodId)
        {
            return FoodMetaOf(foodId)?.AdditiveTags ?? System.Array.Empty<int>();
        }

        /// <summary>已解锁菜谱 id 表（加料组合校验要遍历）。</summary>
        internal static int[] RecipeIds()
        {
            return UnlockedRecipeIds();
        }

        /// <summary>菜谱对应的菜品 id（判断「两个菜谱是不是同一道菜」用）。查不到返回 -1。</summary>
        internal static int RecipeFoodId(int recipeId)
        {
            var meta = RecipeMetaOf(recipeId);
            return meta != null ? meta.FoodId : -1;
        }

        private static ItemMeta MetaOf(Sellable item, int id)
        {
            if (item == null) return null;

            var meta = new ItemMeta { Name = SafeName(item, id), Item = item };

            // 售价：加料不改变售价，所以利润计算里「售价」只跟菜品本身有关。
            // TrueValue 是游戏自己的最终价值（含等级修正），取不到再退回 BaseValue。
            try { meta.Value = item.TrueValue; }
            catch { meta.Value = 0; }
            if (meta.Value <= 0)
            {
                try { meta.Value = item.BaseValue; }
                catch { meta.Value = 0; }
            }

            try
            {
                var tags = item.Tags;
                if (tags == null)
                {
                    meta.Tags = System.Array.Empty<int>();
                }
                else
                {
                    meta.Tags = new int[tags.Length];
                    for (var i = 0; i < tags.Length; i++) meta.Tags[i] = tags[i];
                }
            }
            catch
            {
                meta.Tags = System.Array.Empty<int>();
            }

            // 黑暗料理相关的两张表。读不到不是错误（有些数据可能没填），
            // 但**必须留痕**：闸门悄悄失效比误杀更危险，所以第一次读不到会写一条警告。
            try
            {
                meta.BanTags = CopyInts(item.BanTags);
            }
            catch
            {
                meta.BanTags = System.Array.Empty<int>();
                WarnOnceBanTags(id, "BanTags");
            }

            try
            {
                var additive = item.AdditiveTags;
                meta.AdditiveTags = CopyInts(additive);
            }
            catch
            {
                meta.AdditiveTags = System.Array.Empty<int>();
                WarnOnceBanTags(id, "AdditiveTags");
            }

            return meta;
        }

        private static bool _banTagsWarned;

        /// <summary>读不到禁用表只警告一次，避免每道菜刷屏。</summary>
        private static void WarnOnceBanTags(int id, string what)
        {
            if (_banTagsWarned) return;
            _banTagsWarned = true;
            OrderHelperPlugin.LogSource?.LogWarning(
                "[OrderHelper] 读取菜品 " + what + " 失败（菜品 #" + id + "）：防黑暗料理判定会退化成只按 tag 贪心");
        }

        private static ItemMeta FoodMetaOf(int foodId)
        {
            if (FoodCache.TryGetValue(foodId, out var cached)) return cached;
            try
            {
                if (!DataBaseCore.Foods.TryGetValue(foodId, out var food) || food == null) return null;
                var meta = MetaOf(food, foodId);
                if (meta == null) return null;
                FoodCache[foodId] = meta;
                return meta;
            }
            catch
            {
                return null;
            }
        }

        private static RecipeMeta RecipeMetaOf(int recipeId)
        {
            if (RecipeCache.TryGetValue(recipeId, out var cached)) return cached;
            try
            {
                if (!DataBaseCore.Recipes.TryGetValue(recipeId, out var recipe) || recipe == null) return null;
                var food = FoodMetaOf(recipe.FoodID);
                if (food == null) return null;

                var meta = new RecipeMeta
                {
                    Recipe = recipe,
                    FoodId = recipe.FoodID,
                    Food = food,
                    Ingredients = CopyInts(recipe.Ingredients),
                    Cooker = CookerName(recipe.CookerType.ToString()),
                };
                RecipeCache[recipeId] = meta;
                return meta;
            }
            catch
            {
                return null;
            }
        }

        private static ItemMeta BevMetaOf(int id)
        {
            if (BevCache.TryGetValue(id, out var cached)) return cached;
            try
            {
                var beverages = DataBaseCore.Beverages;
                if (beverages == null || !beverages.TryGetValue(id, out var beverage) || beverage == null) return null;
                var meta = MetaOf(beverage, id);
                if (meta == null) return null;
                BevCache[id] = meta;
                return meta;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>已解锁菜谱 id 表：每次采集都会用到，复制出来免得每位客人都穿一遍 Il2Cpp 列表。</summary>
        private static int[] UnlockedRecipeIds()
        {
            try
            {
                var list = RunTimeStorage.Recipes;
                if (list == null) return System.Array.Empty<int>();

                var count = list.Count;   // 一次 interop；数量没变就沿用上次的副本
                if (_recipeIds != null && count == _recipeIdsCount) return _recipeIds;

                var ids = new int[count];
                for (var i = 0; i < count; i++) ids[i] = list[i];
                _recipeIds = ids;
                _recipeIdsCount = count;
                return ids;
            }
            catch
            {
                return System.Array.Empty<int>();
            }
        }

        /// <summary>饮品 id 表：只在第一次（或换场景后）枚举一次 IL2CPP 字典。</summary>
        private static List<int> BevIds()
        {
            if (_bevIds != null) return _bevIds;
            var ids = new List<int>();
            try
            {
                var beverages = DataBaseCore.Beverages;
                if (beverages == null) return ids;
                var e = beverages.GetEnumerator();
                while (e.MoveNext()) ids.Add(e.Current.Key);
            }
            catch
            {
                // 返回已经拿到的部分，下轮再补
            }

            _bevIds = ids;
            return ids;
        }

        /// <summary>纯 C# 的标签判定（原 HasTag 每次都要穿 interop 取 Tags）。</summary>
        private static bool HasTag(int[] tags, int tag)
        {
            if (tags == null) return false;
            for (var i = 0; i < tags.Length; i++)
                if (tags[i] == tag) return true;
            return false;
        }

        internal static List<DishCandidate> Find(int request, bool exactDish, IList<int> likes = null, IList<int> hates = null, int max = 3, bool unlimited = false)
        {
            var result = new List<DishCandidate>();
            var perfT0 = Perf.Start();

            try
            {
                var ids = UnlockedRecipeIds();
                if (ids.Length == 0)
                {
                    Perf.End("采集·找菜", perfT0);
                    return result;
                }

                // 第一段：只数标签（便宜），把候选按标签分排好。
                // 「能不能做」要逐个查库存字典（穿进 IL2CPP），放在第二段只算真正会展示的那几道，
                // 否则 0.5 秒一次的刷新会把整个食谱表都按可做性算一遍，主线程直接卡一下。
                for (var i = 0; i < ids.Length; i++)
                {
                    // 这一段原来是 3 次 IL2CPP 取值 + N 次标签数组 marshalling，
                    // 现在是纯 C# 字典命中（第一次访问某条食谱时才填缓存）。
                    var id = ids[i];
                    var meta = RecipeMetaOf(id);
                    if (meta == null) continue;

                    var hit = exactDish ? meta.FoodId == request : (unlimited || HasTag(meta.Food.Tags, request));
                    if (!hit) continue;

                    CountTags(meta.Food.Tags, likes, hates, out var likeHits, out var hateHits, out var hitTags);

                    result.Add(new DishCandidate
                    {
                        Name = meta.Food.Name,
                        Cooker = meta.Cooker,
                        Recipe = meta.Recipe,
                        RecipeId = id,
                        BaseScore = TagScore(likeHits, hateHits),
                        LikeHits = likeHits,
                        HateHits = hateHits,
                        Points = likeHits - hateHits,
                        HitTags = hitTags,
                        Price = meta.Food.Value,
                    });
                }

                result.Sort((a, b) => b.BaseScore.CompareTo(a.BaseScore));
                var keep = PoolSize(max);
                if (result.Count > keep) result.RemoveRange(keep, result.Count - keep);

                // 第二段：对入围的算可做性和最终指数。
                for (var i = 0; i < result.Count; i++)
                {
                    var candidate = result[i];
                    var ingredients = RecipeCache.TryGetValue(candidate.RecipeId, out var meta)
                        ? meta.Ingredients
                        : CopyInts(candidate.Recipe?.Ingredients);
                    candidate.CanCook = CanCook(ingredients, out var missing);
                    candidate.Missing = missing;
                    candidate.Score = ScoreOf(candidate.BaseScore, candidate.CanCook);
                    candidate.ScoreNote = $"like{candidate.LikeHits} hate{candidate.HateHits} avail{(candidate.CanCook ? 1 : 0)}";
                }

                result.Sort((a, b) =>
                {
                    var byScore = b.Score.CompareTo(a.Score);
                    return byScore != 0 ? byScore : b.CanCook.CompareTo(a.CanCook);
                });
                if (result.Count > max) result.RemoveRange(max, result.Count - max);
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 匹配食谱失败: " + e.Message);
            }

            Perf.End("采集·找菜", perfT0);
            return result;
        }

        /// <summary>
        /// 从指定菜谱集合里挑（特殊厨具用：三位一体等面板自带一份固定菜谱，只能做里面有的）。
        /// </summary>
        internal static List<DishCandidate> FromPanel(
            IList<Recipe> recipes, int requestTag, IList<int> likes = null, IList<int> hates = null, int max = 3)
        {
            var result = new List<DishCandidate>();
            if (recipes == null) return result;

            try
            {
                var seen = new HashSet<int>();
                foreach (var recipe in recipes)
                {
                    if (recipe == null) continue;
                    var meta = FoodMetaOf(recipe.FoodID);
                    if (meta == null) continue;
                    if (!seen.Add(recipe.FoodID)) continue;

                    var tagHit = requestTag < 0 || HasTag(meta.Tags, requestTag);
                    var canCook = CanCook(CopyInts(recipe.Ingredients), out var missing);
                    CountTags(meta.Tags, likes, hates, out var likeHits, out var hateHits, out var hitTags);
                    var score = ScoreOf(TagScore(likeHits, hateHits), canCook);

                    result.Add(new DishCandidate
                    {
                        Name = meta.Name + (tagHit ? "" : "（点单标签不符）"),
                        RequestHit = tagHit,
                        Cooker = CookerName(recipe.CookerType.ToString()),
                        CanCook = canCook,
                        Missing = missing,
                        Recipe = recipe,
                        Score = Math.Max(0, score - (tagHit ? 0 : 40)),
                        ScoreNote = $"like{likeHits} hate{hateHits} avail{(canCook ? 1 : 0)}",
                        LikeHits = likeHits,
                        HateHits = hateHits,
                        Points = likeHits - hateHits,
                        HitTags = hitTags,
                        Price = meta.Value,
                    });
                }
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 按面板菜谱匹配失败: " + e.Message);
            }

            // 先只保「标签对得上」的候选；一道都没有时才退而求其次显示不符的。
            var hits = result.FindAll(c => c.RequestHit);
            if (hits.Count > 0) result = hits;

            result.Sort((a, b) =>
            {
                var byScore = b.Score.CompareTo(a.Score);
                return byScore != 0 ? byScore : b.CanCook.CompareTo(a.CanCook);
            });
            if (result.Count > max)
                result.RemoveRange(max, result.Count - max);

            return result;
        }

        internal static bool FoodsContain(int id)
        {
            try { return DataBaseCore.Foods.ContainsKey(id); }
            catch { return false; }
        }

        internal static List<DishCandidate> FindBeverages(int request, bool exactDish, IList<int> likes = null, IList<int> hates = null, int max = 3, bool unlimited = false)
        {
            var result = new List<DishCandidate>();
            var perfT0 = Perf.Start();

            try
            {
                var beverages = DataBaseCore.Beverages;
                if (beverages == null)
                {
                    Perf.End("采集·找酒", perfT0);
                    return result;
                }

                // 同样两段式：先按标签筛，再由入围的查库存。
                // id 表和标签都走缓存，这里不再逐条穿 interop 枚举字典。
                var ids = BevIds();
                for (var i = 0; i < ids.Count; i++)
                {
                    var id = ids[i];
                    var meta = BevMetaOf(id);
                    if (meta == null) continue;

                    var hit = exactDish ? id == request : (unlimited || HasTag(meta.Tags, request));
                    if (!hit) continue;

                    CountTags(meta.Tags, likes, hates, out var likeHits, out var hateHits, out var hitTags);

                    result.Add(new DishCandidate
                    {
                        Name = meta.Name,
                        Cooker = "",
                        Sellable = meta.Item,
                        SellableId = id,
                        BaseScore = TagScore(likeHits, hateHits),
                        LikeHits = likeHits,
                        HateHits = hateHits,
                        Points = likeHits - hateHits,
                        HitTags = hitTags,
                        Price = meta.Value,
                    });
                }

                result.Sort((a, b) => b.BaseScore.CompareTo(a.BaseScore));
                var keep = PoolSize(max);
                if (result.Count > keep) result.RemoveRange(keep, result.Count - keep);

                var stock = RunTimeStorage.Beverages;
                for (var i = 0; i < result.Count; i++)
                {
                    var candidate = result[i];
                    var have = 0;
                    if (stock != null)
                    {
                        try { have = stock.TryGetValue(candidate.SellableId, out var n) ? n : 0; }
                        catch { have = 0; }
                    }

                    candidate.CanCook = have > 0;
                    candidate.Missing = have > 0 ? "" : "缺货";
                    // 酒的「利润」= 售价（酒没有食材成本，做/上酒都只是消耗一瓶库存）；
                    // 「余量」= 现在还有几瓶。配酒时先看利润、再看数量（v1.6.0 需求）。
                    var bevMeta = BevMetaOf(candidate.SellableId);
                    candidate.Profit = bevMeta != null ? bevMeta.Value : 0;
                    candidate.MinRemain = have;
                    candidate.Score = ScoreOf(candidate.BaseScore, candidate.CanCook);
                    candidate.ScoreNote = $"like{candidate.LikeHits} hate{candidate.HateHits} avail{(candidate.CanCook ? 1 : 0)}";
                }

                result.Sort((a, b) =>
                {
                    var byScore = b.Score.CompareTo(a.Score);
                    return byScore != 0 ? byScore : b.CanCook.CompareTo(a.CanCook);
                });
                if (result.Count > max) result.RemoveRange(max, result.Count - max);
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 匹配酒水失败: " + e.Message);
            }

            Perf.End("采集·找酒", perfT0);
            return result;
        }

        /// <summary>标签分：客人每喜欢一个 TAG +20，每厌恶一个 -50，基数 40。</summary>
        private static int TagScore(int likeHits, int hateHits)
        {
            var score = 40 + likeHits * 20 - hateHits * 50;
            return Math.Max(0, Math.Min(100, score));
        }

        /// <summary>排序指数：标签分外加「现在能做/有货」的 10 分。</summary>
        private static int ScoreOf(int baseScore, bool available)
        {
            return Math.Max(0, Math.Min(100, baseScore + (available ? 10 : 0)));
        }

        /// <summary>数某个物品命中客人喜好/厌恶标签的情况（纯 C#，标签数组来自缓存）。</summary>
        private static void CountTags(
            int[] tags, IList<int> likes, IList<int> hates, out int likeHits, out int hateHits, out List<int> hitTags)
        {
            likeHits = 0;
            hateHits = 0;
            hitTags = new List<int>();

            if (likes != null)
            {
                foreach (var tag in likes)
                {
                    if (!HasTag(tags, tag)) continue;
                    likeHits++;
                    hitTags.Add(tag);
                }
            }

            if (hates != null)
            {
                foreach (var tag in hates)
                    if (HasTag(tags, tag)) hateHits++;
            }
        }

        /// <summary>第一段之后保留多少条进第二段（够覆盖展示，又不至于把整个表都算一遍）。</summary>
        private static int PoolSize(int max)
        {
            var keep = max * 6;
            if (keep < 24) keep = 24;
            if (keep > 120) keep = 120;
            return keep;
        }

        internal static bool BeveragesContain(int id)
        {
            try { return DataBaseCore.Beverages.ContainsKey(id); }
            catch { return false; }
        }

        private static bool CanCook(int[] ingredients, out string missing)
        {
            missing = "";

            try
            {
                if (ingredients == null) return false;

                var need = new Dictionary<int, int>();
                for (var i = 0; i < ingredients.Length; i++)
                {
                    var id = ingredients[i];
                    need[id] = need.TryGetValue(id, out var n) ? n + 1 : 1;
                }

                var stock = RunTimeStorage.Ingredients;
                if (stock == null)
                {
                    missing = "库存未知";
                    return false;
                }

                // 把缺的食材凑成一行，用名字而不是编号，玩家一眼能看出缺什么。
                var missingParts = new List<string>();
                foreach (var pair in need)
                {
                    var have = stock.TryGetValue(pair.Key, out var count) ? count : 0;
                    if (have >= pair.Value) continue;

                    missingParts.Add($"{IngredientName(pair.Key)}({have}/{pair.Value})");
                }

                if (missingParts.Count == 0) return true;

                missing = "缺食材：" + string.Join("、", missingParts.ToArray());
                return false;
            }
            catch (Exception e)
            {
                missing = "库存读取失败";
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 判定可做性失败: " + e.Message);
                return false;
            }
        }

        private static string CookerName(string type) => type switch
        {
            "Pot" => "煮锅",
            "Grill" => "烤架",
            "Fryer" => "油锅",
            "Steamer" => "蒸锅",
            "CuttingBoard" => "料理台",
            "Empty" => "无",
            _ => type,
        };

        /// <summary>食材名字；查不到就退回编号。缺料提示用。</summary>
        internal static string IngredientName(int id)
        {
            if (IngredientNameCache.TryGetValue(id, out var cached)) return cached;

            try
            {
                var ingredient = DataBaseCore.RefIngredient(id);
                var name = ingredient?.Text?.Name;
                if (!string.IsNullOrEmpty(name))
                {
                    IngredientNameCache[id] = name;
                    return name;
                }
            }
            catch
            {
                // 落到编号
            }

            return "#" + id;
        }

        private static string SafeName(Sellable food, int id)
        {
            try
            {
                var text = food.Text;
                if (text != null && !string.IsNullOrEmpty(text.Name)) return text.Name;
            }
            catch
            {
                // 落到编号
            }

            return "菜品 #" + id;
        }
    }
}
