using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChestSorter
{
    /// <summary>
    ///     Finds the chests that may take part in the sorting. Containers register themselves
    ///     through a Harmony postfix; a scene scan is used if that patch could not be applied.
    /// </summary>
    internal static class ChestRegistry
    {
        private static readonly List<Container> Known = new List<Container>();

        internal static bool UseFallback;

        public static void Add(Container container)
        {
            if (container != null)
            {
                Known.Add(container);
            }
        }

        private static IEnumerable<Container> Candidates()
        {
            if (UseFallback)
            {
                return UnityEngine.Object.FindObjectsOfType<Container>();
            }

            for (int i = Known.Count - 1; i >= 0; i--)
            {
                if (Known[i] == null)
                {
                    Known.RemoveAt(i);
                }
            }

            return Known;
        }

        /// <summary>
        ///     All chests within the radius that the mod is allowed to rearrange. Anything skipped
        ///     for a reason worth telling the player about is written into <paramref name="warnings" />.
        /// </summary>
        public static List<ChestInfo> Scan(Vector3 center, float radius, List<string> warnings)
        {
            List<ChestInfo> result = new List<ChestInfo>();
            HashSet<Container> seen = new HashSet<Container>();
            float sqr = radius * radius;
            int inUse = 0;
            int locked = 0;

            foreach (Container container in Candidates())
            {
                if (container == null || !seen.Add(container))
                {
                    continue;
                }

                Vector3 position = container.transform.position;
                if ((position - center).sqrMagnitude > sqr)
                {
                    continue;
                }

                string prefab = Util.StripClone(container.gameObject.name);

                if (!IsSortable(prefab))
                {
                    continue;
                }

                if (ModConfig.SkipChestsInUse.Value && Util.IsContainerInUse(container))
                {
                    inUse++;
                    continue;
                }

                ZNetView nview = container.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid())
                {
                    continue;
                }

                // a real chest is a piece somebody built; this keeps the virtual containers that
                // other mods create (backpacks and the like) out of the sorting
                if (ModConfig.OnlyBuiltChests.Value && container.GetComponent<Piece>() == null)
                {
                    continue;
                }

                if (!HasAccess(position))
                {
                    locked++;
                    continue;
                }

                Inventory inventory = container.GetInventory();
                List<ItemDrop.ItemData> items = Util.GetItems(inventory);
                if (inventory == null || items == null)
                {
                    continue;
                }

                ChestInfo info = new ChestInfo
                {
                    Container = container,
                    Nview = nview,
                    Inventory = inventory,
                    Items = items,
                    Position = position,
                    PrefabName = prefab,
                    Width = Mathf.Max(1, inventory.GetWidth()),
                    Height = Mathf.Max(1, inventory.GetHeight())
                };

                info.Snapshot.AddRange(items);
                result.Add(info);
            }

            if (inUse > 0 && warnings != null)
            {
                warnings.Add(Util.IsRussian
                    ? string.Format("Пропущено открытых сундуков: {0}", inUse)
                    : string.Format("Skipped chests in use: {0}", inUse));
            }

            if (locked > 0 && warnings != null)
            {
                warnings.Add(Util.IsRussian
                    ? string.Format("Пропущено сундуков без доступа: {0}", locked)
                    : string.Format("Skipped chests you cannot access: {0}", locked));
            }

            // near chests first, so the plan reads in a natural order
            result.Sort((a, b) => (a.Position - center).sqrMagnitude.CompareTo((b.Position - center).sqrMagnitude));
            return result;
        }

        private static bool IsSortable(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
            {
                return false;
            }

            for (int i = 0; i < ModConfig.ExcludeList.Count; i++)
            {
                if (prefabName.IndexOf(ModConfig.ExcludeList[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }
            }

            if (ModConfig.SkipPrivateChests.Value &&
                prefabName.IndexOf("private", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            for (int i = 0; i < ModConfig.IncludeList.Count; i++)
            {
                if (prefabName.IndexOf(ModConfig.IncludeList[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return prefabName.IndexOf("chest", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasAccess(Vector3 position)
        {
            try
            {
                return Wards.HasAccess(position);
            }
            catch (Exception)
            {
                return true;
            }
        }
    }
}
