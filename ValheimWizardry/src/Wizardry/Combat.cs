using System.Collections.Generic;
using UnityEngine;

namespace Wizardry
{
    /// <summary>Spell damage: who may be hit, how much, of what kind, and what it does to trees, rocks and buildings.</summary>
    internal static class Combat
    {
        /// <summary>Set while the mod itself deals damage (curses ticking, thorns), so those hits do not chain.</summary>
        public static bool Internal;

        public static void SetDamage(ref HitData.DamageTypes d, string type, float amount)
        {
            switch (type)
            {
                case "blunt": d.m_blunt += amount; break;
                case "slash": d.m_slash += amount; break;
                case "pierce": d.m_pierce += amount; break;
                case "chop": d.m_chop += amount; break;
                case "pickaxe": d.m_pickaxe += amount; break;
                case "fire": d.m_fire += amount; break;
                case "frost": d.m_frost += amount; break;
                case "lightning": d.m_lightning += amount; break;
                case "poison": d.m_poison += amount; break;
                case "spirit": d.m_spirit += amount; break;
                default: d.m_damage += amount; break;
            }
        }

        /// <summary>Is <paramref name="c" /> someone the caster's hostile spells should touch?</summary>
        public static bool IsEnemy(Player caster, Character c)
        {
            if (c == null || c == caster || c.IsDead())
            {
                return false;
            }

            if (c.IsPlayer())
            {
                var p = (Player)c;
                return ModConfig.FriendlyFire.Value && caster != null && caster.IsPVPEnabled() && p.IsPVPEnabled();
            }

            if (c.IsTamed())
            {
                return false;
            }

            if (c.m_faction == Character.Faction.Players)
            {
                return false;
            }

            return true;
        }

        public static bool IsAlly(Player caster, Character c)
        {
            if (c == null || c.IsDead())
            {
                return false;
            }

            if (c == caster)
            {
                return true;
            }

            if (c.IsPlayer())
            {
                var p = (Player)c;
                return !(caster != null && caster.IsPVPEnabled() && p.IsPVPEnabled());
            }

            return c.IsTamed() || c.m_faction == Character.Faction.Players;
        }

        public static bool IsUndead(Character c)
        {
            string n = Prefab(c);
            return c.m_faction == Character.Faction.Undead || n.Contains("Skeleton") || n.Contains("Draugr") || n.Contains("Ghost") ||
                   n.Contains("Wraith") || n.Contains("Bonemass") || n.Contains("Charred") || n.Contains("Fader");
        }

        public static bool IsCaster(Character c)
        {
            string n = Prefab(c);
            return n.Contains("Shaman") || n.Contains("Mage") || n.Contains("Twitcher") || n.Contains("Warlock") || n.Contains("Cultist");
        }

        public static bool IsInsect(Character c)
        {
            string n = Prefab(c);
            return n.Contains("Seeker") || n.Contains("Deathsquito") || n.Contains("Tick") || n.Contains("Gjall") || n.Contains("Spider");
        }

        public static bool IsTroll(Character c)
        {
            return Prefab(c).Contains("Troll");
        }

        public static bool IsBeast(Character c)
        {
            string n = Prefab(c);
            return n.Contains("Wolf") || n.Contains("Boar") || n.Contains("Lox") || n.Contains("Deer") || n.Contains("Neck") ||
                   n.Contains("Asksvin") || n.Contains("Hare") || n.Contains("Bear") || n.Contains("Fenring") || n.Contains("Hen") ||
                   n.Contains("Chicken") || n.Contains("Drake") || n.Contains("Hatchling") || n.Contains("Serpent") || n.Contains("Leech");
        }

        public static bool IsSkeleton(Character c)
        {
            return Prefab(c).Contains("Skeleton");
        }

        public static string Prefab(Character c)
        {
            if (c == null)
            {
                return "";
            }

            var nv = c.m_nview;
            string n = c.gameObject.name;
            int p = n.IndexOf('(');
            return p > 0 ? n.Substring(0, p) : n;
        }

