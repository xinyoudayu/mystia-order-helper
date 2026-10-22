using System;
using System.Collections.Generic;

using GameData.Core.Collections;
using GameData.Core.Collections.NightSceneUtility;

using Il2CppInterop.Runtime.InteropTypes.Arrays;

using NightScene.GuestManagementUtility;

namespace MystiaOrderHelper
{
    /// <summary>一位在场客人在面板上的展示数据。</summary>
    internal sealed class GuestView
    {
        public string Name = "?";
        public string Kind = "?";

        /// <summary>稀客（点的是标签）还是常客（点的是具体菜）。</summary>
        public bool IsSpecial;
        public int Desk = -1;

        /// <summary>客人还剩几单没上（GuestGroupController.RemainOrderCount）。</summary>
        public int RemainOrders = -1;

        /// <summary>本次到店一共点过几单（AllOrdersCount）。</summary>
        public int AllOrders = -1;
        public int CtrlDesk = -1;
        public int OrderDesk = -1;
        public string Food = "";
        public string Beverage = "";
        public bool Ordering;

        /// <summary>当前这一单已经上过菜、正在等下一单（此时不该再出现在推荐框里）。</summary>
        public bool Served;

        public int RawFoodRequest = -1;

        /// <summary>点菜要求对应的标签 ID（-1 表示未知/精确菜品）。</summary>
        public int FoodTagRequest = -1;
        public int BevTagRequest = -1;
        public List<DishCandidate> Candidates = new();
        public List<DishCandidate> BeverageCandidates = new();

        /// <summary>客人的喜好/厌恶标签（稀客才有厌恶，常客只有喜好）。</summary>
        public List<int> LikeFoodTags = new();
        public List<int> HateFoodTags = new();
        public List<int> LikeBevTags = new();
        public List<int> HateBevTags = new();

        /// <summary>「一道菜 + 一杯酒」的搭配结果，按预计评价从高到低。</summary>
        public List<DishPair> Pairs = new();

        /// <summary>凑不出粉评时，命中标签最多的那道菜（仅稀客用）。</summary>
        public DishCandidate BestDish;

        /// <summary>凑不出粉评时，命中标签最多的那杯酒（仅稀客用）。</summary>
        public DishCandidate BestDrink;
    }

    /// <summary>
    /// 一菜一酒的搭配：游戏评价是「菜 + 酒」合计出来的，所以配对展示比分开列更有用。
    /// </summary>
    internal sealed class DishPair
    {
        public DishCandidate Dish;
        public DishCandidate Drink;

        /// <summary>菜与酒的命中分相加（粉评门槛 4 分）。</summary>
        public int TotalPoints;

        /// <summary>预计评价：粉评/橙评/绿评/紫评/黑评。</summary>
        public string Rating = "";

        /// <summary>离粉评还差几分（已粉评时为 0）。</summary>
        public int Gap => TotalPoints >= 4 ? 0 : 4 - TotalPoints;

        /// <summary>菜和酒都拿得出来（可做 + 有货）。</summary>
        public bool Ready => Dish != null && Dish.CanCook && Drink != null && Drink.CanCook;
    }

    internal static class GuestCollector
    {
        /// <summary>已离开的客人控制器（按 IL2CPP 对象指针记，避免"走完动画还挂在列表里"）。</summary>
        private static readonly HashSet<IntPtr> Left = new();

        internal static void MarkLeft(GuestGroupController controller)
        {
            try
            {
                if (controller == null) return;
                if (Left.Count > 300) Left.Clear();
                Left.Add(controller.Pointer);
            }
            catch { }
        }

        internal static List<GuestView> Collect(bool showNormalGuests)
        {
            var views = new List<GuestView>();
            SpecialGuest sample = null;

            try
            {
                var manager = GuestsManager.Instance;
                if (manager == null) return views;

                var guests = manager.AllPresentedGuestGroupController;
                if (guests == null) return views;

                var enumerator = guests.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    var controller = enumerator.Current;
                    if (controller == null) continue;

                    var isSpecial = controller.TryCast<SpecialGuestsController>() != null;
                    if (!isSpecial && !showNormalGuests) continue;

                    // 客人已经离店（走完离场动画/已离席）就不再进推荐框。
                    try
                    {
                        if (!controller.HaveNotLeft()) continue;
                        if (Left.Contains(controller.Pointer)) continue;
                    }
                    catch { }

                    var view = ToView(controller);
                    if (view != null) views.Add(view);

                    if (sample == null && isSpecial)
                    {
                        try { sample = controller.TryCast<SpecialGuestsController>().SpecialGuest; }
                        catch { }
                    }
                }
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 采集客人失败: " + e.Message);
            }

