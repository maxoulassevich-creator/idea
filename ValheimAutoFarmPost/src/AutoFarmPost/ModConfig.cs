using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using Jotunn.Configs;

namespace AutoFarmPost
{
    /// <summary>
    ///     All user settings. Gameplay values are admin-only so a dedicated server can push
    ///     them to every client (handled by Jotunn).
    /// </summary>
    internal static class ModConfig
    {
        private const string SecGeneral = "1 - General";
        private const string SecRange = "2 - Range";
        private const string SecFilter = "3 - What to farm";
        private const string SecContainer = "4 - Container";
        private const string SecUi = "5 - Interface";

        public static ConfigEntry<float> TickInterval;
        public static ConfigEntry<int> MaxHarvestPerTick;
        public static ConfigEntry<int> MaxPlantPerTick;

        public static ConfigEntry<float> HarvestRadius;
        public static ConfigEntry<float> PlantRadius;
        public static ConfigEntry<float> PlantSpacing;

        public static ConfigEntry<bool> HarvestCrops;
        public static ConfigEntry<bool> HarvestBerries;
        public static ConfigEntry<bool> ReplantEnabled;
        public static ConfigEntry<bool> PlantOnEmptyGround;
        public static ConfigEntry<bool> RequireCultivated;
        public static ConfigEntry<string> ExtraIncludeList;
        public static ConfigEntry<string> ExcludeList;

        public static ConfigEntry<int> SeedRows;
        public static ConfigEntry<int> OutputRows;

        public static ConfigEntry<bool> ShowDivider;
        public static ConfigEntry<bool> ShowHoverStatus;

        /// <summary>Seconds to wait before scanning empty ground again after a fruitless scan.</summary>
        public const float EmptyScanCooldown = 30f;

        public static readonly HashSet<string> IncludeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> ExcludeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void Init(ConfigFile cfg)
        {
            TickInterval = cfg.Bind(SecGeneral, "TickInterval", 5f,
                Describe("How often (seconds) a post checks its field.", new AcceptableValueRange<float>(1f, 60f)));
            MaxHarvestPerTick = cfg.Bind(SecGeneral, "MaxHarvestPerTick", 10,
                Describe("Maximum plants harvested per check. Keeps the game smooth on huge fields.",
                    new AcceptableValueRange<int>(1, 100)));
            MaxPlantPerTick = cfg.Bind(SecGeneral, "MaxPlantPerTick", 5,
                Describe("Maximum seeds planted per check.", new AcceptableValueRange<int>(1, 100)));

            HarvestRadius = cfg.Bind(SecRange, "HarvestRadius", 8f,
                Describe("Harvest radius around the post, in meters.", new AcceptableValueRange<float>(1f, 32f)));
            PlantRadius = cfg.Bind(SecRange, "PlantRadius", 8f,
                Describe("Planting radius around the post, in meters.", new AcceptableValueRange<float>(1f, 32f)));
            PlantSpacing = cfg.Bind(SecRange, "PlantSpacing", 1f,
                Describe("Distance between seeds when the post plants on empty cultivated ground.",
                    new AcceptableValueRange<float>(0.5f, 4f)));

            HarvestCrops = cfg.Bind(SecFilter, "HarvestCrops", true,
                Describe("Harvest everything that can be planted with a cultivator (carrots, turnips, onions, barley, flax, seeds...)."));
            HarvestBerries = cfg.Bind(SecFilter, "HarvestBerries", true,
                Describe("Harvest berry bushes (raspberries, blueberries, cloudberries...). They regrow by themselves and are never replanted."));
            ReplantEnabled = cfg.Bind(SecFilter, "Replant", true,
                Describe("Plant a new seed on the exact spot a crop was harvested from."));
            PlantOnEmptyGround = cfg.Bind(SecFilter, "PlantOnEmptyGround", true,
                Describe("Also fill free cultivated ground inside the planting radius with seeds from the top rows."));
            RequireCultivated = cfg.Bind(SecFilter, "RequireCultivatedGround", true,
                Describe("Only plant on ground worked with a cultivator. Turning this off lets plants that do not need it grow anywhere."));
            ExtraIncludeList = cfg.Bind(SecFilter, "ExtraPrefabs", "",
                Describe("Extra pickable prefabs to harvest, comma separated. Example: Pickable_Mushroom,Pickable_Thistle"));
            ExcludeList = cfg.Bind(SecFilter, "IgnoredPrefabs", "",
                Describe("Pickable prefabs the post must never touch, comma separated. Example: Pickable_Flax"));

            SeedRows = cfg.Bind(SecContainer, "SeedRows", 2,
                Describe("Rows of the seed area (8 slots per row). Restart the game after changing.",
                    new AcceptableValueRange<int>(1, 4)));
            OutputRows = cfg.Bind(SecContainer, "OutputRows", 2,
                Describe("Rows of the harvest area (8 slots per row). Restart the game after changing.",
                    new AcceptableValueRange<int>(1, 4)));

            ShowDivider = cfg.Bind(SecUi, "ShowDivider", true,
                new ConfigDescription("Draw a line between the seed rows and the harvest rows."));
            ShowHoverStatus = cfg.Bind(SecUi, "ShowHoverStatus", true,
                new ConfigDescription("Show radius and last cycle result when looking at the post."));

            ExtraIncludeList.SettingChanged += (s, e) => ParseLists();
            ExcludeList.SettingChanged += (s, e) => ParseLists();
            ParseLists();
        }

        private static void ParseLists()
        {
            Fill(IncludeSet, ExtraIncludeList.Value);
            Fill(ExcludeSet, ExcludeList.Value);
        }

        private static void Fill(HashSet<string> set, string value)
        {
            set.Clear();
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            foreach (string part in value.Split(','))
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0)
                {
                    set.Add(trimmed);
                }
            }
        }

        private static ConfigDescription Describe(string text, AcceptableValueBase range = null)
        {
            // IsAdminOnly makes Jotunn sync the value from the server to every client.
            return new ConfigDescription(text, range, new ConfigurationManagerAttributes { IsAdminOnly = true });
        }
    }
}
