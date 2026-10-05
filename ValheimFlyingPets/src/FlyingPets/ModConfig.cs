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
        private const string SecPegasus = "7 - Pegasus abilities";
        private const string SecRaven = "8 - Raven abilities";
        private const string SecDragon = "9 - Dragon abilities";

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
        public static ConfigEntry<bool> StaffOwnModel;
        public static ConfigEntry<float> StaffScale;
        public static ConfigEntry<float> StaffGripShift;
        public static ConfigEntry<float> StaffRoll;
        public static ConfigEntry<bool> StaffFlip;

        public static ConfigEntry<bool> EyeLight;

        public static ConfigEntry<float> CarryBonus;
        public static ConfigEntry<bool> UnderWing;
        public static ConfigEntry<float> ValhallaRadius;
        public static ConfigEntry<bool> ValkyrieCatch;
        public static ConfigEntry<float> CatchCooldown;
        public static ConfigEntry<bool> ThunderHoof;
        public static ConfigEntry<float> ThunderDamage;
        public static ConfigEntry<float> ThunderRadius;
        public static ConfigEntry<float> ThunderCooldown;
        public static ConfigEntry<float> SpringHealPerSecond;
        public static ConfigEntry<float> SpringDuration;

        public static ConfigEntry<float> HuginnMultiplier;
        public static ConfigEntry<bool> MuninnPins;
        public static ConfigEntry<float> MuninnRange;
        public static ConfigEntry<float> FeastMultiplier;
        public static ConfigEntry<float> FeastRadius;
        public static ConfigEntry<float> PreyFreezeSeconds;
        public static ConfigEntry<bool> TalonGrab;
        public static ConfigEntry<float> GrabCooldown;
        public static ConfigEntry<float> GrabMaxHold;
        public static ConfigEntry<float> GrabMaxRadius;
        public static ConfigEntry<float> GrabLethalHeight;

        public static ConfigEntry<float> SeaTruceRadius;
        public static ConfigEntry<float> LivingWaterMultiplier;
        public static ConfigEntry<float> LivingWaterRadius;
        public static ConfigEntry<float> FisherInterval;
        public static ConfigEntry<float> FisherHeight;
        public static ConfigEntry<bool> SteamingScales;
        public static ConfigEntry<bool> TidalBreath;
        public static ConfigEntry<float> BreathDamage;
        public static ConfigEntry<float> BreathRange;
        public static ConfigEntry<float> BreathCooldown;

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
                "Vanilla staff the item is made from: how it is held, its sounds, and its look when OwnModel is off. Needs a game restart.");
            StaffOwnModel = cfg.Bind(SecStaff, "OwnModel", true,
                "Use the staff's own model (Magic Staff by petersoon, CC BY 4.0) instead of the vanilla one. Needs a game restart.");
            StaffScale = cfg.Bind(SecStaff, "ModelScale", 1f,
                "Size of the staff's model compared with the vanilla staff it replaces. Needs a game restart.");
            StaffGripShift = cfg.Bind(SecStaff, "ModelGripShift", 0f,
                "Move the hand along the staff (a share of its length, e.g. 0.1 = the hand 10% higher up). Needs a game restart.");
            StaffRoll = cfg.Bind(SecStaff, "ModelRoll", 0f,
                "Turn the staff's model around its length, degrees. Needs a game restart.");
            StaffFlip = cfg.Bind(SecStaff, "ModelUpsideDown", false,
                "Turn the model end over end, if the game holds it upside down. Needs a game restart.");

            EyeLight = cfg.Bind(SecLooks, "EyeLight", true, "A faint light from the glowing eyes.");

            // abilities: every setting applies on the game of the player it concerns (the rider, the summoner,
            // or whoever simulates the creature), so in multiplayer each player's own file counts for them
            CarryBonus = cfg.Bind(SecPegasus, "DraftHorseCarryWeight", 150f,
                "Draft horse: extra carry weight while you sit in the pegasus saddle (0 = off).");
            UnderWing = cfg.Bind(SecPegasus, "UnderWing", true,
                "Under the wing: no Wet, Cold or Freezing while you sit in the pegasus saddle.");
            ValhallaRadius = cfg.Bind(SecPegasus, "ValhallaLightRadius", 10f,
                "Light of Valhalla: the undead keep at least this far from a pegasus unless they were hurt " +
                "in the last 20 seconds, m (0 = off).");
            ValkyrieCatch = cfg.Bind(SecPegasus, "ValkyrieCatch", true,
                "Valkyrie's catch: your summoned pegasus catches you into the saddle when you fall from a height.");
            CatchCooldown = cfg.Bind(SecPegasus, "ValkyrieCatchCooldown", 300f, "Valkyrie's catch: cooldown, s.");
            ThunderHoof = cfg.Bind(SecPegasus, "ThunderHoof", true,
                "Thunder hoof (secondary attack in the saddle, in flight): dive and strike the ground with lightning.");
            ThunderDamage = cfg.Bind(SecPegasus, "ThunderHoofDamage", 60f,
                "Thunder hoof: damage of a strike from low height (lightning and blunt). A dive from 40 m or more triples it.");
            ThunderRadius = cfg.Bind(SecPegasus, "ThunderHoofRadius", 7f, "Thunder hoof: radius of the shockwave, m.");
            ThunderCooldown = cfg.Bind(SecPegasus, "ThunderHoofCooldown", 60f, "Thunder hoof: cooldown, s.");
            SpringHealPerSecond = cfg.Bind(SecPegasus, "HippocreneHealPerSecond", 8f,
                "Hippocrene: health per second for players standing in the spring that appears after a strike (0 = no spring).");
            SpringDuration = cfg.Bind(SecPegasus, "HippocreneDuration", 10f, "Hippocrene: how long the spring stays, s.");

            HuginnMultiplier = cfg.Bind(SecRaven, "HuginnExploreMultiplier", 3f,
                "Huginn: the map is uncovered this many times wider while you fly the raven (1 = off).");
            MuninnPins = cfg.Bind(SecRaven, "MuninnPins", true,
                "Muninn: pins dungeons, traders, runestones and boss altars on your map while you ride the raven " +
                "or it follows you.");
            MuninnRange = cfg.Bind(SecRaven, "MuninnRange", 150f, "Muninn: how far the raven notices places, m.");
            FeastMultiplier = cfg.Bind(SecRaven, "RavenFeastTrophyMultiplier", 2f,
                "Ravens' feast: trophies drop this many times more often from creatures killed near a raven (1 = off).");
            FeastRadius = cfg.Bind(SecRaven, "RavenFeastRadius", 30f, "Ravens' feast: how near the raven, m (horizontally).");
            PreyFreezeSeconds = cfg.Bind(SecRaven, "PreyShadowSeconds", 2.5f,
                "Shadow over the prey: animals under a flying raven freeze for this long, s (0 = off).");
            TalonGrab = cfg.Bind(SecRaven, "TalonGrab", true,
                "Talon grab (secondary attack in the saddle, in flight): seize a creature under the raven, " +
                "press again to let go.");
            GrabCooldown = cfg.Bind(SecRaven, "TalonGrabCooldown", 30f, "Talon grab: cooldown after letting go, s.");
            GrabMaxHold = cfg.Bind(SecRaven, "TalonGrabMaxHold", 30f, "Talon grab: the raven lets go after this long, s.");
            GrabMaxRadius = cfg.Bind(SecRaven, "TalonGrabMaxSize", 0f,
                "Talon grab: biggest creature the raven can lift, as the radius of its body in m " +
                "(0 = as big as a troll). Bosses and players are never grabbed.");
            GrabLethalHeight = cfg.Bind(SecRaven, "TalonGrabLethalHeight", 30f,
                "Talon grab: a creature dropped from this height dies; from 6 m it is unharmed, in between it is hurt, m.");

            SeaTruceRadius = cfg.Bind(SecDragon, "SeaTruceRadius", 40f,
                "Sea truce: sea monsters (serpents) keep at least this far from a water dragon unless they were hurt " +
                "in the last 20 seconds, m (0 = off).");
            LivingWaterMultiplier = cfg.Bind(SecDragon, "LivingWaterRegenMultiplier", 2f,
                "Living water: while you are Wet in the dragon's saddle or near a water dragon, health comes back this many " +
                "times faster (1 = off).");
            LivingWaterRadius = cfg.Bind(SecDragon, "LivingWaterRadius", 30f, "Living water: how near the dragon, m.");
            FisherInterval = cfg.Bind(SecDragon, "FisherSeconds", 22f,
                "Fisher: skimming low and fast over water, the dragon brings up a fish about this often, s (0 = off). " +
                "The spray also makes you Wet.");
            FisherHeight = cfg.Bind(SecDragon, "FisherHeight", 5f, "Fisher: how low over the water the dragon must fly, m.");
            SteamingScales = cfg.Bind(SecDragon, "SteamingScales", true,
                "Steaming scales: you do not catch fire (Burning) while you sit in the dragon's saddle.");
            TidalBreath = cfg.Bind(SecDragon, "TidalBreath", true,
                "Tidal breath (secondary attack in the saddle, on the ground or in flight): a jet of icy water from the " +
                "dragon's jaws that hurts, pushes back and soaks everything in front of it.");
            BreathDamage = cfg.Bind(SecDragon, "TidalBreathDamage", 12f,
                "Tidal breath: damage of one gush (frost and blunt); the jet gushes 8 times over 2 seconds.");
            BreathRange = cfg.Bind(SecDragon, "TidalBreathRange", 16f, "Tidal breath: how far the jet reaches, m.");
            BreathCooldown = cfg.Bind(SecDragon, "TidalBreathCooldown", 40f, "Tidal breath: cooldown, s.");
        }
    }
}