                views.RemoveAll(v => v.Food == "（已离席）");

        PopularInfo.Refresh(sample);

            return views;
        }

        private static GuestView ToView(GuestGroupController controller)
        {
            var view = new GuestView();

            try { view.Name = controller.OnGetGuestName(); }
            catch { view.Name = "<取名失败>"; }

            var special = controller.TryCast<SpecialGuestsController>();
            view.Kind = special != null ? "稀客" : "常客";
            view.IsSpecial = special != null;
            try { view.CtrlDesk = controller.DeskCode; } catch { view.CtrlDesk = -1; }
            try
            {
                view.RemainOrders = controller.RemainOrderCount;
                view.AllOrders = controller.AllOrdersCount;
            }
            catch { view.RemainOrders = -1; view.AllOrders = -1; }

            GuestsManager.OrderBase order = null;
            try { order = controller.PeekOrders(); }
            catch (Exception) { view.Food = "<读取点单失败>"; }

            if (order == null)
            {
                view.Food = "（尚未点单）";
                view.Desk = view.CtrlDesk;
                return view;
            }

            view.Desk = order.DeskCode;
            view.OrderDesk = order.DeskCode;
            // 所有单都做完、且没有下一单 → 客人已经吃完了，不再挂在推荐框里。
            if (view.RemainOrders == 0 && view.AllOrders > 0)
            {
                view.Food = "（已离席）";
                view.Beverage = "";
                return view;
            }
            try { view.CtrlDesk = controller.DeskCode; } catch { view.CtrlDesk = -1; }
            try
            {
                view.RemainOrders = controller.RemainOrderCount;
                view.AllOrders = controller.AllOrdersCount;
            }
            catch { view.RemainOrders = -1; view.AllOrders = -1; }
            view.Ordering = !order.IsFullfilled;
            view.RawFoodRequest = order.foodRequest;

            // 已经上过菜的单不再推荐，等客人下一次点单。
            if (order.IsFullfilled)
            {
                view.Served = true;
                view.Food = "（已上菜，等下一单）";
                view.Beverage = "";
                return view;
            }

            if (special != null)
            {
                var specialOrder = order.TryCast<GuestsManager.SpecialOrder>();
                if (specialOrder != null)
                {
                    var tag = specialOrder.RequestFoodTag;
                    var bevTag = specialOrder.RequestBeverageTag;
                    view.RawFoodRequest = tag;
                    view.FoodTagRequest = tag;
                    view.BevTagRequest = bevTag;

                    var data = special.SpecialGuest;

                    ReadPreference(data, view);
                    ReadEvaluatedTags(controller, view);

                    // 负数不是「某个标签」，而是「这张单没这一项」：
                    // 实测（BepInEx 日志）food=-1 时 order.DeskCode 也是 -1，说明客人还没点单；
                    // 已经点单的客人 food 一定是正数，beverage=-1 表示「酒水随意」。
                    var ordered = tag >= 0 || bevTag >= 0;
                    if (!ordered)
                    {
                        view.Food = "（尚未点单）";
                        view.Beverage = "";
                        return view;
                    }

                    var tagText = TagText(data, tag, true);
                    var bevText = TagText(data, bevTag, false);

                    string line;
                    try { line = special.GetOrderFoodText(specialOrder); }
                    catch { line = ""; }

                    view.Food = tag >= 0
                        ? MergeRequirement(tagText, line, tag)
                        : (string.IsNullOrEmpty(line) ? "（这道单不点菜）" : line);

                    view.Beverage = !string.IsNullOrEmpty(bevText)
                        ? bevText
                        : (bevTag >= 0 ? $"酒水标签 {bevTag}" : "酒水：不限（按喜好推荐）");

                    view.Candidates = tag >= 0
                        ? RecipeIndex.Find(tag, false, view.LikeFoodTags, view.HateFoodTags, 4)
                        : new List<DishCandidate>();

                    view.BeverageCandidates = RecipeIndex.FindBeverages(
                        bevTag, false, view.LikeBevTags, view.HateBevTags, 4, bevTag < 0);

                    ApplyRatings(view);
                    return view;
                }
            }

            view.Food = DescribeRequest(order.foodRequest, "菜品");
            view.Beverage = DescribeRequest(order.beverageRequest, "酒水");

            // 普通客人的 foodRequest 可能是菜品 ID，也可能是标签；先按能否查到菜品判断。
            // 普通客人也有喜好 TAG，一并参与打分（命中喜好照样加分）。
            ReadPreference(controller, view);
            ReadEvaluatedTags(controller, view);

            var exact = RecipeIndex.FoodsContain(order.foodRequest);
            if (!exact && order.foodRequest >= 0) view.FoodTagRequest = order.foodRequest;
            view.Candidates = RecipeIndex.Find(order.foodRequest, exact, view.LikeFoodTags, view.HateFoodTags);

            if (order.beverageRequest >= 0)
            {
                var bevExact = RecipeIndex.BeveragesContain(order.beverageRequest);
                view.BeverageCandidates = RecipeIndex.FindBeverages(
                    order.beverageRequest, bevExact, view.LikeBevTags, view.HateBevTags);
            }

            ApplyRatings(view);
            return view;
        }

