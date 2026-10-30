using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wizardry
{
    /// <summary>One spell of the catalogue (generated from tools/spells/*.txt and tools/spell_fx.txt).</summary>
    internal sealed class SpellDef
    {
        public readonly string Id, Incantation, Ru, En, School, Role, Arch, Scale, Anim, Effect, Vfx, Parent, R3, R6, R9, Fx;
        public readonly int Year;
        public readonly string[] Damage;
        public readonly string[] Sources;
        public readonly float Cooldown;
        public FxSpec Spec;

        public SpellDef(string id, string inc, string ru, string en, string school, int year, string role, string arch, string dmg,
            string scale, string anim, float cd, string effect, string vfx, string parent, string r3, string r6, string r9, string fx,
            string src)
        {
            Id = id;
            Incantation = inc;
            Ru = ru;
            En = en;
            School = school;
            Year = year;
            Role = role;
            Arch = arch;
            Damage = string.IsNullOrEmpty(dmg) ? new string[0] : dmg.Split(',');
            Scale = scale;
            Anim = anim;
            Cooldown = cd;
            Effect = effect;
            Vfx = vfx;
            Parent = parent;
            R3 = r3;
            R6 = r6;
            R9 = r9;
            Fx = fx;
            Sources = string.IsNullOrEmpty(src) ? new string[0] : src.Split(',');
        }

        public bool Castable { get { return Fx != null && Role != "lore" && Role != "system" && Role != "meta"; } }
        public bool IsMeta { get { return Role == "meta"; } }
        public bool IsGrand { get { return Role == "grand"; } }
        public bool IsRitual { get { return Role == "ritual"; } }
        public bool IsDark { get { return School == "DRK"; } }
        public bool Unforgivable { get { return Id == "avada_kedavra" || Id == "crucio" || Id == "imperio"; } }
        public string Name { get { return Texts.English ? En : Ru; } }

        /// <summary>The year factor of the formulas in 01_PROGRESSION §12.</summary>
        public float YearFactor { get { return 1f + 0.35f * (Year - 1); } }

        public float BaseDamage
        {
            get
            {
                float coef;
                switch (Arch)
                {
                    case "proj": coef = 1f; break;
                    case "aoe": coef = 0.8f; break;
                    case "beam": coef = 0.45f; break;
                    case "ctrl": coef = 0.5f; break;
                    case "summ": coef = 0.6f; break;
                    case "move": coef = 0.5f; break;
                    case "util": coef = 0.4f; break;
                    case "heal": coef = 1.4f; break;
                    default: coef = 0.6f; break;
                }

                return 12f * YearFactor * coef;
            }
        }

        public float BaseCost
        {
            get
            {
                float b;
                switch (Arch)
                {
                    case "proj": b = 8f; break;
                    case "aoe": b = 16f; break;
                    case "beam": b = 12f; break;
                    case "ctrl": b = 14f; break;
                    case "summ": b = 30f; break;
                    case "buff": b = 20f; break;
                    case "heal": b = 12f; break;
                    case "move": b = 15f; break;
                    case "util": b = 6f; break;
                    case "ritual": b = 60f; break;
                    default: b = 10f; break;
                }

                if (Role == "grand")
                {
                    b = 30f;
                }

                return b * (1f + 0.25f * (Year - 1));
            }
        }

        public float BaseCooldown
        {
            get
            {
                if (Cooldown >= 0f)
                {
                    return Cooldown;
                }

                switch (Arch)
                {
                    case "proj": return 1f;
                    case "aoe": return 6f;
                    case "beam": return 0.5f;
                    case "ctrl": return 8f;
                    case "summ": return 30f;
                    case "buff": return 30f;
                    case "heal": return 4f;
                    case "move": return 8f;
                    case "util": return 5f;
                    case "ritual": return 600f;
                    default: return 3f;
                }
            }
        }
    }

    internal sealed class NodeDef
    {
        public readonly string Id, Type, School, Name, Text, Spell, House, Mods, Hybrid;
        public readonly int Ring, Year;
        public readonly float X, Y;
        public readonly string[] Options, OptionMods;
        public int Index;

        public NodeDef(string id, string type, string school, int ring, int year, float x, float y, string name, string text, string spell,
            string house, string mods, string[] options, string[] optionMods, string hybrid)
        {
            Id = id;
            Type = type;
            School = school;
            Ring = ring;
            Year = year;
            X = x;
            Y = y;
            Name = name;
            Text = text;
            Spell = spell;
            House = house;
            Mods = mods ?? "";
            Options = options ?? new string[0];
            OptionMods = optionMods ?? new string[0];
            Hybrid = hybrid;
        }

        /// <summary>Points it costs: house starts are free.</summary>
        public int Cost { get { return Type == "house" ? 0 : 1; } }
    }

    internal sealed class SpecDef
    {
        public readonly string Id, Name, Role;
        public readonly NodeDef[] Nodes;
        public readonly int[] Edges;
        public readonly List<int>[] Adj;

        public SpecDef(string id, string name, string role, NodeDef[] nodes, int[] edges)
        {
            Id = id;
            Name = name;
            Role = role;
            Nodes = nodes;
            Edges = edges;
            Adj = new List<int>[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                nodes[i].Index = i;
                Adj[i] = new List<int>();
            }

            for (int i = 0; i + 1 < edges.Length; i += 2)
            {
                Adj[edges[i]].Add(edges[i + 1]);
                Adj[edges[i + 1]].Add(edges[i]);
            }
        }
    }

    internal sealed class SchoolDef
    {
        public readonly string Code, Name, NameEn, House;
        public readonly Color Color;

        public SchoolDef(string code, string name, string nameEn, string house, string color)
        {
            Code = code;
            Name = name;
            NameEn = nameEn;
            House = house;
            Color c;
            Color = ColorUtility.TryParseHtmlString(color, out c) ? c : Color.white;
        }
    }

    internal static partial class SpellDb
    {
        private static Dictionary<string, SpellDef> s_byId;

        public static SpellDef Get(string id)
        {
            Init();
            SpellDef s;
            return id != null && s_byId.TryGetValue(id, out s) ? s : null;
        }

        public static void Init()
        {
            if (s_byId != null)
            {
                return;
            }

            s_byId = new Dictionary<string, SpellDef>();
            foreach (var s in All)
            {
                s_byId[s.Id] = s;
                if (s.Fx != null)
                {
                    try
                    {
                        s.Spec = FxSpec.Parse(s.Fx);
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogError("Spell " + s.Id + ": bad fx '" + s.Fx + "': " + e.Message);
                    }
                }
            }
        }

        /// <summary>Variants of a spell (chosen as its patterns).</summary>
        public static List<SpellDef> VariantsOf(string id)
        {
            var list = new List<SpellDef>();
            foreach (var s in All)
            {
                if (s.Parent == id && s.Role == "var")
                {
                    list.Add(s);
                }
            }

            return list;
        }
    }

    internal static partial class TreeDb
    {
        private static Dictionary<string, NodeDef> s_byId;
        private static List<int>[] s_adj;
        private static Dictionary<string, SchoolDef> s_schools;

        public static List<int>[] Adj { get { Init(); return s_adj; } }

        public static void Init()
        {
            if (s_byId != null)
            {
                return;
            }

            s_byId = new Dictionary<string, NodeDef>();
            s_adj = new List<int>[Nodes.Length];
            for (int i = 0; i < Nodes.Length; i++)
            {
                Nodes[i].Index = i;
                s_byId[Nodes[i].Id] = Nodes[i];
                s_adj[i] = new List<int>();
            }

            for (int i = 0; i + 1 < Edges.Length; i += 2)
            {
                s_adj[Edges[i]].Add(Edges[i + 1]);
                s_adj[Edges[i + 1]].Add(Edges[i]);
            }

            s_schools = new Dictionary<string, SchoolDef>();
            foreach (var s in Schools)
            {
                s_schools[s.Code] = s;
            }
        }

        public static NodeDef Get(string id)
        {
            Init();
            NodeDef n;
            return id != null && s_byId.TryGetValue(id, out n) ? n : null;
        }

        public static SchoolDef School(string code)
        {
            Init();
            SchoolDef s;
            return code != null && s_schools.TryGetValue(code, out s) ? s : null;
        }

        public static Color SchoolColor(string code)
        {
            var s = School(code);
            return s != null ? s.Color : new Color(0.85f, 0.8f, 0.65f);
        }

        public static SpecDef Spec(string id)
        {
            foreach (var s in Specs)
            {
                if (s.Id == id)
                {
                    return s;
                }
            }

            return null;
        }

        public static string HouseStart(string house)
        {
            return "house_" + house;
        }
    }
}
