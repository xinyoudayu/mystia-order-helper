using System;
using System.Collections.Generic;

using DEYU.AdpUISystem.LogicalCollection;
using DEYU.UniversalUISystem;

using GameData.Core.Collections;

using HarmonyLib;

using Il2CppInterop.Runtime.InteropTypes.Arrays;

using NightScene.UI.CookingUtility;

using UnityEngine;

namespace MystiaOrderHelper
{
    /// <summary>
    /// 监视原生厨具面板的开关，记录它的位置，并登记每个菜品格对应的游戏按钮。
    /// </summary>
    internal static class CookerPanelWatcher
    {
        internal static bool IsOpen;
        internal static string CookerName = "";
        internal static Rect PanelRect;

        /// <summary>不是常规五种厨具（比如「三位一体」这类特殊厨具）。</summary>
        internal static bool SpecialCooker;

        /// <summary>特殊厨具（三位一体等）能做哪些烹饪方式，从面板自带菜谱反推。</summary>
        internal static readonly HashSet<Cooker.CookerType> SpecialCookerTypes = new();

        private static readonly HashSet<string> KnownCookers = new HashSet<string>
        {
            "煮锅", "烤架", "油锅", "蒸锅", "料理台",
        };

        /// <summary>菜品 ID → 该格子的游戏按钮（由 OnRecipeElementEnabled 登记）。</summary>
        private static readonly Dictionary<int, UIButtonSimple> Buttons = new Dictionary<int, UIButtonSimple>();

        /// <summary>菜品 ID → 菜谱（特殊厨具的推荐来源）。</summary>
        private static readonly Dictionary<int, Recipe> RecipeByFood = new Dictionary<int, Recipe>();

        /// <summary>食材 ID → 面板里的食材格子（由 OnIngElementEnabled 登记）。</summary>
        private static readonly Dictionary<int, IngEntry> Ingredients = new Dictionary<int, IngEntry>();

        private sealed class IngEntry
        {
            internal Ingredient Ingredient;
            internal int Count;
            internal UIElementCluster Cluster;
            internal UIButtonSimple Button;
            internal Il2CppSystem.Collections.Generic.KeyValuePair<Ingredient, int> KeyValue;
        }

        private static Recipe _pendingDish;
        private static float _pendingDishTime;

        private static WorkSceneCookingSelectionPannel _current;

        internal static void SetOpen(WorkSceneCookingSelectionPannel panel)
        {
            _current = panel;
            IsOpen = true;

            try { CookerName = panel?.cookerName?.text ?? ""; }
            catch { CookerName = ""; }

            SpecialCooker = !string.IsNullOrEmpty(CookerName) && !KnownCookers.Contains(CookerName);
            if (!SpecialCooker) SpecialCookerTypes.Clear();

            PanelRect = ComputeRect(panel);
            OrderHelperPlugin.LogSource.LogInfo(
                $"[OrderHelper] 厨具面板打开 cooker='{CookerName}' special={SpecialCooker} rect={PanelRect}");

            if (SpecialCooker)
            {
                DumpSpecialRecipes(panel);
                SpecialCookerTypes.Clear();
                try
                {
                    var recipes = panel?.m_RecipeInstances;
                    if (recipes != null)
                        for (var i = 0; i < recipes.Count; i++)
                            if (recipes[i] != null) SpecialCookerTypes.Add(recipes[i].CookerType);
                }
                catch { }
                var types = new List<string>();
                foreach (var t in SpecialCookerTypes) types.Add(t.ToString());
                OrderHelperPlugin.LogSource.LogInfo("[OrderHelper] 特殊厨具支持 " + types.Count + " 种厨具: " + string.Join(",", types.ToArray()));
            }
        }

