using System;
using System.Reflection;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace AutoFarmPost
{
    /// <summary>
    ///     The rare find of the farm post: a mythic fruit. It is not craftable and cannot be
    ///     grown on purpose - the post turns one up now and then while harvesting. Eating it
    ///     gives a short but very strong buff, stronger than any vanilla meal.
    ///
    ///     The item is assembled at runtime from a vanilla food item (so it keeps working
    ///     ItemDrop/network components), with our own mesh, a golden material and a status
    ///     effect built on the spot.
    /// </summary>
    internal static class MythicFruit
    {
        public const string PrefabName = "AutoFarmPost_MythicFruit";
        public const string NameToken = "$item_autofarm_mythic";
        public const string DescToken = "$item_autofarm_mythic_desc";
        public const string EffectNameToken = "$se_autofarm_mythic";
        public const string EffectTooltipToken = "$se_autofarm_mythic_tooltip";

        /// <summary>Vanilla food items tried as a base, in order.</summary>
        private static readonly string[] BasePrefabs =
        {
            "MushroomYellow", "MushroomMagecap", "MushroomBlue", "Mushroom", "Raspberry", "Turnip", "Carrot"
        };

        private static GameObject _prefab;

        public static GameObject Prefab
        {
            get { return _prefab; }
        }

        public static void Create()
        {
            if (_prefab != null)
            {
                return;
            }

            try
            {
                GameObject basePrefab = null;
                for (int i = 0; i < BasePrefabs.Length && basePrefab == null; i++)
                {
                    basePrefab = PrefabManager.Instance.GetPrefab(BasePrefabs[i]);
                }

                if (basePrefab == null)
                {
                    AutoFarmPlugin.Log.LogWarning("No base item found - the mythic fruit is disabled.");
                    return;
                }

                GameObject prefab = PrefabManager.Instance.CreateClonedPrefab(PrefabName, basePrefab);
                if (prefab == null)
                {
                    return;
                }

                ItemDrop drop = prefab.GetComponent<ItemDrop>();
                if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null)
                {
                    AutoFarmPlugin.Log.LogWarning("Base item has no item data - the mythic fruit is disabled.");
                    return;
                }

                SetupVisual(prefab);

                ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;
                shared.m_name = NameToken;
                shared.m_description = DescToken;
                shared.m_itemType = ItemDrop.ItemData.ItemType.Consumable;
                shared.m_maxStackSize = 10;
                shared.m_weight = 0.3f;
                shared.m_teleportable = true;
                shared.m_questItem = false;

                // Clearly the best meal in the game, but it only lasts 20 minutes by default.
                shared.m_food = ModConfig.MythicFood.Value;
                shared.m_foodStamina = ModConfig.MythicStamina.Value;
                shared.m_foodEitr = ModConfig.MythicEitr.Value;
                shared.m_foodRegen = 8f;
                shared.m_foodBurnTime = Mathf.Max(30f, ModConfig.MythicBuffSeconds.Value);

                Sprite icon = IconRenderer.Render(prefab);
                if (icon != null)
                {
                    shared.m_icons = new[] { icon };
                }
                else if (shared.m_icons != null && shared.m_icons.Length > 0)
                {
                    icon = shared.m_icons[0];
                }

                StatusEffect effect = CreateEffect(icon);
                if (effect != null)
                {
                    shared.m_consumeStatusEffect = effect;
                }

                ItemManager.Instance.AddItem(new CustomItem(prefab, false));
                _prefab = prefab;
                AutoFarmPlugin.Log.LogInfo("Mythic fruit registered (base: " + basePrefab.name + ").");
            }
            catch (Exception e)
            {
                AutoFarmPlugin.Log.LogError("Could not create the mythic fruit: " + e);
            }
        }

        private static void SetupVisual(GameObject prefab)
        {
            try
            {
                Mesh mesh = MythicFruitMesh.Get();
                MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
                for (int i = 0; i < filters.Length; i++)
                {
                    filters[i].sharedMesh = mesh;
                }

                // a warm golden version of whatever material the base item used
                MeshRenderer[] renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Material source = renderers[i].sharedMaterial;
                    if (source == null)
                    {
                        continue;
                    }

                    Material gold = new Material(source);
                    gold.name = "AutoFarmPost_MythicGold";

                    Color tint = new Color(1f, 0.80f, 0.32f, 1f);
                    if (gold.HasProperty("_Color"))
                    {
                        gold.SetColor("_Color", tint);
                    }

                    if (gold.HasProperty("_EmissionColor"))
                    {
                        gold.SetColor("_EmissionColor", new Color(1f, 0.62f, 0.18f, 1f));
                    }

                    if (gold.HasProperty("_EmissiveColor"))
                    {
                        gold.SetColor("_EmissiveColor", new Color(1f, 0.62f, 0.18f, 1f));
                    }

                    renderers[i].sharedMaterial = gold;
                }
            }
            catch (Exception e)
            {
                AutoFarmPlugin.Log.LogWarning("Mythic fruit visuals skipped: " + e.Message);
            }
        }

        /// <summary>
        ///     Builds the buff. The fields that exist in every version are set directly; the
        ///     rest go through reflection, so a renamed field only drops that one bonus.
        /// </summary>
        private static StatusEffect CreateEffect(Sprite icon)
        {
            try
            {
                SE_Stats effect = ScriptableObject.CreateInstance<SE_Stats>();
                effect.name = "SE_AutoFarmMythic";
                effect.m_name = EffectNameToken;
                effect.m_tooltip = EffectTooltipToken;
                effect.m_icon = icon;
                effect.m_ttl = Mathf.Max(30f, ModConfig.MythicBuffSeconds.Value);

                Set(effect, "m_healthRegenMultiplier", 2.5f);
                Set(effect, "m_staminaRegenMultiplier", 2.0f);
                Set(effect, "m_eitrRegenMultiplier", 1.5f);
                Set(effect, "m_addMaxCarryWeight", 100f);
                Set(effect, "m_runStaminaDrainModifier", -0.35f);
                Set(effect, "m_jumpStaminaUseModifier", -0.35f);
                Set(effect, "m_addMaxStamina", 25f);

                ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(effect, false));
                return effect;
            }
            catch (Exception e)
            {
                AutoFarmPlugin.Log.LogWarning("Mythic buff skipped: " + e.Message);
                return null;
            }
        }

        private static void Set(object target, string field, float value)
        {
            try
            {
                FieldInfo info = target.GetType().GetField(field,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (info != null && info.FieldType == typeof(float))
                {
                    info.SetValue(target, value);
                }
            }
            catch (Exception)
            {
                // a bonus we cannot set is not worth a log line
            }
        }
    }
}
