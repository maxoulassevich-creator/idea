using System;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace Wizardry
{
    [BepInPlugin(Guid, "Wizardry", Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.oulassevich.wizardry";
        public const string Version = "1.0.1";

        public static ManualLogSource Log;
        public static Skills.SkillType SkillType;
        private Harmony m_harmony;
        private bool m_registered;

        private void Awake()
        {
            Log = Logger;
            ModConfig.Init(Config);
            SpellDb.Init();
            TreeDb.Init();
            StatusDefs.Register();
            Texts.Register();
            SkillType = SkillManager.Instance.AddSkill(new SkillConfig
            {
                Identifier = Guid + ".skill",
                Name = "$wz_skill",
                Description = "$wz_skill_desc",
                IncreaseStep = 1f,
                Icon = Icons.Make("skill", Icons.Shape.Wand, new Color(0.55f, 0.85f, 1f)),
            });
            PrefabManager.OnVanillaPrefabsAvailable += RegisterContent;
            m_harmony = new Harmony(Guid);
            int failed = 0;
            foreach (var t in AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly()))
            {
                if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0)
                {
                    continue;
                }

                try
                {
                    m_harmony.CreateClassProcessor(t).Patch();
                }
                catch (Exception e)
                {
                    failed++;
                    Log.LogError("Patch " + t.Name + " failed (that feature is off): " + e.Message);
                }
            }

            Log.LogInfo("Wizardry " + Version + ": " + SpellDb.All.Length + " spells, " + TreeDb.Nodes.Length + " tree nodes" +
                        (failed > 0 ? ", " + failed + " patches failed" : ""));
        }

        private void RegisterContent()
        {
            if (m_registered)
            {
                return;
            }

            m_registered = true;
            Step("wands", Wands.Register);
            Step("items", Items.Register);
            Step("codex", Pieces.Register);
        }

        private static void Step(string what, Action a)
        {
            try
            {
                a();
            }
            catch (Exception e)
            {
                Log.LogError("Could not register " + what + ": " + e);
            }
        }

        private void Update()
        {
            try
            {
                Wizard.Update();
            }
            catch (Exception e)
            {
                Report("Update", e);
            }
        }

        private void OnGUI()
        {
            try
            {
                SpellHud.Draw();
                Grimoire.Draw();
            }
            catch (Exception e)
            {
                Report("GUI", e);
            }
        }

        private static readonly System.Collections.Generic.Dictionary<string, int> s_errors = new System.Collections.Generic.Dictionary<string, int>();

        /// <summary>
        /// Logs an error in full the first time it happens. The per-frame loops would otherwise repeat it every frame,
        /// so repeats are only counted and mentioned again every 1000th time.
        /// </summary>
        public static void Report(string where, Exception e)
        {
            string key = where + "|" + e.GetType().Name + "|" + e.Message;
            s_errors.TryGetValue(key, out int n);
            s_errors[key] = ++n;
            if (n == 1)
            {
                Log.LogError(where + " failed: " + e);
            }
            else if (n % 1000 == 0)
            {
                Log.LogError(where + " failed again (" + n + " times): " + e.GetType().Name + ": " + e.Message);
            }
        }

        private void OnDestroy()
        {
            Wizard.Save();
            if (m_harmony != null)
            {
                m_harmony.UnpatchSelf();
            }
        }
    }
}
