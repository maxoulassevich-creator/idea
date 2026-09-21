using System;
using System.Collections.Generic;

namespace ChestSorter
{
    /// <summary>
    ///     Works out where everything should end up, without touching a single item.
    ///
    ///     The plan is always physically possible: merging partial stacks can only reduce the
    ///     number of stacks, so the sorted layout never needs more slots than the chests already
    ///     hold. The only thing that can go wrong is fragmentation - a category needing more
    ///     chests than are free - and that is handled by folding small categories together and,
    ///     as a last resort, by letting one chest hold a mix (which the label then says).
    /// </summary>
    internal static class SortPlanner
    {
        public static SortPlan Build(List<ChestInfo> chests, UnityEngine.Vector3 center, List<string> warnings)
        {
            SortPlan plan = new SortPlan { Center = center };
            if (warnings != null)
            {
                plan.Warnings.AddRange(warnings);
            }

            if (chests == null || chests.Count == 0)
            {
                plan.Error = Util.IsRussian
                    ? "Рядом нет сундуков, которые можно сортировать."
                    : "No chests to sort nearby.";
                return plan;
            }

            // ---- 1. take everything apart -------------------------------------------------
            List<PlannedStack> all = new List<PlannedStack>();

            foreach (ChestInfo chest in chests)
            {
                foreach (ItemDrop.ItemData item in chest.Snapshot)
                {
                    if (item == null || item.m_shared == null || item.m_stack <= 0)
                    {
                        continue;
                    }

                    string category = ItemClassifier.Classify(item);
                    if (!ModConfig.IsCategoryEnabled(category))
                    {
                        category = Categories.Misc;
                    }

                    all.Add(new PlannedStack
                    {
                        Item = item,
                        Stack = item.m_stack,
                        Category = category,
                        Origin = chest,
                        SortKey = Util.PrefabName(item) + "|" + (99 - item.m_quality)
                    });

                    plan.StacksBefore++;
                    plan.ItemsTotal += item.m_stack;
                }
            }

            if (all.Count == 0)
            {
                plan.Error = Util.IsRussian ? "Все сундуки пусты." : "All chests are empty.";
                return plan;
            }

            // ---- 2. merge partial stacks --------------------------------------------------
            List<PlannedStack> stacks = Merge(all);
            plan.StacksAfter = stacks.Count;
            plan.Merged = plan.StacksBefore - plan.StacksAfter;

            // ---- 3. how many slots does each category need? ------------------------------
            Dictionary<string, int> need = new Dictionary<string, int>();
            foreach (PlannedStack stack in stacks)
            {
                int current;
                need.TryGetValue(stack.Category, out current);
                need[stack.Category] = current + 1;
            }

            // ---- 4. fold the smallest categories together until they fit in the chests ----
            FoldCategories(stacks, need, chests.Count, plan);

            // ---- 5. give every chest a category ------------------------------------------
            Dictionary<ChestInfo, string> assignment = Assign(chests, need);

            Dictionary<ChestInfo, ChestPlan> plans = new Dictionary<ChestInfo, ChestPlan>();
            foreach (ChestInfo chest in chests)
            {
                string category;
                assignment.TryGetValue(chest, out category);
                ChestPlan chestPlan = new ChestPlan { Chest = chest, Category = category };
                plans[chest] = chestPlan;
                plan.Chests.Add(chestPlan);
            }

            // ---- 6. fill the chests --------------------------------------------------------
            Dictionary<string, List<PlannedStack>> byCategory = new Dictionary<string, List<PlannedStack>>();
            foreach (PlannedStack stack in stacks)
            {
                List<PlannedStack> list;
                if (!byCategory.TryGetValue(stack.Category, out list))
                {
                    list = new List<PlannedStack>();
                    byCategory[stack.Category] = list;
                }

                list.Add(stack);
            }

            List<PlannedStack> leftovers = new List<PlannedStack>();

            foreach (KeyValuePair<string, List<PlannedStack>> pair in byCategory)
            {
                List<PlannedStack> list = pair.Value;
                list.Sort((a, b) => string.Compare(a.SortKey, b.SortKey, StringComparison.Ordinal));

                List<ChestPlan> targets = new List<ChestPlan>();
                foreach (ChestPlan chestPlan in plan.Chests)
                {
                    if (chestPlan.Category == pair.Key)
                    {
                        targets.Add(chestPlan);
                    }
                }

                int index = 0;
                foreach (PlannedStack stack in list)
                {
                    while (index < targets.Count && targets[index].Free <= 0)
                    {
                        index++;
                    }

                    if (index >= targets.Count)
                    {
                        leftovers.Add(stack);
                        continue;
                    }

                    targets[index].Stacks.Add(stack);
                }
            }

            // ---- 7. whatever is left goes wherever there is room --------------------------
            if (leftovers.Count > 0)
            {
                leftovers.Sort((a, b) => string.Compare(a.SortKey, b.SortKey, StringComparison.Ordinal));

                foreach (PlannedStack stack in leftovers)
                {
                    ChestPlan target = null;
                    foreach (ChestPlan chestPlan in plan.Chests)
                    {
                        if (chestPlan.Free <= 0)
                        {
                            continue;
                        }

                        // prefer a chest that already holds this category, then the emptiest one
                        if (target == null || chestPlan.Category == stack.Category ||
                            (target.Category != stack.Category && chestPlan.Free > target.Free))
                        {
                            target = chestPlan;
                        }
                    }

                    if (target == null)
                    {
                        plan.Error = Util.IsRussian
                            ? "Не хватает места: сундуки не вмещают содержимое даже после объединения стопок."
                            : "Not enough room: the chests cannot hold their own contents after merging.";
                        return plan;
                    }

                    target.Stacks.Add(stack);
                    target.Mixed = true;
                }
            }

            // ---- 8. labels and statistics -------------------------------------------------
            BuildLabels(plan);

            foreach (ChestPlan chestPlan in plan.Chests)
            {
                foreach (PlannedStack stack in chestPlan.Stacks)
                {
                    if (stack.Origin != chestPlan.Chest)
                    {
                        plan.Moved++;
                    }
                }
            }

            plan.Valid = true;
            return plan;
        }

