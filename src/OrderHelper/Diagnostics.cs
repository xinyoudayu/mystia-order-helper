using System;
using System.Collections.Generic;

using BepInEx.Logging;

using NightScene.GuestManagementUtility;

namespace MystiaOrderHelper
{
    internal static class Diagnostics
    {
        private static readonly List<GuestGroupController> Recorded = new();
        private static bool _hooked;
        private static IntPtr _hookedPointer;
        private static ManualLogSource Log => OrderHelperPlugin.LogSource;

        internal static void Hook() => TryHook();

        internal static void Reset()
        {
            Recorded.Clear();
            _hooked = false;
            _hookedPointer = IntPtr.Zero;
        }

        /// <summary>
        /// 场景切换后 GuestsManager 会重建，指针变了就重新挂钩。
        /// 这个事件只是补充来源，真正的数据以 AllPresentedGuestGroupController 为准。
        /// </summary>
        internal static void TryHook()
        {
            try
            {
                var mgr = GuestsManager.Instance;
                if (mgr == null) return;

                var pointer = mgr.Pointer;
                if (_hooked && pointer == _hookedPointer) return;

                mgr.add_OnGuestSpawn(new Action<GuestGroupController>(OnSpawn));
                _hooked = true;
                _hookedPointer = pointer;
                Log.LogInfo($"[OrderHelper] 已挂钩 GuestsManager.OnGuestSpawn ptr=0x{pointer.ToInt64():X}");
            }
            catch (Exception e)
            {
                Log.LogError("[OrderHelper] Hook 失败: " + e);
            }
        }

        private static void OnSpawn(GuestGroupController controller)
        {
            try
            {
                if (controller == null) return;
                Recorded.Add(controller);
                Log.LogInfo($"[OrderHelper] OnGuestSpawn 触发: {SafeName(controller)} | {controller.GetType().Name}");
            }
            catch (Exception e)
            {
                Log.LogError("[OrderHelper] OnSpawn 异常: " + e);
            }
        }

        internal static void Dump()
        {
            try
            {
                Log.LogInfo($"[OrderHelper] ===== 导出开始（事件记录 {Recorded.Count}）=====");

                var mgr = GuestsManager.Instance;
                if (mgr == null)
                {
                    Log.LogInfo("[OrderHelper] GuestsManager.Instance 为空（不在营业场景？）");
                }
                else
                {
                    var live = mgr.AllPresentedGuestGroupController;
                    if (live == null)
                    {
                        Log.LogInfo("[OrderHelper] AllPresentedGuestGroupController 为空");
                    }
                    else
                    {
                        Log.LogInfo($"[OrderHelper] 实时在场数量: {live.Count}");
                        var enumerator = live.GetEnumerator();
                        while (enumerator.MoveNext())
                            DumpController(enumerator.Current);
                    }
                }

                Log.LogInfo("[OrderHelper] ===== 导出结束 =====");
            }
            catch (Exception e)
            {
                Log.LogError("[OrderHelper] Dump 异常: " + e);
            }
        }

        private static string SafeName(GuestGroupController controller)
        {
            try { return controller.OnGetGuestName(); }
            catch { return "<取名失败>"; }
        }

        private static void DumpController(GuestGroupController controller)
        {
            try
            {
                if (controller == null) return;

                var special = controller.TryCast<SpecialGuestsController>();
                var kind = special != null ? "稀客" : "常客";
                Log.LogInfo($"[OrderHelper] 客人: {SafeName(controller)} | {kind} | {controller.GetType().Name}");

                if (special == null) return;

                var data = special.SpecialGuest;
                if (data == null)
                {
                    Log.LogInfo("[OrderHelper]   稀客数据为空");
                    return;
                }

                Log.LogInfo($"[OrderHelper]   stringId={data.stringId} id={data.Id}");

                var likeFood = data.likeFoodTag;
                if (likeFood != null)
                {
                    var parts = new List<string>();
                    for (var i = 0; i < likeFood.Length; i++)
                        parts.Add($"{likeFood[i].tagId}x{likeFood[i].weight}");
                    Log.LogInfo("[OrderHelper]   喜好菜品标签: " + string.Join(",", parts));
                }

                var hate = data.hateFoodTag;
                if (hate != null)
                {
                    var parts = new List<string>();
                    for (var i = 0; i < hate.Length; i++)
                        parts.Add(hate[i].ToString());
                    Log.LogInfo("[OrderHelper]   厌恶菜品标签: " + string.Join(",", parts));
                }

                var likeBev = data.likeBevTag;
                if (likeBev != null)
                {
                    var parts = new List<string>();
                    for (var i = 0; i < likeBev.Length; i++)
                        parts.Add($"{likeBev[i].tagId}x{likeBev[i].weight}");
                    Log.LogInfo("[OrderHelper]   喜好酒水标签: " + string.Join(",", parts));
                }
            }
            catch (Exception e)
            {
                Log.LogWarning("[OrderHelper]   读取客人失败: " + e.Message);
            }
        }
    }
}
