using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Wizardry
{
    /// <summary>
    ///     Where the wizard's finds come from: Chocolate Frog cards and memories lie in dungeon and buried treasure
    ///     chests; a boss leaves its memory; boss trophies shown to the Codex open the next year.
    /// </summary>
    internal static class Loot
    {
        public static readonly Dictionary<string, string[]> Trophies = new Dictionary<string, string[]>
        {
            { "eikthyr", new[] { "TrophyEikthyr" } },
            { "elder", new[] { "TrophyTheElder" } },
            { "bonemass", new[] { "TrophyBonemass" } },
            { "moder", new[] { "TrophyDragonQueen" } },
            { "yagluth", new[] { "TrophyGoblinKing" } },
            { "queen", new[] { "TrophySeekerQueen" } },
            { "fader", new[] { "TrophyFader" } },
            { "kall", new[] { "TrophyKall", "TrophyFimbulbringer", "TrophyKallFimbulbringer" } },
        };

        private static readonly Dictionary<string, string[]> s_bossPrefabs = new Dictionary<string, string[]>
        {
            { "eikthyr", new[] { "Eikthyr" } },
            { "elder", new[] { "gd_king" } },
            { "bonemass", new[] { "Bonemass" } },
            { "moder", new[] { "Dragon" } },
            { "yagluth", new[] { "GoblinKing" } },
            { "queen", new[] { "SeekerQueen" } },
            { "fader", new[] { "Fader" } },
            { "kall", new[] { "Kall", "Fimbul" } },
        };

        /// <summary>The boss key of a trophy item in the inventory, or null.</summary>
        public static string BossOfTrophy(string prefab)
        {
            if (string.IsNullOrEmpty(prefab) || !prefab.StartsWith("Trophy"))
            {
                return null;
            }

            foreach (var kv in Trophies)
            {
                foreach (var t in kv.Value)
                {
                    if (prefab == t)
                    {
                        return kv.Key;
                    }
                }
            }

            return prefab.Contains("Kall") || prefab.Contains("Fimbul") ? "kall" : null;
        }

        public static string BossOf(Character c)
        {
            if (c == null || !c.IsBoss())
            {
                return null;
            }

            string n = Combat.Prefab(c);
            foreach (var kv in s_bossPrefabs)
            {
                foreach (var p in kv.Value)
                {
                    if (n == p || n.StartsWith(p))
                    {
                        return kv.Key;
                    }
                }
            }

            return null;
        }

        [HarmonyPatch(typeof(Container), nameof(Container.AddDefaultItems))]
        private static class Chests
        {
            private static void Postfix(Container __instance)
            {
                string n = __instance.name;
                bool treasure = n.Contains("TreasureChest") || n.Contains("Treasure") || n.Contains("shipwreck") || n.Contains("dvergr") ||
                                n.Contains("Dvergr");
                var inv = __instance.GetInventory();
                if (!treasure || inv == null)
                {
                    return;
                }

                if (Random.value < 0.3f)
                {
                    inv.AddItem(Items.Card, 1, 1, 0, 0L, "", false);
                }

                if (Random.value < 0.12f)
                {
                    inv.AddItem(Items.Memory, 1, 1, 0, 0L, "", false);
                }

                if (Random.value < 0.06f)
                {
                    inv.AddItem(Items.Scroll, 1, 1, 0, 0L, "", false);
                }
            }
        }

        [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
        private static class Bosses
        {
            private static void Postfix(CharacterDrop __instance, List<KeyValuePair<GameObject, int>> __result)
            {
                var c = __instance.GetComponent<Character>();
                string boss = BossOf(c);
                if (boss == null || __result == null || ObjectDB.instance == null)
                {
                    return;
                }

                var memory = ObjectDB.instance.GetItemPrefab(Items.Memory + "_" + boss);
                if (memory != null)
                {
                    __result.Add(new KeyValuePair<GameObject, int>(memory, 1));
                }

                var card = ObjectDB.instance.GetItemPrefab(Items.Card);
                if (card != null)
                {
                    __result.Add(new KeyValuePair<GameObject, int>(card, 1));
                }
            }
        }
    }
}
