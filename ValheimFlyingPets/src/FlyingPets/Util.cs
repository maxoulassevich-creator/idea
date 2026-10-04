using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FlyingPets
{
    internal static class Util
    {
        public static readonly string[] SummonEffects = { "fx_summon_skeleton_spawn", "vfx_spawn", "vfx_odin_despawn" };
        public static readonly string[] DespawnEffects = { "vfx_odin_despawn", "fx_summon_skeleton_spawn", "vfx_spawn" };

        private static int s_solidMask = -1;
        private static FieldInfo s_camDistance;
        private static FieldInfo s_maxAirAltitude;
        private static bool s_reflected;

        /// <summary>Everything a pet can stand on (terrain, buildings, rocks, ships).</summary>
        public static int SolidMask
        {
            get
            {
                if (s_solidMask < 0)
                {
                    s_solidMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
                }

                return s_solidMask;
            }
        }

        /// <summary>Height of the ground under p (cast from a little above p); waterY = sea level.</summary>
        public static float GroundY(Vector3 p, float startAbove, out float waterY)
        {
            var zs = ZoneSystem.instance;
            waterY = zs != null ? zs.m_waterLevel : -10000f;
            RaycastHit hit;
            if (Physics.Raycast(p + Vector3.up * startAbove, Vector3.down, out hit, startAbove + 1000f, SolidMask,
                    QueryTriggerInteraction.Ignore))
            {
                return hit.point.y;
            }

            return zs != null ? zs.GetGroundHeight(p) : p.y;
        }

        /// <summary>The same checks the game makes before it reads movement keys.</summary>
        public static bool InputAllowed()
        {
            try
            {
                if (Chat.instance != null && Chat.instance.HasFocus())
                {
                    return false;
                }

                return !global::Console.IsVisible() && !Menu.IsVisible() && !TextInput.IsVisible() &&
                       !Minimap.InTextInput() && !InventoryGui.IsVisible() && !Hud.InRadial();
            }
            catch (Exception)
            {
                return true;
            }
        }

        public static Player FindPlayer(long playerId)
        {
            if (playerId == 0L)
            {
                return null;
            }

            foreach (var p in Player.GetAllPlayers())
            {
                if (p != null && p.GetPlayerID() == playerId)
                {
                    return p;
                }
            }

            return null;
        }

        public static void SpawnEffect(string[] candidates, Vector3 pos, Quaternion rot)
        {
            var scene = ZNetScene.instance;
            if (scene == null)
            {
                return;
            }

            foreach (string name in candidates)
            {
                var prefab = scene.GetPrefab(name);
                if (prefab == null)
                {
                    continue;
                }

                try
                {
                    // effects are local only; a ZNetView on the prefab would make a networked copy
                    if (prefab.GetComponent<ZNetView>() != null)
                    {
                        continue;
                    }

                    UnityEngine.Object.Instantiate(prefab, pos, rot);
                    return;
                }
                catch (Exception e)
                {
                    FlyingPetsPlugin.Log.LogDebug("Effect " + name + " failed: " + e.Message);
                }
            }
        }

        // ------------------------------------------------------------------ private game fields
        private static void Reflect()
        {
            if (s_reflected)
            {
                return;
            }

            s_reflected = true;
            s_camDistance = AccessTools.Field(typeof(GameCamera), "m_distance");
            s_maxAirAltitude = AccessTools.Field(typeof(Character), "m_maxAirAltitude");
        }

        public static float GetCameraDistance(GameCamera cam)
        {
            Reflect();
            try
            {
                return s_camDistance != null ? (float)s_camDistance.GetValue(cam) : cam.m_maxDistance;
            }
            catch (Exception)
            {
                return cam.m_maxDistance;
            }
        }

        public static void SetCameraDistance(GameCamera cam, float value)
        {
            Reflect();
            try
            {
                if (s_camDistance != null)
                {
                    s_camDistance.SetValue(cam, Mathf.Clamp(value, cam.m_minDistance, cam.m_maxDistance));
                }
            }
            catch (Exception)
            {
                // camera zoom is cosmetic
            }
        }

        /// <summary>Puts a player who lost their mount mid-air on the ground below, without fall damage.</summary>
        public static void PutOnGround(Player player, Vector3 from)
        {
            float water;
            float y = GroundY(from, 1f, out water);
            if (from.y - Mathf.Max(y, water) < 3f)
            {
                return;
            }

            Vector3 p = new Vector3(from.x, Mathf.Max(y, water) + 0.1f, from.z);
            player.transform.position = p;
            var body = player.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = p;
                body.linearVelocity = Vector3.zero;
            }

            Reflect();
            try
            {
                if (s_maxAirAltitude != null)
                {
                    s_maxAirAltitude.SetValue(player, p.y);
                }
            }
            catch (Exception)
            {
                // worst case the player takes fall damage
            }
        }
    }
}
