using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Managers;
using Jotunn.Utils;

namespace FlyingPets
{
    /// <summary>
    ///     Entry point. Loads the pets from the pets folder, registers their network prefabs and the
    ///     summoning staff once the vanilla prefabs exist, and installs the riding patches.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class FlyingPetsPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.oulassevich.flyingpets";
        public const string PluginName = "FlyingPets";
        public const string PluginVersion = "1.2.0";

        internal static ManualLogSource Log;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            ModConfig.Init(Config);
            Texts.Register();

            _harmony = new Harmony(PluginGuid);
            TryPatch(typeof(StaffAttackPatch), "staff");
            TryPatch(typeof(RiderControlsPatch), "rider-controls");
            TryPatch(typeof(RiderStopPatch), "rider-dismount");
            TryPatch(typeof(CreatureAiPatch), "abilities: creature AI");
            TryPatch(typeof(HeldPhysicsPatch), "abilities: talon grab");
            TryPatch(typeof(HuginnExplorePatch), "abilities: Huginn");
            TryPatch(typeof(RavenFeastPatch), "abilities: ravens' feast");
            TryPatch(typeof(UnderWingPatch), "abilities: under the wing");

            PrefabManager.OnVanillaPrefabsAvailable += OnVanillaPrefabsAvailable;
            Log.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        private void OnVanillaPrefabsAvailable()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= OnVanillaPrefabsAvailable;
            try
            {
                PetLibrary.LoadAll(Path.GetDirectoryName(Info.Location));
                PetPrefabs.BuildAll();
            }
            catch (Exception e)
            {
                Log.LogError("Pets could not be prepared: " + e);
            }

            try
            {
                StaffItem.Register();
            }
            catch (Exception e)
            {
                Log.LogError("The staff could not be registered: " + e);
            }
        }

        private void Update()
        {
            Passives.FrameUpdate();
        }

        private void OnDestroy()
        {
            try
            {
                if (_harmony != null)
                {
                    _harmony.UnpatchSelf();
                }
            }
            catch (Exception e)
            {
                Log.LogWarning("Unpatch failed: " + e.Message);
            }
        }

        private void TryPatch(Type type, string label)
        {
            try
            {
                _harmony.PatchAll(type);
            }
            catch (Exception e)
            {
                Log.LogError("Patch '" + label + "' could not be applied: " + e.Message);
            }
        }
    }
}
