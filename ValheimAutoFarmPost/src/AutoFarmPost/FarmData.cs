using System;
using System.Collections.Generic;
using UnityEngine;

namespace AutoFarmPost
{
    internal struct SeedInfo
    {
        /// <summary>Shared (localized) item name, e.g. "$item_carrotseeds". Stable across item instances.</summary>
        public string ItemName;

        public int Amount;
    }

    /// <summary>
    ///     Everything the mod knows about crops and seeds. Built once from the prefab list, so it
    ///     automatically covers every plantable thing the game (or another mod) provides.
    /// </summary>
    internal static class FarmData
    {
        /// <summary>Grown pickable prefab name -> the sapling prefab it came from.</summary>
        public static readonly Dictionary<string, GameObject> CropToPlant = new Dictionary<string, GameObject>();

        /// <summary>Sapling prefab name -> the seed it costs.</summary>
        public static readonly Dictionary<string, SeedInfo> PlantToSeed = new Dictionary<string, SeedInfo>();

        /// <summary>Seed item name -> the sapling it becomes.</summary>
        public static readonly Dictionary<string, GameObject> SeedToPlant = new Dictionary<string, GameObject>();

        /// <summary>Every pickable that grows from a sapling.</summary>
        public static readonly HashSet<string> CropPickables = new HashSet<string>();

        private static readonly Dictionary<string, bool> BerryCache = new Dictionary<string, bool>();

        private static bool _built;

        public static void EnsureBuilt()
        {
            if (_built || ZNetScene.instance == null)
            {
                return;
            }

            List<GameObject> prefabs = ZNetScene.instance.m_prefabs;
            if (prefabs == null || prefabs.Count == 0)
            {
                return;
            }

            foreach (GameObject prefab in prefabs)
            {
                if (prefab == null)
                {
                    continue;
                }

                Plant plant = prefab.GetComponent<Plant>();
                if (plant == null)
                {
                    continue;
                }

                // What does this sapling cost? (Piece requirements of the cultivator piece.)
                Piece piece = prefab.GetComponent<Piece>();
                if (piece != null && piece.m_resources != null && piece.m_resources.Length > 0)
                {
                    Piece.Requirement req = piece.m_resources[0];
                    if (req != null && req.m_resItem != null && req.m_resItem.m_itemData != null &&
                        req.m_resItem.m_itemData.m_shared != null)
                    {
                        SeedInfo info;
                        info.ItemName = req.m_resItem.m_itemData.m_shared.m_name;
                        info.Amount = Mathf.Max(1, req.m_amount);
                        PlantToSeed[prefab.name] = info;

                        if (!SeedToPlant.ContainsKey(info.ItemName))
                        {
                            SeedToPlant[info.ItemName] = prefab;
                        }
                    }
                }

                // What does it grow into? Only pickables count - that filters out tree saplings.
                if (plant.m_grownPrefabs == null)
                {
                    continue;
                }

                foreach (GameObject grown in plant.m_grownPrefabs)
                {
                    if (grown == null || grown.GetComponent<Pickable>() == null)
                    {
                        continue;
                    }

                    CropPickables.Add(grown.name);
                    if (!CropToPlant.ContainsKey(grown.name))
                    {
                        CropToPlant[grown.name] = prefab;
                    }
                }
            }

            _built = true;
            AutoFarmPlugin.Log.LogInfo("Known crops: " + CropPickables.Count + ", known seeds: " + SeedToPlant.Count);
        }

        public static bool IsHarvestable(Pickable pickable)
        {
            string name = Util.PrefabName(pickable.gameObject);

            if (ModConfig.ExcludeSet.Contains(name))
            {
                return false;
            }

            if (ModConfig.IncludeSet.Contains(name))
            {
                return true;
            }

            if (CropPickables.Contains(name))
            {
                return ModConfig.HarvestCrops.Value;
            }

            if (IsBerry(pickable, name))
            {
                return ModConfig.HarvestBerries.Value;
            }

            return false;
        }

        /// <summary>
        ///     A pickable counts as a berry bush when the item it drops is named like a berry.
        ///     That covers raspberries, blueberries, cloudberries and anything a future update
        ///     names the same way, while keeping mushrooms, thistle and flint out.
        /// </summary>
        private static bool IsBerry(Pickable pickable, string prefabName)
        {
            bool cached;
            if (BerryCache.TryGetValue(prefabName, out cached))
            {
                return cached;
            }

            bool berry = false;
            GameObject itemPrefab = pickable.m_itemPrefab;
            if (itemPrefab != null)
            {
                berry = ContainsBerry(itemPrefab.name);

                if (!berry)
                {
                    ItemDrop drop = itemPrefab.GetComponent<ItemDrop>();
                    if (drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null)
                    {
                        berry = ContainsBerry(drop.m_itemData.m_shared.m_name);
                    }
                }
            }

            BerryCache[prefabName] = berry;
            return berry;
        }

        private static bool ContainsBerry(string text)
        {
            return !string.IsNullOrEmpty(text) &&
                   text.IndexOf("berr", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
