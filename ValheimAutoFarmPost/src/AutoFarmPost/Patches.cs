using HarmonyLib;

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
