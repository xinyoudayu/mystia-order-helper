using System;
using System.Collections.Generic;

using GameData.Core.Collections;
using GameData.Core.Collections.NightSceneUtility;

using Il2CppInterop.Runtime.InteropTypes.Arrays;

using NightScene.GuestManagementUtility;

using UnityEngine;

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

        // ── 预算（v1.6.1）────────────────────────────────────────────────────
        // 客人身上带多少钱决定「这一单最多能点多贵的菜+酒」。超了付不起，游戏会给黑评/生气。
        // 三个字段都用 0 表示「读不到」，读不到就不拦（宁可像以前那样推荐，也不要因为
        // 一个字段读失败就把所有酒都判成超预算）。
        /// <summary>客人当前还剩多少钱（GuestGroupController.GetFund）。0 = 读不到。</summary>
        public int Fund;

        /// <summary>这次最多带多少钱（MaxFundCarry），只为日志/展示，不参与筛选。0 = 读不到。</summary>
        public int FundMax;

        /// <summary>
        /// 本次到店的总预算（同一客人观测到的最大资金）。**只用来算最后一单的余量**，
        /// 不做均分。0 = 不知道（那就按剩余钱当上限，不放余量）。
        /// </summary>
        public int FundBase;

        /// <summary>这一单最多花多少（由 <see cref="CapOf"/> 算出并缓存在这里）。0 = 不限。</summary>
        public int BudgetCap;

        /// <summary>是否按预算筛选酒水（配置开着 + 真的读到了钱）。</summary>
        public bool FundKnown;
        public int CtrlDesk = -1;
        public int OrderDesk = -1;
        public string Food = "";
        public string Beverage = "";
        public bool Ordering;

        /// <summary>当前这一单已经上过菜、正在等下一单（此时不该再出现在推荐框里）。</summary>
        public bool Served;

        /// <summary>人已坐下但还没点单（此时列表状态不能写成「在等菜」）。</summary>
        public bool NotOrderedYet;

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

        /// <summary>酒水推荐框最终要显示的那几杯（收集阶段算好，OnGUI 直接读）。</summary>
        public List<DishCandidate> HintDrinks = new();

        // ---- 展示文本（收集阶段生成一次）----
        // ★ OnGUI 每帧跑两遍（Layout + Repaint），现场用字符串插值等于每帧给 GC 送几千个临时字符串。
        //   客人数据 0.5 秒才变一次，所以文本跟着客人数据一起生成，OnGUI 只负责贴出来。
        public string MainTitleLine = "";
        public string MainFoodLine = "";
        public string MainBeverageLine = "";
        public string HintTitleLine = "";
        public string NoDrinkLine = "";

        /// <summary>只有状态和剩单数（详情区用，避免跟标题行的「类型」重复）。</summary>
        public string StatusLine = "";

        /// <summary>头像位衬托：批3 换成真头像之前，先用一个汉字顶着。</summary>
        public string Initial = "?";

        /// <summary>批3：稀客立绘贴图（常客取不到，为 null）。收集阶段查一次，之后不再动。</summary>
        public Texture2D Portrait;

        /// <summary>
        /// 批3：贴图的 GUIContent 包装。收集阶段建一次——OnGUI 里 new GUIContent 等于每帧给 GC 送垃圾。
        /// </summary>
        public GUIContent PortraitContent;

        internal void BuildLines()
        {
            var desk = Desk >= 0 ? Desk : CtrlDesk;
            var deskText = desk >= 0 ? (desk + 1).ToString() : "—";

            MainTitleLine = $"◆ {Name}（{Kind}）  桌位 {deskText}";
            MainFoodLine = "    点菜：" + Food;
            MainBeverageLine = string.IsNullOrEmpty(Beverage) ? "" : "    点酒：" + Beverage;

            // 状态：剩下的单数是「还差几单没上」，这一单上完了但人还在就是「已上菜」。
            // ★ 不能拿 Food 是否为空来判断「没点单」：没点单时 Food 是占位文案「（尚未点单）」，
            //   非空，列表就会误报成「在等菜」（实测截图里就出现过自相矛盾）。用显式标记。
            var state = Served ? "已上菜，等下一单"
                : NotOrderedYet ? "还没点单"
                : RemainOrders > 0 ? "在等菜"
                : "已点单";
            var remainList = RemainOrders > 0 ? "  剩" + RemainOrders + "单" : "";

            // 预算（v1.6.1）：钱读得到才写，读不到就别占位置。
            var fundText = FundKnown ? $"  预算余{Fund}" : "";

            // 刷好感（v1.6.3）：开着就在状态行标一句，免得玩家忘了现在是「省钱吃满单」的口径。
            var favorText = GuestCollector.FavorOn(this) ? "  刷好感" : "";

            // ★ 顶部头像条之后只展示「一位」客人的详情，所以状态单独成行（标题行里已经有类型了）。
            StatusLine = state + remainList + fundText + favorText;

            Initial = Name.Length > 0 ? Name.Substring(0, 1) : "?";

            var remain = RemainOrders >= 0 ? $"  剩{RemainOrders}单" : "";
            HintTitleLine = $"◆ {Name}（{Kind}）桌{deskText}{remain}" + (FundKnown ? $"  余{Fund}" : "");
            // 名字已经由上面的标题行（HintTitleLine）带出来了，这里只留一句结论，别重复。
            NoDrinkLine = "    没有匹配的酒水";
        }
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

        /// <summary>主面板第一行（收集阶段预生成）。</summary>
        public string Line1 = "";

        /// <summary>主面板第二行（收集阶段预生成）。</summary>
        public string Line2 = "";

        /// <summary>需要加料时才有第三行：加了什么、利润多少、做完还剩多少。空串表示不显示。</summary>
        public string Line3 = "";

        // ── 预算（v1.6.1）────────────────────────────────────────────────────
        /// <summary>这一单的价格 = 菜售价 + 酒售价。0 表示价格读不到。</summary>
        public int Price;

        /// <summary>客人此刻手上还剩多少钱。0 表示读不到。</summary>
        public int Budget;

        /// <summary>这一单的上限 = 剩余钱（还有单时）或 剩余钱 + 最后一单的余量。</summary>
        public int Cap;

        /// <summary>超过了这一单的上限 —— 这才是「可能黑评」的账（v1.6.2）。</summary>
        public bool OverBudget;

        /// <summary>没超上限，但比客人手上剩的钱多：动用了最后一单的余量（正常且可接受）。</summary>
        public bool OverCash;

        /// <summary>预算行（收集阶段预生成）。空串表示不显示。</summary>
        public string Line4 = "";
    }

    internal static class GuestCollector
    {
        private struct LeftMark
        {
            public float At;
            public int AllOrders;
        }

        /// <summary>已离开的客人控制器（按 IL2CPP 对象指针记，避免"走完动画还挂在列表里"）。</summary>
        private static readonly Dictionary<IntPtr, LeftMark> Left = new();

        /// <summary>换场景必须清空：指针会被复用，上一场的标记会把新客人挡掉。</summary>
        internal static void ClearLeft()
        {
            Left.Clear();
            _auditLast = "";

            // 预算的「总预算」是按客人名记的，换场景就是新的一晚，必须清，
            // 否则上一晚的钱包会当成今晚的上限。
            FundBase.Clear();
            BudgetChanges = 0;
            FavorLogs = 0;
        }

        /// <summary>
        /// 推荐口径变了（在界面里切了「刷好感」/「按预算推荐」）→ 立刻作废 view 缓存。
        ///
        /// 不清的话，view 里算好的推荐、预算上限、状态行要等 ViewTtl（2 秒）过期才重算：
        /// 玩家点完开关看着面板一动不动，只会以为按钮坏了。代价只是下一帧重算一次（几毫秒），
        /// 而且只在真的点了开关时发生。
        /// </summary>
        internal static void InvalidateCache()
        {
            ViewCache.Clear();

            // 光清缓存不够：采集是 0.8 秒一轮的，清完还要等下一拍才会重算，
            // 玩家点了开关当场看不到变化。这里顺手把采集节拍一并提前（v1.6.7）。
            OverlayBehaviour.RequestRefresh();
        }

        internal static void MarkLeft(GuestGroupController controller)
        {
            try
            {
                if (controller == null) return;
                if (Left.Count > 300) Left.Clear();

                var all = -1;
                try { all = controller.AllOrdersCount; } catch { }
                Left[controller.Pointer] = new LeftMark { At = Time.realtimeSinceStartup, AllOrders = all };
            }
            catch { }
        }

        /// <summary>
        /// 这个控制器是不是「已经走了」。
        /// 老实现是"打过标记就永久隐藏"。但露易兹的符卡「传奇的格列佛」会把没吃饱的稀客拍下来，
        /// 当天结束后投影成「稀客影像」再次光临——游戏很可能复用同一个控制器，
        /// 于是影像被永久挡住，面板里根本看不到这位客人。
        /// 现在：订单数变了 = 这是新的一次到店，撤销标记；标记超过 10 分钟也自动失效。
        /// </summary>
        private static bool IsLeft(GuestGroupController controller)
        {
            LeftMark mark;
            if (!Left.TryGetValue(controller.Pointer, out mark)) return false;

            if (Time.realtimeSinceStartup - mark.At > 600f)
            {
                Left.Remove(controller.Pointer);
                return false;
            }

            try
            {
                var all = controller.AllOrdersCount;
                if (mark.AllOrders >= 0 && all >= 0 && all != mark.AllOrders)
                {
                    Left.Remove(controller.Pointer);
                    Audit("撤销离店标记（订单数 " + mark.AllOrders + "→" + all + "）" + NameOf(controller));
                    return false;
                }
            }
            catch { }

            return true;
        }

        private static string NameOf(GuestGroupController controller)
        {
            try { return controller.OnGetGuestName() ?? "?"; }
            catch { return "?"; }
        }

        /// <summary>
        /// 控制器的真实类型名。常客 / 稀客 / 符卡影像 是靠类型和状态区分的，
        /// 出问题时这条日志能直接看出游戏给的是哪一种。
        /// </summary>
        private static string TypeOf(GuestGroupController controller)
        {
            try
            {
                var t = controller.GetIl2CppType();
                if (t != null && !string.IsNullOrEmpty(t.Name)) return t.Name;
            }
            catch { }

            try { return controller.GetType().Name; } catch { return "?"; }
        }

        private static string _auditLast = "";

        private static void Audit(string line)
        {
            try { OrderHelperPlugin.LogSource.LogInfo("[OrderHelper] 巡查 " + line); } catch { }
        }

        /// <summary>
        /// 把「场上到底有哪几个控制器、各自是什么类型、为什么没进推荐框」写进日志。
        /// 只在内容变化时写，每 5 秒巡一次。排查「某位客人面板里看不到」时先看这段。
        /// </summary>
        internal static void AuditPass()
        {
            var lines = new List<string>();

            try
            {
                var manager = GuestsManager.Instance;
                var guests = manager?.AllPresentedGuestGroupController;
                if (guests != null)
                {
                    var e = guests.GetEnumerator();
                    while (e.MoveNext())
                    {
                        var c = e.Current;
                        if (c == null) continue;

                        var isSpecial = false;
                        try { isSpecial = c.TryCast<SpecialGuestsController>() != null; } catch { }

                        var haveNotLeft = true;
                        try { haveNotLeft = c.HaveNotLeft(); } catch { }

                        var marked = Left.ContainsKey(c.Pointer);

                        var desk = -1;
                        var rem = -1;
                        var all = -1;
                        try { desk = c.DeskCode; } catch { }
                        try { rem = c.RemainOrderCount; } catch { }
                        try { all = c.AllOrdersCount; } catch { }

                        var order = "无单";
                        try
                        {
                            var o = c.PeekOrders();
                            if (o != null) order = o.IsFullfilled ? "已上菜" : "在等";
                        }
                        catch { order = "无单(异常)"; }

                        lines.Add($"{NameOf(c)}|{TypeOf(c)}|稀客={(isSpecial ? "是" : "否")}|{order}" +
                                  $"|桌{desk}|剩{rem}/{all}|在店={(haveNotLeft ? "是" : "否")}|黑名单={(marked ? "是" : "否")}");
                    }
                }
            }
            catch (Exception e)
            {
                lines.Add("巡查失败: " + e.Message);
            }

            var sig = string.Join(" ; ", lines.ToArray());
            if (sig == _auditLast) return;
            _auditLast = sig;

            Audit("控制器 " + lines.Count + " 个（ShowNormalGuests=" + OrderHelperPlugin.ShowNormalGuests.Value + "）");
            foreach (var line in lines) Audit(line);
        }


        /// <summary>「（已离席）」占位文本的过滤谓词（静态，避免每拍分配委托）。</summary>
        private static readonly Predicate<GuestView> IsLeftSeatFood = v => v.Food == "（已离席）";

        internal static List<GuestView> Collect(bool showNormalGuests)
        {
            var views = new List<GuestView>();
            SpecialGuest sample = null;

            // 批3：还没头像的稀客名单。常客不算（游戏本来就不给常客立绘，算进去会永远触发扫描）。
            var missing = new List<string>();

            // 采集分四段计时，别再只有一个「采集」总数：
            //   枚举 = 拿控制器列表 + 离店过滤；指纹 = 读几个便宜字段判断要不要重算；
            //   重算 = 真去算名字/喜好/推荐（贵）；头像 = 扫描与合成。
            // 尖峰落在哪一段，决定了下一刀该往哪里切。
            var enumStart = Perf.Start();

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

                    // 客人已经离店（走完离场动画/已离席）就不再进推荐框。
                    // 注意：标记是可撤销的，见 IsLeft 的说明（符卡影像会复用同一个控制器）。
                    try
                    {
                        if (!controller.HaveNotLeft()) continue;
                        if (IsLeft(controller)) continue;
                    }
                    catch { }

                    // 数据没变就直接拿上次那份（文字、推荐、头像都在里面），变了才重算。
                    // 顺手在里面做「稀客/常客」判定：类型也缓存，省掉每 0.5 秒一次的 TryCast。
                    var view = ToViewCached(controller, showNormalGuests);
                    if (view != null)
                    {
                        var isSpecial = view.IsSpecial;
                        if (isSpecial)
                        {
                            // 头像查一次就记住：Get 在找不到时会走模糊匹配（线性扫全部头像），
                            // 没头像的客人每 0.5 秒重扫一遍纯属浪费，所以有图就不再查。
                            if (view.Portrait == null)
                                view.Portrait = GuestPortraits.Get(view.Name);

                            if (view.Portrait != null)
                            {
                                if (view.PortraitContent == null)
                                {
                                    // ★ il2cpp 互操作里的 GUIContent 只剩 string 构造重载，
                                    //   传 Texture 会编译不过 → 用默认构造 + 给 image 属性赋值。
                                    var gc = new GUIContent();
                                    gc.image = view.Portrait;
                                    view.PortraitContent = gc;
                                }
                            }
                            else if (!missing.Contains(view.Name))
                            {
                                missing.Add(view.Name);
                            }
                        }

                        views.Add(view);

                        if (sample == null && isSpecial)
                        {
                            try { sample = controller.TryCast<SpecialGuestsController>().SpecialGuest; }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 采集客人失败: " + e.Message);
            }

            Perf.End("采集·枚举", enumStart);

            // 静态委托，别每拍 new 一个 lambda（采集是常驻节拍，垃圾会一直积）。
            if (views.Count > 0) views.RemoveAll(IsLeftSeatFood);

            // 有人还没头像才去扫（Image 级快扫，缺人时才做全 Sprite 慢扫）。
            var avatarStart = Perf.Start();
            GuestPortraits.RefreshIfNeeded(missing);

            // 顺手把已加载的其它立绘分批做掉（Image 级快扫 + 指数退避，不空转）。
            GuestPortraits.PrefetchTick();
            Perf.End("头像", avatarStart);

            var popStart = Perf.Start();
            PopularInfo.Refresh(sample);
            Perf.End("采集·热门", popStart);

            return views;
        }

        /// <summary>
        /// 单子状态指纹（全是便宜字段，一次采集只读这几个就能判断「要不要重算」）。
        /// 只要它没变，名字/喜好标签/推荐菜/文字全都是上次那份，没有必要重算。
        /// </summary>
        private struct OrderStamp
        {
            public int CtrlDesk;
            public int Desk;
            public int Remains;
            public int Alls;
            public int FoodReq;
            public int BevReq;
            public bool Fullfilled;

            /// <summary>客人剩余的钱（v1.6.1）。带上它，钱一变（上菜付款 / 加成 buff）就重算预算。</summary>
            public int Fund;

            internal bool Same(in OrderStamp o)
            {
                return CtrlDesk == o.CtrlDesk && Desk == o.Desk && Remains == o.Remains &&
                       Alls == o.Alls && FoodReq == o.FoodReq && BevReq == o.BevReq &&
                       Fullfilled == o.Fullfilled && Fund == o.Fund;
            }
        }

        private sealed class CachedView
        {
            public OrderStamp Stamp;
            public string Name;
            public float At;
            public bool IsSpecial;
            public GuestView View;
        }

        /// <summary>
        /// 客人 view 按 IL2CPP 指针缓存。为什么要这么做：
        /// 采集每 0.5 秒跑一次，而 ToView 里 OnGetGuestName / TryCast / 喜好标签拷贝 /
        /// 找菜找酒 / 拼文字全都要穿 interop。实测一次采集平均 3.6–5.6ms、峰值几十毫秒，
        /// 在 60fps 下就是「每半秒顿一下」——这正是玩家能感觉到的卡顿来源。
        /// 客人安安静静等菜的时候这些数据一个字都没变，重算纯属浪费。
        ///
        /// 安全性：指纹 + 名字都要对得上才复用；名字一起比是为了防 IL2CPP 指针被回收后
        /// 被另一位客人复用（那种情况下指纹可能恰好一样，但名字不一样）。
        /// 就算全撞上了，最多也只错 ViewTtl 秒。
        /// </summary>
        private static readonly Dictionary<IntPtr, CachedView> ViewCache = new();

        /// <summary>缓存有效期：库存与流行风向会变，单子没动也定期重算一遍。</summary>
        private const float ViewTtl = 2f;

        private static GuestView ToViewCached(GuestGroupController controller, bool showNormalGuests)
        {
            IntPtr ptr;
            try { ptr = controller.Pointer; } catch { ptr = IntPtr.Zero; }

            CachedView cached = null;
            if (ptr != IntPtr.Zero) ViewCache.TryGetValue(ptr, out cached);

            // 类型判定也走缓存：TryCast 每次都要问一次 il2cpp 类型系统，而一个控制器的类型永远不会变。
            var isSpecial = cached != null
                ? cached.IsSpecial
                : controller.TryCast<SpecialGuestsController>() != null;

            // 常客要不要显示，在进 ToView 之前就决定（省掉后面的名字/单子/推荐开销）。
            if (!isSpecial && !showNormalGuests) return null;

            var fpStart = Perf.Start();

            var name = "";
            try { name = controller.OnGetGuestName() ?? ""; }
            catch { name = "<取名失败>"; }

            var stamp = new OrderStamp { CtrlDesk = -1, Desk = -1, Remains = -1, Alls = -1 };

            try { stamp.CtrlDesk = controller.DeskCode; } catch { }
            try
            {
                stamp.Remains = controller.RemainOrderCount;
                stamp.Alls = controller.AllOrdersCount;
            }
            catch { stamp.Remains = -1; stamp.Alls = -1; }

            // 预算（v1.6.1）：GetFund = 客人身上还剩多少钱。读不到就当 0（不拦）。
            try { stamp.Fund = controller.GetFund; } catch { stamp.Fund = 0; }

            GuestsManager.OrderBase order = null;
            try { order = controller.PeekOrders(); } catch { order = null; }

            if (order != null)
            {
                try
                {
                    stamp.Desk = order.DeskCode;
                    stamp.FoodReq = order.foodRequest;
                    stamp.BevReq = order.beverageRequest;
                    stamp.Fullfilled = order.IsFullfilled;
                }
                catch { }
            }

            var now = Time.realtimeSinceStartup;

            Perf.End("采集·指纹", fpStart);

            if (cached != null && cached.View != null &&
                cached.IsSpecial == isSpecial && cached.Stamp.Same(stamp) &&
                string.Equals(cached.Name, name, StringComparison.Ordinal) && now - cached.At < ViewTtl)
            {
                // 完全没变：直接用上次算好的那份（文字、推荐、头像都在里面）。
                Perf.Count("命中");
                return cached.View;
            }

            var view = cached?.View;
            var samePerson = cached != null && string.Equals(cached.Name, name, StringComparison.Ordinal);

            if (view == null) view = new GuestView();
            else ResetView(view, samePerson);

            // 「重算」包住 ToView（读喜好标签、找菜找酒、折算评价——真正贵的那段），
            // 「文字」单独计时，因为它只是拼字符串，不该和计算混在一个数字里。
            var rebuildStart = Perf.Start();
            ToView(controller, view, isSpecial, name, order, stamp);

            var buildStart = Perf.Start();
            view.BuildLines();
            Perf.End("采集·文字", buildStart);
            Perf.End("采集·重算", rebuildStart);

            Perf.Count("重算");

            if (ptr != IntPtr.Zero)
            {
                if (ViewCache.Count > 64) ViewCache.Clear();   // 长局防泄漏（指针本来也会被复用）

                ViewCache[ptr] = new CachedView
                {
                    Stamp = stamp, Name = name, At = now, IsSpecial = isSpecial, View = view
                };
            }

            return view;
        }

        /// <summary>
        /// 读客人的钱包（v1.6.2 口径：**不均分**）。
        ///
        /// 规则只有一条：**这单花掉的钱不能超过他手上现钱**，只有「最后一单」才允许小幅超支
        /// （余量 = 本次到店总预算的 <see cref="LastOrderSlackRate"/>，且夹在 30~300 之间）。
        /// 这样预算 1000 的客人，「第一单 800 + 最后一单 300」是可行的（总共 1100，超 10%），
        /// 而不是像均分那样硬把第一单压到 500。
        ///
        /// 读不到、或配置关掉，就整块关掉（FundKnown=false），退回不拦的老行为。
        /// </summary>
        private static void ApplyBudget(GuestGroupController controller, GuestView view)
        {
            view.FundMax = 0;
            view.FundBase = 0;
            view.BudgetCap = 0;
            view.FundKnown = false;

            if (OrderHelperPlugin.RespectFund != null && !OrderHelperPlugin.RespectFund.Value) return;
            if (view.Fund <= 0) return;

            try { view.FundMax = controller.MaxFundCarry; } catch { view.FundMax = 0; }

            view.FundKnown = true;

            // 总预算：同一客人身上看到过的最大资金。资金只会随上菜变少，所以「最大值」
            // 就是这次到店开始时带多少钱；插件中途加载进来时它等于当时的余额，偏小 → 余量更紧，安全。
            var key = view.Name ?? "";
            if (key.Length == 0)
            {
                view.FundBase = view.Fund;
            }
            else
            {
                if (FundBase.Count > 200) FundBase.Clear();
                if (!FundBase.TryGetValue(key, out var prev) || view.Fund > prev) prev = view.Fund;
                FundBase[key] = prev;
                view.FundBase = prev;

                // 资金变少的证据：确认 GetFund 到底是「剩余」还是「初始额度」。
                if (prev > view.Fund && BudgetChanges < 12)
                {
                    BudgetChanges++;
                    OrderHelperPlugin.LogSource?.LogInfo(
                        "[OrderHelper] 预算变化：客人=" + view.Name + " 资金 " + prev + "\u2192" + view.Fund +
                        " 总预算=" + prev + " 剩单=" + view.RemainOrders);
                }
            }

            view.BudgetCap = CapOf(view);

            // 样例日志：字段语义是按名字推断的（GetFund 到底是「剩余」还是「初始」，
            // 得靠实机数据确认）。每位客人各记一条，最多 6 条，不刷屏。
            if (BudgetSamples.Count < 6 && !BudgetSamples.Contains(view.Name))
            {
                BudgetSamples.Add(view.Name);
                OrderHelperPlugin.LogSource?.LogInfo(
                    "[OrderHelper] 预算样例：客人=" + view.Name + " 稀客=" + (view.IsSpecial ? 1 : 0) +
                    " 资金=" + view.Fund + " 上限=" + view.FundMax + " 总预算=" + view.FundBase +
                    " 剩单=" + view.RemainOrders + " 本单上限=" + view.BudgetCap);
            }
        }

        /// <summary>最后一单允许超出的比例（相对本次到店总预算）。10% 属于「略超」，不是大幅超。</summary>
        private const float LastOrderSlackRate = 0.10f;

        /// <summary>
        /// 这一单的上限。
        /// 还有别的单没上 → 只能花手上剩下的钱（保证这一单一定付得起）；
        /// 最后一单 → 允许再超一点（总预算的 10%，30~300 之间），也就是「最后一单小幅超预算」。
        /// </summary>
        internal static int CapOf(GuestView view)
        {
            if (view == null || !view.FundKnown) return int.MaxValue;

            var fund = view.Fund > 0 ? view.Fund : 0;
            if (view.RemainOrders > 1) return fund;

            var baseFund = view.FundBase > fund ? view.FundBase : fund;
            var slack = (int)(baseFund * LastOrderSlackRate);
            if (slack < 30) slack = 30;
            if (slack > 300) slack = 300;
            return fund + slack;
        }

        /// <summary>客人名 → 本次到店的总预算（看到的资金最大值）。换场景清空。</summary>
        private static readonly Dictionary<string, int> FundBase = new();

        private static int BudgetChanges;

        private static readonly HashSet<string> BudgetSamples = new();

        // ── 刷好感模式（v1.6.3）──────────────────────────────────────────────

        /// <summary>
        /// 刷好感模式对这位客人生不生效：只对稀客生效（常客没有好感/没有 5 单的说法），
        /// 而且要配置开着、钱读得到（读不到钱就退化成普通推荐，至少不会乱压预算）。
        /// </summary>
        internal static bool FavorOn(GuestView view)
        {
            if (view == null || !view.IsSpecial || !view.FundKnown) return false;
            return OrderHelperPlugin.FavorFarm != null && OrderHelperPlugin.FavorFarm.Value;
        }

        /// <summary>
        /// 刷好感时这一单的上限：**先给后面每一单留够「最低粉评价」，剩下的才允许这一单花**。
        ///
        /// 为什么不能直接均分：均分假设每单都花一样多，可菜价差很多（便宜的 300、贵的 900），
        /// 平均值会让贵单被砍到做不出粉评。这里只保证「后面每单至少还能买到一份最低粉评组合」，
        /// 多出来的钱这一单随便花 —— 既保吃满单，又不至于像均分那样把手脚绑死。
        ///
        /// 留不出来（钱根本不够吃满）时不硬省：至少让这一单还能粉评，剩下的交给玩家判断。
        /// </summary>
        private static int CapForOrder(GuestView view, List<DishCandidate> dishes)
        {
            var cap = view.FundKnown ? (view.BudgetCap > 0 ? view.BudgetCap : view.Fund) : 0;
            if (!FavorOn(view) || view.RemainOrders <= 1) return cap;

            var need = CheapestPinkPrice(view, dishes);
            if (need <= 0) return cap;

            var reserve = need * (view.RemainOrders - 1);
            var favorCap = view.Fund - reserve;
            if (favorCap < need) favorCap = need;      // 至少这一单还能粉评
            if (favorCap > view.Fund) favorCap = view.Fund;  // 不能超过手上的现钱
            if (favorCap < cap) cap = favorCap;

            // 存回去：推荐框过滤、酒水按钮的「超预算」标记都读这个字段，
            // 三处必须用同一个上限，否则会出现「按钮说能买、搭配说超了」的自相矛盾。
            view.BudgetCap = cap;

            if (FavorLogs < 8)
            {
                FavorLogs++;
                OrderHelperPlugin.LogSource?.LogInfo(
                    "[OrderHelper] 刷好感预算：客人=" + view.Name + " 总预算=" + view.FundBase +
                    " 现钱=" + view.Fund + " 剩单=" + view.RemainOrders +
                    " 每单最低粉评=" + need + " 预留=" + reserve + " 本单上限=" + cap);
            }

            return cap;
        }

        private static int FavorLogs;

        /// <summary>
        /// 一份「最低粉评组合」要多少钱：每道能做的菜各自配上它最便宜的粉评酒，
        /// 取所有菜里的最小值。0 = 算不出来（缺价格/缺粉评候选），调用方就别压预算。
        /// </summary>
        private static int CheapestPinkPrice(GuestView view, List<DishCandidate> dishes)
        {
            var drinks = view.BeverageCandidates;
            if (dishes == null || drinks == null || drinks.Count == 0) return 0;

            var best = 0;
            for (var i = 0; i < dishes.Count; i++)
            {
                var dish = dishes[i];
                if (dish == null || !dish.CanCook) continue;

                var dishPrice = PriceOf(dish);
                for (var j = 0; j < drinks.Count; j++)
                {
                    var drink = drinks[j];
                    if (drink == null) continue;
                    if (dish.Points + drink.Points < 4) continue;

                    var total = dishPrice + PriceOf(drink);
                    if (total <= 0) continue;
                    if (best == 0 || total < best) best = total;
                }
            }

            return best;
        }

        /// <summary>复用 view 对象前必须清干净：早退分支（未点单/已上菜）不会重写所有字段。</summary>
        /// <param name="keepPortrait">还是同一位客人：头像贴图和 GUIContent 留着，别每 2 秒重建一个。</param>
        private static void ResetView(GuestView v, bool keepPortrait)
        {
            v.Name = "";
            v.Kind = "";
            v.IsSpecial = false;
            v.Desk = -1;
            v.CtrlDesk = -1;
            v.OrderDesk = -1;
            v.Food = "";
            v.Beverage = "";
            v.Ordering = false;
            v.Served = false;
            v.Fund = 0;
            v.FundMax = 0;
            v.FundBase = 0;
            v.BudgetCap = 0;
            v.FundKnown = false;
            v.NotOrderedYet = false;
            v.RemainOrders = -1;
            v.AllOrders = -1;
            v.RawFoodRequest = -1;
            v.FoodTagRequest = -1;
            v.BevTagRequest = -1;

            v.Candidates.Clear();
            v.BeverageCandidates.Clear();
            v.LikeFoodTags.Clear();
            v.HateFoodTags.Clear();
            v.LikeBevTags.Clear();
            v.HateBevTags.Clear();
            v.Pairs.Clear();
            v.BestDish = null;
            v.BestDrink = null;
            v.HintDrinks.Clear();

            v.MainTitleLine = "";
            v.MainFoodLine = "";
            v.MainBeverageLine = "";
            v.HintTitleLine = "";
            v.NoDrinkLine = "";
            v.StatusLine = "";
            v.Initial = "?";

            if (!keepPortrait)
            {
                v.Portrait = null;
                v.PortraitContent = null;
            }
        }

        private static GuestView ToView(
            GuestGroupController controller, GuestView view, bool isSpecial,
            string name, GuestsManager.OrderBase order, in OrderStamp stamp)
        {
            view.Name = name;
            view.IsSpecial = isSpecial;
            view.Kind = isSpecial ? "稀客" : "常客";
            view.CtrlDesk = stamp.CtrlDesk;
            view.RemainOrders = stamp.Remains;
            view.AllOrders = stamp.Alls;
            view.Fund = stamp.Fund;
            ApplyBudget(controller, view);

            SpecialGuestsController special = null;
            if (isSpecial)
            {
                try { special = controller.TryCast<SpecialGuestsController>(); } catch { }
            }

            if (order == null)
            {
                view.Food = "（尚未点单）";
                view.NotOrderedYet = true;
                view.Desk = view.CtrlDesk;
                return view;
            }

            view.Desk = stamp.Desk;
            view.OrderDesk = stamp.Desk;
            // 所有单都做完、且没有下一单 → 客人已经吃完了，不再挂在推荐框里。
            if (view.RemainOrders == 0 && view.AllOrders > 0)
            {
                view.Food = "（已离席）";
                view.Beverage = "";
                return view;
            }
            // 桌位/剩单数已经在 ToViewCached 里读过一次（原来这里又读了两遍，白穿两次 interop）。
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
                        view.NotOrderedYet = true;
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

            // v1.5.5：加料能把「分不够」的菜救回粉评，所以开了加料就算力上多取几道进池子，
            // 免得真正能翻盘的那道在第一步就被按原始标签分砍掉。算完再按最终分重排。
            view.Candidates = RecipeIndex.Find(
                order.foodRequest, exact, view.LikeFoodTags, view.HateFoodTags, DishPool());

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
            var perfT0 = Perf.Start();
            try
            {
                ApplyRatingsToCandidates(
                    view.Candidates, view.BeverageCandidates, view.LikeFoodTags, view.HateFoodTags, view);
                BuildPairs(view);
                // 酒水推荐框要显示的那几杯，在收集阶段就定下来：
                // 原来是在 OnGUI 里每帧 new List + 遍历筛选，一帧两次，是稳定的每帧垃圾。
                view.HintDrinks = FilterHintDrinks(view);
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 折算评价失败: " + e.Message);
            }

            Perf.End("采集·评分", perfT0);
        }

        /// <summary>
        /// 推荐框里该显示哪几杯酒：稀客优先合计 ≥4 的，凑不出就退回命中最多的那杯。
        /// v1.6.2 起再按预算排一层：这一单付得起的排前面，全都付不起就按价格从低到高
        /// （让玩家一眼看到「最省钱的那杯粉评是什么」）。
        /// </summary>
        private static List<DishCandidate> FilterHintDrinks(GuestView view)
        {
            var src = view.BeverageCandidates;
            if (src == null || src.Count == 0) return EmptyCandidates;

            // 常客不做粉评过滤，直接用原列表（只读，不复制）。
            if (!view.IsSpecial) return src;

            var pink = new List<DishCandidate>();
            foreach (var drink in src)
                if (drink != null && drink.TotalPoints >= 4) pink.Add(drink);

            if (pink.Count == 0)
            {
                var top = BestHit(src);
                if (top != null) pink.Add(top);
                return pink;
            }

            if (!view.FundKnown) return pink;

            // 菜价按「当前最推荐的那道菜」算：玩家点了这杯，多半就是配这道菜上的。
            // 上限用这一单的上限（最后一单含余量），和配酒时的口径保持一致。
            var refPrice = view.Pairs.Count > 0 ? PriceOf(view.Pairs[0].Dish) : 0;
            var cap = view.BudgetCap > 0 ? view.BudgetCap : view.Fund;
            var room = cap - refPrice;

            var fit = new List<DishCandidate>(pink.Count);
            var over = new List<DishCandidate>(pink.Count);
            foreach (var drink in pink)
            {
                if (PriceOf(drink) <= room) fit.Add(drink);
                else over.Add(drink);
            }

            if (fit.Count == 0)
            {
                // 一杯都付不起：按价格从低到高，先给最省的（按钮上仍会标 ⚠超预算）。
                over.Sort((a, b) => PriceOf(a).CompareTo(PriceOf(b)));
                return MovePairDrinksFirst(view, over);
            }

            // 刷好感（v1.6.4）：同样粉评时越便宜越好——省下的钱能让后面几单继续吃。
            // 普通口径保持原顺序（按利润，玩家想赚钱）。
            if (FavorOn(view)) fit.Sort((a, b) => PriceOf(a).CompareTo(PriceOf(b)));

            fit.AddRange(over);

            // 再和主面板对齐：主面板推荐的那几杯排最前，其余跟随（见方法注释）。
            return MovePairDrinksFirst(view, fit);
        }

        /// <summary>
        /// 把「主面板最佳搭配」里出现过的酒提到最前，其余保持传入顺序（v1.6.4）。
        ///
        /// 为什么要这一步：主面板展示的是 `Pairs`（已经按当前口径排好 —— 普通/按预算/刷好感），
        /// 而酒水推荐框原来走的是另一套顺序（粉评 → 利润 → 库存），两处口径不同。
        /// 玩家切了「刷好感」，主面板变了、柜子旁边的推荐却一动不动，看着就像开关没生效。
        /// 这里把 Pairs 里的 Drink 提前，让两处「第一推荐」保证是同一杯。
        /// </summary>
        private static List<DishCandidate> MovePairDrinksFirst(GuestView view, List<DishCandidate> drinks)
        {
            var pairs = view?.Pairs;
            if (pairs == null || pairs.Count == 0 || drinks == null || drinks.Count < 2) return drinks;

            var head = new List<DishCandidate>(pairs.Count);
            foreach (var pair in pairs)
            {
                var drink = pair?.Drink;
                if (drink == null || !drinks.Contains(drink) || head.Contains(drink)) continue;
                head.Add(drink);
            }

            if (head.Count == 0) return drinks;

            var rest = new List<DishCandidate>(drinks.Count);
            foreach (var drink in drinks)
                if (!head.Contains(drink)) rest.Add(drink);

            head.AddRange(rest);
            return head;
        }

        private static readonly List<DishCandidate> EmptyCandidates = new();

        /// <summary>菜候选池大小：不开加料就只取展示需要的 3 道，开了给规划器多留几道备选。</summary>
        private static int DishPool()
        {
            return OrderHelperPlugin.ConsiderExtras != null && OrderHelperPlugin.ConsiderExtras.Value ? 5 : 3;
        }

        /// <summary>
        /// 给「菜 + 酒」算游戏评价：每项合计 = 自身命中数 + 另一方最高命中数；
        /// 粉评要求菜酒成对且合计 ≥ 4。写完分数后按合计分重排。
        /// 特殊厨具（三位一体）临时补算推荐时也走这里，保证两边显示规则一致。
        ///
        /// v1.5.5 起这里多干一件事：**给每道菜配一个加料方案**。
        /// 菜谱 tag 之外，加料的 tag 也参与评价，所以「需要几分」= 4 − 酒的分数，
        /// 规划器按这个目标补齐（够分就停，不多加一份），再按利润和食材余量排序。
        /// likes / hates 不传就退回旧行为（只算菜谱 tag），用于没有客人上下文的场合。
        /// </summary>
        internal static void ApplyRatingsToCandidates(
            List<DishCandidate> dishes, List<DishCandidate> drinks,
            IList<int> likes = null, IList<int> hates = null, GuestView view = null)
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

            // 加料规划：目标固定 = 4 分（粉评线）。
            // **不再拿酒的分数来抵**：v1.5.x 是「target = 4 − 酒分」，酒分一高（常常 2~3 分），
            // 菜自己早就够了 → 规划器一份加料都不挑（实测 16 次一键制作全是 加料=0）。
            // 现在要求「单单食材自己就要能粉评」，加料才有事可做。
            if (dishes != null && likes != null &&
                OrderHelperPlugin.ConsiderExtras != null && OrderHelperPlugin.ConsiderExtras.Value)
            {
                var target = 4;

                var stock = ExtraPlanner.Stock();
                foreach (var dish in dishes)
                    ApplyPlan(dish, likes, hates, target, stock);

                // 加料会改分数，跨菜排序要跟着更新（否则前 3 名还是按旧分选的）。
                bestDish = 0;
                foreach (var dish in dishes)
                    if (dish.Points > bestDish) bestDish = dish.Points;
            }

            if (dishes != null)
            {
                foreach (var dish in dishes)
                {
                    dish.BaseScore = dish.Score;
                    var totalDish = dish.Points + (hasBev ? bestBev : 0);
                    Rate(dish, totalDish, totalDish, hasBev);
                }
            }

            if (drinks != null)
            {
                foreach (var bev in drinks)
                {
                    bev.BaseScore = bev.Score;
                    var totalBev = bev.Points + (hasDish ? bestDish : 0);
                    Rate(bev, totalBev, totalBev, hasDish);
                }
            }

            // 评分改写后必须重排，否则列表顺序还停在粗评分上，
            // 会出现「下面一条显示粉评、上面一条显示绿评」的自相矛盾。
            SortByRating(dishes, true);
            SortByRating(drinks, false);

            // 文字在这里一次生成：OnGUI 每帧要跑好几遍，逐帧拼多行字符串纯属浪费。
            // room = 客人身上剩的钱 − 参考菜价（第 1 个搭配的菜），给酒水按钮标超预算用。
            var room = -1;
            if (view != null && view.FundKnown)
            {
                var refPrice = view.Pairs.Count > 0 ? PriceOf(view.Pairs[0].Dish) : 0;
                var cap = view.BudgetCap > 0 ? view.BudgetCap : view.Fund;
                room = cap - refPrice;
            }

            PrepareLabels(dishes, true);
            PrepareLabels(drinks, false, room);
        }

        /// <summary>
        /// 给一道菜算加料方案并写回候选（分数、利润、余量、加料名）。
        /// 算不出方案（菜谱丢了）就保持原样，**不改分数**，避免把一道本来能做的菜算成 0 分。
        /// </summary>
        private static void ApplyPlan(
            DishCandidate dish, IList<int> likes, IList<int> hates, int target, Dictionary<int, int> stock)
        {
            if (dish == null || dish.Recipe == null) return;

            try
            {
                var foodId = dish.Recipe.FoodID;
                var foodTags = RecipeIndex.FoodTagsOfFood(foodId);
                var required = GetRecipeIngredients(dish);

                var planStart = Perf.Start();
                var plan = ExtraPlanner.Plan(
                    foodId, dish.RecipeId, dish.Recipe, required, foodTags, likes, hates, target, stock);
                Perf.End("采集·加料", planStart);

                if (plan == null) return;

                Perf.Count("加料·规划");
                if (plan.BlockedCount > 0) Perf.Count("加料·被挡");

                dish.Plan = plan;
                dish.ExtraText = plan.Names;
                dish.Profit = plan.Profit;
                dish.MinRemain = plan.MinRemain;

                // 命中数换成「含加料」的版本，评价与本项目其它地方（净分显示）才对得上。
                dish.LikeHits = plan.LikeHits;
                dish.HateHits = plan.HateHits;
                dish.Points = plan.Net;
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource?.LogWarning("[OrderHelper] 加料规划失败: " + e.Message);
            }
        }

        /// <summary>菜谱自带食材 id 表（走 RecipeIndex 的缓存副本，不在热路径上穿 interop）。</summary>
        private static int[] GetRecipeIngredients(DishCandidate dish)
        {
            if (dish.RecipeId >= 0) return RecipeIndex.RecipeIngredients(dish.RecipeId);

            // 少数候选（面板自带菜谱那种）没有登记 RecipeId，这时才直接穿一次 interop。
            var raw = dish.Recipe?.Ingredients;
            if (raw == null) return System.Array.Empty<int>();

            var copy = new int[raw.Length];
            for (var i = 0; i < raw.Length; i++) copy[i] = raw[i];
            return copy;
        }

        /// <summary>净分：命中喜好标签加分、命中厌恶标签扣分，加减完的合计。</summary>
        internal static int NetOf(DishCandidate candidate)
        {
            return candidate == null ? 0 : candidate.LikeHits - candidate.HateHits;
        }

        /// <summary>缺料说明太长会把整行顶出窗口，配对列表里只留开头，细节看推荐框。</summary>
        internal static string ShortMissing(string missing)
        {
            if (string.IsNullOrEmpty(missing)) return "缺料";
            return missing.Length > 14 ? missing.Substring(0, 13) + "\u2026" : missing;
        }

        /// <summary>预生成按钮/明细文字（供推荐框直接用）。</summary>
        internal static void PrepareLabels(List<DishCandidate> list, bool food, int room = -1)
        {
            if (list == null) return;

            foreach (var item in list)
                PrepareLabel(item, food, room);
        }

        /// <summary>
        /// 单条版：OnGUI 里发现某条还没生成文字时直接调它，
        /// 不用再 new List&lt;DishCandidate&gt;{ dish } 为了一行字产一堆垃圾。
        /// </summary>
        internal static void PrepareLabel(DishCandidate item, bool food, int room = -1)
        {
            if (item == null) return;

            {
                var net = NetOf(item);
                var gap = item.TotalPoints >= 4 ? "" : $" 差{4 - item.TotalPoints}分";

                if (food)
                {
                    var state = item.CanCook
                        ? "可做"
                        : (string.IsNullOrEmpty(item.Missing) ? "缺料" : item.Missing);
                    item.ButtonLabel = $"{item.Name}(净{net}分)  [{item.Rating} 合计{item.TotalPoints}分{gap}] {state}";
                    item.DetailText = item.Recipe != null ? CookerPanelWatcher.IngredientText(item.Recipe) : "";

                    // 加料写在明细行尾部：推荐框里一眼能看到这道菜还要补什么。
                    if (!string.IsNullOrEmpty(item.ExtraText))
                        item.DetailText += "  加料：" + item.ExtraText + $"(赚{item.Profit})";
                    else if (!string.IsNullOrEmpty(item.Plan?.Note))
                        item.DetailText += "  " + item.Plan.Note + "，已跳过";
                }
                else
                {
                    // 酒把单价也带上：预算不够时玩家得知道是哪一杯贵了。
                    // room = 客人身上剩的钱 − 参考菜价，-1 表示不判断（钱读不到）。
                    var price = PriceOf(item);
                    var priceText = price > 0 ? $" 价{price}" : "";
                    var overText = room >= 0 && price > room ? " \u26A0超预算" : "";
                    item.ButtonLabel = $"{item.Name}(净{net}分)  [{item.Rating} 合计{item.TotalPoints}分] " +
                                       $"{(item.CanCook ? "有货" : "缺货")}{priceText}{overText}";
                    item.DetailText = "";
                }

                // 按钮文字连着明细一起定稿：推荐框每帧 `ButtonLabel + "\n" + DetailText`
                // 会为每一行新建一个字符串，客人多、菜多时一帧就是几十个。
                item.FullLabel = string.IsNullOrEmpty(item.DetailText)
                    ? item.ButtonLabel
                    : item.ButtonLabel + "\n" + item.DetailText;
            }
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

        /// <summary>候选项的售价。DishCandidate.Price 读不到时退回加料方案里的菜价。</summary>
        private static int PriceOf(DishCandidate candidate)
        {
            if (candidate == null) return 0;
            if (candidate.Price > 0) return candidate.Price;
            var plan = candidate.Plan;
            return plan != null && plan.Price > 0 ? plan.Price : 0;
        }

        /// <summary>
        /// 给一道菜挑一杯酒（v1.6.3，带预算 + 刷好感）。
        ///
        /// 规则按优先级：
        ///   1. **超过这一单上限的酒不选**（上限由调用方给，见 <see cref="CapForOrder"/>）；
        ///   2. 在付得起的里面先要粉评（合计 ≥ 4）；
        ///      - 普通模式：同样粉评时先看能不能给后面几单留点钱，再比价钱（越贵越赚）、库存；
        ///      - **刷好感模式：同样粉评时越便宜越好**——省下的钱能多上一单，这才是「吃满 5 单」的关键；
        ///   3. 付得起的里面一杯粉评的都没有时，**退回最便宜的粉评那杯**并标记超预算 ——
        ///      玩家至少看得到「要粉评就得超这么多」，而不是被悄悄换成橙评。
        ///   4. 一杯粉评的都没有（真凑不出）就按老规则挑，让上层照常判「凑不出粉评」。
        /// </summary>
        private static DishCandidate PickDrink(GuestView view, DishCandidate dish, int dishPrice, int cap, bool favor)
        {
            var drinks = view.BeverageCandidates;

            // 软顶：只是「更想要」的偏好。普通模式还有单没上时别把现钱一次花光，给后面留 20%；
            // 刷好感模式的上限已经扣掉「后面每单的最低粉评价」，这里不再额外压。
            var soft = cap;
            if (!favor && view.FundKnown && view.RemainOrders > 1)
            {
                soft = view.Fund * 4 / 5;
                if (soft > cap) soft = cap;
            }

            DishCandidate bestFit = null;      // 付得起里最优（不管粉不粉）
            DishCandidate bestFitPink = null;  // 付得起里最优的粉评
            DishCandidate bestPink = null;     // 不管钱，最便宜的粉评（「都超预算」时的兜底）
            DishCandidate bestAny = null;      // 老规则：不管钱挑出来的那个

            for (var i = 0; i < drinks.Count; i++)
            {
                var drink = drinks[i];
                if (drink == null) continue;

                var points = dish.Points + drink.Points;
                var pink = points >= 4;
                var total = dishPrice + PriceOf(drink);
                var fits = !view.FundKnown || total <= cap;
                var fitsSoft = !view.FundKnown || total <= soft;

                // 不管钱的老规则：粉评优先 → 卖得贵（= 赚得多）→ 库存多 → 分高
                if (bestAny == null || BetterDrink(drink, bestAny, pink, dish.Points + bestAny.Points >= 4))
                    bestAny = drink;

                if (pink && (bestPink == null || PriceOf(drink) < PriceOf(bestPink)))
                    bestPink = drink;   // 兜底要「最便宜的粉评」，把超支压到最小

                if (!fits) continue;

                if (bestFit == null || BetterDrink(drink, bestFit, pink, dish.Points + bestFit.Points >= 4))
                    bestFit = drink;

                if (!pink) continue;

                if (bestFitPink == null)
                {
                    bestFitPink = drink;
                    continue;
                }

                // 粉评且付得起：
                //   普通模式 —— 先看能不能给后面几单留点钱，再看赚得多不多、库存够不够；
                //   刷好感模式 —— 越便宜越好（同样粉评，省的钱能让后几单继续吃），再比库存、分。
                bool better;
                if (favor)
                {
                    better = PriceOf(drink) != PriceOf(bestFitPink)
                        ? PriceOf(drink) < PriceOf(bestFitPink)
                        : (drink.MinRemain != bestFitPink.MinRemain
                            ? drink.MinRemain > bestFitPink.MinRemain
                            : drink.Points > bestFitPink.Points);
                }
                else
                {
                    better = fitsSoft != (dishPrice + PriceOf(bestFitPink) <= soft)
                        ? fitsSoft
                        : (PriceOf(drink) != PriceOf(bestFitPink)
                            ? PriceOf(drink) > PriceOf(bestFitPink)
                            : (drink.MinRemain != bestFitPink.MinRemain
                                ? drink.MinRemain > bestFitPink.MinRemain
                                : drink.Points > bestFitPink.Points));
                }

                if (better) bestFitPink = drink;
            }

            if (bestFitPink != null)
            {
                LogBudgetSwap(view, dish, bestAny, bestFitPink, dishPrice);
                return bestFitPink;
            }

            // 付得起的里面没有一个粉评：要么真的凑不出粉评，要么粉评那杯就是付不起。
            // 后者退回最便宜的粉评并标记超预算 —— 让玩家看见「要粉评得超多少」，不被悄悄降档。
            if (bestPink != null)
            {
                LogBudgetSwap(view, dish, bestAny, bestPink, dishPrice);
                return bestPink;
            }

            // 连粉评都没有：优先给「付得起」的那杯（至少能上点东西），
            // 全都付不起就按老规则给一个，上层会判「凑不出粉评」。
            return bestFit ?? bestAny;
        }

        /// <summary>同一条挑选口径下的「a 是否优于 b」：粉评 → 卖得贵 → 库存多 → 分高。</summary>
        private static bool BetterDrink(DishCandidate a, DishCandidate b, bool aPink, bool bPink)
        {
            if (aPink != bPink) return aPink;

            var aPrice = PriceOf(a);
            var bPrice = PriceOf(b);
            if (aPrice != bPrice) return aPrice > bPrice;

            if (a.MinRemain != b.MinRemain) return a.MinRemain > b.MinRemain;
            return a.Points > b.Points;
        }

        private static int _budgetSwapLogs;

        /// <summary>记录一次「因为预算换了酒」，作为功能生效的证据（最多 12 条）。</summary>
        private static void LogBudgetSwap(GuestView view, DishCandidate dish, DishCandidate wanted, DishCandidate picked, int dishPrice)
        {
            if (!view.FundKnown || _budgetSwapLogs >= 12) return;
            if (wanted == null || ReferenceEquals(wanted, picked)) return;

            var cap = view.BudgetCap > 0 ? view.BudgetCap : view.Fund;
            if (dishPrice + PriceOf(wanted) <= cap) return;

            _budgetSwapLogs++;
            OrderHelperPlugin.LogSource?.LogInfo(
                "[OrderHelper] 预算降档：客人=" + view.Name + " 菜=" + (dish != null ? dish.Name : "?") +
                " 菜价=" + dishPrice +
                " 原本会选=" + wanted.Name + "(" + PriceOf(wanted) + ")" +
                " 改选=" + picked.Name + "(" + PriceOf(picked) + ")" +
                " 资金=" + view.Fund + " 本单上限=" + cap + " 总预算=" + view.FundBase +
                " 剩单=" + view.RemainOrders);
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

                // 刷好感（v1.6.3）：还要留钱给后面几单，所以这一单的上限是「现钱 − 后面每单的最低粉评价」。
                var favor = FavorOn(view);
                var cap = view.FundKnown ? CapForOrder(view, dishes) : int.MaxValue;

                foreach (var dish in dishes)
                {
                    if (dish == null) continue;

                    // 配酒（v1.6.1）：**先用预算把付不起的酒剔掉，再按「粉评 → 赚/省 → 库存」挑**。
                    // 顺序不能反：先挑粉评再比价，等于永远选最贵的那杯，客人钱包一瘪就是黑评。
                    var dishPrice = PriceOf(dish);
                    var best = view.BeverageCandidates != null && view.BeverageCandidates.Count > 0
                        ? PickDrink(view, dish, dishPrice, cap, favor)
                        : null;

                    var total = dish.Points + (best != null ? best.Points : 0);

                    // 稀客到不了粉评的组合就不推荐；常客点的是固定菜，照常列出来。
                    // ★ 预算把粉评酒挤掉时不丢这一行：宁可显示「超预算 + 粉评」让玩家自己决定，
                    //   也不要让整块变成「凑不出粉评」，那样玩家连有钱能买的选项都看不到。
                    if (view.IsSpecial && total < 4) continue;

                    var price = dishPrice + PriceOf(best);
                    view.Pairs.Add(new DishPair
                    {
                        Dish = dish,
                        Drink = best,
                        TotalPoints = total,
                        Rating = RatingText(total, best != null),
                        Price = price,
                        Budget = view.FundKnown ? view.Fund : 0,
                        Cap = view.FundKnown ? cap : 0,
                        OverBudget = view.FundKnown && price > cap,
                        OverCash = view.FundKnown && price > view.Fund && price <= cap,
                    });
                }

                view.Pairs.Sort((a, b) =>
                {
                    // 先按菜的命中数（净分），同命中数再比利润率——和推荐列表同一口径。
                    // 刷好感时净分只按到 4 分为止：4 分就是粉评线，再多也不会多给好感，
                    // 却往往是更贵的菜；所以「够 4 分」之后一律并列，让价格决定，方便吃满单。
                    var netA = NetOf(a.Dish);
                    var netB = NetOf(b.Dish);
                    if (favor)
                    {
                        if (netA > 4) netA = 4;
                        if (netB > 4) netB = 4;
                    }

                    var byNet = netB.CompareTo(netA);
                    if (byNet != 0) return byNet;

                    // 刷好感：粉评已经是底线，接下来最要紧的是「别把钱花光」——
                    // 先挑付得起的，同价再挑便宜的，最后才看利润（利润这会儿不重要，吃满单才重要）。
                    if (favor)
                    {
                        var byCap = a.OverBudget.CompareTo(b.OverBudget);
                        if (byCap != 0) return byCap;

                        var byCheap = a.Price.CompareTo(b.Price);
                        if (byCheap != 0) return byCheap;

                        var byPoints = b.TotalPoints.CompareTo(a.TotalPoints);
                        if (byPoints != 0) return byPoints;
                    }

                    var byProfit = b.Dish.Profit.CompareTo(a.Dish.Profit);
                    if (byProfit != 0) return byProfit;

                    var byTotal = b.TotalPoints.CompareTo(a.TotalPoints);
                    if (byTotal != 0) return byTotal;

                    var byReady = b.Ready.CompareTo(a.Ready);
                    if (byReady != 0) return byReady;

                    var byRemain = b.Dish.MinRemain.CompareTo(a.Dish.MinRemain);
                    if (byRemain != 0) return byRemain;

                    return b.Dish.BaseScore.CompareTo(a.Dish.BaseScore);
                });

                if (view.Pairs.Count > max)
                    view.Pairs.RemoveRange(max, view.Pairs.Count - max);

                foreach (var pair in view.Pairs) FillPairText(pair);

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

        /// <summary>生成配对列表的两行文字（只在这里拼一次）。</summary>
        private static void FillPairText(DishPair pair)
        {
            var dish = pair.Dish;
            var drink = pair.Drink;

            var dishState = dish != null && !dish.CanCook ? "（" + ShortMissing(dish.Missing) + "）" : "";
            var drinkState = drink == null || drink.CanCook ? "" : "（缺货）";
            var drinkName = drink != null ? drink.Name : "无酒";
            var cooker = dish != null && !string.IsNullOrEmpty(dish.Cooker) ? "[" + dish.Cooker + "]" : "";
            var gap = pair.Gap > 0 ? $"  差{pair.Gap}分" : "";

            pair.Line1 = $"    \u2192 {(dish != null ? dish.Name : "?")}{cooker}{dishState}\uFF0B{drinkName}{drinkState}";
            pair.Line2 = $"       {pair.Rating}{pair.TotalPoints}分{gap}  \uFF5C 净分 菜{NetOf(dish)}" +
                         (drink != null ? $" 酒{NetOf(drink)}" : "");

            // 第三行只在真的要加料时才出现：加什么、赚多少、做完还剩多少。
            // 「余」= 用到的最紧张那样食材做完后剩余的份数，别把稀有食材吃到见底。
            // 有冲突被挡掉时也占一行，说明「不是没得加，是加了会变黑暗料理」。
            var extra = dish != null ? dish.ExtraText : "";
            var note = dish?.Plan?.Note ?? "";
            pair.Line3 = !string.IsNullOrEmpty(extra)
                ? $"       \uFF5C 加料 {extra}  \uFF5C 赚{dish.Profit} 余{dish.MinRemain}份"
                : (string.IsNullOrEmpty(note) ? "" : $"       \uFF5C {note}，已跳过");

            // 第四行只在这单算得出价钱时出现：这一单要花客人多少钱、他钱包还剩多少。
            // 超过「这一单上限」才叫可能黑评；只是花掉最后一单的余量就轻轻带过。
            pair.Line4 = "";
            if (pair.Price > 0 && pair.Budget > 0)
            {
                var cap = pair.Cap > 0 ? pair.Cap : pair.Budget;
                var flag = pair.OverBudget
                    ? $"  \u26A0超{pair.Price - cap}，可能黑评"
                    : (pair.OverCash ? $"  略超{pair.Price - pair.Budget}（余量内）" : "");
                pair.Line4 = $"       \uFF5C 价{pair.Price} 预算余{pair.Budget}{flag}";
            }
        }

        /// <summary>
        /// 排序（v1.6.0 改口径）。
        ///
        /// 菜：**先按命中标签数（净分 = 喜好命中 − 厌恶命中）**，同分再看利润，
        ///     再看食材余量，最后才比加料份数。酒水的分**不参与菜的排序**——
        ///     以前按「菜+酒合计」排，结果一起偏爱那些「靠酒凑分」的菜。
        /// 酒：先把「配上菜还能粉评」的排前面（合计 ≥ 4），这一档里按利润、再按库存数量。
        /// </summary>
        private static void SortByRating(List<DishCandidate> list, bool food)
        {
            if (list == null || list.Count < 2) return;

            list.Sort((a, b) =>
            {
                if (food)
                {
                    // 净分就是命中数（LikeHits − HateHits），等价于「tag 命中数」排序。
                    var byNet = NetOf(b).CompareTo(NetOf(a));
                    if (byNet != 0) return byNet;

                    var byProfitF = b.Profit.CompareTo(a.Profit);
                    if (byProfitF != 0) return byProfitF;

                    var byRemainF = b.MinRemain.CompareTo(a.MinRemain);
                    if (byRemainF != 0) return byRemainF;

                    var byExtra = ExtraCount(a).CompareTo(ExtraCount(b));
                    if (byExtra != 0) return byExtra;

                    return b.BaseScore.CompareTo(a.BaseScore);
                }

                // 酒：粉评底线优先（合计 ≥ 4），同档按利润 → 数量。
                var aPink = a.TotalPoints >= 4 ? 1 : 0;
                var bPink = b.TotalPoints >= 4 ? 1 : 0;
                var byPink = bPink.CompareTo(aPink);
                if (byPink != 0) return byPink;

                var byProfit = b.Profit.CompareTo(a.Profit);
                if (byProfit != 0) return byProfit;

                var byRemain = b.MinRemain.CompareTo(a.MinRemain);
                if (byRemain != 0) return byRemain;

                var byCook = b.CanCook.CompareTo(a.CanCook);
                if (byCook != 0) return byCook;

                return b.Points.CompareTo(a.Points);
            });
        }

        /// <summary>加了几份料（没有方案时算 0）。</summary>
        private static int ExtraCount(DishCandidate candidate)
        {
            return candidate?.Plan?.Ids?.Length ?? 0;
        }

        /// <summary>
        /// 写评价。ratingBasis 是判「粉评/橙评」用的分——**按合计分（菜+酒）**，
        /// 那是游戏结算的口径，显示出来的评价必须和游戏一致。
        /// 排序另走一套口径（只看菜自己的命中数），见 SortByRating。
        /// </summary>
        private static void Rate(DishCandidate candidate, int ratingBasis, int total, bool paired)
        {
            candidate.TotalPoints = total;
            candidate.Rating = RatingText(ratingBasis, paired);

            if (ratingBasis >= 4 && paired) { candidate.Score = 95; return; }
            if (ratingBasis >= 3) { candidate.Score = 80; return; }

            switch (ratingBasis)
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


