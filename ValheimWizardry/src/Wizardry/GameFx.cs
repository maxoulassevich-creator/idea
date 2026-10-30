using System.Collections.Generic;
using UnityEngine;

namespace Wizardry
{
    /// <summary>
    ///     The game's own magic effects (Dvergr staffs, lightning, frost, summons, the Forsaken powers) played under the
    ///     mod's procedural ones. Only plain effect prefabs are used: anything with a network view is skipped, so nothing
    ///     here creates objects in the world save. Every effect is local; the spell message already reaches every client.
    /// </summary>
    internal static class GameFx
    {
        private static readonly Dictionary<string, GameObject> s_prefabs = new Dictionary<string, GameObject>();

        private static GameObject Find(string name)
        {
            GameObject go;
            if (s_prefabs.TryGetValue(name, out go))
            {
                return go;
            }

            go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
            if (go == null)
            {
                go = Jotunn.Managers.PrefabManager.Cache.GetPrefab<GameObject>(name);
            }

            if (go != null && go.GetComponentInChildren<ZNetView>(true) != null)
            {
                go = null;
            }

            if (ZNetScene.instance != null)
            {
                s_prefabs[name] = go; // remember misses only once the game has loaded everything
            }

            return go;
        }

        /// <summary>Plays the first of the named effects that exists; false when none does.</summary>
        public static bool Play(Vector3 pos, Quaternion rot, float scale, params string[] names)
        {
            if (!ModConfig.VanillaEffects.Value)
            {
                return false;
            }

            foreach (string n in names)
            {
                var prefab = Find(n);
                if (prefab == null)
                {
                    continue;
                }

                try
                {
                    var go = Object.Instantiate(prefab, pos, rot);
                    if (!Mathf.Approximately(scale, 1f))
                    {
                        go.transform.localScale *= scale;
                    }

                    Object.Destroy(go, 10f);
                    return true;
                }
                catch (System.Exception e)
                {
                    Plugin.Report("Game effect " + n, e);
                    s_prefabs[n] = null;
                }
            }

            return false;
        }

        public static string Element(SpellDef s)
        {
            if (s == null) return "";
            if (s.IsDark) return "dark";
            if (s.Arch == "heal") return "heal";
            return s.Damage.Length > 0 ? s.Damage[0] : "";
        }

        /// <summary>The burst at the wand's tip as the spell leaves it.</summary>
        public static void Cast(Vector3 pos, Vector3 dir, SpellDef s)
        {
            var rot = dir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(dir) : Quaternion.identity;
            switch (Element(s))
            {
                case "fire": Play(pos, rot, 0.6f, "vfx_FlameBall_launch", "fx_DvergerMage_Mistile_attack"); break;
                case "frost": Play(pos, rot, 0.6f, "vfx_ColdBall_launch", "fx_DvergerMage_Mistile_attack"); break;
                case "lightning": Play(pos, rot, 0.6f, "vfx_lightningstaff_fire", "fx_DvergerMage_Mistile_attack"); break;
                case "heal": Play(pos, rot, 0.7f, "fx_DvergerMage_Support_start"); break;
                case "dark": Play(pos, rot, 0.5f, "fx_redlightning_launch", "fx_DvergerMage_Mistile_attack"); break;
                default:
                    if (s != null && (s.Arch == "proj" || s.Arch == "beam" || s.Arch == "ctrl"))
                    {
                        Play(pos, rot, 0.5f, "fx_DvergerMage_Mistile_attack");
                    }

                    break;
            }
        }

        /// <summary>Where a spell lands: small for single hits, big for areas.</summary>
        public static void Hit(Vector3 pos, SpellDef s, bool big)
        {
            var rot = Quaternion.identity;
            switch (Element(s))
            {
                case "fire":
                    if (big) Play(pos, rot, 1f, "fx_fireball_staff_explosion", "vfx_FireballHit");
                    else Play(pos, rot, 0.8f, "fx_DvergerMage_Fire_hit", "vfx_FireballHit");
                    break;
                case "frost":
                    if (big) Play(pos, rot, 0.8f, "fx_fenring_icenova", "fx_DvergerMage_Ice_hit");
                    else Play(pos, rot, 0.8f, "fx_DvergerMage_Ice_hit", "vfx_ColdBall_Hit");
                    break;
                case "lightning":
                    if (big) Play(pos, rot, 1f, "fx_Lightning", "fx_chainlightning_hit");
                    else Play(pos, rot, 0.8f, "fx_chainlightning_hit", "fx_lightningstaffprojectile_hit");
                    break;
                case "poison": Play(pos, rot, big ? 1f : 0.6f, "vfx_BombBlob_explode_poison", "fx_greenroots_projectile_hit"); break;
                case "dark": Play(pos, rot, big ? 1f : 0.7f, "fx_redlightning_burst", "fx_DvergerMage_Mistile_die"); break;
                case "heal": Play(pos, rot, 0.8f, "fx_DvergerMage_Support_hit"); break;
                default:
                    if (big) Play(pos, rot, 0.8f, "fx_DvergerMage_Nova_ring", "fx_DvergerMage_Mistile_die");
                    else Play(pos, rot, 0.8f, "fx_DvergerMage_Mistile_die", "vfx_HitSparks");
                    break;
            }
        }

        public static void Heal(Vector3 pos)
        {
            Play(pos, Quaternion.identity, 1f, "fx_DvergerMage_Support_start", "vfx_HealthUpgrade");
        }

        public static void Buff(Vector3 pos, SpellDef s)
        {
            if (s != null && (s.Id.Contains("protego") || s.Arch == "buff" && s.School == "DEF"))
            {
                Play(pos, Quaternion.identity, 1f, "fx_StaffShield_Hit", "fx_shield_start");
            }
            else
            {
                Play(pos, Quaternion.identity, 0.9f, "vfx_HealthUpgrade", "fx_DvergerMage_Support_hit");
            }
        }

        public static void Summon(Vector3 pos)
        {
            Play(pos, Quaternion.identity, 1f, "fx_summon_start", "vfx_spawn_small");
        }

        public static void Blink(Vector3 pos)
        {
            Play(pos, Quaternion.identity, 0.8f, "vfx_odin_despawn", "fx_perfectdodge");
        }

        public static void Ceremony(Vector3 pos)
        {
            Play(pos, Quaternion.identity, 1f, "fx_GP_Activation", "vfx_HealthUpgrade");
        }
    }
}
