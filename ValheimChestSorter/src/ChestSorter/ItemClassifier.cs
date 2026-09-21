using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChestSorter
{
    /// <summary>
    ///     Decides which shelf an item belongs to.
    ///
    ///     Item types are matched by the *name* of the enum value rather than the value itself,
    ///     so a game update that adds or reorders item types cannot break the mod, and item types
    ///     added by other mods still land somewhere sensible.
    /// </summary>
    internal static class ItemClassifier
    {
        private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>();

        private static readonly HashSet<string> WeaponTypes = new HashSet<string>
        {
            "OneHandedWeapon", "TwoHandedWeapon", "TwoHandedWeaponLeft", "Bow", "Shield", "Torch",
            "Attach_Atgeir"
        };

        private static readonly HashSet<string> ArmorTypes = new HashSet<string>
        {
            "Helmet", "Chest", "Legs", "Hands", "Shoulder", "Utility", "Customization"
        };

        private static readonly HashSet<string> AmmoTypes = new HashSet<string>
        {
            "Ammo", "AmmoNonEquipable"
        };

        // Explicit lists first - they beat the name heuristics below.
        private static readonly HashSet<string> Metals = new HashSet<string>
        {
            "Copper", "CopperOre", "CopperScrap", "Tin", "TinOre", "Bronze", "Iron", "IronOre", "IronScrap",
            "Silver", "SilverOre", "BlackMetal", "BlackMetalScrap", "Flametal", "FlametalOre", "FlametalOreNew",
            "BronzeNails", "IronNails", "Obsidian", "Chain", "Tar", "Coal", "Charcoal"
        };

        private static readonly HashSet<string> Woods = new HashSet<string>
        {
            "Wood", "RoundLog", "FineWood", "ElderBark", "YggdrasilWood", "BlackMarble", "Stone", "Flint",
            "Resin", "Grausten", "Marble", "Sap", "Bark", "AskBladder"
        };

        private static readonly HashSet<string> Hides = new HashSet<string>
        {
            "LeatherScraps", "DeerHide", "TrollHide", "WolfPelt", "LoxPelt", "ScaleHide", "Chitin",
            "LinenThread", "JuteRed", "JuteBlue", "Feathers", "Wool", "Hare_Hide", "AsksvinHide"
        };

        private static readonly HashSet<string> MonsterParts = new HashSet<string>
        {
            "GreydwarfEye", "Bloodbag", "Entrails", "Ooze", "BoneFragments", "WitheredBone", "SurtlingCore",
            "Needle", "Guck", "DragonTear", "FreezeGland", "Thunderstone", "YmirRemains", "Softtissue",
            "BlackCore", "Wisp", "Eitr", "Crystal", "TrophyFragment", "ProustitePowder", "CeramicPlate",
            "MorgenHeart", "CharredBone", "SulfurStone", "BonemawTooth", "VoltureEgg"
        };

        private static readonly HashSet<string> Seeds = new HashSet<string>
        {
            "Barley", "Flax", "BarleyFlour", "Pickable_SeedCarrot", "Acorn", "BeechSeeds", "FirCone",
            "PineCone", "BirchSeeds", "AncientSeed", "MagecapSeeds", "JotunPuffsSeeds", "Vineberry",
            "VineGreenSeeds"
        };

        /// <summary>Epic Loot crafting materials are named by rarity, e.g. Runestone_Magic.</summary>
        private static readonly string[] EnchantPrefixes =
        {
            "Runestone", "Essence", "Shard", "Reagent", "Dust", "LeatherBelt", "Enchant"
        };

        public static void ResetCache()
        {
            Cache.Clear();
        }

        public static string Classify(ItemDrop.ItemData item)
        {
            if (item == null || item.m_shared == null)
            {
                return Categories.Misc;
            }

            string prefabName = Util.PrefabName(item);

            // magic gear is judged per item, never cached by prefab name
            if (ModConfig.SeparateMagicItems.Value && EpicLoot.IsMagicItem(item))
            {
                return Categories.MagicItem;
            }

            string cached;
            if (Cache.TryGetValue(prefabName, out cached))
            {
                return cached;
            }

            string category = Decide(item, prefabName);
            Cache[prefabName] = category;
            return category;
        }

        private static string Decide(ItemDrop.ItemData item, string prefabName)
        {
            // 1. what the player configured wins over everything
            string forced = ModConfig.GetOverride(prefabName);
            if (forced != null)
            {
                return forced;
            }

            // 2. enchanting materials
            for (int i = 0; i < EnchantPrefixes.Length; i++)
            {
                if (prefabName.StartsWith(EnchantPrefixes[i], StringComparison.OrdinalIgnoreCase) &&
                    prefabName.IndexOf('_') > 0)
                {
                    return Categories.Enchant;
                }
            }

            // 3. by item type
            string type = item.m_shared.m_itemType.ToString();

            if (type == "Trophy")
            {
                return Categories.Trophy;
            }

            if (AmmoTypes.Contains(type))
            {
                return Categories.Ammo;
            }

            if (type == "Tool")
            {
                return Categories.Tool;
            }

            if (WeaponTypes.Contains(type))
            {
                return Categories.Weapon;
            }

            if (ArmorTypes.Contains(type))
            {
                return Categories.Armor;
            }

            if (type == "Consumable")
            {
                bool nourishing = item.m_shared.m_food > 0f || item.m_shared.m_foodStamina > 0f;
                return nourishing ? Categories.Food : Categories.Potion;
            }

            if (type == "Fish")
            {
                return Categories.Food;
            }

            // 4. materials, by name
            if (Metals.Contains(prefabName) || Matches(prefabName, "Ore", "Scrap", "Metal", "Ingot"))
            {
                return Categories.Metal;
            }

            if (Woods.Contains(prefabName) || Matches(prefabName, "Wood", "Stone", "Log"))
            {
                return Categories.Wood;
            }

            if (Hides.Contains(prefabName) || Matches(prefabName, "Hide", "Pelt", "Leather", "Thread", "Jute"))
            {
                return Categories.Hide;
            }

            if (prefabName.IndexOf("seed", StringComparison.OrdinalIgnoreCase) >= 0 || Seeds.Contains(prefabName))
            {
                return Categories.Seed;
            }

            if (MonsterParts.Contains(prefabName) || Matches(prefabName, "Trophy"))
            {
                return Categories.Monster;
            }

            // 5. anything a trader pays for and that is not a resource above
            if (item.m_shared.m_value > 0)
            {
                return Categories.Valuable;
            }

            return Categories.Misc;
        }

        private static bool Matches(string name, params string[] parts)
        {
            for (int i = 0; i < parts.Length; i++)
            {
                if (name.IndexOf(parts[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
