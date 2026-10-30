using System.Collections.Generic;
using UnityEngine;

namespace Wizardry
{
    /// <summary>
    ///     Spell patterns (01_PROGRESSION §6.3): at ranks 3, 6 and 9 a spell offers its archetype's option or its own
    ///     special one. A special that names a variant (Flipendo Tria) makes the variant castable; any other special
    ///     strengthens the spell (+20% effect and area).
    /// </summary>
    internal static class Patterns
    {
        /// <summary>The cast being resolved right now (set around every per-target step), for durations.</summary>
        public static CastContext Current;

        private static readonly Dictionary<string, string[]> s_pool = new Dictionary<string, string[]>
        {
            { "proj", new[] { "p_fan", "p_pierce", "p_ricochet", "p_speed" } },
            { "aoe", new[] { "a_area", "a_wave", "a_trail", "a_pull" } },
            { "beam", new[] { "c_range", "c_ramp", "c_split", "c_cost" } },
            { "ctrl", new[] { "x_dur", "x_vuln", "x_cascade", "x_power" } },
            { "summ", new[] { "s_plus1", "s_hp", "s_dur", "s_burst" } },
            { "buff", new[] { "b_dur", "b_aoe", "b_regen", "b_cost" } },
            { "heal", new[] { "h_power", "h_aoe", "h_regen", "h_cost" } },
            { "move", new[] { "m_range", "m_charges", "m_decoy", "m_ward" } },
            { "util", new[] { "u_area", "u_dur", "u_cd", "u_cost" } },
            { "ritual", new[] { "r_mats", "r_area", "r_dur", "r_free" } },
        };

        private static readonly Dictionary<string, string[]> s_names = new Dictionary<string, string[]>
        {
            { "p_fan", new[] { "+1 снаряд веером", "+1 projectile in a fan" } },
            { "p_pierce", new[] { "Пробивает ещё 2 цели", "Pierces 2 more targets" } },
            { "p_ricochet", new[] { "Отскок к 2 ближайшим врагам", "Ricochets to 2 nearby enemies" } },
            { "p_speed", new[] { "+40% скорости и дальности", "+40% speed and range" } },
            { "a_area", new[] { "+30% радиуса", "+30% radius" } },
            { "a_wave", new[] { "Вторая волна через 1 с (60%)", "A second wave after 1 s (60%)" } },
            { "a_trail", new[] { "Оставляет след на 4 с", "Leaves a trail for 4 s" } },
            { "a_pull", new[] { "Стягивает врагов к центру", "Pulls enemies to the centre" } },
            { "c_range", new[] { "+30% дальности луча", "+30% beam range" } },
            { "c_ramp", new[] { "Урон растёт до +60% за время луча", "Damage grows to +60% while held" } },
            { "c_split", new[] { "Луч раздваивается (второй враг — 50%)", "The beam forks (second enemy at 50%)" } },
            { "c_cost", new[] { "−40% стоимости в секунду", "−40% cost per second" } },
            { "x_dur", new[] { "+40% длительности контроля", "+40% control duration" } },
            { "x_vuln", new[] { "Цель получает +15% урона 4 с", "The target takes +15% damage for 4 s" } },
            { "x_cascade", new[] { "Перескакивает на 2 ближайших врага", "Jumps to 2 nearby enemies" } },
            { "x_power", new[] { "+25% силы (и против звёздных)", "+25% power (starred foes too)" } },
            { "s_plus1", new[] { "+1 призыв", "+1 summon" } },
            { "s_hp", new[] { "+40% здоровья призыва", "+40% summon health" } },
            { "s_dur", new[] { "+40% длительности призыва", "+40% summon duration" } },
            { "s_burst", new[] { "Появление призыва бьёт по площади", "The summon's arrival strikes an area" } },
            { "b_dur", new[] { "+40% длительности", "+40% duration" } },
            { "b_aoe", new[] { "Действует и на союзников в 6 м", "Also affects allies within 6 m" } },
            { "b_regen", new[] { "Даёт ещё и регенерацию", "Adds regeneration" } },
            { "b_cost", new[] { "−30% стоимости", "−30% cost" } },
            { "h_power", new[] { "+30% лечения", "+30% healing" } },
            { "h_aoe", new[] { "Лечит и союзников в 6 м", "Also heals allies within 6 m" } },
            { "h_regen", new[] { "Оставляет регенерацию на 10 с", "Leaves regeneration for 10 s" } },
            { "h_cost", new[] { "−30% стоимости", "−30% cost" } },
            { "m_range", new[] { "+50% дальности", "+50% range" } },
            { "m_charges", new[] { "Вдвое короче перезарядка", "Half the cooldown" } },
            { "m_decoy", new[] { "Оставляет приманку на старте", "Leaves a decoy behind" } },
            { "m_ward", new[] { "Защита 1 с после перемещения", "1 s of protection after moving" } },
            { "u_area", new[] { "+50% радиуса и дальности", "+50% radius and range" } },
            { "u_dur", new[] { "+50% длительности", "+50% duration" } },
            { "u_cd", new[] { "−30% перезарядки", "−30% cooldown" } },
            { "u_cost", new[] { "−30% стоимости", "−30% cost" } },
            { "r_mats", new[] { "−30% материалов", "−30% materials" } },
            { "r_area", new[] { "+50% радиуса", "+50% radius" } },
            { "r_dur", new[] { "+50% длительности", "+50% duration" } },
            { "r_free", new[] { "Можно колдовать вне Кодекса", "May be cast away from a Codex" } },
            { "special", new[] { "Особый узор: +20% силы и площади", "Special pattern: +20% power and area" } },
        };

