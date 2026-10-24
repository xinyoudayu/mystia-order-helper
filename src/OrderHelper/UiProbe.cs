using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using BepInEx;

using UnityEngine;
using UnityEngine.UI;

namespace MystiaOrderHelper
{
    /// <summary>
    /// 一次性 UI 侦察（**纯只读**：不修改任何游戏对象、不注入、不改游戏目录）。
    ///
    /// 为什么要有它：接下来要做「面板里显示客人头像 + 贴近原版的皮肤」，两者都打算直接借游戏
    /// 自己的 Sprite，不打包任何美术资源。但程序集里只能看到字段名和签名，看不到「哪张图长
    /// 什么样、多大、是不是图集切片」。这个类就是那次上机扫描。
    ///
    /// 它要回答四个问题（直接决定后面怎么画）：
    ///   1. 订单卡上的头像用的是哪张 Sprite，缩到 44px 会不会糊；
    ///   2. 这张 Sprite 是不是图集切片（packed）——图集切片不能整张贴，必须按 textureRect 取 UV；
    ///   3. 头像原图本身是没有框的（决定我们要不要出去套一层 CharFrame 边框）；
    ///   4. 有哪些边框底图是九宫格（border 非零 → 可以直接 9-slice 画面板底和按钮）。
    ///
    /// 输出：
    ///   BepInEx\config\orderhelper-ui-dump.txt              清单
    ///   BepInEx\config\orderhelper-ui-samples\*.png         导出的头像原图（真的图片，可以直接打开看）
    ///
    /// 触发：F10 手动（每次按都重扫一遍）；或 [Diagnostics] UiProbe = true 时第一位客人进店自动扫一次。
    ///
    /// 踩过的坑：导出一定要在扫描那一趟里做，边扫边导。先扫再按名字回头找，找到的往往是另一个
    /// 同名实例（失活的池化/图鉴对象），它的图集贴图没加载，sp.texture 会返回 null 或宽高为 0，
    /// 于是 RenderTexture.GetTemporary 直接抛 "width must be greater than zero"，一张图都导不出。
    /// </summary>
    internal static class UiProbe
    {
        internal const int MaxExport = 12;

        private static bool _autoRan;
        private static string _outDir;
        private static int _seq;

        private static readonly List<string> ExportLog = new List<string>();
        private static readonly List<Rec> Pending = new List<Rec>();
        private static readonly Dictionary<Rec, Sprite> PendingSprite = new Dictionary<Rec, Sprite>();

        /// <summary>自动那次已经扫过了（成功或失败都算，避免每帧重试刷日志）。</summary>
        internal static bool HasRun => _autoRan;

        /// <summary>自动触发：只跑一次。</summary>
        internal static void RunOnce(string reason)
        {
            if (_autoRan) return;
            _autoRan = true;
            Run(reason);
        }

        /// <summary>手动触发（F10）：每次都跑，方便在两个不同时机各取一次。</summary>
        internal static void Run(string reason)
        {
            try
            {
                Dump(reason);
            }
            catch (Exception e)
            {
                try { OrderHelperPlugin.LogSource.LogError("[OrderHelper] UI 侦察失败: " + e); }
                catch { }
            }
        }

        // ---------------------------------------------------------------- 一条记录

        private sealed class Rec
        {
            public string Path = "?";
            public string Sprite = "?";
            public string Texture = "?";
            public int TexW, TexH;
            public int RectW, RectH;
            public float PosW, PosH;        // RectTransform 上的实际显示尺寸
            public float TRx, TRy, TRw, TRh; // sprite.textureRect：图集里的像素位置
            public bool Packed;
            public Vector4 Border;
            public bool Active;
            public int Priority = 99;

            internal string Render() =>
                "  " + Path + "\n" +
                "      sprite=" + Sprite +
                "  rect=" + RectW + "x" + RectH +
                "  texture=" + Texture + " " + TexW + "x" + TexH +
                (Packed ? "  packed" : "") +
                "  texRect=(" + Mathf.RoundToInt(TRx) + "," + Mathf.RoundToInt(TRy) + " " +
                Mathf.RoundToInt(TRw) + "x" + Mathf.RoundToInt(TRh) + ")" +
                "  border=(" + Mathf.RoundToInt(Border.x) + "," + Mathf.RoundToInt(Border.y) + "," +
                Mathf.RoundToInt(Border.z) + "," + Mathf.RoundToInt(Border.w) + ")" +
                "  显示=" + Mathf.RoundToInt(PosW) + "x" + Mathf.RoundToInt(PosH) +
                "  active=" + Active;
        }

        // ---------------------------------------------------------------- 主流程

