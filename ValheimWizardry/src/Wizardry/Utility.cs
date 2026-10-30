using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wizardry
{
    /// <summary>The household side of magic: repairing, growing, opening, fetching, lighting, digging.</summary>
    internal static class Utility
    {
        public static void Grow(Vector3 p, float r, WizardState w)
        {
            int n = 0;
            foreach (var col in Physics.OverlapSphere(p, r))
            {
                var plant = col.GetComponentInParent<Plant>();
                if (plant == null || plant.Nv() == null || plant.Nv().GetZDO() == null)
                {
                    continue;
                }

                if (!plant.Nv().IsOwner())
                {
                    plant.Nv().ClaimOwnership();
                }

                var zdo = plant.Nv().GetZDO();
                long t = zdo.GetLong(ZDOVars.s_plantTime, ZNet.instance.GetTime().Ticks);
                float step = Mathf.Max(plant.m_growTime, plant.m_growTimeMax) * 0.5f * (1f + (w != null ? w.Stats.Get(Stat.plant_growth) : 0f) / 100f);
                zdo.Set(ZDOVars.s_plantTime, t - TimeSpan.FromSeconds(step).Ticks);
                n++;
            }

            if (n > 0)
            {
                Fx.Send(FxKind.Heal, p, Vector3.up, "growth_charm", 0f);
            }
        }

        public static void Repair(Player caster, Vector3 p, float r)
        {
            int n = 0;
            foreach (var wnt in WearNTear.GetAllInstances())
            {
                if (wnt != null && Vector3.Distance(wnt.transform.position, p) <= r && wnt.Repair())
                {
                    n++;
                }
            }

            var item = caster.RightItem() ?? caster.LeftItem();
            if (item != null && item.m_shared.m_useDurability && item.m_durability < item.GetMaxDurability())
            {
                item.m_durability = Mathf.Min(item.GetMaxDurability(), item.m_durability + item.GetMaxDurability() * 0.1f);
                n++;
            }

            Casting.Message(Texts.L("Починено: ", "Repaired: ") + n);
        }

        public static void Terrain(Vector3 p, string what)
        {
            string[] parts = what.Split(':');
            float r = 2f;
            if (parts.Length > 1)
            {
                float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out r);
            }

            var comp = TerrainComp.FindTerrainCompiler(p);
            if (comp == null)
            {
                return;
            }

            var go = new GameObject("WZ_TerrainOp");
            go.SetActive(false);
            go.transform.position = p;
            var op = go.AddComponent<TerrainOp>();
            op.m_settings = new TerrainOp.Settings
            {
                m_raise = true,
                m_raiseRadius = r,
                m_raisePower = 1f,
                m_raiseDelta = parts[0] == "crater" ? -1.2f : -0.6f,
                m_smooth = true,
                m_smoothRadius = r + 0.5f,
                m_smoothPower = 3f,
                m_paintCleared = true,
                m_paintRadius = r,
                m_paintType = TerrainModifier.PaintType.Dirt,
            };
            try
            {
                comp.ApplyOperation(op);
            }
            catch (Exception e)
            {
                Plugin.Report("Terrain change", e);
            }

            UnityEngine.Object.Destroy(go);
        }

        public static void Accio(Player caster, Vector3 p, float r, bool single)
        {
            ItemDrop best = null;
            float bd = single ? 3f : r;
            var list = new List<ItemDrop>();
            foreach (var col in Physics.OverlapSphere(p, single ? 3f : r))
            {
                var item = col.GetComponentInParent<ItemDrop>();
                if (item == null || item.Nv() == null || item.Nv().GetZDO() == null)
                {
                    continue;
                }

                float d = Vector3.Distance(item.transform.position, p);
                if (single)
                {
                    if (d < bd)
                    {
                        bd = d;
                        best = item;
                    }
                }
                else if (!list.Contains(item))
                {
                    list.Add(item);
                }
            }

            if (best != null)
            {
                list.Add(best);
            }

            foreach (var item in list)
            {
                if (!item.Nv().IsOwner())
                {
                    item.Nv().ClaimOwnership();
                }

                item.transform.position = caster.transform.position + Vector3.up * 1.2f;
                var rb = item.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.position = item.transform.position;
                    rb.velocity = Vector3.zero;
                }
            }

            if (list.Count > 0)
            {
                Fx.Send(FxKind.Buff, caster.transform.position, Vector3.up, "accio", 0f);
            }
        }

        public static void Ignite(Vector3 p, float r)
        {
            foreach (var col in Physics.OverlapSphere(p, Mathf.Max(1.5f, r)))
            {
                var fp = col.GetComponentInParent<Fireplace>();
                if (fp != null && !fp.IsBurning())
                {
                    try
                    {
                        fp.AddFuel(1f);
                    }
                    catch (Exception)
                    {
                        // a fireplace that takes no fuel
                    }
                }
            }
        }

        public static void Extinguish(Player caster, Vector3 p, float r)
        {
            foreach (var c in Character.GetAllCharacters())
            {
                if (Vector3.Distance(c.transform.position, p) <= r && Combat.IsAlly(caster, c))
                {
                    Net.Cleanse(c, "burn");
                }
            }
        }

        /// <summary>Wind spells blow away the arrows and spit of monsters (each game stops the ones it owns; this one takes them over).</summary>
        public static void Deflect(Player caster, Vector3 p, float r)
        {
            int n = 0;
            foreach (var pr in UnityEngine.Object.FindObjectsByType<global::Projectile>(FindObjectsSortMode.None))
            {
                if (pr == null || pr.Nv() == null || !pr.Nv().IsValid() || Vector3.Distance(pr.transform.position, p) > r)
                {
                    continue;
                }

                var owner = pr.Owner();
                if (owner != null && (owner.IsPlayer() || owner.IsTamed()))
                {
                    continue;
                }

                if (!pr.Nv().IsOwner())
                {
                    pr.Nv().ClaimOwnership();
                }

                Fx.Burst(pr.transform.position, 0.5f, new Color(0.8f, 0.9f, 1f), 0.5f);
                pr.Nv().Destroy();
                n++;
            }

            if (n > 0)
            {
                Casting.Message(Texts.L("Отражено снарядов: ", "Projectiles blown away: ") + n);
            }
        }

        public static void Ping(Player caster)
        {
            if (Chat.instance != null)
            {
                Chat.instance.SendPing(caster.transform.position);
            }
        }

        public static void Door(Player caster, Vector3 p, string mode, float seconds, bool heavy)
        {
            Door best = null;
            float bd = 4f;
            foreach (var col in Physics.OverlapSphere(p, 4f))
            {
                var d = col.GetComponentInParent<Door>();
                if (d != null && Vector3.Distance(d.transform.position, p) < bd)
                {
                    bd = Vector3.Distance(d.transform.position, p);
                    best = d;
                }
            }

            if (best == null || best.Nv() == null || best.Nv().GetZDO() == null)
            {
                Casting.Message(Texts.L("Рядом нет двери.", "No door there."));
                return;
            }

            if (mode == "lock")
            {
                if (!best.Nv().IsOwner())
                {
                    best.Nv().ClaimOwnership();
                }

                best.Nv().GetZDO().Set("wz_locked", (long)(ZNet.instance.GetTimeSeconds() + seconds));
                best.Nv().GetZDO().Set("wz_locker", caster.GetPlayerID());
                Casting.Message(Texts.L("Дверь запечатана.", "The door is sealed."));
                return;
            }

            if (best.m_keyItem != null && !heavy && Wizard.State != null && Wizard.State.Year < 3)
            {
                Casting.Message(Texts.L("Этот замок пока сильнее вас.", "This lock is too strong for you yet."));
                return;
            }

            if (Locked(best, null))
            {
                if (!best.Nv().IsOwner())
                {
                    best.Nv().ClaimOwnership();
                }

                best.Nv().GetZDO().Set("wz_locked", 0L);
                Casting.Message(Texts.L("Печать Коллопортуса снята.", "The Colloportus seal is broken."));
            }

            bool forward = Vector3.Dot(best.transform.forward, caster.transform.position - best.transform.position) < 0f;
            best.Nv().InvokeRPC("UseDoor", forward);
        }

        public static bool Locked(Door d, Humanoid user)
        {
            var zdo = d != null && d.Nv() != null ? d.Nv().GetZDO() : null;
            if (zdo == null || ZNet.instance == null)
            {
                return false;
            }

            long until = zdo.GetLong("wz_locked", 0L);
            var pl = user as Player;
            return until > ZNet.instance.GetTimeSeconds() && (pl == null || pl.GetPlayerID() != zdo.GetLong("wz_locker", 0L));
        }

        /// <summary>Cistem Aperio: a chest's contents fly to you (if nobody else has it open).</summary>
        public static void Chest(Player caster, Vector3 p)
        {
            Container best = null;
            foreach (var col in Physics.OverlapSphere(p, 3f))
            {
                var c = col.GetComponentInParent<Container>();
                if (c != null)
                {
                    best = c;
                    break;
                }
            }

            if (best == null)
            {
                Casting.Message(Texts.L("Рядом нет сундука.", "No chest there."));
                return;
            }

            if (best.IsInUse())
            {
                Casting.Message(Texts.L("Этот сундук сейчас открыт.", "That chest is open now."));
                return;
            }

            var nv = best.GetComponent<ZNetView>();
            if (nv != null && !nv.IsOwner())
            {
                nv.ClaimOwnership();
            }

            var from = best.GetInventory();
            var to = caster.GetInventory();
            int moved = 0;
            foreach (var it in new List<ItemDrop.ItemData>(from.GetAllItems()))
            {
                if (!to.CanAddItem(it))
                {
                    break;
                }

                if (to.AddItem(it))
                {
                    from.RemoveItem(it);
                    moved++;
                }
            }

            best.SaveNow();
            Fx.Send(FxKind.Buff, best.transform.position, Vector3.up, "cistem_aperio", 0f);
            Casting.Message(Texts.L("Достано предметов: ", "Items taken: ") + moved);
        }

        public static void Stick(Player caster, Vector3 p)
        {
            WearNTear best = null;
            float bd = 3f;
            foreach (var col in Physics.OverlapSphere(p, 3f))
            {
                var w = col.GetComponentInParent<WearNTear>();
                if (w != null && Vector3.Distance(w.transform.position, p) < bd)
                {
                    bd = Vector3.Distance(w.transform.position, p);
                    best = w;
                }
            }

            var nv = best != null ? best.GetComponent<ZNetView>() : null;
            if (nv == null || nv.GetZDO() == null)
            {
                return;
            }

            int limit = 10 + (Wizard.State != null && Wizard.State.Rank("permanent_sticking") >= 6 ? 10 : 0) +
                        (Wizard.State != null && Wizard.State.Rank("permanent_sticking") >= 9 ? 20 : 0);
            if (!nv.IsOwner())
            {
                nv.ClaimOwnership();
            }

            nv.GetZDO().Set("wz_stick", true);
            Fx.Send(FxKind.Buff, best.transform.position, Vector3.up, "permanent_sticking", 0f);
            Casting.Message(Texts.L("Деталь закреплена навсегда (до " + limit + " на игрока).", "The piece is stuck for good (up to " + limit + ")."));
        }

        public static void LevitateObject(Player caster, Vector3 p, float seconds)
        {
            foreach (var col in Physics.OverlapSphere(p, 2.5f))
            {
                var rb = col.attachedRigidbody;
                var nv = col.GetComponentInParent<ZNetView>();
                if (rb == null || nv == null || col.GetComponentInParent<Character>() != null || col.GetComponentInParent<Piece>() != null)
                {
                    continue;
                }

                if (!nv.IsOwner())
                {
                    nv.ClaimOwnership();
                }

                var f = rb.gameObject.GetComponent<Carried>() ?? rb.gameObject.AddComponent<Carried>();
                f.Owner = caster;
                f.Until = Time.time + seconds;
                f.Height = 2.2f;
                return;
            }
        }

        public static void Carry(Player caster, string kind, float seconds)
        {
            Rigidbody best = null;
            float bd = 6f;
            foreach (var col in Physics.OverlapSphere(caster.transform.position, 6f))
            {
                var rb = col.attachedRigidbody;
                if (rb == null || col.GetComponentInParent<Player>() != null)
                {
                    continue;
                }

                bool ok;
                switch (kind)
                {
                    case "log": ok = col.GetComponentInParent<TreeLog>() != null; break;
                    case "body": ok = col.GetComponentInParent<Ragdoll>() != null || (col.GetComponentInParent<Character>() != null && col.GetComponentInParent<Character>().IsTamed()); break;
                    default: ok = col.GetComponentInParent<Vagon>() != null || col.GetComponentInParent<ItemDrop>() != null; break;
                }

                if (!ok)
                {
                    continue;
                }

                float d = Vector3.Distance(rb.position, caster.transform.position);
                if (d < bd)
                {
                    bd = d;
                    best = rb;
                }
            }

            if (best == null)
            {
                Casting.Message(Texts.L("Рядом нечего поднять.", "Nothing to lift nearby."));
                return;
            }

            var nv = best.GetComponentInParent<ZNetView>();
            if (nv != null && !nv.IsOwner())
            {
                nv.ClaimOwnership();
            }

            var f = best.gameObject.GetComponent<Carried>() ?? best.gameObject.AddComponent<Carried>();
            f.Owner = caster;
            f.Until = Time.time + seconds;
            f.Height = 1.5f;
        }

        public static void Vanish(Player caster, Vector3 p, float r)
        {
            foreach (var col in Physics.OverlapSphere(p, r))
            {
                var proj = col.GetComponentInParent<global::Projectile>();
                var nv = proj != null ? proj.GetComponent<ZNetView>() : null;
                if (nv != null && nv.GetZDO() != null)
                {
                    if (!nv.IsOwner())
                    {
                        nv.ClaimOwnership();
                    }

                    nv.Destroy();
                }
            }

            Fx.Send(FxKind.Burst, p, Vector3.up, "evanesco", 1f);
        }

        public static void DispelSummons(Player caster, Vector3 p, float r)
        {
            string[] small = { "Skeleton", "Seeker", "Blob", "Tick", "Bat", "Leech", "Twitcher", "Imp", "Ghost" };
            foreach (var c in Character.GetAllCharacters())
            {
                if (!Combat.IsEnemy(caster, c) || c.IsBoss() || Vector3.Distance(c.transform.position, p) > r)
                {
                    continue;
                }

                string n = Combat.Prefab(c);
                bool isSummon = false;
                foreach (var s in small)
                {
                    isSummon |= n.Contains(s);
                }

                if (!isSummon || c.GetMaxHealth() > 150f * Mathf.Max(1, Wizard.State != null ? Wizard.State.Year : 1))
                {
                    continue;
                }

                var hit = new HitData();
                hit.m_damage.m_spirit = c.GetMaxHealth() * 2f;
                hit.m_point = c.GetCenterPoint();
                hit.m_hitType = HitData.HitType.PlayerHit;
                hit.SetAttacker(caster);
                c.Damage(hit);
                Fx.Send(FxKind.Burst, c.transform.position, Vector3.up, "vipera_evanesca", 1f);
            }
        }

        public static void CallTames(Player caster, float r)
        {
            int n = 0;
            foreach (var c in Character.GetAllCharacters())
            {
                if (c == null || !c.IsTamed() || Vector3.Distance(c.transform.position, caster.transform.position) > r)
                {
                    continue;
                }

                if (c.Nv() != null && !c.Nv().IsOwner())
                {
                    c.Nv().ClaimOwnership();
                }

                var ai = c.GetBaseAI() as MonsterAI;
                if (ai != null)
                {
                    ai.SetFollowTarget(caster.gameObject);
                }

                n++;
            }

            Casting.Message(Texts.L("Откликнулись звери: ", "Beasts answered: ") + n);
        }

        /// <summary>Pack: into the open chest goes everything it already holds a stack of.</summary>
        public static void Pack(Player caster)
        {
            var gui = InventoryGui.instance;
            var chest = gui != null ? gui.OpenContainer() : null;
            if (chest == null)
            {
                Casting.Message(Texts.L("Откройте сундук, затем колдуйте Пак.", "Open a chest, then cast Pack."));
                return;
            }

            var into = chest.GetInventory();
            var from = caster.GetInventory();
            var names = new HashSet<string>();
            foreach (var it in into.GetAllItems())
            {
                names.Add(it.m_shared.m_name);
            }

            int moved = 0;
            foreach (var it in new List<ItemDrop.ItemData>(from.GetAllItems()))
            {
                if (it.m_equipped || !names.Contains(it.m_shared.m_name) || !into.CanAddItem(it))
                {
                    continue;
                }

                if (into.AddItem(it))
                {
                    from.RemoveItem(it);
                    moved++;
                }
            }

            Casting.Message(Texts.L("Уложено: ", "Packed: ") + moved);
        }

        public static void Barber(Player caster)
        {
            var hair = new List<string>();
            var beard = new List<string>();
            foreach (var go in ObjectDB.instance.m_items)
            {
                var drop = go != null ? go.GetComponent<ItemDrop>() : null;
                if (drop == null || drop.m_itemData.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Customization)
                {
                    continue;
                }

                if (go.name.StartsWith("Hair")) hair.Add(go.name);
                if (go.name.StartsWith("Beard")) beard.Add(go.name);
            }

            string newHair = hair.Count > 0 ? hair[UnityEngine.Random.Range(0, hair.Count)] : null;
            string newBeard = beard.Count > 0 && UnityEngine.Random.value < 0.5f ? beard[UnityEngine.Random.Range(0, beard.Count)] : null;
            caster.SetLooks(newHair, newBeard);

            Fx.Send(FxKind.Buff, caster.transform.position, Vector3.up, "crinus_muto", 0f);
        }

        public static string LastMead;
        private static float s_refillReady;

        public static void Refill(Player caster)
        {
            if (Time.time < s_refillReady || string.IsNullOrEmpty(LastMead))
            {
                Casting.Message(Texts.L("Нечего наполнять.", "Nothing to refill."));
                return;
            }

            s_refillReady = Time.time + 900f;
            if (UnityEngine.Random.value < 0.5f && caster.GetInventory().AddItem(LastMead, 1, 1, 0, 0L, "", false) != null)
            {
                Casting.Message(Texts.L("Рог снова полон!", "Your horn is full again!"));
            }
            else
            {
                Casting.Message(Texts.L("Не вышло — в другой раз.", "No luck this time."));
            }
        }

        /// <summary>Teleportation: the item under the cursor (or the last stack) goes to the vault of the Codexes.</summary>
        public static void SendHome(Player caster)
        {
            var inv = caster.GetInventory();
            ItemDrop.ItemData pick = null;
            foreach (var it in inv.GetAllItems())
            {
                if (!it.m_equipped && it.m_shared.m_teleportable && !Wands.IsWand(it))
                {
                    pick = it;
                }
            }

            if (pick == null)
            {
                Casting.Message(Texts.L("Нечего отправить (руду и металл нельзя).", "Nothing to send (no ore or metal)."));
                return;
            }

            Vault.Put(Wizard.State, pick);
            inv.RemoveItem(pick);
            Wizard.Changed();
            Casting.Message(Texts.L("Отправлено в тайник Кодекса: ", "Sent to the Codex vault: ") + Localization.instance.Localize(pick.m_shared.m_name));
        }

        public static void PointMe(Player caster)
        {
            var profile = Game.instance.GetPlayerProfile();
            Vector3 target;
            string what;
            if (profile.HaveDeathPoint())
            {
                target = profile.GetDeathPoint();
                what = Texts.L("ваше надгробие", "your grave");
            }
            else
            {
                target = caster.transform.position + Vector3.forward * 1000f;
                what = Texts.L("север", "north");
            }

            Vector3 d = target - caster.transform.position;
            d.y = 0f;
            float ang = Vector3.SignedAngle(caster.transform.forward, d, Vector3.up);
            string side = Mathf.Abs(ang) < 20f ? Texts.L("прямо", "ahead") : ang > 0 ? Texts.L("справа", "to the right") : Texts.L("слева", "to the left");
            Fx.Send(FxKind.Beam, caster.transform.position + Vector3.up, d.normalized * 6f, "point_me", 1.5f);
            Casting.Message(what + ": " + side + ", " + Mathf.RoundToInt(d.magnitude) + Texts.L(" м", " m"));
        }

        public static void Tempus(Player caster)
        {
            float f = EnvMan.instance.GetDayFraction();
            int minutes = Mathf.FloorToInt(f * 24f * 60f);
            Casting.Message(Texts.L("День ", "Day ") + EnvMan.instance.GetDay() + ", " + (minutes / 60).ToString("00") + ":" + (minutes % 60).ToString("00"));
        }

        public static void Message(Player caster, SpellDef s)
        {
            Casting.Message(s.Name + ": " + s.Effect);
        }

        public static void HideMap(Player caster, float seconds)
        {
            if (ZNet.instance != null)
            {
                ZNet.instance.SetPublicReferencePosition(false);
            }

            Casting.Message(Texts.L("Вас больше не видно на чужих картах.", "You no longer show on others' maps."));
        }
    }

    /// <summary>An object carried by magic: it floats after its wizard until the time runs out.</summary>
    internal sealed class Carried : MonoBehaviour
    {
        public Player Owner;
        public float Until, Height = 1.5f;
        private Rigidbody m_rb;

        private void Awake()
        {
            m_rb = GetComponent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            if (Owner == null || Time.time > Until || m_rb == null)
            {
                Destroy(this);
                return;
            }

            Vector3 target = Owner.transform.position + Owner.transform.forward * 2.5f + Vector3.up * Height;
            Vector3 to = target - m_rb.position;
            m_rb.velocity = Vector3.ClampMagnitude(to * 4f, 12f);
            m_rb.angularVelocity *= 0.9f;
        }
    }
}
