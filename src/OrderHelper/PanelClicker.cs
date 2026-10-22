using System;

using DEYU.AdpUISystem.LogicalCollection;

using TMPro;

using UnityEngine;
using UnityEngine.EventSystems;

namespace MystiaOrderHelper
{
    /// <summary>
    /// 在游戏 UI 面板里按文字找到对应元素，并模拟一次点击。
    /// 优先调用元素上的 UIButtonSimple.OnPointerDown/OnPointerUp（游戏自己的点击回调链），
    /// 找不到按钮组件时退回 ExecuteEvents 的 pointerClick 派发。
    /// </summary>
    internal static class PanelClicker
    {
        internal static bool TryClick(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return false;

            try
            {
                var visited = 0;
                var hit = Find(root, name, ref visited, 0);
                if (hit == null)
                {
                    OrderHelperPlugin.LogSource.LogInfo($"[OrderHelper] 点击未找到元素 '{name}'");
                    DumpTree(root);
                    return false;
                }

                OrderHelperPlugin.LogSource.LogInfo($"[OrderHelper] 点击元素 '{name}' node={NodePath(hit)}");

                var node = hit;
                for (var i = 0; i < 12 && node != null; i++)
                {
                    var button = node.GetComponent<UIButtonSimple>();
                    if (button != null && PressButton(button, node)) return true;

                    if (ExecuteEvents.Execute(node.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler))
                        return true;

                    node = node.parent;
                }

                OrderHelperPlugin.LogSource.LogInfo($"[OrderHelper] 点击无响应（无 UIButtonSimple / 无 pointerClick 处理器）'{name}'");
                DumpTree(root);
                return false;
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 模拟点击失败: " + e.Message);
                return false;
            }
        }

        internal static void DumpTree(Transform root)
        {
            try
            {
                var count = 0;
                DumpTree(root, 0, ref count);
                OrderHelperPlugin.LogSource.LogInfo($"[OrderHelper] 树转储结束 nodes={count}");
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 树转储失败: " + e.Message);
            }
        }

        private static void DumpTree(Transform t, int depth, ref int count)
        {
            if (t == null || count > 2500 || depth > 24) return;
            count++;

            try
            {
                var indent = new string(' ', depth * 2);
                var labels = new System.Text.StringBuilder();
                foreach (var c in t.GetComponents<Component>())
                {
                    if (c == null) continue;
                    labels.Append(c.GetIl2CppType().Name).Append(',');
                }

                var text = t.GetComponent<TMP_Text>();
                var value = text != null ? text.text : "";
                OrderHelperPlugin.LogSource.LogInfo(
                    $"[OrderHelper] 树 {indent}{t.name} act={t.gameObject.activeSelf} c=[{labels}] txt='{value}'");
            }
            catch
            {
                // 忽略单个节点错误
            }

            for (var i = 0; i < t.childCount; i++)
                DumpTree(t.GetChild(i), depth + 1, ref count);
        }

        internal static void DebugDump(Transform root, string name)
        {
            try
            {
                var visited = 0;
                var hit = Find(root, name, ref visited, 0);
                if (hit == null)
                {
                    OrderHelperPlugin.LogSource.LogInfo($"[OrderHelper] 调试：未找到 '{name}'");
                    return;
                }

                var node = hit;
                for (var i = 0; i < 8 && node != null; i++)
                {
                    var comps = node.GetComponents<Component>();
                    var labels = new System.Text.StringBuilder();
                    foreach (var c in comps)
                    {
                        if (c == null) continue;
                        labels.Append(c.GetIl2CppType().Name).Append(',');
                    }
                    OrderHelperPlugin.LogSource.LogInfo($"[OrderHelper] 调试链[{i}] {node.name} ({labels})");
                    node = node.parent;
                }
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] 调试失败: " + e.Message);
            }
        }

        private static bool PressButton(UIButtonSimple button, Transform node)
        {
            try
            {
                var data = new PointerEventData(EventSystem.current);
                var rt = node as RectTransform;
                data.position = rt != null
                    ? (Vector2)RectTransformUtility.WorldToScreenPoint(null, rt.position)
                    : new Vector2(Screen.width / 2f, Screen.height / 2f);
                data.button = PointerEventData.InputButton.Left;
                data.clickCount = 1;

                button.OnPointerDown(data);
                button.OnPointerUp(data);
                OrderHelperPlugin.LogSource.LogInfo($"[OrderHelper] 已用 UIButtonSimple 点击 {node.name} at ({data.position.x:0},{data.position.y:0})");
                return true;
            }
            catch (Exception e)
            {
                OrderHelperPlugin.LogSource.LogWarning("[OrderHelper] UIButtonSimple 点击失败: " + e.Message);
                return false;
            }
        }

        private static string NodePath(Transform t)
        {
            var sb = new System.Text.StringBuilder();
            var n = t;
            for (var i = 0; i < 5 && n != null; i++)
            {
                if (sb.Length > 0) sb.Insert(0, "/");
                sb.Insert(0, n.name);
                n = n.parent;
            }
            return sb.ToString();
        }

        private static Transform Find(Transform t, string name, ref int visited, int depth)
        {
            if (t == null || visited > 4000 || depth > 16) return null;
            visited++;

            var childCount = t.childCount;
            for (var i = 0; i < childCount; i++)
            {
                var child = t.GetChild(i);
                if (child == null) continue;

                var text = child.GetComponent<TMP_Text>();
                if (text != null && !string.IsNullOrEmpty(text.text))
                {
                    var value = text.text.Trim();
                    if (value == name || value.Contains(name)) return child;
                }

                var found = Find(child, name, ref visited, depth + 1);
                if (found != null) return found;
            }

            return null;
        }
    }
}

