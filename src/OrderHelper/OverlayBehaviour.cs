using System;
using System.Collections.Generic;
using System.Text;

using GameData.Core.Collections;

using UnityEngine;
using UnityEngine.EventSystems;

namespace MystiaOrderHelper
{
    public class OverlayBehaviour : MonoBehaviour
    {
        public OverlayBehaviour(IntPtr ptr) : base(ptr) { }

        internal static OverlayBehaviour Instance;

        // 默认不显示：进入营业场景（Work）时才自动打开。
        private bool _visible;
        private Rect _rect = new Rect(40f, 40f, 560f, 460f);
        private Vector2 _scroll;

        // 控制面板（F9）：字体、常客、一键制作等功能开关。
        private bool _showSettings;
        internal static bool SettingsVisible;
        private bool _settingsPaused;
        private float _prevTimeScale = 1f;
        private readonly List<EventSystem> _blockedSystems = new List<EventSystem>();
        private readonly List<UnityEngine.InputSystem.InputAction> _blockedActions =
            new List<UnityEngine.InputSystem.InputAction>();
        private NightScene.Input.WorkScenePlayerInputGenerator _playerInput;
        private bool _prevCursorVisible = true;
        private CursorLockMode _prevCursorLock = CursorLockMode.None;
        private Rect _settingsRect;

        private Font _font;
        private int _fontSize = -1;

        private bool _dragging;
        private Vector2 _dragOffset;
        private const float TitleHeight = 24f;

        // 侧边推荐框：各自记住位置与滚动位置，支持拖动标题栏 + 滚轮。
        private Rect _cookerHintRect = new Rect(0f, 0f, -1f, 0f);
        private Rect _bevHintRect = new Rect(0f, 0f, -1f, 0f);
        private Vector2 _cookerHintScroll;
        private Vector2 _bevHintScroll;
        private Vector2 _settingsScroll;
        private string _draggingHint;
        private Vector2 _hintDragOffset;

        private List<GuestView> _guests = new();
        private float _nextRefresh;
        private float _nextSnapshot;
        private float _nextAudit;

        /// <summary>
        /// 常规采集节拍（秒）。原来是 0.5 秒一次，实测「采集」均值 10.8ms、头像那段高峰 200ms+，
        /// 60fps 下每 30 帧就要吃掉一整帧的预算 —— 玩家感觉就是「有点卡」。
        /// 客人点单、上菜这种变化晚 0.8 秒看到完全无感，而切换开关这类「必须立刻生效」的操作
        /// 走 RequestRefresh() 事件通道，不依赖这个节拍。
        /// </summary>
        private const float CollectInterval = 0.8f;

        /// <summary>第一次看到客人的时间（-1 = 还没看到）。UI 侦察靠它延迟 3 秒再扫。</summary>
        private float _firstGuestAt = -1f;

        private bool _guiLogged;
        private bool _guiErrorLogged;
        private bool _updateErrorLogged;
        private bool _hintLogged;
        private float _hintGuestLogTime;
        private bool _bevHintLogged;
        private bool _injected;

        // 主面板：上面一排客人，下面展开选中那一位的完整推荐。
        // 头像位先用一个汉字顶着（批3 换成真立绘），44 这个尺寸提前定死。
        private const float AvatarSize = 48f;

        // 推荐框顶部头像条的间距（头像本身沿用 AvatarSize，点起来跟主面板一样大）。
        private const float StripGap = 6f;

        /// <summary>
        /// 头像按钮的尺寸约束。GUILayout.Width/Height 每次调用都会 new 一个 GUILayoutOption，
        /// 头像条一帧两遍（Layout+Repaint）、每位客人两个 → 一帧几十个垃圾。缓存一份反复用。
        /// </summary>
        private static readonly GUILayoutOption[] AvatarOption =
            { GUILayout.Width(AvatarSize), GUILayout.Height(AvatarSize) };

        /// <summary>
        /// 还能点菜的客人（已上菜等下一单的不进推荐，也不进头像条）。
        /// ★ 在采集阶段重建，不要放在 OnGUI 里 —— 每帧两趟 × 每趟新建 List 等于给 GC 送垃圾。
        /// </summary>
        private readonly List<GuestView> _activeGuests = new();

        private static readonly string ListTip = "客人头像（点一下，下面展开这位的详细推荐）";
        private static readonly Color NormalTint = new Color(1f, 1f, 1f, 1f);
        private static readonly Color SelectedTint = new Color(0.99f, 0.84f, 0.52f, 1f);
        private GuestView _selected;
        private string _selectedKey = "";

        // 推荐匹配缓存：MatchedFor 里有全表扫描，不能每帧每个 GUI 事件都跑一遍。
        // 客人数据 0.5 秒换一次（_guestsVersion 变），厨具面板开关/换厨具时 CookerPanelWatcher.Version 变。
        private readonly List<List<DishCandidate>> _matchCache = new List<List<DishCandidate>>();
        private int _guestsVersion;
        private int _matchCacheGuests = -1;
        private int _matchCachePanel = -1;
        private int _ticks;

        // 窗口标题、主面板首行、料理框标题：内容不变就不重建字符串。
        // OnGUI 每帧两遍（Layout + Repaint），这些原来都是每帧现拼的。
        private static readonly string WindowTitle = "夜雀点单助手 v" + OrderHelperPlugin.Version;
        private static readonly string SettingsTitle = "夜雀点单助手 v" + OrderHelperPlugin.Version + " · 控制面板";
        private string _mainHeaderLine = "";
        private int _mainHeaderGuests = -1;
        private string _cookerTitle = "";
        private string _cookerTitleKey = "";

        private static readonly Dictionary<string, KeyCode> KeyCache = new();
        private static Texture2D _lineTex;

        private void ApplySavedMainRect()
        {
            var rect = new Rect(
                HintPos(OrderHelperPlugin.MainX.Value, _rect.x),
                HintPos(OrderHelperPlugin.MainY.Value, _rect.y),
                HintSize(OrderHelperPlugin.MainW.Value, OrderHelperPlugin.DefaultMainW),
                HintSize(OrderHelperPlugin.MainH.Value, OrderHelperPlugin.DefaultMainH));

            KeepOnScreen(ref rect);
            _rect = rect;
        }

        private void Awake()
        {
            Instance = this;
            ApplySavedMainRect();
        }

        internal void Show()
        {
            if (!_visible)
                OrderHelperPlugin.LogSource.LogInfo("[OrderHelper] 自动打开面板");
            _visible = true;
        }

        /// <summary>
        /// 立刻重采一次（下一帧生效）。
        ///
        /// 为什么需要它：常规采集节拍是 CollectInterval（0.8 秒），这个频率对「客人点单/上菜」
        /// 完全够用，但玩家点了「刷好感 / 按预算推荐」开关时必须马上看到变化，
        /// 等 0.8 秒会以为按钮没反应。所以把「事件」和「轮询」分开：
        /// 轮询降频省 CPU，事件走这条通道保证即时。
        /// </summary>
        internal static void RequestRefresh()
        {
            var inst = Instance;
            if (inst != null) inst._nextRefresh = 0f;
        }

        private void Update()
        {
            try
            {
                Perf.BeginFrame(_guests.Count);

                // 每帧只做「读鼠标 + 挪一块 8×8 挡板 + 改一个 bool」，开销可忽略。
                // 用 OnGUI 上一帧登记的矩形判断指针是否在我们的面板上。
                MouseGuard.Tick();

                // 进游戏后的版本提示：只负责计时，到点在 OnGUI 里画出。
                VersionToast.Tick();

                if (_showSettings && Input.GetMouseButtonDown(1))
                {
                    CloseSettings();
                    return;
                }

                if (++_ticks % 1800 == 0)
                    OrderHelperPlugin.LogSource.LogInfo(
                        $"[OrderHelper] 心跳 frame={Time.frameCount} visible={_visible} guests={_guests.Count}");

                Diagnostics.TryHook();

                // 采集节拍用真实时间（unscaledTime），不用 Time.time（v1.6.5）：
                // 打开料理台 / 酒水柜时游戏会把 timeScale 压到 0，Time.time 立刻冻住，
                // 于是「在柜子界面点一下刷好感开关」这种操作，主面板靠 OnGUI 重画看着像变了，
                // 推荐框却因为采集停摆一直显示旧数据。用 unscaledTime 后暂停中也会刷，
                // 数据刷新本来就不该受游戏内时间缩放影响。
                if (Time.unscaledTime >= _nextRefresh)
                {
                    _nextRefresh = Time.unscaledTime + CollectInterval;
                    var collectStart = Perf.Start();
                    _guests = GuestCollector.Collect(OrderHelperPlugin.ShowNormalGuests.Value);
                    Perf.End("采集", collectStart);
                    EnsureSelection();      // 选中项跟着新数据更新（走的人要撤销选中）
                    _activeGuests.Clear();
                    foreach (var g in _guests)
                        // 已上菜的、以及还没点单的都不显示：后者还没接到单，列出来只是噪音，
                        // 而且它的菜/酒字段都是占位文本，容易让人误以为有单可做。
                        if (!g.Served && !g.NotOrderedYet) _activeGuests.Add(g);
                    _guestsVersion++;
                    _matchCache.Clear();
                }

                if (Time.time >= _nextSnapshot)
                {
                    _nextSnapshot = Time.time + 30f;
                    SnapshotLog();
                }

                // 客人巡查：纯诊断用，默认关（见配置 Diagnostics.GuestAudit）。开着时也别 5 秒一次——
                // 它每次要把全部控制器的类型名/单子状态读一遍，实测 8–26ms，5 秒一次就是稳定的周期性掉帧。
                if (OrderHelperPlugin.GuestAudit.Value && Time.time >= _nextAudit)
                {
                    _nextAudit = Time.time + 30f;
                    var auditStart = Perf.Start();
                    GuestCollector.AuditPass();
                    Perf.End("巡查", auditStart);
                }

                // UI 侦察：等第一位客人进店后再等 3 秒（此时点单头像、订单卡片都建好了），只跑一次。
                if (OrderHelperPlugin.UiProbeOn != null && OrderHelperPlugin.UiProbeOn.Value && !UiProbe.HasRun)
                {
                    if (_guests.Count > 0)
                    {
                        if (_firstGuestAt < 0f) _firstGuestAt = Time.time;
                        else if (Time.time - _firstGuestAt > 3f) UiProbe.RunOnce("首次有客人后 3 秒（自动）");
                    }
                }

                PollFunctionKeys();
                CookerPanelWatcher.Tick();
            }
            catch (Exception e)
            {
                if (!_updateErrorLogged)
                {
                    _updateErrorLogged = true;
                    OrderHelperPlugin.LogSource.LogError("[OrderHelper] Update 异常: " + e);
                }
            }
        }

