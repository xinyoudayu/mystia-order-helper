using System;

using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

using HarmonyLib;

using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;

using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace MystiaOrderHelper
{
    [BepInPlugin(Guid, Name, Version)]
    public class OrderHelperPlugin : BasePlugin
    {
        public const string Guid = "local.mystia.orderhelper";
        public const string Name = "夜雀点单助手";
        public const string Version = "1.6.7";

        internal static OrderHelperPlugin Instance;
        internal static ManualLogSource LogSource;
        internal static ConfigEntry<string> ToggleKey;
        internal static ConfigEntry<string> SettingsKey;
        internal static ConfigEntry<string> DumpKey;
        internal static ConfigEntry<int> FontSize;
        internal static ConfigEntry<bool> VanillaSkin;
        internal static ConfigEntry<bool> ShowVersionNotice;
        internal static ConfigEntry<bool> ClickGuard;
        internal static ConfigEntry<bool> ShowNormalGuests;
        internal static ConfigEntry<bool> EnableOneClickCook;
        internal static ConfigEntry<bool> ConsiderExtras;
        internal static ConfigEntry<bool> AvoidDarkCooking;

        /// <summary>按客人钱包推荐酒水（v1.6.1）：别把客人付不起的酒推给他。</summary>
        internal static ConfigEntry<bool> RespectFund;

        /// <summary>刷稀客好感（v1.6.3）：保粉评 + 留够钱让她吃满所有单。</summary>
        internal static ConfigEntry<bool> FavorFarm;
        // 排查用开关：默认只留性能探针（每 30 秒一行），快照默认关（一次两百行日志，会顿）。
        internal static ConfigEntry<bool> PerfLog;
        internal static ConfigEntry<bool> SnapshotLog;
        internal static ConfigEntry<bool> GuestAudit;
        internal static ConfigEntry<bool> UiProbeOn;

        // 推荐框的位置与大小（-1 = 自动排布，0 = 自动大小）
        // 推荐框默认布局：由 2560×1440 实机调好的位置和大小固化而来。
        // 玩家在控制面板（F9）或直接用鼠标拖动改过之后，新数值会写进配置文件，
        // 下次进游戏按新数值摆放，这里的常量只作为首次运行 / 点「默认」时的值。
        internal const float DefaultMainX = 40f;
        internal const float DefaultMainY = 40f;
        internal const float DefaultMainW = 560f;
        internal const float DefaultMainH = 460f;
        internal const float DefaultCookerX = 1904f;
        internal const float DefaultCookerY = 74f;
        internal const float DefaultCookerW = 620f;
        internal const float DefaultCookerH = 460f;
        internal const float DefaultBevX = 1855f;
        internal const float DefaultBevY = 119f;
        internal const float DefaultBevW = 520f;
        internal const float DefaultBevH = 510f;

        internal static ConfigEntry<float> MainX;
        internal static ConfigEntry<float> MainY;
        internal static ConfigEntry<float> MainW;
        internal static ConfigEntry<float> MainH;
        internal static ConfigEntry<float> CookerHintX;
        internal static ConfigEntry<float> CookerHintY;
        internal static ConfigEntry<float> CookerHintW;
        internal static ConfigEntry<float> CookerHintH;
        internal static ConfigEntry<float> BevHintX;
        internal static ConfigEntry<float> BevHintY;
        internal static ConfigEntry<float> BevHintW;
        internal static ConfigEntry<float> BevHintH;

        private static GameObject _host;

        public override void Load()
        {
            LogSource = Log;

            Instance = this;

            ToggleKey = Config.Bind("General", "ToggleKey", "F8", "呼出 / 收起主面板的按键");
            SettingsKey = Config.Bind("General", "SettingsKey", "F9", "打开 / 关闭控制面板的按键");
            DumpKey = Config.Bind("General", "DumpKey", "F10",
                "（已废弃，仅作兼容保留）导出客人信息的旧按键；现在导出请用控制面板里的按钮");
            FontSize = Config.Bind("Display", "FontSize", 20, "面板字号（12-32）");
            VanillaSkin = Config.Bind("Display", "VanillaSkin", true,
                "原版皮肤：用游戏自带的订单小票底图当面板背景（关掉则退回普通深色面板）");
            ShowVersionNotice = Config.Bind("Display", "ShowVersionNotice", true,
                "进游戏后弹一条版本提示（屏幕上方居中，约 4 秒后自动淡出，每次启动只弹一次）");
            ClickGuard = Config.Bind("Behaviour", "ClickGuard", true,
                "点击遮挡：鼠标在我们面板上时盖一层 8×8 透明图，挡住走 Unity UI 射线的控件。" +
                "注意它只能挡 uGUI，厨具/酒水面板不走这条路，仍可能误触（游戏侧拦截已放弃，见 README 已知限制）");
            ShowNormalGuests = Config.Bind("Display", "ShowNormalGuests", false, "是否在面板中显示常客");
            EnableOneClickCook = Config.Bind("Behaviour", "EnableOneClickCook", true,
                "推荐框里点击推荐条目是否直接一键制作（关闭后点击不再放食材）");
            ConsiderExtras = Config.Bind("Behaviour", "ConsiderExtras", true,
                "推荐时把「加料」的标签也算进评价：菜谱自带 tag 之外，追加食材的 tag 同样计分，" +
                "在能凑到粉评的前提下按利润（售价−食材成本）和食材余量排序。关掉则只看菜谱自带 tag");

            AvoidDarkCooking = Config.Bind("Behaviour", "AvoidDarkCooking", true,
                "加料前先检查会不会把菜做成黑暗料理：加料不得引入菜品禁止的 tag（BanTags），" +
                "命中即拒绝这份食材。关掉则退回纯 tag 贪心（v1.5.6 的行为），可能会推荐出黑暗料理。" +
                "注：不再检查「会不会顺便凑成另一道菜」——游戏会把能做的菜都列出来，由玩家自己选做哪道");

            RespectFund = Config.Bind("Behaviour", "RespectFund", true,
                "按客人的钱包推荐酒水：还有单没上时，这一单最多只能花他手上现的钱；只有最后一单允许" +
                "小幅超支（本次到店总预算的 10%，夹在 30~300 之间）——均分会让第一单过度保守，" +
                "所以不采用均分。目的是避免「搭配价格远超客人预算 → 付不起 → 黑评」。\n" +
                "关掉则退回不看钱的老行为（v1.6.0）。注意：预算读的是游戏内 GuestGroupController.GetFund，" +
                "读不到时本功能自动失效（不会把推荐全清空）");

            FavorFarm = Config.Bind("Behaviour", "FavorFarm", false,
                "刷稀客好感：想让某位稀客吃满所有单（最多 5 单）刷好感时打开。\n" +
                "推荐会变成「先保粉评，再省钱」：①这一单的上限 = 手上现钱 − 后面每单的「最低粉评价」，" +
                "把后面几单的钱先留出来；②同样能粉评时挑更便宜的酒，而不是更贵的。\n" +
                "只对稀客生效，常客不受影响；关掉就是 v1.6.2 的正常口径（能花就花、利润优先）。\n" +
                "★ 依赖 [Behaviour] RespectFund = true（要读客人的钱包才能留钱），那项关了本项自动失效。\n" +
                "注意：留钱是按「最低能粉评的组合」估的，不是按你现在选的菜估，所以贵菜照样能吃满，只是后面几单会更省");

            PerfLog = Config.Bind("Diagnostics", "PerfLog", true,
                "性能探针：每 30 秒输出一行耗时汇总，单帧超过 150ms 记一条长帧。排查卡顿用，平时可关");
            SnapshotLog = Config.Bind("Diagnostics", "SnapshotLog", false,
                "每 30 秒把每位客人的候选菜写进日志（很长，默认关；只在排查推荐不对时打开）");
            GuestAudit = Config.Bind("Diagnostics", "GuestAudit", false,
                "客人巡查：每 30 秒把场上每个控制器的类型/状态/过滤原因写进日志（内容变化时才写）。" +
                "排查「客人面板里看不到」时用。★ 默认关：它每次要把所有控制器的类型名、单子状态全读一遍，实测 8–26ms，" +
                "是本插件里最重的一项，平时开着只会白白掉帧");
            // ★ 默认必须关：它会把全场几千个 Image/Sprite 扫一遍并写文件，很重。
            //   只在「要改 UI、需要重新摸素材」时才临时开（开了之后开局自动跑一次，或随时 F10 手动跑）。
            UiProbeOn = Config.Bind("Diagnostics", "UiProbe", false,
                "UI 侦察：开局第一次有客人进店时扫一遍全场 Image/Sprite，写到 config\\orderhelper-ui-dump.txt（很重，只在改 UI 前临时开）");

            Perf.On = PerfLog.Value;

            MainX = Config.Bind("Layout", "MainX", DefaultMainX, "点单助手窗口左上角 X");
            MainY = Config.Bind("Layout", "MainY", DefaultMainY, "点单助手窗口左上角 Y");
            MainW = Config.Bind("Layout", "MainW", DefaultMainW, "点单助手窗口宽度");
            MainH = Config.Bind("Layout", "MainH", DefaultMainH, "点单助手窗口高度");
            CookerHintX = Config.Bind("Layout", "CookerHintX", DefaultCookerX, "料理推荐框左上角 X");
            CookerHintY = Config.Bind("Layout", "CookerHintY", DefaultCookerY, "料理推荐框左上角 Y");
            CookerHintW = Config.Bind("Layout", "CookerHintW", DefaultCookerW, "料理推荐框宽度");
            CookerHintH = Config.Bind("Layout", "CookerHintH", DefaultCookerH, "料理推荐框高度");
            BevHintX = Config.Bind("Layout", "BevHintX", DefaultBevX, "酒水推荐框左上角 X");
            BevHintY = Config.Bind("Layout", "BevHintY", DefaultBevY, "酒水推荐框左上角 Y");
            BevHintW = Config.Bind("Layout", "BevHintW", DefaultBevW, "酒水推荐框宽度");
            BevHintH = Config.Bind("Layout", "BevHintH", DefaultBevH, "酒水推荐框高度");

            // 头像人工映射表：一次读盘，之后随便查。
            // ★ 必须在任何界面出现之前读好，不然第一位客人进来那次还是占位。
            GuestPortraits.LoadOverrides();

            ClassInjector.RegisterTypeInIl2Cpp<OverlayBehaviour>();

            try
            {
                var harmony = new Harmony(Guid);
                Probes.Install(harmony);
            }
            catch (Exception e)
            {
                LogSource.LogError("[OrderHelper] 安装探测补丁失败: " + e);
            }

            // 极早期创建的 GameObject 会在首个场景加载时被销毁，
            // 因此改为每个场景加载完成后重建宿主对象。
            try
            {
                var callback = DelegateSupport.ConvertDelegate<UnityAction<Scene, LoadSceneMode>>(
                    new Action<Scene, LoadSceneMode>(OnSceneLoaded));
                SceneManager.add_sceneLoaded(callback);
                LogSource.LogInfo("[OrderHelper] 已注册 sceneLoaded");
            }
            catch (Exception e)
            {
                LogSource.LogError("[OrderHelper] 注册 sceneLoaded 失败: " + e);
            }

            CreateHost("Load");

            Diagnostics.Hook();

            LogSource.LogInfo($"{Name} v{Version} 已加载（F8 主面板，F9 控制面板）");
        }

        internal static void SaveConfig()
        {
            try { Instance?.Config.Save(); }
            catch (Exception e) { LogSource?.LogWarning("[OrderHelper] 保存配置失败: " + e.Message); }
        }

        internal static void SetFontSize(int size)
        {
            FontSize.Value = Mathf.Clamp(size, 12, 32);
            SaveConfig();
        }

        internal static void SetShowNormalGuests(bool on)
        {
            ShowNormalGuests.Value = on;
            SaveConfig();
        }

        /// <summary>写回推荐框布局；只传变化过的字段（其余传 float.NaN 表示保持不变）。</summary>
        internal static void SetMainLayout(float x, float y, float w, float h)
        {
            if (!float.IsNaN(x)) MainX.Value = x;
            if (!float.IsNaN(y)) MainY.Value = y;
            if (!float.IsNaN(w)) MainW.Value = w;
            if (!float.IsNaN(h)) MainH.Value = h;

            SaveConfig();
        }

        internal static void SetHintLayout(bool beverage, float x, float y, float w, float h)
        {
            if (beverage)
            {
                if (!float.IsNaN(x)) BevHintX.Value = x;
                if (!float.IsNaN(y)) BevHintY.Value = y;
                if (!float.IsNaN(w)) BevHintW.Value = w;
                if (!float.IsNaN(h)) BevHintH.Value = h;
            }
            else
            {
                if (!float.IsNaN(x)) CookerHintX.Value = x;
                if (!float.IsNaN(y)) CookerHintY.Value = y;
                if (!float.IsNaN(w)) CookerHintW.Value = w;
                if (!float.IsNaN(h)) CookerHintH.Value = h;
            }

            SaveConfig();
        }

        internal static void SetOneClickCook(bool on)
        {
            EnableOneClickCook.Value = on;
            SaveConfig();
        }

        /// <summary>按预算推荐（v1.6.4 起可在界面里切换，不必退出游戏改配置文件）。</summary>
        internal static void SetRespectFund(bool on)
        {
            RespectFund.Value = on;
            SaveConfig();
            // 口径变了就把客人缓存作废：不然面板要等 2 秒才换算法，看着像按钮没反应。
            GuestCollector.InvalidateCache();
        }

        /// <summary>刷稀客好感（v1.6.4 起可在界面里切换）。</summary>
        internal static void SetFavorFarm(bool on)
        {
            FavorFarm.Value = on;
            SaveConfig();
            GuestCollector.InvalidateCache();
            LogSource?.LogInfo("[OrderHelper] 刷稀客好感：" + (on ? "开" : "关"));
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            LogSource.LogInfo($"[OrderHelper] 场景加载: {scene.name} ({mode})");
            Diagnostics.Reset();
            RecipeIndex.Invalidate();      // 数据表可能重载；指针也可能被复用
            GuestCollector.ClearLeft();    // 离店标记只在一场营业内有效
            GuestPortraits.ResetBudget();  // 头像扫描预算重置（贴图是自己的副本，缓存不用清）
            CreateHost(scene.name);

            // 只在营业场景自动打开面板；其他场景保持隐藏，按 F8 手动呼出。
            if (scene.name == "Work")
                OverlayBehaviour.Instance?.Show();
        }

        private static void CreateHost(string where)
        {
            try
            {
                if (_host != null)
                {
                    UnityEngine.Object.Destroy(_host);
                    _host = null;
                }

                _host = new GameObject("MystiaOrderHelperHost");
                UnityEngine.Object.DontDestroyOnLoad(_host);
                var component = _host.AddComponent<OverlayBehaviour>();

                LogSource.LogInfo(
                    $"[OrderHelper] 宿主已创建（{where}）active={_host.activeSelf} " +
                    $"inScene={_host.scene.name} componentNull={component == null}");
            }
            catch (Exception e)
            {
                LogSource.LogError("[OrderHelper] 创建宿主失败: " + e);
            }
        }
    }
}