        /// <summary>特殊厨具（三位一体等）：把面板自带的菜谱列表打出来，确认它能做哪些菜。</summary>
        private static void DumpSpecialRecipes(WorkSceneCookingSelectionPannel panel)
        {
            try
            {
                var recipes = panel.m_RecipeInstances;
                if (recipes == null)
                {
                    OrderHelperPlugin.LogSource.LogInfo("[OrderHelper] 特殊厨具菜谱列表为空");
                    return;
                }

                OrderHelperPlugin.LogSource.LogInfo($"[OrderHelper] 特殊厨具菜谱 {recipes.Count} 道");
                for (var i = 0; i < recipes.Count && i < 20; i++)
                {
                    var r = recipes[i];
                    if (r == null) continue;
                    var name = "";
                    try { name = r.Food != null && r.Food.Text != null ? r.Food.Text.Name : ""; } catch { }
                    OrderHelperPlugin.LogSource.LogInfo(
                        $"[OrderHelper]   菜谱[{i}] food={r.FoodID} cooker={r.CookerType} name={name}");
                }
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 输出特殊厨具菜谱失败: " + e.Message);
            }
        }

        internal static void SetClosed()
        {
            _current = null;
            IsOpen = false;
            CookerName = "";
            PanelRect = default;
            Buttons.Clear();
            RecipeByFood.Clear();
            Ingredients.Clear();
            _pendingDish = null;
            OrderHelperPlugin.LogSource.LogInfo("[OrderHelper] 厨具面板关闭");
        }

        /// <summary>
        /// 这道菜在当前打开的厨具面板上是否真的能点。
        /// 直接看面板自己有没有登记这个菜格子 —— 这样「三位一体」这类特殊厨具
        /// 不需要名字映射也能判断（它把好几种厨具的菜谱合在一个面板里）。
        /// </summary>
        /// <summary>当前面板自带的菜谱（特殊厨具用来做推荐来源）。</summary>
        internal static List<Recipe> RegisteredRecipes()
        {
            // 特殊厨具（三位一体等）的菜谱是面板自带的固定表，直接读面板字段，别只靠按钮登记（可能是按需生成的）。
            if (SpecialCooker)
            {
                try
                {
                    var p = _current;
                    if (p != null && p.m_RecipeInstances != null && p.m_RecipeInstances.Count > 0)
                    {
                        var copy = new List<Recipe>();
                        for (var i = 0; i < p.m_RecipeInstances.Count; i++) copy.Add(p.m_RecipeInstances[i]);
                        return copy;
                    }
                }
                catch { }
            }

            var list = new List<Recipe>();
            try
            {
                foreach (var recipe in RecipeByFood.Values)
                    if (recipe != null) list.Add(recipe);
            }
            catch { }

            return list;
        }

        internal static bool CanCookHere(int foodId)
        {
            try { return IsOpen && Buttons.ContainsKey(foodId); }
            catch { return false; }
        }

        internal static int RegisteredRecipeCount => Buttons.Count;

        /// <summary>特殊厨具是否能做这道菜（按面板自带菜谱反推出的烹饪方式判断）。</summary>
        internal static bool Supports(Recipe recipe)
        {
            if (recipe == null) return false;
            if (!SpecialCooker) return true;
            if (SpecialCookerTypes.Count == 0) return true;
            try { return SpecialCookerTypes.Contains(recipe.CookerType); }
            catch { return true; }
        }

        /// <summary>游戏为菜品格注册按钮时调用：记下来，之后直接触发它。</summary>
        internal static void RegisterRecipeButton(Recipe recipe, UIButtonSimple button)
        {
            try
            {
                if (recipe == null || button == null) return;
                Buttons[recipe.FoodID] = button;
                RecipeByFood[recipe.FoodID] = recipe;
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 登记菜品格失败: " + e.Message);
            }
        }

        /// <summary>游戏为食材格注册时调用：记下 (食材, 数量, 格子, 按钮)，之后直接调面板的选择回调。</summary>
        internal static void RegisterIngredient(
            Il2CppSystem.Collections.Generic.KeyValuePair<Ingredient, int> keyValue,
            UIElementCluster cluster, UIButtonSimple button)
        {
            try
            {
                var ingredient = keyValue.Key;
                if (ingredient == null || button == null) return;

                Ingredients[ingredient.Id] = new IngEntry
                {
                    Ingredient = ingredient,
                    Count = keyValue.Value,
                    Cluster = cluster,
                    Button = button,
                    KeyValue = keyValue,
                };
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 登记食材格失败: " + e.Message);
            }
        }