        private static void Dump(string reason)
        {
            ExportLog.Clear();
            Pending.Clear();
            PendingSprite.Clear();
            _seq = 0;

            try
            {
                _outDir = Path.Combine(Paths.BepInExRootPath, "config", "orderhelper-ui-samples");
                Directory.CreateDirectory(_outDir);
                foreach (var f in Directory.GetFiles(_outDir, "*.png")) File.Delete(f);
            }
            catch (Exception e)
            {
                _outDir = null;
                ExportLog.Add("样例目录准备失败: " + e.Message);
            }

            var recs = Collect();
            Export();

            var sb = new StringBuilder();
            sb.AppendLine("=== 夜雀点单助手 · UI 侦察 ===");
            sb.AppendLine("触发: " + reason);
            sb.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("frame: " + Time.frameCount + "   场景: " + SceneName());
            sb.AppendLine("Image 组件: " + recs.Count);
            sb.AppendLine();

            Append("一、订单卡 / 座位号相关（路径或图名含 order / card / seat / guest / icon）",
                Pick(recs, IsOrderCard), sb, out _);
            sb.AppendLine();

            Append("二、疑似客人头像（图名含 guest / head / face / portrait / chara）",
                Pick(recs, IsAvatar), sb, out _);
            sb.AppendLine();

            Append("三、九宫格皮肤候选（border 非零 → 可直接 9-slice 画面板底/按钮）",
                Pick(recs, r => r.Border != Vector4.zero), sb, out _);
            sb.AppendLine();

            AppendAtlas(sb, recs);
            sb.AppendLine();

            sb.AppendLine("## 五、全部 Sprite 名（排序，xN = 被几个 Image 引用）");
            var names = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var r in recs) names[r.Sprite] = names.TryGetValue(r.Sprite, out var n) ? n + 1 : 1;
            if (names.Count == 0) sb.AppendLine("  （一张都没有）");
            foreach (var kv in names) sb.AppendLine("  " + kv.Key + "  x" + kv.Value);
            sb.AppendLine();

            sb.AppendLine("## 六、导出样例图（" + _outDir + "）");
            if (ExportLog.Count == 0) sb.AppendLine("  （没有候选）");
            foreach (var line in ExportLog) sb.AppendLine("  " + line);