        private void PollFunctionKeys()
        {
            for (var i = 1; i <= 12; i++)
            {
                var name = "F" + i;
                var legacy = LegacyDown(name);
                var modern = ModernDown(name);
                if (!legacy && !modern) continue;

                OrderHelperPlugin.LogSource.LogInfo($"[OrderHelper] 按键 {name} legacy={legacy} inputsystem={modern}");

                if (name == Normalize(OrderHelperPlugin.ToggleKey.Value, "F8"))
                {
                    _visible = !_visible;
                    OrderHelperPlugin.LogSource.LogInfo($"[OrderHelper] 面板切换 -> {_visible}");
                }
                else if (name == Normalize(OrderHelperPlugin.SettingsKey.Value, "F9"))
                {
                    ToggleSettings();
                }
                else if (name == "F10")
                {
                    UiProbe.Run("F10 手动（手动可按多次，每次重写 dump 与样例图）");
                }
                else if (name == "F11")
                {
                    // 列立绘内部名：看到「[缺] 華扇」就去 config\orderhelper-portraits.txt 补一行
                    GuestPortraits.DumpKeys();
                }
            }
        }

        private static string Normalize(string configured, string fallback) =>
            string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim().ToUpperInvariant();

        private static bool LegacyDown(string keyName)
        {
            if (!KeyCache.TryGetValue(keyName, out var key))
            {
                if (!Enum.TryParse<KeyCode>(keyName, true, out key)) key = KeyCode.None;
                KeyCache[keyName] = key;
            }
            if (key == KeyCode.None) return false;

            try { return Input.GetKeyDown(key); }
            catch { return false; }
        }

        private static bool ModernDown(string keyName)
        {
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb == null) return false;

