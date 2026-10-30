using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wizardry
{
    /// <summary>
    ///     The spell bar, the keys, casting (cost, cast time, cooldowns, animations), channels, Focus, Darkness,
    ///     ranks and the skill. Runs on the local player's game.
    /// </summary>
    internal static class Casting
    {
        private sealed class Pending
        {
            public SpellDef Spell;
            public float Release, Start;
            public float Cost;
            public bool FromAttack;
        }

        private sealed class Channel
        {
            public CastContext Ctx;
            public float End, Next, Start, CostPerTick;
            public bool Hold;
        }

        private sealed class Scheduled
        {
            public float At;
            public CastContext Ctx;
        }

        private static readonly Dictionary<string, float> s_cooldowns = new Dictionary<string, float>();
        private static readonly List<Scheduled> s_scheduled = new List<Scheduled>();
        private static readonly List<string> s_elements = new List<string>();
        private static Pending s_cast;
        private static Channel s_chan;
        public static float ProtegoUntil, NextCastBonus;
        public static string ProtegoReflect;
        public static SpellDef ProtegoSpell;
        private static float s_lastCast = -10f, s_lastCombat = -100f, s_lastMove = -100f, s_stillSince, s_lastInvisible = -100f;
        private static Vector3 s_lastPos;
        private static int s_duel;
        private static float s_duelUntil;
        private static bool s_instantNext;
        private static int s_combo;
        private static float s_comboAt;
        private static float s_saveTimer, s_possessTimer;

        public static bool Busy { get { return s_cast != null || s_chan != null; } }

        /// <summary>The spell being cast or channelled and how far along it is (for the HUD).</summary>
        public static SpellDef Progress(out float fraction)
        {
            fraction = 0f;
            if (s_cast != null)
            {
                fraction = Mathf.Clamp01((Time.time - s_cast.Start) / Mathf.Max(0.05f, s_cast.Release - s_cast.Start));
                return s_cast.Spell;
            }

            if (s_chan != null)
            {
                fraction = 1f - Mathf.Clamp01((Time.time - s_chan.Start) / Mathf.Max(0.05f, s_chan.End - s_chan.Start));
                return s_chan.Ctx.Spell;
            }

            return null;
        }

        public static int DuelCharges { get { return Time.time < s_duelUntil ? s_duel : 0; } }

        // ------------------------------------------------------------------ situation
        public static Cond Now(Player p, WizardState w)
        {
            if (p == null)
            {
                return Cond.None;
            }

            Cond c = Cond.None;
            c |= EnvMan.IsNight() ? Cond.night : Cond.day;
            if (p.GetHealth() < p.GetMaxHealth() * 0.5f) c |= Cond.lowhp;
            var biome = p.GetCurrentBiome();
            if (biome == Heightmap.Biome.BlackForest || biome == Heightmap.Biome.Mistlands) c |= Cond.forest;
            if (p.InInterior()) c |= Cond.dungeon;
            if (Time.time - s_stillSince > 2f) c |= Cond.still;
            if (Time.time - s_lastMove < 3f) c |= Cond.aftermove;
            if (w != null && w.Darkness >= 50f) c |= Cond.dark50;
            if (Time.time - s_lastInvisible < 3f) c |= Cond.unseen;
            if (!p.IsOnGround()) c |= Cond.air;
            if (Time.time - s_lastCombat < 10f) c |= Cond.combat;
            var profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
            if (profile != null && profile.HaveCustomSpawnPoint() && Vector3.Distance(profile.GetCustomSpawnPoint(), p.transform.position) < 30f)
            {
                c |= Cond.home;
            }

            return c;
        }

        public static float ElementBalance(WizardState w, string element)
        {
            if (s_elements.Count == 0)
            {
                return 1f;
            }

            if (s_elements[s_elements.Count - 1] == element && s_elements.Count > 1)
            {
                return 0.85f;
            }

            var distinct = new HashSet<string>(s_elements);
            return 1f + 0.15f * distinct.Count;
        }

        // ------------------------------------------------------------------ every frame
        public static void Update(Player p, WizardState w)
        {
            float dt = Time.deltaTime;
            if ((p.transform.position - s_lastPos).sqrMagnitude > 0.01f)
            {
                s_stillSince = Time.time;
                s_lastPos = p.transform.position;
            }

            if (Buffs.Value(p, "invis") > 0f || Buffs.Value(p, "stealth") >= 75f)
            {
                s_lastInvisible = Time.time;
            }

            Keys(p, w);

            if (s_cast != null && Time.time >= s_cast.Release)
            {
                var c = s_cast;
                s_cast = null;
                Release(p, w, c);
            }

            if (s_chan != null)
            {
                TickChannel(p, w);
            }

            for (int i = s_scheduled.Count - 1; i >= 0; i--)
            {
                if (Time.time >= s_scheduled[i].At)
                {
                    var s = s_scheduled[i];
                    s_scheduled.RemoveAt(i);
                    try
                    {
                        s.Ctx.Origin = Wands.Tip(p);
                        SpellOps.Execute(s.Ctx);
                    }
                    catch (Exception e)
                    {
                        Plugin.Report("Repeat", e);
                    }
                }
            }

            // eitr: the wand's own trickle (Obscurus gets nothing on its own)
            if (Wands.Equipped(p) != null && !w.Stats.Has("ks_obscurus") && Time.time - s_lastCast > 1f)
            {
                p.AddEitr(ModConfig.EitrRegen.Value * (1f + w.Stats.Get(Stat.eitr_regen, Now(p, w)) / 100f) * dt);
            }

            // Focus fades out of combat, Darkness fades always
            if (Time.time - s_lastCombat > 10f && w.Focus > 0f)
            {
                w.Focus = Mathf.Max(0f, w.Focus - 5f * dt);
            }

            if (w.Darkness > 0f)
            {
                w.Darkness = Mathf.Max(0f, w.Darkness - dt / 60f * (1f + w.Stats.Get(Stat.dark_decay) / 100f));
            }

            if (Time.time > s_duelUntil)
            {
                s_duel = 0;
            }

            Possession(p, w, dt);

            // high-water mark of the skill: points never shrink when a death costs some skill
            int lvl = Mathf.FloorToInt(p.GetSkillLevel(Plugin.SkillType));
            if (lvl > w.SkillHigh)
            {
                w.SkillHigh = lvl;
                Wizard.Dirty = true;
                Message(Texts.L("Новое очко Древа Чар! Откройте Кодекс (K).", "A new point for the Tree of Charms! Open the grimoire (K)."));
            }

            s_saveTimer += dt;
            if (s_saveTimer > 5f)
            {
                s_saveTimer = 0f;
                Wizard.Save();
            }
        }

        private static void Keys(Player p, WizardState w)
        {
            if (!Wizard.CanUseKeys(p))
            {
                return;
            }

            if (ZInput.GetKeyDown(ModConfig.GrimoireKey.Value, false))
            {
                Grimoire.Toggle();
                return;
            }

            bool mod = ModConfig.SelectModifier.Value != KeyCode.None && ZInput.GetKey(ModConfig.SelectModifier.Value, false);
            if (mod)
            {
                for (int i = 0; i < 8; i++)
                {
                    if (ZInput.GetKeyDown(KeyCode.Alpha1 + i, false))
                    {
                        w.Selected = i;
                        Wizard.Dirty = true;
                    }
                }

                float wheel = ZInput.GetMouseScrollWheel();
                if (wheel > 0.01f) Select(w, -1);
                else if (wheel < -0.01f) Select(w, 1);
            }

            if (ModConfig.NextSpellKey.Value != KeyCode.None && ZInput.GetKeyDown(ModConfig.NextSpellKey.Value, false)) Select(w, 1);
            if (ModConfig.PrevSpellKey.Value != KeyCode.None && ZInput.GetKeyDown(ModConfig.PrevSpellKey.Value, false)) Select(w, -1);
            if (ZInput.GetKeyDown(ModConfig.GrandKey.Value, false) && w.Grand != null)
            {
                TryCast(p, w, w.Grand, false);
            }

            // wandless magic casts with the grand key's neighbour: the middle mouse button is the weapon's own
            if (w.Stats.Has("ks_wandless") && Wands.Equipped(p) == null && ZInput.GetKeyDown(KeyCode.Mouse3, false))
            {
                CastSelected(p, w, false);
            }
        }

        private static void Select(WizardState w, int step)
        {
            for (int k = 0; k < 8; k++)
            {
                w.Selected = (w.Selected + step + 8) % 8;
                if (w.Bar[w.Selected] != null)
                {
                    break;
                }
            }

            Wizard.Dirty = true;
        }

        public static void CastSelected(Player p, WizardState w, bool fromAttack)
        {
            string id = w.Bar[Mathf.Clamp(w.Selected, 0, 7)];
            TryCast(p, w, id ?? "basic_cast", fromAttack);
        }

        // ------------------------------------------------------------------ casting
        public static bool TryCast(Player p, WizardState w, string id, bool fromAttack)
        {
            if (s_cast != null || p.IsDead() || p.InPlaceMode())
            {
                return false;
            }

            if (s_chan != null)
            {
                s_chan = null;
                return false;
            }

            var s = SpellDb.Get(id);
            if (s == null || !s.Castable || s.Spec == null)
            {
                return false;
            }

            if (!w.KnowsSpell(id))
            {
                Message(Texts.L("Вы не знаете этого заклинания.", "You do not know this spell."));
                return false;
            }

            if (s.Year > w.Year)
            {
                Message(Texts.L("Это заклинание " + s.Year + " курса.", "That spell is for year " + s.Year + "."));
                return false;
            }

            float cd;
            if (s_cooldowns.TryGetValue(id, out cd) && cd > Time.time)
            {
                Message(s.Name + ": " + Mathf.CeilToInt(cd - Time.time) + Texts.L(" с", " s"));
                return false;
            }

            if (w.Stats.Has("ks_healer") && s.IsDark)
            {
                Message(Texts.L("Клятва целителя запрещает тёмные искусства.", "The Healer's Oath forbids the Dark Arts."));
                return false;
            }

            if (s.Id == "expecto_patronum" && w.Darkness >= 50f)
            {
                Message(Texts.L("Слишком много Тьмы в душе: Патронус не приходит.", "Too much Darkness in you: the Patronus will not come."));
                return false;
            }

            if (s.IsGrand)
            {
                float need = (s.Id == "phoenix_tears" && w.Stats.Has("phoenix_cheap")) || (s.Id == "morsmordre" && w.Stats.Has("morsmordre_cheap")) ? 50f : 100f;
                if (w.Focus < need)
                {
                    Message(Texts.L("Средоточие не заполнено.", "Focus is not full."));
                    return false;
                }
            }

            if (s.IsRitual && !Rituals.CanCast(p, w, s))
            {
                return false;
            }

            float cost = Cost(p, w, s);
            if (s.Spec.Delivery != "chan" && cost > 0f && !p.HaveEitr(cost))
            {
                SpellHud.FlashEitr();
                Message(Texts.L("Не хватает магической силы.", "Not enough eitr."));
                return false;
            }

            float castTime = CastTime(w, s);
            s_cast = new Pending { Spell = s, Release = Time.time + castTime, Start = Time.time, Cost = cost, FromAttack = fromAttack };
            Animate(p, s);
            var metas = w.Metas(s.Id);
            bool silent = metas.Contains("nonverbal") || s.Role == "basic" || s.Id == "lumos" || s.Id == "nox";
            if (!silent && !string.IsNullOrEmpty(s.Incantation))
            {
                Fx.Send(FxKind.Text, p.transform.position, Vector3.up, s.Id, 0f, s.Incantation);
            }

            if (!silent)
            {
                Fx.Send(FxKind.Cast, Wands.Tip(p), p.transform.forward, s.Id, 0f);
            }

            return true;
        }

        public static float Cost(Player p, WizardState w, SpellDef s)
        {
            if (s.Id == "basic_cast" || s.Id == "lumos" || s.Id == "nox" || s.Id == "protego")
            {
                return 0f;
            }

            if (w.Stats.Has("free_utility") && s.Arch == "util")
            {
                return 0f;
            }

            if ((w.Stats.Has("finite_free") && s.Id == "finite_incantatem") || (w.Stats.Has("keys_free") && (s.Id == "alohomora" || s.Id == "cistem_aperio")))
            {
                return 0f;
            }

            float cost = s.BaseCost * ModConfig.CostMultiplier.Value * (1f + 0.05f * (w.Rank(s.Id) - 1));
            var now = Now(p, w);
            float cheaper = w.Stats.Get(Stat.cost, now);
            if (s.Arch == "heal") cheaper += w.Stats.Get(Stat.cost, now | Cond.heal) - w.Stats.Get(Stat.cost, now);
            if (s.Arch == "summ") cheaper += w.Stats.Get(Stat.cost, now | Cond.summon) - w.Stats.Get(Stat.cost, now);
            cost *= Mathf.Max(0.1f, 1f - cheaper / 100f);
            if (s.Unforgivable) cost *= Mathf.Max(0.2f, 1f - w.Stats.Get(Stat.unforgivable_cost) / 100f);
            if (w.Stats.Has("half_blood") && (s.Id == "sectumsempra" || s.Id == "levicorpus" || s.Id == "muffliato")) cost *= 0.7f;
            if (w.Stats.Has("safe_apparate") && (s.Id == "apparition" || s.Id == "side_along")) cost *= 0.5f;
            if (w.Stats.Has("digger") && (s.Id == "defodio" || s.Id == "deprimo")) cost *= 0.5f;
            if (w.Stats.Has("incendio_cheap") && (s.Id.StartsWith("incendio") || s.Id.StartsWith("glacius"))) cost *= 0.7f;
            if (w.Stats.Has("ks_wandless") && Wands.Equipped(p) == null) cost *= 1.2f;
            if (w.Stats.Has("weather_cheap") && (s.Id == "impervius" || s.Id == "warming_charm" || s.Id == "cooling_charm")) cost *= 0.5f;
            foreach (var m in w.Metas(s.Id))
            {
                switch (m)
                {
                    case "meta_maxima": cost *= w.Stats.Has("maxima_cheap") ? 1.2f : 1.4f; break;
                    case "meta_duo": cost *= 1.6f; break;
                    case "shield_penetration": cost *= 1.25f; break;
                    case "meta_totalum": cost *= 1.5f; break;
                    case "magicus_extremos": cost *= 1.6f; break;
                    case "protean_charm": cost *= 1.5f; break;
                }
            }

            return cost * Patterns.CostMult(w, s);
        }

        private static float CastTime(WizardState w, SpellDef s)
        {
            if (s_instantNext && s.Role != "ritual")
            {
                s_instantNext = false;
                return 0.05f;
            }

            float t;
            switch (s.Arch)
            {
                case "proj": t = 0.3f; break;
                case "aoe": t = 0.55f; break;
                case "ctrl": t = 0.4f; break;
                case "buff": t = 0.35f; break;
                case "summ": t = 0.75f; break;
                case "heal": t = 0.4f; break;
                case "move": t = 0.1f; break;
                case "beam": t = 0.15f; break;
                case "ritual": t = 3f; break;
                default: t = 0.3f; break;
            }

            if (s.Id == "basic_cast") t = 0.2f;
            if (s.IsGrand) t = 1.2f;
            if (s.Anim == "snap") t = Mathf.Min(t, 0.12f);
            float speed = w.Stats.Get(Stat.cast_speed) + (s_duel > 0 && w.Stats.Has("ks_duel") ? 6f * s_duel : 0f);
            if (w.Metas(s.Id).Contains("nonverbal")) speed += 25f;
            return t / Mathf.Max(0.3f, 1f + speed / 100f);
        }

        private static void Animate(Player p, SpellDef s)
        {
            if (!ModConfig.Animations.Value || p.GetZAnim() == null)
            {
                return;
            }

            string trigger;
            switch (s.Anim)
            {
                case "slash": case "whip": trigger = "spear_throw"; break;
                case "point": case "figure": case "pull": trigger = "staff_rapidfire"; break;
                case "circle": case "touch": case "self": case "strain": trigger = "staff_shield"; break;
                case "push": trigger = "throw_bomb"; break;
                case "stab": case "raise": case "spiral": case "ritual": trigger = "staff_summon"; break;
                case "snap": case "turn": case "-": return;
                default: trigger = "staff_fireball"; break;
            }

            try
            {
                p.GetZAnim().SetTrigger(trigger);
            }
            catch (Exception)
            {
                // an animation the 1.0.21 player does not have: no motion, the spell still works
            }
        }

        private static void Release(Player p, WizardState w, Pending pc)
        {
            var s = pc.Spell;
            if (p.IsDead())
            {
                return;
            }

            if (s.Spec.Delivery != "chan" && pc.Cost > 0f)
            {
                if (!p.HaveEitr(pc.Cost))
                {
                    Message(Texts.L("Не хватает магической силы.", "Not enough eitr."));
                    return;
                }

                p.UseEitr(pc.Cost);
            }

            if (s.IsRitual && !Rituals.Pay(p, w, s))
            {
                return;
            }

            if (s.IsGrand)
            {
                float need = (s.Id == "phoenix_tears" && w.Stats.Has("phoenix_cheap")) || (s.Id == "morsmordre" && w.Stats.Has("morsmordre_cheap")) ? 50f : 100f;
                w.Focus = Mathf.Max(0f, w.Focus - need);
            }

            s_lastCast = Time.time;
            w.CastCount++;
            var metas = w.Metas(s.Id);
            var ctx = new CastContext
            {
                Caster = p, W = w, Spell = s, Spec = s.Spec, Origin = Wands.Tip(p), Metas = new HashSet<string>(metas),
                Patterns = Patterns.Active(w, s)
            };
            Vector3 end;
            ctx.AimChar = SpellOps.RayTarget(ctx, 60f, out end);
            ctx.AimPoint = end;
            ctx.Dir = (end - ctx.Origin).sqrMagnitude > 0.01f ? (end - ctx.Origin).normalized : p.transform.forward;
            ctx.Mult *= 1f + Wands.Affinity(p, s.School) / 100f + NextCastBonus;
            NextCastBonus = 0f;
            if (metas.Contains("magicus_extremos")) ctx.Mult *= 1.35f;
            if (metas.Contains("nonverbal") && !w.Stats.Has("nonverbal_free")) ctx.Mult *= 0.9f;
            if (metas.Contains("meta_maxima"))
            {
                ctx.Mult *= 1.5f;
                ctx.Area *= 1.5f;
            }

            if (metas.Contains("cascading_jinx") || s.Spec.Has("chain")) ctx.Cascade = true;
            if (s.Spec.Has("combo"))
            {
                s_combo = Time.time - s_comboAt < 4f ? s_combo + 1 : 1;
                s_comboAt = Time.time;
                if (s_combo % 3 == 0)
                {
                    ctx.Mult *= 1.6f;
                    Fx.Send(FxKind.Burst, p.transform.position + p.transform.forward * 3f, Vector3.up, s.Id, 3f);
                }
            }

            if (s.Spec.Harmful)
            {
                Effects.BreakInvisibility(p);
            }
            if (metas.Contains("homing_spell")) ctx.Homing = true;
            if (metas.Contains("shield_penetration")) ctx.ShieldPen = true;
            if (w.Stats.Has("ks_obscurus") && p.GetEitr() >= p.GetMaxEitr() - 0.5f) ctx.Mult *= 2f;
            if (w.Stats.Has("ks_duel"))
            {
                bool aoe = s.Spec.Delivery == "nova" || s.Spec.Delivery == "blast" || s.Spec.Delivery == "cone";
                ctx.Mult *= aoe ? 0.7f : 1f + 0.06f * s_duel;
            }

            if (w.Stats.Has("core_wild") && Wands.CoreKey(Wands.Equipped(p)) == "dragon" && UnityEngine.Random.value < 0.03f)
            {
                Schedule(ctx, 0.35f, 1f);
            }

            // costs that are not eitr
            var hpOp = s.Spec.Get("hp");
            if (hpOp != null)
            {
                float hp = p.GetMaxHealth() * hpOp.F(0, 10f) / 100f * (w.Rank(s.Id) >= 6 ? 0.5f : 1f);
                var hit = new HitData();
                hit.m_damage.m_damage = hp;
                Combat.Internal = true;
                try
                {
                    p.Damage(hit);
                }
                finally
                {
                    Combat.Internal = false;
                }
            }

            var darkOp = s.Spec.Get("dark");
            if (darkOp != null)
            {
                float gain = darkOp.F(0, 5f) * ModConfig.DarknessGain.Value * Mathf.Max(0f, 1f + w.Stats.Get(Stat.dark_gain) / 100f);
                w.Darkness = Mathf.Min(100f, w.Darkness + gain);
            }

            if (s.Id == "expecto_patronum")
            {
                w.Darkness = Mathf.Max(0f, w.Darkness - 10f);
            }

            // cooldown
            float cdMult = ModConfig.CooldownMultiplier.Value * Mathf.Max(0.2f, 1f - w.Stats.Get(Stat.cdr) / 100f);
            if (s.Arch == "move") cdMult *= Mathf.Max(0.2f, 1f - w.Stats.Get(Stat.cdr_move) / 100f);
            if (w.Stats.Has("utility_cdr") && s.Arch == "util") cdMult *= 0.5f;
            float cdTime = s.BaseCooldown * cdMult * Patterns.CooldownMult(w, s);
            if (metas.Contains("meta_maxima")) cdTime *= 1.3f;
            s_cooldowns[s.Id] = Time.time + cdTime;
            if (s.Arch == "move")
            {
                s_lastMove = Time.time;
            }

            // elements for the Balance keystone
            if (s.Damage.Length > 0 && (s.Damage[0] == "fire" || s.Damage[0] == "frost" || s.Damage[0] == "lightning"))
            {
                s_elements.Add(s.Damage[0]);
                if (s_elements.Count > 3)
                {
                    s_elements.RemoveAt(0);
                }
            }

            // growth: the skill and the spell's rank
            p.RaiseSkill(Plugin.SkillType, Mathf.Max(0.2f, pc.Cost / 20f) * ModConfig.SkillGainMultiplier.Value * (1f + w.Stats.Get(Stat.skill_xp) / 100f));
            AddXp(p, w, s, Mathf.Max(4f, pc.Cost));
            w.HousePoints++;

            if (metas.Contains("meta_delayed") && (s.Spec.Delivery == "bolt" || s.Spec.Delivery == "blast" || s.Spec.Delivery == "nova"))
            {
                Anchor.Spawn(p, w, s, "spelltrap", SpellOps.GroundAt(ctx, 25f), 2.5f, 600f);
                return;
            }

            try
            {
                Patterns.Apply(ctx);
                SpellOps.Execute(ctx);
                Patterns.After(ctx);
            }
            catch (Exception e)
            {
                Plugin.Report("Spell " + s.Id, e);
            }

            if (ctx.Patterns.Contains("a_wave") && s.Spec.Delivery != "chan") Schedule(ctx, 1f, 0.6f);

            if (metas.Contains("meta_duo")) Schedule(ctx, 0.4f, 0.7f);
            if (metas.Contains("prior_incantato") && UnityEngine.Random.value < 0.2f) Schedule(ctx, 0.5f, 0.9f);
            if (metas.Contains("meta_totalum") && (s.Spec.Delivery == "nova" || s.Spec.Delivery == "blast"))
            {
                Schedule(ctx, 2f, 0.5f);
                Schedule(ctx, 4f, 0.5f);
            }

            if (w.Stats.Has("stupefy_split") && s.Id == "stupefy")
            {
                var left = Copy(ctx);
                left.Dir = Quaternion.AngleAxis(-12f, Vector3.up) * ctx.Dir;
                left.Mult *= 0.6f;
                var right = Copy(ctx);
                right.Dir = Quaternion.AngleAxis(12f, Vector3.up) * ctx.Dir;
                right.Mult *= 0.6f;
                Schedule(left, 0.01f, 1f);
                Schedule(right, 0.01f, 1f);
            }

            Wizard.Dirty = true;
        }

        private static CastContext Copy(CastContext c)
        {
            return new CastContext
            {
                Caster = c.Caster, W = c.W, Spell = c.Spell, Spec = c.Spec, Origin = c.Origin, Dir = c.Dir, AimPoint = c.AimPoint,
                AimChar = c.AimChar, Mult = c.Mult, Area = c.Area, Nonverbal = c.Nonverbal, ShieldPen = c.ShieldPen, Homing = c.Homing,
                Cascade = c.Cascade, Metas = c.Metas, ExtraBolts = c.ExtraBolts, ExtraPierce = c.ExtraPierce, SpeedMult = c.SpeedMult,
                DurMult = c.DurMult, Patterns = c.Patterns
            };
        }

        private static void Schedule(CastContext c, float delay, float mult)
        {
            var copy = Copy(c);
            copy.Mult *= mult;
            copy.Metas = new HashSet<string>(); // repeats do not repeat themselves
            s_scheduled.Add(new Scheduled { At = Time.time + delay, Ctx = copy });
        }

        // ------------------------------------------------------------------ channels
        public static void StartChannel(CastContext c, float max, float range)
        {
            s_chan = new Channel
            {
                Ctx = c, Start = Time.time, End = Time.time + max, Next = Time.time,
                CostPerTick = Cost(c.Caster, c.W, c.Spell) / 4f * (c.Patterns.Contains("c_cost") ? 0.6f : 1f), Hold = ZInput.GetButton("Attack")
            };
        }

        private static void TickChannel(Player p, WizardState w)
        {
            var ch = s_chan;
            if (Time.time > ch.End || p.IsDead() || (ch.Hold && !ZInput.GetButton("Attack") && Time.time - ch.Start > 0.3f))
            {
                s_chan = null;
                return;
            }

            if (Time.time < ch.Next)
            {
                return;
            }

            ch.Next = Time.time + 0.25f;
            if (ch.CostPerTick > 0f)
            {
                if (!p.HaveEitr(ch.CostPerTick))
                {
                    s_chan = null;
                    SpellHud.FlashEitr();
                    return;
                }

                p.UseEitr(ch.CostPerTick);
            }

            var c = ch.Ctx;
            c.Origin = Wands.Tip(p);
            Vector3 end;
            float range = c.Spec.Param("rng", 15f) * c.SpeedMult;
            var target = SpellOps.RayTarget(c, range, out end);
            c.Dir = (end - c.Origin).normalized;
            var ramp = c.Spec.Get("ramp");
            c.TickMult = ramp != null ? 1f + (ramp.F(0, 3f) - 1f) * Mathf.Clamp01((Time.time - ch.Start) / (ch.End - ch.Start)) : 1f;
            if (c.Patterns.Contains("c_ramp"))
            {
                c.TickMult *= 1f + 0.6f * Mathf.Clamp01((Time.time - ch.Start) / (ch.End - ch.Start));
            }
            AddXp(p, w, c.Spell, ch.CostPerTick);
            Fx.Send(FxKind.Beam, c.Origin, end - c.Origin, c.Spell.Id, 0.3f);
            if (c.Spec.Extra.Contains("cone"))
            {
                foreach (var t in Character.GetAllCharacters())
                {
                    if (t == p)
                    {
                        continue;
                    }

                    Vector3 d = t.GetCenterPoint() - c.Origin;
                    if (d.magnitude < range && Vector3.Angle(c.Dir, d) < 30f)
                    {
                        SpellOps.OnCharacter(c, t, t.GetCenterPoint(), d.normalized);
                    }
                }

                return;
            }

            if (target != null)
            {
                SpellOps.OnCharacter(c, target, end, c.Dir);
                if (c.Patterns.Contains("c_split"))
                {
                    var second = SpellOps.NearestEnemy(c, target.transform.position, 6f);
                    if (second != null && second != target)
                    {
                        var half = Copy(c);
                        half.Mult *= 0.5f;
                        half.TickMult = c.TickMult;
                        Fx.Send(FxKind.Beam, target.GetCenterPoint(), second.GetCenterPoint() - target.GetCenterPoint(), c.Spell.Id, 0.3f);
                        SpellOps.OnCharacter(half, second, second.GetCenterPoint(), (second.transform.position - target.transform.position).normalized);
                    }
                }
            }
            else if (c.Spec.Has("heal") && c.Spec.Has("ally"))
            {
                SpellOps.OnAlly(c, p);
            }

            if (c.Spec.Has("terrain") && ModConfig.TerrainSpells.Value)
            {
                Utility.Terrain(end, c.Spec.Get("terrain").S(0, "dig:2"));
                Combat.StrikeObjects(p, w, c.Spell, end, 1.2f, 1f);
            }

            if (c.Spec.Has("vanish"))
            {
                Utility.Vanish(p, end, 1.5f);
            }

            if (c.Spec.Has("extinguish"))
            {
                Utility.Extinguish(p, end, 2f);
            }
        }

        // ------------------------------------------------------------------ growth
        public static void AddXp(Player p, WizardState w, SpellDef s, float amount)
        {
            int before = w.Rank(s.Id);
            float xp;
            w.Xp.TryGetValue(s.Id, out xp);
            xp += amount * ModConfig.RankXpMultiplier.Value * (1f + w.Stats.Get(Stat.rank_xp) / 100f);
            w.Xp[s.Id] = xp;
            int after = w.Rank(s.Id);
            if (after > before)
            {
                string extra = after == 3 || after == 6 || after == 9
                    ? Texts.L(" — выберите узор (K).", " — choose a pattern (K).")
                    : after == 2 || after == 5 || after == 8 ? Texts.L(" — открыт слот метамагии.", " — a metamagic slot opened.") : "";
                Message(s.Name + Texts.L(": ранг ", ": rank ") + after + extra);
            }
        }

        public static void OnSpellHit(Player p, WizardState w, SpellDef s, Character target)
        {
            if (p == null || w == null)
            {
                return;
            }

            s_lastCombat = Time.time;
            w.Focus = Mathf.Min(100f, w.Focus + 2f * (1f + w.Stats.Get(Stat.focus) / 100f));
            AddXp(p, w, s, Mathf.Max(2f, s.BaseCost * 0.5f));
            bool single = s.Spec != null && (s.Spec.Delivery == "bolt" || s.Spec.Delivery == "ray" || s.Spec.Delivery == "touch");
            if (single && w.Stats.Has("ks_duel"))
            {
                s_duel = Mathf.Min(5, s_duel + 1);
                s_duelUntil = Time.time + 6f;
            }

            if (target != null && target.GetHealth() <= 0f)
            {
                OnKill(p, w, s, target);
            }
        }

        public static void OnKill(Player p, WizardState w, SpellDef s, Character target)
        {
            w.Focus = Mathf.Min(100f, w.Focus + 5f);
            if (s != null && s.IsDark && w.Stats.Has("kill_restore"))
            {
                p.AddEitr(8f);
            }

            if (w.Stats.Has("poly_restore") && Effects.Has(target, "poly"))
            {
                p.AddEitr(10f);
            }

            Bestiary.Kill(w, target);
        }

        public static void OnHeal(Player p, WizardState w)
        {
            w.Focus = Mathf.Min(100f, w.Focus + 3f);
            s_lastCombat = Mathf.Max(s_lastCombat, Time.time - 8f);
        }

        public static void OnCombat()
        {
            s_lastCombat = Time.time;
        }

        public static void OnParry(Player p, WizardState w)
        {
            if (w.Stats.Has("instant_after_parry"))
            {
                s_instantNext = true;
            }

            if (w.Stats.Has("parry_restore"))
            {
                p.AddEitr(10f);
                s_cooldowns.Remove("expelliarmus");
            }
        }

        // ------------------------------------------------------------------ the rest
        private static void Possession(Player p, WizardState w, float dt)
        {
            if (w.Darkness < 100f || !ModConfig.PossessionSpawns.Value || !EnvMan.IsNight() || w.Stats.Has("dread_name"))
            {
                return;
            }

            s_possessTimer += dt;
            if (s_possessTimer < 300f)
            {
                return;
            }

            s_possessTimer = 0f;
            var wraith = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("Wraith") : null;
            if (wraith != null)
            {
                Vector3 pos = p.transform.position + Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward * 25f;
                UnityEngine.Object.Instantiate(wraith, pos + Vector3.up * 2f, Quaternion.identity);
                Message(Texts.L("Тьма зовёт: за вами пришли призраки.", "The Darkness calls: wraiths have come for you."));
            }
        }

        public static void ToggleLumos(Player p, bool on)
        {
            var zdo = p.Nv() != null ? p.Nv().GetZDO() : null;
            if (zdo != null)
            {
                zdo.Set("wz_lumos", on);
            }
        }

        public static void CycleColour(WizardState w)
        {
            string[] palette = { null, "#ffd27a", "#7ad7ff", "#9dff8a", "#ff7ab8", "#c59bff", "#ffffff" };
            int i = Array.IndexOf(palette, w.Colour);
            w.Colour = palette[(i + 1) % palette.Length];
            Wizard.Dirty = true;
            Message(Texts.L("Цвет Люмоса изменён.", "Lumos colour changed."));
        }

        public static float Cooldown(string id)
        {
            float cd;
            return s_cooldowns.TryGetValue(id, out cd) ? Mathf.Max(0f, cd - Time.time) : 0f;
        }

        public static void Message(string text)
        {
            if (MessageHud.instance != null)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, text);
            }
        }

        public static void Reset()
        {
            s_cast = null;
            s_chan = null;
            s_scheduled.Clear();
            s_cooldowns.Clear();
        }
    }
}