        /// <summary>
        ///     The hit of one spell on one creature: base damage × rank × wand × skill × the wizard's stats × the
        ///     spell's own multiplier, split between the spell's damage types.
        /// </summary>
        public static HitData BuildHit(Player caster, WizardState w, SpellDef spell, Character target, float mult, Vector3 point,
            Vector3 dir, out bool crit)
        {
            var hit = new HitData();
            crit = false;
            hit.m_point = point;
            hit.m_dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward;
            hit.m_hitType = HitData.HitType.PlayerHit;
            hit.m_skill = Plugin.SkillType;
            hit.m_skillRaiseAmount = 0f;
            hit.m_toolTier = (short)Mathf.Clamp(w.Year, 1, 8);
            hit.m_blockable = true;
            hit.m_dodgeable = true;
            hit.m_staggerMultiplier = 1f;
            if (caster != null)
            {
                hit.SetAttacker(caster);
            }

            float dmg = Power(caster, w, spell) * mult;
            var st = w.Stats;
            Cond now = Casting.Now(caster, w);
            float inc = st.Get(Stat.dmg, now);
            bool aoe = spell.Spec != null && (spell.Spec.Delivery == "nova" || spell.Spec.Delivery == "blast" || spell.Spec.Delivery == "cone" ||
                                              spell.Spec.P.ContainsKey("aoe"));
            inc += aoe ? st.Get(Stat.dmg_area, now) : st.Get(Stat.dmg_single, now);
            if (spell.IsDark)
            {
                inc += st.Get(Stat.dmg_dark, now) + DarkBonus(w);
            }

            if (target != null)
            {
                if (IsUndead(target)) inc += st.Get(Stat.dmg_undead, now);
                if (IsCaster(target)) inc += st.Get(Stat.dmg_casters, now);
                if (Effects.HasControl(target)) inc += st.Get(Stat.dmg_controlled, now);
                if (target.GetBaseAI() != null && !target.GetBaseAI().IsAlerted()) inc += st.Get(Stat.dmg_unaware, now);
                inc += Bestiary.DamageBonus(w, target);
            }

            dmg *= Mathf.Max(0f, 1f + inc / 100f);
            dmg *= ModConfig.DamageMultiplier.Value;
            if (target != null && target.IsPlayer())
            {
                dmg *= ModConfig.PvpDamage.Value;
            }

            // crits: 5% base, Arithmancy decides alone when taken
            float critChance = 5f * (1f + st.Get(Stat.crit) / 100f);
            if (st.Has("ks_arithmancy"))
            {
                crit = w.CastCount % 7 == 0;
            }
            else
            {
                crit = Random.value * 100f < critChance;
            }

            if (crit)
            {
                dmg *= 1.5f + st.Get(Stat.crit_mult) / 100f;
                if (st.Has("crit_restore") && caster != null)
                {
                    caster.AddEitr(4f);
                }
            }

            string[] types = spell.Damage.Length > 0 ? spell.Damage : new[] { "spirit" };
            float each = dmg / types.Length;
            foreach (string t in types)
            {
                float m = 1f;
                switch (t)
                {
                    case "fire": m += st.Get(Stat.dmg_fire, now) / 100f; break;
                    case "frost": m += st.Get(Stat.dmg_frost, now) / 100f; break;
                    case "lightning": m += st.Get(Stat.dmg_lightning, now) / 100f; break;
                    case "poison": m += st.Get(Stat.dmg_poison, now) / 100f; break;
                    case "spirit": m += st.Get(Stat.dmg_spirit, now) / 100f; break;
                    case "blunt": case "slash": case "pierce": m += st.Get(Stat.dmg_phys, now) / 100f; break;
                }

                if (st.Has("ks_elements") && (t == "fire" || t == "frost" || t == "lightning"))
                {
                    m *= Casting.ElementBalance(w, t);
                }

                SetDamage(ref hit.m_damage, t, each * Mathf.Max(0f, m));
                if (t == "chop" || t == "pickaxe")
                {
                    // tools only: the wizard still hurts creatures with the rest
                    SetDamage(ref hit.m_damage, "blunt", 0f);
                }
            }

            hit.m_staggerMultiplier = 1f + st.Get(Stat.stagger) / 100f;
            return hit;
        }

