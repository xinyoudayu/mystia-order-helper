using System;
using System.Collections.Generic;

using DEYU.AdpUISystem.LogicalCollection;
using DEYU.UniversalUISystem;

using GameData.Core.Collections;

using HarmonyLib;

using NightScene.UI.CookingUtility;

namespace MystiaOrderHelper
{
    /// <summary>
    /// 监视酒水存储面板（WorkSceneStoragePannel 的酒水页），
    /// 打开时在侧边显示推荐酒水，并支持点击选中。
    /// </summary>
    internal static class BeveragePanelWatcher
    {
        internal static bool IsOpen;
        internal static WorkSceneStoragePannel Panel;

        /// <summary>商品 ID → 该格子的游戏按钮（由 OnElementEnabled 登记）。</summary>
        private static readonly Dictionary<int, UIButtonSimple> Buttons = new Dictionary<int, UIButtonSimple>();

        internal static void SetOpen(WorkSceneStoragePannel panel)
        {
            Panel = panel;
            IsOpen = false;

            var count = 0;
            try
            {
                var beverages = panel?.m_Beverages;
                if (beverages != null) count = beverages.Count;
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 读取酒水列表失败: " + e.Message);
            }

            IsOpen = count > 0;
            OrderHelperPlugin.LogSource.LogInfo(
                $"[OrderHelper] 存储面板打开 酒水={IsOpen} 数量={count}");
        }

        internal static void SetClosed()
        {
            Panel = null;
            IsOpen = false;
            Buttons.Clear();
        }

        /// <summary>游戏为酒水格注册按钮时调用。</summary>
        internal static void RegisterButton(Il2CppSystem.Collections.Generic.KeyValuePair<Sellable, int> value, UIButtonSimple button)
        {
            try
            {
                if (button == null) return;
                var sellable = value.Key;
                if (sellable == null) return;
                Buttons[sellable.Id] = button;
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 登记酒水格失败: " + e.Message);
            }
        }

        internal static bool TrySelectBeverage(Sellable sellable, string name)
        {
            if (!IsOpen || Panel == null) return false;

            // 直接把这个 Sellable 交给面板的 Extract —— 不依赖格子索引，
            // 因此不会出现"点 A 得到 B"的错位。
            if (sellable != null)
            {
                try
                {
                    Panel.Extract(sellable);
                    OrderHelperPlugin.LogSource.LogInfo(
                        $"[OrderHelper] 取酒水 id={sellable.Id} name={name}（直接 Extract）");
                    return true;
                }
                catch (Exception e)
                {
                    OrderHelperPlugin.LogSource.LogWarning(
                        "[OrderHelper] 直接 Extract 酒水失败，回退到按钮: " + e.Message);
                }
            }

            try
            {
                if (sellable != null && Buttons.TryGetValue(sellable.Id, out var button) && button != null)
                {
                    button.CallSubmitAction();
                    OrderHelperPlugin.LogSource.LogInfo(
                        $"[OrderHelper] 点击酒水格 id={sellable.Id} name={name} buttons={Buttons.Count}");
                    return true;
                }

                OrderHelperPlugin.LogSource.LogInfo(
                    $"[OrderHelper] 酒水格未登记 id={(sellable == null ? -1 : sellable.Id)} name={name} buttons={Buttons.Count}");
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 点击酒水格失败: " + e.Message);
            }

            if (!string.IsNullOrEmpty(name))
                return PanelClicker.TryClick(Panel.transform, name);

            return false;
        }
    }

    [HarmonyPatch(typeof(WorkSceneStoragePannel), "OnPanelOpen")]
    internal static class BeveragePanelOpenPatch
    {
        [HarmonyPostfix]
        private static void Postfix(WorkSceneStoragePannel __instance) =>
            BeveragePanelWatcher.SetOpen(__instance);
    }

    [HarmonyPatch(typeof(WorkSceneStoragePannel), "OnPanelClose")]
    internal static class BeveragePanelClosePatch
    {
        [HarmonyPostfix]
        private static void Postfix() => BeveragePanelWatcher.SetClosed();
    }

    [HarmonyPatch(typeof(WorkSceneStoragePannel), "OnElementEnabled")]
    internal static class BeverageElementEnabledPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            Il2CppSystem.Collections.Generic.KeyValuePair<Sellable, int> objectBase,
            UIElementCluster cluster,
            UIButtonSimple uiButtonSimple) =>
            BeveragePanelWatcher.RegisterButton(objectBase, uiButtonSimple);
    }
}
