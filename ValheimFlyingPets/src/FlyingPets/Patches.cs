using HarmonyLib;

namespace FlyingPets
{
    /// <summary>Attacking with the staff summons instead of casting.</summary>
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    internal static class StaffAttackPatch
    {
        private static bool Prefix(Humanoid __instance, bool secondaryAttack, ref bool __result)
        {
            var player = __instance as Player;
            if (player == null || player != Player.m_localPlayer || !StaffItem.IsStaff(player.GetCurrentWeapon()))
            {
                return true;
            }

            __result = Summoner.UseStaff(player, secondaryAttack);
            return false;
        }
    }

    /// <summary>
    ///     While riding a pet, Jump / Crouch steer the flight instead of ending the ride, and attacks
    ///     are ignored (the game would otherwise drop the rider).
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
    internal static class RiderControlsPatch
    {
        private static void Prefix(Player __instance, ref bool attack, ref bool attackHold, ref bool secondaryAttack,
            ref bool secondaryAttackHold, ref bool jump, ref bool crouch, ref bool dodge)
        {
            if (__instance != Player.m_localPlayer || !(__instance.GetDoodadController() is FlyingPet))
            {
                return;
            }

            attack = false;
            attackHold = false;
            secondaryAttack = false;
            secondaryAttackHold = false;
            jump = false;
            crouch = false;
            dodge = false;
        }
    }

    /// <summary>Use (E) while riding: land first and climb down along the wing instead of falling off.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.StopDoodadControl))]
    internal static class RiderStopPatch
    {
        private static bool Prefix(Player __instance)
        {
            var pet = __instance.GetDoodadController() as FlyingPet;
            return pet == null || pet.AllowStopControl(__instance);
        }
    }
}
