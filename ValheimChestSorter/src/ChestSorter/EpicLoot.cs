using System;
using System.Collections.Generic;

namespace ChestSorter
{
    /// <summary>
    ///     Recognises items that other mods have attached data to - Epic Loot enchantments above
    ///     all. Nothing here references those mods: it only looks at the item's own fields, so it
    ///     works whether they are installed or not.
    ///
    ///     Such items are never merged with anything, so their data cannot be lost.
    /// </summary>
    internal static class EpicLoot
    {
        private static readonly string[] Markers =
        {
            "epicloot", "magicitem", "\"rarity\"", "rarity\":", "eidf", "randyknapp"
        };

        private static readonly Dictionary<ItemDrop.ItemData, bool> Cache =
            new Dictionary<ItemDrop.ItemData, bool>();

        public static void ResetCache()
        {
            Cache.Clear();
        }

        public static bool IsMagicItem(ItemDrop.ItemData item)
        {
            if (item == null)
            {
                return false;
            }

            bool cached;
            if (Cache.TryGetValue(item, out cached))
            {
                return cached;
            }

            bool magic = Detect(item);
            Cache[item] = magic;
            return magic;
        }

        private static bool Detect(ItemDrop.ItemData item)
        {
            if (Contains(item.m_crafterName))
            {
                return true;
            }

            return Contains(Util.CustomDataText(item));
        }

        private static bool Contains(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            for (int i = 0; i < Markers.Length; i++)
            {
                if (text.IndexOf(Markers[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     True when an item carries data that must never be merged away: enchantments,
        ///     a backpack's contents, a crafter's name, damaged durability.
        /// </summary>
        public static bool IsUnique(ItemDrop.ItemData item)
        {
            if (item == null || item.m_shared == null)
            {
                return true;
            }

            if (item.m_shared.m_maxStackSize <= 1)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(item.m_crafterName) || item.m_crafterID != 0)
            {
                return true;
            }

            return Util.HasCustomData(item);
        }
    }
}