        /// <summary>Spell power before situational modifiers: base × rank × wand × skill.</summary>
        public static float Power(Player caster, WizardState w, SpellDef spell)
        {
            float rank = 1f + 0.12f * (w.Rank(spell.Id) - 1);
            float wand = Wands.Power(caster);
            float skill = 0.7f + 0.3f * Mathf.Clamp01((caster != null ? caster.GetSkillLevel(Plugin.SkillType) : 0f) / 100f);
            return spell.BaseDamage * rank * wand * skill;
        }

        public static float DarkBonus(WizardState w)
        {
            float d = w.Darkness;
            float bonus = d >= 100f ? 20f : d >= 75f ? 15f : d >= 50f ? 10f : d >= 25f ? 5f : 0f;
            if (w.Stats.Has("ks_obscurus") || w.Stats.Has("dark_lord"))
            {
                bonus *= 1.5f;
            }

            return bonus * (1f + w.Stats.Get(Stat.dark_bonus) / 100f);
        }

        /// <summary>Damages a creature; returns false if it could not be hit (a friend, already dead).</summary>
        public static bool Strike(Player caster, WizardState w, SpellDef spell, Character target, float mult, Vector3 point, Vector3 dir,
            float push, float lifestealPct = 0f)
        {
            if (target == null || target.IsDead())
            {
                return false;
            }

            bool crit;
            var hit = BuildHit(caster, w, spell, target, mult, point, dir, out crit);
            hit.m_pushForce = push;
            float before = target.GetHealth();
            target.Damage(hit);
            Fx.Send(FxKind.Hit, point, dir, spell.Id, crit ? 1f : 0f);
            float ls = w.Stats.Get(Stat.lifesteal) + lifestealPct;
            if (ls > 0f && caster != null)
            {
                caster.Heal(hit.GetTotalDamage() * ls / 100f, false);
            }

            Casting.OnSpellHit(caster, w, spell, target);
            return true;
        }

        /// <summary>Hits trees, rocks, ore and buildings in a radius with the spell's chop and pickaxe damage.</summary>
        public static void StrikeObjects(Player caster, WizardState w, SpellDef spell, Vector3 center, float radius, float mult)
        {
            var seen = new HashSet<IDestructible>();
            foreach (var col in Physics.OverlapSphere(center, Mathf.Max(0.5f, radius)))
            {
                var d = col.GetComponentInParent<IDestructible>();
                if (d == null || d is Character || !seen.Add(d))
                {
                    continue;
                }

                // buildings: only somebody else's, and only in PvP (no griefing your own base by accident)
                var wnt = d as WearNTear;
                if (wnt != null)
                {
                    var piece = wnt.GetComponent<Piece>();
                    long creator = piece != null ? piece.GetCreator() : 0L;
                    if (!ModConfig.FriendlyFire.Value || caster == null || !caster.IsPVPEnabled() || creator == 0L ||
                        creator == caster.GetPlayerID())
                    {
                        continue;
                    }
                }

                bool crit;
                var hit = BuildHit(caster, w, spell, null, mult, col.ClosestPoint(center), Vector3.down, out crit);
                float total = hit.GetTotalDamage();
                // tools: everything counts as chop for trees and pickaxe for stone and ore
                hit.m_damage.m_chop += total * 0.5f;
                hit.m_damage.m_pickaxe += total * 0.5f;
                hit.m_toolTier = (short)Mathf.Clamp(w.Year, 1, 8);
                d.Damage(hit);
            }
        }
    }
}
