using System.Collections.Generic;
using UnityEngine;

namespace Wizardry
{
    /// <summary>Putting statuses on characters, taking them off, and what every game knows about who has which.</summary>
    internal static class Effects
    {
        // statuses seen through the effect broadcast: zdoid -> key -> until (for visuals and "is it controlled?")
        private static readonly Dictionary<ZDOID, Dictionary<string, float>> s_seen = new Dictionary<ZDOID, Dictionary<string, float>>();

        private static readonly HashSet<string> s_control = new HashSet<string>
            { "stun", "root", "poly", "slow", "confuse", "fear", "charm", "lift", "blind", "silence", "disarm", "forget" };

        private static readonly HashSet<string> s_hardControl = new HashSet<string> { "stun", "poly", "charm", "lift", "fear", "forget" };

        /// <summary>
        ///     Puts a status on a creature or a player (through its owner). Bosses shrug off hard control; players
        ///     get the PvP share of it; the wizard's control-duration stat stretches it.
        /// </summary>
        public static void Apply(Player caster, WizardState w, Character target, string key, float duration, float power,
            string variant = null, string dtype = null, string buffs = null, string seName = null)
        {
            if (target == null || target.IsDead() || duration <= 0f)
            {
                return;
            }

            if (target.IsBoss() && s_hardControl.Contains(key))
            {
                if (key == "stun" || key == "lift")
                {
                    key = "slow";
                    power = 40f;
                    duration *= 0.5f;
                }
                else
                {
                    return;
                }
            }

            bool hostile = key != "buff" && key != "immune" && key != "hidden" && key != "ward";
            duration *= Patterns.Duration;
            if (hostile && w != null)
            {
                if (s_control.Contains(key))
                {
                    duration *= 1f + w.Stats.Get(Stat.ctrl_dur) / 100f;
                }

                if (key == "poly")
                {
                    duration *= 1f + w.Stats.Get(Stat.poly_dur) / 100f;
                }

                if (key == "root")
                {
                    duration *= 1f + w.Stats.Get(Stat.root_dur) / 100f;
                }
            }
            else if (!hostile && w != null)
            {
                duration *= 1f + w.Stats.Get(Stat.dur) / 100f;
            }

            if (hostile && target.IsPlayer())
            {
                duration *= ModConfig.PvpControl.Value;
                if (duration <= 0.05f)
                {
                    return;
                }
            }

            var order = new StatusOrder
            {
                Key = key, SeName = seName ?? ("WZ_" + key), Duration = duration, Power = power, Variant = variant ?? "",
                DamageType = dtype ?? "", Buffs = buffs ?? "",
                Source = caster != null && caster.m_nview != null && caster.m_nview.GetZDO() != null ? caster.m_nview.GetZDO().m_uid : ZDOID.None
            };
            Net.SendStatus(target, order);
            Fx.SendStatus(target, key, variant, duration);
        }

        /// <summary>Makes <paramref name="target" /> attack <paramref name="focus" /> for a while.</summary>
        public static void ApplyTaunt(Player caster, Character target, Character focus, float duration)
        {
            if (target == null || focus == null || target.IsPlayer() || focus.m_nview == null || focus.m_nview.GetZDO() == null)
            {
                return;
            }

            var order = new StatusOrder
            {
                Key = "taunt", SeName = "WZ_taunt", Duration = duration, Power = 0f, Source = focus.m_nview.GetZDO().m_uid
            };
            Net.SendStatus(target, order);
            Fx.SendStatus(target, "taunt", "", duration);
        }

        /// <summary>Runs on the owner of <paramref name="c" />.</summary>
        public static void ApplyLocal(Character c, StatusOrder o)
        {
            var seman = c.GetSEMan();
            if (seman == null)
            {
                return;
            }

            bool hostile = o.Key != "buff" && o.Key != "immune" && o.Key != "hidden" && o.Key != "ward";
            if (hostile && seman.HaveStatusEffect(StatusDefs.Hash("immune")))
            {
                return;
            }

            if (hostile && c == Player.m_localPlayer)
            {
                var w = Wizard.State;
                if (w != null)
                {
                    o.Duration *= Mathf.Max(0.1f, 1f - w.Stats.Get(Stat.res_ctrl) / 100f);
                    if (w.Stats.Has("mind_wall") && (o.Key == "charm" || o.Key == "confuse"))
                    {
                        return;
                    }
                }

                if (Buffs.Value(c, "debuffimm") > 0f && o.Key != "slow")
                {
                    return;
                }
            }

            int hash = o.SeName.GetStableHashCode();
            var se = seman.AddStatusEffect(hash, true) as WzStatus;
            if (se == null)
            {
                se = seman.GetStatusEffect(hash) as WzStatus;
            }

            if (se == null)
            {
                return;
            }

            if (se.Def == null)
            {
                se.Def = StatusDefs.Get(o.Key);
            }

            se.m_ttl = o.Duration;
            se.m_time = 0f;
            se.Power = o.Power;
            se.Variant = o.Variant;
            se.DamageType = o.DamageType;
            se.Buffs = o.Buffs;
            if (o.Source != ZDOID.None && ZNetScene.instance != null)
            {
                var go = ZNetScene.instance.FindInstance(o.Source);
                se.Source = go != null ? go.GetComponent<Character>() : null;
            }

            se.Begin();
        }

