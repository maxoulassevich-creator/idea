using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChestSorter
{
    internal class SortResult
    {
        public bool Ok;
        public string Error;
        public int Chests;
        public int Moved;
        public int Merged;
        public int Stacks;
        public bool Verified;
    }

    /// <summary>
    ///     Carries out a plan. Items are never copied or recreated: the very same item objects are
    ///     moved between the chests' lists, so enchantments, backpack contents, durability and
    ///     crafter names survive untouched.
    ///
    ///     Everything is checked before the first item moves, the previous layout is recorded for
    ///     undo, and the contents are counted before and after to prove nothing was lost.
    /// </summary>
    internal static class SortExecutor
    {
        private class UndoEntry
        {
            public ItemDrop.ItemData Item;
            public ChestInfo Chest;
            public Vector2i GridPos;
            public int Stack;
        }

        private static List<UndoEntry> _undo;
        private static List<ChestInfo> _undoChests;
        private static Dictionary<ChestInfo, string> _undoLabels;

        /// <summary>What the chests looked like right after sorting - undo refuses if that changed.</summary>
        private static Dictionary<ChestInfo, List<ItemDrop.ItemData>> _afterState;
        private static Dictionary<ChestInfo, List<int>> _afterStacks;

        public static bool CanUndo
        {
            get { return _undo != null && _undo.Count > 0; }
        }

        public static SortResult Apply(SortPlan plan)
        {
            SortResult result = new SortResult();

            if (plan == null || !plan.Valid)
            {
                result.Error = plan != null && plan.Error != null
                    ? plan.Error
                    : (Util.IsRussian ? "Плана нет." : "No plan.");
                return result;
            }

            // ---- checks before anything moves ---------------------------------------------
            foreach (ChestPlan chestPlan in plan.Chests)
            {
                ChestInfo chest = chestPlan.Chest;

                if (chest.Container == null || chest.Nview == null || !chest.Nview.IsValid() ||
                    chest.Items == null)
                {
                    result.Error = Util.IsRussian
                        ? "Один из сундуков исчез или выгрузился. Ничего не тронуто, повторите план."
                        : "One of the chests is gone. Nothing was touched, build the plan again.";
                    return result;
                }

                if (ModConfig.SkipChestsInUse.Value && Util.IsContainerInUse(chest.Container))
                {
                    result.Error = Util.IsRussian
                        ? "Один из сундуков кто-то открыл. Ничего не тронуто."
                        : "Somebody opened one of the chests. Nothing was touched.";
                    return result;
                }

                if (chestPlan.Stacks.Count > chest.Capacity)
                {
                    result.Error = Util.IsRussian
                        ? "Внутренняя ошибка плана: стопок больше, чем ячеек. Сортировка отменена."
                        : "Internal plan error: more stacks than slots. Sorting cancelled.";
                    return result;
                }

                // the plan points at the very item objects seen during the preview - if anything
                // was taken out or added since then, applying it could duplicate an item
                if (!Unchanged(chest))
                {
                    result.Error = Util.IsRussian
                        ? "Содержимое сундуков изменилось после построения плана. Ничего не тронуто, постройте план заново."
                        : "The chests changed since the plan was built. Nothing was touched, build it again.";
                    return result;
                }
            }

            Dictionary<string, int> before = Census(plan);

            // ---- remember how it was, so it can be put back -------------------------------
            _undo = new List<UndoEntry>();
            _undoChests = new List<ChestInfo>();
            _undoLabels = new Dictionary<ChestInfo, string>();

            foreach (ChestPlan chestPlan in plan.Chests)
            {
                ChestInfo chest = chestPlan.Chest;
                _undoChests.Add(chest);
                _undoLabels[chest] = LabelStore.Get(chest.Container);

                foreach (ItemDrop.ItemData item in chest.Items)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    _undo.Add(new UndoEntry
                    {
                        Item = item,
                        Chest = chest,
                        GridPos = item.m_gridPos,
                        Stack = item.m_stack
                    });
                }
            }

            // ---- move ----------------------------------------------------------------------
            try
            {
                foreach (ChestPlan chestPlan in plan.Chests)
                {
                    chestPlan.Chest.Items.Clear();
                }

                foreach (ChestPlan chestPlan in plan.Chests)
                {
                    List<PlannedStack> stacks = chestPlan.Stacks;
                    if (ModConfig.SortInsideChest.Value)
                    {
                        stacks.Sort((a, b) => string.Compare(a.SortKey, b.SortKey, StringComparison.Ordinal));
                    }

                    int width = chestPlan.Chest.Width;
                    for (int i = 0; i < stacks.Count; i++)
                    {
                        PlannedStack stack = stacks[i];
                        stack.Item.m_stack = stack.Stack;
                        stack.Item.m_gridPos = new Vector2i(i % width, i / width);
                        chestPlan.Chest.Items.Add(stack.Item);
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Sorting failed halfway, putting everything back: " + e);
                RestoreFromUndo();
                result.Error = Util.IsRussian
                    ? "Сбой при перекладывании. Всё возвращено на место."
                    : "Something went wrong. Everything was put back.";
                return result;
            }

            // ---- save ----------------------------------------------------------------------
            foreach (ChestPlan chestPlan in plan.Chests)
            {
                Commit(chestPlan.Chest);

                if (ModConfig.WriteLabels.Value)
                {
                    LabelStore.Set(chestPlan.Chest.Container, chestPlan.Label);
                }
            }

            RememberResultState(plan);

            // ---- prove nothing was lost ----------------------------------------------------
            Dictionary<string, int> after = CensusLive(plan);
            result.Verified = Compare(before, after, plan);

            result.Ok = true;
            result.Chests = plan.Chests.Count;
            result.Moved = plan.Moved;
            result.Merged = plan.Merged;
            result.Stacks = plan.StacksAfter;
            return result;
        }

        public static SortResult Undo()
        {
            SortResult result = new SortResult();

            if (!CanUndo)
            {
                result.Error = Util.IsRussian ? "Отменять нечего." : "Nothing to undo.";
                return result;
            }

            foreach (ChestInfo chest in _undoChests)
            {
                if (chest.Container == null || chest.Nview == null || !chest.Nview.IsValid() || chest.Items == null)
                {
                    result.Error = Util.IsRussian
                        ? "Сундуки изменились или выгрузились - отмена невозможна."
                        : "The chests changed or unloaded - cannot undo.";
                    return result;
                }

                if (!MatchesResult(chest))
                {
                    result.Error = Util.IsRussian
                        ? "После сортировки содержимое сундуков уже меняли - отмена отменена, чтобы ничего не задвоить."
                        : "The chests were changed after sorting - undo refused so nothing gets duplicated.";
                    return result;
                }
            }

            RestoreFromUndo();

            result.Ok = true;
            result.Chests = _undoChests.Count;
            _undo = null;
            _undoChests = null;
            _undoLabels = null;
            _afterState = null;
            _afterStacks = null;
            return result;
        }

        private static void RestoreFromUndo()
        {
            if (_undo == null || _undoChests == null)
            {
                return;
            }

            foreach (ChestInfo chest in _undoChests)
            {
                if (chest.Items != null)
                {
                    chest.Items.Clear();
                }
            }

            foreach (UndoEntry entry in _undo)
            {
                if (entry.Chest.Items == null)
                {
                    continue;
                }

                entry.Item.m_stack = entry.Stack;
                entry.Item.m_gridPos = entry.GridPos;
                entry.Chest.Items.Add(entry.Item);
            }

            foreach (ChestInfo chest in _undoChests)
            {
                Commit(chest);

                string label;
                if (_undoLabels != null && _undoLabels.TryGetValue(chest, out label))
                {
                    LabelStore.Set(chest.Container, label);
                }
            }
        }

        private static void Commit(ChestInfo chest)
        {
            try
            {
                if (!chest.Nview.IsOwner())
                {
                    chest.Nview.ClaimOwnership();
                }

                Util.NotifyChanged(chest.Inventory);
                Util.SaveContainer(chest.Container);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not save a chest: " + e.Message);
            }
        }

        private static void RememberResultState(SortPlan plan)
        {
            _afterState = new Dictionary<ChestInfo, List<ItemDrop.ItemData>>();
            _afterStacks = new Dictionary<ChestInfo, List<int>>();

            foreach (ChestPlan chestPlan in plan.Chests)
            {
                List<ItemDrop.ItemData> items = new List<ItemDrop.ItemData>(chestPlan.Chest.Items);
                List<int> stacks = new List<int>();
                foreach (ItemDrop.ItemData item in items)
                {
                    stacks.Add(item != null ? item.m_stack : 0);
                }

                _afterState[chestPlan.Chest] = items;
                _afterStacks[chestPlan.Chest] = stacks;
            }
        }

        private static bool MatchesResult(ChestInfo chest)
        {
            List<ItemDrop.ItemData> expected;
            List<int> stacks;

            if (_afterState == null || !_afterState.TryGetValue(chest, out expected) ||
                _afterStacks == null || !_afterStacks.TryGetValue(chest, out stacks))
            {
                return false;
            }

            if (chest.Items.Count != expected.Count)
            {
                return false;
            }

            for (int i = 0; i < expected.Count; i++)
            {
                if (!ReferenceEquals(chest.Items[i], expected[i]) || chest.Items[i] == null ||
                    chest.Items[i].m_stack != stacks[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Is the chest still exactly as it was when the plan was built?</summary>
        private static bool Unchanged(ChestInfo chest)
        {
            if (chest.Items.Count != chest.Snapshot.Count)
            {
                return false;
            }

            for (int i = 0; i < chest.Items.Count; i++)
            {
                ItemDrop.ItemData live = chest.Items[i];
                ItemDrop.ItemData seen = chest.Snapshot[i];

                if (!ReferenceEquals(live, seen) || live == null || live.m_stack != seen.m_stack)
                {
                    return false;
                }
            }

            return true;
        }

        private static Dictionary<string, int> Census(SortPlan plan)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();

            foreach (ChestPlan chestPlan in plan.Chests)
            {
                foreach (ItemDrop.ItemData item in chestPlan.Chest.Snapshot)
                {
                    Count(counts, item);
                }
            }

            return counts;
        }

        private static Dictionary<string, int> CensusLive(SortPlan plan)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();

            foreach (ChestPlan chestPlan in plan.Chests)
            {
                foreach (ItemDrop.ItemData item in chestPlan.Chest.Items)
                {
                    Count(counts, item);
                }
            }

            return counts;
        }

        private static void Count(Dictionary<string, int> counts, ItemDrop.ItemData item)
        {
            if (item == null || item.m_shared == null)
            {
                return;
            }

            string key = Util.PrefabName(item) + "|" + item.m_quality + "|" + item.m_variant;
            int current;
            counts.TryGetValue(key, out current);
            counts[key] = current + item.m_stack;
        }

        private static bool Compare(Dictionary<string, int> before, Dictionary<string, int> after, SortPlan plan)
        {
            bool ok = true;

            foreach (KeyValuePair<string, int> pair in before)
            {
                int now;
                after.TryGetValue(pair.Key, out now);
                if (now != pair.Value)
                {
                    ok = false;
                    Plugin.Log.LogError(string.Format(
                        "Item count mismatch for {0}: {1} before, {2} after", pair.Key, pair.Value, now));
                }
            }

            foreach (KeyValuePair<string, int> pair in after)
            {
                if (!before.ContainsKey(pair.Key))
                {
                    ok = false;
                    Plugin.Log.LogError("Unexpected item after sorting: " + pair.Key);
                }
            }

            if (!ok)
            {
                plan.Warnings.Add(Util.IsRussian
                    ? "ВНИМАНИЕ: проверка содержимого не сошлась, подробности в логе. Можно отменить сортировку."
                    : "WARNING: the contents check did not add up, see the log. You can undo the sorting.");
            }

            return ok;
        }
    }
}
