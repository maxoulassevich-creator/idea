using System.Collections.Generic;
using UnityEngine;

namespace AutoFarmPost
{
    /// <summary>
    ///     Helpers that work on a rectangular part of a container, so the top rows (seeds) and the
    ///     bottom rows (harvest) can be treated as two independent inventories.
    ///     All row indexes are inclusive.
    /// </summary>
    internal static class InventoryUtils
    {
        public static ItemDrop.ItemData GetPrototype(GameObject itemPrefab)
        {
            if (itemPrefab == null)
            {
                return null;
            }

            ItemDrop drop = itemPrefab.GetComponent<ItemDrop>();
            return drop != null ? drop.m_itemData : null;
        }

        public static int CountItem(Inventory inv, string itemName, int y0, int y1)
        {
            int width = inv.GetWidth();
            int total = 0;

            for (int y = y0; y <= y1; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    ItemDrop.ItemData slot = inv.GetItemAt(x, y);
                    if (slot != null && slot.m_shared != null && slot.m_shared.m_name == itemName)
                    {
                        total += slot.m_stack;
                    }
                }
            }

            return total;
        }

        public static void CollectItemNames(Inventory inv, int y0, int y1, List<string> result)
        {
            int width = inv.GetWidth();

            for (int y = y0; y <= y1; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    ItemDrop.ItemData slot = inv.GetItemAt(x, y);
                    if (slot == null || slot.m_shared == null || slot.m_stack <= 0)
                    {
                        continue;
                    }

                    if (!result.Contains(slot.m_shared.m_name))
                    {
                        result.Add(slot.m_shared.m_name);
                    }
                }
            }
        }

        public static bool RemoveItems(Inventory inv, string itemName, int amount, int y0, int y1)
        {
            if (amount <= 0 || CountItem(inv, itemName, y0, y1) < amount)
            {
                return false;
            }

            int width = inv.GetWidth();
            int left = amount;

            for (int y = y0; y <= y1 && left > 0; y++)
            {
                for (int x = 0; x < width && left > 0; x++)
                {
                    ItemDrop.ItemData slot = inv.GetItemAt(x, y);
                    if (slot == null || slot.m_shared == null || slot.m_shared.m_name != itemName)
                    {
                        continue;
                    }

                    int take = Mathf.Min(left, slot.m_stack);
                    inv.RemoveItem(slot, take);
                    left -= take;
                }
            }

            return left == 0;
        }

        public static bool CanFit(Inventory inv, GameObject itemPrefab, int amount, int y0, int y1)
        {
            ItemDrop.ItemData proto = GetPrototype(itemPrefab);
            if (proto == null || proto.m_shared == null)
            {
                return false;
            }

            int width = inv.GetWidth();
            int maxStack = Mathf.Max(1, proto.m_shared.m_maxStackSize);
            int room = 0;

            for (int y = y0; y <= y1; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    ItemDrop.ItemData slot = inv.GetItemAt(x, y);
                    if (slot == null)
                    {
                        room += maxStack;
                    }
                    else if (SameItem(slot, proto))
                    {
                        room += Mathf.Max(0, maxStack - slot.m_stack);
                    }

                    if (room >= amount)
                    {
                        return true;
                    }
                }
            }

            return room >= amount;
        }

        /// <summary>Puts items into the given rows only. Returns how many were actually stored.</summary>
        public static int Store(Inventory inv, GameObject itemPrefab, int amount, int y0, int y1)
        {
            ItemDrop.ItemData proto = GetPrototype(itemPrefab);
            if (proto == null || proto.m_shared == null || amount <= 0)
            {
                return 0;
            }

            int width = inv.GetWidth();
            int maxStack = Mathf.Max(1, proto.m_shared.m_maxStackSize);
            int left = amount;

            // 1. top up stacks that are already there
            for (int y = y0; y <= y1 && left > 0; y++)
            {
                for (int x = 0; x < width && left > 0; x++)
                {
                    ItemDrop.ItemData slot = inv.GetItemAt(x, y);
                    if (slot == null || !SameItem(slot, proto))
                    {
                        continue;
                    }

                    int add = Mathf.Min(maxStack - slot.m_stack, left);
                    if (add <= 0)
                    {
                        continue;
                    }

                    slot.m_stack += add;
                    left -= add;
                }
            }

            // 2. use free slots
            for (int y = y0; y <= y1 && left > 0; y++)
            {
                for (int x = 0; x < width && left > 0; x++)
                {
                    if (inv.GetItemAt(x, y) != null)
                    {
                        continue;
                    }

                    int add = Mathf.Min(maxStack, left);
                    ItemDrop.ItemData item = proto.Clone();
                    item.m_stack = add;
                    item.m_gridPos = new Vector2i(x, y);
                    item.m_dropPrefab = itemPrefab;
                    inv.m_inventory.Add(item);
                    left -= add;
                }
            }

            if (left != amount)
            {
                inv.Changed();
            }

            return amount - left;
        }

        private static bool SameItem(ItemDrop.ItemData a, ItemDrop.ItemData b)
        {
            return a.m_shared != null && b.m_shared != null &&
                   a.m_shared.m_name == b.m_shared.m_name &&
                   a.m_quality == b.m_quality &&
                   a.m_variant == b.m_variant;
        }
    }
}
