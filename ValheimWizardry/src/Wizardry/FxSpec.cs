using System.Collections.Generic;
using System.Globalization;

namespace Wizardry
{
    /// <summary>One operation of a spell: "slow=60,4" is Name "slow", Args ["60", "4"].</summary>
    internal sealed class FxOp
    {
        public string Name;
        public string[] Args;

        public string S(int i, string def = null)
        {
            return Args != null && i < Args.Length && Args[i].Length > 0 ? Args[i] : def;
        }

        public float F(int i, float def = 0f)
        {
            float v;
            return Args != null && i < Args.Length && float.TryParse(Args[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                ? v
                : def;
        }
    }

    /// <summary>
    ///     The executable side of a spell, parsed from its line in tools/spell_fx.txt:
    ///     a delivery (bolt, ray, chan, cone, nova, blast, self, ally, touch, place, global, ritual),
    ///     its parameters (spd, n, spr, pierce, r, ang, rng, max, aoe, delay) and the operations it applies.
    /// </summary>
    internal sealed class FxSpec
    {
        private static readonly HashSet<string> s_params = new HashSet<string>
            { "spd", "n", "spr", "pierce", "r", "ang", "rng", "max", "aoe", "delay" };

        private static readonly HashSet<string> s_deliveries = new HashSet<string>
            { "bolt", "ray", "chan", "cone", "nova", "blast", "self", "ally", "touch", "place", "global", "ritual" };

        public string Delivery;
        public readonly Dictionary<string, float> P = new Dictionary<string, float>();
        public readonly List<FxOp> Ops = new List<FxOp>();
        public readonly HashSet<string> Extra = new HashSet<string>();

        public static FxSpec Parse(string text)
        {
            var spec = new FxSpec();
            var tokens = text.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                string t = tokens[i];
                if (i == 0 && s_deliveries.Contains(t))
                {
                    spec.Delivery = t;
                    continue;
                }

                if (s_deliveries.Contains(t))
                {
                    spec.Extra.Add(t); // a second shape: "self ... cone" pushes enemies in a cone too
                    continue;
                }

                int eq = t.IndexOf('=');
                string name = eq > 0 ? t.Substring(0, eq) : t;
                string val = eq > 0 ? t.Substring(eq + 1) : null;
                float f;
                if (val != null && s_params.Contains(name) &&
                    float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f))
                {
                    spec.P[name] = f;
                    continue;
                }

                spec.Ops.Add(new FxOp { Name = name, Args = val != null ? val.Split(',') : new string[0] });
            }

            if (spec.Delivery == null)
            {
                spec.Delivery = "self";
            }

            return spec;
        }

        public float Param(string key, float def)
        {
            float v;
            return P.TryGetValue(key, out v) ? v : def;
        }

        public bool Has(string op)
        {
            foreach (var o in Ops)
            {
                if (o.Name == op)
                {
                    return true;
                }
            }

            return false;
        }

        public FxOp Get(string op)
        {
            foreach (var o in Ops)
            {
                if (o.Name == op)
                {
                    return o;
                }
            }

            return null;
        }

        /// <summary>Does it hurt anybody (decides who it looks for and whether it is a hostile act)?</summary>
        public bool Harmful
        {
            get
            {
                foreach (var o in Ops)
                {
                    switch (o.Name)
                    {
                        case "dmg": case "push": case "lift": case "stagger": case "slow": case "root": case "stun": case "freeze":
                        case "blind": case "silence": case "confuse": case "fear": case "charm": case "poly": case "vuln": case "weak":
                        case "shred": case "dot": case "bleed": case "exec": case "bosspct": case "forget": case "taunt": case "slam":
                        case "turn": case "revert": case "disarm": case "interrupt": case "mark": case "deaf": case "thorns":
                        case "shrink": case "skeleton": case "randomprank": case "calm": case "tame": case "info": case "pull":
                            return true;
                    }
                }

                return false;
            }
        }
    }
}