        public static string Kind(SpellDef s)
        {
            if (s.IsRitual) return "ritual";
            return s_pool.ContainsKey(s.Arch) ? s.Arch : "aoe";
        }

        /// <summary>The archetype option offered at <paramref name="rank" /> (3, 6 or 9).</summary>
        public static string PoolOption(SpellDef s, int rank, bool alternative)
        {
            var pool = s_pool[Kind(s)];
            if (alternative)
            {
                return pool[3];
            }

            return pool[rank >= 9 ? 2 : rank >= 6 ? 1 : 0];
        }

        public static string Special(SpellDef s, int rank)
        {
            return rank >= 9 ? s.R9 : rank >= 6 ? s.R6 : s.R3;
        }

        /// <summary>The variant the special option of this rank opens, if any.</summary>
        public static SpellDef Variant(SpellDef s, int rank)
        {
            string tag = "(р" + rank + ")";
            foreach (var v in SpellDb.VariantsOf(s.Id))
            {
                if (v.Effect != null && v.Effect.Contains(tag))
                {
                    return v;
                }
            }

            string sp = Special(s, rank);
            if (!string.IsNullOrEmpty(sp))
            {
                foreach (var v in SpellDb.VariantsOf(s.Id))
                {
                    if (sp.StartsWith(v.Ru) || sp.StartsWith(v.En))
                    {
                        return v;
                    }
                }
            }

            return null;
        }

        /// <summary>The two choices at a rank: the archetype's, and the special one (code, label).</summary>
        public static KeyValuePair<string, string>[] Choices(SpellDef s, int rank)
        {
            string pool = PoolOption(s, rank, false);
            var v = Variant(s, rank);
            string sp = Special(s, rank);
            KeyValuePair<string, string> second;
            if (v != null)
            {
                second = new KeyValuePair<string, string>(v.Id, Texts.L("Вариант: ", "Variant: ") + v.Name);
            }
            else if (!string.IsNullOrEmpty(sp))
            {
                second = new KeyValuePair<string, string>("special", sp + " — " + Label("special"));
            }
            else
            {
                string alt = PoolOption(s, rank, true);
                second = new KeyValuePair<string, string>("pool:" + alt, Label(alt));
            }

            return new[] { new KeyValuePair<string, string>("pool:" + pool, Label(pool)), second };
        }

        public static string Label(string code)
        {
            string[] n;
            return s_names.TryGetValue(code, out n) ? Texts.L(n[0], n[1]) : code;
        }

        public static HashSet<string> Active(WizardState w, SpellDef s)
        {
            var set = new HashSet<string>();
            if (w == null || s == null)
            {
                return set;
            }

            int rank = w.Rank(s.Id);
            foreach (int r in new[] { 3, 6, 9 })
            {
                if (rank < r)
                {
                    continue;
                }

                string c = w.AugChoice(s.Id, r);
                if (string.IsNullOrEmpty(c))
                {
                    continue;
                }

                if (c.StartsWith("pool:")) set.Add(c.Substring(5));
                else if (c == "special") set.Add("special");
            }

            return set;
        }

