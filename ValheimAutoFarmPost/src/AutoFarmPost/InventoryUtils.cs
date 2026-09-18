using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
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

        /// <summary>How many more of this item the given rows can take.</summary>
        public static int Room(Inventory inv, GameObject itemPrefab, int y0, int y1)
        {
            ItemDrop.ItemData proto = GetPrototype(itemPrefab);
            if (proto == null || proto.m_shared == null)
            {
                return 0;
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
                }
            }

            return room;
        }

        /// <summary>Puts items into the given rows only. Returns how many were actually stored.</summary>
        public static int Store(Inventory inv, GameObject itemPrefab, int amount, int y0, int y1)
        {
            ItemDrop.ItemData proto = GetPrototype(itemPrefab);
            if (proto == null || proto.m_shared == null || amount <= 0)
            {
                return 0;
            }

            List<ItemDrop.ItemData> items = GetItems(inv);
            if (items == null)
            {
                return 0;
            }

            int width = inv.GetWidth();
            int maxStack = Mathf.Max(1, proto.m_shared.m_maxStackSize);
            int left = amount;

            // pass 0: top up stacks that are already there, pass 1: use free slots
            for (int pass = 0; pass < 2 && left > 0; pass++)
            {
                for (int y = y0; y <= y1 && left > 0; y++)
                {
                    for (int x = 0; x < width && left > 0; x++)
                    {
                        ItemDrop.ItemData slot = inv.GetItemAt(x, y);

                        if (pass == 0)
                        {
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
                        else
                        {
                            if (slot != null)
                            {
                                continue;
                            }

                            int add = Mathf.Min(maxStack, left);
                            ItemDrop.ItemData item = proto.Clone();
                            item.m_stack = add;
                            item.m_gridPos = new Vector2i(x, y);
                            item.m_dropPrefab = itemPrefab;
                            items.Add(item);
                            left -= add;
                        }
                    }
                }
            }

            if (left != amount)
            {
                NotifyChanged(inv);
            }

            return amount - left;
        }

        // The item list and its change notification are reached by name: that works whether the
        // game keeps them public or private, and does not depend on a method signature.
        private static FieldInfo _itemsField;
        private static MethodInfo _changedMethod;
        private static bool _reflectionChecked;
        private static bool _warned;

        private static List<ItemDrop.ItemData> GetItems(Inventory inv)
        {
            if (!_reflectionChecked)
            {
                _reflectionChecked = true;
                _itemsField = AccessTools.Field(typeof(Inventory), "m_inventory");
                _changedMethod = AccessTools.Method(typeof(Inventory), "Changed", new Type[0]);
            }

            if (_itemsField != null)
            {
                try
                {
                    List<ItemDrop.ItemData> list = _itemsField.GetValue(inv) as List<ItemDrop.ItemData>;
                    if (list != null)
                    {
                        return list;
                    }
                }
                catch (Exception)
                {
                    // handled below
                }
            }

            if (!_warned)
            {
                _warned = true;
                AutoFarmPlugin.Log.LogError("Could not access the container inventory - harvesting is disabled.");
            }

            return null;
        }

        private static void NotifyChanged(Inventory inv)
        {
            if (_changedMethod == null)
            {
                return;
            }

            try
            {
                _changedMethod.Invoke(inv, null);
            }
            catch (Exception)
            {
                // the container is saved explicitly as well
            }
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
