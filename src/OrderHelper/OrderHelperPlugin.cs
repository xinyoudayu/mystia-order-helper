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
        public const string Version = "1.5.0";

        internal static OrderHelperPlugin Instance;
        internal static ManualLogSource LogSource;
        internal static ConfigEntry<string> ToggleKey;
        internal static ConfigEntry<string> SettingsKey;
        internal static ConfigEntry<string> DumpKey;
        internal static ConfigEntry<int> FontSize;
        internal static ConfigEntry<bool> ShowNormalGuests;
        internal static ConfigEntry<bool> EnableOneClickCook;

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
            ShowNormalGuests = Config.Bind("Display", "ShowNormalGuests", false, "是否在面板中显示常客");
            EnableOneClickCook = Config.Bind("Behaviour", "EnableOneClickCook", true,
                "推荐框里点击推荐条目是否直接一键制作（关闭后点击不再放食材）");

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

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            LogSource.LogInfo($"[OrderHelper] 场景加载: {scene.name} ({mode})");
            Diagnostics.Reset();
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






