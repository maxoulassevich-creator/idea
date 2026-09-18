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
    /// <summary>How the post lays out seeds on free ground.</summary>
    internal enum PlantPattern
    {
        /// <summary>Offset rows - fits about 15% more plants into the same field.</summary>
        Hex,

        Square
    }

    internal static class ModConfig
    {
        private const string SecGeneral = "1 - General";
        private const string SecRange = "2 - Range";
        private const string SecFilter = "3 - What to farm";
        private const string SecContainer = "4 - Container";
        private const string SecUi = "5 - Interface";
        private const string SecChests = "6 - Chests";
        private const string SecProtect = "7 - Protection";
        private const string SecMythic = "8 - Mythic fruit";

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
        public static ConfigEntry<float> SeedShare;
        public static ConfigEntry<bool> SeedsToSeedRows;
        public static ConfigEntry<string> ExtraIncludeList;
        public static ConfigEntry<string> ExcludeList;

        public static ConfigEntry<int> SeedRows;
        public static ConfigEntry<int> OutputRows;

        public static ConfigEntry<bool> SplitView;
        public static ConfigEntry<bool> ShowHoverStatus;
        public static ConfigEntry<bool> LivingRaven;

        public static ConfigEntry<PlantPattern> Pattern;

        public static ConfigEntry<bool> UseChests;
        public static ConfigEntry<float> ChestRadius;
        public static ConfigEntry<bool> PullSeedsFromChests;
        public static ConfigEntry<bool> PushHarvestToChests;
        public static ConfigEntry<int> PushThresholdPercent;
        public static ConfigEntry<bool> PullOnlySeedItems;
        public static ConfigEntry<int> MaxTransferPerTick;
        public static ConfigEntry<int> SeedStockTarget;

        public static ConfigEntry<bool> Scarecrow;
        public static ConfigEntry<float> ScarecrowRadius;

        public static ConfigEntry<bool> MythicEnabled;
        public static ConfigEntry<float> MythicChance;
        public static ConfigEntry<float> MythicBuffSeconds;
        public static ConfigEntry<float> MythicFood;
        public static ConfigEntry<float> MythicStamina;
        public static ConfigEntry<float> MythicEitr;

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
            Pattern = cfg.Bind(SecRange, "PlantPattern", PlantPattern.Hex,
                Describe("How free ground is filled. Hex uses offset rows and fits about 15% more " +
                         "plants into the same field at the same spacing."));
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
            SeedShare = cfg.Bind(SecFilter, "SeedShare", 0.5f,
                Describe("Share of the harvested vegetables and berries that goes back into the seed rows, " +
                         "so the post can plant them and grow seeds. 0 - everything goes to the harvest rows, " +
                         "1 - everything goes to the seed rows.", new AcceptableValueRange<float>(0f, 1f)));
            SeedsToSeedRows = cfg.Bind(SecFilter, "SeedsToSeedRows", true,
                Describe("Harvested seeds always go to the seed rows, never to the harvest rows."));
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

            SplitView = cfg.Bind(SecUi, "SplitView", true,
                new ConfigDescription("Show the container as two separate blocks: seeds on top, harvest below."));
            ShowHoverStatus = cfg.Bind(SecUi, "ShowHoverStatus", true,
                new ConfigDescription("Show radius and last cycle result when looking at the post."));
            LivingRaven = cfg.Bind(SecUi, "LivingRaven", true,
                new ConfigDescription("A live raven perches on the post now and then, and the carved eyes glow at night."));

            UseChests = cfg.Bind(SecChests, "UseChests", true,
                Describe("Let the post work with chests around it: take seeds out and put the harvest in."));
            ChestRadius = cfg.Bind(SecChests, "ChestRadius", 8f,
                Describe("How far the post looks for chests, in meters.", new AcceptableValueRange<float>(1f, 32f)));
            PullSeedsFromChests = cfg.Bind(SecChests, "PullSeeds", true,
                Describe("Refill the seed rows from nearby chests."));
            PushHarvestToChests = cfg.Bind(SecChests, "PushHarvest", true,
                Describe("Move the harvest into nearby chests so the post never clogs up."));
            PushThresholdPercent = cfg.Bind(SecChests, "PushThreshold", 50,
                Describe("Start moving the harvest out once this share of the harvest slots is taken (percent). " +
                         "0 means move everything out right away.", new AcceptableValueRange<int>(0, 100)));
            PullOnlySeedItems = cfg.Bind(SecChests, "PullOnlySeeds", false,
                Describe("Only take actual seeds out of chests. Off: the post also refills crops it is " +
                         "already using as seed (the ones lying in the seed rows), and never touches anything else."));
            SeedStockTarget = cfg.Bind(SecChests, "SeedStock", 50,
                Describe("How many of each seed the post keeps in the seed rows when refilling from chests. " +
                         "Keeps it from dragging the whole storage into the field.",
                    new AcceptableValueRange<int>(1, 1000)));
            MaxTransferPerTick = cfg.Bind(SecChests, "MaxTransferPerCycle", 40,
                Describe("Maximum items moved between the post and chests per cycle.",
                    new AcceptableValueRange<int>(1, 500)));

            Scarecrow = cfg.Bind(SecProtect, "Scarecrow", true,
                Describe("The raven guards the field: creatures can no longer damage crops inside the radius. " +
                         "Players still can, so you can clear a field by hand."));
            ScarecrowRadius = cfg.Bind(SecProtect, "ScarecrowRadius", 10f,
                Describe("Radius of the protection, in meters.", new AcceptableValueRange<float>(1f, 32f)));

            MythicEnabled = cfg.Bind(SecMythic, "Enabled", true,
                Describe("Now and then the post turns up a mythic fruit while harvesting."));
            MythicChance = cfg.Bind(SecMythic, "Chance", 5f,
                Describe("Chance per harvested plant, in percent. On a large field 5% is a fruit every few cycles - " +
                         "lower it to 1 if that feels too generous.", new AcceptableValueRange<float>(0f, 100f)));
            MythicBuffSeconds = cfg.Bind(SecMythic, "BuffSeconds", 1200f,
                Describe("How long the fruit lasts, in seconds. Restart the game after changing.",
                    new AcceptableValueRange<float>(60f, 3600f)));
            MythicFood = cfg.Bind(SecMythic, "Health", 130f,
                Describe("Health the fruit gives. Restart the game after changing.",
                    new AcceptableValueRange<float>(0f, 500f)));
            MythicStamina = cfg.Bind(SecMythic, "Stamina", 130f,
                Describe("Stamina the fruit gives. Restart the game after changing.",
                    new AcceptableValueRange<float>(0f, 500f)));
            MythicEitr = cfg.Bind(SecMythic, "Eitr", 70f,
                Describe("Eitr the fruit gives. Restart the game after changing.",
                    new AcceptableValueRange<float>(0f, 500f)));

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
