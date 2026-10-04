using System;
using System.Collections.Generic;

namespace FlyingPets
{
    /// <summary>
    ///     Ability ids a pet can list in its json ("abilities": [...]). Passives work by themselves,
    ///     the one active ability of a pet is used with the secondary attack (mouse wheel) in the saddle.
    /// </summary>
    internal static class PetAbility
    {
        // pegasus
        public const string DraftHorse = "draft_horse";         // more carry weight in the saddle
        public const string UnderWing = "under_wing";           // no wet / cold / freezing in the saddle
        public const string ValhallaLight = "valhalla_light";   // the undead keep away unless provoked
        public const string ValkyrieCatch = "valkyrie_catch";   // catches its falling owner
        public const string ThunderHoof = "thunder_hoof";       // active: dive and strike the ground

        // raven
        public const string Huginn = "huginn";                  // explores the map in a wider circle
        public const string Muninn = "muninn";                  // pins dungeons, traders, runestones, altars
        public const string RavenFeast = "raven_feast";         // more trophies near the raven
        public const string PreyShadow = "prey_shadow";         // animals freeze under its shadow
        public const string TalonGrab = "talon_grab";           // active: carry a creature

        /// <summary>Abilities of the built-in pets, for a pet folder from an older version without the list.</summary>
        public static string[] Defaults(string key)
        {
            switch (key)
            {
                case "pegasus":
                    return new[] { DraftHorse, UnderWing, ValhallaLight, ValkyrieCatch, ThunderHoof };
                case "raven":
                    return new[] { Huginn, Muninn, RavenFeast, PreyShadow, TalonGrab };
                default:
                    return new string[0];
            }
        }

        public static HashSet<string> Read(string key, string[] listed)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in listed ?? Defaults(key))
            {
                if (!string.IsNullOrEmpty(id))
                {
                    set.Add(id.Trim());
                }
            }

            return set;
        }

        /// <summary>The active ability of a pet (used with the secondary attack), or null.</summary>
        public static string ActiveOf(HashSet<string> set)
        {
            if (set.Contains(ThunderHoof))
            {
                return ThunderHoof;
            }

            return set.Contains(TalonGrab) ? TalonGrab : null;
        }
    }
}