        /// <summary>
        /// 点推荐菜：优先走面板自带的「导入配方」通道（importedRecipe / hasImported），
        /// 让面板自己把用料算进选中区和可制作区；这条路不模拟点击，缺料也会由面板自己处理。
        /// 导入后如果面板没变化，再退回逐个点食材格。
        /// </summary>
        internal static bool SelectIngredients(Recipe recipe)
        {
            if (!IsOpen || _current == null || recipe == null) return false;

            try
            {
                // 方案 A：面板自带的「导入配方」通道
                _current.importedRecipe = recipe;
                _current.hasImported = true;

                _current.UpdateIngField();
                _current.UpdateSelectedField();
                _current.UpdateOutputField();
                _current.UpdateRecipeField();
                _current.UpdateAllVisual();

                var selectedCount = CountSelected(_current);
                var outputCount = CountOutput(_current);

                OrderHelperPlugin.LogSource.LogInfo(
                    $"[OrderHelper] 导入配方 food={recipe.FoodID} name={ShowNameOf(recipe)} 选中食材={selectedCount} 可制作组合={outputCount}");

                if (selectedCount > 0 || outputCount > 0) return true;
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 导入配方失败: " + e.Message);
            }

            // 方案 B：直接写面板的 selectedIngredients（ID 列表），再让它自己重新匹配
            try
            {
                var ids = recipe.Ingredients;
                var list = _current.selectedIngredients;
                if (list == null)
                {
                    list = new Il2CppSystem.Collections.Generic.List<int>();
                    _current.selectedIngredients = list;
                }

                list.Clear();
                for (var i = 0; i < ids.Count; i++) list.Add(ids[i]);

                var instances = _current.m_SelectedInstances;
                if (instances != null)
                {
                    instances.Clear();
                    for (var i = 0; i < ids.Count; i++)
                    {
                        var ingredient = DataBaseCore.RefIngredient(ids[i]);
                        if (ingredient != null) instances.Add(ingredient);
                    }
                }

                _current.UpdateSelectedField();

                var combos = _current.m_OutputInstances;
                if (combos == null)
                {
                    combos = new Il2CppSystem.Collections.Generic.List<WorkSceneCookingSelectionPannel.MatchedCookCombo>();
                    _current.m_OutputInstances = combos;
                }
                combos.Clear();

                bool haveUnObtained;
                _current.MatchingSelected(combos, out haveUnObtained);
                _current.UpdateOutputField();
                _current.UpdateRecipeField();
                _current.UpdateAllVisual();

                OrderHelperPlugin.LogSource.LogInfo(
                    $"[OrderHelper] 直填食材 food={recipe.FoodID} 写入={ids.Count} 选中={CountSelected(_current)} 可制作组合={CountOutput(_current)} 未解锁组合={haveUnObtained}");

                if (CountSelected(_current) > 0 || CountOutput(_current) > 0) return true;
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 直填食材失败: " + e.Message);
            }

            return SelectIngredientOneByOne(recipe);
        }

        private static int CountSelected(WorkSceneCookingSelectionPannel panel)
        {
            try { return panel.selectedIngredients == null ? 0 : panel.selectedIngredients.Count; }
            catch { return 0; }
        }

        private static int CountOutput(WorkSceneCookingSelectionPannel panel)
        {
            try { return panel.m_OutputInstances == null ? 0 : panel.m_OutputInstances.Count; }
            catch { return 0; }
        }

