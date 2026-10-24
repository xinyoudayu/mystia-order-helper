using System;

using UnityEngine;
using UnityEngine.UI;

namespace MystiaOrderHelper
{
    /// <summary>
    /// 点击防误触：指针落在我们面板上时，别让游戏把这个点击也当成「点厨具 / 点酒水 / 点垃圾箱」。
    ///
    /// 为什么会被误触：IMGUI 画的窗口对 Unity 的 EventSystem **完全不存在**。
    /// 我们在 OnGUI 里收到点击的同时，游戏自己的 uGUI 也在同一帧收到同一次鼠标按下，
    /// 于是「点面板上的按钮」= 「顺便点到了背后的厨具图标」。IMGUI 没有 API 能把事件「吃掉」。
    ///
    /// 解法：在游戏 uGUI 之上盖一块**只有 8×8、跟着鼠标走**的透明 Image，
    /// 只在指针位于我们的面板矩形内时把它的 raycastTarget 打开：
    ///   - 它挡住射线 → EventSystem 认为指针在我们的图上，游戏按钮收不到点击；
    ///   - 我们自己的 IMGUI 按钮**不走 EventSystem**，照常响应，不受影响；
    ///   - 指针一旦离开面板，raycastTarget 立刻关掉，游戏正常点击。
    ///
    /// 纪律：
    ///   1. 每帧只在 Update 里改一个 bool + 一个坐标，不枚举、不 new（这是每帧路径）。
    ///   2. 创建失败不许抛：置 _failed 写一条日志，退回原样（会误触，但不影响面板本身可用）。
    ///   3. 矩形由 OnGUI 每帧发布（BeginFrame/SetRect），Update 用上一帧的值——
    ///      拖动时最多差一帧，肉眼看不出来，换来的是不用在 Update 里重算布局。
    /// </summary>
    internal static class MouseGuard
    {
        /// <summary>要挡的面板数量：主面板 / 控制面板 / 料理推荐框 / 酒水推荐框。</summary>
        private const int Slots = 4;

        private static readonly Rect[] Rects = new Rect[Slots];

        /// <summary>跟随鼠标的透明挡板，尺寸小一点就够（8×8 中心对准指针）。</summary>
        private const float BlockerSize = 8f;

        private static GameObject _go;
        private static RectTransform _rt;
        private static Image _img;

        private static bool _failed;
        private static float _nextTry;

        /// <summary>诊断日志限频：连着点的时候不要每条都写。</summary>
        private static float _nextLog;

        internal static bool Active => _img != null && !_failed;

        /// <summary>OnGUI 开头清零——这一帧没画的面板就不算可点区域。</summary>
        internal static void BeginFrame()
        {
            for (var i = 0; i < Slots; i++) Rects[i] = default;
        }

        /// <summary>OnGUI 里画完一个面板就登记它的矩形（GUI 坐标：左上原点，y 向下）。</summary>
        internal static void SetRect(int slot, Rect r)
        {
            if (slot < 0 || slot >= Slots) return;
            if (r.width <= 0f || r.height <= 0f) return;
            Rects[slot] = r;
        }

        private static bool Hit(Vector2 guiPos)
        {
            for (var i = 0; i < Slots; i++)
            {
                var r = Rects[i];
                if (r.width > 0f && r.height > 0f && r.Contains(guiPos)) return true;
            }
            return false;
        }

        /// <summary>每帧调一次（OverlayBehaviour.Update）。</summary>
        internal static void Tick()
        {
            if (OrderHelperPlugin.ClickGuard != null && !OrderHelperPlugin.ClickGuard.Value) return;

            if (!Ensure()) return;

            Vector2 m;
            try { m = Input.mousePosition; }
            catch { return; }

            // Input.mousePosition 原点在左下，GUI 坐标原点在左上
            var gui = new Vector2(m.x, Screen.height - m.y);
            var hit = Hit(gui);

            try
            {
                _img.raycastTarget = hit;
                _rt.position = new Vector3(m.x, m.y, 0f);

                // 诊断：挡住的时候按了鼠标 → 记一条。万一游戏还有非 uGUI 的点击通道，
                // 靠这条日志能立刻分清「没挡住」还是「挡住了但它走的是另一条路」。
                if (hit && (_nextLog < 0f || Time.realtimeSinceStartup >= _nextLog))
                {
                    if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
                    {
                        _nextLog = Time.realtimeSinceStartup + 1f;
                        OrderHelperPlugin.LogSource.LogInfo(
                            "[OrderHelper] 点击防误触：指针在面板内已遮挡 at (" +
                            m.x.ToString("0") + "," + m.y.ToString("0") + ")");
                    }
                }
            }
            catch (Exception e)
            {
                _failed = true;
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 点击防误触失效: " + e.Message);
            }
        }

        /// <summary>
        /// 惰性创建挡板（第一次 Tick 时建）。失败就每 2 秒重试一次，不阻塞主流程。
        /// </summary>
        private static bool Ensure()
        {
            if (_img != null) return true;
            if (_failed) return false;
            if (Time.realtimeSinceStartup < _nextTry) return false;
            _nextTry = Time.realtimeSinceStartup + 2f;

            try
            {
                _go = new GameObject("MystiaOrderHelperMouseGuard");
                UnityEngine.Object.DontDestroyOnLoad(_go);

                var canvas = _go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32000;   // 压在所有游戏 UI 之上

                _go.AddComponent<GraphicRaycaster>();   // 没有这个，EventSystem 的射线不会看到它

                var child = new GameObject("Blocker");
                child.transform.SetParent(_go.transform, false);

                var img = child.AddComponent<Image>();
                img.color = new Color(0f, 0f, 0f, 0f);   // 完全透明，只看它的 rect 挡射线
                img.raycastTarget = false;

                var rt = child.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(BlockerSize, BlockerSize);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);

                _img = img;
                _rt = rt;

                OrderHelperPlugin.LogSource.LogInfo("[OrderHelper] 点击防误触已启用（面板区域遮挡 uGUI 射线）");
                return true;
            }
            catch (Exception e)
            {
                _failed = true;
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 点击防误触创建失败，退回原样: " + e.Message);
                return false;
            }
        }
    }
}
