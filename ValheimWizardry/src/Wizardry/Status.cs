using System.Collections.Generic;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace Wizardry
{
    /// <summary>What a status does to whoever has it. Many spells share one kind with their own look.</summary>
    internal enum StatusKind
    {
        Slow, Root, Stun, Blind, Silence, Confuse, Fear, Charm, Poly, Vuln, Weak, Shred, Dot, Bleed, Lift, Forget, Taunt,
        Deaf, Thorns, Mark, Disarm, Shrink, Immune, Buff, Hidden, Ward
    }

    internal sealed class StatusDef
    {
        public string Key, Ru, En;
        public StatusKind Kind;
        public bool Disables; // the AI stops (stun, sleep, poly...)
        public Color Tint;
    }

    internal static class StatusDefs
    {
        public static readonly List<StatusDef> All = new List<StatusDef>
        {
            D("slow", "Замедление", "Slowed", StatusKind.Slow, false, new Color(0.3f, 0.9f, 0.85f)),
            D("root", "Оковы", "Rooted", StatusKind.Root, false, new Color(0.75f, 0.55f, 0.3f)),
            D("stun", "Оглушение", "Stunned", StatusKind.Stun, true, new Color(1f, 0.3f, 0.3f)),
            D("blind", "Ослепление", "Blinded", StatusKind.Blind, false, new Color(0.15f, 0.15f, 0.15f)),
            D("silence", "Немота", "Silenced", StatusKind.Silence, false, new Color(0.7f, 0.7f, 0.9f)),
            D("confuse", "Замешательство", "Confused", StatusKind.Confuse, false, new Color(1f, 0.5f, 0.85f)),
            D("fear", "Ужас", "Terrified", StatusKind.Fear, false, new Color(0.4f, 0.1f, 0.5f)),
            D("charm", "Подчинение", "Charmed", StatusKind.Charm, false, new Color(0.6f, 0.95f, 0.6f)),
            D("poly", "Превращение", "Transfigured", StatusKind.Poly, true, new Color(0.4f, 0.6f, 1f)),
            D("vuln", "Уязвимость", "Vulnerable", StatusKind.Vuln, false, new Color(1f, 0.6f, 0.2f)),
            D("weak", "Ослабление", "Weakened", StatusKind.Weak, false, new Color(0.6f, 0.6f, 0.6f)),
            D("shred", "Пробитая броня", "Armor broken", StatusKind.Shred, false, new Color(0.5f, 0.45f, 0.4f)),
            D("dot", "Проклятие", "Cursed", StatusKind.Dot, false, new Color(0.6f, 0.2f, 0.8f)),
            D("bleed", "Кровотечение", "Bleeding", StatusKind.Bleed, false, new Color(0.8f, 0.05f, 0.05f)),
            D("lift", "Левитация", "Levitating", StatusKind.Lift, true, new Color(0.8f, 0.9f, 1f)),
            D("forget", "Забвение", "Forgetting", StatusKind.Forget, false, new Color(0.3f, 0.9f, 0.5f)),
            D("taunt", "Вызов", "Taunted", StatusKind.Taunt, false, new Color(1f, 0.6f, 0.2f)),
            D("deaf", "Глухота", "Deafened", StatusKind.Deaf, false, new Color(0.9f, 0.8f, 0.4f)),
            D("thorns", "Шипы", "Spiked", StatusKind.Thorns, false, new Color(0.7f, 0.9f, 0.4f)),
            D("mark", "Метка", "Marked", StatusKind.Mark, false, new Color(1f, 0.85f, 0.3f)),
            D("disarm", "Связанные руки", "Bound arms", StatusKind.Disarm, false, new Color(0.75f, 0.55f, 0.3f)),
            D("shrink", "Уменьшение", "Shrunk", StatusKind.Shrink, false, new Color(0.7f, 0.5f, 1f)),
            D("immune", "Защита от чар", "Charm-proof", StatusKind.Immune, false, new Color(1f, 1f, 1f)),
            D("buff", "Чары", "Enchanted", StatusKind.Buff, false, new Color(1f, 0.9f, 0.5f)),
            D("hidden", "Сокрытие", "Concealed", StatusKind.Hidden, false, new Color(0.6f, 0.6f, 0.7f)),
            D("ward", "Под оберегом", "Warded", StatusKind.Ward, false, new Color(0.65f, 0.7f, 1f)),
        };

        private static readonly Dictionary<string, StatusDef> s_byKey = new Dictionary<string, StatusDef>();
        private static readonly Dictionary<int, StatusDef> s_byHash = new Dictionary<int, StatusDef>();

        private static StatusDef D(string key, string ru, string en, StatusKind kind, bool disables, Color tint)
        {
            return new StatusDef { Key = key, Ru = ru, En = en, Kind = kind, Disables = disables, Tint = tint };
        }

        public static int Hash(string key)
        {
            return ("WZ_" + key).GetStableHashCode();
        }

        public static StatusDef Get(string key)
        {
            StatusDef d;
            return s_byKey.TryGetValue(key, out d) ? d : null;
        }

        public static StatusDef Get(int hash)
        {
            StatusDef d;
            return s_byHash.TryGetValue(hash, out d) ? d : null;
        }

        /// <summary>Registers one status effect per kind with Jotunn (they reach ObjectDB on every client).</summary>
        public static void Register()
        {
            foreach (var d in All)
            {
                var se = ScriptableObject.CreateInstance<WzStatus>();
                se.name = "WZ_" + d.Key;
                se.m_name = "$wz_se_" + d.Key;
                se.m_tooltip = "";
                se.m_ttl = 5f;
                se.m_icon = Icons.Status(d.Key, d.Tint);
                se.Def = d;
                ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(se, false));
                s_byKey[d.Key] = d;
                s_byHash[Hash(d.Key)] = d;
            }

            // one buff per spell, so several can be on at once (each with the spell's name and its own timer)
            SpellDb.Init();
            foreach (var s in SpellDb.All)
            {
                if (s.Spec != null && (s.Spec.Has("buff") || s.Spec.Has("rested")))
                {
                    MakeBuff(s.Id, s.Ru, "", 30f);
                }
            }
        }

        /// <summary>A named buff ("WZ_buff_<key>") with preset values (potions) or values set when cast (spells).</summary>
        public static StatusEffect MakeBuff(string key, string name, string buffs, float ttl)
        {
            var se = ScriptableObject.CreateInstance<WzStatus>();
            se.name = "WZ_buff_" + key;
            se.m_name = name;
            se.m_tooltip = buffs;
            se.m_ttl = ttl;
            se.m_icon = Icons.Status("buff_" + key, new Color(1f, 0.85f, 0.4f));
            se.Def = Get("buff");
            se.Buffs = buffs;
            ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(se, false));
            return se;
        }
    }

    /// <summary>
    ///     A wizard's status on a creature or a player. It runs on the owner of the character (the game sends our
    ///     RPC there), so speed, attacks and the AI can be touched safely.
    /// </summary>
    internal sealed class WzStatus : StatusEffect
    {
        public StatusDef Def;
        public float Power;          // percent for slow/vuln/weak/shred, damage per second for dot/bleed
        public string Variant;       // how it looks: petrify, dance, laugh, hen, ...
        public string DamageType;    // dot: fire, poison, spirit, lightning, pierce
        public Character Source;
        public string Buffs;         // buff: "armor:15;speed:20"
        private float m_tick;
        private Vector3 m_anchor;
        private Character.Faction m_faction;
        private bool m_factionChanged;

        public override void Setup(Character character)
        {
            if (Def == null)
            {
                // Unity copies only the serialized fields when the game clones a status effect
                string n = name.Replace("(Clone)", "");
                Def = n.StartsWith("WZ_buff") ? StatusDefs.Get("buff") : n.StartsWith("WZ_") ? StatusDefs.Get(n.Substring(3)) : null;
            }

            base.Setup(character);
            m_anchor = character.transform.position;
        }

        public override void SetAttacker(Character attacker)
        {
            Source = attacker;
        }

        public void Begin()
        {
            if (Def == null || m_character == null)
            {
                return;
            }

            if (Def.Kind == StatusKind.Charm && !m_factionChanged && !m_character.IsPlayer())
            {
                m_faction = m_character.m_faction;
                m_character.m_faction = Character.Faction.Players;
                m_factionChanged = true;
                var ai = m_character.GetBaseAI() as MonsterAI;
                if (ai != null)
                {
                    ai.SetTargetCreature(null);
                }
            }

            if (Def.Kind == StatusKind.Forget)
            {
                var ai = m_character.GetBaseAI();
                if (ai != null)
                {
                    ai.SetAlertedState(false);
                    var mai = ai as MonsterAI;
                    if (mai != null)
                    {
                        mai.SetTargetCreature(null);
                    }
                }
            }

            if (Def.Kind == StatusKind.Lift && m_character.Body() != null)
            {
                var v = m_character.Body().velocity;
                v.y = Mathf.Max(v.y, Power);
                m_character.Body().velocity = v;
            }
        }

        public override void Stop()
        {
            if (Def != null && Def.Kind == StatusKind.Poly && m_character != null && m_character.Nv() != null &&
                m_character.Nv().IsOwner() && m_character.Nv().GetZDO() != null)
            {
                Fx.Send(FxKind.Poly, m_character.transform.position, Vector3.up, "", 0f, "", m_character.Nv().GetZDO().m_uid);
            }

            if (m_factionChanged && m_character != null)
            {
                m_character.m_faction = m_faction;
                m_factionChanged = false;
                var ai = m_character.GetBaseAI() as MonsterAI;
                if (ai != null)
                {
                    ai.SetTargetCreature(null);
                }
            }

            base.Stop();
        }

        public override void UpdateStatusEffect(float dt)
        {
            base.UpdateStatusEffect(dt);
            if (Def == null || m_character == null)
            {
                return;
            }

            switch (Def.Kind)
            {
                case StatusKind.Dot:
                case StatusKind.Bleed:
                    m_tick -= dt;
                    if (m_tick <= 0f)
                    {
                        m_tick += 1f;
                        var hit = new HitData();
                        Combat.SetDamage(ref hit.m_damage, Def.Kind == StatusKind.Bleed ? "slash" : (DamageType ?? "spirit"), Power);
                        hit.m_point = m_character.GetCenterPoint();
                        hit.m_dir = Vector3.up;
                        hit.m_hitType = HitData.HitType.PlayerHit;
                        hit.m_pushForce = 0f;
                        hit.m_staggerMultiplier = 0f;
                        if (Source != null)
                        {
                            hit.SetAttacker(Source);
                        }

                        Combat.Internal = true;
                        try
                        {
                            m_character.Damage(hit);
                        }
                        finally
                        {
                            Combat.Internal = false;
                        }
                    }

                    break;
                case StatusKind.Root:
                case StatusKind.Stun:
                case StatusKind.Poly:
                    if (m_character.Body() != null && m_character.IsOnGround())
                    {
                        var v = m_character.Body().velocity;
                        m_character.Body().velocity = new Vector3(0f, v.y, 0f);
                    }

                    break;
                case StatusKind.Lift:
                    if (m_character.Body() != null && m_time < 0.6f)
                    {
                        var v = m_character.Body().velocity;
                        v.y = Mathf.Max(v.y, Power * 0.5f);
                        m_character.Body().velocity = v;
                    }

                    break;
                case StatusKind.Thorns:
                    m_tick -= dt;
                    if (m_tick <= 0f)
                    {
                        m_tick += 1f;
                        if (m_character.GetVelocity().magnitude > 1f)
                        {
                            Prick();
                        }
                    }

                    break;
                case StatusKind.Taunt:
                    if (Source != null)
                    {
                        var ai = m_character.GetBaseAI() as MonsterAI;
                        if (ai != null && !Source.IsDead())
                        {
                            ai.SetTargetCreature(Source);
                        }
                    }

                    break;
            }
        }

        /// <summary>Porcupine and Sea Urchin jinxes: whoever stands next to the moving target is pricked.</summary>
        private void Prick()
        {
            foreach (var other in Character.GetAllCharacters())
            {
                if (other == m_character || other.IsDead() || other.IsPlayer() || other.IsTamed() ||
                    Vector3.Distance(other.transform.position, m_character.transform.position) > 2.5f)
                {
                    continue;
                }

                var hit = new HitData();
                hit.m_damage.m_pierce = Power;
                hit.m_point = other.GetCenterPoint();
                hit.m_dir = (other.transform.position - m_character.transform.position).normalized;
                hit.m_hitType = HitData.HitType.PlayerHit;
                if (Source != null)
                {
                    hit.SetAttacker(Source);
                }

                Combat.Internal = true;
                try
                {
                    other.Damage(hit);
                }
                finally
                {
                    Combat.Internal = false;
                }
            }
        }

        public override void ModifySpeed(float baseSpeed, ref float speed, Character character, Vector3 dir)
        {
            if (Def == null)
            {
                return;
            }

            switch (Def.Kind)
            {
                case StatusKind.Slow:
                    speed *= Mathf.Clamp01(1f - Power / 100f);
                    break;
                case StatusKind.Root:
                case StatusKind.Stun:
                case StatusKind.Poly:
                case StatusKind.Lift:
                    speed = 0f;
                    break;
                case StatusKind.Shrink:
                    speed *= 0.8f;
                    break;
                case StatusKind.Buff:
                    float sp = BuffValue("speed");
                    if (sp != 0f)
                    {
                        speed *= Mathf.Max(0.1f, 1f + sp / 100f);
                    }

                    break;
            }
        }

        public override void ModifyAttack(Skills.SkillType skill, ref HitData hitData)
        {
            if (Def == null)
            {
                return;
            }

            if (Def.Kind == StatusKind.Weak || Def.Kind == StatusKind.Shrink)
            {
                hitData.m_damage.Modify(Mathf.Clamp01(1f - Power / 100f));
            }
            else if (Def.Kind == StatusKind.Buff)
            {
                float d = BuffValue("dmg");
                if (d != 0f)
                {
                    hitData.m_damage.Modify(1f + d / 100f);
                }
            }
        }

        public override void ModifyArmorMods(ref float armor)
        {
            if (Def != null && Def.Kind == StatusKind.Buff)
            {
                armor += BuffValue("armor");
            }
        }

        public override void ModifyMaxCarryWeight(float baseLimit, ref float limit)
        {
            if (Def != null && Def.Kind == StatusKind.Buff)
            {
                limit += BuffValue("carry");
            }
        }

        public override void ModifyFallDamage(float baseDamage, ref float damage)
        {
            if (Def != null && Def.Kind == StatusKind.Buff)
            {
                float f = BuffValue("falldmg");
                if (f != 0f)
                {
                    damage *= Mathf.Clamp01(1f + f / 100f);
                }
            }
        }

        public override void ModifyStaminaRegen(ref float staminaRegen)
        {
            if (Def != null && Def.Kind == StatusKind.Buff)
            {
                staminaRegen *= 1f + BuffValue("stamregen") / 100f;
            }
        }

        public override void ModifyHealthRegen(ref float regenMultiplier)
        {
            if (Def != null && Def.Kind == StatusKind.Buff)
            {
                regenMultiplier *= 1f + BuffValue("hpregen") / 10f;
            }
        }

        public override void ModifyRunStaminaDrain(float baseDrain, ref float drain, Vector3 dir)
        {
            if (Def != null && Def.Kind == StatusKind.Buff)
            {
                drain *= Mathf.Max(0f, 1f + BuffValue("runstam") / 100f);
            }
        }

        public override void ModifySwimStaminaUsage(float baseStaminaUse, ref float staminaUse)
        {
            if (Def != null && Def.Kind == StatusKind.Buff && BuffValue("swim") > 0f)
            {
                staminaUse = 0f;
            }
        }

        public override void ModifyStealth(float baseStealth, ref float stealth)
        {
            if (Def != null && Def.Kind == StatusKind.Buff)
            {
                float s = Concealment();
                if (s > 0f)
                {
                    stealth *= Mathf.Max(0.02f, 1f - s / 100f);
                }
            }
        }

        public override void ModifyNoise(float baseNoise, ref float noise)
        {
            if (Def != null && Def.Kind == StatusKind.Buff)
            {
                float s = Concealment();
                if (s > 0f)
                {
                    noise *= Mathf.Max(0.02f, 1f - s / 100f);
                }
            }
        }

        public override void ModifySkillLevel(Skills.SkillType skill, ref float level)
        {
            if (Def != null && Def.Kind == StatusKind.Buff)
            {
                level += BuffValue("skills");
            }
        }

        public override void ModifyDamageMods(ref HitData.DamageModifiers modifiers)
        {
            if (Def == null || Def.Kind != StatusKind.Buff)
            {
                return;
            }

            if (BuffValue("fireimm") > 0f)
            {
                modifiers.m_fire = HitData.DamageModifier.VeryResistant;
            }
            else if (BuffValue("resfire") >= 40f)
            {
                modifiers.m_fire = HitData.DamageModifier.Resistant;
            }
        }

        /// <summary>Stealth in percent: the buff's own, invisibility, and the Cloak while sneaking or standing still.</summary>
        private float Concealment()
        {
            float s = BuffValue("stealth") + (BuffValue("invis") > 0f ? 100f : 0f);
            if (BuffValue("cloak") > 0f && m_character != null && (m_character.IsCrouching() || m_character.GetVelocity().magnitude < 0.5f))
            {
                s += 100f;
            }

            return Mathf.Min(98f, s);
        }

        public float BuffValue(string key)
        {
            if (string.IsNullOrEmpty(Buffs))
            {
                return 0f;
            }

            float total = 0f;
            foreach (string part in Buffs.Split(';'))
            {
                int c = part.IndexOf(':');
                float v;
                if (c > 0 && part.Substring(0, c) == key &&
                    float.TryParse(part.Substring(c + 1), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out v))
                {
                    total += v;
                }
            }

            return total;
        }

        public override string GetIconText()
        {
            return Mathf.CeilToInt(Mathf.Max(0f, m_ttl - m_time)).ToString();
        }
    }
}