        // ------------------------------------------------------------------ effects
        public static void Apply(CastContext c)
        {
            var p = c.Patterns;
            if (p.Contains("special")) { c.Mult *= 1.2f; c.Area *= 1.2f; }
            if (p.Contains("p_fan")) c.ExtraBolts++;
            if (p.Contains("p_pierce")) c.ExtraPierce += 2;
            if (p.Contains("p_ricochet") || p.Contains("x_cascade")) c.Cascade = true;
            if (p.Contains("p_speed")) c.SpeedMult *= 1.4f;
            if (p.Contains("a_area")) c.Area *= 1.3f;
            if (p.Contains("c_range")) c.SpeedMult *= 1.3f;
            if (p.Contains("x_dur")) c.DurMult *= 1.4f;
            if (p.Contains("x_power")) c.Mult *= 1.25f;
            if (p.Contains("b_dur")) c.DurMult *= 1.4f;
            if (p.Contains("h_power")) c.Mult *= 1.3f;
            if (p.Contains("m_range")) c.SpeedMult *= 1.5f;
            if (p.Contains("u_area") || p.Contains("r_area")) c.Area *= 1.5f;
            if (p.Contains("u_dur") || p.Contains("r_dur")) c.DurMult *= 1.5f;
            if (p.Contains("m_decoy")) Summons.Decoy(c, c.Caster.transform.position, 1, 5f);
        }

        /// <summary>What happens after the spell went off (in the caster's game).</summary>
        public static void After(CastContext c)
        {
            var p = c.Patterns;
            var caster = c.Caster;
            var w = c.W;
            if (p.Contains("a_pull"))
            {
                foreach (var t in Character.GetAllCharacters())
                {
                    if (Combat.IsEnemy(caster, t) && !t.IsBoss() && Vector3.Distance(t.transform.position, c.AimPoint) < 8f * c.Area)
                    {
                        Vector3 to = c.AimPoint - t.transform.position;
                        Net.Impulse(t, to.normalized * Mathf.Min(12f, to.magnitude * 2f) + Vector3.up * 2f);
                    }
                }
            }

            if (p.Contains("a_trail"))
            {
                Anchor.Spawn(caster, w, c.Spell, "trail", c.Spec.Delivery == "nova" ? caster.transform.position : c.AimPoint, 3f * c.Area, 4f);
            }

            if (p.Contains("b_aoe") || p.Contains("h_aoe"))
            {
                foreach (var t in Character.GetAllCharacters())
                {
                    if (t != caster && Combat.IsAlly(caster, t) && Vector3.Distance(t.transform.position, caster.transform.position) < 6f)
                    {
                        SpellOps.OnAlly(c, t);
                    }
                }
            }

            if (p.Contains("b_regen") || p.Contains("h_regen"))
            {
                Effects.Apply(caster, w, caster, "buff", 10f, 0f, null, null, "hpregen:6");
            }

            if (p.Contains("m_ward"))
            {
                Effects.Apply(caster, w, caster, "buff", 1f, 0f, null, null, "armor:200");
            }

            if (p.Contains("s_burst"))
            {
                Fx.Send(FxKind.Burst, c.AimPoint + Vector3.up * 0.3f, Vector3.up, c.Spell.Id, 4f);
                foreach (var t in Character.GetAllCharacters())
                {
                    if (Combat.IsEnemy(caster, t) && Vector3.Distance(t.transform.position, c.AimPoint) < 4f)
                    {
                        Combat.Strike(caster, w, c.Spell, t, 0.8f, t.GetCenterPoint(), (t.transform.position - c.AimPoint).normalized, 8f);
                    }
                }
            }
        }

        public static float CostMult(WizardState w, SpellDef s)
        {
            var p = Active(w, s);
            float m = 1f;
            if (p.Contains("b_cost") || p.Contains("h_cost") || p.Contains("u_cost")) m *= 0.7f;
            return m;
        }

        public static float CooldownMult(WizardState w, SpellDef s)
        {
            var p = Active(w, s);
            float m = 1f;
            if (p.Contains("m_charges")) m *= 0.5f;
            if (p.Contains("u_cd")) m *= 0.7f;
            return m;
        }

        /// <summary>Duration multiplier of the cast in progress (1 outside a cast).</summary>
        public static float Duration { get { return Current != null ? Current.DurMult : 1f; } }
    }
}