        /// <summary>备用方案：把菜谱需要的食材在原生面板里挨个选中。</summary>
        private static bool SelectIngredientOneByOne(Recipe recipe)
        {
            var ids = recipe.Ingredients;
            if (ids == null || ids.Count == 0)
            {
                OrderHelperPlugin.LogSource.LogInfo(
                    $"[OrderHelper] 菜谱无食材 food={recipe.FoodID} 已登记食材={Ingredients.Count}");
                return false;
            }

            var selected = 0;
            var missing = new List<int>();

            try
            {
                for (var i = 0; i < ids.Count; i++)
                {
                    var id = ids[i];
                    if (!Ingredients.TryGetValue(id, out var entry))
                    {
                        missing.Add(id);
                        continue;
                    }

                    try
                    {
                        _current.OnIngElementSelected(entry.KeyValue, entry.Cluster, entry.Button);
                        selected++;
                    }
                    catch (Exception e)
                    {
                        OrderHelperPlugin.LogSource.LogWarning(
                            $"[OrderHelper] 选中食材失败 id={id}: {e.Message}");
                    }
                }
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 选择食材异常: " + e.Message);
            }

            OrderHelperPlugin.LogSource.LogInfo(
                $"[OrderHelper] 逐个选食材 food={recipe.FoodID} 选中={selected} 缺={missing.Count} 已登记={Ingredients.Count}" +
                (missing.Count > 0 ? " 缺ID=" + string.Join(",", missing.ConvertAll(x => x.ToString()).ToArray()) : ""));

            // 食材选完后，下一帧再试着点菜品格（此时面板会按已选食材刷新）。
            _pendingDish = recipe;
            _pendingDishTime = Time.realtimeSinceStartup + 0.25f;
            return selected > 0;
        }

        /// <summary>由 OverlayBehaviour.Update 驱动：处理推迟的菜品格点击。</summary>
        internal static void Tick()
        {
            if (_pendingDish == null || Time.realtimeSinceStartup < _pendingDishTime) return;

            var recipe = _pendingDish;
            _pendingDish = null;
            TrySelectDish(recipe, ShowNameOf(recipe));
        }

        /// <summary>把菜谱需要的食材拼成给人看的一行，缺的标注「缺」。</summary>
        internal static string IngredientText(Recipe recipe)
        {
            try
            {
                if (recipe == null) return "";

                var ids = recipe.Ingredients;
                if (ids == null || ids.Count == 0) return "食材：无";

                var counts = new Dictionary<int, int>();
                for (var i = 0; i < ids.Count; i++)
                {
                    var id = ids[i];
                    counts[id] = counts.TryGetValue(id, out var c) ? c + 1 : 1;
                }

                var parts = new List<string>();
                foreach (var pair in counts)
                {
                    var name = NameOfIngredient(pair.Key);
                    var enough = Ingredients.ContainsKey(pair.Key);
                    parts.Add(pair.Value > 1 ? $"{name}×{pair.Value}" : name);
                    if (!enough) parts.Add("(缺)");
                }

                return "食材：" + string.Join("、", parts.ToArray());
            }
            catch (Exception e)
            {
                return "食材：读取失败(" + e.Message + ")";
            }
        }

