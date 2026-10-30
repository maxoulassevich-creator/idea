using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Wizardry
{
    /// <summary>Where the mod hooks into the game: the wand's attacks, damage, the wizard's passive numbers, chests, doors, raids.</summary>
    internal static class Patches
    {
        private static float s_guardianReady;

        private static WizardState LocalState(Character c)
        {
            return c != null && c == Player.m_localPlayer ? Wizard.State : null;
        }

        // ------------------------------------------------------------------ the wand: LMB casts the chosen spell, MMB the Basic Cast
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
        private static class StartAttack
        {
            private static bool Prefix(Humanoid __instance, bool secondaryAttack, ref bool __result)
            {
                if (!__instance.IsPlayer() && (Effects.Has(__instance, "disarm") || (Effects.Has(__instance, "silence") && Combat.IsCaster(__instance))))
                {
                    __result = false;
                    return false; // bound arms, a silenced shaman
                }

                var p = __instance as Player;
                if (p == null || p != Player.m_localPlayer || Wizard.State == null)
                {
                    return true;
                }

                if (Wands.Equipped(p) == null)
                {
                    Effects.BreakInvisibility(p);
                    return true;
                }

                __result = false;
                if (Effects.Has(p, "silence") || Effects.Has(p, "disarm"))
                {
                    Casting.Message(Texts.L("Вы не можете колдовать.", "You cannot cast."));
                    return false;
                }

                if (!Casting.Busy)
                {
                    if (secondaryAttack)
                    {
                        Casting.TryCast(p, Wizard.State, "basic_cast", true);
                    }
                    else
                    {
                        Casting.CastSelected(p, Wizard.State, true);
                    }
                }

                return false;
            }
        }

        /// <summary>Blocking with a wand is Protego: it costs eitr instead of stamina; a parry feeds the duelling nodes.</summary>
        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyBlockStaminaUsage))]
        private static class BlockCost
        {
            private static void Postfix(SEMan __instance, Character ___m_character, ref float staminaUse)
            {
                var p = ___m_character as Player;
                var w = LocalState(p);
                if (w == null || Wands.Equipped(p) == null || staminaUse <= 0f)
                {
                    return;
                }

                float eitr = staminaUse * 0.5f * Mathf.Max(0.2f, 1f - w.Stats.Get(Stat.protego_cost) / 100f);
                if (p.HaveEitr(eitr))
                {
                    p.UseEitr(eitr);
                    staminaUse = 0f;
                }
            }
        }

        [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
        private static class Parry
        {
            private static void Prefix(float ___m_blockTimer, out float __state)
            {
                __state = ___m_blockTimer;
            }

            private static void Postfix(Humanoid __instance, HitData hit, bool __result, float __state)
            {
                var p = __instance as Player;
                var w = LocalState(p);
                if (!__result || w == null || Wands.Equipped(p) == null)
                {
                    return;
                }

                Fx.Send(FxKind.Hit, hit.m_point, -hit.m_dir, "protego", 0f);
                if (__state >= 0f && __state < 0.25f)
                {
                    Casting.OnParry(p, w);
                }
            }
        }

        // ------------------------------------------------------------------ damage
        /// <summary>The attacker's side: the wizard's summons hit harder.</summary>
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        private static class DamageOut
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                if (hit == null || Combat.Internal)
                {
                    return;
                }

                var attacker = hit.GetAttacker();
                var w = Wizard.State;
                if (w != null && attacker != null && Summons.IsMine(attacker))
                {
                    float bonus = w.Stats.Get(Stat.summon_dmg) + (Combat.IsUndead(attacker) ? w.Stats.Get(Stat.undead_summon_dmg) : 0f);
                    if (bonus != 0f)
                    {
                        hit.m_damage.Modify(Mathf.Max(0.1f, 1f + bonus / 100f));
                    }
                }

                if (attacker == Player.m_localPlayer || __instance == Player.m_localPlayer)
                {
                    Casting.OnCombat();
                }
            }
        }

        /// <summary>The victim's side (runs on the owner): Protego, resistances, dodging, the Elixir and the guardians.</summary>
        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        private static class DamageIn
        {
            private static bool Prefix(Character __instance, HitData hit)
            {
                var p = __instance as Player;
                var w = LocalState(p);
                if (w == null || hit == null || Combat.Internal || p.IsDead())
                {
                    return true;
                }

                var attacker = hit.GetAttacker();
                if (Time.time < Casting.ProtegoUntil + 0.25f && attacker != p)
                {
                    Fx.Send(FxKind.Burst, p.GetCenterPoint(), Vector3.up, "protego", 1.2f);
                    Reflect(p, w, hit, attacker);
                    return false;
                }

                if (w.Stats.Get(Stat.dodge) > 0f && Random.value * 100f < w.Stats.Get(Stat.dodge))
                {
                    Fx.Send(FxKind.Text, p.transform.position + Vector3.up * 2f, Vector3.up, "", 0f, Texts.L("Уклонение", "Dodged"));
                    return false;
                }

                float elem = Mathf.Clamp(w.Stats.Get(Stat.res_elem), -100f, 80f) / 100f;
                float poison = Mathf.Clamp(w.Stats.Get(Stat.res_poison), -100f, 80f) / 100f;
                float spirit = Mathf.Clamp(w.Stats.Get(Stat.res_spirit), -100f, 80f) / 100f;
                hit.m_damage.m_fire *= 1f - elem;
                hit.m_damage.m_frost *= 1f - elem;
                hit.m_damage.m_lightning *= 1f - elem;
                hit.m_damage.m_poison *= 1f - poison;
                hit.m_damage.m_spirit *= 1f - spirit;
                if (hit.m_backstabBonus > 1f && w.Stats.Get(Stat.backstab_res) > 0f)
                {
                    hit.m_backstabBonus = Mathf.Max(1f, hit.m_backstabBonus * (1f - w.Stats.Get(Stat.backstab_res) / 100f));
                }

                // a blow that would kill (an estimate before armour): the Elixir of Life, then a guardian nearby
                if (hit.GetTotalDamage() >= p.GetHealth())
                {
                    if (Buffs.Value(p, "elixir") > 0f)
                    {
                        Effects.CleanseLocal(p, "buffs");
                        RemoveBuff(p, "WZ_buff_potion_elixir");
                        p.Heal(p.GetMaxHealth() * 0.4f - p.GetHealth(), true);
                        Fx.Send(FxKind.Ceremony, p.transform.position, Vector3.up, "", 0f, "phoenix");
                        Casting.Message(Texts.L("Эликсир жизни спас вас!", "The Elixir of Life saved you!"));
                        return false;
                    }

                    int flags;
                    var guardian = Guardians.Near(p, out flags);
                    if (guardian != null && Time.time > s_guardianReady)
                    {
                        s_guardianReady = Time.time + 300f;
                        hit.m_damage.Modify(Mathf.Clamp01((p.GetHealth() - 1f) / Mathf.Max(1f, hit.GetTotalDamage())));
                        Effects.ApplyLocal(p, new StatusOrder { Key = "buff", SeName = "WZ_buff", Duration = 2f, Buffs = "armor:500" });
                        if ((flags & 2) != 0)
                        {
                            Later.Run(0.2f, () => { if (p != null) p.Heal(p.GetMaxHealth() * 0.25f, true); });
                        }

                        Fx.Send(FxKind.Burst, p.GetCenterPoint(), Vector3.up, "protego", 2f);
                        Casting.Message(Texts.L("Вас заслонил ", "You were shielded by ") + guardian.GetPlayerName() + "!");
                    }
                }

                return true;
            }

            private static void Reflect(Player p, WizardState w, HitData hit, Character attacker)
            {
                string mode = Casting.ProtegoReflect;
                if (string.IsNullOrEmpty(mode))
                {
                    return;
                }

                Character target = mode == "caster" ? attacker : null;
                if (target == null)
                {
                    float bd = 20f;
                    foreach (var c in Character.GetAllCharacters())
                    {
                        float d = Vector3.Distance(c.transform.position, p.transform.position);
                        if (Combat.IsEnemy(p, c) && d < bd)
                        {
                            bd = d;
                            target = c;
                        }
                    }
                }

                if (target == null || target.IsDead() || !Combat.IsEnemy(p, target))
                {
                    return;
                }

                var back = new HitData();
                back.m_damage = hit.m_damage.Clone();
                back.m_point = target.GetCenterPoint();
                back.m_dir = (target.transform.position - p.transform.position).normalized;
                back.m_hitType = HitData.HitType.PlayerHit;
                back.SetAttacker(p);
                Fx.Send(FxKind.Beam, p.GetCenterPoint(), target.GetCenterPoint() - p.GetCenterPoint(), Casting.ProtegoSpell != null ? Casting.ProtegoSpell.Id : "protego", 0.3f);
                target.Damage(back);
            }

            private static void RemoveBuff(Player p, string name)
            {
                var seman = p.GetSEMan();
                var se = seman != null ? seman.GetStatusEffect(name.GetStableHashCode()) : null;
                if (se != null)
                {
                    seman.RemoveStatusEffect(se, true);
                }
            }
        }

        /// <summary>On every owner: Vulnerable, Armor broken and Marked creatures take more damage.</summary>
        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        private static class StatusDamage
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                var seman = __instance != null ? __instance.GetSEMan() : null;
                if (hit == null || seman == null)
                {
                    return;
                }

                float all = 1f, phys = 1f;
                foreach (var se in seman.GetStatusEffects())
                {
                    var wz = se as WzStatus;
                    if (wz == null || wz.Def == null)
                    {
                        continue;
                    }

                    switch (wz.Def.Kind)
                    {
                        case StatusKind.Vuln: all *= 1f + Mathf.Clamp(wz.Power, 0f, 100f) / 100f; break;
                        case StatusKind.Shred: phys *= 1f + Mathf.Clamp(wz.Power, 0f, 100f) / 100f; break;
                        case StatusKind.Mark: all *= 1.1f; break;
                    }
                }

                if (all != 1f)
                {
                    hit.m_damage.Modify(all);
                }

                if (phys != 1f)
                {
                    hit.m_damage.m_blunt *= phys;
                    hit.m_damage.m_slash *= phys;
                    hit.m_damage.m_pierce *= phys;
                }
            }
        }

        // ------------------------------------------------------------------ the wizard's passive numbers
        [HarmonyPatch(typeof(Player), "GetTotalFoodValue")]
        private static class FoodValues
        {
            private static void Postfix(Player __instance, ref float hp, ref float eitr)
            {
                var w = LocalState(__instance);
                if (w == null)
                {
                    return;
                }

                hp = (hp + w.Stats.Get(Stat.hp)) * (1f + w.Stats.Get(Stat.hp_pct) / 100f);
                float wand = Wands.Equipped(__instance) != null ? Wands.Eitr(__instance) * ModConfig.WandEitrMultiplier.Value : 0f;
                eitr = (eitr + wand + w.Stats.Get(Stat.eitr)) * (1f + w.Stats.Get(Stat.eitr_pct) / 100f);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.GetBodyArmor))]
        private static class Armor
        {
            private static void Postfix(Player __instance, ref float __result)
            {
                var w = LocalState(__instance);
                if (w != null)
                {
                    __result += w.Stats.Get(Stat.armor);
                }
            }
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ApplyStatusEffectSpeedMods))]
        private static class Speed
        {
            private static void Postfix(SEMan __instance, Character ___m_character, ref float speed)
            {
                var w = LocalState(___m_character);
                if (w != null)
                {
                    speed *= Mathf.Max(0.2f, 1f + w.Stats.Get(Stat.move_speed, Casting.Now(Player.m_localPlayer, w)) / 100f);
                }
            }
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyStaminaRegen))]
        private static class StaminaRegen
        {
            private static void Postfix(SEMan __instance, Character ___m_character, ref float staminaMultiplier)
            {
                var w = LocalState(___m_character);
                if (w != null) staminaMultiplier *= Mathf.Max(0f, 1f + w.Stats.Get(Stat.stam_regen) / 100f);
            }
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyEitrRegen))]
        private static class EitrRegen
        {
            private static void Postfix(SEMan __instance, Character ___m_character, ref float eitrMultiplier)
            {
                var w = LocalState(___m_character);
                if (w != null) eitrMultiplier *= Mathf.Max(0f, 1f + w.Stats.Get(Stat.eitr_regen) / 100f);
            }
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyHealthRegen))]
        private static class HealthRegen
        {
            private static void Postfix(SEMan __instance, Character ___m_character, ref float regenMultiplier)
            {
                var w = LocalState(___m_character);
                if (w != null) regenMultiplier *= Mathf.Max(0f, 1f + w.Stats.Get(Stat.hp_regen) / 100f);
            }
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyMaxCarryWeight))]
        private static class Carry
        {
            private static void Postfix(SEMan __instance, Character ___m_character, ref float limit)
            {
                var w = LocalState(___m_character);
                if (w != null) limit += w.Stats.Get(Stat.carry);
            }
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyStealth))]
        private static class Stealth
        {
            private static void Postfix(SEMan __instance, Character ___m_character, ref float stealth)
            {
                var w = LocalState(___m_character);
                if (w != null && w.Stats.Get(Stat.stealth) > 0f) stealth *= Mathf.Max(0.05f, 1f - w.Stats.Get(Stat.stealth) / 100f);
            }
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyFallDamage))]
        private static class Fall
        {
            private static void Postfix(SEMan __instance, Character ___m_character, ref float damage)
            {
                var w = LocalState(___m_character);
                if (w != null) damage *= Mathf.Clamp01(1f - w.Stats.Get(Stat.fall_res) / 100f);
                if (___m_character == Player.m_localPlayer && Time.time < Mobility.SafeLandingUntil) damage = 0f;
            }
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyRunStaminaDrain))]
        private static class RunDrain
        {
            private static void Postfix(SEMan __instance, Character ___m_character, ref float drain)
            {
                var w = LocalState(___m_character);
                if (w != null) drain *= Mathf.Max(0f, 1f - w.Stats.Get(Stat.run_stam) / 100f);
            }
        }

        /// <summary>Pepperup and the Healer's warmth keep the cold away; meads last longer for the Potioneer.</summary>
        [HarmonyPatch(typeof(SEMan), nameof(SEMan.AddStatusEffect), typeof(StatusEffect), typeof(bool), typeof(int), typeof(float), typeof(short))]
        private static class AddStatus
        {
            private static bool Prefix(SEMan __instance, Character ___m_character, StatusEffect statusEffect, ref StatusEffect __result)
            {
                if (statusEffect != null && ___m_character == Player.m_localPlayer &&
                    (statusEffect.name == "Cold" || statusEffect.name == "Freezing") && Buffs.Value(___m_character, "coldimm") > 0f)
                {
                    __result = null;
                    return false;
                }

                return true;
            }

            private static void Postfix(SEMan __instance, Character ___m_character, StatusEffect __result)
            {
                var w = LocalState(___m_character);
                if (w != null && __result != null && __result.name.StartsWith("Potion_") && w.Stats.Get(Stat.mead_dur) > 0f && __result.Elapsed() < 0.1f)
                {
                    __result.m_ttl *= 1f + w.Stats.Get(Stat.mead_dur) / 100f;
                }
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.ConsumeItem))]
        private static class Consume
        {
            private static void Prefix(Player __instance, ItemDrop.ItemData item)
            {
                if (__instance == Player.m_localPlayer && item != null && item.m_shared.m_consumeStatusEffect != null)
                {
                    string prefab = Items.PrefabOf(item);
                    if (prefab != null && prefab.StartsWith("Mead"))
                    {
                        Utility.LastMead = prefab;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ the world
        /// <summary>Fortifundus, Fianto Duri and the Great Dome harden buildings; the wizard's own buildings resist more.</summary>
        [HarmonyPatch(typeof(WearNTear), "RPC_Damage")]
        private static class Fortify
        {
            private static void Prefix(WearNTear __instance, HitData hit)
            {
                if (hit == null)
                {
                    return;
                }

                Vector3 p = __instance.transform.position;
                if (Anchor.Covers(p, "fortify") || Anchor.Covers(p, "dome_great"))
                {
                    hit.m_damage.Modify(0.4f);
                }

                var nv = __instance.GetComponent<ZNetView>();
                if (nv != null && nv.GetZDO() != null && nv.GetZDO().GetBool("wz_stick"))
                {
                    hit.m_damage.Modify(0.5f);
                }

                var piece = __instance.GetComponent<Piece>();
                var w = Wizard.State;
                if (w != null && piece != null && Player.m_localPlayer != null && piece.GetCreator() == Player.m_localPlayer.GetPlayerID() &&
                    w.Stats.Get(Stat.building_res) > 0f)
                {
                    hit.m_damage.Modify(Mathf.Max(0.1f, 1f - w.Stats.Get(Stat.building_res) / 100f));
                }
            }
        }

        /// <summary>Colloportus: a sealed door opens only for the one who sealed it.</summary>
        [HarmonyPatch(typeof(Door), nameof(Door.Interact))]
        private static class SealedDoor
        {
            private static bool Prefix(Door __instance, Humanoid character, ref bool __result)
            {
                if (!Utility.Locked(__instance, character))
                {
                    return true;
                }

                Casting.Message(Texts.L("Дверь запечатана Коллопортусом.", "The door is sealed by Colloportus."));
                __result = false;
                return false;
            }
        }

        /// <summary>Capacious Extremis: an extended chest gets its extra rows before its items load.</summary>
        [HarmonyPatch(typeof(Container), "Awake")]
        private static class ChestRows
        {
            private static void Prefix(Container __instance)
            {
                if (!ModConfig.ChestExtension.Value)
                {
                    return;
                }

                var nv = __instance.GetComponent<ZNetView>();
                var zdo = nv != null ? nv.GetZDO() : null;
                int rows = zdo != null ? zdo.GetInt(Rituals.RowsKey) : 0;
                if (rows > 0)
                {
                    __instance.m_height += rows;
                }
            }
        }

        /// <summary>Raids do not choose players hidden by the Fidelius Charm.</summary>
        [HarmonyPatch(typeof(RandEventSystem), "CheckBase")]
        private static class Raids
        {
            private static bool Prefix(RandEventSystem.PlayerEventData player, ref bool __result)
            {
                if (ModConfig.RaidBlock.Value && Anchor.Covers(player.position, "fidelius"))
                {
                    __result = false;
                    return false;
                }

                return true;
            }
        }

        /// <summary>Felix Felicis and the Collector's nodes: a creature the wizard fought sometimes drops one more thing.</summary>
        [HarmonyBefore("randyknapp.mods.epicloot")]
        [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
        private static class Luck
        {
            private static void Postfix(CharacterDrop __instance, List<KeyValuePair<GameObject, int>> __result)
            {
                var me = Player.m_localPlayer;
                var w = Wizard.State;
                if (me == null || w == null || __result == null || __result.Count == 0 ||
                    Vector3.Distance(me.transform.position, __instance.transform.position) > 30f)
                {
                    return;
                }

                float chance = w.Stats.Get(Stat.loot) + Buffs.Value(me, "loot");
                if (chance > 0f && Random.value * 100f < chance)
                {
                    // only plain materials and trophies: never a weapon or armour, which Epic Loot may turn magic
                    var plain = __result.FindAll(kv => kv.Key != null && kv.Key.GetComponent<ItemDrop>() != null &&
                        (kv.Key.GetComponent<ItemDrop>().m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Material ||
                         kv.Key.GetComponent<ItemDrop>().m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy));
                    if (plain.Count > 0)
                    {
                        var pick = plain[Random.Range(0, plain.Count)];
                        __result.Add(new KeyValuePair<GameObject, int>(pick.Key, Mathf.Max(1, pick.Value)));
                    }
                }
            }
        }
    }

    /// <summary>What statuses do to the minds of creatures: stunned and transfigured ones stand still, the terrified flee, the confused wander, the blind lose their target.</summary>
    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
    internal static class MindPatch
    {
        private static bool Prefix(MonsterAI __instance, float dt, ref bool __result, Character ___m_character,
            ref Character ___m_targetCreature, ref StaticTarget ___m_targetStatic)
        {
            var c = ___m_character;
            if (c == null || c.IsDead())
            {
                return true;
            }

            if (Effects.Has(c, "stun") || Effects.Has(c, "poly") || Effects.Has(c, "lift") || Effects.Has(c, "root") && Effects.Has(c, "disarm"))
            {
                __instance.StopMoving();
                __result = true;
                return false;
            }

            if (Effects.Has(c, "fear"))
            {
                var from = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : c.transform.position + c.transform.forward;
                var target = ___m_targetCreature;
                if (target != null)
                {
                    from = target.transform.position;
                }

                __instance.FleeFrom(dt, from);
                __result = true;
                return false;
            }

            if (Effects.Has(c, "confuse"))
            {
                __instance.Wander(dt, c.transform.position);
                __result = true;
                return false;
            }

            if (Effects.Has(c, "blind"))
            {
                ___m_targetCreature = null;
                ___m_targetStatic = null;
            }

            return true;
        }
    }
}