        /// <summary>Disillusionment and the Bedazzling Hex end when their wearer attacks.</summary>
        public static void BreakInvisibility(Character c)
        {
            var seman = c != null ? c.GetSEMan() : null;
            if (seman == null)
            {
                return;
            }

            var remove = new List<StatusEffect>();
            foreach (var se in seman.GetStatusEffects())
            {
                var wz = se as WzStatus;
                if (wz != null && wz.Def != null && wz.Def.Kind == StatusKind.Buff && (wz.BuffValue("invis") > 0f || wz.BuffValue("stealth") >= 75f) &&
                    !se.name.Contains("cloak"))
                {
                    remove.Add(se);
                }
            }

            foreach (var se in remove)
            {
                seman.RemoveStatusEffect(se, true);
            }
        }

        public static void CleanseLocal(Character c, string what)
        {
            var seman = c != null ? c.GetSEMan() : null;
            if (seman == null)
            {
                return;
            }

            var keys = new HashSet<string>((what ?? "").Split(','));
            bool all = keys.Contains("all");
            var remove = new List<StatusEffect>();
            foreach (var se in seman.GetStatusEffects())
            {
                var wz = se as WzStatus;
                string vanilla = se.name;
                if (wz != null && wz.Def != null)
                {
                    bool ctrl = s_control.Contains(wz.Def.Key) || wz.Def.Key == "vuln" || wz.Def.Key == "weak" || wz.Def.Key == "shred" ||
                                wz.Def.Key == "dot" || wz.Def.Key == "bleed" || wz.Def.Key == "deaf" || wz.Def.Key == "mark";
                    if (keys.Contains("buffs") && wz.Def.Kind == StatusKind.Buff)
                    {
                        remove.Add(se);
                    }
                    else if ((all || keys.Contains("ctrl") || keys.Contains("curse")) && ctrl)
                    {
                        remove.Add(se);
                    }
                    else if (keys.Contains(wz.Def.Key) || (keys.Contains("poly") && wz.Def.Key == "poly"))
                    {
                        remove.Add(se);
                    }

                    continue;
                }

                if (all && (vanilla == "Wet" || vanilla == "Poison" || vanilla == "Burning" || vanilla == "Tared" || vanilla == "Smoked" ||
                            vanilla == "Frost" || vanilla == "Spirit" || vanilla == "Lightning"))
                {
                    remove.Add(se);
                    continue;
                }

                if ((keys.Contains("wet") && vanilla == "Wet") || (keys.Contains("poison") && vanilla == "Poison") ||
                    (keys.Contains("burn") && vanilla == "Burning") || (keys.Contains("tar") && vanilla == "Tared") ||
                    (keys.Contains("smoke") && vanilla == "Smoked") || (keys.Contains("slime") && (vanilla == "Tared" || vanilla == "Wet")) ||
                    (keys.Contains("cold") && (vanilla == "Cold" || vanilla == "Freezing")))
                {
                    remove.Add(se);
                }

                if (keys.Contains("buffs") && !c.IsPlayer() && se.m_ttl > 0f && vanilla != "Burning" && vanilla != "Poison")
                {
                    remove.Add(se); // a shaman's shield and the like
                }
            }

            foreach (var se in remove)
            {
                seman.RemoveStatusEffect(se, true);
            }
        }

        // ------------------------------------------------------------------ what this game has seen
        public static void Seen(ZDOID id, string key, float duration)
        {
            Dictionary<string, float> d;
            if (!s_seen.TryGetValue(id, out d))
            {
                d = new Dictionary<string, float>();
                s_seen[id] = d;
            }

            d[key] = Time.time + duration;
        }

        public static void Forget(ZDOID id, string key)
        {
            Dictionary<string, float> d;
            if (s_seen.TryGetValue(id, out d))
            {
                d.Remove(key);
            }
        }

        public static bool Has(Character c, string key)
        {
            if (c == null)
            {
                return false;
            }

            var seman = c.GetSEMan();
            if (seman != null && seman.HaveStatusEffect(StatusDefs.Hash(key)))
            {
                return true;
            }

            var zdo = c.m_nview != null ? c.m_nview.GetZDO() : null;
            Dictionary<string, float> d;
            float until;
            return zdo != null && s_seen.TryGetValue(zdo.m_uid, out d) && d.TryGetValue(key, out until) && until > Time.time;
        }

        public static bool HasControl(Character c)
        {
            foreach (var k in s_control)
            {
                if (Has(c, k))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool Disabled(Character c)
        {
            return Has(c, "stun") || Has(c, "poly") || Has(c, "lift");
        }

        public static void Prune()
        {
            if (Time.frameCount % 600 != 0)
            {
                return;
            }

            var dead = new List<ZDOID>();
            foreach (var kv in s_seen)
            {
                bool any = false;
                foreach (var t in kv.Value.Values)
                {
                    any |= t > Time.time;
                }

                if (!any)
                {
                    dead.Add(kv.Key);
                }
            }

            foreach (var id in dead)
            {
                s_seen.Remove(id);
            }
        }
    }

    /// <summary>Reads the wizard's own buffs (statuses of kind Buff) on a character.</summary>
    internal static class Buffs
    {
        public static float Value(Character c, string key)
        {
            var seman = c != null ? c.GetSEMan() : null;
            if (seman == null)
            {
                return 0f;
            }

            float v = 0f;
            foreach (var se in seman.GetStatusEffects())
            {
                var wz = se as WzStatus;
                if (wz != null && wz.Def != null && wz.Def.Kind == StatusKind.Buff)
                {
                    v += wz.BuffValue(key);
                }
            }

            return v;
        }
    }
}
