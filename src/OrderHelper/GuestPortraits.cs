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
    /// 客人头像缓存（批3）：把游戏订单卡上的角色立绘抓下来，贴进我们面板的头像位。
    ///
    /// 素材从哪来（见 docs/UI侦察报告.md）：
    ///   订单卡 Order/.../SpecialObject/Char 上挂的 sprite，命名规律是「角色名 + Main_0, 1」
    ///   （例如「慧音Main_0, 1」「響子-工作服Main_0, 1」），64x64，图集切片。
    ///   同一角色还有一层「角色名 + Eyes_0, 1」（Face 节点），是眼睛/表情，**必须叠在身体层上**
    ///   ——只画身体层就是「有身子没脸」。
    ///   ★ 只有稀客单才有 Char 图层，常客单走 NormalObject_*（只有菜/酒图标）→ 常客取不到头像，正常。
    ///
    /// 四条纪律（跟 UiSkin 一致）：
    ///   1. **贴图是自己的副本**：不持有游戏的 Sprite/Texture，做成我们自己的 Texture2D。
    ///      跨场景不失效，也不用处理图集 UV 翻转。
    ///      ★ 游戏 Sprite 对象本身也**不能长期持有**（换场景后 IL2CPP 指针会悬空），
    ///        所以现扫现做、做完立刻转成自持贴图，不把 Sprite 存进字段。
    ///   2. **只回读用到的那一小块**：绝不把 2048² 图集整张 Blit 回读。
    ///      整图回读一次是 16MB，实测会顶出一条 248ms 的长帧（卡一下很显眼）；
    ///      改成按 sprite.textureRect 只回读 40x60 左右的一小块，单次 ~10KB。
    ///   3. **扫描要分级、要有衰减**：挂 UI 的 Image 数量远少于全场 Sprite，日常用 Image 级快扫；
    ///      只有「确实缺人头像」时才做一次全 Sprite 级慢扫，且限频限次。
    ///      预加载做完就按指数退避停手，不然后台每 0.5 秒枚举上万对象，那才是真正的卡顿源。
    ///   4. **跨场景不失效**：自持贴图不受 IL2CPP 对象失效影响，换场景只重置预算（ResetBudget）。
    /// </summary>
    internal static class GuestPortraits
    {
        /// <summary>身体层 sprite 名的统一后缀；「Main_」之前的部分就是角色名。</summary>
        private const string Suffix = "Main_";

        /// <summary>眼神层（表情）后缀，必须叠在身体层上。</summary>
        private const string EyeSuffix = "Eyes_";

        private const int MaxDryRuns = 12;
        private const float ScanInterval = 2f;

        /// <summary>
        /// 全 Sprite 级慢扫的最小间隔（秒）。这个扫一次很贵，绝不能勤扫。
        /// ★ v1.6.7：12 秒 → 45 秒。`Resources.FindObjectsOfTypeAll&lt;Sprite&gt;()` 是一次性 API，
        ///   中途停不下来，实测一次 177–288ms —— 12 秒来一发就是每 12 秒固定顿一下，
        ///   玩家描述为「有时候会卡」。慢扫的作用只是捞「没挂 UI 的图集切片」，
        ///   这类图整局不会变多，没必要勤扫。
        /// </summary>
        private const float DeepScanInterval = 45f;

        /// <summary>
        /// 整局最多做几次全量慢扫。慢扫能捞到的图是固定的，捞不到就是捞不到，
        /// 无限重试纯属白卡；到上限后彻底放弃，缺的人继续用首字占位。
        /// </summary>
        private const int MaxDeepRuns = 5;

        /// <summary>每次后台摊销的单帧时间预算（毫秒）——超了就下一帧继续，绝不让头像拖出卡帧。</summary>
        private const float FrameBudgetMs = 2f;

        /// <summary>
        /// 时间预算的检查粒度（每看几个 Image 查一次时钟）。
        /// ★ v1.6.7：64 → 8。原来一批检查一次，一批撞到 40ms 也发现不了，
        ///   2ms 的预算形同虚设（实测单次扫描均值 46ms、峰值 291ms）。
        /// </summary>
        private const int BudgetCheckEvery = 8;

        /// <summary>
        /// 一趟最多看多少个 Image。原来是「每次调用都把全场 Image 全过一遍」，
        /// 场景里几千个 Image 就是 50–90ms 的卡顿（实测采集峰值全在头像这一段）。
        /// 现在改成带游标的切片扫描：每次只看一小段，几趟拼成一整轮，单次开销封顶。
        /// ★ v1.6.7：900 → 400，配合 8 个一查的预算检查，单趟直接压到几毫秒。
        /// </summary>
        private const int ImageSlice = 400;

        /// <summary>Image 快照的有效期（秒）。过期才重新枚举一次——枚举本身才是大头。</summary>
        private const float ImageSnapshotTtl = 4f;

        /// <summary>缓存上限，防止把图集里所有角色的图都做一遍。</summary>
        private const int MaxCharacters = 120;

        /// <summary>
        /// 输出贴图的边长，跟面板头像位 OverlayBehaviour.AvatarSize 保持一致。
        /// ★ 为什么要在拷贝时就缩好：IMGUI 画 GUIContent 里的图片是**按原始尺寸**画的，不会自动缩，
        ///   64x64 的立绘直接塞进 48px 的按钮会溢出去盖住旁边文字。
        /// </summary>
        private const int OutSize = 48;

        /// <summary>
        /// 头像取景：取身体内容高度的这个比例做「头肩特写」。
        /// ★ 为什么不按整个人物的不透明包围盒缩：像慧音这种长发角色，包围盒又高又满，
        ///   一缩进 48px 脸只剩一小块、看着像被头发糊住了。头肩特写才是头像该有的样子。
        /// ★ 这个值决定「脸有多大」（0.66 实测大小正合适），下移多少用 HeadShiftDown 单独调——
        ///   之前上沿死贴内容顶部，下沿刚好切在眼睛下方，下巴和嘴全没了。
        /// </summary>
        private const float HeadCropRatio = 0.72f;

        /// <summary>
        /// 裁剪框从内容顶部往下的偏移（占边长比例）：把帽子顶裁掉一点，换来下巴和肩膀进框。
        /// ★ 实测 0.18 刚好；调大 = 更贴脸但会切帽子，调小 = 露出更多帽子但脸更靠下。
        /// </summary>
        private const float HeadShiftDown = 0.18f;

        /// <summary>alpha 低于这个值视为透明（立绘边缘的半透明噪点不算内容）。</summary>
        private const float AlphaCut = 0.06f;

        private static readonly Dictionary<string, Texture2D> Map = new();

        // ---- 扫描侧的摊销状态（v1.5.6 为治卡顿而加）----
        // 原来每次扫描都：①枚举全场 Image（几千个，50–90ms）②把每个的 sprite 读一遍。
        // 现在：图像快照缓存 4 秒 + 游标切片，每次只走一小段，几趟拼成一轮。

        /// <summary>已发现的「身体层 / 眼神层」sprite，按内部名累积。跨切片复用，表情层才配得齐。</summary>
        private static readonly Dictionary<string, Sprite> BodySprites = new();
        private static readonly Dictionary<string, Sprite> EyeSprites = new();

        private static Image[] _imgSnapshot;
        private static float _imgSnapshotAt = -999f;
        private static int _imgCursor;

        /// <summary>
        /// 是否已经把当前快照整轮扫完。分片扫描时身体层和表情层可能落在不同切片里，
        /// 这轮没扫完就合成会得到「没有表情层」的头像——所以合成必须等整轮扫完。
        /// </summary>
        private static bool _imgSwept;

        /// <summary>
        /// 已经取过名字的 sprite 指针（v1.6.7）。
        /// 图集里几百个 sprite 会被成千上万个 Image 反复引用，而 `sprite.name` 每次都要
        /// 跨 il2cpp 封送一个字符串 —— 这是「一次扫描 46ms」里最贵的一步。
        /// 同一个 sprite 只读一次名字，后面的引用直接跳过。
        /// 指针复用的风险：这些是图集切片，随场景常驻，不存在扫描期间被释放的情况。
        /// </summary>
        private static readonly HashSet<IntPtr> SeenSprites = new();

        /// <summary>上次真正执行扫描的时刻（用来避免同一帧里 Refresh 和 Prefetch 各扫一遍）。</summary>
        private static float _lastBuildAt = -999f;

        /// <summary>全量慢扫已经做过几次（到 MaxDeepRuns 就彻底不做了）。</summary>
        private static int _deepRuns;

        /// <summary>
        /// 查不到头像的负缓存：名字 → 当时的 Map 大小。
        /// Map 长大（新立绘被发现）才重试；否则没头像的客人每 0.5 秒都要线性扫一遍全表。
        /// </summary>
        private static readonly Dictionary<string, int> MissAt = new();

        /// <summary>
        /// 汉字变体归一表：立绘 sprite 名可能用日文原字（華/覚/霧/雛），面板显示名是简体中文（华/觉/雾/雏），
        /// 逐字归一后再做包含匹配，省得给每个角色都维护一条别名。
        /// </summary>
        private static readonly Dictionary<char, char> Variants = new()
        {
            ['華'] = '华', ['覚'] = '觉', ['霧'] = '雾', ['雛'] = '雏', ['鍵'] = '键',
            ['響'] = '响', ['夢'] = '梦', ['輝'] = '辉', ['遠'] = '远', ['塚'] = '冢',
            ['綾'] = '绫', ['姫'] = '姬', ['鈴'] = '铃', ['桜'] = '樱', ['憑'] = '凭',
            ['頼'] = '赖', ['豊'] = '丰', ['廃'] = '废', ['々'] = '々', ['値'] = '值',
        };

        /// <summary>
        /// 个别角色的立绘名和显示名差得太远（火焰猫燐的日文简称是「お燐」），归一救不了，只能列别名。
        /// key = 面板显示名，value = 立绘里可能出现的名字；命中任何一个即可。
        /// </summary>
        private static readonly Dictionary<string, string[]> Aliases = new()
        {
            ["火焰猫燐"] = new[] { "お燐", "阿燐", "焰" },
            ["古明地觉"] = new[] { "こいし", "觉" },
            ["古明地恋"] = new[] { "こいし", "恋" },
            ["雾雨魔理沙"] = new[] { "魔理沙", "Marisa" },
            ["博丽灵梦"] = new[] { "灵梦", "霊夢", "Reimu" },
            ["魂魄妖梦"] = new[] { "妖梦", "妖夢", "Youmu" },
            ["十六夜咲夜"] = new[] { "咲夜", "Sakuya" },
            ["茨木华扇"] = new[] { "华扇", "華扇", "Kasen" },
            ["藤原妹红"] = new[] { "妹红", "妹紅" },
            ["上白泽慧音"] = new[] { "慧音", "恵音" },
            ["八云紫"] = new[] { "紫", "Yukari" },
            ["蕾米莉亚"] = new[] { "蕾米", "Remilia" },
            ["芙兰朵露"] = new[] { "芙兰", "Flandre" },
            ["帕秋莉"] = new[] { "帕秋莉", "Patchouli" },
        };

        /// <summary>
        /// 人工映射表：显示名 → 立绘内部名（sprite 名里「Main_」之前那段）。
        /// ★ 为什么要有这层：自动匹配靠「内部名里含显示名的字」，游戏一旦用代号或怪写法
        ///   （例如「11_咲夜」这种带编号前缀的、或者纯英文缩写），再多的别名表也猜不到。
        ///   有了这张表，缺谁就在 config\orderhelper-portraits.txt 里填一行，存盘重开游戏即可，
        ///   **不用改代码、不用重新编译**。
        /// </summary>
        private static readonly Dictionary<string, string> Overrides = new();

        /// <summary>
        /// 外部头像：用户在 `config\orderhelper-portraits\` 里放的 PNG/JPG，文件名就是客人名。
        /// ★ 这条路是**最高优先级**，比任何名字匹配都靠前——因为它是人眼确认过的，不可能错。
        ///   也正因为如此，它顺便解决了「内部名猜不出来」的老问题：不需要知道游戏里叫什么，
        ///   只要文件名和面板上显示的名字一致就行。
        /// </summary>
        private static readonly Dictionary<string, Texture2D> External = new();

        private static bool _externalLoaded;


        private static bool _overridesLoaded;

        private static float _nextScan;
        private static float _nextDeep;
        private static float _nextPrefetch;

        /// <summary>预加载退避间隔：一次没新增就翻倍，有新增就回到基准。防止空转扫全场。</summary>
        private static float _prefetchGap = 1f;

        private static bool _stopLogged;
        private static bool _errorLogged;

        /// <summary>取景参数日志已输出几条（只记前 8 个角色，够算参数又不刷屏）。</summary>
        private static int _cropLogs;

        /// <summary>连续「一无所获」的扫描次数；有收获归零，连续空手到上限就彻底停手。</summary>
        private static int _dryRuns;

        /// <summary>当前缺头像的客人名单快照，写诊断日志用。</summary>
        private static string _missingText = "";

        internal static int Count => Map.Count;

        /// <summary>映射表文件名（放在 BepInEx\config 下）。</summary>
        private const string OverrideFile = "orderhelper-portraits.txt";

        /// <summary>F11 生成的内部名清单文件名。</summary>
        private const string KeyListFile = "orderhelper-portraits-keys.txt";

        private const string OverrideTemplate =
@"# 夜雀点单助手 —— 客人头像映射表
#
# 什么时候需要改这里：面板上某个稀客一直是「无头像」（只有首字占位），
# 说明游戏里她的立绘内部名猜不出来。补一行就永久解决，改完存盘、重开游戏生效。
#
# 写法（等号左边是游戏内部名，右边是面板上显示的客人名）：
#     華扇 = 茨木华扇
#     11_咲夜 = 十六夜咲夜
#
# 内部名怎么查：游戏里按 F10（导出原图到 config\orderhelper-ui-samples，
#   文件名就带内部名），或按 F11（把场上所有立绘内部名列成清单）。
#
# 以 # 开头的行是注释，空行忽略。下面按需添加：

";

        /// <summary>
        /// 读人工映射表。首次运行会生成一份带注释的模板，免得用户不知道格式。
        /// ★ 只在插件 Load 时调一次：这张表是静态配置，没必要每帧看。
        /// </summary>
        internal static void LoadOverrides()
        {
            if (_overridesLoaded) return;
            _overridesLoaded = true;

            try
            {
                var file = Path.Combine(Paths.BepInExRootPath, "config", OverrideFile);

                if (!File.Exists(file))
                {
                    File.WriteAllText(file, OverrideTemplate, new UTF8Encoding(false));
                    OrderHelperPlugin.LogSource.LogInfo("[OrderHelper] 已生成头像映射表模板：" + file);
                    return;
                }

                var loaded = 0;
                foreach (var raw in File.ReadAllLines(file, Encoding.UTF8))
                {
                    // 某些编辑器会用带 BOM 的 UTF-8，第一行会残留 \uFEFF
                    var line = (raw ?? "").Trim().TrimStart('\uFEFF');
                    if (line.Length == 0) continue;
                    if (line[0] == '#' || line.StartsWith("//", StringComparison.Ordinal)) continue;

                    var eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    var display = line.Substring(0, eq).Trim();
                    var internalName = line.Substring(eq + 1).Trim();
                    if (display.Length == 0 || internalName.Length == 0) continue;

                    Overrides[display] = internalName;
                    loaded++;
                }

                if (loaded > 0)
                {
                    var pairs = new List<string>(loaded);
                    foreach (var kv in Overrides) pairs.Add(kv.Key + " ← " + kv.Value);
                    pairs.Sort(StringComparer.Ordinal);
                    OrderHelperPlugin.LogSource.LogInfo(
                        "[OrderHelper] 头像映射表载入 " + loaded + " 条：" + string.Join(" / ", pairs));
                }
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 读取头像映射表失败: " + e.Message);
            }
        }

        /// <summary>外部头像目录名（放在 BepInEx\config 下）。</summary>
        private const string ExternalDir = "orderhelper-portraits";

        private const string ExternalReadme =
@"夜雀点单助手 —— 自定义头像目录
================================

把头像图片（PNG / JPG）放进这个文件夹，**文件名就是客人在面板上的名字**，
下次启动游戏自动用上，优先级最高（会盖过从游戏里自动抓的立绘）。

举例：
    茨木华扇.png
    博丽灵梦.png
    雾雨魔理沙.png
    火焰猫燐.png

要求：
  · 正方形最好（不是正方形会按中心裁成正方形，可能切边）。
  · 至少 48×48；给 96×96 或 128×128 缩小后更清晰。
  · 只截头像本身，不要带周围的面板背景和文字。
  · 名字写错就会当成「另一个客人」，面板上那位就还是占位。

改完不用重启也可以，但要重新进入有客人的场景才会重建头像缓存。
";

        /// <summary>
        /// 第一次用到头像时懒加载外部目录。
        /// ★ 为什么不在插件 Load 里读：那时候渲染上下文可能还没就绪，
        ///   `Graphics.Blit` / `LoadImage` 有概率失败。放在第一次查询（采集阶段）里做最稳。
        /// </summary>
        private static void EnsureExternalLoaded()
        {
            if (_externalLoaded) return;
            _externalLoaded = true;

            var dir = "";
            try
            {
                dir = Path.Combine(Paths.BepInExRootPath, "config", ExternalDir);
                Directory.CreateDirectory(dir);

                var readme = Path.Combine(dir, "说明.txt");
                if (!File.Exists(readme))
                    File.WriteAllText(readme, ExternalReadme, new UTF8Encoding(false));

                var loaded = 0;
                var failed = 0;

                foreach (var file in Directory.GetFiles(dir))
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") continue;

                    var name = Path.GetFileNameWithoutExtension(file).Trim();
                    if (name.Length == 0 || External.ContainsKey(name)) continue;

                    try
                    {
                        var bytes = File.ReadAllBytes(file);
                        var tmp = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (!ImageConversion.LoadImage(tmp, bytes))
                        {
                            UnityEngine.Object.Destroy(tmp);
                            failed++;
                            continue;
                        }

                        var tex = FitSquare(tmp);
                        UnityEngine.Object.Destroy(tmp);

                        if (tex == null) { failed++; continue; }

                        External[name] = tex;

                        // 顺带把归一化的名字也登记一份：用户写「茨木華扇」而面板是「茨木华扇」也能命中
                        var norm = Norm(name);
                        if (norm != name && !External.ContainsKey(norm)) External[norm] = tex;

                        loaded++;
                    }
                    catch (Exception e)
                    {
                        failed++;
                        OrderHelperPlugin.LogSource.LogWarning(
                            "[OrderHelper] 外部头像读取失败 " + Path.GetFileName(file) + ": " + e.Message);
                    }
                }

                if (loaded > 0 || failed > 0)
                {
                    var names = new List<string>(External.Count);
                    foreach (var kv in External) names.Add(kv.Key);
                    names.Sort(StringComparer.Ordinal);

                    OrderHelperPlugin.LogSource.LogInfo(
                        "[OrderHelper] 外部头像载入 " + loaded + " 张" +
                        (failed > 0 ? "（失败 " + failed + "）" : "") + "：" + string.Join(" / ", names));
                }
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 外部头像目录不可用: " + e.Message);
            }
        }

        /// <summary>
        /// 把任意尺寸的图变成 48×48：非正方形按中心裁正方形，再缩到目标尺寸。
        /// 本来就是 48×48 的直接拷像素（少一次重采样，最清晰）。
        /// </summary>
        private static Texture2D FitSquare(Texture2D src)
        {
            if (src == null || src.width <= 0 || src.height <= 0) return null;

            var w = src.width;
            var h = src.height;

            try
            {
                if (w == OutSize && h == OutSize)
                {
                    var same = new Texture2D(OutSize, OutSize, TextureFormat.RGBA32, false);
                    same.SetPixels(src.GetPixels());
                    same.Apply();
                    same.wrapMode = TextureWrapMode.Clamp;
                    same.hideFlags = HideFlags.HideAndDontSave;
                    return same;
                }

                var side = Mathf.Min(w, h);
                var scale = new Vector2((float)side / w, (float)side / h);
                var offset = new Vector2((w - side) * 0.5f / w, (h - side) * 0.5f / h);

                RenderTexture rt = null;
                try
                {
                    rt = RenderTexture.GetTemporary(OutSize, OutSize, 0);
                    RenderTexture.active = rt;
                    Graphics.Blit(src, rt, scale, offset);

                    var tex = new Texture2D(OutSize, OutSize, TextureFormat.RGBA32, false);
                    tex.ReadPixels(new Rect(0, 0, OutSize, OutSize), 0, 0);
                    tex.Apply();
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.hideFlags = HideFlags.HideAndDontSave;
                    return tex;
                }
                finally
                {
                    RenderTexture.active = null;
                    if (rt != null) RenderTexture.ReleaseTemporary(rt);
                }
            }
            catch (Exception e)
            {
                LogErrorOnce("缩放外部头像失败", e.Message);
                return null;
            }
        }

        /// <summary>
        /// F11：把场上所有立绘的内部名导成一份清单，标出哪些还没对上头像。
        /// 只读 sprite 的名字，不做像素回读，一次几十毫秒，可以随手按。
        /// ★ 这是给「自动匹配失败」用的诊断工具：看到「[缺] 華扇」，就知道该在映射表里填「華扇 = 茨木华扇」。
        /// </summary>
        internal static void DumpKeys()
        {
            var keys = new List<string>();

            try
            {
                var all = Resources.FindObjectsOfTypeAll<Sprite>();
                if (all != null)
                {
                    for (var i = 0; i < all.Length; i++)
                    {
                        string n;
                        try
                        {
                            var sp = all[i];
                            if (sp == null) continue;
                            n = sp.name;
                        }
                        catch { continue; }

                        if (string.IsNullOrEmpty(n)) continue;
                        var cut = n.IndexOf(Suffix, StringComparison.Ordinal);
                        if (cut <= 0) continue;

                        var key = n.Substring(0, cut);
                        if (!keys.Contains(key)) keys.Add(key);
                    }
                }
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 列立绘名失败: " + e.Message);
            }

            keys.Sort(StringComparer.Ordinal);

            var missing = 0;
            var sb = new StringBuilder();
            sb.AppendLine("# 立绘内部名清单（游戏里按 F11 生成，每次覆盖）");
            sb.AppendLine("# [有] = 已经有头像了；[缺] = 还没对上，需要去 orderhelper-portraits.txt 里加一行");
            sb.AppendLine("# 共 " + keys.Count + " 位立绘被游戏加载；当前已缓存头像 " + Map.Count + " 张");
            sb.AppendLine("#");
            sb.AppendLine("# [状态] 内部名");

            foreach (var key in keys)
            {
                var has = Map.ContainsKey(key);
                if (!has) missing++;
                sb.AppendLine((has ? "[有] " : "[缺] ") + key);
            }

            try
            {
                var file = Path.Combine(Paths.BepInExRootPath, "config", KeyListFile);
                File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
                OrderHelperPlugin.LogSource.LogInfo(
                    "[OrderHelper] F11 立绘清单：" + keys.Count + " 位（缺头像 " + missing + "）→ " + file);
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 写立绘清单失败: " + e.Message);
            }
        }

        /// <summary>场景切换时重置扫描预算（贴图是自己持有的，Map 不用清）。</summary>
        internal static void ResetBudget()
        {
            _dryRuns = 0;
            _stopLogged = false;
            _nextScan = 0f;
            _nextDeep = 0f;
            _nextPrefetch = 0f;
            _prefetchGap = 1f;
            _deepRuns = 0;
            _lastBuildAt = -999f;

            // 扫描侧缓存必须清：Image / Sprite 都是场景对象，换场景后全是失效包装。
            _imgSnapshot = null;
            _imgSnapshotAt = -999f;
            _imgCursor = 0;
            _imgSwept = false;
            BodySprites.Clear();
            EyeSprites.Clear();
            MissAt.Clear();

            // sprite 指针换场景后失效，而且地址可能被复用成别的对象 —— 不清会导致新场景
            // 里某些立绘被误判成「already seen」而永远读不到名字（头像再也出不来）。
            SeenSprites.Clear();
        }

        /// <summary>逐字归一：華→华、覚→觉 这类变体，省得逐角色列别名。</summary>
        private static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var ch in s)
                sb.Append(Variants.TryGetValue(ch, out var rep) ? rep : ch);
            return sb.ToString();
        }

        /// <summary>
        /// 按客人名取头像。依次试：精确 → 别名表 → 归一后双向包含（「魂魄妖梦」↔「妖梦」）。
        /// 匹配上了就记进 Map，下次不用再遍历。取不到返回 null（调用方画占位字）。
        /// </summary>
        internal static Texture2D Get(string guestName)
        {
            if (string.IsNullOrEmpty(guestName)) return null;

            // 外部目录只读一次（第一次查询时懒加载，见 EnsureExternalLoaded）
            EnsureExternalLoaded();

            // ① 人眼确认过的外部图：文件名就是客人名，优先级最高，连别名都不用走。
            //    这一条让「内部名猜不出来」不再是问题——玩家自己放图即可。
            if (External.TryGetValue(guestName, out var ext)) return ext;

            if (Map.TryGetValue(guestName, out var hit))
            {
                MissAt.Remove(guestName);   // 上次没找到、这次找到了：负缓存作废
                return hit;
            }

            // ② 归一化名字再试一次（用户可能写成「茨木華扇」而面板是「茨木华扇」）
            var normWant = Norm(guestName);
            if (External.TryGetValue(normWant, out ext)) return ext;

            // ②.5 外部目录也走一次双向包含：游戏显示名可能是「咲夜」，玩家文件名是「十六夜咲夜」，
            //     反之亦然。没有这一步，名字差一个字就等于没放图，玩家很难自己看出来哪里错了。
            //
            // ★ 负缓存：下面两段都是线性扫描（每项还要做一次逐字归一，会分配字符串）。
            //   没头像的客人每 0.5 秒来问一次，不给负缓存就是稳定的每帧垃圾。
            //   头像池（Map）长大 = 有新图被发现 = 值得重试一次。
            if (MissAt.TryGetValue(guestName, out var missedAt) && missedAt == Map.Count) return null;

            foreach (var kv in External)
            {
                if (kv.Key.Length < 2) continue;
                var key = Norm(kv.Key);
                if (normWant.IndexOf(key, StringComparison.Ordinal) >= 0 ||
                    key.IndexOf(normWant, StringComparison.Ordinal) >= 0)
                {
                    Map[guestName] = kv.Value;
                    return kv.Value;
                }
            }


            // 人工映射表优先于一切猜测：用户在 config 里指定了「显示名 = 内部名」就直接用。
            // ★ 放在别名和模糊匹配之前——手动填的永远比猜的准，也不用担心「华扇」被猜成别人。
            if (Overrides.TryGetValue(guestName, out var forcedName) &&
                Map.TryGetValue(forcedName, out hit))
            {
                Map[guestName] = hit;
                return hit;
            }

            if (Aliases.TryGetValue(guestName, out var aliases))
            {
                foreach (var alias in aliases)
                {
                    if (Map.TryGetValue(alias, out hit)) { Map[guestName] = hit; return hit; }
                }
            }

            var want = Norm(guestName);
            foreach (var kv in Map)
            {
                if (kv.Key.Length == 0) continue;
                var key = Norm(kv.Key);
                if (want.IndexOf(key, StringComparison.Ordinal) >= 0 ||
                    key.IndexOf(want, StringComparison.Ordinal) >= 0)
                {
                    Map[guestName] = kv.Value;   // 记住这次匹配结果
                    MissAt.Remove(guestName);
                    return kv.Value;
                }
            }

            // 这次确实没有：记下当时的池子大小，池子不长大就不再重扫。
            MissAt[guestName] = Map.Count;
            return null;
        }

        internal static bool Has(string guestName) => Get(guestName) != null;

        /// <summary>
        /// 还有客人没头像时才扫。传进来的应该是「当前缺头像的稀客名」列表。
        /// 由采集阶段（0.5 秒一次）调用。
        /// ★ 首次出现的新客人不排队：直接扫（她都点单了，头像还在占位，等不得这 2 秒）。
        /// </summary>
        internal static void RefreshIfNeeded(List<string> missing)
        {
            if (Map.Count >= MaxCharacters) return;
            if (missing == null || missing.Count == 0) return;
            if (_dryRuns >= MaxDryRuns) return;

            var now = Time.realtimeSinceStartup;
            if (now < _nextScan) return;
            _nextScan = now + ScanInterval;

            _missingText = string.Join(" / ", missing);

            // 先用便宜的 Image 级扫一遍；还缺、且距上次深扫够久，才做全 Sprite 级慢扫
            // （有些角色的图集切片没挂在任何 Image 上，只有全扫才看得见）。
            var before = Map.Count;
            var got = BuildPass(missing, FrameBudgetMs, false);

            if (got == 0 && now >= _nextDeep && _deepRuns < MaxDeepRuns)
            {
                _nextDeep = now + DeepScanInterval;
                _deepRuns++;
                got += BuildPass(missing, FrameBudgetMs, true);
            }

            if (Map.Count > before)
            {
                _dryRuns = 0;
                OrderHelperPlugin.LogSource.LogInfo(
                    "[OrderHelper] 头像新增 " + (Map.Count - before) + " 位（共 " + Map.Count +
                    "）：" + KeyList());
            }
            else
            {
                _dryRuns++;

                if (_dryRuns >= MaxDryRuns && !_stopLogged)
                {
                    _stopLogged = true;
                    OrderHelperPlugin.LogSource.LogInfo(
                        "[OrderHelper] 头像扫描连续 " + MaxDryRuns + " 次无收获，停手。已缓存 " + Map.Count +
                        " 位：" + KeyList() +
                        "；仍缺的客人用首字占位。缺：" + _missingText +
                        " —— 若立绘命名与显示名不一致，需要补别名映射（见 docs/UI美化开发计划.md）");
                }
            }
        }

        /// <summary>
        /// 后台摊销预加载：趁客人还没点单，把图集里已加载的立绘分批做成头像。
        /// 只做 Image 级快扫（便宜），且**指数退避**——做完一轮后逐渐拉到 30 秒一次，
        /// 避免「每 0.5 秒枚举上万个 Sprite」这种把游戏拖卡的行为。
        /// </summary>
        internal static void PrefetchTick()
        {
            if (Map.Count >= MaxCharacters) return;

            var now = Time.realtimeSinceStartup;

            // 同一次采集里 RefreshIfNeeded 刚扫过就不再扫第二遍（v1.6.7）。
            // 这两者都由采集节拍驱动，原本可能在同一 tick 各扫一趟，
            // 实测量到的「头像·扫描」均值就是这么翻倍的。
            if (now - _lastBuildAt < 0.4f) return;

            if (now < _nextPrefetch) return;
            _nextPrefetch = now + _prefetchGap;

            var before = Map.Count;
            BuildPass(null, FrameBudgetMs, false);

            if (Map.Count > before)
            {
                _prefetchGap = 2f;                    // 还有新货：保持勤快
            }
            else
            {
                _prefetchGap = Mathf.Min(_prefetchGap * 2f, 30f);   // 没新货：逐步退避到 30 秒
            }
        }

        private static string KeyList()
        {
            var names = new List<string>(Map.Keys);
            names.Sort(StringComparer.Ordinal);
            return string.Join(" / ", names);
        }

        /// <summary>
        /// 一趟处理：把已加载的角色立绘做成自己的贴图。
        /// </summary>
        /// <param name="wanted">只优先做这几位；为 null 就按发现顺序做，直到用完时间预算。</param>
        /// <param name="budgetMs">本次最多花多少毫秒。</param>
        /// <param name="deep">
        /// false = 只扫挂 UI 的 Image（快）；
        /// true = 扫全场 Sprite（慢，但能捞到没挂 UI 的图集切片）。
        /// </param>
        private static int BuildPass(List<string> wanted, float budgetMs, bool deep)
        {
            // 包一层只是为了计时：内部有多个提早 return（没图 / 这一轮还没扫完），
            // 用 try/finally 才能保证「扫了多久」一定被记上。
            var scanStart = Perf.Start();
            try
            {
                return BuildPassCore(wanted, budgetMs, deep);
            }
            finally
            {
                _lastBuildAt = Time.realtimeSinceStartup;
                Perf.End("头像·扫描", scanStart);
            }
        }

        private static int BuildPassCore(List<string> wanted, float budgetMs, bool deep)
        {
            // 累积字典：跨切片复用，身体层与眼神层才能配齐（否则切片正好切在两者之间就丢表情）。
            var bodies = BodySprites;
            var eyes = EyeSprites;

            try
            {
                if (deep)
                {
                    // 全量扫（FindObjectsOfTypeAll<Sprite>）是头像里最重的一刀，
                    // 单独计时，才知道「慢扫」到底值不值这个价。
                    var deepStart = Perf.Start();
                    try
                    {
                        var all = Resources.FindObjectsOfTypeAll<Sprite>();
                        if (all == null) return 0;
                        Collect(all, bodies, eyes);
                    }
                    finally
                    {
                        Perf.End("头像·全量", deepStart);
                        Perf.Count("全量扫");
                    }
                }
                else
                {
                    var imgs = ImageSnapshot();
                    if (imgs == null) return 0;
                    ScanImages(imgs, bodies, eyes, budgetMs);

                    // 还没把这一轮扫完就先不合：表情层可能在下一片里。
                    if (!_imgSwept) return 0;
                }
            }
            catch (Exception e)
            {
                LogErrorOnce("枚举立绘失败", e.Message);
                return 0;
            }

            if (bodies.Count == 0) return 0;

            // 优先做「正在缺」的：先按 wanted 顺序，再补其他人。
            var order = new List<string>();
            if (wanted != null)
            {
                foreach (var w in wanted)
                {
                    if (Map.ContainsKey(w)) continue;

                    // 有映射表就只认指定的那个内部名，不参与模糊匹配（避免抓成别人）
                    if (Overrides.TryGetValue(w, out var forcedKey))
                    {
                        if (bodies.ContainsKey(forcedKey) && !order.Contains(forcedKey)) order.Add(forcedKey);
                        continue;
                    }

                    if (bodies.ContainsKey(w)) { order.Add(w); continue; }
                    foreach (var kv in bodies)
                    {
                        if (Match(w, kv.Key)) { order.Add(kv.Key); break; }
                    }
                }
            }

            foreach (var kv in bodies)
            {
                if (!order.Contains(kv.Key)) order.Add(kv.Key);
            }

            var t0 = Time.realtimeSinceStartup;
            var added = 0;

            // 合成 = Graphics.Blit + ReadPixels + Apply，是真正「做图」的开销。
            // 它跟扫描分开计，才知道慢在读图还是慢在做图。
            var composeStart = Perf.Start();
            try
            {
                foreach (var key in order)
                {
                    if (Map.Count >= MaxCharacters) break;

                    // 第二个人起走时间预算：这一帧做不完就留到下一帧
                    if (added > 0 && (Time.realtimeSinceStartup - t0) * 1000f > budgetMs) break;

                    if (!bodies.TryGetValue(key, out var body)) continue;
                    eyes.TryGetValue(key, out var eye);

                    var tex = Compose(body, eye, key);
                    if (tex != null)
                    {
                        Map[key] = tex;
                        added++;
                    }
                }
            }
            finally
            {
                if (added > 0) Perf.End("头像·合成", composeStart);
            }

            return added;
        }

        /// <summary>
        /// 全场 Image 的快照。枚举本身（FindObjectsOfTypeAll）就是最贵的一步，
        /// 4 秒内重复用同一份数组；客人进店后新建的 Image 最多晚 4 秒才被看见，头像晚一点出无妨。
        /// </summary>
        private static Image[] ImageSnapshot()
        {
            var now = Time.realtimeSinceStartup;

            // 关键：快照要活到「整轮扫完 + TTL」才开始过期。
            // 否则图多的时候还没扫完就换了一份新数组，游标一直从头开始，永远扫不完。
            if (_imgSnapshot != null)
            {
                if (!_imgSwept) return _imgSnapshot;
                if (now - _imgSnapshotAt < ImageSnapshotTtl) return _imgSnapshot;
            }

            // FindObjectsOfTypeAll 本身就是最贵的一步，单独计时：
            // 「头像·扫描」慢的时候，要能立刻分辨是「枚举花时间」还是「逐个读 sprite 花时间」。
            var snapStart = Perf.Start();
            try { _imgSnapshot = Resources.FindObjectsOfTypeAll<Image>(); }
            catch { _imgSnapshot = null; }
            Perf.End("头像·快照", snapStart);
            Perf.Count("快照重建");

            _imgSnapshotAt = now;
            _imgCursor = 0;
            _imgSwept = false;
            return _imgSnapshot;
        }

        /// <summary>
        /// 按游标切片扫描：单次最多 ImageSlice 个、且不超过时间预算，剩下的下一趟接着来。
        /// 这是「不再每 2 秒卡一下 80ms」的关键——开销被封顶，摊到多帧里。
        /// </summary>
        private static void ScanImages(Image[] imgs, Dictionary<string, Sprite> bodies,
                                       Dictionary<string, Sprite> eyes, float budgetMs)
        {
            var len = imgs.Length;
            if (len == 0) return;

            if (_imgCursor >= len) _imgCursor = 0;

            var t0 = Time.realtimeSinceStartup;
            var end = Mathf.Min(len, _imgCursor + ImageSlice);
            var sinceCheck = 0;

            for (var i = _imgCursor; i < end; i++)
            {
                Sprite sp;
                try
                {
                    var img = imgs[i];
                    if (img == null) continue;
                    sp = img.sprite;
                }
                catch { continue; }

                Take(sp, bodies, eyes);

                // 每 BudgetCheckEvery 个查一次时钟（读计时器本身也不免费）。
                // 用计数器而不是 (i & 63)：_imgCursor 是 i+1，位与对齐会错位，
                // 有些切片里几乎永远撞不上检查点 → 预算失效。
                if (++sinceCheck >= BudgetCheckEvery)
                {
                    sinceCheck = 0;
                    if ((Time.realtimeSinceStartup - t0) * 1000f > budgetMs)
                    {
                        _imgCursor = i + 1;
                        return;
                    }
                }
            }

            _imgCursor = end;

            // 走到数组尾部 = 这一轮扫完了，可以开始合成头像。
            if (end >= len) _imgSwept = true;
        }

        private static void Collect(Sprite[] all, Dictionary<string, Sprite> bodies, Dictionary<string, Sprite> eyes)
        {
            for (var i = 0; i < all.Length; i++)
            {
                Sprite sp;
                try
                {
                    sp = all[i];
                    if (sp == null) continue;
                }
                catch { continue; }

                Take(sp, bodies, eyes);
            }
        }

        /// <summary>按名字把 sprite 分到「身体层 / 眼神层」，不认识的直接丢。</summary>
        private static void Take(Sprite sp, Dictionary<string, Sprite> bodies, Dictionary<string, Sprite> eyes)
        {
            if (sp == null) return;

            // 同一个 sprite 只读一次名字（v1.6.7）：读 name 是跨 il2cpp 封送字符串，
            // 而图集切片会被大量 Image 共享引用，不去重就等于把最贵的操作做几十遍。
            var ptr = IntPtr.Zero;
            try { ptr = sp.Pointer; } catch { }
            if (ptr != IntPtr.Zero)
            {
                if (SeenSprites.Contains(ptr)) return;
                if (SeenSprites.Count > 6000) SeenSprites.Clear();   // 上限保护，正常到不了
                SeenSprites.Add(ptr);
            }

            string name;
            try { name = sp.name; }
            catch { return; }
            if (string.IsNullOrEmpty(name)) return;

            var cut = name.IndexOf(Suffix, StringComparison.Ordinal);
            if (cut > 0)
            {
                var key = name.Substring(0, cut);
                if (!Map.ContainsKey(key) && !bodies.ContainsKey(key)) bodies[key] = sp;
                return;
            }

            var ecut = name.IndexOf(EyeSuffix, StringComparison.Ordinal);
            if (ecut > 0)
            {
                var key = name.Substring(0, ecut);
                if (!eyes.ContainsKey(key)) eyes[key] = sp;
            }
        }

        /// <summary>归一后的双向包含判定，跟 <see cref="Get"/> 保持一致。</summary>
        private static bool Match(string guestName, string key)
        {
            if (guestName == key) return true;
            var a = Norm(guestName);
            var b = Norm(key);
            return a.IndexOf(b, StringComparison.Ordinal) >= 0 ||
                   b.IndexOf(a, StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// 合成一张 48x48 头像：身体层 + 眼神层 → 头肩特写 → 缩小。
        /// </summary>
        private static Texture2D Compose(Sprite body, Sprite eye, string key)
        {
            try
            {
                var rect = body.rect;
                var lw = Mathf.Clamp(Mathf.RoundToInt(rect.width), 1, 512);
                var lh = Mathf.Clamp(Mathf.RoundToInt(rect.height), 1, 512);

                var canvas = new Color[lw * lh];

                int ex = 0, ey = 0, ew = 0, eh = 0;
                if (!DrawLayer(body, canvas, lw, lh, false, out _, out _, out _, out _))
                    return null;
                if (eye != null)
                    DrawLayer(eye, canvas, lw, lh, true, out ex, out ey, out ew, out eh);

                // 内容包围盒（裁掉四周透明）
                var minX = lw; var minY = lh; var maxX = -1; var maxY = -1;
                for (var y = 0; y < lh; y++)
                {
                    var row = y * lw;
                    for (var x = 0; x < lw; x++)
                    {
                        if (canvas[row + x].a <= AlphaCut) continue;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
                if (maxX < minX || maxY < minY) return null;   // 全透明，不是有效立绘

                var cw = maxX - minX + 1;
                var ch = maxY - minY + 1;

                int side;
                int ox;
                int oy;

                if (ew > 0 && eh > 0)
                {
                    // ★ 有眼睛就用眼睛定位头部：以「内容高度 × HeadCropRatio」取一块头肩特写，
                    //   水平方向以眼睛为中线。长发角色再也不会把脸挤成一小块。
                    side = Mathf.Clamp(Mathf.RoundToInt(ch * HeadCropRatio), 12, Mathf.Max(cw, ch));

                    // 上沿 = 内容顶部再往下挪 HeadShiftDown×side（换下巴和肩膀进框）
                    oy = maxY - Mathf.RoundToInt(side * HeadShiftDown) - side + 1;

                    // 水平按眼睛居中。★ 注意不能把中心夹到「内容左右边界之内」：
                    //   像慧音 cw=42、裁 51 宽时，那个夹取会把图推到左边、右半边空一条透明，
                    //   表现为头像偏到框子一侧。这里只保证中心落在内容带上、允许裁框超出内容。
                    var eyeCx = ex + ew * 0.5f;
                    var inner = Mathf.Min(cw, side) * 0.5f;
                    if (cw > 1) eyeCx = Mathf.Clamp(eyeCx, minX + inner, maxX + 1 - inner);
                    ox = Mathf.RoundToInt(eyeCx - side * 0.5f);
                }
                else
                {
                    // 没有眼神层（理论上不会发生）：退回整块内容居中补方
                    side = Mathf.Max(cw, ch);
                    ox = minX - (side - cw) / 2;
                    oy = minY - (side - ch) / 2;
                }

                // 取景参数落一份日志（前 8 个角色就够）：以后嫌脸大切小、嫌切帽子都能直接算，
                // 不用再靠截图猜。只在内存里加过一次的角色会走到这里，不会刷屏。
                if (_cropLogs < 8)
                {
                    _cropLogs++;
                    OrderHelperPlugin.LogSource.LogInfo(
                        "[OrderHelper] 取景 " + key + " 内容" + cw + "x" + ch +
                        " 眼(" + ex + "," + ey + "," + ew + "," + eh + ")" +
                        " 裁" + side + "@" + ox + "," + oy);
                }

                var square = new Color[side * side];                for (var y = 0; y < side; y++)
                {
                    var sy = oy + y;
                    if (sy < 0 || sy >= lh) continue;
                    for (var x = 0; x < side; x++)
                    {
                        var sx = ox + x;
                        if (sx < 0 || sx >= lw) continue;
                        square[y * side + x] = canvas[sy * lw + sx];
                    }
                }

                var crop = new Texture2D(side, side, TextureFormat.RGBA32, false);
                crop.SetPixels(square);
                crop.Apply();
                crop.hideFlags = HideFlags.HideAndDontSave;

                // 缩到面板显示尺寸（IMGUI 不缩放 GUIContent 里的图片）。
                var small = RenderTexture.GetTemporary(OutSize, OutSize, 0);
                RenderTexture.active = small;
                Graphics.Blit(crop, small);

                var tex = new Texture2D(OutSize, OutSize, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, OutSize, OutSize), 0, 0);
                tex.Apply();

                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(small);
                UnityEngine.Object.Destroy(crop);

                tex.wrapMode = TextureWrapMode.Clamp;
                tex.hideFlags = HideFlags.HideAndDontSave;    // 自己持有整个会话，别被场景卸载回收

                return tex;
            }
            catch (Exception e)
            {
                LogErrorOnce("合成立绘失败", e.Message);
                return null;
            }
        }

        /// <summary>
        /// 把一层（身体或眼神）画进逻辑画布，并返回它落在画布上的像素矩形（眼神层定位头部要用）。
        /// ★ 对齐方式：sp.vertices 给出这层内容在「逻辑格」里的位置（局部坐标），
        ///   sp.textureRect 给出它在图集里的像素区域。两边一算就能确定缩放和落点，
        ///   不会因为图集紧压缩（tight packing）而错位。
        /// ★ 图集取图只回读这一小块（见类注释第 2 条纪律）。
        /// </summary>
        private static bool DrawLayer(Sprite sp, Color[] canvas, int lw, int lh, bool isEye,
                                      out int rx, out int ry, out int rw, out int rh)
        {
            rx = ry = rw = rh = 0;

            try
            {
                var r = sp.textureRect;
                var sw = Mathf.RoundToInt(r.width);
                var sh = Mathf.RoundToInt(r.height);
                if (sw <= 0 || sh <= 0) return false;

                var src = ReadRegion(sp, sw, sh);
                if (src == null) return false;

                // 目标落点：vertex 局部坐标 × ppu + pivot
                var dx = 0f;
                var dy = 0f;
                var dw = sw;
                var dh = sh;

                var verts = sp.vertices;
                if (verts != null && verts.Length >= 2)
                {
                    var minX = float.MaxValue; var minY = float.MaxValue;
                    var maxX = float.MinValue; var maxY = float.MinValue;
                    for (var i = 0; i < verts.Length; i++)
                    {
                        var v = verts[i];
                        if (v.x < minX) minX = v.x;
                        if (v.y < minY) minY = v.y;
                        if (v.x > maxX) maxX = v.x;
                        if (v.y > maxY) maxY = v.y;
                    }

                    var ppu = sp.pixelsPerUnit;
                    var pivot = sp.pivot;   // 逻辑格内的锚点像素位置
                    dx = minX * ppu + pivot.x;
                    dy = minY * ppu + pivot.y;
                    dw = Mathf.Max(1, Mathf.RoundToInt((maxX - minX) * ppu));
                    dh = Mathf.Max(1, Mathf.RoundToInt((maxY - minY) * ppu));
                }

                var x0 = Mathf.RoundToInt(dx);
                var y0 = Mathf.RoundToInt(dy);

                for (var y = 0; y < dh; y++)
                {
                    var syi = Mathf.Clamp(y * sh / dh, 0, sh - 1);
                    var ty = y0 + y;
                    if (ty < 0 || ty >= lh) continue;

                    for (var x = 0; x < dw; x++)
                    {
                        var sxi = Mathf.Clamp(x * sw / dw, 0, sw - 1);
                        var tx = x0 + x;
                        if (tx < 0 || tx >= lw) continue;

                        // 源over混合，保住眼神层的半透明边缘
                        var s = src[syi * sw + sxi];
                        if (s.a <= 0f) continue;

                        var idx = ty * lw + tx;
                        var d = canvas[idx];
                        var oa = s.a + d.a * (1f - s.a);
                        if (oa <= 0f) continue;

                        canvas[idx] = new Color(
                            (s.r * s.a + d.r * d.a * (1f - s.a)) / oa,
                            (s.g * s.a + d.g * d.a * (1f - s.a)) / oa,
                            (s.b * s.a + d.b * d.a * (1f - s.a)) / oa,
                            oa);
                    }
                }

                if (isEye)
                {
                    rx = x0; ry = y0; rw = dw; rh = dh;
                }

                return true;
            }
            catch (Exception e)
            {
                LogErrorOnce("画立绘图层失败", e.Message);
                return false;
            }
        }

        /// <summary>
        /// 只回读 sprite 在图集上的那一小块（Graphics.Blit 带 scale/offset 直接抠图）。
        /// ★ 千万别整图回读：2048² 是 16MB，实测会顶出一条 248ms 的长帧。
        /// </summary>
        private static Color[] ReadRegion(Sprite sp, int sw, int sh)
        {
            Texture2D tmp = null;
            RenderTexture rt = null;
            try
            {
                var src = sp.texture;
                if (src == null || src.width <= 0 || src.height <= 0) return null;

                var r = sp.textureRect;
                var scale = new Vector2((float)sw / src.width, (float)sh / src.height);
                var offset = new Vector2(r.x / src.width, r.y / src.height);

                rt = RenderTexture.GetTemporary(sw, sh, 0);
                RenderTexture.active = rt;
                Graphics.Blit(src, rt, scale, offset);

                tmp = new Texture2D(sw, sh, TextureFormat.RGBA32, false);
                tmp.ReadPixels(new Rect(0, 0, sw, sh), 0, 0);
                tmp.Apply();

                return tmp.GetPixels();
            }
            catch (Exception e)
            {
                LogErrorOnce("取立绘像素失败", e.Message);
                return null;
            }
            finally
            {
                RenderTexture.active = null;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (tmp != null) UnityEngine.Object.Destroy(tmp);
            }
        }

        private static void LogErrorOnce(string what, string detail)
        {
            if (_errorLogged) return;
            _errorLogged = true;
            OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 头像" + what + ": " + detail);
        }
    }
}