        private static string NameOfIngredient(int id)
        {
            try
            {
                var ingredient = DataBaseCore.RefIngredient(id);
                var name = ingredient?.Text?.Name;
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch
            {
                // ignore
            }

            return "#" + id;
        }

        private static string ShowNameOf(Recipe recipe)
        {
            try
            {
                var name = recipe?.Food?.Text?.Name;
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch
            {
                // ignore
            }

            return "food=" + (recipe == null ? -1 : recipe.FoodID);
        }

        /// <summary>
        /// 推荐菜如果不在原生面板的列表里，就追加进去并重建菜品格，让它变成真正可点的格子。
        /// 只动面板自带的 m_RecipeInstances，不改游戏逻辑。
        /// </summary>
        internal static int InjectRecipes(IEnumerable<Recipe> recipes, string reason)
        {
            if (_current == null) return 0;

            var added = 0;
            try
            {
                var list = _current.m_RecipeInstances;
                if (list == null) return 0;

                foreach (var r in recipes)
                {
                    if (r == null) continue;

                    var exists = false;
                    for (var i = 0; i < list.Count; i++)
                    {
                        if (list[i] != null && list[i].FoodID == r.FoodID) { exists = true; break; }
                    }
                    if (exists) continue;

                    list.Add(r);
                    added++;
                }

                if (added > 0)
                {
                    _current.UpdateRecipeField();
                    OrderHelperPlugin.LogSource.LogInfo(
                        $"[OrderHelper] 已注入推荐菜 {added} 道（{reason}），面板列表共 {list.Count} 道");
                }
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 注入推荐菜失败: " + e.Message);
            }

            return added;
        }

        internal static bool HasDish(int foodId)
        {
            try { return Buttons.ContainsKey(foodId); }
            catch { return false; }
        }

        internal static bool TrySelectDish(Recipe recipe, string dishName)
        {
            if (!IsOpen || _current == null) return false;

            try
            {
                if (recipe != null && Buttons.TryGetValue(recipe.FoodID, out var button) && button != null)
                {
                    button.CallSubmitAction();
                    OrderHelperPlugin.LogSource.LogInfo(
                        $"[OrderHelper] 点击菜品格 food={recipe.FoodID} name={dishName} buttons={Buttons.Count}");
                    return true;
                }

                OrderHelperPlugin.LogSource.LogInfo(
                    $"[OrderHelper] 菜品格未登记 recipe={(recipe == null ? -1 : recipe.Id)} food={(recipe == null ? -1 : recipe.FoodID)} name={dishName} buttons={Buttons.Count}");
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 点击菜品格失败: " + e.Message);
            }

            // 兜底：按文字点
            if (!string.IsNullOrEmpty(dishName))
                return PanelClicker.TryClick(_current.transform, dishName);

            return false;
        }

        private static Rect ComputeRect(WorkSceneCookingSelectionPannel panel)
        {
            try
            {
                if (panel == null) return default;

                var transform = panel.transform;
                if (transform == null) return default;

                var rectTransform = transform.TryCast<RectTransform>();
                if (rectTransform == null) return default;

                var corners = new Il2CppStructArray<Vector3>(4);
                rectTransform.GetWorldCorners(corners);

                var min = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
                var max = RectTransformUtility.WorldToScreenPoint(null, corners[2]);

                return new Rect(min.x, Screen.height - max.y, max.x - min.x, max.y - min.y);
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 计算面板位置失败: " + e.Message);
                return default;
            }
        }
    }

    [HarmonyPatch(typeof(WorkSceneCookingSelectionPannel), "OnPanelOpen")]
    internal static class CookerPanelOpenPatch
    {
        [HarmonyPostfix]
        private static void Postfix(WorkSceneCookingSelectionPannel __instance) =>
            CookerPanelWatcher.SetOpen(__instance);
    }

    [HarmonyPatch(typeof(WorkSceneCookingSelectionPannel), "OnPanelClose")]
    internal static class CookerPanelClosePatch
    {
        [HarmonyPostfix]
        private static void Postfix() => CookerPanelWatcher.SetClosed();
    }

    /// <summary>
    /// 游戏给每个菜品格绑定回调时会经过这里，我们把 (菜品, 按钮) 记下来，
    /// 之后用按钮自己的提交动作触发，等价于玩家真点了一下。
    /// </summary>
    [HarmonyPatch(typeof(WorkSceneCookingSelectionPannel), "OnRecipeElementEnabled")]
    internal static class RecipeElementEnabledPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Recipe recipe, UIElementCluster cluster, UIButtonSimple uiButtonSimple) =>
            CookerPanelWatcher.RegisterRecipeButton(recipe, uiButtonSimple);
    }

    /// <summary>
    /// 游戏给每个食材格绑定回调时会经过这里，记下食材和它的按钮，
    /// 之后点推荐菜就能直接调面板的 OnIngElementSelected 完成“选中食材”。
    /// </summary>
    [HarmonyPatch(typeof(WorkSceneCookingSelectionPannel), "OnIngElementEnabled")]
    internal static class IngElementEnabledPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Il2CppSystem.Collections.Generic.KeyValuePair<Ingredient, int> objectBase,
            UIElementCluster cluster, UIButtonSimple uiButtonSimple) =>
            CookerPanelWatcher.RegisterIngredient(objectBase, cluster, uiButtonSimple);
    }
}
