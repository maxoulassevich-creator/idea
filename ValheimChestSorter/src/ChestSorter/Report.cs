using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ChestSorter
{
    /// <summary>
    ///     Everything the mod tells the player: the plan goes into the chat (where it can be read
    ///     and scrolled), a short line onto the screen, and the full detail into the log.
    /// </summary>
    internal static class Report
    {
        private static Type _chatType;
        private static MethodInfo _addString;
        private static bool _chatChecked;

        public static void Line(string text)
        {
            Plugin.Log.LogInfo(text);
            Chat(text);
        }

        public static void Center(string text)
        {
            try
            {
                MessageHud hud = MessageHud.instance;
                if (hud != null)
                {
                    hud.ShowMessage(MessageHud.MessageType.Center, text);
                }
            }
            catch (Exception)
            {
                // the chat line is enough
            }
        }

        private static void Chat(string text)
        {
            try
            {
                if (!_chatChecked)
                {
                    _chatChecked = true;
                    _chatType = AccessTools.TypeByName("Chat");
                    if (_chatType != null)
                    {
                        _addString = AccessTools.Method(_chatType, "AddString", new[] { typeof(string) });
                    }
                }

                if (_chatType == null || _addString == null)
                {
                    return;
                }

                object instance = _chatType.GetField("instance", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null);
                if (instance == null)
                {
                    return;
                }

                _addString.Invoke(instance, new object[] { text });
            }
            catch (Exception)
            {
                // log only, then
            }
        }

        public static void Plan(SortPlan plan)
        {
            bool ru = Util.IsRussian;

            if (plan == null || !plan.Valid)
            {
                string error = plan != null && !string.IsNullOrEmpty(plan.Error)
                    ? plan.Error
                    : (ru ? "План построить не удалось." : "Could not build a plan.");
                Line("<color=#FF8080>[" + Name(ru) + "] " + error + "</color>");
                Center(error);
                return;
            }

            Line("<color=#FFD98A>――― " + Name(ru) + " ―――</color>");
            Line(ru
                ? string.Format("Сундуков: {0} · стопок сейчас: {1} · предметов: {2}",
                    plan.Chests.Count, plan.StacksBefore, plan.ItemsTotal)
                : string.Format("Chests: {0} · stacks now: {1} · items: {2}",
                    plan.Chests.Count, plan.StacksBefore, plan.ItemsTotal));

            List<ChestPlan> ordered = new List<ChestPlan>(plan.Chests);
            ordered.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.Ordinal));

            foreach (ChestPlan chestPlan in ordered)
            {
                Line(string.Format("  {0} — {1}/{2} — {3}",
                    Pad(chestPlan.Label, 22),
                    chestPlan.Stacks.Count,
                    chestPlan.Chest.Capacity,
                    chestPlan.Chest.Describe(plan.Center)));
            }

            Line(ru
                ? string.Format("Переедет стопок: {0} · объединится: {1} · станет стопок: {2}",
                    plan.Moved, plan.Merged, plan.StacksAfter)
                : string.Format("Stacks to move: {0} · to merge: {1} · stacks after: {2}",
                    plan.Moved, plan.Merged, plan.StacksAfter));

            foreach (string warning in plan.Warnings)
            {
                Line("<color=#FFB060>  ! " + warning + "</color>");
            }

            string apply = ModConfig.ApplyKey.Value.ToString();
            Line(ru
                ? "<color=#A0FFA0>" + apply + " — применить</color>"
                : "<color=#A0FFA0>" + apply + " — apply</color>");

            Center(ru
                ? string.Format("План в чате: {0} сундуков\n{1} — применить", plan.Chests.Count, apply)
                : string.Format("Plan in the chat: {0} chests\n{1} — apply", plan.Chests.Count, apply));
        }

        public static void Result(SortResult result, bool undo)
        {
            bool ru = Util.IsRussian;

            if (result == null || !result.Ok)
            {
                string error = result != null && !string.IsNullOrEmpty(result.Error)
                    ? result.Error
                    : (ru ? "Не получилось." : "Failed.");
                Line("<color=#FF8080>[" + Name(ru) + "] " + error + "</color>");
                Center(error);
                return;
            }

            if (undo)
            {
                string text = ru
                    ? string.Format("Сортировка отменена, содержимое {0} сундуков возвращено на место.", result.Chests)
                    : string.Format("Sorting undone, {0} chests are back as they were.", result.Chests);
                Line("<color=#A0FFA0>[" + Name(ru) + "] " + text + "</color>");
                Center(ru ? "Сортировка отменена" : "Sorting undone");
                return;
            }

            string done = ru
                ? string.Format("Готово: {0} сундуков, перемещено стопок {1}, объединено {2}.",
                    result.Chests, result.Moved, result.Merged)
                : string.Format("Done: {0} chests, {1} stacks moved, {2} merged.",
                    result.Chests, result.Moved, result.Merged);

            Line("<color=#A0FFA0>[" + Name(ru) + "] " + done + "</color>");

            Line(result.Verified
                ? (ru ? "<color=#A0FFA0>Проверка: всё на месте, ничего не потеряно.</color>"
                      : "<color=#A0FFA0>Check: everything accounted for.</color>")
                : (ru ? "<color=#FF8080>Проверка не сошлась! Подробности в логе, сортировку можно отменить.</color>"
                      : "<color=#FF8080>Check failed! See the log, you can undo.</color>"));

            Line(ru
                ? "<color=#C0C0C0>" + ModConfig.UndoKey.Value + " — вернуть как было</color>"
                : "<color=#C0C0C0>" + ModConfig.UndoKey.Value + " — undo</color>");

            Center(ru ? "Сундуки отсортированы" : "Chests sorted");
        }

        private static string Name(bool ru)
        {
            return ru ? "Сортировщик" : "ChestSorter";
        }

        private static string Pad(string text, int width)
        {
            if (text == null)
            {
                text = string.Empty;
            }

            return text.Length >= width ? text : text + new string(' ', width - text.Length);
        }
    }
}
