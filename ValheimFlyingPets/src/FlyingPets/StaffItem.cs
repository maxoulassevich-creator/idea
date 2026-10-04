using System;
using System.Collections.Generic;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;

namespace FlyingPets
{
    /// <summary>The summoning staff: a vanilla staff model with our name, recipe and no attack of its own.</summary>
    internal static class StaffItem
    {
        public const string PrefabName = "FP_SkyStaff";
        public const string Token = "$item_fp_staff";

        public static bool IsStaff(ItemDrop.ItemData item)
        {
            return item != null && item.m_shared != null && item.m_shared.m_name == Token;
        }

        public static void Register()
        {
            string basePrefab = null;
            foreach (string candidate in new[] { ModConfig.StaffBasePrefab.Value, "StaffIceShards", "StaffSkeleton", "StaffFireball", "Club" })
            {
                if (!string.IsNullOrEmpty(candidate) && PrefabManager.Instance.GetPrefab(candidate) != null)
                {
                    basePrefab = candidate;
                    break;
                }
            }

            if (basePrefab == null)
            {
                FlyingPetsPlugin.Log.LogError("No vanilla staff found to base the summoning staff on");
                return;
            }

            var config = new ItemConfig
            {
                Name = Token,
                Description = "$item_fp_staff_desc",
                CraftingStation = ModConfig.StaffStation.Value ?? "",
                MinStationLevel = Math.Max(1, ModConfig.StaffStationLevel.Value),
                Requirements = ParseRecipe(ModConfig.StaffRecipe.Value),
            };

            var item = new CustomItem(PrefabName, basePrefab, config);
            var shared = item.ItemDrop.m_itemData.m_shared;
            shared.m_damages = new HitData.DamageTypes();
            shared.m_damagesPerLevel = new HitData.DamageTypes();
            shared.m_useDurability = false;
            shared.m_maxQuality = 1;
            shared.m_teleportable = true;
            shared.m_weight = 1.5f;
            shared.m_attackStatusEffect = null;
            Disarm(shared.m_attack);
            Disarm(shared.m_secondaryAttack);
            ItemManager.Instance.AddItem(item);
            FlyingPetsPlugin.Log.LogInfo("Registered " + PrefabName + " (model of " + basePrefab + ")");
        }

        private static void Disarm(Attack attack)
        {
            if (attack == null)
            {
                return;
            }

            attack.m_attackEitr = 0f;
            attack.m_attackStamina = 0f;
            attack.m_attackHealth = 0f;
        }

        private static RequirementConfig[] ParseRecipe(string text)
        {
            var list = new List<RequirementConfig>();
            foreach (string part in (text ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] kv = part.Split(':');
                int amount;
                if (kv.Length == 2 && int.TryParse(kv[1].Trim(), out amount) && amount > 0 && kv[0].Trim().Length > 0)
                {
                    list.Add(new RequirementConfig(kv[0].Trim(), amount, 0, true));
                }
                else
                {
                    FlyingPetsPlugin.Log.LogWarning("Ignoring recipe entry '" + part + "' (expected Prefab:Amount)");
                }
            }

            if (list.Count == 0)
            {
                list.Add(new RequirementConfig("Wood", 10, 0, true));
            }

            return list.ToArray();
        }
    }
}
