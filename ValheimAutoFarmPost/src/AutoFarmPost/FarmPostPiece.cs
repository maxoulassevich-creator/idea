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
                SetupVisual(prefab);

                if (prefab.GetComponent<FarmPost>() == null)
                {
                    prefab.AddComponent<FarmPost>();
                }

                if (prefab.GetComponent<PerchedRaven>() == null)
                {
                    prefab.AddComponent<PerchedRaven>();
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

                Sprite icon = IconRenderer.Render(prefab);
                if (icon != null)
                {
                    config.Icon = icon;
                }

                PieceManager.Instance.AddPiece(new CustomPiece(prefab, false, config));
                AutoFarmPlugin.Log.LogInfo("Farm post registered (base: " + basePrefab.name + ").");

                MythicFruit.Create();
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

        /// <summary>
        ///     Swaps the borrowed pole model for our own: a round carved pillar with a raven
        ///     head. The renderers themselves are kept so the game's wear-and-tear system keeps
        ///     working; only the mesh behind them changes.
        /// </summary>
        private static void SetupVisual(GameObject prefab)
        {
            try
            {
                Mesh mesh = RavenPostMesh.Get();
                if (mesh == null)
                {
                    return;
                }

                MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
                for (int i = 0; i < filters.Length; i++)
                {
                    filters[i].sharedMesh = mesh;
                    Reset(filters[i].transform, prefab.transform);
                }

                if (filters.Length == 0)
                {
                    AutoFarmPlugin.Log.LogWarning("Base prefab has no mesh to replace - the post keeps its old look.");
                }

                // the pole's thin collider does not fit a carved post any more
                BoxCollider[] boxes = prefab.GetComponentsInChildren<BoxCollider>(true);
                for (int i = 0; i < boxes.Length; i++)
                {
                    boxes[i].center = new Vector3(0f, 0.95f, 0.01f);
                    boxes[i].size = new Vector3(0.34f, 1.90f, 0.34f);
                    Reset(boxes[i].transform, prefab.transform);
                }

                if (boxes.Length == 0)
                {
                    MeshCollider[] meshColliders = prefab.GetComponentsInChildren<MeshCollider>(true);
                    for (int i = 0; i < meshColliders.Length; i++)
                    {
                        meshColliders[i].sharedMesh = mesh;
                        Reset(meshColliders[i].transform, prefab.transform);
                    }
                }
            }
            catch (Exception e)
            {
                AutoFarmPlugin.Log.LogWarning("Could not build the post model: " + e.Message);
            }
        }

        private static void Reset(Transform target, Transform root)
        {
            if (target == null || target == root)
            {
                return;
            }

            target.localPosition = Vector3.zero;
            target.localRotation = Quaternion.identity;
            target.localScale = Vector3.one;
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
                { "autofarm_hover_noseeds", "<color=orange>No seeds in the top rows</color>" },
                { "autofarm_hover_mythic", "Mythic fruits found: {0}" },
                { "autofarm_ui_split", "seeds above  -  harvest below" },
                { "msg_autofarm_mythic", "The raven found a mythic fruit" },
                { "item_autofarm_mythic", "Mythic Fruit" },
                {
                    "item_autofarm_mythic_desc",
                    "A fruit that should not exist. The raven turns one up in the field once in a " +
                    "long while. Eating it is worth more than any feast - but the gift is brief."
                },
                { "se_autofarm_mythic", "Gift of the Raven" },
                { "se_autofarm_mythic_tooltip", "Health and stamina regenerate much faster, and you carry more." }
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
                { "autofarm_hover_noseeds", "<color=orange>Нет семян в верхних рядах</color>" },
                { "autofarm_hover_mythic", "Найдено мифических плодов: {0}" },
                { "autofarm_ui_split", "сверху семена  -  снизу урожай" },
                { "msg_autofarm_mythic", "Ворон нашёл мифический плод" },
                { "item_autofarm_mythic", "Мифический плод" },
                {
                    "item_autofarm_mythic_desc",
                    "Плод, которого не должно быть. Ворон находит такой в поле раз в долгое время. " +
                    "Съесть его выгоднее любого пира — но дар недолог."
                },
                { "se_autofarm_mythic", "Дар ворона" },
                { "se_autofarm_mythic_tooltip", "Здоровье и выносливость восстанавливаются намного быстрее, и вы несёте больше." }
            });
        }
    }
}
