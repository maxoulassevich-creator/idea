using System;
using System.Collections.Generic;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace Wizardry
{
    /// <summary>Books, scrolls, cards, memories, cores, the Deathly Hallows and the potions.</summary>
    internal static class Items
    {
        public const string Book = "WZ_Book_";
        public const string Weasley = "WZ_Book_Weasley";
        public const string Rituals = "WZ_Book_Rituals";
        public const string Grimoire = "WZ_Book_Grimoire";
        public const string Practice = "WZ_Book_Practice";
        public const string Scroll = "WZ_Scroll_Obliviate";
        public const string Cup = "WZ_Cup_Pensieve";
        public const string Card = "WZ_Card";
        public const string Memory = "WZ_Memory";
        public const string Stone = "WZ_ResurrectionStone";
        public const string Cloak = "WZ_InvisibilityCloak";

        public static readonly string[] Bosses = { "eikthyr", "elder", "bonemass", "moder", "yagluth", "queen", "fader", "kall" };

        public static readonly Dictionary<string, string> BossMemoryMods = new Dictionary<string, string>
        {
            { "eikthyr", "dmg_lightning:20;move_speed:5" },
            { "elder", "summon_hp:25;root_dur:30;dmg_poison:15" },
            { "bonemass", "res_poison:30;dmg_dark:15;undead_summon_dmg:20" },
            { "moder", "dmg_frost:25;ctrl_dur:15" },
            { "yagluth", "dmg_fire:25;flag:burn_long" },
            { "queen", "summon_limit:1;dmg_poison:20;birds:3" },
            { "fader", "dmg:15;res_elem:20" },
            { "kall", "dmg_frost:20;armor:25;flag:shatter" },
        };

        public static readonly string[] CardNames =
        {
            "Мерлин", "Моргана", "Агриппа", "Птолемей", "Цирцея", "Парацельс", "Клиодна", "Николас Фламель", "Хенгист Вудкрофтский",
            "Боумен Райт", "Альбус Дамблдор", "Ровена Когтевран"
        };

        public static readonly string[] CardNamesEn =
        {
            "Merlin", "Morgana", "Agrippa", "Ptolemy", "Circe", "Paracelsus", "Cliodna", "Nicolas Flamel", "Hengist of Woodcroft",
            "Bowman Wright", "Albus Dumbledore", "Rowena Ravenclaw"
        };

        private static readonly string[] SmallBases = { "Ruby", "AmberPearl", "Amber", "Coins", "Resin" };

        public static void Register()
        {
            // books: one per year, plus the kits
            for (int y = 1; y <= 7; y++)
            {
                Simple(Book + y, "$wz_book", "$wz_book_desc", Icons.Shape.Book, BookColor(y), Mathf.Clamp(y, 1, 7),
                    BookRecipe(y), 1f, " " + y);
            }

            Simple(Weasley, "$wz_book_weasley", "$wz_book_desc", Icons.Shape.Book, new Color(1f, 0.55f, 0.15f), 2,
                Req(R("Feathers", 3), R("Coal", 4), R("Raspberry", 5)), 1f);
            Simple(Rituals, "$wz_book_rituals", "$wz_book_desc", Icons.Shape.Book, new Color(0.65f, 0.7f, 1f), 3,
                Req(R("FineWood", 4), R("Silver", 3), R("Crystal", 2)), 1f);
            Simple(Grimoire, "$wz_book_grimoire", "$wz_book_desc", Icons.Shape.Book, new Color(0.2f, 0.15f, 0.2f), 5,
                Req(R("BlackMetal", 5), R("Eitr", 5), R("BlackCore", 1)), 1.5f);
            Simple(Practice, "$wz_book_practice", "$wz_book_practice_desc", Icons.Shape.Book, new Color(0.8f, 0.75f, 0.6f), 1,
                Req(R("Feathers", 2), R("Resin", 4), R("Coal", 2)), 0.6f);
            Simple(Scroll, "$wz_scroll", "$wz_scroll_desc", Icons.Shape.Scroll, new Color(0.35f, 0.9f, 0.5f), 1,
                Req(R("Feathers", 2), R("Resin", 2), R("Coal", 1)), 0.2f);
            Simple(Cup, "$wz_cup", "$wz_cup_desc", Icons.Shape.Cup, new Color(0.75f, 0.8f, 0.95f), 4,
                Req(R("Silver", 5), R("Eitr", "Bronze", 2), R("BoneFragments", 10)), 1f);
            Simple(Card, "$wz_card", "$wz_card_desc", Icons.Shape.Card, new Color(0.55f, 0.35f, 0.2f), 0, null, 0.05f);
            Simple(Memory, "$wz_memory", "$wz_memory_desc", Icons.Shape.Orb, new Color(0.75f, 0.85f, 1f), 0, null, 0.1f);
            foreach (var boss in Bosses)
            {
                Simple(Memory + "_" + boss, "$wz_memory", "$wz_memory_desc", Icons.Shape.Orb, new Color(1f, 0.85f, 0.45f), 0, null, 0.1f,
                    " (" + boss + ")");
            }

            // the Deathly Hallows
            Simple(Stone, "$wz_stone", "$wz_stone_desc", Icons.Shape.Stone, new Color(0.2f, 0.2f, 0.22f), 7,
                Req(R("BlackCore", "Obsidian", 2), R("FlametalNew", "Flametal", "BlackMetal", 3), R("TrophyBonemass", 1)), 0.5f);
            Cloak_();
            Potions();
        }

        private static Color BookColor(int y)
        {
            var colors = new[]
            {
                new Color(0.55f, 0.35f, 0.2f), new Color(0.6f, 0.2f, 0.2f), new Color(0.2f, 0.45f, 0.25f), new Color(0.2f, 0.3f, 0.6f),
                new Color(0.5f, 0.2f, 0.55f), new Color(0.25f, 0.25f, 0.28f), new Color(0.75f, 0.6f, 0.2f)
            };
            return colors[Mathf.Clamp(y - 1, 0, 6)];
        }

        private static RequirementConfig[] BookRecipe(int y)
        {
            switch (y)
            {
                case 1: return Req(R("Feathers", 2), R("Resin", 3), R("LeatherScraps", 2));
                case 2: return Req(R("Feathers", 3), R("DeerHide", 2), R("GreydwarfEye", 3));
                case 3: return Req(R("FineWood", 3), R("Bronze", 1), R("TrollHide", 1));
                case 4: return Req(R("ElderBark", 4), R("Iron", 2), R("Guck", 3));
                case 5: return Req(R("Silver", 2), R("WolfPelt", 2), R("Crystal", 2));
                case 6: return Req(R("BlackMetal", 2), R("LinenThread", 4), R("Needle", 3));
                default: return Req(R("Eitr", "BlackMetal", 4), R("Carapace", "LoxPelt", 3), R("YggdrasilWood", 4));
            }
        }

        private static string[] R(params object[] x)
        {
            var list = new List<string>();
            foreach (var o in x)
            {
                list.Add(o.ToString());
            }

            return list.ToArray();
        }

        private static RequirementConfig[] Req(params string[][] r)
        {
            return Wands.Requirements(r);
        }

        /// <summary>A small item made from a vanilla trinket, recoloured, with our name and icon.</summary>
        public static CustomItem Simple(string prefab, string token, string desc, Icons.Shape shape, Color color, int station,
            RequirementConfig[] recipe, float weight, string suffix = "")
        {
            string basePrefab = Wands.FindPrefab(SmallBases);
            if (basePrefab == null)
            {
                return null;
            }

            try
            {
                var cfg = new ItemConfig
                {
                    Name = token,
                    Description = desc,
                    Icons = new[] { Icons.Make(prefab, shape, color) },
                };
                if (recipe != null)
                {
                    cfg.CraftingStation = Pieces.CodexName;
                    cfg.MinStationLevel = Mathf.Max(1, station);
                    cfg.Requirements = recipe;
                }
                else
                {
                    cfg.Enabled = true;
                }

                var item = new CustomItem(prefab, basePrefab, cfg);
                var shared = item.ItemDrop.m_itemData.m_shared;
                shared.m_itemType = ItemDrop.ItemData.ItemType.Material;
                shared.m_maxStackSize = token == "$wz_card" || token == "$wz_scroll" ? 20 : (token == "$wz_memory" ? 1 : 10);
                shared.m_weight = weight;
                shared.m_teleportable = true;
                shared.m_value = 0;
                if (!string.IsNullOrEmpty(suffix))
                {
                    item.ItemDrop.m_itemData.m_shared.m_name = token;
                    shared.m_description = desc + suffix;
                }

                Tint(item.ItemPrefab, color);
                ItemManager.Instance.AddItem(item);
                return item;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Item " + prefab + " could not be registered: " + e.Message);
                return null;
            }
        }

        public static void Tint(GameObject prefab, Color color)
        {
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer)
                {
                    continue;
                }

                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null)
                    {
                        continue;
                    }

                    var m = new Material(mats[i]);
                    if (m.HasProperty("_Color"))
                    {
                        m.SetColor("_Color", color);
                    }

                    mats[i] = m;
                }

                r.sharedMaterials = mats;
            }
        }

        private static void Cloak_()
        {
            string basePrefab = Wands.FindPrefab("CapeDeerHide", "CapeTrollHide", "CapeLinen");
            if (basePrefab == null)
            {
                return;
            }

            var cfg = new ItemConfig
            {
                Name = "$wz_cloak",
                Description = "$wz_cloak_desc",
                CraftingStation = Pieces.CodexName,
                MinStationLevel = 7,
                Requirements = Req(R("LinenThread", 20), R("Silver", 5), R("TrophyFader", "TrophySeekerQueen", 1)),
                Icons = new[] { Icons.Make(Cloak, Icons.Shape.Cloak, new Color(0.45f, 0.5f, 0.65f)) },
            };
            var item = new CustomItem(Cloak, basePrefab, cfg);
            var shared = item.ItemDrop.m_itemData.m_shared;
            shared.m_maxQuality = 1;
            shared.m_armor = 2f;
            shared.m_equipStatusEffect = Hallows.CloakEffect();
            Tint(item.ItemPrefab, new Color(0.45f, 0.5f, 0.65f, 1f));
            ItemManager.Instance.AddItem(item);
        }

        // ------------------------------------------------------------------ potions (the Potioneer's recipes)
        public static readonly string[][] PotionDefs =
        {
            new[] { "felix", "dmg:15;crit:20;loot:20", "600", "recipe_felix" },
            new[] { "polyjuice", "stealth:90", "180", "recipe_polyjuice" },
            new[] { "pepperup", "coldimm:1", "600", "recipe_pepperup" },
            new[] { "death", "stealth:100", "20", "recipe_death" },
            new[] { "strength", "dmg:25", "120", "recipe_strength" },
            new[] { "elixir", "elixir:1", "1800", "recipe_elixir" },
        };

        private static void Potions()
        {
            string basePrefab = Wands.FindPrefab("MeadHealthMinor", "MeadStaminaMinor", "MeadTasty");
            if (basePrefab == null)
            {
                return;
            }

            foreach (var p in PotionDefs)
            {
                try
                {
                    var se = StatusDefs.MakeBuff("potion_" + p[0], "$wz_potion_" + p[0], p[1], float.Parse(p[2]));
                    var cfg = new ItemConfig
                    {
                        Name = "$wz_potion_" + p[0],
                        Description = "$wz_potion_desc",
                        CraftingStation = Pieces.CodexName,
                        MinStationLevel = 3,
                        Requirements = PotionRecipe(p[0]),
                        Icons = new[] { Icons.Make("potion_" + p[0], Icons.Shape.Potion, PotionColor(p[0])) },
                    };
                    var item = new CustomItem("WZ_Potion_" + p[0], basePrefab, cfg);
                    var shared = item.ItemDrop.m_itemData.m_shared;
                    shared.m_consumeStatusEffect = se;
                    shared.m_food = 0f;
                    shared.m_foodStamina = 0f;
                    shared.m_foodEitr = 0f;
                    ItemManager.Instance.AddItem(item);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Potion " + p[0] + ": " + e.Message);
                }
            }
        }

        private static Color PotionColor(string k)
        {
            switch (k)
            {
                case "felix": return new Color(1f, 0.85f, 0.2f);
                case "polyjuice": return new Color(0.45f, 0.5f, 0.25f);
                case "pepperup": return new Color(1f, 0.35f, 0.2f);
                case "death": return new Color(0.2f, 0.15f, 0.3f);
                case "strength": return new Color(0.8f, 0.3f, 0.3f);
                default: return new Color(0.9f, 0.2f, 0.25f);
            }
        }

        private static RequirementConfig[] PotionRecipe(string k)
        {
            switch (k)
            {
                case "felix": return Req(R("Honey", 3), R("Dandelion", 3), R("AmberPearl", "Amber", 1));
                case "polyjuice": return Req(R("Guck", 2), R("Bloodbag", 2), R("Mushroom", 3));
                case "pepperup": return Req(R("Thistle", 3), R("FreezeGland", 1), R("Honey", 1));
                case "death": return Req(R("MushroomYellow", "Mushroom", 3), R("Ooze", 2), R("Bloodbag", 1));
                case "strength": return Req(R("Bloodbag", 2), R("Barley", "Carrot", 3), R("Iron", 1));
                default: return Req(R("Ruby", 2), R("Eitr", "Silver", 3), R("RoyalJelly", "Honey", 3));
            }
        }

        // ------------------------------------------------------------------ queries
        public static string PrefabOf(ItemDrop.ItemData it)
        {
            if (it == null)
            {
                return null;
            }

            if (it.m_dropPrefab != null)
            {
                return it.m_dropPrefab.name;
            }

            return null;
        }

        public static int Count(Player p, string prefab)
        {
            int n = 0;
            foreach (var it in p.GetInventory().GetAllItems())
            {
                if (PrefabOf(it) == prefab)
                {
                    n += it.m_stack;
                }
            }

            return n;
        }

        public static bool Take(Player p, string prefab, int amount = 1)
        {
            var inv = p.GetInventory();
            foreach (var it in new List<ItemDrop.ItemData>(inv.GetAllItems()))
            {
                if (PrefabOf(it) == prefab && it.m_stack >= amount)
                {
                    inv.RemoveItem(it, amount);
                    return true;
                }
            }

            return false;
        }

        public static ItemDrop.ItemData Find(Player p, string prefab)
        {
            foreach (var it in p.GetInventory().GetAllItems())
            {
                if (PrefabOf(it) == prefab)
                {
                    return it;
                }
            }

            return null;
        }

        public static bool HasAllHallows(Player p)
        {
            return p != null && Find(p, Wands.Prefix + "elder") != null && Find(p, Stone) != null && Find(p, Cloak) != null;
        }

        /// <summary>A memory's modifiers: fixed for a boss's, random (and then fixed) for others.</summary>
        public static string MemoryMods(ItemDrop.ItemData it)
        {
            string prefab = PrefabOf(it) ?? "";
            if (prefab.StartsWith(Memory + "_"))
            {
                string boss = prefab.Substring(Memory.Length + 1);
                string m;
                return BossMemoryMods.TryGetValue(boss, out m) ? m : "dmg:10";
            }

            if (it.m_customData == null)
            {
                it.m_customData = new Dictionary<string, string>();
            }

            string mods;
            if (!it.m_customData.TryGetValue("wz_mods", out mods))
            {
                mods = RandomMemory();
                it.m_customData["wz_mods"] = mods;
            }

            return mods;
        }

        private static readonly string[][] MemoryPool =
        {
            new[] { "dmg_fire", "8", "16" }, new[] { "dmg_frost", "8", "16" }, new[] { "dmg_lightning", "8", "16" },
            new[] { "dmg_poison", "8", "16" }, new[] { "dmg_spirit", "8", "16" }, new[] { "dmg", "4", "8" }, new[] { "eitr", "4", "10" },
            new[] { "hp", "5", "12" }, new[] { "cdr", "3", "8" }, new[] { "cdr_move", "5", "12" }, new[] { "cast_speed", "3", "7" },
            new[] { "crit", "8", "18" }, new[] { "heal", "5", "12" }, new[] { "summon_dmg", "6", "14" }, new[] { "armor", "4", "10" },
            new[] { "ctrl_dur", "5", "12" }, new[] { "area", "5", "12" }, new[] { "carry", "15", "40" }, new[] { "eitr_regen", "5", "12" },
        };

        private static string RandomMemory()
        {
            int n = UnityEngine.Random.value < 0.35f ? 3 : 2;
            var used = new HashSet<int>();
            var parts = new List<string>();
            while (parts.Count < n)
            {
                int i = UnityEngine.Random.Range(0, MemoryPool.Length);
                if (!used.Add(i))
                {
                    continue;
                }

                int lo = int.Parse(MemoryPool[i][1]), hi = int.Parse(MemoryPool[i][2]);
                parts.Add(MemoryPool[i][0] + ":" + UnityEngine.Random.Range(lo, hi + 1));
            }

            return string.Join(";", parts.ToArray());
        }
    }
}
