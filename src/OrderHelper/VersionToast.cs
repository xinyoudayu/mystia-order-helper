using System;

using UnityEngine;

namespace MystiaOrderHelper
{
    /// <summary>
    /// 进游戏后弹一条版本提示：屏幕上方居中的小卡片，写着「夜雀点单助手 vX.Y.Z」，
    /// 淡入 → 停留 → 淡出，整轮约 4.5 秒，**每次启动只弹一次**。
    ///
    /// 「只弹一次」的落实方式（三板斧，缺一不可）：
    ///   · _shown   —— 放出去过就不再放，即使中途被什么原因打断（淡出没画完）也不会重来；
    ///   · _finished —— 淡出结束 / 出错 / 配置关掉，之后 Tick 和 Draw 双双向早退；
    ///   · 同帧去重 —— 万一以后又挂了第二个 OverlayBehaviour，同一帧只认第一次调用。
    /// 三个都是 static，作用域是整个进程，所以「切场景 / 回标题 / 重开一天 / 反复开关面板」
    /// 都不会让它再弹；只有真正重启游戏才会重新弹一次。
    ///
    /// 为什么要单独做，不挂在主面板上：
    ///   面板默认不显示（非营业场景），如果画在面板里，进游戏时根本看不到。
    ///   所以它必须在 OnGUI 的「什么都没有就早退」那句话之前画。
    ///
    /// 性能注意（和面板相同的铁律）：
    ///   · 字符串和 GUIStyle 都只在 Tick（Update）里建一次，OnGUI 里只做画；
    ///   · 用 realtimeSinceStartup 而不是 Time.time —— 打开厨具/酒水面板时游戏会把
    ///     timeScale 设成 0，Time.time 会冻住，提示就永远淡不出去。
    /// </summary>
    internal static class VersionToast
    {
        // 时间轴：启动后 Delay 秒出现，淡入 FadeIn，停留 Hold，淡出 FadeOut。
        private const float Delay = 2.5f;
        private const float FadeIn = 0.25f;
        private const float Hold = 3.4f;
        private const float FadeOut = 0.8f;

        private const float CardWidth = 320f;
        private const float CardHeight = 46f;
        private const float TopMargin = 36f;

        private static readonly Color CardBg = new Color(0.10f, 0.09f, 0.08f, 0.92f);
        private static readonly Color NameInk = new Color(0.96f, 0.93f, 0.86f, 1f);

        private static bool _armed;      // 计时已开始
        private static bool _shown;      // 已经弹过（本次进程内不再弹）
        private static bool _finished;   // 淡出结束，之后彻底不管
        private static int _lastTickFrame = -1;   // 同帧去重
        private static float _armedAt = -1f;
        private static float _shownAt;
        private static string _text;
        private static GUIStyle _style;
        private static Texture2D _bg;

        /// <summary>每帧一次（OverlayBehaviour.Update）。只负责「到点了就把提示放出去」。</summary>
        internal static void Tick()
        {
            try
            {
                // 同一帧只认第一次调用：以后要是有人再挂一个 OverlayBehaviour，也不会弹两遍。
                var frame = Time.frameCount;
                if (frame == _lastTickFrame) return;
                _lastTickFrame = frame;

                if (_shown || _finished) return;

                if (OrderHelperPlugin.ShowVersionNotice != null && !OrderHelperPlugin.ShowVersionNotice.Value)
                {
                    _finished = true;
                    return;
                }

                // 宿主对象在插件 Load 时就建好了，那时还在引擎启动阶段；先记个起点，等 Delay 秒再弹，
                // 免得提示闪在游戏自己的启动画面下面、玩家根本没看见。
                if (!_armed)
                {
                    _armed = true;
                    _armedAt = Time.realtimeSinceStartup;
                    return;
                }

                if (Time.realtimeSinceStartup - _armedAt < Delay) return;

                // 名字用亮色、版本号用暗色，和联机模组那种「标题 + 灰版本」的观感一致。
                // 富文本靠 GUIStyle.richText 打开，只有这里拼一次字符串。
                _text = OrderHelperPlugin.Name +
                        "  <color=#9E9483>v" + OrderHelperPlugin.Version + "</color>";

                _shown = true;
                _shownAt = Time.realtimeSinceStartup;

                OrderHelperPlugin.LogSource?.LogInfo(
                    "[OrderHelper] 版本提示已显示：" + OrderHelperPlugin.Name + " v" + OrderHelperPlugin.Version);
            }
            catch (Exception e)
            {
                _finished = true;
                OrderHelperPlugin.LogSource?.LogWarning("[OrderHelper] 版本提示初始化失败: " + e.Message);
            }
        }

