using System;
using System.Collections.Generic;
using System.Reflection;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace AutoFarmPost
{
    /// <summary>
    ///     Builds the custom piece at runtime by cloning a vanilla wooden pole and bolting a
    ///     chest inventory plus the farming logic onto it. No asset bundle needed.
    /// </summary>
    internal static class FarmPostPiece
    {
        public const string PrefabName = "piece_autofarm_post";
        public const string NameToken = "$piece_autofarm_post";
        public const string DescToken = "$piece_autofarm_post_desc";

        /// <summary>Tried in order, so the mod still works if one of them is ever removed.</summary>
        private static readonly string[] BasePrefabs = { "wood_pole2", "wood_pole", "wood_beam", "piece_chest_wood" };

        public static void Register()
        {
            AddLocalization();
            PrefabManager.OnVanillaPrefabsAvailable += Create;
        }

        private static void Create()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Create;

            try
            {
                GameObject basePrefab = null;
                for (int i = 0; i < BasePrefabs.Length && basePrefab == null; i++)
                {
                    basePrefab = PrefabManager.Instance.GetPrefab(BasePrefabs[i]);
                }

                if (basePrefab == null)
                {
                    AutoFarmPlugin.Log.LogError("No base prefab found - the farm post was not created.");
                    return;
                }

                GameObject prefab = PrefabManager.Instance.CreateClonedPrefab(PrefabName, basePrefab);
                if (prefab == null)
                {
                    AutoFarmPlugin.Log.LogError("Could not clone " + basePrefab.name + ".");
                    return;
                }

                SetupContainer(prefab);
                SetupWear(prefab);

                if (prefab.GetComponent<FarmPost>() == null)
                {
                    prefab.AddComponent<FarmPost>();
                }

                PieceConfig config = new PieceConfig();
                config.Name = NameToken;
                config.Description = DescToken;
                config.PieceTable = "Hammer";
                config.Category = "Misc";
                config.CraftingStation = "piece_workbench";
                config.AllowedInDungeons = false;
                config.Requirements = new[]
                {
                    new RequirementConfig { Item = "Wood", Amount = 10, Recover = true },
                    new RequirementConfig { Item = "Stone", Amount = 5, Recover = true }
                };

                PieceManager.Instance.AddPiece(new CustomPiece(prefab, false, config));
                AutoFarmPlugin.Log.LogInfo("Farm post registered (base: " + basePrefab.name + ").");
            }
            catch (Exception e)
            {
                AutoFarmPlugin.Log.LogError("Could not create the farm post: " + e);
            }
        }

        private static void SetupContainer(GameObject prefab)
        {
            Container container = prefab.GetComponent<Container>();
            if (container == null)
            {
                container = prefab.AddComponent<Container>();

                // Take the sounds and settings of a wooden chest so it behaves like a vanilla one.
                GameObject chest = PrefabManager.Instance.GetPrefab("piece_chest_wood");
                Container source = chest != null ? chest.GetComponent<Container>() : null;
                if (source != null)
                {
                    CopyPlainFields(source, container);
                }
            }

            int seedRows = Mathf.Clamp(ModConfig.SeedRows.Value, 1, 4);
            int outputRows = Mathf.Clamp(ModConfig.OutputRows.Value, 1, 4);

            container.m_name = NameToken;
            container.m_width = 8;
            container.m_height = seedRows + outputRows;
            container.m_autoDestroyEmpty = false;

            if (container.m_openEffects == null)
            {
                container.m_openEffects = new EffectList();
            }

            if (container.m_closeEffects == null)
            {
                container.m_closeEffects = new EffectList();
            }
        }

        private static void SetupWear(GameObject prefab)
        {
            WearNTear wear = prefab.GetComponent<WearNTear>();
            if (wear == null)
            {
                return;
            }

            // A post standing alone in a field should not rot away in the rain or complain
            // about missing support.
            wear.m_noRoofWear = false;
            wear.m_noSupportWear = false;
        }

        /// <summary>
        ///     Copies inspector values between two components of the same type, skipping anything
        ///     that points at another prefab's objects (lids, animators, colliders...).
        /// </summary>
        private static void CopyPlainFields(Component source, Component target)
        {
            FieldInfo[] fields = target.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);

            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (field.IsInitOnly || field.IsLiteral)
                {
                    continue;
                }

                Type type = field.FieldType;
                if (typeof(Component).IsAssignableFrom(type) || type == typeof(GameObject))
                {
                    continue;
                }

                try
                {
                    field.SetValue(target, field.GetValue(source));
                }
                catch (Exception)
                {
                    // a field we are not allowed to touch - ignore it
                }
            }
        }

        private static void AddLocalization()
        {
            CustomLocalization loc = LocalizationManager.Instance.GetLocalization();

            loc.AddTranslation("English", new Dictionary<string, string>
            {
                { "piece_autofarm_post", "Farm Post" },
                {
                    "piece_autofarm_post_desc",
                    "Harvests crops and berries around it and replants seeds.\n" +
                    "Top rows: seeds. Bottom rows: harvest."
                },
                { "autofarm_hover_range", "Harvest {0} m / planting {1} m" },
                { "autofarm_hover_last", "Last cycle: harvested {0}, planted {1}" },
                { "autofarm_hover_full", "<color=orange>Harvest rows are full</color>" },
                { "autofarm_hover_noseeds", "<color=orange>No seeds in the top rows</color>" }
            });

            loc.AddTranslation("Russian", new Dictionary<string, string>
            {
                { "piece_autofarm_post", "Фермерский столб" },
                {
                    "piece_autofarm_post_desc",
                    "Сам собирает овощи и ягоды вокруг себя и сажает семена заново.\n" +
                    "Верхние ряды — семена. Нижние ряды — урожай."
                },
                { "autofarm_hover_range", "Сбор {0} м / посадка {1} м" },
                { "autofarm_hover_last", "Прошлый цикл: собрано {0}, посажено {1}" },
                { "autofarm_hover_full", "<color=orange>Ячейки урожая заполнены</color>" },
                { "autofarm_hover_noseeds", "<color=orange>Нет семян в верхних рядах</color>" }
            });
        }
    }
}
