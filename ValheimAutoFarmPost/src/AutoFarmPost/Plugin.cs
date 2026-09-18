using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Utils;

namespace AutoFarmPost
{
    /// <summary>
    ///     Entry point. Registers the custom piece, config and (optional) patches.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class AutoFarmPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.oulassevich.autofarmpost";
        public const string PluginName = "AutoFarmPost";
        public const string PluginVersion = "1.3.0";

        internal static ManualLogSource Log;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            ModConfig.Init(Config);

            _harmony = new Harmony(PluginGuid);

            // The pickable registry is the only patch the mod really needs. If it cannot be
            // applied (game update renamed something) we silently fall back to scene scanning.
            if (!TryPatch(typeof(CorePatches), "pickable-registry"))
            {
                PickableRegistry.UseFallback = true;
            }

            // Pure cosmetics - never let them break the mod.
            TryPatch(typeof(HoverPatches), "hover-text");

            FarmPostPiece.Register();

            Log.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        private void LateUpdate()
        {
            FarmPostUi.LateUpdate();
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

        private bool TryPatch(Type type, string label)
        {
            try
            {
                _harmony.PatchAll(type);
                return true;
            }
            catch (Exception e)
            {
                Log.LogWarning("Optional patch '" + label + "' could not be applied: " + e.Message);
                return false;
            }
        }
    }
}
