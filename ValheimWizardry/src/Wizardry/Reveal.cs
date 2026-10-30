using System.Collections.Generic;
using UnityEngine;

namespace Wizardry
{
    /// <summary>Revealing charms: beacons over what the spell finds (seen by the caster only) and pins on the map.</summary>
    internal static class Reveal
    {
        /// <summary>A tall beam over a creature, for this game only.</summary>
        public static void Mark(Character t, float duration)
        {
            if (t == null)
            {
                return;
            }

            Beacon(t.transform, new Color(1f, 0.85f, 0.3f), duration, t.GetHeight() + 0.3f);
        }

        public static void Beacon(Transform target, Color c, float duration, float height)
        {
            if (target == null)
            {
                return;
            }

            var old = target.Find("WZ_Beacon");
            if (old != null)
            {
                Object.Destroy(old.gameObject);
            }

            var go = new GameObject("WZ_Beacon");
            go.transform.SetParent(target, false);
            go.transform.localPosition = Vector3.up * height;
            var lr = Fx.Line(go.transform, "Beam", c, 0.12f);
            lr.useWorldSpace = false;
            lr.positionCount = 2;
            lr.SetPosition(0, Vector3.zero);
            lr.SetPosition(1, Vector3.up * 14f);
            var glow = Fx.Line(go.transform, "Glow", new Color(c.r, c.g, c.b, 0.3f), 0.5f);
            glow.useWorldSpace = false;
            glow.positionCount = 2;
            glow.SetPosition(0, Vector3.zero);
            glow.SetPosition(1, Vector3.up * 6f);
            Object.Destroy(go, duration);
        }

        public static void Area(Player caster, Vector3 p, float r, float duration, string what)
        {
            int found = 0;
            switch (what)
            {
                case "enemy":
                case "human":
                case "all":
                    foreach (var c in Character.GetAllCharacters())
                    {
                        if (c == null || c == caster || c.IsDead() || Vector3.Distance(c.transform.position, p) > r)
                        {
                            continue;
                        }

                        bool human = c.IsPlayer() || Combat.IsCaster(c) || Combat.Prefab(c).Contains("Dverger") || Combat.Prefab(c).Contains("Goblin") ||
                                     Combat.Prefab(c).Contains("Charred");
                        if ((what == "enemy" && !Combat.IsEnemy(caster, c)) || (what == "human" && !human))
                        {
                            continue;
                        }

                        Beacon(c.transform, Combat.IsEnemy(caster, c) ? new Color(1f, 0.3f, 0.25f) : new Color(0.4f, 1f, 0.5f), duration, c.GetHeight() + 0.3f);
                        found++;
                    }

                    if (what == "all")
                    {
                        found += Things(p, r, duration, true, true);
                    }

                    break;
                case "trap":
                    foreach (var a in Anchor.All)
                    {
                        if (a != null && (a.Type == "trap" || a.Type == "firetrap" || a.Type == "spelltrap" || a.Type == "alarm") &&
                            Vector3.Distance(a.transform.position, p) <= r)
                        {
                            Beacon(a.transform, new Color(1f, 0.5f, 0.1f), duration, 0.2f);
                            found++;
                        }
                    }

                    break;
                case "ore":
                    found += Ores(p, r, duration);
                    break;
                case "rune":
                    foreach (var col in Physics.OverlapSphere(p, r))
                    {
                        var rs = col.GetComponentInParent<RuneStone>();
                        var vv = col.GetComponentInParent<Vegvisir>();
                        Transform tr = rs != null ? rs.transform : vv != null ? vv.transform : null;
                        if (tr != null && tr.Find("WZ_Beacon") == null)
                        {
                            Beacon(tr, new Color(0.5f, 0.8f, 1f), duration, 2f);
                            found++;
                        }
                    }

                    break;
                case "chest":
                    found += Things(p, r, duration, true, false);
                    break;
            }

            Fx.Send(FxKind.Burst, p + Vector3.up, Vector3.up, "revelio", Mathf.Min(r, 6f));
            Casting.Message(Texts.L("Обнаружено: ", "Revealed: ") + found);
        }