        /// <summary>
        /// 游戏里「标签文案」和「点单台词」经常是同一句，合并时去重。
        /// </summary>
        private static string MergeRequirement(string tagText, string line, int tag)
        {
            if (string.IsNullOrEmpty(tagText) && string.IsNullOrEmpty(line))
                return $"标签 {tag}";

            if (string.IsNullOrEmpty(tagText)) return line;
            if (string.IsNullOrEmpty(line) || line == tagText) return tagText;
            if (line.Contains(tagText)) return line;
            if (tagText.Contains(line)) return tagText;

            return $"{tagText}（{line}）";
        }

        private static string TagText(GameData.Core.Collections.NightSceneUtility.SpecialGuest data, int tag, bool food)
        {
            if (data == null || tag < 0) return "";
            try { return food ? data.GetFoodTagText(tag) : data.GetBevTagText(tag); }
            catch { return ""; }
        }

        /// <summary>读出稀客的喜好/厌恶标签，供推荐指数使用。</summary>
        /// <summary>
        /// 游戏真正拿来打分的标签集合（会包含流行风向等外部加成），比原始喜好更准。
        /// </summary>
        private static void ReadEvaluatedTags(GuestGroupController controller, GuestView view)
        {
            try
            {
                var like = ToList(controller.EvaluateLikeFoodTags);
                if (like.Count > 0) view.LikeFoodTags = like;
            }
            catch { }

            try
            {
                var hate = ToList(controller.EvaluateHateFoodTags);
                if (hate.Count > 0) view.HateFoodTags = hate;
            }
            catch { }
        }

        /// <summary>
        /// 按游戏评价折算预计评价。
        /// 规则：客人每喜欢一个 TAG +1 分，命中厌恶 TAG -1 分；菜与酒水的分数相加。
        /// 粉评 = 菜与酒水都命中点单要求，且合计 ≥ 4 分；合计 3 分封顶橙评。
        /// </summary>
        private static void ApplyRatings(GuestView view)
        {
            try
            {
                ApplyRatingsToCandidates(view.Candidates, view.BeverageCandidates);
                BuildPairs(view);
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 折算评价失败: " + e.Message);
            }
        }

        /// <summary>
        /// 给「菜 + 酒」算游戏评价：每项合计 = 自身命中数 + 另一方最高命中数；
        /// 粉评要求菜酒成对且合计 ≥ 4。写完分数后按合计分重排。
        /// 特殊厨具（三位一体）临时补算推荐时也走这里，保证两边显示规则一致。
        /// </summary>
        internal static void ApplyRatingsToCandidates(List<DishCandidate> dishes, List<DishCandidate> drinks)
        {
            var bestDish = 0;
            if (dishes != null)
            {
                foreach (var dish in dishes)
                    if (dish.Points > bestDish) bestDish = dish.Points;
            }

            var bestBev = 0;
            if (drinks != null)
            {
                foreach (var bev in drinks)
                    if (bev.Points > bestBev) bestBev = bev.Points;
            }

            var hasDish = dishes != null && dishes.Count > 0;
            var hasBev = drinks != null && drinks.Count > 0;

            if (dishes != null)
            {
                foreach (var dish in dishes)
                {
                    dish.BaseScore = dish.Score;
                    Rate(dish, dish.Points + (hasBev ? bestBev : 0), hasBev);
                }
            }

            if (drinks != null)
            {
                foreach (var bev in drinks)
                {
                    bev.BaseScore = bev.Score;
                    Rate(bev, bev.Points + (hasDish ? bestDish : 0), hasDish);
                }
            }

            // 评分改写后必须重排，否则列表顺序还停在粗评分上，
            // 会出现「下面一条显示粉评、上面一条显示绿评」的自相矛盾。
            SortByRating(dishes);
            SortByRating(drinks);
        }