        /// <summary>OnGUI 里调（必须在「没有任何面板就 return」之前）。</summary>
        internal static void Draw()
        {
            if (!_shown || _finished) return;

            float elapsed = Time.realtimeSinceStartup - _shownAt;
            float total = FadeIn + Hold + FadeOut;

            if (elapsed >= total)
            {
                _finished = true;
                // 这条日志是「只弹一次」的凭证：一次启动里，它必须恰好出现一次。
                OrderHelperPlugin.LogSource?.LogInfo("[OrderHelper] 版本提示结束，本次启动不再显示");
                return;
            }

            float alpha;
            if (elapsed < FadeIn) alpha = elapsed / FadeIn;
            else if (elapsed <= FadeIn + Hold) alpha = 1f;
            else alpha = 1f - (elapsed - FadeIn - Hold) / FadeOut;
            alpha = Mathf.Clamp01(alpha);

            var style = Style();
            if (style == null) return;

            var oldColor = GUI.color;
            try
            {
                // 卡片稍微往上抬一点点淡入，比纯透明度变化自然（位移量很小，不影响布局）。
                var lift = (1f - alpha) * 6f;
                var rect = new Rect((Screen.width - CardWidth) * 0.5f, TopMargin - lift, CardWidth, CardHeight);

                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.Box(rect, _text, style);
            }
            catch (Exception e)
            {
                _finished = true;
                OrderHelperPlugin.LogSource?.LogWarning("[OrderHelper] 版本提示绘制失败: " + e.Message);
            }
            finally
            {
                GUI.color = oldColor;
            }
        }

        /// <summary>样式只建一次；字号或屏幕缩放变了才重建。</summary>
        private static GUIStyle Style()
        {
            var fontSize = Mathf.Clamp(OrderHelperPlugin.FontSize.Value, 12, 28) - 2;

            if (_style != null && _style.fontSize == fontSize) return _style;

            _style = new GUIStyle(GUI.skin.box);
            _style.name = "OrderHelperVersionToast";
            _style.alignment = TextAnchor.MiddleCenter;
            _style.richText = true;
            _style.fontSize = fontSize;
            _style.normal.background = CardTexture();
            _style.normal.textColor = NameInk;
            // 底色是 1×1 纯色贴图，**不要设 border**（九宫格要求切边小于贴图尺寸，
            // 1 像素的图配 6 像素切边会触发 Unity 的切片告警，而且纯色也不需要九宫格）。
            _style.border = new RectOffset();
            _style.padding = new RectOffset { left = 12, right = 12, top = 8, bottom = 8 };

            // ★ 必须显式给中文字体：这条提示画在 OnGUI 早退之前，那时 ApplyFont 还没跑过，
            //   此时 GUI.skin.font 是 Unity 默认 Arial，中文会变成方块。
            var font = OverlayBehaviour.SharedFont(fontSize);
            if (font != null) _style.font = font;

            return _style;
        }

        /// <summary>纯色底：1×1 贴图，比纸片皮肤更适合放在任意场景上（不受面板是否取到素材影响）。</summary>
        private static Texture2D CardTexture()
        {
            if (_bg != null) return _bg;

            _bg = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            _bg.SetPixel(0, 0, CardBg);
            _bg.Apply();
            return _bg;
        }
    }
}
