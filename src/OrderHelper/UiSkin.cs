using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

namespace MystiaOrderHelper
{
    /// <summary>
    /// 面板皮肤：把 IMGUI 的灰盒子换成游戏自己的「纸条」质感。
    ///
    /// 素材全部来自游戏运行时，不打包任何图片（见 docs/UI侦察报告.md）：
    ///   OrderReceipt_BG_0 / _1   订单小票底图，32x66，border=(10,6,10,14) → 天然九宫格
    ///   CharBG / CharFrame       头像底衬与描边（批3 头像用，这里先备好）
    ///   Check                    完成标记（后续批次可能用）
    ///
    /// 三条设计纪律：
    ///   1. **一次扫描、全程缓存**：扫描几千个 Image 只在第一次画面板时做一次，之后只查字典。
    ///      OnGUI 每帧两遍，任何 property 读取都可能变 interop 调用，绝不留在这条路径上。
    ///   2. **贴图是自己的副本**：不去持有游戏的 Texture2D/Image，而是把图集里的那一小块拷成我们
    ///      自己的 Texture2D。跨场景不用失效（§ IL2CPP 指针失效规则对自持有对象不适用），
    ///      也顺带绕开了 DrawTextureWithTexCoords 的 UV 翻转坑。
    ///   3. **取不到就降级**：任何一步失败都退回原来的 IMGUI 灰盒样式，并把原因写进日志（不许静默）。
    /// </summary>
    internal static class UiSkin
    {
        private sealed class Asset
        {
            internal Texture2D Tex;
            internal int W;
            internal int H;
            internal Vector4 Border;
        }

        // 墨色：纸条上写字用的深褐；线条用它的半透明版本。
        internal static readonly Color Ink = new Color(0.16f, 0.12f, 0.09f, 1f);
        internal static readonly Color InkSoft = new Color(0.38f, 0.30f, 0.21f, 1f);

        private const string Paper = "OrderReceipt_BG_0";
        private const string PaperCurrent = "OrderReceipt_BG_1";
        private const string CharBG = "CharBG";
        private const string CharFrame = "CharFrame";
        private const string Check = "Check";

        private static readonly string[] Wanted = { Paper, PaperCurrent, CharBG, CharFrame, Check };

        private static readonly Dictionary<string, Asset> _cache = new Dictionary<string, Asset>(StringComparer.Ordinal);
        private static bool _scanned;
        private static float _nextRetry = -1f;
        private static bool _reasonLogged;
        private const int MaxAttempts = 6;
        private static int _attempts;

        private static GUIStyle _paperStyle;
        private static int _styleFontSize = -1;

        internal static bool Enabled => OrderHelperPlugin.VanillaSkin.Value;

        /// <summary>纸已经取到（取不到时返回 false，调用方退回默认样式）。</summary>
        internal static bool Ready => Enabled && _cache.ContainsKey(Paper);

        // ------------------------------------------------------------------ 取素材

        /// <summary>每帧可调用的轻量入口：只在第一次真正做事，之后命中缓存直接返回。</summary>
        internal static void Prepare()
        {
            if (_scanned || !Enabled) return;
            if (_nextRetry > 0f && Time.time < _nextRetry) return;

            // 没拿到核心素材（纸条底）才重试；拿到了就彻底停手，别再碰那 5 千个 Image。
            // 重试上限 6 次 ≈ 30 秒：营业刚开局时 UI 可能还没建完，之后就不再打扰。
            if (_attempts >= MaxAttempts) return;
            _attempts++;

            _nextRetry = Time.time + 5f;
            Scan();

            if (_cache.ContainsKey(Paper) || _attempts >= MaxAttempts)
            {
                _scanned = true;

                if (!_reasonLogged)
                {
                    _reasonLogged = true;
                    var miss = new List<string>();
                    foreach (var n in Wanted) if (!_cache.ContainsKey(n)) miss.Add(n);

                    OrderHelperPlugin.LogSource.LogInfo(
                        "[OrderHelper] 皮肤素材就绪 " + _cache.Count + "/" + Wanted.Length +
                        (miss.Count > 0 ? "，缺：" + string.Join(" / ", miss) + "（用到它们的部分退回默认样式）" : ""));
                }
            }
        }

        private static int Scan()
        {
            Image[] all;
            try { all = Resources.FindObjectsOfTypeAll<Image>(); }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 皮肤扫描取不到 Image 列表: " + e.Message);
                return 0;
            }
            if (all == null) return 0;

