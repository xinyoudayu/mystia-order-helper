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

        /// <summary>酒水来源（FindBeverages 时填充）。</summary>
        public Sellable Sellable;

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
    }

    /// <summary>
    /// 把「需求」翻译成「现在能做的菜」。
    /// 需求有两种可能：某个菜品 ID（精确），或某个标签（匹配菜品标签集合）。
    /// </summary>
    internal static class RecipeIndex
    {
        internal static List<DishCandidate> Find(int request, bool exactDish, IList<int> likes = null, IList<int> hates = null, int max = 3, bool unlimited = false)
        {
            var result = new List<DishCandidate>();

            try
            {
                var unlocked = RunTimeStorage.Recipes;
                if (unlocked == null) return result;

                for (var i = 0; i < unlocked.Count; i++)
                {
                    var recipeId = unlocked[i];
                    if (!DataBaseCore.Recipes.TryGetValue(recipeId, out var recipe) || recipe == null) continue;
                    if (!DataBaseCore.Foods.TryGetValue(recipe.FoodID, out var food) || food == null) continue;

                    var hit = exactDish ? recipe.FoodID == request : (unlimited || HasTag(food, request));
                    if (!hit) continue;

                    var canCook = CanCook(recipe, out var missing);
                    var score = ScoreOf(food, canCook, likes, hates, out var note, out var likeHits, out var hateHits, out var hitTags);

                    result.Add(new DishCandidate
                    {
                        Name = SafeName(food, recipe.FoodID),
                        Cooker = CookerName(recipe.CookerType.ToString()),
                        CanCook = canCook,
                        Missing = missing,
                        Recipe = recipe,
                        Score = score,
                        ScoreNote = note,
                        LikeHits = likeHits,
                        HateHits = hateHits,
                        Points = likeHits - hateHits,
                        HitTags = hitTags,
                    });
                }
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 匹配食谱失败: " + e.Message);
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
                    if (!seen.Add(recipe.FoodID)) continue;
                    if (!DataBaseCore.Foods.TryGetValue(recipe.FoodID, out var food) || food == null) continue;

                    var tagHit = requestTag < 0 || HasTag(food, requestTag);
                    var canCook = CanCook(recipe, out var missing);
                    var score = ScoreOf(food, canCook, likes, hates, out var note, out var likeHits, out var hateHits, out var hitTags);

                    result.Add(new DishCandidate
                    {
                        Name = SafeName(food, recipe.FoodID) + (tagHit ? "" : "（点单标签不符）"),
                        RequestHit = tagHit,
                        Cooker = CookerName(recipe.CookerType.ToString()),
                        CanCook = canCook,
                        Missing = missing,
                        Recipe = recipe,
                        Score = score - (tagHit ? 0 : 40),
                        ScoreNote = note,
                        LikeHits = likeHits,
                        HateHits = hateHits,
                        Points = likeHits - hateHits,
                        HitTags = hitTags,
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

            try
            {
                var beverages = DataBaseCore.Beverages;
                if (beverages == null) return result;

                var stock = RunTimeStorage.Beverages;

                var enumerator = beverages.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    var pair = enumerator.Current;
                    var beverage = pair.Value;
                    if (beverage == null) continue;

                    var hit = exactDish ? pair.Key == request : (unlimited || HasTag(beverage, request));
                    if (!hit) continue;

                    var have = 0;
                    if (stock != null) have = stock.TryGetValue(pair.Key, out var n) ? n : 0;

                    var score = ScoreOf(beverage, have > 0, likes, hates, out var note, out var likeHits, out var hateHits, out var hitTags);

                    result.Add(new DishCandidate
                    {
                        Name = SafeName(beverage, pair.Key),
                        Cooker = "",
                        CanCook = have > 0,
                        Missing = have > 0 ? "" : "缺货",
                        Sellable = beverage,
                        Score = score,
                        ScoreNote = note,
                        LikeHits = likeHits,
                        HateHits = hateHits,
                        Points = likeHits - hateHits,
                        HitTags = hitTags,
                    });
                }
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 匹配酒水失败: " + e.Message);
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

        /// <summary>
        /// 按游戏评价规则打分：客人每喜欢该菜的一个 TAG 得 1 分，命中厌恶 TAG 扣 1 分。
        /// 点单需求本身也是喜好的一个 TAG，所以命中需求已含在 LikeHits 里。
        /// 指数只是排序用的粗刻度，真正决定是否粉评的是 Points 合计（菜 + 酒水 ≥ 4）。
        /// </summary>
        private static int ScoreOf(
            Sellable item, bool available, IList<int> likes, IList<int> hates, out string note,
            out int likeHits, out int hateHits, out List<int> hitTags)
        {
            likeHits = 0;
            hateHits = 0;
            hitTags = new List<int>();

            if (likes != null)
            {
                foreach (var tag in likes)
                {
                    if (!HasTag(item, tag)) continue;
                    likeHits++;
                    hitTags.Add(tag);
                }
            }

            if (hates != null)
            {
                foreach (var tag in hates)
                    if (HasTag(item, tag)) hateHits++;
            }

            var score = 40 + likeHits * 20 - hateHits * 50;
            if (available) score += 10;
            score = Math.Max(0, Math.Min(100, score));

            note = $"like{likeHits} hate{hateHits} avail{(available ? 1 : 0)}";
            return score;
        }

        internal static bool BeveragesContain(int id)
        {
            try { return DataBaseCore.Beverages.ContainsKey(id); }
            catch { return false; }
        }

        private static bool HasTag(Sellable food, int tag)
        {
            try
            {
                var tags = food.Tags;
                if (tags == null) return false;
                for (var i = 0; i < tags.Length; i++)
                    if (tags[i] == tag) return true;
            }
            catch
            {
                // 忽略：视为不匹配
            }
            return false;
        }

        private static bool CanCook(Recipe recipe, out string missing)
        {
            missing = "";

            try
            {
                var ingredients = recipe.Ingredients;
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
            try
            {
                var ingredient = DataBaseCore.RefIngredient(id);
                var name = ingredient?.Text?.Name;
                if (!string.IsNullOrEmpty(name)) return name;
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
