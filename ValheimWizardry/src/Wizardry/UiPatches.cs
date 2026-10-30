using HarmonyLib;
using UnityEngine;

namespace Wizardry
{
    /// <summary>Small hooks that make the mod easier to find and use.</summary>
    internal static class UiPatches
    {
        /// <summary>The Codex tells what else it can do: "[K] Grimoire".</summary>
        [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.GetHoverText))]
        private static class CodexHover
        {
            private static void Postfix(CraftingStation __instance, ref string __result)
            {
                if (__instance == null || __instance.m_name != "$wz_codex" || string.IsNullOrEmpty(__result))
                {
                    return;
                }

                __result += "\n[<color=yellow><b>" + ModConfig.GrimoireKey.Value + "</b></color>] " +
                            Texts.L("Гримуар: путь, заклинания, таланты", "Grimoire: path, spells, talents");
                var w = Wizard.State;
                if (w != null && w.PointsFree > 0)
                {
                    __result += "\n<color=#ffd27a>" + w.PointsFree + "</color> " + Texts.L("свободных очков магии", "free magic points");
                }
            }
        }

        /// <summary>Alt + a number picks a spell slot, so it must not also use the item in that hotbar slot.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.UseHotbarItem))]
        private static class HotbarWithModifier
        {
            private static bool Prefix(Player __instance)
            {
                var key = ModConfig.SelectModifier.Value;
                return key == KeyCode.None || __instance != Player.m_localPlayer || !ZInput.GetKey(key, false);
            }
        }
    }
}
