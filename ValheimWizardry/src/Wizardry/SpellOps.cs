using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wizardry
{
    /// <summary>One cast in progress: who, what, where, and what the metamagic changed.</summary>
    internal sealed class CastContext
    {
        public Player Caster;
        public WizardState W;
        public SpellDef Spell;
        public FxSpec Spec;
        public Vector3 Origin, Dir, AimPoint;
        public Character AimChar;
        public float Mult = 1f, Area = 1f;
        public bool Nonverbal, ShieldPen, Homing, Cascade;
        public int Depth;
        public HashSet<string> Metas = new HashSet<string>();
        public float TickMult = 1f;
        public int ExtraBolts, ExtraPierce;
        public float SpeedMult = 1f, DurMult = 1f;
        public HashSet<string> Patterns = new HashSet<string>();
    }

    /// <summary>
    ///     Runs a spell's line from tools/spell_fx.txt: the delivery finds who and where, the operations do the rest.
    ///     Everything here runs in the caster's game; other games learn about it through statuses (sent to the owner
    ///     of each creature), anchors (network objects) and effect messages.
    /// </summary>
    internal static class SpellOps
    {
        private static readonly int s_charMask = LayerMask.GetMask("character", "character_net", "character_ghost", "hitbox", "character_noenv");

        // ------------------------------------------------------------------ delivery
        public static void Execute(CastContext c)
        {
            var prev = Patterns.Current;
            Patterns.Current = c;
            try
            {
                Deliver(c);
            }
            finally
            {
                Patterns.Current = prev;
            }
        }

        private static void Deliver(CastContext c)
        {
            var spec = c.Spec;
            float r = spec.Param("r", 4f) * c.Area * (1f + c.W.Stats.Get(Stat.area) / 100f);
            float range = spec.Param("rng", 30f) * (1f + c.W.Stats.Get(Stat.range) / 100f);
            switch (spec.Delivery)
            {
                case "bolt":
                    Bolts(c);
                    break;
                case "ray":
                {
                    Vector3 end;
                    var t = RayTarget(c, range, out end);
                    Fx.Send(FxKind.Ray, c.Origin, end - c.Origin, c.Spell.Id, 0f);
                    if (t != null)
                    {
                        OnCharacter(c, t, end, c.Dir);
                    }

                    OnPoint(c, end, t);
                    break;
                }
                case "chan":
                    Casting.StartChannel(c, spec.Param("max", 3f), range);
                    break;
                case "cone":
                    Cone(c, spec.Param("r", 6f) * c.Area, spec.Param("ang", 45f));
                    OnPoint(c, c.Caster.transform.position + c.Dir * 2f, null);
                    break;
                case "nova":
                    Fx.Send(FxKind.Burst, c.Caster.transform.position + Vector3.up * 0.3f, Vector3.up, c.Spell.Id, r);
                    Area(c, c.Caster.transform.position, r, true);
                    OnPoint(c, c.Caster.transform.position, null);
                    break;
                case "blast":
                {
                    Vector3 p = GroundAt(c, range);
                    Fx.Send(FxKind.Burst, p + Vector3.up * 0.3f, Vector3.up, c.Spell.Id, r);
                    Area(c, p, r, false);
                    OnPoint(c, p, null);
                    if (spec.Has("structure") || spec.Has("terrain"))
                    {
                        Combat.StrikeObjects(c.Caster, c.W, c.Spell, p, r, Op(c, "structure", 0, 1f));
                    }

                    break;
                }
                case "ally":
                {
                    Vector3 end;
                    var t = RayTarget(c, spec.Param("rng", 20f), out end);
                    if (t == null || !Combat.IsAlly(c.Caster, t))
                    {
                        t = c.Caster;
                    }

                    OnAlly(c, t);
                    break;
                }
                case "touch":
                {
                    Vector3 end;
                    var t = RayTarget(c, spec.Param("rng", 4f), out end);
                    if (t != null)
                    {
                        OnCharacter(c, t, end, c.Dir);
                    }
                    else if (spec.Has("heal"))
                    {
                        OnAlly(c, c.Caster);
                    }

                    break;
                }
                case "place":
                case "ritual":
                {
                    Vector3 p = spec.Delivery == "ritual" ? c.Caster.transform.position : GroundAt(c, range);
                    OnPoint(c, p, null);
                    break;
                }
                case "global":
                    OnPoint(c, c.Caster.transform.position, null);
                    break;
                default: // self
                    OnSelf(c);
                    if (spec.Extra.Contains("cone"))
                    {
                        Cone(c, spec.Param("r", 6f) * c.Area, spec.Param("ang", 40f));
                    }

                    break;
            }

            if (spec.Delivery == "cone" && spec.Extra.Contains("self"))
            {
                OnSelf(c);
            }

            if (spec.Has("deflect"))
            {
                Utility.Deflect(c.Caster, c.Caster.transform.position + c.Dir * 3f, spec.Param("r", 6f) * c.Area);
            }
        }

        private static void Bolts(CastContext c)
        {
            var spec = c.Spec;
            int n = Mathf.Max(1, (int)spec.Param("n", 1f)) + c.ExtraBolts;
            var arrows = spec.Get("arrows");
            if (arrows != null)
            {
                // a volley: many conjured arrows share one and a half spells' worth of damage
                n = Mathf.Max(2, (int)arrows.F(0, 10f)) + c.ExtraBolts;
                c = Clone(c);
                c.Mult *= 1.5f / n;
            }

            float spread = spec.Param("spr", n > 1 ? (arrows != null ? 10f : 15f) : 0f);
            if (c.ExtraBolts > 0 && spread < 8f) spread = 8f;
            float speed = spec.Param("spd", 40f) * (1f + c.W.Stats.Get(Stat.proj_speed) / 100f) * c.SpeedMult;
            int pierce = (int)spec.Param("pierce", 0f) + c.ExtraPierce;
            string extra = spec.Has("arc") ? "arc" : "";
            bool homing = spec.Has("homing") || c.Homing;
            Character homingTarget = homing ? NearestEnemy(c, c.AimPoint, 15f) : null;
            float delay = spec.Param("delay", 0f);
            float aoe = spec.Param("aoe", 0f) * c.Area;
            var color = Fx.SpellColor(c.Spell);
            for (int i = 0; i < n; i++)
            {
                float ang = n > 1 ? Mathf.Lerp(-spread, spread, i / (n - 1f)) : 0f;
                Vector3 dir = Quaternion.AngleAxis(ang, Vector3.up) * c.Dir;
                Fx.Send(FxKind.Bolt, c.Origin, dir, c.Spell.Id, speed, extra);
                var p = Projectile.Launch(c.Origin, dir, speed, color, c.Spell, c.Caster, (target, point, d) =>
                {
                    if (aoe > 0f || delay > 0f)
                    {
                        Fx.Send(FxKind.Burst, point, Vector3.up, c.Spell.Id, Mathf.Max(1.5f, aoe));
                        Area(c, point, Mathf.Max(1.5f, aoe), false, delay);
                        OnPoint(c, point, null);
                        return;
                    }

                    if (target != null)
                    {
                        OnCharacter(c, target, point, d);
                    }
                    else
                    {
                        Fx.Send(FxKind.Hit, point, d, c.Spell.Id, 0f);
                    }

                    if (c.Spec.Has("structure") || c.Spec.Has("chop"))
                    {
                        Combat.StrikeObjects(c.Caster, c.W, c.Spell, point, 0.8f, Op(c, "structure", 0, 1f));
                    }

                    OnPoint(c, point, target);
                }, extra);
                p.Pierce = pierce;
                p.Homing = homingTarget;
                p.Range = c.Spec.Param("rng", 50f) * c.SpeedMult;
            }
        }

        private static void Cone(CastContext c, float r, float ang)
        {
            Vector3 o = c.Caster.transform.position + Vector3.up;
            Fx.Send(FxKind.Cone, c.Origin, c.Dir, c.Spell.Id, r);
            foreach (var t in Character.GetAllCharacters())
            {
                if (t == c.Caster)
                {
                    continue;
                }

                Vector3 d = t.GetCenterPoint() - o;
                if (d.magnitude <= r + t.GetRadius() && Vector3.Angle(c.Dir, d) <= ang)
                {
                    OnCharacter(c, t, t.GetCenterPoint(), d.normalized);
                }
            }
        }

        public static void Area(CastContext c, Vector3 center, float r, bool fromCaster, float delay = 0f)
        {
            var list = new List<Character>();
            foreach (var t in Character.GetAllCharacters())
            {
                if (t == null || t.IsDead())
                {
                    continue;
                }

                if (Vector3.Distance(t.transform.position, center) <= r + t.GetRadius())
                {
                    list.Add(t);
                }
            }

            foreach (var t in list)
            {
                if (t == c.Caster)
                {
                    if (HelpOps(c.Spec))
                    {
                        OnAlly(c, t);
                    }

                    continue;
                }

                Vector3 dir = (t.transform.position - center);
                dir.y = 0f;
                OnCharacter(c, t, t.GetCenterPoint(), dir.sqrMagnitude > 0.01f ? dir.normalized : c.Dir);
            }
        }

        // ------------------------------------------------------------------ per character
        private static bool HelpOps(FxSpec s)
        {
            return s.Has("heal") || s.Has("buff") || s.Has("cleanse") || s.Has("revive") || s.Has("stam") || s.Has("immune") ||
                   s.Has("rested") || s.Has("allies") || s.Has("painswap");
        }

        public static void OnCharacter(CastContext c, Character t, Vector3 point, Vector3 dir)
        {
            var prev = Patterns.Current;
            Patterns.Current = c;
            try
            {
                OnCharacterIn(c, t, point, dir);
            }
            finally
            {
                Patterns.Current = prev;
            }
        }

        private static void OnCharacterIn(CastContext c, Character t, Vector3 point, Vector3 dir)
        {
            if (t == null || t.IsDead())
            {
                return;
            }

            if (Combat.IsAlly(c.Caster, t))
            {
                if (HelpOps(c.Spec))
                {
                    OnAlly(c, t);
                }

                return;
            }

            if (!Combat.IsEnemy(c.Caster, t))
            {
                return;
            }

            if (t.IsPlayer() && c.Spell.Unforgivable && !ModConfig.UnforgivablesPvp.Value)
            {
                return;
            }

            Harm(c, t, point, dir);
            if (c.Patterns.Contains("x_vuln") && !t.IsDead())
            {
                Effects.Apply(c.Caster, c.W, t, "vuln", 4f, 15f);
            }

            if (c.Cascade && c.Depth == 0)
            {
                int jumps = 0;
                foreach (var other in Character.GetAllCharacters())
                {
                    if (jumps >= 2)
                    {
                        break;
                    }

                    if (other == t || other == c.Caster || !Combat.IsEnemy(c.Caster, other) ||
                        Vector3.Distance(other.transform.position, t.transform.position) > 8f)
                    {
                        continue;
                    }

                    jumps++;
                    var c2 = Clone(c);
                    c2.Depth = 1;
                    c2.Mult *= 0.65f;
                    Fx.Send(FxKind.Beam, t.GetCenterPoint(), other.GetCenterPoint() - t.GetCenterPoint(), c.Spell.Id, 0.25f);
                    Harm(c2, other, other.GetCenterPoint(), (other.transform.position - t.transform.position).normalized);
                }
            }
        }

        private static CastContext Clone(CastContext c)
        {
            return new CastContext
            {
                Caster = c.Caster, W = c.W, Spell = c.Spell, Spec = c.Spec, Origin = c.Origin, Dir = c.Dir, AimPoint = c.AimPoint,
                AimChar = c.AimChar, Mult = c.Mult, Area = c.Area, Nonverbal = c.Nonverbal, ShieldPen = c.ShieldPen, Homing = c.Homing,
                Cascade = c.Cascade, Depth = c.Depth, Metas = c.Metas, TickMult = c.TickMult, ExtraBolts = c.ExtraBolts,
                ExtraPierce = c.ExtraPierce, SpeedMult = c.SpeedMult, DurMult = c.DurMult, Patterns = c.Patterns
            };
        }

        private static float Op(CastContext c, string name, int i, float def)
        {
            var o = c.Spec.Get(name);
            return o != null ? o.F(i, def) : def;
        }

        /// <summary>Everything a hostile spell does to one enemy.</summary>
        private static void Harm(CastContext c, Character t, Vector3 point, Vector3 dir)
        {
            var spec = c.Spec;
            var w = c.W;
            var caster = c.Caster;
            if (spec.Has("weakonly") && (t.IsBoss() || t.GetMaxHealth() > 400f * w.Year))
            {
                // too big to lift or freeze: only the slow
                var so = spec.Get("slow");
                if (so != null)
                {
                    Effects.Apply(caster, w, t, "slow", so.F(1, 3f), so.F(0, 40f));
                }

                return;
            }

            if (spec.Has("beastonly") && !Combat.IsBeast(t))
            {
                return;
            }

            if (spec.Has("living") && Combat.IsUndead(t))
            {
                return;
            }

            // --- damage
            float mult = c.Mult * c.TickMult;
            var dmgOp = spec.Get("dmg");
            bool hasDamage = dmgOp != null || (c.Spell.Damage.Length > 0 && spec.Delivery != "self" && !spec.Has("summon") && !spec.Has("zone"));
            if (dmgOp != null)
            {
                mult *= dmgOp.F(0, 1f);
            }

            if (spec.Has("undead") && Combat.IsUndead(t)) mult *= Op(c, "undead", 0, 2f);
            if (spec.Has("insect") && Combat.IsInsect(t)) mult *= Op(c, "insect", 0, 2.5f);
            if (spec.Has("skeleton") && Combat.IsSkeleton(t)) mult *= Op(c, "skeleton", 0, 5f);
            if (spec.Has("vulnstun") && Effects.HasControl(t)) mult *= 1f + Op(c, "vulnstun", 0, 30f) / 100f;
            if (spec.Has("wetbonus") && t.GetSEMan() != null && t.GetSEMan().HaveStatusEffect("Wet".GetStableHashCode())) mult *= 1f + Op(c, "wetbonus", 0, 40f) / 100f;
            var bonus = spec.Get("bonus");
            if (bonus != null)
            {
                string kind = bonus.S(0, "");
                string[] kv = kind.Split(':');
                float bm = kv.Length > 1 ? bonus.F(0, 2f) : 2f;
                float parsed;
                if (kv.Length > 1 && float.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed))
                {
                    bm = parsed;
                }

                if ((kv[0] == "fire" && (Combat.Prefab(t).Contains("Surtling") || t.m_tolerateFire)) ||
                    (kv[0] == "frost" && (Combat.Prefab(t).Contains("Ice") || Combat.Prefab(t).Contains("Frost"))))
                {
                    mult *= bm;
                }
            }

            var exec = spec.Get("exec");
            bool executed = false;
            if (exec != null && !t.IsBoss())
            {
                float threshold = exec.F(0, 35f) + (w.Stats.Has("avada_plus") ? 10f : 0f);
                if (t.GetMaxHealth() < 60f * w.Year)
                {
                    threshold = Mathf.Max(threshold, 60f);
                }

                if (t.GetHealth() / t.GetMaxHealth() * 100f <= threshold)
                {
                    var kill = new HitData();
                    kill.m_damage.m_damage = t.GetHealth() * 3f + 100f;
                    kill.m_point = point;
                    kill.m_dir = dir;
                    kill.m_hitType = HitData.HitType.PlayerHit;
                    kill.SetAttacker(caster);
                    t.Damage(kill);
                    Fx.Send(FxKind.Hit, point, dir, c.Spell.Id, 1f);
                    executed = true;
                    Casting.OnSpellHit(caster, w, c.Spell, t);
                }
            }

            if (!executed && t.IsBoss() && spec.Has("bosspct"))
            {
                var hit = new HitData();
                hit.m_damage.m_spirit = t.GetHealth() * Op(c, "bosspct", 0, 15f) / 100f;
                hit.m_point = point;
                hit.m_hitType = HitData.HitType.PlayerHit;
                hit.SetAttacker(caster);
                t.Damage(hit);
                executed = true;
            }

            float push = Op(c, "push", 0, 0f) * c.Area;
            if (hasDamage && !executed)
            {
                if (spec.Has("troll") && Combat.IsTroll(t))
                {
                    Effects.Apply(caster, w, t, "stun", Op(c, "troll", 0, 3f), 0f, "petrify");
                }

                Combat.Strike(caster, w, c.Spell, t, mult, point, dir, push * 3f);
                if (spec.Has("stagger"))
                {
                    t.Stagger(dir);
                }
            }

            // --- movement
            if (push > 0f && !hasDamage)
            {
                Net.Impulse(t, dir * push + Vector3.up * push * 0.25f);
            }
            else if (push < 0f)
            {
                Net.Impulse(t, -dir * push);
            }

            float pull = Op(c, "pull", 0, 0f);
            if (pull > 0f && !t.IsBoss())
            {
                Vector3 to = (c.Caster.transform.position - t.transform.position);
                Net.Impulse(t, to.normalized * Mathf.Min(pull * 1.5f, to.magnitude * 2f) + Vector3.up * 2f);
            }

            float lift = Op(c, "lift", 0, 0f);
            if (lift > 0f && !t.IsBoss())
            {
                Net.Impulse(t, Vector3.up * lift * 1.6f);
            }

            float slam = Op(c, "slam", 0, 0f);
            if (slam > 0f)
            {
                Net.Impulse(t, Vector3.down * 25f);
                Effects.Apply(caster, w, t, "stun", slam, 0f, "slam");
                if (t.IsFlying() || !t.IsOnGround())
                {
                    Combat.Strike(caster, w, c.Spell, t, mult * 1.5f, point, Vector3.down, 0f);
                }
            }

            // --- statuses
            foreach (var o in spec.Ops)
            {
                switch (o.Name)
                {
                    case "slow": Effects.Apply(caster, w, t, "slow", o.F(1, 3f), o.F(0, 40f)); break;
                    case "root": Effects.Apply(caster, w, t, "root", o.F(0, 2f), 0f, spec.Has("ropes") ? "ropes" : ""); break;
                    case "stun":
                    {
                        string[] parts = o.S(0, "2").Split(':');
                        float dur;
                        float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dur);
                        Effects.Apply(caster, w, t, spec.Has("lift") ? "lift" : "stun", Mathf.Max(0.3f, dur), lift, parts.Length > 1 ? parts[1] : "");
                        break;
                    }
                    case "freeze": Effects.Apply(caster, w, t, "stun", o.F(0, 1.5f), 0f, "freeze"); break;
                    case "blind": Effects.Apply(caster, w, t, "blind", o.F(0, 3f) * (spec.Has("big") ? 1f : 1f), 0f); break;
                    case "silence": Effects.Apply(caster, w, t, "silence", o.F(0, 4f), 0f); break;
                    case "confuse": Effects.Apply(caster, w, t, "confuse", o.F(0, 4f), 0f, o.S(1, "")); break;
                    case "fear": Effects.Apply(caster, w, t, "fear", o.F(0, 3f), 0f); break;
                    case "charm": Effects.Apply(caster, w, t, "charm", o.F(0, 10f) * (w.Stats.Has("puppet_master") ? 1.3f : 1f), 0f, o.S(1, "")); break;
                    case "poly": Polymorph.Cast(caster, w, t, o.S(0, "Hen"), o.F(1, 6f), spec.Has("firebomb")); break;
                    case "vuln": Effects.Apply(caster, w, t, "vuln", o.F(1, 4f), o.F(0, 20f)); break;
                    case "healred": Effects.Apply(caster, w, t, "vuln", 6f, 10f); break;
                    case "weak": Effects.Apply(caster, w, t, "weak", o.F(1, 4f), o.F(0, 30f)); break;
                    case "shred": Effects.Apply(caster, w, t, "shred", o.F(1, 6f), o.F(0, 25f)); break;
                    case "dot":
                        Effects.Apply(caster, w, t, "dot", o.F(2, 4f), o.F(1, 4f) * Combat.Power(caster, w, c.Spell) / 12f *
                                                                        (1f + w.Stats.Get(Stat.dmg_dot) / 100f), "", o.S(0, "spirit"));
                        break;
                    case "bleed":
                        Effects.Apply(caster, w, t, "bleed", o.F(1, 6f), o.F(0, 5f) * Combat.Power(caster, w, c.Spell) / 12f *
                                                                         (1f + w.Stats.Get(Stat.dmg_dot) / 100f));
                        break;
                    case "forget": Effects.Apply(caster, w, t, "forget", o.F(0, 6f), 0f); break;
                    case "mark": Effects.Apply(caster, w, t, "mark", o.F(0, 10f), 0f); Reveal.Mark(t, o.F(0, 10f)); break;
                    case "deaf": Effects.Apply(caster, w, t, "deaf", o.F(0, 8f), 0f); break;
                    case "thorns": Effects.Apply(caster, w, t, "thorns", o.F(1, 8f), o.F(0, 5f)); break;
                    case "disarm": Effects.Apply(caster, w, t, "disarm", o.F(0, 4f), 100f); break;
                    case "shrink": Effects.Apply(caster, w, t, "shrink", o.F(0, 8f), Op(c, "weak", 0, 35f)); break;
                    case "interrupt": t.Stagger(dir); break;
                    case "crit": Effects.Apply(caster, w, t, "vuln", 6f, o.F(0, 25f) * 0.5f); break; // Legilimens: you see the blow coming
                    case "foresee": Reveal.Mark(t, 6f); break;
                    case "stonedmg": Effects.Apply(caster, w, t, "shred", 3f, 40f); break; // stone breaks: +40% physical damage
                    case "nocall": break; // the silenced cannot call for help: silence already stops them
                    case "taunttarget":
                        foreach (var other in Character.GetAllCharacters())
                        {
                            if (other != t && !other.IsPlayer() && Vector3.Distance(other.transform.position, t.transform.position) < 12f &&
                                Combat.IsEnemy(caster, other))
                            {
                                Effects.ApplyTaunt(caster, other, t, o.F(0, 6f));
                            }
                        }

                        break;
                    case "turn": Effects.Apply(caster, w, t, "stun", o.F(0, 2f), 0f, "turn"); Net.Impulse(t, Vector3.zero); break;
                    case "revert": Effects.Apply(caster, w, t, "root", 0.5f, 0f); Net.Impulse(t, -dir * 8f); break;
                    case "calm": Effects.Apply(caster, w, t, "charm", o.F(0, 30f), 0f, "calm"); break;
                    case "tame": Tame(t, o.F(0, 20f) * (1f + w.Stats.Get(Stat.tame_speed) / 100f)); break;
                    case "info": Bestiary.Inform(caster, w, t); break;
                    case "light": break; // the beam is the light
                    case "wet": Wet(t); break;
                    case "extinguish": Net.Cleanse(t, "burn"); break;
                    case "ignite": if (!hasDamage) Effects.Apply(caster, w, t, "dot", 3f, 4f, "", "fire"); break;
                    case "chill":
                        if (Effects.Has(t, "slow"))
                        {
                            Effects.Apply(caster, w, t, "stun", 1.5f, 0f, "freeze");
                        }

                        break;
                    case "shatter":
                        if (Effects.Has(t, "stun"))
                        {
                            Combat.Strike(caster, w, c.Spell, t, mult * 0.5f, point, dir, 0f);
                        }

                        break;
                    case "randomprank": RandomPrank(c, t, point, dir); break;
                    case "scare":
                        if (Combat.IsBeast(t))
                        {
                            Effects.Apply(caster, w, t, "fear", o.F(0, 5f) > 5f ? 5f : 3f, 0f);
                        }

                        break;
                    case "fishdrop":
                        if (UnityEngine.Random.value * 100f < o.F(0, 5f))
                        {
                            Drop("Fish1", t.transform.position + Vector3.up);
                        }

                        break;
                    case "antlers": Effects.Apply(caster, w, t, "weak", 6f, 30f, "antlers"); break;
                    case "untarget": Effects.Apply(caster, w, t, "forget", 4f, 0f); break;
                    case "invuln": break;
                }
            }

            if (c.ShieldPen)
            {
                Net.Cleanse(t, "buffs");
            }

            // a status counts as a hit for focus and rank even without damage
            if (!hasDamage && !executed)
            {
                Casting.OnSpellHit(caster, w, c.Spell, t);
            }
        }

        /// <summary>Everything a helpful spell does to a friend (or the caster).</summary>
        public static void OnAlly(CastContext c, Character t)
        {
            var prev = Patterns.Current;
            Patterns.Current = c;
            try
            {
                OnAllyIn(c, t);
            }
            finally
            {
                Patterns.Current = prev;
            }
        }

        private static void OnAllyIn(CastContext c, Character t)
        {
            var spec = c.Spec;
            var w = c.W;
            var caster = c.Caster;
            float power = Combat.Power(caster, w, c.Spell) * c.Mult;
            foreach (var o in spec.Ops)
            {
                switch (o.Name)
                {
                    case "heal":
                    {
                        float amount = power * o.F(0, 1f) * (1f + w.Stats.Get(Stat.heal, Casting.Now(caster, w) | Cond.heal) / 100f);
                        if (w.Stats.Has("triage") && t.GetHealth() / t.GetMaxHealth() < 0.35f) amount *= 1.4f;
                        if (w.Stats.Has("ks_healer")) amount *= 1.5f;
                        if (w.Stats.Has("second_wind") && t == caster && t.GetHealth() / t.GetMaxHealth() < 0.25f) amount *= 2f;
                        if (!t.IsPlayer() && w.Stats.Has("heal_tamed")) amount *= 2f;
                        float missing = t.GetMaxHealth() - t.GetHealth();
                        t.Heal(amount, true);
                        if ((w.Stats.Has("overheal_shield") || w.Stats.Has("ks_healer")) && amount > missing)
                        {
                            Effects.Apply(caster, w, t, "buff", 6f, 0f, null, null, "armor:" + Mathf.RoundToInt(Mathf.Min(amount - missing, t.GetMaxHealth() * 0.2f)), "WZ_buff");
                        }

                        if (w.Stats.Has("heal_cleanse") || w.Stats.Has("heal_cleanse_poison")) Net.Cleanse(t, "poison,burn");
                        if (w.Stats.Has("heal_calm")) Net.Cleanse(t, "confuse,fear");
                        if (w.Stats.Has("heal_haste")) Effects.Apply(caster, w, t, "buff", 5f, 0f, null, null, "speed:20", "WZ_buff");
                        if (w.Stats.Has("heal_armor")) Effects.Apply(caster, w, t, "buff", 5f, 0f, null, null, "armor:10", "WZ_buff");
                        if (w.Stats.Has("heal_warm")) Effects.Apply(caster, w, t, "buff", 60f, 0f, null, null, "coldimm:1", "WZ_buff");
                        if (w.Stats.Has("warm_hands") && t != caster) caster.Heal(amount * 0.25f, false);
                        if (w.Stats.Has("heal_aoe") && c.Depth == 0)
                        {
                            foreach (var other in Character.GetAllCharacters())
                            {
                                if (other != t && Combat.IsAlly(caster, other) && Vector3.Distance(other.transform.position, t.transform.position) < 4f)
                                {
                                    other.Heal(amount * 0.5f, true);
                                }
                            }
                        }

                        Fx.Send(FxKind.Heal, t.transform.position, Vector3.up, c.Spell.Id, amount);
                        Casting.OnHeal(caster, w);
                        break;
                    }
                    case "cleanse": Net.Cleanse(t, string.Join(",", o.Args)); break;
                    case "revive": Net.Cleanse(t, "stun,root,poly,lift,slow,confuse,fear,ctrl"); break;
                    case "stam":
                        var pl = t as Player;
                        if (pl != null) pl.AddStamina(o.F(0, 30f));
                        break;
                    case "immune": Effects.Apply(caster, w, t, "immune", o.F(0, 3f), 0f); break;
                    case "buff": Buff(c, t, o); break;
                    case "rested": Rested(t, o.F(0, 300f)); break;
                    case "painswap":
                        // takes half of the friend's recent hurt: the friend heals it, the caster pays it and gets stronger
                        float h = Mathf.Min(t.GetMaxHealth() * 0.25f, t.GetMaxHealth() - t.GetHealth()) * 0.5f;
                        t.Heal(h, true);
                        var hit = new HitData();
                        hit.m_damage.m_damage = h * 0.5f;
                        Combat.Internal = true;
                        try
                        {
                            caster.Damage(hit);
                        }
                        finally
                        {
                            Combat.Internal = false;
                        }

                        Casting.NextCastBonus = 0.3f;
                        break;
                }
            }

            if (spec.Has("protego"))
            {
                Casting.ProtegoUntil = Time.time + 0.5f;
                Casting.ProtegoReflect = spec.Get("reflect") != null ? spec.Get("reflect").S(0, "nearest") : null;
                Casting.ProtegoSpell = c.Spell;
            }
        }

        /// <summary>A buff: "buff=armor:15;speed:20@30" — values before @, seconds after.</summary>
        private static void Buff(CastContext c, Character t, FxOp o)
        {
            string raw = string.Join(",", o.Args);
            string values = raw;
            float dur = 30f;
            int at = raw.IndexOf('@');
            if (at >= 0)
            {
                values = raw.Substring(0, at);
                float.TryParse(raw.Substring(at + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dur);
            }

            float mult = 1f + 0.08f * (c.W.Rank(c.Spell.Id) - 1);
            if (c.W.Stats.Has("weather_cheap") && (c.Spell.Id == "impervius" || c.Spell.Id == "warming_charm" || c.Spell.Id == "cooling_charm"))
            {
                dur *= 2f;
            }

            if (c.W.Stats.Has("elemental_buffs"))
            {
                values += ";dmg:5";
            }

            Effects.Apply(c.Caster, c.W, t, "buff", dur * mult, 0f, null, null, values, "WZ_buff_" + c.Spell.Id);
            Fx.Send(FxKind.Buff, t.transform.position, Vector3.up, c.Spell.Id, 0f);
            if (c.Metas.Contains("protean_charm") && t == c.Caster)
            {
                foreach (var other in Character.GetAllCharacters())
                {
                    if (other != t && other.IsPlayer() && Combat.IsAlly(c.Caster, other) && Vector3.Distance(other.transform.position, t.transform.position) < 20f)
                    {
                        Effects.Apply(c.Caster, c.W, other, "buff", dur * mult, 0f, null, null, values, "WZ_buff_" + c.Spell.Id);
                        break;
                    }
                }
            }
        }

        private static void Rested(Character t, float seconds)
        {
            var seman = t.GetSEMan();
            var se = seman != null ? seman.GetStatusEffect("Rested".GetStableHashCode()) : null;
            if (se != null)
            {
                se.m_ttl += seconds;
            }
            else if (seman != null && t == Player.m_localPlayer)
            {
                var added = seman.AddStatusEffect("Rested".GetStableHashCode(), true);
                if (added != null)
                {
                    added.m_ttl = seconds;
                }
            }
        }

        private static void Wet(Character t)
        {
            if (t.Nv() != null && t.Nv().IsOwner() && t.GetSEMan() != null)
            {
                t.GetSEMan().AddStatusEffect("Wet".GetStableHashCode(), true);
            }
        }

        private static void Tame(Character t, float seconds)
        {
            var tame = t.GetComponent<Tameable>();
            if (tame == null)
            {
                return;
            }

            if (Wizard.State != null && Wizard.State.Stats.Get(Stat.tame_speed) >= 200f && t.GetHealth() / t.GetMaxHealth() < 0.25f &&
                t.Nv() != null && t.Nv().IsOwner())
            {
                tame.TameNow();
                return;
            }

            if (t.Nv() != null && t.Nv().IsOwner())
            {
                tame.DecreaseTameTime(seconds);
            }
        }

        private static void RandomPrank(CastContext c, Character t, Vector3 point, Vector3 dir)
        {
            var pranks = new List<SpellDef>();
            foreach (var s in SpellDb.All)
            {
                if (s.Role == "prank" && s.Id != "mutatio_skullus" && s.Spec != null && s.Spec.Delivery == "bolt")
                {
                    pranks.Add(s);
                }
            }

            if (pranks.Count == 0)
            {
                return;
            }

            var pick = pranks[UnityEngine.Random.Range(0, pranks.Count)];
            var c2 = Clone(c);
            c2.Spell = pick;
            c2.Spec = pick.Spec;
            c2.Depth = 1;
            Harm(c2, t, point, dir);
        }

        public static void Drop(string prefab, Vector3 pos)
        {
            var go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null;
            if (go != null)
            {
                UnityEngine.Object.Instantiate(go, pos, Quaternion.identity);
            }
        }

        // ------------------------------------------------------------------ the place it lands
        private static void OnPoint(CastContext c, Vector3 p, Character hitChar)
        {
            var spec = c.Spec;
            var w = c.W;
            var caster = c.Caster;
            foreach (var o in spec.Ops)
            {
                switch (o.Name)
                {
                    case "zone": Anchor.Spawn(caster, w, c.Spell, o.S(0, "ward"), p, o.F(1, 4f) * c.Area, o.F(2, 10f)); break;
                    case "dome": Anchor.Spawn(caster, w, c.Spell, "dome_" + o.S(2, "shield"), p, o.F(0, 10f) * c.Area * (1f + w.Stats.Get(Stat.ward_radius) / 100f), o.F(1, 30f)); break;
                    case "wall": Anchor.Spawn(caster, w, c.Spell, "wall", p, o.F(0, 6f), o.F(1, 15f), c.Dir); break;
                    case "wallfront":
                        if (c.W.Rank(c.Spell.Id) >= 6)
                        {
                            Anchor.Spawn(caster, w, c.Spell, "wall", caster.transform.position + c.Dir * 3f, 4f, 10f, c.Dir);
                        }

                        break;
                    case "bridge": Anchor.Spawn(caster, w, c.Spell, "bridge", caster.transform.position + Vector3.up * 0.1f, o.F(0, 20f), o.F(1, 60f), c.Dir); break;
                    case "tent": Anchor.Spawn(caster, w, c.Spell, "tent", p, 3f, o.F(0, 1800f)); break;
                    case "orb": Anchor.Spawn(caster, w, c.Spell, "orb", p + Vector3.up * 3f, 20f, o.F(0, 120f)); break;
                    case "flame": Anchor.Spawn(caster, w, c.Spell, "flame", p, 4f, o.F(0, 600f)); break;
                    case "sign": Anchor.Spawn(caster, w, c.Spell, "sign", p + Vector3.up * 1.5f, 1f, o.F(0, 600f), c.Dir); break;
                    case "summon": Summons.Spawn(c, p, o); break;
                    case "decoy": Summons.Decoy(c, p, (int)o.F(0, 1f), o.F(1, 10f)); break;
                    case "grow": Utility.Grow(p, Mathf.Max(o.F(0, 4f), 3f), w); break;
                    case "repair": Utility.Repair(caster, p, o.F(0, 6f) * (w.Stats.Has("repair_plus") ? 2f : 1f)); break;
                    case "fortify": Anchor.Spawn(caster, w, c.Spell, "fortify", p, o.F(0, 8f), o.F(1, 60f)); break;
                    case "terrain":
                        if (ModConfig.TerrainSpells.Value)
                        {
                            Utility.Terrain(p, o.S(0, "dig:2"));
                        }

                        break;
                    case "accio": Utility.Accio(caster, p, o.F(0, 20f), spec.Delivery == "ray"); break;
                    case "vacuum": Utility.Accio(caster, caster.transform.position, o.F(0, 8f), false); break;
                    case "ignite": Utility.Ignite(p, o.F(0, 1f)); break;
                    case "extinguish": Utility.Extinguish(caster, p, o.F(0, 4f)); break;
                    case "weather":
                        if (ModConfig.WeatherSpells.Value)
                        {
                            Weather.Cast(caster, o.S(0, "Clear"), o.F(1, 180f));
                        }

                        break;
                    case "reveal": Reveal.Area(caster, p, o.F(0, 20f), o.F(1, 8f), o.S(2, "all")); break;
                    case "track": Reveal.Track(caster, o.F(0, 120f)); break;
                    case "map": Reveal.Map(caster, o.F(0, 100f), o.F(1, 600f)); break;
                    case "flare": Fx.Send(FxKind.Flare, caster.transform.position, Vector3.up, c.Spell.Id, 0f, o.S(0, "red")); Utility.Ping(caster); break;
                    case "ping": Utility.Ping(caster); break;
                    case "darkmark": Fx.Send(FxKind.DarkMark, caster.transform.position, Vector3.up, c.Spell.Id, o.F(0, 60f)); break;
                    case "fearall":
                        foreach (var t in Character.GetAllCharacters())
                        {
                            if (Combat.IsEnemy(caster, t) && !t.IsBoss() && Vector3.Distance(t.transform.position, p) < o.F(0, 30f))
                            {
                                Effects.Apply(caster, w, t, "fear", o.F(1, 4f), 0f);
                            }
                        }

                        break;
                    case "door": Utility.Door(caster, p, o.S(0, "open"), o.F(1, 60f), spec.Has("heavy")); break;
                    case "chest": Utility.Chest(caster, p); break;
                    case "stick": Utility.Stick(caster, p); break;
                    case "stickpiece": Utility.Stick(caster, p); break;
                    case "levitateobj": if (hitChar == null) Utility.LevitateObject(caster, p, o.F(0, 20f)); break;
                    case "vanish": Utility.Vanish(caster, p, spec.Delivery == "nova" ? 6f : 2f); break;
                    case "dispelsummons": Utility.DispelSummons(caster, p, spec.Delivery == "nova" ? 6f : 3f); break;
                    case "dispel":
                        foreach (var t in Character.GetAllCharacters())
                        {
                            if (Vector3.Distance(t.transform.position, p) > spec.Param("r", 6f) * c.Area)
                            {
                                continue;
                            }

                            if (Combat.IsEnemy(caster, t))
                            {
                                Net.Cleanse(t, "buffs");
                            }
                            else if (Combat.IsAlly(caster, t))
                            {
                                Net.Cleanse(t, "ctrl");
                            }
                        }

                        break;
                    case "unpoly":
                        foreach (var t in Character.GetAllCharacters())
                        {
                            if (Vector3.Distance(t.transform.position, p) < 6f)
                            {
                                Net.Cleanse(t, "poly");
                            }
                        }

                        break;
                    case "call": Utility.CallTames(caster, o.F(0, 60f)); break;
                    case "phoenix": Fx.Send(FxKind.Ceremony, p, Vector3.up, c.Spell.Id, 0f, "phoenix"); break;
                }
            }
        }

        // ------------------------------------------------------------------ on the caster
        private static void OnSelf(CastContext c)
        {
            var spec = c.Spec;
            var w = c.W;
            var caster = c.Caster;
            foreach (var o in spec.Ops)
            {
                switch (o.Name)
                {
                    case "lumos": Casting.ToggleLumos(caster, true); break;
                    case "nox": Casting.ToggleLumos(caster, false); break;
                    case "blink": Mobility.Blink(caster, w, c, o.F(0, 15f), spec.Has("sidealong")); break;
                    case "leap": Mobility.Leap(caster, o.F(0, 8f)); break;
                    case "grapple": Mobility.Grapple(caster, c, o.F(0, 25f)); break;
                    case "flight": Mobility.Flight(caster, o.F(0, 8f) * (1f + 0.05f * (w.Rank(c.Spell.Id) - 1))); break;
                    case "rewind": Mobility.Rewind(caster, o.F(0, 5f)); break;
                    case "carry": Utility.Carry(caster, o.S(0, "chest"), o.F(1, 120f)); break;
                    case "pack": Utility.Pack(caster); break;
                    case "colour": Casting.CycleColour(w); break;
                    case "barber": Utility.Barber(caster); break;
                    case "refill": Utility.Refill(caster); break;
                    case "sendhome": Utility.SendHome(caster); break;
                    case "row": Ships.Row(caster, o.F(0, 60f) * (1f + w.Stats.Get(Stat.dur) / 100f)); break;
                    case "wind": Ships.Wind(caster, o.F(0, 20f) * (w.Stats.Has("wind_long") ? 1.5f : 1f)); break;
                    case "pointme": Utility.PointMe(caster); break;
                    case "tempus": Utility.Tempus(caster); break;
                    case "msg": Utility.Message(caster, c.Spell); break;
                    case "bubbles": Fx.Send(FxKind.Buff, caster.transform.position, Vector3.up, c.Spell.Id, 0f); break;
                    case "hidemap": Utility.HideMap(caster, o.F(0, 3600f)); break;
                    case "portkey": Rituals.Portkey(caster, w); break;
                    case "cabinet": Rituals.Cabinet(caster, w); break;
                    case "chestrow": Rituals.ChestRow(caster, w); break;
                    case "swap": Mobility.Swap(caster, c); break;
                    case "focus": break;
                    case "decoy": Summons.Decoy(c, caster.transform.position, (int)o.F(0, 2f), o.F(1, 10f)); break;
                    case "untaunt":
                        foreach (var t in Character.GetAllCharacters())
                        {
                            var ai = t.GetBaseAI() as MonsterAI;
                            if (ai != null && ai.GetTargetCreature() == caster && Vector3.Distance(t.transform.position, caster.transform.position) < 30f)
                            {
                                Effects.Apply(caster, w, t, "forget", o.F(0, 5f), 0f);
                            }
                        }

                        break;
                    case "taunt":
                        foreach (var t in Character.GetAllCharacters())
                        {
                            if (Combat.IsEnemy(caster, t) && Vector3.Distance(t.transform.position, caster.transform.position) < spec.Param("r", 15f))
                            {
                                Effects.ApplyTaunt(caster, t, caster, o.F(0, 5f));
                            }
                        }

                        break;
                }
            }

            if (HelpOps(spec))
            {
                OnAlly(c, caster);
            }

            if (spec.Has("protego"))
            {
                Casting.ProtegoUntil = Time.time + 0.5f;
                Casting.ProtegoReflect = spec.Get("reflect") != null ? spec.Get("reflect").S(0, "nearest") : null;
                Casting.ProtegoSpell = c.Spell;
            }
        }

        // ------------------------------------------------------------------ aiming
        public static Character RayTarget(CastContext c, float range, out Vector3 end)
        {
            var cam = GameCamera.instance != null ? GameCamera.instance.transform : null;
            Vector3 from = cam != null ? cam.position : c.Origin;
            Vector3 dir = cam != null ? cam.forward : c.Dir;
            float extra = cam != null ? Vector3.Distance(cam.position, c.Caster.transform.position) : 0f;
            RaycastHit hit;
            foreach (var h in Physics.SphereCastAll(from, 0.35f, dir, range + extra, Projectile.Mask, QueryTriggerInteraction.Ignore))
            {
                var ch = h.collider.GetComponentInParent<Character>();
                if (ch != null && ch != c.Caster && !ch.IsDead())
                {
                    end = h.point;
                    return ch;
                }
            }

            if (Physics.Raycast(from, dir, out hit, range + extra, Projectile.Mask, QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
                return null;
            }

            end = from + dir * (range + extra);
            return null;
        }

        public static Vector3 GroundAt(CastContext c, float range)
        {
            Vector3 end;
            RayTarget(c, range, out end);
            float d = Vector3.Distance(c.Caster.transform.position, end);
            if (d > range)
            {
                end = c.Caster.transform.position + (end - c.Caster.transform.position).normalized * range;
            }

            RaycastHit hit;
            if (Physics.Raycast(end + Vector3.up * 3f, Vector3.down, out hit, 50f, Projectile.Mask & ~s_charMask, QueryTriggerInteraction.Ignore))
            {
                return hit.point;
            }

            return end;
        }

        public static Character NearestEnemy(CastContext c, Vector3 p, float range)
        {
            Character best = null;
            float bd = range;
            foreach (var t in Character.GetAllCharacters())
            {
                if (!Combat.IsEnemy(c.Caster, t))
                {
                    continue;
                }

                float d = Vector3.Distance(t.transform.position, p);
                if (d < bd)
                {
                    bd = d;
                    best = t;
                }
            }

            return best;
        }
    }
}
