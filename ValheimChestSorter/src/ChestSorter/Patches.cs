using HarmonyLib;

namespace ChestSorter
{
    /// <summary>Every container registers itself and gets a label holder.</summary>
    public static class ContainerPatches
    {
        [HarmonyPatch(typeof(Container), "Awake")]
        [HarmonyPostfix]
        private static void ContainerAwakePostfix(Container __instance)
        {
            ChestRegistry.Add(__instance);

            if (__instance.GetComponent<ChestLabel>() == null)
            {
                __instance.gameObject.AddComponent<ChestLabel>();
            }
        }
    }

    /// <summary>Looking at a chest tells you what it is for.</summary>
    public static class HoverPatches
    {
        [HarmonyPatch(typeof(Container), "GetHoverText")]
        [HarmonyPostfix]
        private static void ContainerHoverPostfix(Container __instance, ref string __result)
        {
            if (!ModConfig.WriteLabels.Value)
            {
                return;
            }

            string label = LabelStore.Get(__instance);
            if (!string.IsNullOrEmpty(label))
            {
                __result = "<color=#FFD98A>[ " + label + " ]</color>\n" + __result;
            }
        }
    }
}