                return keyName switch
                {
                    "F1" => kb.f1Key.wasPressedThisFrame,
                    "F2" => kb.f2Key.wasPressedThisFrame,
                    "F3" => kb.f3Key.wasPressedThisFrame,
                    "F4" => kb.f4Key.wasPressedThisFrame,
                    "F5" => kb.f5Key.wasPressedThisFrame,
                    "F6" => kb.f6Key.wasPressedThisFrame,
                    "F7" => kb.f7Key.wasPressedThisFrame,
                    "F8" => kb.f8Key.wasPressedThisFrame,
                    "F9" => kb.f9Key.wasPressedThisFrame,
                    "F10" => kb.f10Key.wasPressedThisFrame,
                    "F11" => kb.f11Key.wasPressedThisFrame,
                    "F12" => kb.f12Key.wasPressedThisFrame,
                    _ => false,
                };
            }
            catch
            {
                return false;
            }
        }

        private void HandleDrag()
        {
            var e = Event.current;
            if (e == null) return;

            var titleRect = new Rect(_rect.x, _rect.y, _rect.width, TitleHeight);

            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && titleRect.Contains(e.mousePosition):
                    _dragging = true;
                    _dragOffset = e.mousePosition - _rect.position;
                    e.Use();
                    break;

                case EventType.MouseDrag when _dragging:
                    _rect.position = e.mousePosition - _dragOffset;
                    e.Use();
                    break;

                case EventType.MouseUp when _dragging:
                    _dragging = false;
                    OrderHelperPlugin.SetMainLayout(_rect.x, _rect.y, float.NaN, float.NaN);
                    e.Use();
                    break;
            }
        }

        /// <summary>侧边推荐框的拖动：抓标题栏拖动，拖出面板也没关系。</summary>
        // ---- 布局调节（主面板 + 两个推荐框，控制面板里用） ----
        // kind: 0 = 点单助手窗口，1 = 料理推荐框，2 = 酒水推荐框

        private static Rect DefaultRectOf(int kind)
        {
            switch (kind)
            {
                case 0:
                    return new Rect(OrderHelperPlugin.DefaultMainX, OrderHelperPlugin.DefaultMainY,
                        OrderHelperPlugin.DefaultMainW, OrderHelperPlugin.DefaultMainH);
                case 1:
                    return new Rect(OrderHelperPlugin.DefaultCookerX, OrderHelperPlugin.DefaultCookerY,
                        OrderHelperPlugin.DefaultCookerW, OrderHelperPlugin.DefaultCookerH);
                default:
                    return new Rect(OrderHelperPlugin.DefaultBevX, OrderHelperPlugin.DefaultBevY,
                        OrderHelperPlugin.DefaultBevW, OrderHelperPlugin.DefaultBevH);
            }
        }

        private Rect GetPanelRect(int kind)
        {
            switch (kind)
            {
                case 0: return _rect;
                case 1: return _cookerHintRect;
                default: return _bevHintRect;
            }
        }

        private void SetPanelRect(int kind, Rect rect)
        {
            switch (kind)
            {
                case 0: _rect = rect; break;
                case 1: _cookerHintRect = rect; break;
                default: _bevHintRect = rect; break;
            }
        }

        /// <summary>把矩形写进配置（宽高传 NaN 表示只存位置）。</summary>
        private static void SavePanelLayout(int kind, Rect rect)
        {
            if (kind == 0) OrderHelperPlugin.SetMainLayout(rect.x, rect.y, rect.width, rect.height);
            else OrderHelperPlugin.SetHintLayout(kind == 2, rect.x, rect.y, rect.width, rect.height);
        }

        private static void ResetLayout(int kind)
        {
            var rect = DefaultRectOf(kind);
            SavePanelLayout(kind, rect);

            var overlay = Instance;
            if (overlay != null) overlay.SetPanelRect(kind, rect);
        }

        private void MovePanel(int kind, float dx, float dy)
        {
            var rect = GetPanelRect(kind);
            if (rect.width < 1f) rect = DefaultRectOf(kind);

            rect.x = Mathf.Max(0f, rect.x + dx);
            rect.y = Mathf.Max(0f, rect.y + dy);

            SetPanelRect(kind, rect);
            SavePanelLayout(kind, new Rect(rect.x, rect.y, float.NaN, float.NaN));
        }

        private void SizePanel(int kind, float dw, float dh)
        {
            var rect = GetPanelRect(kind);
            if (rect.width < 1f) rect = DefaultRectOf(kind);

            if (dw != 0f) rect.width = Mathf.Clamp(rect.width + dw, 220f, 1400f);
            if (dh != 0f) rect.height = Mathf.Clamp(rect.height + dh, 120f, 1200f);

            rect.width = Mathf.Round(rect.width);
            rect.height = Mathf.Round(rect.height);

            SetPanelRect(kind, rect);
            SavePanelLayout(kind, rect);
        }

        private void DrawLayoutControls(string label, int kind)
        {
            float x, y, w, h;
            switch (kind)
            {
                case 0:
                    x = OrderHelperPlugin.MainX.Value; y = OrderHelperPlugin.MainY.Value;
                    w = OrderHelperPlugin.MainW.Value; h = OrderHelperPlugin.MainH.Value;
                    break;
                case 1:
                    x = OrderHelperPlugin.CookerHintX.Value; y = OrderHelperPlugin.CookerHintY.Value;
                    w = OrderHelperPlugin.CookerHintW.Value; h = OrderHelperPlugin.CookerHintH.Value;
                    break;
                default:
                    x = OrderHelperPlugin.BevHintX.Value; y = OrderHelperPlugin.BevHintY.Value;
                    w = OrderHelperPlugin.BevHintW.Value; h = OrderHelperPlugin.BevHintH.Value;
                    break;
            }

            GUILayout.Label($"{label}　({x:0},{y:0})　{w:0}×{h:0}");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("← 10")) MovePanel(kind, -10f, 0f);
            if (GUILayout.Button("→ 10")) MovePanel(kind, 10f, 0f);
            if (GUILayout.Button("↑ 10")) MovePanel(kind, 0f, -10f);
            if (GUILayout.Button("↓ 10")) MovePanel(kind, 0f, 10f);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("宽 -20")) SizePanel(kind, -20f, 0f);
            if (GUILayout.Button("宽 +20")) SizePanel(kind, 20f, 0f);
            if (GUILayout.Button("高 -20")) SizePanel(kind, 0f, -20f);
            if (GUILayout.Button("高 +20")) SizePanel(kind, 0f, 20f);
            if (GUILayout.Button("默认")) ResetLayout(kind);
            GUILayout.EndHorizontal();
        }

        private bool HandleHintDrag(ref Rect rect, string id)
        {
            var e = Event.current;
            if (e == null) return false;

            if (_draggingHint == id)
            {
                if (e.type == EventType.MouseDrag && e.button == 0)
                {
                    rect.position = e.mousePosition - _hintDragOffset;
                    e.Use();
                    return true;
                }

                if (e.type == EventType.MouseUp)
                {
                    _draggingHint = null;
                    OrderHelperPlugin.SetHintLayout(id == "beverage", rect.x, rect.y, float.NaN, float.NaN);
                    e.Use();
                }

                return true;
            }

            if (e.type == EventType.MouseDown && e.button == 0 &&
                new Rect(rect.x, rect.y, rect.width, TitleHeight).Contains(e.mousePosition))
            {
                _draggingHint = id;
                _hintDragOffset = e.mousePosition - rect.position;
                e.Use();
                return true;
            }

            return false;
        }

        /// <summary>宽高：配置里 > 0 就用配置，否则用自动算出来的值。</summary>
        private static float HintSize(float configured, float fallback)
        {
            return configured > 0f ? configured : fallback;
        }

        /// <summary>坐标：配置里 >= 0 就用配置，否则自动排布。</summary>
        private static float HintPos(float configured, float fallback)
        {
            return configured >= 0f ? configured : fallback;
        }

        private static void KeepOnScreen(ref Rect rect)
        {
            rect.x = Mathf.Clamp(rect.x, 0f, Mathf.Max(0f, Screen.width - 80f));
            rect.y = Mathf.Clamp(rect.y, 0f, Mathf.Max(0f, Screen.height - 50f));
        }

        private void OnGUI()
        {
            try
            {
                if (!_guiLogged)
                {
                    _guiLogged = true;
                    OrderHelperPlugin.LogSource.LogInfo("[OrderHelper] OnGUI 首次调用");
                }

                _rect.width = HintSize(OrderHelperPlugin.MainW.Value, OrderHelperPlugin.DefaultMainW);
                _rect.height = HintSize(OrderHelperPlugin.MainH.Value, OrderHelperPlugin.DefaultMainH);

                // 这一帧没画出来的面板，就不该算「可点区域」——先清零，画到哪块登记哪块。
                MouseGuard.BeginFrame();

                // ★ 必须放在下面这句早退之前：面板默认不显示，放后面等于永远不弹。
                VersionToast.Draw();

                if (!_visible && !_showSettings && !CookerPanelWatcher.IsOpen && !BeveragePanelWatcher.IsOpen) return;

                var guiStart = Perf.Start();

                // 皮肤第一次 Prepare 要枚举几千个 Image 找素材，那一帧会明显卡一下；
                // 和「画主面板」分开计，才不会被误判成每帧都慢。
                var skinStart = Perf.Start();
                var size = Mathf.Clamp(OrderHelperPlugin.FontSize.Value, 10, 32);
                ApplyFont(size);

                // 皮肤：取素材只在第一次做（几千个 Image 枚举一遍），之后每次只是查一次字典。
                UiSkin.Prepare();
                UiSkin.ApplyTone();
                var paper = UiSkin.PaperBox(size);
                Perf.End("绘制·皮肤", skinStart);

                if (_visible)
                {
                    var mainStart = Perf.Start();
                    HandleDrag();
                    GUI.Box(_rect, WindowTitle, paper);

                    // 主面板整块（含标题栏与拖动态）都算我们的地盘：点这里不该穿透到厨具
                    MouseGuard.SetRect(0, _rect);

                    if (paper != null)
                        UiSkin.DrawTitleRule(_rect, _rect.y + TitleHeight);

                var inner = new Rect(_rect.x + 10f, _rect.y + TitleHeight + 2f,
                                     _rect.width - 20f, _rect.height - TitleHeight - 12f);

                GUILayout.BeginArea(inner);
                GUILayout.BeginVertical();

                if (_mainHeaderGuests != _guests.Count)
                {
                    _mainHeaderGuests = _guests.Count;
                    _mainHeaderLine = "在场客人：" + _guests.Count + "    F8 显示/隐藏 · F9 控制面板";
                }

                GUILayout.Label(_mainHeaderLine);
                GUILayout.Label(PopularInfo.BothText);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("控制面板（F9）"))
                    ToggleSettings();
                GUILayout.Label(OrderHelperPlugin.EnableOneClickCook.Value ? "一键制作：开" : "一键制作：关");
                // 刷好感（v1.6.4）：营业中随手能切，不用按 F9 把画面暂停掉再找开关。
                if (GUILayout.Button(FavorLabel()))
                    OrderHelperPlugin.SetFavorFarm(!OrderHelperPlugin.FavorFarm.Value);
                GUILayout.EndHorizontal();

                _scroll = GUILayout.BeginScrollView(_scroll, false, true);
                DrawGuests();
                GUILayout.EndScrollView();

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("关闭"))
                    _visible = false;

                GUILayout.EndVertical();
                GUILayout.EndArea();
                    Perf.End("绘制·主面板", mainStart);
                }

                var hintStart = Perf.Start();
                DrawCookerHint();
                DrawBeverageHint();
                Perf.End("绘制·推荐框", hintStart);

                var setStart = Perf.Start();
                DrawSettings();
                Perf.End("绘制·控制面板", setStart);

                Perf.End("绘制", guiStart);
            }
            catch (Exception e)
            {
                if (!_guiErrorLogged)
                {
                    _guiErrorLogged = true;
                    OrderHelperPlugin.LogSource.LogError("[OrderHelper] OnGUI 异常: " + e);
                }
            }
        }

        /// <summary>控制面板（F9）：字体、常客、一键制作等开关。</summary>
        private void DrawSettings()
        {
            if (!_showSettings) return;

            const float width = 560f;
            const float height = 620f;

            if (_settingsRect.width < 1f)
                _settingsRect = new Rect((Screen.width - width) * 0.5f,
                                         (Screen.height - height) * 0.5f, width, height);

            var oldBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.94f, 0.78f, 1f);
            var settingsStyle = UiSkin.PaperBox(Mathf.Clamp(OrderHelperPlugin.FontSize.Value, 10, 32));
            GUI.Box(_settingsRect, SettingsTitle, settingsStyle);
            GUI.backgroundColor = oldBg;

            MouseGuard.SetRect(1, _settingsRect);

            if (settingsStyle != null)
                UiSkin.DrawTitleRule(_settingsRect, _settingsRect.y + 26f);

            GUILayout.BeginArea(new Rect(_settingsRect.x + 14f, _settingsRect.y + 30f,
                                         _settingsRect.width - 28f, _settingsRect.height - 44f));
            GUILayout.BeginVertical();

            _settingsScroll = GUILayout.BeginScrollView(_settingsScroll, false, true);

            GUILayout.Label("【显示】");
            var size = Mathf.Clamp(OrderHelperPlugin.FontSize.Value, 12, 32);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"字体大小：{size}   （12 - 32）", GUILayout.Width(_settingsRect.width - 160f));
            if (GUILayout.Button("－ 2", GUILayout.Width(56f)))
                OrderHelperPlugin.SetFontSize(size - 2);
            if (GUILayout.Button("＋ 2", GUILayout.Width(56f)))
                OrderHelperPlugin.SetFontSize(size + 2);
            GUILayout.EndHorizontal();

            if (GUILayout.Button(OrderHelperPlugin.ShowNormalGuests.Value
                    ? "常客：显示中（点这里改为隐藏）"
                    : "常客：隐藏中（点这里改为显示）"))
                OrderHelperPlugin.SetShowNormalGuests(!OrderHelperPlugin.ShowNormalGuests.Value);

            GUILayout.Space(6f);
            GUILayout.Label("【推荐框】");
            if (GUILayout.Button(OrderHelperPlugin.EnableOneClickCook.Value
                    ? "一键制作：开启（点击推荐条目直接开做）"
                    : "一键制作：关闭（点击推荐条目不再放食材）"))
                OrderHelperPlugin.SetOneClickCook(!OrderHelperPlugin.EnableOneClickCook.Value);
            GUILayout.Label("关闭后推荐框照常显示推荐，只是点击不会把食材放上料理台。");

            GUILayout.Space(6f);
            GUILayout.Label("【推荐口径】");
            if (GUILayout.Button(OrderHelperPlugin.RespectFund.Value
                    ? "按预算推荐：开启（这一单不超过客人手上的钱）"
                    : "按预算推荐：关闭（不看客人钱包，可能超预算黑评）"))
                OrderHelperPlugin.SetRespectFund(!OrderHelperPlugin.RespectFund.Value);
            if (GUILayout.Button(OrderHelperPlugin.FavorFarm.Value
                    ? "刷稀客好感：开启（先保粉评，再省钱吃满单）"
                    : "刷稀客好感：关闭（正常口径：能花就花、利润优先）"))
                OrderHelperPlugin.SetFavorFarm(!OrderHelperPlugin.FavorFarm.Value);
            GUILayout.Label("刷好感只对稀客生效，并依赖「按预算推荐」开着（要靠读客人钱包才留得出后面的钱）。");
            if (OrderHelperPlugin.FavorFarm.Value && !OrderHelperPlugin.RespectFund.Value)
                GUILayout.Label("★ 现在「按预算推荐」是关的，刷好感不会起作用。");
            else
                GUILayout.Label("开启后客人状态行会标「刷好感」，主面板顶部也有一个快捷开关。");

            GUILayout.Space(6f);
            GUILayout.Label("【窗口布局】");
            DrawLayoutControls("点单助手窗口", 0);
            DrawLayoutControls("料理推荐框", 1);
            DrawLayoutControls("酒水推荐框", 2);
            GUILayout.Label("也可以直接拖标题栏挪位置，松手会记住。");

            GUILayout.Space(6f);
            GUILayout.Label("【其它】");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("导出客人信息到日志"))
                Diagnostics.Dump();
            if (GUILayout.Button("恢复默认"))
            {
                OrderHelperPlugin.SetFontSize(20);
                OrderHelperPlugin.SetShowNormalGuests(false);
                OrderHelperPlugin.SetOneClickCook(true);
                OrderHelperPlugin.SetRespectFund(true);
                OrderHelperPlugin.SetFavorFarm(false);
                ResetLayout(0);
                ResetLayout(1);
                ResetLayout(2);
            }
            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();

            GUILayout.Label("F8 显示 / 隐藏主面板　·　F9 开关本面板　·　右键 关闭本面板");
            if (GUILayout.Button("关闭控制面板"))
                CloseSettings();

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        /// <summary>
        /// 刷好感按钮的文案（v1.6.4）。★ 只返回常量字面量：OnGUI 一帧两趟，
        /// 在这里拼串等于每帧白送两坨垃圾，而这行按钮是每帧都画的。
        /// </summary>
        private static string FavorLabel()
        {
            return OrderHelperPlugin.FavorFarm != null && OrderHelperPlugin.FavorFarm.Value
                ? "刷好感：开"
                : "刷好感：关";
        }

        private void ToggleSettings()
        {
            if (_showSettings)
            {
                CloseSettings();
                return;
            }

            _showSettings = true;
            SettingsVisible = true;
            BlockUI();
            TryPauseTime();
            OrderHelperPlugin.LogSource.LogInfo(
                $"[OrderHelper] 打开控制面板（timeScale={Time.timeScale}，游戏输入已阻断）");
        }

        private void CloseSettings()
        {
            _showSettings = false;
            SettingsVisible = false;
            if (_settingsPaused)
            {
                Time.timeScale = _prevTimeScale;
                _settingsPaused = false;
            }

            UnblockUI();
            OrderHelperPlugin.LogSource.LogInfo(
                $"[OrderHelper] 关闭控制面板（timeScale={Time.timeScale}，输入已恢复）");
        }

        private void TryPauseTime()
        {
            try
            {
                if (_settingsPaused) return;

                if (Time.timeScale < 0.999f)
                {
                    OrderHelperPlugin.LogSource.LogInfo(
                        $"[OrderHelper] 游戏已处于暂停状态 timeScale={Time.timeScale}，不重复暂停");
                    return;
                }

                var panel = AnyGamePanelActive();
                if (panel != null)
                {
                    OrderHelperPlugin.LogSource.LogInfo(
                        "[OrderHelper] 场景中有面板处于打开状态(" + panel + ")，为安全起见不暂停时间");
                    return;
                }

                _prevTimeScale = Time.timeScale;
                Time.timeScale = 0f;
                _settingsPaused = true;
                OrderHelperPlugin.LogSource.LogInfo("[OrderHelper] 已暂停游戏时间");
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 暂停时间失败: " + e.Message);
            }
        }

        private static string AnyGamePanelActive()
        {
            try
            {
                if (CookerPanelWatcher.IsOpen) return "CookerPanel";

                var all = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
                foreach (var mb in all)
                {
                    if (mb == null) continue;
                    var go = mb.gameObject;
                    if (go == null || !go.activeInHierarchy) continue;
                    var name = go.name ?? string.Empty;
                    if (name.IndexOf("(Clone)", StringComparison.Ordinal) < 0) continue;
                    var interesting =
                        name.IndexOf("NoteBook", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (name.IndexOf("Selection", StringComparison.OrdinalIgnoreCase) >= 0 &&
                         name.IndexOf("CookerModule", StringComparison.OrdinalIgnoreCase) < 0);
                    if (!interesting) continue;

                    if (GetMaxAlpha(go) <= 0.01f) continue;

                    return name;
                }
            }
            catch
            {
            }

            return null;
        }

        private static float GetMaxAlpha(UnityEngine.GameObject go)
        {
            try
            {
                var groups = go.GetComponentsInChildren<CanvasGroup>(true);
                if (groups == null || groups.Length == 0) return 1f;
                var max = 0f;
                for (var i = 0; i < groups.Length; i++)
                {
                    var g = groups[i];
                    if (g != null && g.alpha > max) max = g.alpha;
                }

                return max;
            }
            catch
            {
                return 1f;
            }
        }

        private static bool IsPointerAction(UnityEngine.InputSystem.InputAction action)
        {
            try
            {
                var map = action.actionMap;
                if (map != null && !string.IsNullOrEmpty(map.name) &&
                    map.name.IndexOf("UI", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                var name = action.name ?? string.Empty;
                if (name.IndexOf("Point", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (name.IndexOf("Mouse", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (name.IndexOf("Cursor", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                return false;
            }
            catch
            {
                return false;
            }
        }

        private void BlockUI()
        {
            try
            {
                _prevCursorVisible = Cursor.visible;
                _prevCursorLock = Cursor.lockState;
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 光标处理异常: " + e.Message);
            }

            _blockedSystems.Clear();

            try
            {
                _blockedActions.Clear();
                var actions = UnityEngine.InputSystem.InputSystem.ListEnabledActions();
                for (var i = 0; i < actions.Count; i++)
                {
                    var action = actions[i];
                    if (action == null) continue;
                    if (IsPointerAction(action)) continue;
                    action.Disable();
                    _blockedActions.Add(action);
                }

                OrderHelperPlugin.LogSource.LogInfo($"[OrderHelper] 已暂停 InputSystem 动作 {_blockedActions.Count} 个");
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 暂停 InputSystem 异常: " + e.Message);
            }

            try
            {
                _playerInput = UnityEngine.Object.FindObjectOfType<NightScene.Input.WorkScenePlayerInputGenerator>();
                if (_playerInput == null)
                {
                    var all = UnityEngine.Object.FindObjectsOfType<NightScene.Input.WorkScenePlayerInputGenerator>();
                    if (all != null && all.Length > 0) _playerInput = all[0];
                }
                if (_playerInput != null)
                {
                    _playerInput.ForbidMovement();
                    OrderHelperPlugin.LogSource.LogInfo("[OrderHelper] 已禁止角色移动");
                }
                else
                {
                    OrderHelperPlugin.LogSource.LogInfo("[OrderHelper] 未找到角色输入组件（可能不在营业场景）");
                }
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 禁止移动异常: " + e.Message);
            }
        }

        private void UnblockUI()
        {
            try
            {
                foreach (var es in _blockedSystems)
                {
                    if (es != null) es.enabled = true;
                }
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] UnblockUI 异常: " + e.Message);
            }

            _blockedSystems.Clear();

            try
            {
                foreach (var action in _blockedActions)
                {
                    if (action != null) action.Enable();
                }
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 恢复 InputSystem 异常: " + e.Message);
            }

            _blockedActions.Clear();

            try
            {
                if (_playerInput != null)
                {
                    _playerInput.RecoverMovement();
                    OrderHelperPlugin.LogSource.LogInfo("[OrderHelper] 已恢复角色移动");
                }
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 恢复移动异常: " + e.Message);
            }

            _playerInput = null;

            try
            {
                Cursor.visible = _prevCursorVisible;
                Cursor.lockState = _prevCursorLock;
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 恢复光标异常: " + e.Message);
            }
        }

        private void SaveHintLayouts()
        {
            if (_rect.width >= 1f)
                OrderHelperPlugin.SetMainLayout(_rect.x, _rect.y, _rect.width, _rect.height);

            if (_cookerHintRect.width >= 1f)
                OrderHelperPlugin.SetHintLayout(false, _cookerHintRect.x, _cookerHintRect.y,
                    _cookerHintRect.width, _cookerHintRect.height);

            if (_bevHintRect.width >= 1f)
                OrderHelperPlugin.SetHintLayout(true, _bevHintRect.x, _bevHintRect.y,
                    _bevHintRect.width, _bevHintRect.height);
        }

        private void OnDestroy()
        {
            SaveHintLayouts();
            UnblockUI();
            if (_settingsPaused)
            {
                Time.timeScale = _prevTimeScale <= 0f ? 1f : _prevTimeScale;
                _settingsPaused = false;
            }
        }

        private void ApplyFont(int size)
        {
            if (_font == null || _fontSize != size)
            {
                _fontSize = size;
                _font = ResolveFont(size);
            }

            if (_font == null) return;

            GUI.skin.font = _font;
            GUI.skin.label.fontSize = size;
            GUI.skin.button.fontSize = size;
            GUI.skin.box.fontSize = size;
        }

        private Font ResolveFont(int size) => SharedFont(size);

        private static Font _sharedFont;
        private static bool _sharedFontLogged;

        /// <summary>
        /// 解析能显示中文的字体（TMP 源字体 → 系统雅黑 → null）。
        ///
        /// 做成 static + 全局缓存：面板和「进游戏版本提示」共用一份。
        /// 版本提示画在 OnGUI 早退之前（那时 ApplyFont 还没跑到），如果它自己去建字体，
        /// 就会多一次 CreateDynamicFontFromOSFont，而且两条路径的字体可能不一致。
        /// </summary>
        internal static Font SharedFont(int size)
        {
            if (_sharedFont != null) return _sharedFont;

            try
            {
                var asset = TMPro.TMP_Settings.defaultFontAsset;
                if (asset != null && asset.sourceFontFile != null)
                {
                    _sharedFont = asset.sourceFontFile;
                    if (!_sharedFontLogged)
                    {
                        _sharedFontLogged = true;
                        OrderHelperPlugin.LogSource.LogInfo(
                            "[OrderHelper] 使用游戏 TMP 源字体: " + asset.sourceFontFile.name);
                    }
                    return _sharedFont;
                }
            }
            catch (Exception e)
            {
                if (!_sharedFontLogged)
                {
                    _sharedFontLogged = true;
                    OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 读取 TMP 字体失败，改用系统字体: " + e.Message);
                }
            }

            try
            {
                if (!_sharedFontLogged)
                {
                    _sharedFontLogged = true;
                    OrderHelperPlugin.LogSource.LogInfo("[OrderHelper] 使用系统字体: Microsoft YaHei UI");
                }
                _sharedFont = Font.CreateDynamicFontFromOSFont(
                    new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei" }, size);
            }
            catch
            {
                _sharedFont = null;
            }

            return _sharedFont;
        }

        /// <summary>
        /// 厨具面板打开时，在面板旁边显示「本厨具能做的推荐菜」。只读，不改原生 UI。
        /// 框宽固定 380，框高按在场客人数估算；客人多时在框内用滚轮滚动。
        /// </summary>
        private void DrawCookerHint()
        {
            if (!CookerPanelWatcher.IsOpen)
            {
                _hintLogged = false;
                _injected = false;
                return;
            }
            if (_activeGuests.Count == 0) return;

            var width = HintSize(OrderHelperPlugin.CookerHintW.Value, OrderHelperPlugin.DefaultCookerW);
            var height = HintSize(OrderHelperPlugin.CookerHintH.Value,
                Mathf.Min(520f, 110f + _activeGuests.Count * 150f));

            if (_cookerHintRect.width < 0f)
            {
                // 料理在左、酒水在右，两块分开，避免叠在一起。
                var defaultX = Mathf.Max(8f, Screen.width - width * 2f - 24f);
                var defaultY = Mathf.Clamp(90f, 8f, Mathf.Max(8f, Screen.height - height - 8f));
                _cookerHintRect = new Rect(
                    HintPos(OrderHelperPlugin.CookerHintX.Value, defaultX),
                    HintPos(OrderHelperPlugin.CookerHintY.Value, defaultY),
                    width, height);
            }

            _cookerHintRect.width = width;
            _cookerHintRect.height = height;

            KeepOnScreen(ref _cookerHintRect);
            HandleHintDrag(ref _cookerHintRect, "cooker");

            var x = _cookerHintRect.x;
            var y = _cookerHintRect.y;

            // 诊断用：每 5 秒把所有客人的状态灌进日志。默认关——这条纯噪音，
            // 而且它每次都要拼一串字符串（开着锅的时候每 5 秒一次）。
            if (OrderHelperPlugin.SnapshotLog != null && OrderHelperPlugin.SnapshotLog.Value &&
                Time.time >= _hintGuestLogTime)
            {
                _hintGuestLogTime = Time.time + 5f;
                var sbh = new System.Text.StringBuilder();
                foreach (var g in _guests)
                    sbh.Append(g.Name).Append("(rem=").Append(g.RemainOrders).Append(",all=").Append(g.AllOrders)
                        .Append(",food=").Append(g.Food).Append(") ");
                OrderHelperPlugin.LogSource.LogInfo("[OrderHelper] 推荐框客人 " + sbh);
            }

            if (!_hintLogged)
            {
                _hintLogged = true;
                OrderHelperPlugin.LogSource.LogInfo(
                    $"[OrderHelper] 推荐框绘制 guests={_guests.Count} pos=({x:0},{y:0}) size=({width:0}x{height:0}) " +
                    $"cooker={CookerPanelWatcher.CookerName} special={CookerPanelWatcher.SpecialCooker} " +
                    $"registered={CookerPanelWatcher.RegisteredRecipeCount}");
            }

            // 标题只在厨具或开关变化时重建（原来每帧拼一次）。
            var titleKey = CookerPanelWatcher.CookerName + "|" + OrderHelperPlugin.EnableOneClickCook.Value;
            if (titleKey != _cookerTitleKey)
            {
                _cookerTitleKey = titleKey;
                _cookerTitle = "【料理】推荐（" +
                               (string.IsNullOrEmpty(CookerPanelWatcher.CookerName) ? "当前厨具" : CookerPanelWatcher.CookerName) +
                               "）" + (OrderHelperPlugin.EnableOneClickCook.Value ? "" : "  一键制作：关");
            }

            var title = _cookerTitle;

            // 第一次画这个面板时，把面板里没有的推荐菜注入原生列表，让它们变成可点的格子。
            if (!_injected && _guests.Count > 0)
            {
                _injected = true;
                var toInject = new List<Recipe>();
                foreach (var guest in _guests)
                {
                    if (guest.Served) continue;
                    var pick = 0;
                    foreach (var dish in MatchesFor(guest))
                    {
                        if (dish.Recipe == null) continue;
                        if (pick++ >= 2) break;
                        toInject.Add(dish.Recipe);
                    }
                }

                if (toInject.Count > 0)
                {
                    var injectStart = Perf.Start();
                    var added = CookerPanelWatcher.InjectRecipes(toInject, "推荐");
                    Perf.End("注入", injectStart);
                    OrderHelperPlugin.LogSource.LogInfo($"[OrderHelper] 推荐菜注入结果 added={added} 请求={toInject.Count}");
                }
            }

            var oldBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.86f, 0.62f, 1f);
            var cookerStyle = UiSkin.PaperBox(Mathf.Clamp(OrderHelperPlugin.FontSize.Value, 10, 32));
            var cookerRect = new Rect(x, y, width, height);
            GUI.Box(cookerRect, title, cookerStyle);
            GUI.backgroundColor = oldBg;

            MouseGuard.SetRect(2, cookerRect);

            if (cookerStyle != null)
                UiSkin.DrawTitleRule(cookerRect, cookerRect.y + TitleHeight);

            // 顶部常驻头像条：所有还能点菜的客人并排一行，点谁就切到谁。
            // ★ 头像条不跟着推荐列表滚动 —— 锅开着的时候随时能点，不用先滚回顶部。
            var focusGuest = FocusGuest();
            var areaLeft = x + 6f;
            var areaWidth = width - 12f;
            var areaTop = y + TitleHeight + 2f;
            var stripH = DrawHintStrip(_activeGuests, focusGuest, areaLeft, areaTop, areaWidth);

            var bodyTop = areaTop + stripH;
            var bodyH = Mathf.Max(24f, height - TitleHeight - 8f - stripH);

            GUILayout.BeginArea(new Rect(areaLeft, bodyTop, areaWidth, bodyH));
            _cookerHintScroll = GUILayout.BeginScrollView(_cookerHintScroll, false, true);
            GUILayout.BeginVertical();
            GUILayout.Label(PopularInfo.FoodLine);

            if (focusGuest == null)
            {
                GUILayout.Label("（没有在等菜的客人）");
                GUILayout.EndVertical();
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }

            // ★ 变量别叫 guest：上面注入那段 foreach 已经用过这个名字，C# 不允许外层再声明同名的。
            var cur = focusGuest;
            var matched = MatchesFor(cur);

            GUILayout.Label(cur.HintTitleLine);

            if (matched.Count == 0)
            {
                if (CookerPanelWatcher.SpecialCooker)
                {
                    var shown = 0;
                    foreach (var dish in cur.Candidates)
                    {
                        if (shown++ >= 6) break;
                        OrderHelperPlugin.LogSource.LogInfo(
                            "[OrderHelper] 匹配失败 dish=" + dish.Name +
                            " food=" + (dish.Recipe != null ? dish.Recipe.FoodID : -1) +
                            " cooker=" + dish.Cooker +
                            " inPanel=" + (dish.Recipe != null && CookerPanelWatcher.CanCookHere(dish.Recipe.FoodID)));
                    }
                }

                GUILayout.Label("    本厨具无匹配");
                GUILayout.EndVertical();
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }

            // 稀客的粉评筛选已经在 MatchesFor 里做过了（结果跟着缓存走），这里直接用。
            foreach (var dish in matched)
            {
                if (string.IsNullOrEmpty(dish.ButtonLabel))
                    GuestCollector.PrepareLabel(dish, true);

                var label = string.IsNullOrEmpty(dish.FullLabel) ? dish.ButtonLabel : dish.FullLabel;
                if (GUILayout.Button(label, GUILayout.ExpandHeight(true)))
                {
                    if (OrderHelperPlugin.EnableOneClickCook.Value)
                        CookerPanelWatcher.SelectIngredients(dish.Recipe, dish.Plan?.Ids);
                    else
                        OrderHelperPlugin.LogSource.LogInfo("[OrderHelper] 一键制作已关闭，忽略点击: " + dish.Name);
                }
            }

            GUILayout.EndVertical();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>画一条水平线：高度、不透明度、左右缩进可调。</summary>
        private static void DrawLine(float height, float alpha, float indent)
        {
            try
            {
                GUILayout.Space(3f);

                if (_lineTex == null)
                {
                    _lineTex = new Texture2D(1, 1);
                    _lineTex.SetPixel(0, 0, Color.white);
                    _lineTex.Apply();
                }

                var rect = GUILayoutUtility.GetRect(10f, height, GUILayout.ExpandWidth(true));
                if (indent > 0f)
                {
                    rect.x += indent;
                    rect.width = Mathf.Max(10f, rect.width - indent * 2f);
                }

                var old = GUI.color;
                GUI.color = UiSkin.Line(alpha);
                GUI.DrawTexture(rect, _lineTex, ScaleMode.StretchToFill);
                GUI.color = old;

                GUILayout.Space(3f);
            }
            catch
            {
                GUILayout.Label("----------------------");
            }
        }

        /// <summary>同一客人内部 酒水 / 菜品 之间的分割：细、淡、两侧缩进。</summary>
        private static void SectionDivider()
        {
            DrawLine(1f, 0.35f, 22f);
        }

        /// <summary>
        /// 当前厨具下，这位客人可以推荐的菜。
        /// - 普通厨具：从客人候选里挑菜、按厨具名过滤，再把面板里没有的注入面板；
        /// - 特殊厨具（三位一体等）：它没有固定名字可匹配，改为按面板自带菜谱反推出的烹饪方式，
        ///   在「全部已解锁菜谱」里挑点单标签对得上的，再注入面板——这样推荐的是最符合的，而不是面板里恰好有的。
        /// </summary>
        /// <summary>带缓存的 MatchedFor：同一批客人 + 同一个厨具面板状态下只算一次。</summary>
        private List<DishCandidate> MatchesFor(GuestView guest)
        {
            if (guest == null) return new List<DishCandidate>();

            var index = _guests.IndexOf(guest);
            if (index < 0) return FilterForDisplay(guest, MatchedFor(guest));

            if (_matchCacheGuests != _guestsVersion ||
                _matchCachePanel != CookerPanelWatcher.Version ||
                _matchCache.Count != _guests.Count)
            {
                _matchCache.Clear();
                for (var i = 0; i < _guests.Count; i++) _matchCache.Add(null);
                _matchCacheGuests = _guestsVersion;
                _matchCachePanel = CookerPanelWatcher.Version;
            }

            var cached = _matchCache[index];
            if (cached != null) return cached;

            cached = OrderLikePairs(guest, FilterForDisplay(guest, MatchedFor(guest)));
            _matchCache[index] = cached;
            return cached;
        }

        /// <summary>
        /// 两道候选是不是「同一道菜」（v1.6.5）。
        ///
        /// 不能只比引用：普通厨具的推荐直接复用 `guest.Candidates` 里的对象，引用是一致的；
        /// 但三位一体这类特殊厨具会在 `MatchedFor` 里用 `RecipeIndex.Find` 重新生成一批
        /// `DishCandidate`，同一道菜是两个实例——按引用比永远不相等，同步逻辑就静默失效。
        /// 所以先比菜谱 ID（最可靠），没有菜谱信息时退回比菜名。
        /// </summary>
        private static bool SameDish(DishCandidate a, DishCandidate b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;

            if (a.Recipe != null && b.Recipe != null && a.Recipe.FoodID > 0 && b.Recipe.FoodID > 0)
                return a.Recipe.FoodID == b.Recipe.FoodID;

            return !string.IsNullOrEmpty(a.Name) && string.Equals(a.Name, b.Name, StringComparison.Ordinal);
        }

        /// <summary>
        /// 让料理推荐框的菜顺序跟主面板的「最佳搭配」一致（v1.6.4 起）。
        ///
        /// 主面板展示 `Pairs`，已经按当前口径排好（普通 / 按预算 / 刷好感三种不同）；
        /// 而推荐框原来直接用 `Candidates` 的原顺序，口径不同步。玩家切了「刷好感」，
        /// 主面板变了、柜子旁边的推荐却一动不动，看着像开关没生效。
        /// 这里把 Pairs 里的菜按主面板顺序提到前面，其余保留原顺序
        /// （推荐框会按厨具过滤，主面板那几道本厨具做不了的就自然不在列表里）。
        /// ★ 只在缓存重建时跑（0.5 秒一次），不在 OnGUI 每帧跑。
        /// ★ 比对用 `SameDish`（菜谱 ID），因为特殊厨具的候选是另一批实例（v1.6.5）。
        /// </summary>
        private static List<DishCandidate> OrderLikePairs(GuestView guest, List<DishCandidate> dishes)
        {
            var pairs = guest?.Pairs;
            if (pairs == null || pairs.Count == 0 || dishes == null || dishes.Count < 2) return dishes;

            var head = new List<DishCandidate>(pairs.Count);
            foreach (var pair in pairs)
            {
                var want = pair?.Dish;
                if (want == null) continue;

                // 用列表里真正那一个实例（同菜不同实例时，它的 Plan/评分 才是这里算好的）。
                var hit = FindSame(dishes, want);
                if (hit == null || FindSame(head, hit) != null) continue;
                head.Add(hit);
            }

            if (head.Count == 0) return dishes;

            var rest = new List<DishCandidate>(dishes.Count);
            foreach (var dish in dishes)
                if (FindSame(head, dish) == null) rest.Add(dish);

            head.AddRange(rest);
            return head;
        }

        /// <summary>在列表里找「同一道菜」的那一项（找不到返回 null）。列表很短（≤40），线性扫足够。</summary>
        private static DishCandidate FindSame(List<DishCandidate> list, DishCandidate want)
        {
            if (list == null || want == null) return null;
            for (var i = 0; i < list.Count; i++)
                if (SameDish(list[i], want)) return list[i];
            return null;
        }

        /// <summary>
        /// 推荐框真正要显示的几道菜：稀客优先合计 ≥4（能粉评），一道都凑不出就退回命中最多的那道。
        /// ★ 放在这里而不是 OnGUI 里：结果跟着 MatchesFor 一起缓存，
        ///   每帧就不用再 FindAll + new List（原来是每帧每位客人一套）。
        /// </summary>
        private static List<DishCandidate> FilterForDisplay(GuestView guest, List<DishCandidate> matched)
        {
            if (matched == null) return new List<DishCandidate>();
            if (matched.Count == 0 || !guest.IsSpecial) return matched;

            var pink = new List<DishCandidate>();
            foreach (var dish in matched)
                if (dish != null && dish.TotalPoints >= 4) pink.Add(dish);

            if (pink.Count > 0) return pink;

            var top = GuestCollector.BestHit(matched);
            if (top != null) pink.Add(top);
            return pink;
        }

        private static List<DishCandidate> MatchedFor(GuestView guest)
        {
            var matched = new List<DishCandidate>();
            if (guest == null) return matched;

            if (!CookerPanelWatcher.SpecialCooker)
            {
                foreach (var dish in guest.Candidates)
                    if (MatchesCooker(dish)) matched.Add(dish);
                return matched;
            }

            // 和普通厨具同一套：先按点单标签算出「最推荐的 4 道」，三位一体只保留自己能做的那几道。
            // 客人还没点单（FoodTagRequest<0）就不推荐，避免没点单就塞建议。
            if (guest.FoodTagRequest < 0) return matched;

            try
            {
                // 池子放大到 40 道再按厨具过滤，避免「前 4 名全都做不了」时整块推荐空掉。
                var pool = RecipeIndex.Find(
                    guest.FoodTagRequest, false, guest.LikeFoodTags, guest.HateFoodTags, 40);

                // 这批候选没经过 GuestCollector，评价/排序要在这里补算，
                // 否则会显示「空评级 + 合计0分」，和主面板对不上。
                // 酒水传副本，避免把 view 里已算好的酒水评分再覆盖一遍。
                GuestCollector.ApplyRatingsToCandidates(
                    pool, new List<DishCandidate>(guest.BeverageCandidates),
                    guest.LikeFoodTags, guest.HateFoodTags, guest);

                // 主面板搭配里的菜优先占位（v1.6.5）：特殊厨具这里只取前 4 道，
                // 若主面板推荐的那道排在池子第 10 名，不先挑出来就会被截掉 ——
                // 玩家看到的就是「主面板变了、柜子旁边的推荐没变」。
                // ★ 池子是 RecipeIndex.Find 新生成的一批实例，只能按菜谱 ID 认（SameDish）。
                foreach (var pair in guest.Pairs)
                {
                    if (matched.Count >= 4) break;
                    var want = pair?.Dish;
                    if (want == null) continue;

                    var hit = FindSame(pool, want);
                    if (hit == null || hit.Recipe == null) continue;
                    if (!CookerPanelWatcher.Supports(hit.Recipe)) continue;
                    if (FindSame(matched, hit) != null) continue;
                    matched.Add(hit);
                }

                foreach (var dish in pool)
                {
                    if (matched.Count >= 4) break;
                    if (dish.Recipe == null) continue;
                    if (!CookerPanelWatcher.Supports(dish.Recipe)) continue;
                    if (FindSame(matched, dish) != null) continue;
                    matched.Add(dish);
                }
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 特殊厨具推荐失败: " + e.Message);
            }

            return matched;
        }

        private static bool MatchesCooker(DishCandidate dish)
        {
            if (dish == null) return false;

            // 面板自己登记了这道菜的格子 = 这个厨具（含「三位一体」这类特殊厨具）肯定能做它。
            if (dish.Recipe != null && CookerPanelWatcher.CanCookHere(dish.Recipe.FoodID))
                return true;

            // 面板还没登记（刚打开、格子还在生成）时退回按名字匹配。
            if (string.IsNullOrEmpty(dish.Cooker)) return false;
            var panelCooker = CookerPanelWatcher.CookerName;
            if (string.IsNullOrEmpty(panelCooker)) return true;
            if (string.Equals(dish.Cooker, panelCooker, StringComparison.Ordinal)) return true;
            return panelCooker.Contains(dish.Cooker) || dish.Cooker.Contains(panelCooker);
        }

        private void DrawBeverageHint()
        {
            if (!BeveragePanelWatcher.IsOpen)
            {
                _bevHintLogged = false;
                return;
            }
            if (_activeGuests.Count == 0) return;

            var width = HintSize(OrderHelperPlugin.BevHintW.Value, OrderHelperPlugin.DefaultBevW);
            var height = HintSize(OrderHelperPlugin.BevHintH.Value,
                Mathf.Min(620f, 90f + _activeGuests.Count * 120f));

            if (_bevHintRect.width < 0f)
            {
                var defaultX = Screen.width - width - 12f;
                var defaultY = Mathf.Clamp(90f, 8f, Mathf.Max(8f, Screen.height - height - 8f));
                _bevHintRect = new Rect(
                    HintPos(OrderHelperPlugin.BevHintX.Value, defaultX),
                    HintPos(OrderHelperPlugin.BevHintY.Value, defaultY),
                    width, height);
            }

            _bevHintRect.width = width;
            _bevHintRect.height = height;

            KeepOnScreen(ref _bevHintRect);
            HandleHintDrag(ref _bevHintRect, "beverage");

            var x = _bevHintRect.x;
            var y = _bevHintRect.y;

            if (!_bevHintLogged)
            {
                _bevHintLogged = true;
                OrderHelperPlugin.LogSource.LogInfo(
                    $"[OrderHelper] 酒水推荐框绘制 guests={_guests.Count} pos=({x:0},{y:0}) size=({width:0}x{height:0})");
            }

            var oldBg = GUI.backgroundColor;
            // 原来这里用偏蓝的底色区分酒水框；换成纸条底之后蓝色会把纸染脏，跟着改暖色调。
            GUI.backgroundColor = UiSkin.Ready
                ? new Color(1f, 0.90f, 0.70f, 1f)
                : new Color(0.72f, 0.88f, 1f, 1f);
            var bevStyle = UiSkin.PaperBox(Mathf.Clamp(OrderHelperPlugin.FontSize.Value, 10, 32));
            var bevRect = new Rect(x, y, width, height);
            GUI.Box(bevRect, "【酒水】推荐", bevStyle);
            GUI.backgroundColor = oldBg;

            MouseGuard.SetRect(3, bevRect);

            if (bevStyle != null)
                UiSkin.DrawTitleRule(bevRect, bevRect.y + TitleHeight);
            // 同样：顶部常驻头像条，点谁就切到谁。
            var focusGuest = FocusGuest();
            var areaLeft = x + 6f;
            var areaWidth = width - 12f;
            var areaTop = y + TitleHeight + 2f;
            var stripH = DrawHintStrip(_activeGuests, focusGuest, areaLeft, areaTop, areaWidth);

            var bodyTop = areaTop + stripH;
            var bodyH = Mathf.Max(24f, height - TitleHeight - 8f - stripH);

            GUILayout.BeginArea(new Rect(areaLeft, bodyTop, areaWidth, bodyH));
            _bevHintScroll = GUILayout.BeginScrollView(_bevHintScroll, false, true);
            GUILayout.BeginVertical();
            GUILayout.Label(PopularInfo.BevLine);

            if (focusGuest == null)
            {
                GUILayout.Label("（没有在等酒的客人）");
                GUILayout.EndVertical();
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }

            // 该显示哪几杯已经在收集阶段算好了（GuestView.HintDrinks），这里不再每帧筛选。
            var guest = focusGuest;
            GUILayout.Label(guest.HintTitleLine);

            var drinkable = guest.HintDrinks;
            if (drinkable == null || drinkable.Count == 0)
            {
                GUILayout.Label(guest.NoDrinkLine);
                GUILayout.EndVertical();
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }

            var shown = 0;
            foreach (var drink in drinkable)
            {
                if (shown++ >= 6) break;
                if (string.IsNullOrEmpty(drink.ButtonLabel))
                    GuestCollector.PrepareLabels(new List<DishCandidate> { drink }, false);

                var label = drink.ButtonLabel;
                if (GUILayout.Button(label))
                {
                    if (OrderHelperPlugin.EnableOneClickCook.Value)
                        BeveragePanelWatcher.TrySelectBeverage(drink.Sellable, drink.Name);
                    else
                        OrderHelperPlugin.LogSource.LogInfo("[OrderHelper] 一键制作已关闭，忽略酒水点击: " + drink.Name);
                }
            }

            GUILayout.EndVertical();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawGuests()
        {
            // 只列「已经点单、还没上完菜」的客人：未点单的人连单都没有，
            // 列出来既占位置又容易让人以为有菜可做。
            if (_activeGuests.Count == 0)
            {
                GUILayout.Label(_guests.Count == 0
                    ? "（营业场景里没有客人，或还没开始营业）"
                    : "（客人还没点单，点单后这里会出现）");
                return;
            }

            // 外层 OnGUI 已经有滚动视图，这里不要再套一层。
            //
            // 布局取舍（2026-10-23 第二轮，用户要求）：顶部**只排头像、不排文字**。
            // 之前是「头像 + 两行文字」每行一位，文字太占宽，一行只放得下一个头像；
            // 现在头像单独横排（一行能排几个排几个，放不下换行），点谁在下面展开谁的完整文字。
            GUILayout.Label(ListTip);

            DrawAvatarRows(_activeGuests, _selected, _rect.width - 40f);

            SectionDivider();

            if (_selected == null)
            {
                GUILayout.Label("（点上面的头像，展开这位客人的推荐）");
                return;
            }

            // 详情区标题也带头像：点了上面的头像跳到这里，还能看到是同一位。
            GUILayout.BeginHorizontal();
            DrawAvatar(_selected);
            GUILayout.BeginVertical();
            GUILayout.Label(_selected.MainTitleLine);
            if (!string.IsNullOrEmpty(_selected.StatusLine))
                GUILayout.Label(_selected.StatusLine);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Label(_selected.MainFoodLine);
            if (!string.IsNullOrEmpty(_selected.MainBeverageLine))
                GUILayout.Label(_selected.MainBeverageLine);

            DrawPairs(_selected);
        }

        private void Select(GuestView guest)
        {
            if (guest == null) return;
            _selectedKey = KeyOf(guest);
            _selected = guest;
        }

        /// <summary>
        /// 推荐框当前要展开哪一位：优先用主面板选中的那位；他要是上车/离店了，
        /// 退回第一位还能点菜的稀客，再不行就是第一个客人。
        /// ★ 只读取，不改 _selected —— 绘制阶段改选中会让主面板跟着乱跳。
        /// </summary>
        private GuestView FocusGuest()
        {
            if (_activeGuests.Count == 0) return null;

            if (_selected != null && !_selected.Served)
                foreach (var g in _activeGuests)
                    if (ReferenceEquals(g, _selected)) return g;

            foreach (var g in _activeGuests)
                if (g.IsSpecial) return g;

            return _activeGuests[0];
        }

        /// <summary>
        /// 推荐框顶部常驻的客人头像条：所有人并排一行（放不下就换行），
        /// 点谁就把主面板和推荐框切到谁 —— 不用先回主面板找名字。
        /// 返回这条占了多高（含与下方内容之间的空隙）。
        /// </summary>
        private float DrawHintStrip(List<GuestView> list, GuestView focus, float left, float top, float usableWidth)
        {
            if (list == null || list.Count == 0) return 0f;

            var perRow = StripPerRow(usableWidth);
            var rows = (list.Count + perRow - 1) / perRow;
            var stripH = rows * AvatarSize + (rows - 1) * StripGap;

            GUILayout.BeginArea(new Rect(left, top, usableWidth, stripH));
            DrawAvatarRows(list, focus, usableWidth);
            GUILayout.EndArea();

            return stripH + StripGap;
        }

        /// <summary>一行能排几个头像（宽度不够就换行，不要把头像挤成一条）。</summary>
        private static int StripPerRow(float usableWidth)
        {
            return Mathf.Max(1, Mathf.FloorToInt((usableWidth + StripGap) / (AvatarSize + StripGap)));
        }

        /// <summary>
        /// 纯头像排布：只有头像，没有文字，点谁就切到谁。
        /// 主面板顶部和两个推荐框顶部共用这一套，尺寸/高亮/换行规则保持一致。
        /// </summary>
        private void DrawAvatarRows(List<GuestView> list, GuestView focus, float usableWidth)
        {
            if (list == null || list.Count == 0) return;

            var perRow = StripPerRow(usableWidth);
            var oldBg = GUI.backgroundColor;

            for (var i = 0; i < list.Count; i += perRow)
            {
                GUILayout.BeginHorizontal();
                for (var j = 0; j < perRow && i + j < list.Count; j++)
                {
                    var g = list[i + j];
                    GUI.backgroundColor = ReferenceEquals(g, focus) ? SelectedTint : NormalTint;
                    DrawAvatar(g);
                    if (j + 1 < perRow && i + j + 1 < list.Count)
                        GUILayout.Space(StripGap);
                }
                GUILayout.EndHorizontal();
            }

            GUI.backgroundColor = oldBg;
        }

        // 头像按钮样式：不要 padding，图片才能铺满那 48x48 的格子（默认按钮的 padding 会把图挤小）。
        private static GUIStyle _avatarStyle;

        /// <summary>
        /// 头像位（批3）：稀客有立绘就贴真图，取不到（常客本来就没有）就退回首字占位。
        /// 贴图在收集阶段就缩成 48x48 了，这里按原始尺寸画正好铺满。
        /// </summary>
        private void DrawAvatar(GuestView guest)
        {
            var content = guest.PortraitContent;

            if (content != null)
            {
                if (_avatarStyle == null)
                {
                    _avatarStyle = new GUIStyle(GUI.skin.button);
                    _avatarStyle.padding = new RectOffset();     // il2cpp 版 RectOffset 没有 4 参构造，默认就是全 0
                    _avatarStyle.imagePosition = ImagePosition.ImageOnly;
                    _avatarStyle.alignment = TextAnchor.MiddleCenter;
                }

                if (GUILayout.Button(content, _avatarStyle, AvatarOption))
                    Select(guest);
                return;
            }

            if (GUILayout.Button(guest.Initial, AvatarOption))
                Select(guest);
        }

        /// <summary>客人身份 key：名字 + 桌位。数据每 0.5 秒重建一次，靠它认人。</summary>
        private static string KeyOf(GuestView guest) =>
            guest.Name + "#" + guest.Desk + "#" + guest.CtrlDesk;

        /// <summary>
        /// 选中项维护：客人走了要自动换人，新客人来了要能选上。
        /// 放在采集之后（0.5 秒一次），别放进 OnGUI —— 那边每帧两遍还带字符串拼接。
        /// </summary>
        private void EnsureSelection()
        {
            if (_guests.Count == 0)
            {
                _selected = null;
                _selectedKey = "";
                return;
            }

            GuestView found = null;
            if (!string.IsNullOrEmpty(_selectedKey))
                foreach (var g in _guests)
                    if (KeyOf(g) == _selectedKey) { found = g; break; }

            // 选中的人走了（或从来没选过）：默认落在第一位稀客，没有稀客就第一位。
            if (found == null)
            {
                found = _guests[0];
                foreach (var g in _guests)
                    if (g.IsSpecial) { found = g; break; }
            }

            _selected = found;
            _selectedKey = KeyOf(found);
        }

        /// <summary>
        /// 主面板推荐：直接给「一道菜 + 一杯酒」的搭配。
        /// 游戏评价是两者合计出来的，分开列菜和酒玩家还得自己心算。
        /// </summary>
        private static void DrawPairs(GuestView guest)
        {
            GUILayout.Label("    [菜 + 酒 搭配]");

            if (guest.Pairs == null || guest.Pairs.Count == 0)
            {
                if (guest.Candidates.Count == 0)
                {
                    GUILayout.Label("    → 没有匹配的已解锁食谱");
                    return;
                }

                // 稀客凑不出粉评：拆开列出命中标签最多的菜和酒，至少给个方向。
                if (guest.IsSpecial && (guest.BestDish != null || guest.BestDrink != null))
                {
                    GUILayout.Label("    [凑不出粉评 · 命中标签最多]");
                    DrawSinglePick(guest.BestDish, true);
                    DrawSinglePick(guest.BestDrink, false);
                    return;
                }

                var best = 0;
                foreach (var dish in guest.Candidates)
                    if (dish.TotalPoints > best) best = dish.TotalPoints;

                GUILayout.Label($"    → 没有能粉评的组合（最高 {best} 分）");
                return;
            }

            foreach (var pair in guest.Pairs)
            {
                if (string.IsNullOrEmpty(pair.Line1))
                {
                    GUILayout.Label("    → " + (pair.Dish != null ? pair.Dish.Name : "?"));
                    continue;
                }

                GUILayout.Label(pair.Line1);
                GUILayout.Label(pair.Line2);
                if (!string.IsNullOrEmpty(pair.Line3)) GUILayout.Label(pair.Line3);
                if (!string.IsNullOrEmpty(pair.Line4)) GUILayout.Label(pair.Line4);
            }
        }

        /// <summary>净分：命中喜好标签加分、命中厌恶标签扣分，加减完的合计。</summary>
        private static int Net(DishCandidate candidate)
        {
            return candidate == null ? 0 : candidate.LikeHits - candidate.HateHits;
        }

        /// <summary>单条「命中最多的菜/酒」：用于稀客凑不出粉评时的兜底展示。</summary>
        private static void DrawSinglePick(DishCandidate candidate, bool food)
        {
            if (candidate == null)
            {
                GUILayout.Label(food ? "    → 菜品：没有可做的" : "    → 酒水：没有有货的");
                return;
            }

            var kind = food ? "菜" : "酒水";
            var state = candidate.CanCook ? "" : "（" + (food ? GuestCollector.ShortMissing(candidate.Missing) : "缺货") + "）";
            var net = Net(candidate);
            var risk = net < 0 ? "  ← 净分是负的，点了会扣分" : (net == 0 ? "（没有命中喜好）" : "");

            var cooker = food && !string.IsNullOrEmpty(candidate.Cooker) ? $"[{candidate.Cooker}]" : "";
            GUILayout.Label($"    → 最佳{kind}：{candidate.Name}{cooker}{state}  净{net}分 合计{candidate.TotalPoints}分{risk}");
        }

        private void SnapshotLog()
        {
            if (_guests.Count == 0) return;
            // 配置默认关闭：这个快照会给每位客人写几十行（6 人 ≈ 200 行），
            // 每 30 秒一次全量同步写日志，是肉眼可见的顿一下。排查时才打开。
            if (OrderHelperPlugin.SnapshotLog == null || !OrderHelperPlugin.SnapshotLog.Value) return;

            var log = OrderHelperPlugin.LogSource;
            log.LogInfo($"[OrderHelper] --- 快照 {_guests.Count} 位客人 ---");

            foreach (var guest in _guests)
            {
                log.LogInfo(
                    $"[OrderHelper] {guest.Name} | {guest.Kind} | 桌{guest.Desk} (ctrl={guest.CtrlDesk} order={guest.OrderDesk}) | " +
                    $"菜={guest.Food} | 酒={guest.Beverage} | raw={guest.RawFoodRequest}");

                if (guest.Candidates.Count == 0)
                {
                    log.LogInfo("[OrderHelper]    -> 无匹配食谱");
                    continue;
                }

                var shown = 0;
                foreach (var dish in guest.Candidates)
                {
                    if (shown++ >= 5)
                    {
                        log.LogInfo($"[OrderHelper]    -> …另有 {guest.Candidates.Count - 5} 道（快照只列前 5）");
                        break;
                    }

                    log.LogInfo($"[OrderHelper]    -> {dish.Name} [{dish.Cooker}] canCook={dish.CanCook} {dish.Missing}");
                }

                foreach (var drink in guest.BeverageCandidates)
                    log.LogInfo($"[OrderHelper]    酒 -> {drink.Name} inStock={drink.CanCook}");
            }
        }
    }
}