        /// <summary>
        ///     Partial stacks of the same plain item become full ones. Items carrying any extra
        ///     data are passed through untouched, so nothing unique is ever merged away.
        /// </summary>
        private static List<PlannedStack> Merge(List<PlannedStack> input)
        {
            if (!ModConfig.MergeStacks.Value)
            {
                return input;
            }

            List<PlannedStack> result = new List<PlannedStack>();
            Dictionary<string, List<PlannedStack>> groups = new Dictionary<string, List<PlannedStack>>();

            foreach (PlannedStack stack in input)
            {
                if (EpicLoot.IsUnique(stack.Item))
                {
                    result.Add(stack);
                    continue;
                }

                string key = stack.Category + "|" + Util.PrefabName(stack.Item) + "|" +
                             stack.Item.m_quality + "|" + stack.Item.m_variant;

                List<PlannedStack> group;
                if (!groups.TryGetValue(key, out group))
                {
                    group = new List<PlannedStack>();
                    groups[key] = group;
                }

                group.Add(stack);
            }

            foreach (KeyValuePair<string, List<PlannedStack>> pair in groups)
            {
                List<PlannedStack> group = pair.Value;

                int total = 0;
                foreach (PlannedStack stack in group)
                {
                    total += stack.Stack;
                }

                int maxStack = Math.Max(1, group[0].Item.m_shared.m_maxStackSize);
                int index = 0;

                while (total > 0 && index < group.Count)
                {
                    PlannedStack stack = group[index++];
                    stack.Stack = Math.Min(maxStack, total);
                    total -= stack.Stack;
                    result.Add(stack);
                }

                // cannot happen (each source stack is at most maxStack), but never lose items
                while (total > 0)
                {
                    PlannedStack spare = new PlannedStack
                    {
                        Item = group[group.Count - 1].Item.Clone(),
                        Stack = Math.Min(maxStack, total),
                        Category = group[0].Category,
                        Origin = group[0].Origin,
                        SortKey = group[0].SortKey
                    };

                    total -= spare.Stack;
                    result.Add(spare);
                }
            }

            return result;
        }