            var file = Path.Combine(Paths.BepInExRootPath, "config", "orderhelper-ui-dump.txt");
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));

            OrderHelperPlugin.LogSource.LogInfo(
                "[OrderHelper] UI 侦察已写出: " + file +
                "  (Image=" + recs.Count + " 候选=" + Pending.Count + " 导出png=" + _seq + ")");
        }

        // ---------------------------------------------------------------- 收集

        private static List<Rec> Collect()
        {
            var recs = new List<Rec>(256);

            // FindObjectsOfTypeAll 会连未激活对象一起翻出来：订单卡/头像的图标池常常是失活的
            // 池化对象，FindObjectsOfType（只找激活的）会漏掉它们。这里要的就是"全部"。
            Image[] all = null;
            try { all = Resources.FindObjectsOfTypeAll<Image>(); }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] UI 侦察取不到 Image 列表: " + e.Message);
                return recs;
            }
            if (all == null) return recs;

            for (var i = 0; i < all.Length; i++)
            {
                Image img;
                try { img = all[i]; }
                catch { continue; }                       // 一次性跳过的也要有痕迹（静默失败纪律）
                if (img == null) continue;

                Sprite sp;
                try { sp = img.sprite; }
                catch { continue; }
                if (sp == null) continue;                 // 纯色 Image（sprite 为 null），不是头像也不是底图

                var rec = new Rec();

                try
                {
                    rec.Path = PathOf(img.transform);
                    rec.Sprite = string.IsNullOrEmpty(sp.name) ? "?" : sp.name;

                    var r = sp.rect;
                    rec.RectW = Mathf.RoundToInt(r.width);
                    rec.RectH = Mathf.RoundToInt(r.height);

                    var tr = sp.textureRect;
                    rec.TRx = tr.x; rec.TRy = tr.y; rec.TRw = tr.width; rec.TRh = tr.height;
                    rec.Packed = sp.packed;
                    rec.Border = sp.border;

                    var tex = sp.texture;
                    if (tex != null)
                    {
                        rec.Texture = string.IsNullOrEmpty(tex.name) ? "?" : tex.name;
                        rec.TexW = tex.width;
                        rec.TexH = tex.height;
                    }

                    var rt = img.rectTransform;
                    if (rt != null)
                    {
                        var s = rt.rect;                  // UI 空间的实际宽高，不受 scale 影响
                        rec.PosW = s.width;
                        rec.PosH = s.height;
                    }

                    rec.Active = img.gameObject.activeInHierarchy;
                }
                catch
                {
                    // 图集被卸载后访问 rect/texture 会抛；跨场景时 Il2Cpp 对象也随时可能失效。
                    continue;
                }

                recs.Add(rec);

                // 边扫边登记导出候选：此刻手上的 sp 才是能读到贴图的那个实例。
                rec.Priority = PriorityOf(rec);
                if (rec.Priority < 99)
                {
                    Pending.Add(rec);
                    PendingSprite[rec] = sp;
                }
            }

            return recs;
        }

        /// <summary>导出优先级：订单卡原件 > 图鉴客人立绘 > 疑似角色表情图。99 = 不导。</summary>
        private static int PriorityOf(Rec r)
        {
            var p = r.Path ?? string.Empty;
            var last = LastSeg(p);

            if (p.Contains("Order(Clone)")) return 0;
            if (p.Contains("GuestElement") || last == "AvatarMask") return 1;

            var s = (r.Sprite ?? string.Empty).ToLowerInvariant();
            if (s.Contains("main_") || s.Contains("eyes")) return 2;

            return 99;
        }

        private static string LastSeg(string path)
        {
            var i = path == null ? -1 : path.LastIndexOf('/');
            return i < 0 ? path ?? string.Empty : path.Substring(i + 1);
        }

        // ---------------------------------------------------------------- 导出

        private static void Export()
        {
            if (_outDir == null) return;

            Pending.Sort((a, b) =>
            {
                if (a.Priority != b.Priority) return a.Priority.CompareTo(b.Priority);
                return b.Active.CompareTo(a.Active);   // 同优先级下先看激活的（没失活的贴图才读得到）
            });

            var done = new HashSet<string>(StringComparer.Ordinal);

            foreach (var rec in Pending)
            {
                if (_seq >= MaxExport) { ExportLog.Add("达到上限 " + MaxExport + " 张，其余候选跳过"); break; }
                if (done.Contains(rec.Sprite)) continue;

                if (!PendingSprite.TryGetValue(rec, out var sp) || sp == null) continue;
                done.Add(rec.Sprite);
                TryExport(rec, sp);
            }
        }

        private static void TryExport(Rec rec, Sprite sp)
        {
            try
            {
                if (rec.RectW <= 0 || rec.RectW > 512 || rec.RectH > 512)
                {
                    ExportLog.Add("跳过 " + rec.Sprite + "：尺寸 " + rec.RectW + "x" + rec.RectH + " 不在 1..512");
                    return;
                }

                var src = sp.texture;
                if (src == null) { ExportLog.Add("跳过 " + rec.Sprite + "：texture 为空"); return; }
                if (src.width <= 0 || src.height <= 0)
                {
                    ExportLog.Add("跳过 " + rec.Sprite + "：图集未加载（texture " + src.width + "x" + src.height +
                                  "，active=" + rec.Active + "）");
                    return;
                }

                var bytes = CropToPng(sp, src);
                if (bytes == null) { ExportLog.Add("跳过 " + rec.Sprite + "：裁剪编码返回空"); return; }

                var file = _seq.ToString("00") + "_" + SafeName(rec.Sprite) + "_" +
                           rec.RectW + "x" + rec.RectH + ".png";
                File.WriteAllBytes(Path.Combine(_outDir, file), bytes);
                _seq++;
                ExportLog.Add("导出 " + file + "   ← " + LastSeg(rec.Path));
            }
            catch (Exception e)
            {
                ExportLog.Add("跳过 " + rec.Sprite + "：异常 " + e.Message);
            }
        }

        /// <summary>
        /// 按 sprite 在图集里的像素范围裁一块存成 PNG。
        /// 为什么要先 Blit 一遍：图集贴图通常关了 Read/Write，直接 GetPixels 会抛；过一道
        /// RenderTexture 拿可读副本（不改原图）。
        /// </summary>
        private static byte[] CropToPng(Sprite sp, Texture2D src)
        {
            Texture2D copy = null;
            Texture2D crop = null;
            RenderTexture rt = null;

            try
            {
                var w = src.width;
                var h = src.height;

                // Unity 2021+ 收窄了 GetTemporary 的重载，这里用最稳的 3 参版本。
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
                var cw = Mathf.Clamp(Mathf.RoundToInt(r.width), 1, 512);
                var ch = Mathf.Clamp(Mathf.RoundToInt(r.height), 1, 512);
                var cx = Mathf.Clamp(Mathf.RoundToInt(r.x), 0, Mathf.Max(0, w - 1));
                var cy = Mathf.Clamp(Mathf.RoundToInt(r.y), 0, Mathf.Max(0, h - 1));
                if (cx + cw > w) cw = w - cx;
                if (cy + ch > h) ch = h - cy;
                if (cw <= 0 || ch <= 0) return null;

                crop = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
                crop.SetPixels(copy.GetPixels(cx, cy, cw, ch));
                crop.Apply();

                // 注意：EncodeToPNG 在新版 Unity 里挪到了 ImageConversion（否则报 CS1061）。
                return ImageConversion.EncodeToPNG(crop);
            }
            finally
            {
                if (RenderTexture.active != null) RenderTexture.active = null;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (copy != null) UnityEngine.Object.Destroy(copy);
                if (crop != null) UnityEngine.Object.Destroy(crop);
            }
        }

        private static string SafeName(string s)
        {
            var bad = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(s ?? "?");
            foreach (var c in bad) sb.Replace(c.ToString(), "_");
            return sb.Length > 40 ? sb.ToString(0, 40) : sb.ToString();
        }

        // ---------------------------------------------------------------- 筛选与输出

        private delegate bool Filter(Rec r);

        private static List<Rec> Pick(List<Rec> recs, Filter f)
        {
            var list = new List<Rec>();
            foreach (var r in recs) { if (f(r)) list.Add(r); }
            return list;
        }

        private static bool IsOrderCard(Rec r)
        {
            var p = (r.Path ?? string.Empty).ToLowerInvariant();
            var s = (r.Sprite ?? string.Empty).ToLowerInvariant();
            return p.Contains("order") || p.Contains("card") || p.Contains("seat") || p.Contains("table") ||
                   s.Contains("order") || s.Contains("card") || s.Contains("seat");
        }

        private static bool IsAvatar(Rec r)
        {
            var s = (r.Sprite ?? string.Empty).ToLowerInvariant();
            var p = (r.Path ?? string.Empty).ToLowerInvariant();
            return s.Contains("guest") || s.Contains("head") || s.Contains("face") || s.Contains("portrait") ||
                   s.Contains("chara") || p.Contains("guest") || p.Contains("icon");
        }

        private static void Append(string title, List<Rec> pick, StringBuilder sb, out List<Rec> capped)
        {
            sb.AppendLine("## " + title);

            // 同一个 sprite 在多个 Image 上重复出现是常态，这里只留前 120 条，避免文件爆掉。
            var limit = Mathf.Min(pick.Count, 120);
            for (var i = 0; i < limit; i++) sb.AppendLine(pick[i].Render());
            if (pick.Count > limit) sb.AppendLine("  …… 另有 " + (pick.Count - limit) + " 条同类命中未列出");
            if (pick.Count == 0) sb.AppendLine("  （没有命中）");

            capped = pick;
        }

        private static void AppendAtlas(StringBuilder sb, List<Rec> recs)
        {
            sb.AppendLine("## 四、图集分组（同一张 texture 被几个 Image 引用 / 里面有哪些 sprite）");

            var groups = new Dictionary<string, List<Rec>>(StringComparer.Ordinal);
            foreach (var r in recs)
            {
                var key = r.Texture + " " + r.TexW + "x" + r.TexH;
                if (!groups.TryGetValue(key, out var list)) { list = new List<Rec>(); groups[key] = list; }
                list.Add(r);
            }

            var keys = new List<string>(groups.Keys);
            keys.Sort(StringComparer.Ordinal);

            foreach (var key in keys)
            {
                var list = groups[key];
                var sprites = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (var r in list) sprites[r.Sprite] = sprites.TryGetValue(r.Sprite, out var n) ? n + 1 : 1;

                sb.AppendLine("  " + key + "  引用=" + list.Count + "  sprite种类=" + sprites.Count);
                var shown = 0;
                foreach (var kv in sprites)
                {
                    if (shown++ >= 8) { sb.AppendLine("      …… 另 " + (sprites.Count - shown) + " 种"); break; }
                    sb.AppendLine("      " + kv.Key + " x" + kv.Value);
                }
            }
        }

        /// <summary>把 Transform 往上拼成 "Canvas/Panel/Child" 这样的路径，方便认位置。</summary>
        private static string PathOf(Transform t)
        {
            var parts = new List<string>(8);
            var depth = 0;

            while (t != null && depth++ < 10)
            {
                parts.Add(string.IsNullOrEmpty(t.name) ? "?" : t.name);
                t = t.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        private static string SceneName()
        {
            try
            {
                return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            }
            catch
            {
                return "?";
            }
        }
    }
}
