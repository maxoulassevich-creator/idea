using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AutoFarmPost
{
    /// <summary>
    ///     Picking a plant without spawning loose items on the ground.
    ///     The few members that are private in the game are reached through reflection, so a
    ///     renamed field never stops the mod from compiling - at worst a bush keeps its old look
    ///     until the area is reloaded.
    /// </summary>
    internal static class PickableUtil
    {
        private static readonly int ZdoPicked = "picked".GetStableHashCode();
        private static readonly int ZdoPickedTime = "pickedTime".GetStableHashCode();
        private static readonly object[] TrueArg = { true };

        private static MethodInfo _setPicked;
        private static FieldInfo _pickedField;
        private static FieldInfo _hideWhenPicked;
        private static bool _reflectionReady;
        private static bool _warned;

        public static bool IsPicked(Pickable pickable, ZNetView nview)
        {
            ZDO zdo = nview.GetZDO();
            if (zdo != null && zdo.GetBool(ZdoPicked, false))
            {
                return true;
            }

            EnsureReflection();
            if (_pickedField != null)
            {
                try
                {
                    return (bool)_pickedField.GetValue(pickable);
                }
                catch (Exception)
                {
                    // ignore - the ZDO answer above is authoritative anyway
                }
            }

            return false;
        }

        /// <summary>
        ///     Marks the pickable as harvested. Bushes that regrow keep their ZDO (and respawn
        ///     normally), one-shot crops are removed from the world.
        /// </summary>
        public static void Pick(Pickable pickable, ZNetView nview)
        {
            if (!nview.IsOwner())
            {
                nview.ClaimOwnership();
            }

            if (pickable.m_respawnTimeMinutes > 0f)
            {
                ZDO zdo = nview.GetZDO();
                if (zdo != null)
                {
                    zdo.Set(ZdoPicked, true);
                    zdo.Set(ZdoPickedTime, ZNet.instance.GetTime().Ticks);
                }

                HidePicked(pickable);
                return;
            }

            // Unity only destroys the object at the end of the frame, but we want to plant a new
            // seed on that exact spot right now - so take its colliders out of the way first.
            Collider[] colliders = pickable.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    colliders[i].enabled = false;
                }
            }

            nview.Destroy();
        }

        private static void HidePicked(Pickable pickable)
        {
            EnsureReflection();

            try
            {
                if (_setPicked != null)
                {
                    _setPicked.Invoke(pickable, TrueArg);
                    return;
                }

                if (_pickedField != null)
                {
                    _pickedField.SetValue(pickable, true);
                }

                if (_hideWhenPicked != null)
                {
                    GameObject visual = _hideWhenPicked.GetValue(pickable) as GameObject;
                    if (visual != null)
                    {
                        visual.SetActive(false);
                    }
                }
            }
            catch (Exception e)
            {
                if (!_warned)
                {
                    _warned = true;
                    AutoFarmPlugin.Log.LogWarning("Could not update picked visuals: " + e.Message);
                }
            }
        }

        private static void EnsureReflection()
        {
            if (_reflectionReady)
            {
                return;
            }

            _reflectionReady = true;
            _setPicked = AccessTools.Method(typeof(Pickable), "SetPicked", new[] { typeof(bool) });
            _pickedField = AccessTools.Field(typeof(Pickable), "m_picked");
            _hideWhenPicked = AccessTools.Field(typeof(Pickable), "m_hideWhenPicked");
        }
    }
}