        private static int Things(Vector3 p, float r, float duration, bool chests, bool items)
        {
            int n = 0;
            var seen = new HashSet<Transform>();
            foreach (var col in Physics.OverlapSphere(p, r))
            {
                if (chests)
                {
                    var c = col.GetComponentInParent<Container>();
                    if (c != null && seen.Add(c.transform))
                    {
                        bool treasure = c.name.Contains("Treasure") || c.name.Contains("treasure") || c.name.Contains("Buried");
                        Beacon(c.transform, treasure ? new Color(1f, 0.8f, 0.2f) : new Color(0.9f, 0.75f, 0.4f), duration, 1f);
                        n++;
                    }
                }

                if (items)
                {
                    var it = col.GetComponentInParent<ItemDrop>();
                    if (it != null && seen.Add(it.transform))
                    {
                        Beacon(it.transform, new Color(0.8f, 0.8f, 1f), duration, 0.4f);
                        n++;
                    }
                }
            }

            return n;
        }

        private static int Ores(Vector3 p, float r, float duration)
        {
            string[] ores = { "copper", "tin", "silver", "iron", "obsidian", "flametal", "ore", "blackmarble", "meteor" };
            int n = 0;
            var seen = new HashSet<Transform>();
            foreach (var col in Physics.OverlapSphere(p, r))
            {
                Transform tr = null;
                var m5 = col.GetComponentInParent<MineRock5>();
                var m = col.GetComponentInParent<MineRock>();
                var d = col.GetComponentInParent<Destructible>();
                if (m5 != null) tr = m5.transform;
                else if (m != null) tr = m.transform;
                else if (d != null) tr = d.transform;
                if (tr == null || !seen.Add(tr))
                {
                    continue;
                }

                string name = tr.name.ToLowerInvariant();
                foreach (var o in ores)
                {
                    if (name.Contains(o))
                    {
                        Beacon(tr, new Color(1f, 0.6f, 0.2f), duration, 1.5f);
                        n++;
                        break;
                    }
                }
            }

            return n;
        }

        /// <summary>Appare Vestigium: every creature around leaves a glowing trail for a while.</summary>
        public static void Track(Player caster, float duration)
        {
            int n = 0;
            foreach (var c in Character.GetAllCharacters())
            {
                if (c == null || c == caster || c.IsDead() || Vector3.Distance(c.transform.position, caster.transform.position) > 120f)
                {
                    continue;
                }

                var trail = new GameObject("WZ_Trail");
                trail.transform.SetParent(c.transform, false);
                trail.transform.localPosition = Vector3.up * 0.1f;
                var tr = trail.AddComponent<TrailRenderer>();
                tr.time = 12f;
                tr.widthMultiplier = 0.25f;
                tr.sharedMaterial = Fx.Mat(Fx.Dot());
                var col = Combat.IsEnemy(caster, c) ? new Color(1f, 0.5f, 0.3f) : new Color(0.5f, 1f, 0.6f);
                tr.startColor = col;
                tr.endColor = new Color(col.r, col.g, col.b, 0f);
                Object.Destroy(trail, duration);
                n++;
            }

            Casting.Message(Texts.L("Следы проявлены: ", "Trails revealed: ") + n);
        }

        /// <summary>Minimap.AddPin through reflection: its last parameter's type lives in an assembly mods do not reference.</summary>
        private static Minimap.PinData AddPin(Minimap map, Vector3 pos, Minimap.PinType type, string label)
        {
            var m = HarmonyLib.AccessTools.Method(typeof(Minimap), "AddPin");
            if (m == null)
            {
                return null;
            }

            var ps = m.GetParameters();
            var args = new object[ps.Length];
            object[] given = { pos, type, label, false, false, 0L };
            for (int i = 0; i < ps.Length; i++)
            {
                args[i] = i < given.Length ? given[i] : ps[i].HasDefaultValue && ps[i].DefaultValue != null ? ps[i].DefaultValue :
                    ps[i].ParameterType.IsValueType ? System.Activator.CreateInstance(ps[i].ParameterType) : null;
            }

            return m.Invoke(map, args) as Minimap.PinData;
        }

        /// <summary>Homonculous Charm: the map around shows itself, and every creature in range gets a pin for a while.</summary>
        public static void Map(Player caster, float r, float duration)
        {
            var map = Minimap.instance;
            if (map == null)
            {
                return;
            }

            map.Explore(caster.transform.position, r * 3f);
            var pins = new List<Minimap.PinData>();
            foreach (var c in Character.GetAllCharacters())
            {
                if (c == null || c == caster || c.IsDead() || Vector3.Distance(c.transform.position, caster.transform.position) > r)
                {
                    continue;
                }

                string label = Localization.instance != null ? Localization.instance.Localize(c.m_name) : c.m_name;
                var pin = AddPin(map, c.transform.position, c.IsPlayer() ? Minimap.PinType.Player : Minimap.PinType.Icon3, label);
                if (pin != null)
                {
                    pins.Add(pin);
                }
            }

            Later.Run(Mathf.Min(duration, 120f), () =>
            {
                if (Minimap.instance == null)
                {
                    return;
                }

                foreach (var pin in pins)
                {
                    Minimap.instance.RemovePin(pin);
                }
            });
            Casting.Message(Texts.L("Карта ожила: отмечено ", "The map comes alive: marked ") + pins.Count);
        }
    }

