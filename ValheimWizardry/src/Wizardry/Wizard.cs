using System;
using UnityEngine;

namespace Wizardry
{
    /// <summary>The local wizard: loads the character's data, keeps the stats current, saves, and runs the per-frame work.</summary>
    internal static class Wizard
    {
        public static WizardState State;
        public static bool Dirty;
        private static Player s_player;
        private static float s_statsTimer, s_lumosTimer;

        public static Player Player { get { return s_player; } }

        public static void Update()
        {
            var p = Player.m_localPlayer;
            if (p != s_player)
            {
                if (s_player != null && State != null)
                {
                    Save();
                }

                s_player = p;
                State = p != null ? WizardState.Load(p) : null;
                Casting.Reset();
                if (State != null)
                {
                    State.RebuildStats(p);
                    Grimoire.OnNewCharacter(State);
                }
            }

            // each part runs on its own: one that fails must not stop the others
            try { UpdateLumos(); } catch (Exception e) { Plugin.Report("Lumos", e); }
            try { Effects.Prune(); } catch (Exception e) { Plugin.Report("Effects", e); }
            try { Weather.Update(); } catch (Exception e) { Plugin.Report("Weather", e); }
            if (p == null || State == null)
            {
                return;
            }

            try { Mobility.Update(p); } catch (Exception e) { Plugin.Report("Mobility", e); }
            try { Ships.Update(); } catch (Exception e) { Plugin.Report("Ships", e); }

            s_statsTimer += Time.deltaTime;
            if (s_statsTimer > 1f)
            {
                s_statsTimer = 0f;
                try { State.RebuildStats(p); } catch (Exception e) { Plugin.Report("Stats", e); }
                try { Universalist(State); } catch (Exception e) { Plugin.Report("Universalist", e); }
                try { Guardians.Publish(p, State); } catch (Exception e) { Plugin.Report("Guardians", e); }
                try { Hallows.Update(p, State); } catch (Exception e) { Plugin.Report("Hallows", e); }
            }

            try { Casting.Update(p, State); } catch (Exception e) { Plugin.Report("Casting", e); }
        }

        /// <summary>The Universalist notable: +4% damage per school in the bar.</summary>
        private static void Universalist(WizardState w)
        {
            if (!w.Stats.Has("universalist"))
            {
                return;
            }

            var schools = new System.Collections.Generic.HashSet<string>();
            foreach (var id in w.Bar)
            {
                var s = SpellDb.Get(id);
                if (s != null)
                {
                    schools.Add(s.School);
                }
            }

            w.Stats.AddStat(Stat.dmg, Mathf.Min(32f, 4f * schools.Count));
        }

        public static void Save()
        {
            if (s_player != null && State != null && Dirty)
            {
                State.Save(s_player);
                Dirty = false;
            }
        }

        public static void Changed()
        {
            Dirty = true;
            if (s_player != null && State != null)
            {
                State.RebuildStats(s_player);
                State.Save(s_player);
            }
        }

        public static bool CanUseKeys(Player p)
        {
            if (p == null || p != Player.m_localPlayer || p.IsDead() || Grimoire.Open)
            {
                return false;
            }

            if (InventoryGui.IsVisible() || Menu.IsVisible() || Console.IsVisible() || TextInput.IsVisible() || Minimap.IsOpen() ||
                StoreGui.IsVisible() || (Chat.instance != null && Chat.instance.HasFocus()))
            {
                return false;
            }

            return true;
        }

        /// <summary>Lumos: a light at the tip of every lit wand, for everybody (the flag lives in the player's ZDO).</summary>
        private static void UpdateLumos()
        {
            s_lumosTimer -= Time.deltaTime;
            if (s_lumosTimer > 0f)
            {
                return;
            }

            s_lumosTimer = 0.4f;
            foreach (var pl in Player.GetAllPlayers())
            {
                var zdo = pl != null && pl.Nv() != null ? pl.Nv().GetZDO() : null;
                if (zdo == null)
                {
                    continue;
                }

                bool on = zdo.GetBool("wz_lumos");
                var existing = pl.transform.Find("WZ_Lumos");
                if (on && existing == null)
                {
                    var go = new GameObject("WZ_Lumos");
                    go.transform.SetParent(pl.transform, false);
                    go.AddComponent<LumosFollow>().Owner = pl;
                    var l = go.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.range = 14f;
                    l.intensity = 1.6f;
                    Color c;
                    l.color = State != null && pl == s_player && !string.IsNullOrEmpty(State.Colour) &&
                              ColorUtility.TryParseHtmlString(State.Colour, out c)
                        ? c
                        : new Color(1f, 0.97f, 0.88f);
                    l.shadows = LightShadows.Soft;
                }
                else if (!on && existing != null)
                {
                    UnityEngine.Object.Destroy(existing.gameObject);
                }
            }
        }
    }

    internal sealed class LumosFollow : MonoBehaviour
    {
        public Player Owner;

        private void LateUpdate()
        {
            if (Owner == null)
            {
                Destroy(gameObject);
                return;
            }

            transform.position = Wands.Tip(Owner) + Vector3.up * 0.05f;
        }
    }

    /// <summary>
    ///     Keystones and nodes that protect others (Blood Protection, the Healer's Return, the Locked Door) publish a
    ///     flag in the wizard's ZDO; the protected player's own game reads it when a lethal blow comes.
    /// </summary>
    internal static class Guardians
    {
        public const string Key = "wz_guard";

        public static void Publish(Player p, WizardState w)
        {
            var zdo = p.Nv() != null ? p.Nv().GetZDO() : null;
            if (zdo == null || !p.Nv().IsOwner())
            {
                return;
            }

            int flags = (w.Stats.Has("ks_blood") ? 1 : 0) | (w.Stats.Has("return_ally") ? 2 : 0) | (w.Stats.Has("locked_door") ? 4 : 0);
            if (zdo.GetInt(Key) != flags)
            {
                zdo.Set(Key, flags);
            }
        }

        /// <summary>A guardian near <paramref name="victim" /> (not the victim), or null.</summary>
        public static Player Near(Player victim, out int flags)
        {
            flags = 0;
            foreach (var pl in Player.GetAllPlayers())
            {
                if (pl == null || pl == victim || pl.IsDead())
                {
                    continue;
                }

                var zdo = pl.Nv() != null ? pl.Nv().GetZDO() : null;
                int f = zdo != null ? zdo.GetInt(Key) : 0;
                if (f != 0 && Vector3.Distance(pl.transform.position, victim.transform.position) < 15f)
                {
                    flags = f;
                    return pl;
                }
            }

            return null;
        }
    }
}