            for (var i = 0; i < all.Length; i++)
            {
                Image img;
                Sprite sp;
                string name;

                try
                {
                    img = all[i];
                    if (img == null) continue;
                    sp = img.sprite;
                    if (sp == null) continue;
                    name = sp.name;
                    if (string.IsNullOrEmpty(name)) continue;
                }
                catch { continue; }

                if (Array.IndexOf(Wanted, name) < 0) continue;
                if (_cache.ContainsKey(name)) continue;

                var asset = Copy(sp, name);
                if (asset != null)
                    _cache[name] = asset;

                var done = true;
                foreach (var n in Wanted) { if (!_cache.ContainsKey(n)) { done = false; break; } }
                if (done) break;   // 五张都到手就别再枚举剩下的 Image 了
            }

            return _cache.Count;
        }

        /// <summary>
        /// 把 sprite 在图集里的那一小块拷成我们自己的 Texture2D。
        /// 图集贴图通常关了 Read/Write，直接 GetPixels 会抛 → 先过一道 RenderTexture 拿可读副本。
        /// </summary>
        private static Asset Copy(Sprite sp, string name)
        {
            Texture2D copy = null;
            Texture2D crop = null;
            RenderTexture rt = null;

            try
            {
                var src = sp.texture;
                if (src == null || src.width <= 0 || src.height <= 0) return null;

                var w = src.width;
                var h = src.height;

                // Unity 2021+ 收窄了 GetTemporary 的重载，用最稳的 3 参版本。
                rt = RenderTexture.GetTemporary(w, h, 0);
                RenderTexture.active = rt;
                Graphics.Blit(src, rt);

                copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                copy.Apply();
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                rt = null;

                var r = sp.textureRect;
                var cw = Mathf.Clamp(Mathf.RoundToInt(r.width), 1, 2048);
                var ch = Mathf.Clamp(Mathf.RoundToInt(r.height), 1, 2048);
                var cx = Mathf.Clamp(Mathf.RoundToInt(r.x), 0, Mathf.Max(0, w - 1));
                var cy = Mathf.Clamp(Mathf.RoundToInt(r.y), 0, Mathf.Max(0, h - 1));
                if (cx + cw > w) cw = w - cx;
                if (cy + ch > h) ch = h - cy;
                if (cw <= 0 || ch <= 0) return null;

                crop = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
                crop.SetPixels(copy.GetPixels(cx, cy, cw, ch));
                crop.Apply();
                crop.wrapMode = TextureWrapMode.Clamp;
                crop.hideFlags = HideFlags.HideAndDontSave;   // 自己持有整个会话，别被场景卸载回收

                return new Asset
                {
                    Tex = crop,
                    W = cw,
                    H = ch,
                    Border = SafeBorder(sp)
                };
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 皮肤素材 " + name + " 拷贝失败: " + e.Message);
                return null;
            }
            finally
            {
                if (RenderTexture.active != null) RenderTexture.active = null;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (copy != null) UnityEngine.Object.Destroy(copy);
                // crop 是要留用的，故意不销毁
            }
        }

        private static Vector4 SafeBorder(Sprite sp)
        {
            try { return sp.border; }
            catch { return Vector4.zero; }
        }

        // ------------------------------------------------------------------ 样式

        /// <summary>纸条底盒样式；取不到素材时返回 null，调用方用默认 GUI.Box 样式。</summary>
        internal static GUIStyle PaperBox(int fontSize)
        {
            if (!Ready) return null;

            if (_paperStyle == null)
            {
                var a = _cache[Paper];

                _paperStyle = new GUIStyle(GUI.skin.box);
                _paperStyle.name = "OrderHelperPaper";

                // GUIStyle 会按 border 把背景图切成九块拉伸，这正是九宫格的目的。
                // sprite.border = (left, bottom, right, top)；RectOffset 是 (left, right, top, bottom) —— 别写反。
                // 注意：这台机器上的 RectOffset 没有 4 参构造函数（ils2cpp 版只暴露默认构造 + 字段），只能逐个赋值。
                _paperStyle.border = FromSpriteBorder(a.Border, 0);
                _paperStyle.padding = new RectOffset { left = 12, right = 12, top = 8, bottom = 8 };
                _paperStyle.alignment = TextAnchor.UpperCenter;
                _paperStyle.normal.background = a.Tex;
                _paperStyle.normal.textColor = Ink;
                _paperStyle.active.background = a.Tex;
                _paperStyle.active.textColor = Ink;
                _paperStyle.hover.background = a.Tex;
                _paperStyle.hover.textColor = Ink;
                _paperStyle.focused.background = a.Tex;
                _paperStyle.focused.textColor = Ink;
            }

            // 字号是运行时可改的，样式对象本身复用，只同步字号和字体。
            if (_styleFontSize != fontSize || _paperStyle.font != GUI.skin.font)
            {
                _styleFontSize = fontSize;
                _paperStyle.font = GUI.skin.font;
                _paperStyle.fontSize = fontSize - 2;
            }

            return _paperStyle;
        }