    /// <summary>
    ///     What the wizard knows about each kind of creature: kills and studies make spells hit it harder
    ///     (2/4/6/8% at 10/25/50/100 kills, +3% once studied with Informous or Specialis Revelio).
    /// </summary>
    internal static class Bestiary
    {
        public static float DamageBonus(WizardState w, Character target)
        {
            if (w == null || target == null || target.IsPlayer())
            {
                return 0f;
            }

            string k = Combat.Prefab(target);
            int kills;
            w.Kills.TryGetValue(k, out kills);
            float b = kills >= 100 ? 8f : kills >= 50 ? 6f : kills >= 25 ? 4f : kills >= 10 ? 2f : 0f;
            if (w.Informs.ContainsKey(k))
            {
                b += 3f;
            }

            return b * (1f + w.Stats.Get(Stat.bestiary_xp) / 100f);
        }

        public static void Kill(WizardState w, Character target)
        {
            if (w == null || target == null || target.IsPlayer())
            {
                return;
            }

            string k = Combat.Prefab(target);
            int n;
            w.Kills.TryGetValue(k, out n);
            w.Kills[k] = ++n;
            if (n == 1 || n == 10 || n == 25 || n == 50 || n == 100)
            {
                string name = Localization.instance != null ? Localization.instance.Localize(target.m_name) : target.m_name;
                Casting.Message(Texts.L("Бестиарий: ", "Bestiary: ") + name + " — " + n + Texts.L(" побед", " kills"));
            }

            Wizard.Dirty = true;
        }

        public static void Inform(Player caster, WizardState w, Character t)
        {
            if (t == null)
            {
                return;
            }

            string k = Combat.Prefab(t);
            int n;
            w.Informs.TryGetValue(k, out n);
            w.Informs[k] = n + 1;
            Wizard.Dirty = true;
            var mods = t.GetDamageModifiers();
            var weak = new List<string>();
            var strong = new List<string>();
            Sort(mods.m_blunt, Texts.L("дробящий", "blunt"), weak, strong);
            Sort(mods.m_slash, Texts.L("режущий", "slash"), weak, strong);
            Sort(mods.m_pierce, Texts.L("колющий", "pierce"), weak, strong);
            Sort(mods.m_fire, Texts.L("огонь", "fire"), weak, strong);
            Sort(mods.m_frost, Texts.L("мороз", "frost"), weak, strong);
            Sort(mods.m_lightning, Texts.L("молния", "lightning"), weak, strong);
            Sort(mods.m_poison, Texts.L("яд", "poison"), weak, strong);
            Sort(mods.m_spirit, Texts.L("дух", "spirit"), weak, strong);
            string name = Localization.instance != null ? Localization.instance.Localize(t.m_name) : t.m_name;
            string text = name + " ★" + (t.GetLevel() - 1) + "  " + Mathf.RoundToInt(t.GetHealth()) + "/" + Mathf.RoundToInt(t.GetMaxHealth()) + " HP";
            if (weak.Count > 0) text += "\n" + Texts.L("Уязвим: ", "Weak to: ") + string.Join(", ", weak.ToArray());
            if (strong.Count > 0) text += "\n" + Texts.L("Стоек: ", "Resists: ") + string.Join(", ", strong.ToArray());
            int kills;
            w.Kills.TryGetValue(k, out kills);
            text += "\n" + Texts.L("Побед: ", "Kills: ") + kills + Texts.L(", бонус урона +", ", damage bonus +") + Mathf.RoundToInt(DamageBonus(w, t)) + "%";
            if (MessageHud.instance != null)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, text);
            }
        }

        private static void Sort(HitData.DamageModifier m, string name, List<string> weak, List<string> strong)
        {
            switch (m)
            {
                case HitData.DamageModifier.Weak:
                case HitData.DamageModifier.VeryWeak:
                    weak.Add(name);
                    break;
                case HitData.DamageModifier.Resistant:
                case HitData.DamageModifier.VeryResistant:
                case HitData.DamageModifier.Immune:
                    strong.Add(name);
                    break;
            }
        }
    }
}