        /// <summary>命中喜好标签最多的那一个；同数量时优先可做、再按合计分。</summary>
        internal static DishCandidate BestHit(List<DishCandidate> list)
        {
            if (list == null || list.Count == 0) return null;

            DishCandidate best = null;
            foreach (var item in list)
            {
                if (item == null) continue;
                if (best == null) { best = item; continue; }
                if (item.LikeHits > best.LikeHits) { best = item; continue; }
                if (item.LikeHits < best.LikeHits) continue;
                if (item.CanCook && !best.CanCook) { best = item; continue; }
                if (item.CanCook != best.CanCook) continue;
                if (item.TotalPoints > best.TotalPoints) best = item;
            }

            return best;
        }

        /// <summary>按合计分折算评价名（粉评要求菜酒成对）。</summary>
        internal static string RatingText(int total, bool paired)
        {
            if (total >= 4 && paired) return "粉评";
            if (total >= 3) return "橙评";
            if (total == 2) return "绿评";
            if (total == 1) return "紫评";
            return "黑评";
        }

        /// <summary>
        /// 给一位客人算出「菜 + 酒」的最佳搭配（每道菜配它最合适的那杯酒），按合计分排序。
        /// 主面板直接展示这个，避免把菜和酒分开列、玩家还得自己心算能不能粉评。
        /// </summary>
        internal static void BuildPairs(GuestView view, int max = 3)
        {
            view.Pairs = new List<DishPair>();

            try
            {
                var dishes = view.Candidates;
                if (dishes == null || dishes.Count == 0) return;

                foreach (var dish in dishes)
                {
                    if (dish == null) continue;

                    DishCandidate best = null;
                    if (view.BeverageCandidates != null)
                    {
                        foreach (var drink in view.BeverageCandidates)
                        {
                            if (drink == null) continue;
                            if (best == null) { best = drink; continue; }
                            if (drink.Points > best.Points) { best = drink; continue; }
                            if (drink.Points == best.Points && drink.CanCook && !best.CanCook) best = drink;
                        }
                    }

                    var total = dish.Points + (best != null ? best.Points : 0);

                    // 稀客到不了粉评的组合就不推荐；常客点的是固定菜，照常列出来。
                    if (view.IsSpecial && total < 4) continue;

                    view.Pairs.Add(new DishPair
                    {
                        Dish = dish,
                        Drink = best,
                        TotalPoints = total,
                        Rating = RatingText(total, best != null),
                    });
                }

                view.Pairs.Sort((a, b) =>
                {
                    var byTotal = b.TotalPoints.CompareTo(a.TotalPoints);
                    if (byTotal != 0) return byTotal;

                    var byReady = b.Ready.CompareTo(a.Ready);
                    if (byReady != 0) return byReady;

                    return b.Dish.BaseScore.CompareTo(a.Dish.BaseScore);
                });

                if (view.Pairs.Count > max)
                    view.Pairs.RemoveRange(max, view.Pairs.Count - max);

                // 稀客凑不出粉评时，退回「命中标签最多」的菜和酒，各给一条。
                if (view.IsSpecial && view.Pairs.Count == 0)
                {
                    view.BestDish = BestHit(dishes);
                    view.BestDrink = BestHit(view.BeverageCandidates);
                }
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 搭配推荐失败: " + e.Message);
                view.Pairs = new List<DishPair>();
            }
        }

        /// <summary>按游戏评价合计分排序：分高的在前，同分优先能做、再按粗评分。</summary>
        private static void SortByRating(List<DishCandidate> list)
        {
            if (list == null || list.Count < 2) return;

            list.Sort((a, b) =>
            {
                var byTotal = b.TotalPoints.CompareTo(a.TotalPoints);
                if (byTotal != 0) return byTotal;

                var byCook = b.CanCook.CompareTo(a.CanCook);
                if (byCook != 0) return byCook;

                return b.BaseScore.CompareTo(a.BaseScore);
            });
        }

        private static void Rate(DishCandidate candidate, int total, bool paired)
        {
            candidate.TotalPoints = total;
            candidate.Rating = RatingText(total, paired);

            if (total >= 4 && paired) { candidate.Score = 95; return; }
            if (total >= 3) { candidate.Score = 80; return; }

            switch (total)
            {
                case 2: candidate.Score = 60; break;
                case 1: candidate.Score = 40; break;
                default: candidate.Score = 10; break;
            }
        }

