using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace FlyingPets
{
    /// <summary>Creature AI: held in the talons, frozen under the raven's shadow, or keeping away from a pegasus.</summary>
    [HarmonyPatch]
    internal static class CreatureAiPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var monster = AccessTools.DeclaredMethod(typeof(MonsterAI), "UpdateAI", new[] { typeof(float) });
            if (monster != null)
            {
                yield return monster;
            }

            var animal = AccessTools.DeclaredMethod(typeof(AnimalAI), "UpdateAI", new[] { typeof(float) });
            if (animal != null)
            {
                yield return animal;
            }
        }

        private static bool Prefix(BaseAI __instance, float __0, ref bool __result)
        {
            return Passives.OverrideAi(__instance, __0, ref __result);
        }
    }

    /// <summary>A creature in the raven's talons is carried, not simulated.</summary>
    [HarmonyPatch]
    internal static class HeldPhysicsPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var character = AccessTools.DeclaredMethod(typeof(Character), "CustomFixedUpdate", new[] { typeof(float) });
            if (character != null)
            {
                yield return character;
            }

            var humanoid = AccessTools.DeclaredMethod(typeof(Humanoid), "CustomFixedUpdate", new[] { typeof(float) });
            if (humanoid != null)
            {
                yield return humanoid;
            }
        }

        private static bool Prefix(Character __instance)
        {
            return !Passives.IsHeld(__instance);
        }
    }

    /// <summary>Huginn: a wider circle of the map is uncovered while flying the raven.</summary>
    [HarmonyPatch(typeof(Minimap), "UpdateExplore")]
    internal static class HuginnExplorePatch
    {
        private static void Prefix(Minimap __instance, out float __state)
        {
            __state = __instance.m_exploreRadius;
            if (Passives.HuginnActive)
            {
                __instance.m_exploreRadius = __state * ModConfig.HuginnMultiplier.Value;
            }
        }

        private static void Postfix(Minimap __instance, float __state)
        {
            __instance.m_exploreRadius = __state;
        }
    }

    /// <summary>Ravens' feast: trophies drop more often from creatures that die near a raven.</summary>
    [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
    internal static class RavenFeastPatch
    {
        private static void Prefix(CharacterDrop __instance, out List<KeyValuePair<CharacterDrop.Drop, float>> __state)
        {
            __state = null;
            if (__instance.m_drops == null || !Passives.FeastNear(__instance.transform.position))
            {
                return;
            }

            float mult = ModConfig.FeastMultiplier.Value;
            foreach (var drop in __instance.m_drops)
            {
                if (drop == null || drop.m_prefab == null || !IsTrophy(drop.m_prefab))
                {
                    continue;
                }

                if (__state == null)
                {
                    __state = new List<KeyValuePair<CharacterDrop.Drop, float>>();
                }

                __state.Add(new KeyValuePair<CharacterDrop.Drop, float>(drop, drop.m_chance));
                drop.m_chance *= mult;
            }
        }

        private static void Postfix(List<KeyValuePair<CharacterDrop.Drop, float>> __state)
        {
            if (__state == null)
            {
                return;
            }

            foreach (var kv in __state)
            {
                kv.Key.m_chance = kv.Value;
            }
        }

        private static bool IsTrophy(UnityEngine.GameObject prefab)
        {
            if (prefab.name.StartsWith("Trophy"))
            {
                return true;
            }

            var item = prefab.GetComponent<ItemDrop>();
            return item != null && item.m_itemData != null && item.m_itemData.m_shared != null &&
                   item.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy;
        }
    }

    /// <summary>Under the wing: rain and frost do not reach the pegasus rider.</summary>
    [HarmonyPatch]
    internal static class UnderWingPatch
    {
        private static MethodBase TargetMethod()
        {
            foreach (var m in typeof(SEMan).GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                var ps = m.GetParameters();
                if (m.Name == "AddStatusEffect" && ps.Length >= 1 && ps[0].ParameterType == typeof(StatusEffect))
                {
                    return m;
                }
            }

            return null;
        }

        private static bool Prefix(SEMan __instance, StatusEffect __0, ref StatusEffect __result)
        {
            if (!Passives.UnderWingActive || __0 == null)
            {
                return true;
            }

            var player = Player.m_localPlayer;
            if (player == null || !ReferenceEquals(__instance, player.GetSEMan()))
            {
                return true;
            }

            int hash = __0.NameHash();
            if (hash == SEMan.s_statusEffectWet || hash == SEMan.s_statusEffectCold || hash == SEMan.s_statusEffectFreezing)
            {
                __result = null;
                return false;
            }

            return true;
        }
    }
}
