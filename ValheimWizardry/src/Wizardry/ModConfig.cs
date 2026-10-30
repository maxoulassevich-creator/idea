using BepInEx.Configuration;
using Jotunn.Configs;
using UnityEngine;

namespace Wizardry
{
    /// <summary>
    ///     Settings in BepInEx/config/com.oulassevich.wizardry.cfg. Balance and world rules are admin-only: the
    ///     server's values are sent to every client (Jotunn).
    /// </summary>
    internal static class ModConfig
    {
        public static ConfigEntry<float> DamageMultiplier, CostMultiplier, CooldownMultiplier, PointsMultiplier, RankXpMultiplier,
            SkillGainMultiplier, PvpDamage, PvpControl, DarknessGain, EffectsQuality, WandEitrMultiplier, EitrRegen;

        public static ConfigEntry<bool> UnforgivablesPvp, TerrainSpells, WeatherSpells, RaidBlock, PortkeyMetal,
            ChestExtension, Blood, Animations, ScreenEffects, ShowIncantation, PossessionSpawns, FriendlyFire;

        public static ConfigEntry<KeyCode> SelectModifier, GrandKey, GrimoireKey, NextSpellKey, PrevSpellKey;

        public static ConfigEntry<string> Language;

        public static void Init(ConfigFile cfg)
        {
            var admin = new ConfigurationManagerAttributes { IsAdminOnly = true };

            DamageMultiplier = cfg.Bind("Balance", "DamageMultiplier", 1f,
                new ConfigDescription("All spell damage is multiplied by this.", new AcceptableValueRange<float>(0.1f, 5f), admin));
            CostMultiplier = cfg.Bind("Balance", "CostMultiplier", 1f,
                new ConfigDescription("Eitr cost of every spell.", new AcceptableValueRange<float>(0f, 5f), admin));
            CooldownMultiplier = cfg.Bind("Balance", "CooldownMultiplier", 1f,
                new ConfigDescription("Cooldowns of every spell.", new AcceptableValueRange<float>(0.1f, 5f), admin));
            PointsMultiplier = cfg.Bind("Balance", "PointsMultiplier", 1f,
                new ConfigDescription("Tree points from skill, bosses and cards.", new AcceptableValueRange<float>(0.1f, 5f), admin));
            RankXpMultiplier = cfg.Bind("Balance", "RankXpMultiplier", 1f,
                new ConfigDescription("How fast spells rank up.", new AcceptableValueRange<float>(0.1f, 20f), admin));
            SkillGainMultiplier = cfg.Bind("Balance", "SkillGainMultiplier", 1f,
                new ConfigDescription("How fast the Wizardry skill grows.", new AcceptableValueRange<float>(0.1f, 20f), admin));
            PvpDamage = cfg.Bind("Balance", "PvpDamageMultiplier", 0.5f,
                new ConfigDescription("Spell damage against players.", new AcceptableValueRange<float>(0f, 2f), admin));
            PvpControl = cfg.Bind("Balance", "PvpControlMultiplier", 0.5f,
                new ConfigDescription("Length of stuns, roots and other control on players.", new AcceptableValueRange<float>(0f, 2f), admin));
            DarknessGain = cfg.Bind("Balance", "DarknessGainMultiplier", 1f,
                new ConfigDescription("How much Darkness dark spells give.", new AcceptableValueRange<float>(0f, 5f), admin));
            WandEitrMultiplier = cfg.Bind("Balance", "WandEitrMultiplier", 1f,
                new ConfigDescription("Eitr a wand in hand adds.", new AcceptableValueRange<float>(0f, 5f), admin));
            EitrRegen = cfg.Bind("Balance", "WandEitrRegenPerSecond", 1.5f,
                new ConfigDescription("Extra eitr per second with a wand in hand.", new AcceptableValueRange<float>(0f, 20f), admin));

            UnforgivablesPvp = cfg.Bind("World", "UnforgivablesAgainstPlayers", false,
                new ConfigDescription("Avada Kedavra, Crucio and Imperio may hit players.", null, admin));
            FriendlyFire = cfg.Bind("World", "SpellsHitPlayersInPvp", true,
                new ConfigDescription("Hostile spells hit players who have PvP on.", null, admin));
            TerrainSpells = cfg.Bind("World", "TerrainSpells", true,
                new ConfigDescription("Deprimo and Defodio dig into the ground.", null, admin));
            WeatherSpells = cfg.Bind("World", "WeatherSpells", true,
                new ConfigDescription("Spells may change the weather around the caster for everybody there.", null, admin));
            RaidBlock = cfg.Bind("World", "FideliusBlocksRaids", true,
                new ConfigDescription("Players inside a Fidelius area are not chosen for raids.", null, admin));
            PortkeyMetal = cfg.Bind("World", "LongTravelCarriesMetal", false,
                new ConfigDescription("Apparition home, Portus and the Vanishing Cabinet may carry ore and metal (vanilla portals cannot).", null, admin));
            ChestExtension = cfg.Bind("World", "ChestExtension", true,
                new ConfigDescription("Capacious Extremis adds rows to chests. WARNING: items in the extra rows are lost if the mod is removed.", null, admin));
            PossessionSpawns = cfg.Bind("World", "PossessionSpawnsWraiths", true,
                new ConfigDescription("At Darkness 100 wraiths come for the wizard at night.", null, admin));

            Blood = cfg.Bind("Visuals", "Blood", true, "Sectumsempra and the like show blood.");
            Animations = cfg.Bind("Visuals", "CastAnimations", true, "Play vanilla attack/emote animations while casting.");
            ScreenEffects = cfg.Bind("Visuals", "ScreenEffects", true, "Camera shake, vignettes and tints.");
            EffectsQuality = cfg.Bind("Visuals", "EffectsQuality", 1f,
                new ConfigDescription("Particle density of spell effects.", new AcceptableValueRange<float>(0.25f, 1f)));
            ShowIncantation = cfg.Bind("Visuals", "ShowIncantation", true, "Spoken spells show their incantation over the head.");
            Language = cfg.Bind("Visuals", "Language", "auto", "auto, ru or en: the language of the mod's windows.");

            SelectModifier = cfg.Bind("Keys", "SelectSpellModifier", KeyCode.LeftAlt,
                "Hold it and press 1-8 (or turn the wheel) to choose the spell in the bar.");
            NextSpellKey = cfg.Bind("Keys", "NextSpell", KeyCode.None, "Optional key: next spell in the bar.");
            PrevSpellKey = cfg.Bind("Keys", "PreviousSpell", KeyCode.None, "Optional key: previous spell in the bar.");
            GrandKey = cfg.Bind("Keys", "GrandSpell", KeyCode.G, "Cast the grand spell when Focus is full.");
            GrimoireKey = cfg.Bind("Keys", "Grimoire", KeyCode.K, "Open the grimoire (tree, spells, specialisation, bestiary).");
        }
    }
}