        /// <summary>
        ///     While there are more kinds of goods than chests, the smallest kind is poured into
        ///     the category it falls back to (meads into food, tools into weapons, and so on).
        /// </summary>
        private static void FoldCategories(List<PlannedStack> stacks, Dictionary<string, int> need,
            int chestCount, SortPlan plan)
        {
            while (need.Count > chestCount)
            {
                string smallest = null;
                int smallestNeed = int.MaxValue;

                foreach (KeyValuePair<string, int> pair in need)
                {
                    Category category = Categories.Get(pair.Key);
                    if (category.FallbackId == null || !need.ContainsKey(pair.Key))
                    {
                        continue;
                    }

                    if (pair.Value < smallestNeed)
                    {
                        smallestNeed = pair.Value;
                        smallest = pair.Key;
                    }
                }

                if (smallest == null)
                {
                    break;
                }

                string target = Categories.Get(smallest).FallbackId;

                foreach (PlannedStack stack in stacks)
                {
                    if (stack.Category == smallest)
                    {
                        stack.Category = target;
                    }
                }

                int moved = need[smallest];
                need.Remove(smallest);

                int current;
                need.TryGetValue(target, out current);
                need[target] = current + moved;

                plan.Warnings.Add(Util.IsRussian
                    ? string.Format("Сундуков меньше, чем видов: «{0}» сложено вместе с «{1}».",
                        Categories.Get(smallest).Label, Categories.Get(target).Label)
                    : string.Format("Fewer chests than kinds: {0} was folded into {1}.",
                        Categories.Get(smallest).Label, Categories.Get(target).Label));
            }
        }

        /// <summary>
        ///     Gives every chest a category, preferring the one it already mostly holds - that way
        ///     the sorting moves as few items as possible.
        /// </summary>
        private static Dictionary<ChestInfo, string> Assign(List<ChestInfo> chests, Dictionary<string, int> need)
        {
            Dictionary<ChestInfo, string> assignment = new Dictionary<ChestInfo, string>();
            List<ChestInfo> free = new List<ChestInfo>(chests);
            Dictionary<string, int> remaining = new Dictionary<string, int>(need);

            // how many slots of each category a chest already holds
            Dictionary<ChestInfo, Dictionary<string, int>> score =
                new Dictionary<ChestInfo, Dictionary<string, int>>();

            foreach (ChestInfo chest in chests)
            {
                Dictionary<string, int> counts = new Dictionary<string, int>();
                foreach (ItemDrop.ItemData item in chest.Snapshot)
                {
                    if (item == null || item.m_shared == null)
                    {
                        continue;
                    }

                    string category = ItemClassifier.Classify(item);
                    if (!ModConfig.IsCategoryEnabled(category))
                    {
                        category = Categories.Misc;
                    }

                    int value;
                    counts.TryGetValue(category, out value);
                    counts[category] = value + 1;
                }

                score[chest] = counts;
            }

            while (free.Count > 0 && remaining.Count > 0)
            {
                ChestInfo bestChest = null;
                string bestCategory = null;
                long bestScore = long.MinValue;

                foreach (KeyValuePair<string, int> pair in remaining)
                {
                    foreach (ChestInfo chest in free)
                    {
                        int held;
                        score[chest].TryGetValue(pair.Key, out held);

                        // already holds it > bigger chest > bigger category
                        long value = (long)held * 10000 + chest.Capacity * 10 + Math.Min(pair.Value, 9);
                        if (value > bestScore)
                        {
                            bestScore = value;
                            bestChest = chest;
                            bestCategory = pair.Key;
                        }
                    }
                }

                if (bestChest == null)
                {
                    break;
                }

                assignment[bestChest] = bestCategory;
                free.Remove(bestChest);

                int left = remaining[bestCategory] - bestChest.Capacity;
                if (left > 0)
                {
                    remaining[bestCategory] = left;
                }
                else
                {
                    remaining.Remove(bestCategory);
                }
            }

            // chests nobody needed stay empty and are used for overflow
            return assignment;
        }

