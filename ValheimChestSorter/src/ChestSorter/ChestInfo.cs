using System.Collections.Generic;
using UnityEngine;

namespace ChestSorter
{
    /// <summary>One chest taking part in the sorting, with its live item list.</summary>
    internal class ChestInfo
    {
        public Container Container;
        public ZNetView Nview;
        public Inventory Inventory;

        /// <summary>The inventory's own list - writing here is what actually moves items.</summary>
        public List<ItemDrop.ItemData> Items;

        /// <summary>What the chest held when the plan was built.</summary>
        public List<ItemDrop.ItemData> Snapshot = new List<ItemDrop.ItemData>();

        public Vector3 Position;
        public string PrefabName;
        public int Width;
        public int Height;

        public int Capacity
        {
            get { return Width * Height; }
        }

        public string Describe(Vector3 from)
        {
            return Util.Describe(from, Position);
        }
    }
}
