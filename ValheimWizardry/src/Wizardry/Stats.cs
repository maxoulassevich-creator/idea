using System;
using System.Collections.Generic;
using System.Globalization;

namespace Wizardry
{
    /// <summary>
    ///     The numbers the tree, the specialisation, the house, the relics and the memories add up to.
    ///     Values are percents unless the name says otherwise (eitr, hp, armor, carry are flat).
    ///     Positive is always good for the wizard: "cost:8" makes spells 8% cheaper.
    /// </summary>
    internal enum Stat
    {
        dmg, dmg_single, dmg_area, dmg_fire, dmg_frost, dmg_lightning, dmg_poison, dmg_spirit, dmg_phys, dmg_dark, dmg_dot,
        dmg_undead, dmg_casters, dmg_controlled, dmg_unaware,
        cast_speed, cost, cdr, cdr_move, crit, crit_mult, range, proj_speed, area, dur, ctrl_dur, poly_dur, stagger,
        summon_dmg, summon_hp, summon_speed, summon_dur, summon_limit, undead_summon_dmg, birds, construct_dur,
        armor, res_elem, res_poison, res_spirit, res_ctrl, backstab_res, block, protego_cost, ward_power, ward_radius, ward_dur,
        building_res,
        eitr, eitr_pct, eitr_regen, hp, hp_pct, hp_regen, heal, lifesteal, stam_regen, run_stam, move_speed, carry, fall_res,
        stealth, dodge,
        rank_xp, skill_xp, bestiary_xp, dark_decay, dark_gain, dark_bonus, unforgivable_cost, tame_power, tame_speed,
        plant_growth, root_dur, food, mead_dur, mead_power, loot, focus, metaslot,
        Count
    }

    /// <summary>Situations a modifier can depend on ("dmg:12@night").</summary>
    [Flags]
    internal enum Cond
    {
        None = 0, night = 1, day = 2, lowhp = 4, forest = 8, dungeon = 16, still = 32, aftermove = 64, home = 128, dark50 = 256,
        unseen = 512, air = 1024, combat = 2048, heal = 4096, zone = 8192, summon = 16384
    }

    internal struct Mod
    {
        public Stat Stat;
        public float Value;
        public Cond When;
        public string Flag;
    }

    internal static class ModParser
    {
        private static readonly Dictionary<string, Mod[]> s_cache = new Dictionary<string, Mod[]>();

        public static Mod[] Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new Mod[0];
            }

            Mod[] cached;
            if (s_cache.TryGetValue(text, out cached))
            {
                return cached;
            }

            var list = new List<Mod>();
            foreach (string raw in text.Split(';'))
            {
                string part = raw.Trim();
                if (part.Length == 0)
                {
                    continue;
                }

                var m = new Mod();
                int at = part.IndexOf('@');
                if (at > 0)
                {
                    Cond c;
                    if (Enum.TryParse(part.Substring(at + 1), out c))
                    {
                        m.When = c;
                    }

                    part = part.Substring(0, at);
                }

                int colon = part.IndexOf(':');
                if (colon < 0)
                {
                    continue;
                }

                string key = part.Substring(0, colon);
                string val = part.Substring(colon + 1);
                if (key == "flag")
                {
                    m.Flag = val;
                    list.Add(m);
                    continue;
                }

                if (key == "learn")
                {
                    m.Flag = "learn:" + val;
                    list.Add(m);
                    continue;
                }

                Stat st;
                float v;
                if (Enum.TryParse(key, out st) && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                {
                    m.Stat = st;
                    m.Value = v;
                    list.Add(m);
                }
                else
                {
                    Plugin.Log.LogWarning("Unknown modifier '" + part + "' in '" + text + "'");
                }
            }

            var arr = list.ToArray();
            s_cache[text] = arr;
            return arr;
        }
    }

    /// <summary>The sum of everything the wizard has taken, rebuilt whenever it changes.</summary>
    internal sealed class StatBlock
    {
        private readonly float[] m_always = new float[(int)Stat.Count];
        private readonly List<Mod> m_conditional = new List<Mod>();
        private readonly HashSet<string> m_flags = new HashSet<string>();

        public void Clear()
        {
            Array.Clear(m_always, 0, m_always.Length);
            m_conditional.Clear();
            m_flags.Clear();
        }

        public void Add(string mods)
        {
            foreach (var m in ModParser.Parse(mods))
            {
                Add(m);
            }
        }

        public void Add(Mod m)
        {
            if (m.Flag != null)
            {
                m_flags.Add(m.Flag);
            }
            else if (m.When == Cond.None)
            {
                m_always[(int)m.Stat] += m.Value;
            }
            else
            {
                m_conditional.Add(m);
            }
        }

        public void AddStat(Stat s, float v)
        {
            m_always[(int)s] += v;
        }

        public bool Has(string flag)
        {
            return m_flags.Contains(flag);
        }

        public IEnumerable<string> Flags { get { return m_flags; } }

        /// <summary>The value of a stat in the current situation.</summary>
        public float Get(Stat s, Cond now = Cond.None)
        {
            float v = m_always[(int)s];
            if (m_conditional.Count > 0)
            {
                foreach (var m in m_conditional)
                {
                    if (m.Stat == s && (m.When & now) == m.When)
                    {
                        v += m.Value;
                    }
                }
            }

            return v;
        }

        /// <summary>1 + percent/100, never below 0.</summary>
        public float Mult(Stat s, Cond now = Cond.None)
        {
            return Math.Max(0f, 1f + Get(s, now) / 100f);
        }
    }
}
