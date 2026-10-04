using BepInEx.Configuration;

namespace FlyingPets
{
    /// <summary>
    ///     All user settings (BepInEx\config\com.oulassevich.flyingpets.cfg).
    ///     Movement values are read by whoever simulates the pet, which is always the rider's game.
    /// </summary>
    internal static class ModConfig
    {
        private const string SecControls = "1 - Controls";
        private const string SecFlight = "2 - Flight";
        private const string SecGround = "3 - Ground";
        private const string SecPet = "4 - Pet";
        private const string SecStaff = "5 - Staff";
        private const string SecLooks = "6 - Looks";

        public static ConfigEntry<bool> PitchFollowsCamera;
        public static ConfigEntry<float> PitchDeadZone;
        public static ConfigEntry<float> CameraDistance;
        public static ConfigEntry<bool> ShowControlsHint;

        public static ConfigEntry<float> FlySpeed;
        public static ConfigEntry<float> SprintSpeed;
        public static ConfigEntry<float> ClimbSpeed;
        public static ConfigEntry<float> DiveSpeed;
        public static ConfigEntry<float> MaxAltitude;

        public static ConfigEntry<float> WalkSpeed;
        public static ConfigEntry<float> RunSpeed;

        public static ConfigEntry<float> PetScale;
        public static ConfigEntry<float> FollowDistance;
        public static ConfigEntry<float> TeleportDistance;
        public static ConfigEntry<bool> OthersCanRide;
        public static ConfigEntry<float> RiderSeatHeight;

        public static ConfigEntry<string> StaffRecipe;
        public static ConfigEntry<string> StaffStation;
        public static ConfigEntry<int> StaffStationLevel;
        public static ConfigEntry<string> StaffBasePrefab;

        public static ConfigEntry<bool> EyeLight;

        public static void Init(ConfigFile cfg)
        {
            PitchFollowsCamera = cfg.Bind(SecControls, "PitchFollowsCamera", true,
                "In flight, looking up or down while moving forward makes the pet climb or dive. " +
                "Turn off to change height only with Jump / Crouch.");
            PitchDeadZone = cfg.Bind(SecControls, "PitchDeadZone", 12f,
                "Camera tilt (degrees) that is ignored, so the usual slightly-downward view keeps the flight level.");
            CameraDistance = cfg.Bind(SecControls, "CameraDistance", 11f,
                "Maximum camera distance while riding (the game default is 6).");
            ShowControlsHint = cfg.Bind(SecControls, "ShowControlsHint", true,
                "Show a short controls reminder every time you mount.");

            FlySpeed = cfg.Bind(SecFlight, "FlySpeed", 14f, "Cruise speed in flight, m/s.");
            SprintSpeed = cfg.Bind(SecFlight, "SprintSpeed", 22f, "Flight speed with Run held, m/s.");
            ClimbSpeed = cfg.Bind(SecFlight, "ClimbSpeed", 7f, "Climb rate with Jump held, m/s.");
            DiveSpeed = cfg.Bind(SecFlight, "DiveSpeed", 10f, "Descent rate with Crouch held, m/s.");
            MaxAltitude = cfg.Bind(SecFlight, "MaxAltitude", 180f, "Highest flight above the ground or the sea, m.");

            WalkSpeed = cfg.Bind(SecGround, "WalkSpeed", 3.5f, "Walking speed on the ground, m/s.");
            RunSpeed = cfg.Bind(SecGround, "RunSpeed", 8f, "Running speed on the ground (Run held), m/s.");

            PetScale = cfg.Bind(SecPet, "PetScale", 1f,
                new ConfigDescription("Size of the pets (1 = a heavy war horse). Applies to newly summoned pets.",
                    new AcceptableValueRange<float>(0.6f, 1.6f)));
            FollowDistance = cfg.Bind(SecPet, "FollowDistance", 5f, "How close a riderless pet stays to you, m.");
            TeleportDistance = cfg.Bind(SecPet, "TeleportDistance", 120f,
                "A riderless pet left further behind than this is moved next to you, m.");
            OthersCanRide = cfg.Bind(SecPet, "OthersCanRide", false,
                "Allow other players to ride your pet (only matters on your own game).");
            RiderSeatHeight = cfg.Bind(SecPet, "RiderSeatHeight", 0f,
                "Raise (+) or lower (-) the rider in the saddle, m. Use it if the character floats above or sinks into the saddle.");

            StaffRecipe = cfg.Bind(SecStaff, "Recipe", "Wood:10,Feathers:6,LeatherScraps:4,Resin:4",
                "Staff ingredients: PrefabName:Amount separated by commas. Needs a game restart.");
            StaffStation = cfg.Bind(SecStaff, "CraftingStation", "piece_workbench",
                "Where the staff is crafted (piece_workbench, forge, piece_magetable, ... or empty for hand crafting).");
            StaffStationLevel = cfg.Bind(SecStaff, "StationLevel", 1, "Required crafting station level.");
            StaffBasePrefab = cfg.Bind(SecStaff, "BaseModel", "StaffIceShards",
                "Vanilla item whose 3D model the staff borrows until it gets its own model.");

            EyeLight = cfg.Bind(SecLooks, "EyeLight", true, "A faint light from the glowing eyes.");
        }
    }
}