        /// <summary>
        ///     Label from what a chest actually ends up holding: the main kind, its number when a
        ///     kind spans several chests, and a hint when the chest holds a mix.
        /// </summary>
        private static void BuildLabels(SortPlan plan)
        {
            Dictionary<string, List<ChestPlan>> byCategory = new Dictionary<string, List<ChestPlan>>();

            foreach (ChestPlan chestPlan in plan.Chests)
            {
                string main = Dominant(chestPlan);
                chestPlan.Category = main;

                if (main == null)
                {
                    continue;
                }

                List<ChestPlan> list;
                if (!byCategory.TryGetValue(main, out list))
                {
                    list = new List<ChestPlan>();
                    byCategory[main] = list;
                }

                list.Add(chestPlan);
            }

            foreach (KeyValuePair<string, List<ChestPlan>> pair in byCategory)
            {
                List<ChestPlan> list = pair.Value;
                Category category = Categories.Get(pair.Key);

                for (int i = 0; i < list.Count; i++)
                {
                    string label = category.Label;
                    if (list.Count > 1)
                    {
                        label += string.Format(" ({0}/{1})", i + 1, list.Count);
                    }

                    string extra = SecondCategory(list[i]);
                    if (extra != null)
                    {
                        list[i].Mixed = true;
                        label += " + " + extra;
                    }

                    list[i].Label = label;
                }
            }

            foreach (ChestPlan chestPlan in plan.Chests)
            {
                if (chestPlan.Stacks.Count == 0)
                {
                    chestPlan.Label = Util.IsRussian ? "ПУСТО" : "EMPTY";
                }
            }
        }

        private static string Dominant(ChestPlan chestPlan)
        {
            if (chestPlan.Stacks.Count == 0)
            {
                return null;
            }

            Dictionary<string, int> counts = new Dictionary<string, int>();
            foreach (PlannedStack stack in chestPlan.Stacks)
            {
                int value;
                counts.TryGetValue(stack.Category, out value);
                counts[stack.Category] = value + 1;
            }

            string best = null;
            int bestCount = -1;
            foreach (KeyValuePair<string, int> pair in counts)
            {
                if (pair.Value > bestCount)
                {
                    bestCount = pair.Value;
                    best = pair.Key;
                }
            }

            return best;
        }

        /// <summary>
        ///     What else ended up in this chest, so the label says what is really inside instead
        ///     of a vague "and some other things".
        /// </summary>
        private static string SecondCategory(ChestPlan chestPlan)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();

            foreach (PlannedStack stack in chestPlan.Stacks)
            {
                if (stack.Category == chestPlan.Category)
                {
                    continue;
                }

                int value;
                counts.TryGetValue(stack.Category, out value);
                counts[stack.Category] = value + 1;
            }

            if (counts.Count == 0)
            {
                return null;
            }

            string best = null;
            int bestCount = -1;
            foreach (KeyValuePair<string, int> pair in counts)
            {
                if (pair.Value > bestCount)
                {
                    bestCount = pair.Value;
                    best = pair.Key;
                }
            }

            string label = Categories.Get(best).Label;
            if (counts.Count > 1)
            {
                label += Util.IsRussian ? " и др." : " etc.";
            }

            return label;
        }
    }
}
