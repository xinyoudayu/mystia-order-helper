using System;
using System.Collections.Generic;

using HarmonyLib;

using GameData.Core.Collections;
using GameData.RunTime.Common;
using GameData.RunTime.NightSceneUtility;

using NightScene.UI.CookingUtility;

namespace MystiaOrderHelper
{
    /// <summary>
    /// 一次性探测：确认厨具面板到底列出了哪些菜谱，以及它和几个候选列表的关系。
    /// 只读，不修改任何游戏状态。
    /// </summary>
    internal static class Probes
    {
        internal static void Install(Harmony harmony)
        {
            TryPatch(harmony, typeof(RecipeElementProbe), "OnRecipeElementEnabled");
            TryPatch(harmony, typeof(PanelProbe), "OnPanelOpen");
            TryPatch(harmony, typeof(PanelCloseProbe), "OnPanelClose");
            TryPatch(harmony, typeof(CookerPanelOpenPatch), "Cooker.OnPanelOpen");
            TryPatch(harmony, typeof(CookerPanelClosePatch), "Cooker.OnPanelClose");
            TryPatch(harmony, typeof(RecipeElementEnabledPatch), "RecipeElementEnabled");
            TryPatch(harmony, typeof(IngElementEnabledPatch), "IngredientElementEnabled");
            TryPatch(harmony, typeof(BeveragePanelOpenPatch), "Storage.OnPanelOpen");
            TryPatch(harmony, typeof(BeveragePanelClosePatch), "Storage.OnPanelClose");
            TryPatch(harmony, typeof(BeverageElementEnabledPatch), "Storage.OnElementEnabled");
            TryPatch(harmony, typeof(CookerCallGuard), "Cooker.CallCooker");
            TryPatch(harmony, typeof(GuestMoveToSpawnPatch), "Guest.MoveToSpawn");
            TryPatch(harmony, typeof(GuestFlyToSpawnPatch), "Guest.FlyToSpawn");
        }

        private static void TryPatch(Harmony harmony, Type type, string label)
        {
            try
            {
                harmony.PatchAll(type);
                OrderHelperPlugin.LogSource.LogInfo("[Probe] 已挂补丁: " + label);
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogError($"[Probe] 挂补丁失败 {label}: " + e);
            }
        }

        internal static void DumpList(string label, Il2CppSystem.Collections.Generic.List<Recipe> list)
        {
            if (list == null)
            {
                OrderHelperPlugin.LogSource.LogInfo($"[Probe] {label} = null");
                return;
            }

            var ids = new List<string>();
            for (var i = 0; i < list.Count; i++)
            {
                var recipe = list[i];
                ids.Add(recipe == null ? "null" : recipe.Id.ToString());
            }

            OrderHelperPlugin.LogSource.LogInfo($"[Probe] {label} ({list.Count}): {string.Join(",", ids)}");
        }
    }

    [HarmonyPatch(typeof(WorkSceneCookingSelectionPannel), "OnRecipeElementEnabled")]
    internal static class RecipeElementProbe
    {
        [HarmonyPrefix]
        private static void Prefix(Recipe recipe)
        {
            try
            {
                var name = recipe?.Food?.Text?.Name ?? "?";
                OrderHelperPlugin.LogSource.LogInfo(
                    $"[Probe] 面板元素 recipe={recipe?.Id} food={recipe?.FoodID} name={name}");
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[Probe] 元素日志失败: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(WorkSceneCookingSelectionPannel), "OnPanelOpen")]
    internal static class PanelProbe
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            try
            {
                OrderHelperPlugin.LogSource.LogInfo("[Probe] ===== 厨具面板打开 =====");

                var configure = IzakayaConfigure.Instance;
                if (configure == null)
                {
                    OrderHelperPlugin.LogSource.LogInfo("[Probe] IzakayaConfigure 为空");
                }
                else
                {
                    Probes.DumpList("DailyRecipes", configure.DailyRecipes);
                    Probes.DumpList("PopNotHateRecipes", configure.PopNotHateRecipes);
                    Probes.DumpList("CheapestRecipes", configure.CheapestRecipes);
                    Probes.DumpList("TheMostExpensiveRecipes", configure.TheMostExpensiveRecipes);
                }

                var unlocked = RunTimeStorage.Recipes;
                OrderHelperPlugin.LogSource.LogInfo($"[Probe] 已解锁食谱数: {unlocked?.Count ?? -1}");
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogError("[Probe] 面板打开日志失败: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(WorkSceneCookingSelectionPannel), "OnPanelClose")]
    internal static class PanelCloseProbe
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            OrderHelperPlugin.LogSource.LogInfo("[Probe] ===== 厨具面板关闭 =====");
        }
    }
    /// <summary>
    /// 控制面板打开时拦截“点击厨具进入烹饪”，避免暂停期间误触导致面板状态机错乱。
    /// </summary>
    [HarmonyPatch(typeof(NightScene.CookingUtility.CookSystemManager), "CallCooker")]
    internal static class CookerCallGuard
    {
        [HarmonyPrefix]
        private static bool Prefix(UnityEngine.Vector3Int cookerPosition)
        {
            if (!OverlayBehaviour.SettingsVisible) return true;
            OrderHelperPlugin.LogSource.LogInfo(
                $"[OrderHelper] 控制面板打开中，已拦截打开厨具 pos={cookerPosition}");
            return false;
        }
    }
}