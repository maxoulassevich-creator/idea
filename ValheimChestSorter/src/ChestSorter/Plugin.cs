using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ChestSorter
{
    /// <summary>
    ///     Sorts a whole storage room: one kind of goods per chest, overflow into the next chest
    ///     of the same kind, and a label over every chest afterwards.
    ///
    ///     Works in three steps on purpose - look at the plan, apply it, undo it if you do not
    ///     like it - because a storage room is the last place where a surprise is welcome.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.oulassevich.chestsorter";
        public const string PluginName = "ChestSorter";
        public const string PluginVersion = "1.0.0";

        /// <summary>A plan older than this is rebuilt instead of applied.</summary>
        private const float PlanLifetime = 120f;

        public static ManualLogSource Log;

        private Harmony _harmony;
        private SortPlan _plan;
        private float _planTime;

        private void Awake()
        {
            Log = Logger;
            ModConfig.Init(Config);

            _harmony = new Harmony(PluginGuid);

            if (!TryPatch(typeof(ContainerPatches), "container-registry"))
            {
                ChestRegistry.UseFallback = true;
            }

            TryPatch(typeof(HoverPatches), "hover-text");

            Log.LogInfo(PluginName + " " + PluginVersion + " loaded.");
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

        private void Update()
        {
            if (Player.m_localPlayer == null || Util.IsTyping())
            {
                return;
            }

            // the most specific shortcut first, otherwise a plain key would swallow it
            if (ModConfig.UndoKey.Value.IsDown())
            {
                Report.Result(SortExecutor.Undo(), true);
                _plan = null;
                return;
            }

            if (ModConfig.ApplyKey.Value.IsDown())
            {
                Apply();
                return;
            }

            if (ModConfig.PreviewKey.Value.IsDown())
            {
                BuildPlan(true);
            }
        }

        private SortPlan BuildPlan(bool report)
        {
            Player player = Player.m_localPlayer;
            List<string> warnings = new List<string>();

            EpicLoot.ResetCache();

            List<ChestInfo> chests = ChestRegistry.Scan(player.transform.position, ModConfig.Radius.Value, warnings);
            SortPlan plan = SortPlanner.Build(chests, player.transform.position, warnings);

            _plan = plan.Valid ? plan : null;
            _planTime = Time.time;

            if (report)
            {
                Report.Plan(plan);
            }

            return plan;
        }

        private void Apply()
        {
            bool ru = Util.IsRussian;

            if (_plan == null || Time.time - _planTime > PlanLifetime)
            {
                SortPlan plan = BuildPlan(true);
                if (plan.Valid)
                {
                    Report.Line(ru
                        ? "<color=#FFD98A>План обновлён. Нажмите " + ModConfig.ApplyKey.Value +
                          " ещё раз, чтобы применить.</color>"
                        : "<color=#FFD98A>Plan refreshed. Press " + ModConfig.ApplyKey.Value +
                          " again to apply.</color>");
                }

                return;
            }

            SortResult result = SortExecutor.Apply(_plan);
            Report.Result(result, false);
            _plan = null;
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
                Log.LogWarning("Patch '" + label + "' could not be applied: " + e.Message);
                return false;
            }
        }
    }
}