        /// <summary>普通客人的喜好标签同样参与评价折算。</summary>
        private static void ReadPreference(GuestGroupController controller, GuestView view)
        {
            try { view.LikeFoodTags = ToList(controller.LikeFoodTags); } catch { }
            try { view.HateFoodTags = ToList(controller.HateFoodTags); } catch { }
            try { view.LikeBevTags = ToList(controller.LikeBevTags); } catch { }
            try { view.HateBevTags = ToList(controller.HateBevTags); } catch { }
        }
        private static void ReadPreference(GameData.Core.Collections.NightSceneUtility.SpecialGuest data, GuestView view)
        {
            if (data == null) return;

            try { view.LikeFoodTags = ToList(data.LikeFoodTag); }
            catch (Exception e) { OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 读取喜好菜标签失败: " + e.Message); }

            try { view.HateFoodTags = ToList(data.HateFoodTag); }
            catch (Exception e) { OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 读取厌恶菜标签失败: " + e.Message); }

            try { view.LikeBevTags = ToList(data.LikeBevTag); }
            catch (Exception e) { OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 读取喜好酒标签失败: " + e.Message); }

            try { view.HateBevTags = ToList(data.HateBevTag); }
            catch (Exception e) { OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 读取厌恶酒标签失败: " + e.Message); }
        }

        private static List<int> ToList(Il2CppStructArray<int> tags)
        {
            var list = new List<int>();
            if (tags == null) return list;
            for (var i = 0; i < tags.Length; i++) list.Add(tags[i]);
            return list;
        }

        private static List<int> ToList(Il2CppSystem.Collections.Generic.IEnumerable<int> tags)
        {
            var list = new List<int>();
            if (tags == null) return list;

            // Il2Cpp 的 IEnumerable 不是 BCL 接口：强转 System.Collections.IEnumerable 会抛
            // 「Unable to cast object of type 'Il2CppSystem.Collections.Generic.IEnumerable`1[System.Int32]'」，
            // 所以必须用 Il2Cpp 自己的枚举器，否则厌恶标签永远读不到、还每 0.5 秒刷一次警告。
            try
            {
                // 这类属性底层多半就是 List<int> 或 int[]，能直接按下标读，省掉枚举器开销。
                var asList = tags.TryCast<Il2CppSystem.Collections.Generic.List<int>>();
                if (asList != null)
                {
                    for (var i = 0; i < asList.Count; i++) list.Add(asList[i]);
                    return list;
                }

                var asArray = tags.TryCast<Il2CppStructArray<int>>();
                if (asArray != null)
                {
                    for (var i = 0; i < asArray.Length; i++) list.Add(asArray[i]);
                    return list;
                }

                // Il2Cpp 泛型枚举器只暴露 Current，MoveNext 在非泛型接口上。
                var enumerator = tags.GetEnumerator();
                if (enumerator != null)
                {
                    var walker = enumerator.Cast<Il2CppSystem.Collections.IEnumerator>();
                    while (walker.MoveNext()) list.Add(enumerator.Current);
                }
            }
            catch (Exception e)
            {
                if (!_enumWarned)
                {
                    _enumWarned = true;
                    OrderHelperPlugin.LogSource.LogWarning(
                        "[OrderHelper] 读取标签集合失败（后续同类错误不再重复记录）: " + e.Message);
                }
            }

            return list;
        }

        private static bool _enumWarned;

        private static string DescribeRequest(int id, string label)
        {
            if (id < 0) return "";

            try
            {
                if (DataBaseCore.Foods.TryGetValue(id, out var food) && food != null)
                {
                    var name = food.Text?.Name;
                    if (!string.IsNullOrEmpty(name)) return name;
                }
            }
            catch
            {
                // 落到下面的原始编号
            }

            // 点酒时 beverageRequest 是酒水 ID，不在 Foods 里；不查 Beverages 就只会显示「酒水 #123」。
            try
            {
                if (DataBaseCore.Beverages.TryGetValue(id, out var beverage) && beverage != null)
                {
                    var name = beverage.Text?.Name;
                    if (!string.IsNullOrEmpty(name)) return name;
                }
            }
            catch
            {
                // 落到下面的原始编号
            }

            return $"{label} #{id}";
        }
    }
}


