using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace ChestSorter
{
    internal static class ModConfig
    {
        private const string SecKeys = "1 - Keys";
        private const string SecScan = "2 - Which chests";
        private const string SecSort = "3 - Sorting";
        private const string SecLabels = "4 - Labels";

        public static ConfigEntry<KeyboardShortcut> PreviewKey;
        public static ConfigEntry<KeyboardShortcut> ApplyKey;
        public static ConfigEntry<KeyboardShortcut> UndoKey;

        public static ConfigEntry<float> Radius;
        public static ConfigEntry<string> IncludePrefabs;
        public static ConfigEntry<string> ExcludePrefabs;
        public static ConfigEntry<bool> SkipPrivateChests;
        public static ConfigEntry<bool> SkipChestsInUse;
        public static ConfigEntry<bool> OnlyBuiltChests;

        public static ConfigEntry<bool> SortInsideChest;
        public static ConfigEntry<bool> MergeStacks;
        public static ConfigEntry<bool> SeparateMagicItems;
        public static ConfigEntry<string> CategoryOverrides;
        public static ConfigEntry<string> DisabledCategories;

        public static ConfigEntry<bool> WriteLabels;
        public static ConfigEntry<bool> ShowFloatingLabels;
        public static ConfigEntry<float> LabelDistance;
        public static ConfigEntry<float> LabelHeight;
        public static ConfigEntry<float> LabelSize;

        private static readonly Dictionary<string, string> Overrides =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> Disabled =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly List<string> Include = new List<string>();
        private static readonly List<string> Exclude = new List<string>();

        public static void Init(ConfigFile cfg)
        {
            PreviewKey = cfg.Bind(SecKeys, "PreviewKey", new KeyboardShortcut(KeyCode.F8),
                new ConfigDescription("Shows what the sorting would do. Nothing is moved yet."));
            ApplyKey = cfg.Bind(SecKeys, "ApplyKey",
                new KeyboardShortcut(KeyCode.F8, KeyCode.LeftShift),
                new ConfigDescription("Carries out the sorting shown by the preview."));
            UndoKey = cfg.Bind(SecKeys, "UndoKey",
                new KeyboardShortcut(KeyCode.F8, KeyCode.LeftControl),
                new ConfigDescription("Puts everything back exactly where it was before the last sorting."));

            Radius = cfg.Bind(SecScan, "Radius", 30f,
                new ConfigDescription("How far around you chests are collected, in meters.",
                    new AcceptableValueRange<float>(5f, 64f)));
            IncludePrefabs = cfg.Bind(SecScan, "AlsoInclude", "",
                new ConfigDescription("Extra container prefabs to sort, comma separated. " +
                                      "Anything with \"chest\" in its name is taken anyway."));
            ExcludePrefabs = cfg.Bind(SecScan, "NeverTouch",
                "piece_enchanter,piece_augmenter,piece_enchantingtable,piece_sacrificialstone," +
                "Karve,VikingShip,Raft,Cart,wagon,piece_chest_treasure,loot_chest,TreasureChest",
                new ConfigDescription("Containers the mod must never touch, comma separated. " +
                                      "Matched as part of the prefab name."));
            SkipPrivateChests = cfg.Bind(SecScan, "SkipPersonalChests", true,
                new ConfigDescription("Leave personal (locked) chests alone."));
            SkipChestsInUse = cfg.Bind(SecScan, "SkipChestsInUse", true,
                new ConfigDescription("Leave chests that somebody has open alone."));

            OnlyBuiltChests = cfg.Bind(SecScan, "OnlyBuiltChests", true,
                new ConfigDescription("Only sort chests that were actually built. Keeps containers that " +
                                      "other mods keep out of sight (backpacks, station buffers) untouched."));

            SortInsideChest = cfg.Bind(SecSort, "SortInsideChest", true,
                new ConfigDescription("Also line the items up neatly inside each chest."));
            MergeStacks = cfg.Bind(SecSort, "MergeStacks", true,
                new ConfigDescription("Merge partial stacks of the same item. Items carrying extra data " +
                                      "(enchanted gear, backpacks, named craft) are never merged."));
            SeparateMagicItems = cfg.Bind(SecSort, "SeparateMagicItems", true,
                new ConfigDescription("Give enchanted gear (Epic Loot) its own chest instead of mixing it " +
                                      "with ordinary weapons and armor."));
            CategoryOverrides = cfg.Bind(SecSort, "Overrides", "",
                new ConfigDescription("Force single items into a category, e.g. \"Coal=METAL,Resin=MISC\". " +
                                      "Categories: FOOD, POTION, WEAPON, ARMOR, MAGICITEM, AMMO, TOOL, METAL, " +
                                      "WOOD, HIDE, MONSTER, SEED, VALUABLE, ENCHANT, TROPHY, MISC."));
            DisabledCategories = cfg.Bind(SecSort, "DisabledCategories", "",
                new ConfigDescription("Categories you do not want separate chests for, comma separated. " +
                                      "Their items go to MISC."));

            WriteLabels = cfg.Bind(SecLabels, "WriteLabels", true,
                new ConfigDescription("Label the chests after sorting."));
            ShowFloatingLabels = cfg.Bind(SecLabels, "FloatingLabels", true,
                new ConfigDescription("Show the label as text hanging over the chest. " +
                                      "Off: the label is only visible when you look at the chest."));
            LabelDistance = cfg.Bind(SecLabels, "LabelDistance", 16f,
                new ConfigDescription("How far away floating labels stay visible, in meters.",
                    new AcceptableValueRange<float>(2f, 64f)));
            LabelHeight = cfg.Bind(SecLabels, "LabelHeight", 0.35f,
                new ConfigDescription("How high above the chest the label hangs, in meters.",
                    new AcceptableValueRange<float>(0f, 3f)));
            LabelSize = cfg.Bind(SecLabels, "LabelSize", 2.2f,
                new ConfigDescription("Label text size.", new AcceptableValueRange<float>(0.5f, 8f)));

            CategoryOverrides.SettingChanged += (s, e) => Parse();
            DisabledCategories.SettingChanged += (s, e) => Parse();
            IncludePrefabs.SettingChanged += (s, e) => Parse();
            ExcludePrefabs.SettingChanged += (s, e) => Parse();
            Parse();
        }

        private static void Parse()
        {
            Overrides.Clear();
            foreach (string part in Split(CategoryOverrides.Value))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0 || eq >= part.Length - 1)
                {
                    continue;
                }

                string item = part.Substring(0, eq).Trim();
                string category = part.Substring(eq + 1).Trim().ToUpperInvariant();
                if (item.Length > 0 && Categories.Exists(category))
                {
                    Overrides[item] = category;
                }
            }

            Disabled.Clear();
            foreach (string part in Split(DisabledCategories.Value))
            {
                string category = part.Trim().ToUpperInvariant();
                if (Categories.Exists(category) && category != Categories.Misc)
                {
                    Disabled.Add(category);
                }
            }

            Include.Clear();
            Include.AddRange(Split(IncludePrefabs.Value));

            Exclude.Clear();
            Exclude.AddRange(Split(ExcludePrefabs.Value));

            ItemClassifier.ResetCache();
        }

        private static IEnumerable<string> Split(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                yield break;
            }

            foreach (string part in value.Split(','))
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0)
                {
                    yield return trimmed;
                }
            }
        }

        public static string GetOverride(string prefabName)
        {
            string category;
            return Overrides.TryGetValue(prefabName, out category) ? category : null;
        }

        public static bool IsCategoryEnabled(string categoryId)
        {
            return !Disabled.Contains(categoryId);
        }

        public static IList<string> IncludeList
        {
            get { return Include; }
        }

        public static IList<string> ExcludeList
        {
            get { return Exclude; }
        }
    }
}
