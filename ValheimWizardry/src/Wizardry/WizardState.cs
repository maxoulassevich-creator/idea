using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Wizardry
{
    /// <summary>
    ///     Everything a character has learned. Lives in Player.m_customData["Wizardry.v1"], so it travels with the
    ///     character between worlds and servers and never touches the world save.
    /// </summary>
    internal sealed class WizardState
    {
        public const string Key = "Wizardry.v1";
        public static readonly int[] RankCap = { 0, 3, 4, 5, 6, 7, 8, 9, 10, 10 };

        public string House;
        public readonly HashSet<string> Trophies = new HashSet<string>();
        public int SkillHigh;
        public readonly HashSet<string> Cards = new HashSet<string>();
        public readonly HashSet<string> Nodes = new HashSet<string>();
        public readonly HashSet<string> Pending = new HashSet<string>();
        public readonly Dictionary<string, int> Mastery = new Dictionary<string, int>();
        public readonly Dictionary<string, string> Sockets = new Dictionary<string, string>();
        public int FreeRefunds;
        public string Spec;
        public readonly HashSet<string> SpecNodes = new HashSet<string>();
        public readonly HashSet<string> Known = new HashSet<string>();
        public readonly HashSet<string> Books = new HashSet<string>();
        public readonly Dictionary<string, float> Xp = new Dictionary<string, float>();
        public readonly Dictionary<string, string> Aug = new Dictionary<string, string>();
        public readonly Dictionary<string, string> Meta = new Dictionary<string, string>();
        public readonly string[] Bar = new string[8];
        public string Grand;
        public int Selected;
        public float Darkness;
        public float Focus;
        public readonly Dictionary<string, int> Kills = new Dictionary<string, int>();
        public readonly Dictionary<string, int> Informs = new Dictionary<string, int>();
        public int HousePoints;
        public string Patronus = "Deer";
        public string Colour;
        public string Horcrux;
        public int CastCount;

        // 2.0: spells bought with points, talents, finished steps of the wizard's path
        public const int CurrentVersion = 2;
        public int Version;
        public readonly HashSet<string> Bought = new HashSet<string>();
        public readonly HashSet<string> Talents = new HashSet<string>();
        public readonly HashSet<string> Quests = new HashSet<string>();
        public bool Migrated;

        public readonly StatBlock Stats = new StatBlock();

        // ------------------------------------------------------------------ derived numbers
        public int Year { get { return Mathf.Clamp(1 + Trophies.Count, 1, 9); } }

        public int PointsTotal
        {
            get
            {
                float mult = Mathf.Max(0.1f, ModConfig.PointsMultiplier.Value);
                int raw = SkillHigh + (House != null ? 2 : 0) + 2 * Trophies.Count + Cards.Count + Quests.Count + Reading.Points(this);
                return Mathf.RoundToInt(raw * mult);
            }
        }

        public int PointsSpent
        {
            get { return TalentTree.Spent(this) + Learning.Spent(this); }
        }

        public int PointsFree { get { return PointsTotal - PointsSpent; } }
        public int SpecPointsTotal { get { return 2 * Mathf.Clamp(Year - 4, 0, 4); } }
        public int SpecPointsFree { get { return SpecPointsTotal - SpecNodes.Count; } }
        public int MaxRank { get { return RankCap[Year]; } }

        public static float XpForRank(int rank)
        {
            // total xp needed to reach `rank` from 1
            float total = 0f;
            for (int r = 1; r < rank; r++)
            {
                total += 150f * Mathf.Pow(1.55f, r - 1);
            }

            return total;
        }

        public int Rank(string spell)
        {
            float xp;
            Xp.TryGetValue(spell, out xp);
            int rank = 1;
            while (rank < 10 && xp >= XpForRank(rank + 1))
            {
                rank++;
            }

            return Mathf.Min(rank, MaxRank);
        }

        public bool Allocated(string id)
        {
            return Nodes.Contains(id) || Pending.Contains(id);
        }

        public bool KnowsSpell(string id)
        {
            var s = SpellDb.Get(id);
            if (s == null)
            {
                return false;
            }

            if (s.Role == "basic")
            {
                return true;
            }

            if (s.Role == "var")
            {
                return s.Parent != null && KnowsSpell(s.Parent) && VariantChosen(s);
            }

            return Known.Contains(id);
        }

        /// <summary>A variant is usable once a pattern of its parent names it.</summary>
        public bool VariantChosen(SpellDef v)
        {
            string a;
            if (!Aug.TryGetValue(v.Parent, out a) || string.IsNullOrEmpty(a))
            {
                return false;
            }

            foreach (string part in a.Split(','))
            {
                if (part.EndsWith("=" + v.Id))
                {
                    return true;
                }
            }

            return false;
        }

        public string AugChoice(string spell, int rank)
        {
            string a;
            if (!Aug.TryGetValue(spell, out a) || string.IsNullOrEmpty(a))
            {
                return null;
            }

            string prefix = "r" + rank + "=";
            foreach (string part in a.Split(','))
            {
                if (part.StartsWith(prefix))
                {
                    return part.Substring(prefix.Length);
                }
            }

            return null;
        }

        public void SetAugChoice(string spell, int rank, string choice)
        {
            var parts = new List<string>();
            string a;
            if (Aug.TryGetValue(spell, out a) && !string.IsNullOrEmpty(a))
            {
                parts.AddRange(a.Split(',').Where(p => !p.StartsWith("r" + rank + "=")));
            }

            parts.Add("r" + rank + "=" + choice);
            Aug[spell] = string.Join(",", parts.ToArray());
        }

        public List<string> Metas(string spell)
        {
            string m;
            return Meta.TryGetValue(spell, out m) && !string.IsNullOrEmpty(m) ? m.Split(',').ToList() : new List<string>();
        }

        public int MetaSlots(string spell)
        {
            int r = Rank(spell);
            int slots = r >= 8 ? 3 : r >= 5 ? 2 : r >= 2 ? 1 : 0;
            return slots + Mathf.RoundToInt(Stats.Get(Stat.metaslot));
        }

        // ------------------------------------------------------------------ tree: allocation
        public bool YearAllows(NodeDef n)
        {
            if (n.Type == "house")
            {
                return n.House == House;
            }

            return n.Year <= Year;
        }

        /// <summary>The shortest chain of new nodes from what is taken to <paramref name="target" />.</summary>
        public List<int> PathTo(NodeDef target)
        {
            if (target == null || Allocated(target.Id) || House == null)
            {
                return null;
            }

            var adj = TreeDb.Adj;
            var prev = new Dictionary<int, int>();
            var q = new Queue<int>();
            foreach (var id in Nodes.Concat(Pending))
            {
                var n = TreeDb.Get(id);
                if (n != null && !prev.ContainsKey(n.Index))
                {
                    prev[n.Index] = -1;
                    q.Enqueue(n.Index);
                }
            }

            while (q.Count > 0)
            {
                int x = q.Dequeue();
                if (x == target.Index)
                {
                    break;
                }

                foreach (int y in adj[x])
                {
                    if (prev.ContainsKey(y) || !YearAllows(TreeDb.Nodes[y]))
                    {
                        continue;
                    }

                    prev[y] = x;
                    q.Enqueue(y);
                }
            }

            if (!prev.ContainsKey(target.Index))
            {
                return null;
            }

            var path = new List<int>();
            int c = target.Index;
            while (c >= 0 && !Allocated(TreeDb.Nodes[c].Id))
            {
                path.Add(c);
                c = prev[c];
            }

            path.Reverse();
            return path;
        }

        /// <summary>Would the tree still hang together without <paramref name="id" />?</summary>
        public bool StaysConnectedWithout(string id)
        {
            string start = TreeDb.HouseStart(House);
            var all = new HashSet<string>(Nodes.Concat(Pending));
            all.Remove(id);
            var seen = new HashSet<string> { start };
            var q = new Queue<string>();
            q.Enqueue(start);
            var adj = TreeDb.Adj;
            while (q.Count > 0)
            {
                var n = TreeDb.Get(q.Dequeue());
                foreach (int y in adj[n.Index])
                {
                    string yid = TreeDb.Nodes[y].Id;
                    if (all.Contains(yid) && seen.Add(yid))
                    {
                        q.Enqueue(yid);
                    }
                }
            }

            return seen.Count == all.Count;
        }

        public void ConfirmPending()
        {
            foreach (var id in Pending)
            {
                Nodes.Add(id);
                var n = TreeDb.Get(id);
                if (n != null && n.Spell != null)
                {
                    Known.Add(n.Spell);
                }
            }

            Pending.Clear();
        }

        public void ResetTree()
        {
            Nodes.Clear();
            Pending.Clear();
            Mastery.Clear();
            Sockets.Clear();
            if (House != null)
            {
                Nodes.Add(TreeDb.HouseStart(House));
            }

            // tree spells are forgotten with their nodes (the house's own spell stays)
            foreach (var n in TreeDb.Nodes)
            {
                if (n.Spell != null && !(House != null && TreeData_HouseSpell(House) == n.Spell))
                {
                    Known.Remove(n.Spell);
                }
            }
        }

        public static string TreeData_HouseSpell(string house)
        {
            switch (house)
            {
                case "gryffindor": return "stupefy";
                case "ravenclaw": return "diffindo";
                case "hufflepuff": return "herbivicus";
                case "slytherin": return "serpensortia";
                default: return null;
            }
        }

        // ------------------------------------------------------------------ stats
        public void RebuildStats(Player player)
        {
            Stats.Clear();
            if (House != null)
            {
                Stats.Add(HouseMods(House));
            }

            foreach (var id in Talents)
            {
                var n = TalentTree.Get(id);
                if (n != null)
                {
                    Stats.Add(n.Mods);
                }
            }

            if (player != null && Items.HasAllHallows(player))
            {
                Stats.Add(TreeDb.HallowsMods);
            }

            Stats.Add(Wands.CoreMods(player));
        }

        private static string HouseMods(string house)
        {
            var n = TreeDb.Get(TreeDb.HouseStart(house));
            return n != null ? n.Mods : "";
        }

        // ------------------------------------------------------------------ persistence
        public static WizardState Load(Player player)
        {
            var s = new WizardState();
            string text;
            if (player != null && player.m_customData != null && player.m_customData.TryGetValue(Key, out text))
            {
                try
                {
                    s.Read(text);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Could not read the wizard's data, starting fresh: " + e.Message);
                    s = new WizardState();
                }
            }

            if (s.Version < CurrentVersion)
            {
                s.Migrate();
            }

            return s;
        }

        /// <summary>1.x saves: the old 457-node tree, sockets, masteries and callings are gone, so their points come back.</summary>
        private void Migrate()
        {
            Migrated = Nodes.Count > 0 || SpecNodes.Count > 0;
            Nodes.Clear();
            Pending.Clear();
            Mastery.Clear();
            Sockets.Clear();
            SpecNodes.Clear();
            Spec = null;
            Meta.Clear();
            FreeRefunds = 0;
            Version = CurrentVersion;
        }

        public void Save(Player player)
        {
            if (player == null || player.m_customData == null)
            {
                return;
            }

            player.m_customData[Key] = Write();
        }

        private string Write()
        {
            var sb = new StringBuilder();
            Line(sb, "house", House);
            Line(sb, "trophies", Join(Trophies));
            Line(sb, "skill", SkillHigh.ToString(CultureInfo.InvariantCulture));
            Line(sb, "cards", Join(Cards));
            Line(sb, "nodes", Join(Nodes));
            Line(sb, "mastery", Join(Mastery.Select(kv => kv.Key + ":" + kv.Value)));
            Line(sb, "sockets", Join(Sockets.Select(kv => kv.Key + ":" + kv.Value)));
            Line(sb, "refunds", FreeRefunds.ToString(CultureInfo.InvariantCulture));
            Line(sb, "spec", Spec);
            Line(sb, "specnodes", Join(SpecNodes));
            Line(sb, "known", Join(Known));
            Line(sb, "books", Join(Books));
            Line(sb, "xp", Join(Xp.Select(kv => kv.Key + ":" + kv.Value.ToString("0.#", CultureInfo.InvariantCulture))));
            Line(sb, "aug", Join(Aug.Select(kv => kv.Key + ":" + kv.Value)));
            Line(sb, "meta", Join(Meta.Select(kv => kv.Key + ":" + kv.Value)));
            Line(sb, "bar", string.Join("|", Bar.Select(b => b ?? "").ToArray()));
            Line(sb, "grand", Grand);
            Line(sb, "selected", Selected.ToString(CultureInfo.InvariantCulture));
            Line(sb, "darkness", Darkness.ToString("0.##", CultureInfo.InvariantCulture));
            Line(sb, "focus", Focus.ToString("0.##", CultureInfo.InvariantCulture));
            Line(sb, "kills", Join(Kills.Select(kv => kv.Key + ":" + kv.Value)));
            Line(sb, "informs", Join(Informs.Select(kv => kv.Key + ":" + kv.Value)));
            Line(sb, "housepoints", HousePoints.ToString(CultureInfo.InvariantCulture));
            Line(sb, "patronus", Patronus);
            Line(sb, "colour", Colour);
            Line(sb, "horcrux", Horcrux);
            Line(sb, "casts", CastCount.ToString(CultureInfo.InvariantCulture));
            Line(sb, "version", Version.ToString(CultureInfo.InvariantCulture));
            Line(sb, "bought", Join(Bought));
            Line(sb, "talents", Join(Talents));
            Line(sb, "quests", Join(Quests));
            return sb.ToString();
        }

        private void Read(string text)
        {
            foreach (string raw in text.Split('\n'))
            {
                int eq = raw.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                string k = raw.Substring(0, eq);
                string v = raw.Substring(eq + 1).TrimEnd('\r');
                switch (k)
                {
                    case "house": House = Empty(v); break;
                    case "trophies": Fill(Trophies, v); break;
                    case "skill": SkillHigh = Int(v); break;
                    case "cards": Fill(Cards, v); break;
                    case "nodes": Fill(Nodes, v); break;
                    case "mastery": foreach (var kv in Pairs(v)) Mastery[kv.Key] = Int(kv.Value); break;
                    case "sockets": foreach (var kv in Pairs(v)) Sockets[kv.Key] = kv.Value; break;
                    case "refunds": FreeRefunds = Int(v); break;
                    case "spec": Spec = Empty(v); break;
                    case "specnodes": Fill(SpecNodes, v); break;
                    case "known": Fill(Known, v); break;
                    case "books": Fill(Books, v); break;
                    case "xp": foreach (var kv in Pairs(v)) Xp[kv.Key] = Float(kv.Value); break;
                    case "aug": foreach (var kv in Pairs(v)) Aug[kv.Key] = kv.Value; break;
                    case "meta": foreach (var kv in Pairs(v)) Meta[kv.Key] = kv.Value; break;
                    case "bar":
                        var slots = v.Split('|');
                        for (int i = 0; i < Bar.Length && i < slots.Length; i++)
                        {
                            Bar[i] = Empty(slots[i]);
                        }

                        break;
                    case "grand": Grand = Empty(v); break;
                    case "selected": Selected = Mathf.Clamp(Int(v), 0, 7); break;
                    case "darkness": Darkness = Float(v); break;
                    case "focus": Focus = Float(v); break;
                    case "kills": foreach (var kv in Pairs(v)) Kills[kv.Key] = Int(kv.Value); break;
                    case "informs": foreach (var kv in Pairs(v)) Informs[kv.Key] = Int(kv.Value); break;
                    case "housepoints": HousePoints = Int(v); break;
                    case "patronus": Patronus = Empty(v) ?? "Deer"; break;
                    case "colour": Colour = Empty(v); break;
                    case "horcrux": Horcrux = Empty(v); break;
                    case "casts": CastCount = Int(v); break;
                    case "version": Version = Int(v); break;
                    case "bought": Fill(Bought, v); break;
                    case "talents": Fill(Talents, v); break;
                    case "quests": Fill(Quests, v); break;
                }
            }
        }

        private static void Line(StringBuilder sb, string k, string v)
        {
            sb.Append(k).Append('=').Append((v ?? "").Replace("\n", " ")).Append('\n');
        }

        private static string Join(IEnumerable<string> items)
        {
            return string.Join("|", items.Where(x => !string.IsNullOrEmpty(x)).Select(x => x.Replace("|", "/")).ToArray());
        }

        private static void Fill(HashSet<string> set, string v)
        {
            foreach (string x in v.Split('|'))
            {
                if (x.Length > 0)
                {
                    set.Add(x);
                }
            }
        }

        private static IEnumerable<KeyValuePair<string, string>> Pairs(string v)
        {
            foreach (string x in v.Split('|'))
            {
                int c = x.IndexOf(':');
                if (c > 0)
                {
                    yield return new KeyValuePair<string, string>(x.Substring(0, c), x.Substring(c + 1));
                }
            }
        }

        private static string Empty(string v)
        {
            return string.IsNullOrEmpty(v) ? null : v;
        }

        private static int Int(string v)
        {
            int i;
            return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out i) ? i : 0;
        }

        private static float Float(string v)
        {
            float f;
            return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f) ? f : 0f;
        }
    }
}
