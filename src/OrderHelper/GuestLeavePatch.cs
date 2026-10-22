using System;
using HarmonyLib;
using NightScene.GuestManagementUtility;

namespace MystiaOrderHelper
{
    /// <summary>
    /// 客人离店时会走回生成点/飞走。这里打上"已离开"标记，
    /// 推荐框不再显示已经离场的客人（有些版本里 controller 会在列表里多留一阵）。
    /// </summary>
    [HarmonyPatch(typeof(GuestGroupController), "MoveToSpawn")]
    internal static class GuestMoveToSpawnPatch
    {
        [HarmonyPostfix]
        private static void Postfix(GuestGroupController __instance)
        {
            GuestCollector.MarkLeft(__instance);
        }
    }

    /// <summary>被赶走/飞走的客人（驱逐类离场）。</summary>
    [HarmonyPatch(typeof(GuestGroupController), "FlyToSpawn")]
    internal static class GuestFlyToSpawnPatch
    {
        [HarmonyPostfix]
        private static void Postfix(GuestGroupController __instance)
        {
            GuestCollector.MarkLeft(__instance);
        }
    }
}
