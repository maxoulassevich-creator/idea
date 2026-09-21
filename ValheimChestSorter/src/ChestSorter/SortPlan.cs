using System.Collections.Generic;
using UnityEngine;

namespace ChestSorter
{
    /// <summary>One stack as it will look after sorting. The item object itself is reused.</summary>
    internal class PlannedStack
    {
        public ItemDrop.ItemData Item;
        public int Stack;
        public string Category;
        public string SortKey;
        public ChestInfo Origin;
    }

    /// <summary>What one chest will hold.</summary>
    internal class ChestPlan
    {
        public ChestInfo Chest;
        public string Category;
        public readonly List<PlannedStack> Stacks = new List<PlannedStack>();
        public string Label = string.Empty;
        public bool Mixed;

        public int Free
        {
            get { return Chest.Capacity - Stacks.Count; }
        }
    }

    internal class SortPlan
    {
        public bool Valid;
        public string Error;
        public Vector3 Center;

        public readonly List<ChestPlan> Chests = new List<ChestPlan>();
        public readonly List<string> Warnings = new List<string>();

        public int StacksBefore;
        public int StacksAfter;
        public int Merged;
        public int Moved;
        public int ItemsTotal;
    }
}
