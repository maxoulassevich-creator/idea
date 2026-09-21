// Offline test of the planner: builds fake chests, runs the real planning code and checks
// that nothing is lost, nothing overflows, and that full chests can be sorted at all.
using System;
using System.Collections.Generic;
using ChestSorter;
using UnityEngine;

public static class TestHarness
{
    private class ItemKind
    {
        public string Name;
        public int MaxStack;
        public string Type;
        public int Value;
        public float Food;
    }

    private static readonly ItemKind[] Kinds =
    {
        new ItemKind { Name = "Wood",          MaxStack = 100, Type = "Material" },
        new ItemKind { Name = "Stone",         MaxStack = 100, Type = "Material" },
        new ItemKind { Name = "FineWood",      MaxStack = 100, Type = "Material" },
        new ItemKind { Name = "CopperOre",     MaxStack = 30,  Type = "Material" },
        new ItemKind { Name = "IronScrap",     MaxStack = 30,  Type = "Material" },
        new ItemKind { Name = "Silver",        MaxStack = 30,  Type = "Material" },
        new ItemKind { Name = "LeatherScraps", MaxStack = 100, Type = "Material" },
        new ItemKind { Name = "DeerHide",      MaxStack = 100, Type = "Material" },
        new ItemKind { Name = "GreydwarfEye",  MaxStack = 100, Type = "Material" },
        new ItemKind { Name = "Entrails",      MaxStack = 50,  Type = "Material" },
        new ItemKind { Name = "CarrotSeeds",   MaxStack = 100, Type = "Material" },
        new ItemKind { Name = "Coins",         MaxStack = 999, Type = "Material", Value = 1 },
        new ItemKind { Name = "Carrot",        MaxStack = 50,  Type = "Consumable", Food = 15 },
        new ItemKind { Name = "CookedMeat",    MaxStack = 20,  Type = "Consumable", Food = 40 },
        new ItemKind { Name = "MeadHealth",    MaxStack = 10,  Type = "Consumable" },
        new ItemKind { Name = "SwordIron",     MaxStack = 1,   Type = "OneHandedWeapon" },
        new ItemKind { Name = "ArmorIronChest",MaxStack = 1,   Type = "Chest" },
        new ItemKind { Name = "ArrowIron",     MaxStack = 100, Type = "Ammo" },
        new ItemKind { Name = "AxeIron",       MaxStack = 1,   Type = "Tool" },
        new ItemKind { Name = "TrophyBoar",    MaxStack = 20,  Type = "Trophy" }
    };

    private static readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>();

    private static ItemDrop.ItemData MakeItem(ItemKind kind, int stack, string crafter)
    {
        GameObject prefab;
        if (!Prefabs.TryGetValue(kind.Name, out prefab))
        {
            prefab = new GameObject(kind.Name);
            Prefabs[kind.Name] = prefab;
        }

        ItemDrop.ItemData item = new ItemDrop.ItemData();
        item.m_shared = new ItemDrop.ItemData.SharedData
        {
            m_name = "$item_" + kind.Name.ToLowerInvariant(),
            m_maxStackSize = kind.MaxStack,
            m_itemType = ParseType(kind.Type),
            m_value = kind.Value,
            m_food = kind.Food
        };
        item.m_stack = Math.Min(stack, kind.MaxStack);
        item.m_dropPrefab = prefab;
        item.m_crafterName = crafter;
        return item;
    }

    private static ItemDrop.ItemData.ItemType ParseType(string name)
    {
        return (ItemDrop.ItemData.ItemType)Enum.Parse(typeof(ItemDrop.ItemData.ItemType), name);
    }

    private static ChestInfo MakeChest(int width, int height, Vector3 position)
    {
        ChestInfo chest = new ChestInfo();
        chest.Width = width;
        chest.Height = height;
        chest.Position = position;
        chest.Items = new List<ItemDrop.ItemData>();
        return chest;
    }

    private static Dictionary<string, int> Census(IEnumerable<ChestInfo> chests, bool snapshot)
    {
        Dictionary<string, int> counts = new Dictionary<string, int>();
        foreach (ChestInfo chest in chests)
        {
            foreach (ItemDrop.ItemData item in snapshot ? chest.Snapshot : chest.Items)
            {
                string key = Util.PrefabName(item) + "|" + item.m_quality + "|" + item.m_crafterName;
                int current;
                counts.TryGetValue(key, out current);
                counts[key] = current + item.m_stack;
            }
        }

        return counts;
    }

    private static int _failures;

    private static void Check(bool condition, string what)
    {
        if (condition)
        {
            Console.WriteLine("  ok   " + what);
        }
        else
        {
            _failures++;
            Console.WriteLine("  FAIL " + what);
        }
    }