        /// <summary>
        /// 把自带的 label / button 文字与底图统一到纸片风格。
        ///
        /// 演变过程（留个记录，别再走回头路）：
        ///   1. 只换面板底图 → 按钮还是 Unity 默认灰，配墨色字深底深字，看不清；
        ///   2. 把按钮字改成米白 → 能读了，但灰按钮本身和纸风格不搭；
        ///   3. 最终：按钮直接铺纸片色（比面板稍深一点，做出层次），字回墨色；滚动条也铺同色。
        /// </summary>
        internal static void ApplyTone()
        {
            if (!Ready) return;

            try
            {
                GUI.skin.label.normal.textColor = Ink;
                GUI.skin.label.hover.textColor = Ink;
                GUI.skin.label.active.textColor = Ink;
                GUI.skin.box.normal.textColor = Ink;

                var chip = Chip();
                if (chip != null)
                {
                    var btn = GUI.skin.button;
                    btn.normal.background = chip;
                    btn.hover.background = chip;
                    btn.active.background = chip;
                    btn.focused.background = chip;
                    btn.normal.textColor = Ink;
                    btn.hover.textColor = Ink;
                    btn.active.textColor = Ink;
                    btn.focused.textColor = Ink;

                    // Unity 默认的金属灰滚动条在纸面板上很扎眼，同一张纸片色铺上去。
                    var thumb = GUI.skin.verticalScrollbarThumb;
                    thumb.normal.background = chip;
                    thumb.hover.background = chip;
                    thumb.active.background = chip;

                    var vbar = GUI.skin.verticalScrollbar;
                    vbar.normal.background = chip;
                    vbar.hover.background = chip;
                    vbar.active.background = chip;
                }
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 应用皮肤色调失败: " + e.Message);
            }
        }

        /// <summary>
        /// 纸片色：取纸条底的中心像素，压暗一点点做按钮/滑块的底。
        /// 用纯色而不是整张纸图，是为了避免小尺寸按钮被九宫格切得七零八落。
        /// </summary>
        private static Texture2D Chip()
        {
            if (_chip != null) return _chip;
            if (!_cache.TryGetValue(Paper, out var a) || a?.Tex == null) return null;

            try
            {
                var c = a.Tex.GetPixel(a.W / 2, a.H / 2);
                c = new Color(c.r * 0.94f, c.g * 0.92f, c.b * 0.88f, 1f);

                _chip = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                _chip.SetPixel(0, 0, c);
                _chip.Apply();
                return _chip;
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 纸片色生成失败: " + e.Message);
                return null;
            }
        }

        private static Texture2D _chip;

        /// <summary>分割线颜色：纸条上用墨色的半透明版本，不再是原来的白线。</summary>
        internal static Color Line(float alpha) => Ready ? new Color(Ink.r, Ink.g, Ink.b, alpha) : new Color(1f, 1f, 1f, alpha);

        /// <summary>面板标题下方那道横线——用小标题从视觉上和内容分开。</summary>
        internal static void DrawTitleRule(Rect win, float y)
        {
            if (!Ready) return;

            try
            {
                if (_rule == null)
                {
                    _rule = new Texture2D(1, 1);
                    _rule.SetPixel(0, 0, Color.white);
                    _rule.Apply();
                }

                var old = GUI.color;
                GUI.color = new Color(Ink.r, Ink.g, Ink.b, 0.45f);
                GUI.DrawTexture(new Rect(win.x + 14f, y, win.width - 28f, 1.5f), _rule, ScaleMode.StretchToFill);
                GUI.color = old;
            }
            catch { }
        }

        private static Texture2D _rule;

        /// <summary>
        /// sprite.border = (x=左, y=下, z=右, w=上) → RectOffset(left, right, top, bottom)。
        /// inflate 用来给 padding 之类额外加一圈（九宫格本身不要动）。
        /// </summary>
        private static RectOffset FromSpriteBorder(Vector4 v, int inflate)
        {
            var r = new RectOffset();
            r.left = Mathf.Max(0, Mathf.RoundToInt(v.x) + inflate);
            r.right = Mathf.Max(0, Mathf.RoundToInt(v.z) + inflate);
            r.top = Mathf.Max(0, Mathf.RoundToInt(v.w) + inflate);
            r.bottom = Mathf.Max(0, Mathf.RoundToInt(v.y) + inflate);
            return r;
        }

        /// <summary>取某张素材（批3 头像要用），拿不到返回 null。</summary>
        internal static bool TryGet(string name, out Texture2D tex, out Vector4 border)
        {
            tex = null;
            border = Vector4.zero;

            if (_cache.TryGetValue(name, out var a) && a != null)
            {
                tex = a.Tex;
                border = a.Border;
                return tex != null;
            }

            return false;
        }

        internal const string RolePaperCurrent = PaperCurrent;
        internal const string RoleCharBG = CharBG;
        internal const string RoleCharFrame = CharFrame;
        internal const string RoleCheck = Check;
    }
}
