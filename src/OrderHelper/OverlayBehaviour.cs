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
        private bool _fontLogged;

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

        private bool _guiLogged;
        private bool _guiErrorLogged;
        private bool _updateErrorLogged;
        private bool _hintLogged;
        private float _hintGuestLogTime;
        private bool _bevHintLogged;
        private bool _injected;
        private int _ticks;

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

        private void Update()
        {
            try
            {
                if (_showSettings && Input.GetMouseButtonDown(1))
                {
                    CloseSettings();
                    return;
                }

                if (++_ticks % 300 == 0)
                    OrderHelperPlugin.LogSource.LogInfo(
                        $"[OrderHelper] 心跳 frame={Time.frameCount} visible={_visible} guests={_guests.Count}");

                Diagnostics.TryHook();

                if (Time.time >= _nextRefresh)
                {
                    _nextRefresh = Time.time + 0.5f;
                    _guests = GuestCollector.Collect(OrderHelperPlugin.ShowNormalGuests.Value);
                }

                if (Time.time >= _nextSnapshot)
                {
                    _nextSnapshot = Time.time + 10f;
                    SnapshotLog();
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

                if (!_visible && !_showSettings && !CookerPanelWatcher.IsOpen && !BeveragePanelWatcher.IsOpen) return;

                ApplyFont(Mathf.Clamp(OrderHelperPlugin.FontSize.Value, 10, 32));

                if (_visible)
                {
                    HandleDrag();
                    GUI.Box(_rect, "夜雀点单助手 v" + OrderHelperPlugin.Version);

                var inner = new Rect(_rect.x + 10f, _rect.y + TitleHeight + 2f,
                                     _rect.width - 20f, _rect.height - TitleHeight - 12f);

                GUILayout.BeginArea(inner);
                GUILayout.BeginVertical();

                GUILayout.Label($"在场客人：{_guests.Count}    F8 显示/隐藏 · F9 控制面板");
                GUILayout.Label("今日流行｜菜：" + PopularInfo.FoodText + "    酒：" + PopularInfo.BevText);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("控制面板（F9）"))
                    ToggleSettings();
                GUILayout.Label(OrderHelperPlugin.EnableOneClickCook.Value ? "一键制作：开" : "一键制作：关");
                GUILayout.EndHorizontal();

                _scroll = GUILayout.BeginScrollView(_scroll, false, true);
                DrawGuests();
                GUILayout.EndScrollView();

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("关闭"))
                    _visible = false;

                GUILayout.EndVertical();
                GUILayout.EndArea();
                }

                DrawCookerHint();
                DrawBeverageHint();
                DrawSettings();
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
            GUI.Box(_settingsRect, "夜雀点单助手 v" + OrderHelperPlugin.Version + " · 控制面板");
            GUI.backgroundColor = oldBg;

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

        private Font ResolveFont(int size)
        {
            try
            {
                var asset = TMPro.TMP_Settings.defaultFontAsset;
                if (asset != null && asset.sourceFontFile != null)
                {
                    if (!_fontLogged)
                    {
                        _fontLogged = true;
                        OrderHelperPlugin.LogSource.LogInfo(
                            "[OrderHelper] 使用游戏 TMP 源字体: " + asset.sourceFontFile.name);
                    }
                    return asset.sourceFontFile;
                }
            }
            catch (Exception e)
            {
                if (!_fontLogged)
                {
                    _fontLogged = true;
                    OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 读取 TMP 字体失败，改用系统字体: " + e.Message);
                }
            }

            try
            {
                if (!_fontLogged)
                {
                    _fontLogged = true;
                    OrderHelperPlugin.LogSource.LogInfo("[OrderHelper] 使用系统字体: Microsoft YaHei UI");
                }
                return Font.CreateDynamicFontFromOSFont(
                    new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei" }, size);
            }
            catch
            {
                return null;
            }
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
            if (_guests.Count == 0) return;

            var width = HintSize(OrderHelperPlugin.CookerHintW.Value, OrderHelperPlugin.DefaultCookerW);
            var height = HintSize(OrderHelperPlugin.CookerHintH.Value,
                Mathf.Min(520f, 110f + _guests.Count * 150f));

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

            if (Time.time >= _hintGuestLogTime)
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

            var title = "【料理】推荐（" +
                        (string.IsNullOrEmpty(CookerPanelWatcher.CookerName) ? "当前厨具" : CookerPanelWatcher.CookerName) +
                        "）" + (OrderHelperPlugin.EnableOneClickCook.Value ? "" : "  一键制作：关");

            // 第一次画这个面板时，把面板里没有的推荐菜注入原生列表，让它们变成可点的格子。
            if (!_injected && _guests.Count > 0)
            {
                _injected = true;
                var toInject = new List<Recipe>();
                foreach (var guest in _guests)
                {
                    if (guest.Served) continue;
                    var pick = 0;
                    foreach (var dish in MatchedFor(guest))
                    {
                        if (dish.Recipe == null) continue;
                        if (pick++ >= 2) break;
                        toInject.Add(dish.Recipe);
                    }
                }

                if (toInject.Count > 0)
                {
                    var added = CookerPanelWatcher.InjectRecipes(toInject, "推荐");
                    OrderHelperPlugin.LogSource.LogInfo($"[OrderHelper] 推荐菜注入结果 added={added} 请求={toInject.Count}");
                }
            }

            var oldBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.86f, 0.62f, 1f);
            GUI.Box(new Rect(x, y, width, height), title);
            GUI.backgroundColor = oldBg;

            GUILayout.BeginArea(new Rect(x + 6f, y + TitleHeight + 2f, width - 12f, height - TitleHeight - 8f));
            _cookerHintScroll = GUILayout.BeginScrollView(_cookerHintScroll, false, true);
            GUILayout.BeginVertical();
            GUILayout.Label("今日流行｜菜：" + PopularInfo.FoodText);
            var firstGuest = true;
            foreach (var guest in _guests)
            {
                // 已经上过菜、正在等下一单的客人不再进推荐框。
                if (guest.Served) continue;

                var matched = MatchedFor(guest);

                if (!firstGuest) GuestDivider();
                firstGuest = false;

                var desk = guest.Desk >= 0 ? guest.Desk : guest.CtrlDesk;
                var remain = guest.RemainOrders >= 0 ? $"  剩{guest.RemainOrders}单" : "";
                GUILayout.Label($"◆ {guest.Name}（{guest.Kind}）桌{(desk >= 0 ? (desk + 1).ToString() : "—")}{remain}");

                if (matched.Count == 0)
                {
                    if (CookerPanelWatcher.SpecialCooker && _guests.IndexOf(guest) == 0)
                    {
                        var shown = 0;
                        foreach (var dish in guest.Candidates)
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
                    continue;
                }

                // 稀客只推能粉评的菜；一道都凑不出来就退回命中标签最多的那道。
                // 常客点的是固定菜，不做粉评过滤。
                if (guest.IsSpecial)
                {
                    var pink = matched.FindAll(d => d.TotalPoints >= 4);
                    if (pink.Count > 0) matched = pink;
                    else
                    {
                        var top = GuestCollector.BestHit(matched);
                        matched = top != null ? new List<DishCandidate> { top } : matched;
                    }
                }

                foreach (var dish in matched)
                {
                    var state = dish.CanCook
                        ? "可做"
                        : (string.IsNullOrEmpty(dish.Missing) ? "缺料" : dish.Missing);
                    var gap = dish.TotalPoints >= 4 ? "" : $" 差{4 - dish.TotalPoints}分";
                    var label = $"{dish.Name}(净{Net(dish)}分)  [{dish.Rating} 合计{dish.TotalPoints}分{gap}] {state}\n" +
                                CookerPanelWatcher.IngredientText(dish.Recipe);
                    if (GUILayout.Button(label, GUILayout.ExpandHeight(true)))
                    {
                        if (OrderHelperPlugin.EnableOneClickCook.Value)
                            CookerPanelWatcher.SelectIngredients(dish.Recipe);
                        else
                            OrderHelperPlugin.LogSource.LogInfo("[OrderHelper] 一键制作已关闭，忽略点击: " + dish.Name);
                    }
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
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.DrawTexture(rect, _lineTex, ScaleMode.StretchToFill);
                GUI.color = old;

                GUILayout.Space(3f);
            }
            catch
            {
                GUILayout.Label("----------------------");
            }
        }

        /// <summary>客人之间的分割：粗一点、亮一点、占满整行。</summary>
        private static void GuestDivider()
        {
            DrawLine(3f, 0.80f, 0f);
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
                    pool, new List<DishCandidate>(guest.BeverageCandidates));

                foreach (var dish in pool)
                {
                    if (dish.Recipe == null) continue;
                    if (!CookerPanelWatcher.Supports(dish.Recipe)) continue;
                    matched.Add(dish);
                    if (matched.Count >= 4) break;
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
            if (_guests.Count == 0) return;

            var width = HintSize(OrderHelperPlugin.BevHintW.Value, OrderHelperPlugin.DefaultBevW);
            var height = HintSize(OrderHelperPlugin.BevHintH.Value,
                Mathf.Min(620f, 90f + _guests.Count * 120f));

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
            GUI.backgroundColor = new Color(0.72f, 0.88f, 1f, 1f);
            GUI.Box(new Rect(x, y, width, height), "【酒水】推荐");
            GUI.backgroundColor = oldBg;
            GUILayout.BeginArea(new Rect(x + 6f, y + TitleHeight + 2f, width - 12f, height - TitleHeight - 8f));
            _bevHintScroll = GUILayout.BeginScrollView(_bevHintScroll, false, true);
            GUILayout.BeginVertical();
            GUILayout.Label("今日流行｜酒：" + PopularInfo.BevText);

            var firstGuest = true;
            foreach (var guest in _guests)
            {
                // 已经上过菜、正在等下一单的客人不再进推荐框。
                if (guest.Served) continue;

                if (!firstGuest) GuestDivider();
                firstGuest = false;

                if (guest.BeverageCandidates.Count == 0)
                {
                    GUILayout.Label($"◆ {guest.Name}：没有匹配的酒水");
                    continue;
                }

                // 稀客只推能粉评的酒水；一杯都凑不出来就退回命中标签最多的那杯。
                // 常客不做粉评过滤。
                var drinkable = new List<DishCandidate>();
                if (guest.IsSpecial)
                {
                    foreach (var drink in guest.BeverageCandidates)
                        if (drink.TotalPoints >= 4) drinkable.Add(drink);

                    if (drinkable.Count == 0)
                    {
                        var top = GuestCollector.BestHit(guest.BeverageCandidates);
                        if (top != null) drinkable.Add(top);
                    }
                }
                else
                {
                    drinkable.AddRange(guest.BeverageCandidates);
                }

                var desk = guest.Desk >= 0 ? guest.Desk : guest.CtrlDesk;
                var remain = guest.RemainOrders >= 0 ? $"  剩{guest.RemainOrders}单" : "";
                GUILayout.Label($"◆ {guest.Name}（{guest.Kind}）桌{(desk >= 0 ? (desk + 1).ToString() : "—")}{remain}");
                var shown = 0;
                foreach (var drink in drinkable)
                {
                    if (shown++ >= 4) break;
                    var label = $"{drink.Name}(净{Net(drink)}分)  [{drink.Rating} 合计{drink.TotalPoints}分] {(drink.CanCook ? "有货" : "缺货")}";
                    if (GUILayout.Button(label))
                    {
                        if (OrderHelperPlugin.EnableOneClickCook.Value)
                            BeveragePanelWatcher.TrySelectBeverage(drink.Sellable, drink.Name);
                        else
                            OrderHelperPlugin.LogSource.LogInfo("[OrderHelper] 一键制作已关闭，忽略酒水点击: " + drink.Name);
                    }
                }
            }
            GUILayout.EndVertical();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawGuests()
        {
            if (_guests.Count == 0)
            {
                GUILayout.Label("（营业场景里没有客人，或还没开始营业）");
                return;
            }

            // 外层 OnGUI 已经有滚动视图，这里不要再套一层。
            var first = true;
            foreach (var guest in _guests)
            {
                if (!first) GuestDivider();
                first = false;

                var desk = guest.Desk >= 0 ? guest.Desk : guest.CtrlDesk;
                GUILayout.Label($"◆ {guest.Name}（{guest.Kind}）  桌位 {(desk >= 0 ? (desk + 1).ToString() : "—")}");
                GUILayout.Label($"    点菜：{guest.Food}");
                if (!string.IsNullOrEmpty(guest.Beverage))
                    GUILayout.Label($"    点酒：{guest.Beverage}");

                DrawPairs(guest);
            }
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
                var dish = pair.Dish;
                var drink = pair.Drink;

                var dishState = dish.CanCook ? "" : "（" + ShortMissing(dish.Missing) + "）";
                var drinkState = drink == null || drink.CanCook ? "" : "（缺货）";
                var drinkName = drink != null ? drink.Name : "无酒";
                var gap = pair.Gap > 0 ? $"  差{pair.Gap}分" : "";

                var cooker = string.IsNullOrEmpty(dish.Cooker) ? "" : $"[{dish.Cooker}]";
                GUILayout.Label($"    → {dish.Name}{cooker}{dishState}＋{drinkName}{drinkState}");
                GUILayout.Label($"       {pair.Rating}{pair.TotalPoints}分{gap}  ｜ 净分 菜{Net(dish)}" +
                                (drink != null ? $" 酒{Net(drink)}" : ""));
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
            var state = candidate.CanCook ? "" : "（" + (food ? ShortMissing(candidate.Missing) : "缺货") + "）";
            var net = Net(candidate);
            var risk = net < 0 ? "  ← 净分是负的，点了会扣分" : (net == 0 ? "（没有命中喜好）" : "");

            var cooker = food && !string.IsNullOrEmpty(candidate.Cooker) ? $"[{candidate.Cooker}]" : "";
            GUILayout.Label($"    → 最佳{kind}：{candidate.Name}{cooker}{state}  净{net}分 合计{candidate.TotalPoints}分{risk}");
        }

        /// <summary>缺料说明太长会把整行顶出窗口，配对列表里只留开头，细节看推荐框。</summary>
        private static string ShortMissing(string missing)
        {
            if (string.IsNullOrEmpty(missing)) return "缺料";
            return missing.Length > 14 ? missing.Substring(0, 13) + "…" : missing;
        }

        private void SnapshotLog()
        {
            if (_guests.Count == 0) return;

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

                foreach (var dish in guest.Candidates)
                    log.LogInfo($"[OrderHelper]    -> {dish.Name} [{dish.Cooker}] canCook={dish.CanCook} {dish.Missing}");

                foreach (var drink in guest.BeverageCandidates)
                    log.LogInfo($"[OrderHelper]    酒 -> {drink.Name} inStock={drink.CanCook}");
            }
        }
    }
}