    private static void Scenario(string title, int chestCount, int width, int height, int seed,
        bool fillCompletely, int uniqueItems)
    {
        Console.WriteLine();
        Console.WriteLine("=== " + title + " ===");

        System.Random rng = new System.Random(seed);
        List<ChestInfo> chests = new List<ChestInfo>();

        for (int i = 0; i < chestCount; i++)
        {
            ChestInfo chest = MakeChest(width, height, new Vector3(i * 2f, 0f, 0f));
            int slots = fillCompletely ? chest.Capacity : rng.Next(chest.Capacity / 2, chest.Capacity + 1);

            for (int s = 0; s < slots; s++)
            {
                ItemKind kind = Kinds[rng.Next(Kinds.Length)];
                // partial stacks on purpose, that is what merging is for
                int stack = kind.MaxStack == 1 ? 1 : rng.Next(1, kind.MaxStack + 1);
                string crafter = uniqueItems > 0 && rng.Next(100) < uniqueItems ? "Magic" + s : string.Empty;
                chest.Items.Add(MakeItem(kind, stack, crafter));
            }

            chest.Snapshot.AddRange(chest.Items);
            chests.Add(chest);
        }

        Dictionary<string, int> before = Census(chests, true);
        int slotsUsedBefore = 0;
        foreach (ChestInfo c in chests) slotsUsedBefore += c.Snapshot.Count;

        SortPlan plan = SortPlanner.Build(chests, Vector3.zero, new List<string>());

        Check(plan.Valid, "plan is valid" + (plan.Valid ? "" : ": " + plan.Error));
        if (!plan.Valid) return;

        // capacity
        bool capacityOk = true;
        int placed = 0;
        foreach (ChestPlan cp in plan.Chests)
        {
            if (cp.Stacks.Count > cp.Chest.Capacity) capacityOk = false;
            placed += cp.Stacks.Count;
        }

        Check(capacityOk, "no chest is over capacity");
        Check(placed == plan.StacksAfter, string.Format("every stack was placed ({0} of {1})", placed, plan.StacksAfter));

        // contents
        Dictionary<string, int> after = new Dictionary<string, int>();
        foreach (ChestPlan cp in plan.Chests)
        {
            foreach (PlannedStack stack in cp.Stacks)
            {
                string key = Util.PrefabName(stack.Item) + "|" + stack.Item.m_quality + "|" + stack.Item.m_crafterName;
                int current;
                after.TryGetValue(key, out current);
                after[key] = current + stack.Stack;
            }
        }

        bool sameContents = before.Count == after.Count;
        foreach (KeyValuePair<string, int> pair in before)
        {
            int now;
            after.TryGetValue(pair.Key, out now);
            if (now != pair.Value)
            {
                sameContents = false;
                Console.WriteLine("       lost/gained: " + pair.Key + " " + pair.Value + " -> " + now);
            }
        }

        Check(sameContents, "not a single item lost or gained");

        // one kind per chest
        int mixedChests = 0;
        foreach (ChestPlan cp in plan.Chests)
        {
            HashSet<string> categories = new HashSet<string>();
            foreach (PlannedStack stack in cp.Stacks) categories.Add(stack.Category);
            if (categories.Count > 1) mixedChests++;
        }

        // stacks never exceed their maximum
        bool stacksOk = true;
        foreach (ChestPlan cp in plan.Chests)
            foreach (PlannedStack stack in cp.Stacks)
                if (stack.Stack > Math.Max(1, stack.Item.m_shared.m_maxStackSize) || stack.Stack <= 0)
                    stacksOk = false;

        Check(stacksOk, "every stack is within its maximum size");

        // uniques survive untouched
        int uniques = 0;
        foreach (ChestPlan cp in plan.Chests)
            foreach (PlannedStack stack in cp.Stacks)
                if (!string.IsNullOrEmpty(stack.Item.m_crafterName)) uniques++;

        int uniquesBefore = 0;
        foreach (ChestInfo c in chests)
            foreach (ItemDrop.ItemData item in c.Snapshot)
                if (!string.IsNullOrEmpty(item.m_crafterName)) uniquesBefore++;

        Check(uniques == uniquesBefore,
            string.Format("special items kept one by one ({0} of {1})", uniques, uniquesBefore));

        Console.WriteLine(string.Format("       {0} chests, {1} slots used before, {2} stacks after, {3} merged, {4} moved, {5} mixed chests",
            chests.Count, slotsUsedBefore, plan.StacksAfter, plan.Merged, plan.Moved, mixedChests));

        foreach (ChestPlan cp in plan.Chests)
        {
            Console.WriteLine(string.Format("       [{0,-24}] {1,2}/{2}", cp.Label, cp.Stacks.Count, cp.Chest.Capacity));
        }
    }

    public static void Main()
    {
        ModConfig.Init(new BepInEx.Configuration.ConfigFile());
        ModConfig.MergeStacks.Value = true;
        ModConfig.SortInsideChest.Value = true;
        ModConfig.SeparateMagicItems.Value = true;

        Scenario("A storage room stuffed to the brim", 8, 6, 4, 1, true, 0);
        Scenario("Same, with enchanted gear mixed in", 8, 6, 4, 7, true, 15);
        Scenario("Few chests, many kinds of goods", 3, 6, 4, 11, true, 0);
        Scenario("Many small chests", 14, 5, 2, 23, true, 5);
        Scenario("Half empty chests", 6, 6, 4, 31, false, 10);
        Scenario("A single chest", 1, 6, 4, 41, true, 0);
        // the hard one: full chests where nothing can be merged at all, so sorting is a pure
        // rearrangement with not one free slot to work with
        Scenario("Full of one-of-a-kind items, nothing to merge", 6, 6, 4, 53, true, 100);

        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "ALL CHECKS PASSED" : _failures + " CHECKS FAILED");
        Environment.Exit(_failures == 0 ? 0 : 1);
    }
}
