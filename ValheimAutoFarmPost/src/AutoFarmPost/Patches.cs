using HarmonyLib;
using UnityEngine;

namespace AutoFarmPost
{
    /// <summary>Keeps <see cref="PickableRegistry" /> up to date.</summary>
    public static class CorePatches
    {
        [HarmonyPatch(typeof(Pickable), "Awake")]
        [HarmonyPostfix]
        private static void PickableAwakePostfix(Pickable __instance)
        {
            PickableRegistry.Add(__instance);
        }
    }

    /// <summary>Keeps <see cref="ContainerRegistry" /> up to date.</summary>
    public static class ContainerPatches
    {
        [HarmonyPatch(typeof(Container), "Awake")]
        [HarmonyPostfix]
        private static void ContainerAwakePostfix(Container __instance)
        {
            ContainerRegistry.Add(__instance);
        }
    }

    /// <summary>
    ///     The scarecrow: creatures cannot damage crops standing inside a post's radius.
    ///     Players still can, so a field can be cleared by hand.
    /// </summary>
    public static class ScarecrowPatches
    {
        [HarmonyPatch(typeof(Destructible), "Damage")]
        [HarmonyPrefix]
        private static bool DestructibleDamagePrefix(Destructible __instance, HitData hit)
        {
            if (!ModConfig.Scarecrow.Value)
            {
                return true;
            }

            if (__instance.GetComponent<Plant>() == null && __instance.GetComponent<Pickable>() == null)
            {
                return true;
            }

            if (hit != null && hit.GetAttacker() is Player)
            {
                return true;
            }

            if (!FarmPostRegistry.IsProtected(__instance.transform.position))
            {
                return true;
            }

            return false;
        }
    }

    /// <summary>Adds the post status to the hover text of the container.</summary>
    public static class HoverPatches
    {
        [HarmonyPatch(typeof(Container), "GetHoverText")]
        [HarmonyPostfix]
        private static void ContainerHoverPostfix(Container __instance, ref string __result)
        {
            if (!ModConfig.ShowHoverStatus.Value)
            {
                return;
            }

            FarmPost post = __instance.GetComponent<FarmPost>();
            if (post == null)
            {
                return;
            }

            string status = post.GetStatusText();
            if (!string.IsNullOrEmpty(status))
            {
                __result = __result + "\n" + status;
            }
        }
    }
}

