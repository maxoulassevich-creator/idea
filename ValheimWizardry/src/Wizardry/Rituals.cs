using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Wizardry
{
    /// <summary>
    ///     Rituals are cast within 10 m of a Codex and burn materials that grow with the spell's year. Here too: Portus
    ///     (travel between bound Codexes), the Vanishing Cabinet (a chest at a Codex ↔ the vault) and Capacious Extremis.
    /// </summary>
    internal static class Rituals
    {
        private const string PortkeyKey = "Wizardry.portkeys";

        private static string[][] Materials(int year)
        {
            if (year <= 2) return new[] { new[] { "Resin", "5" }, new[] { "GreydwarfEye", "2" } };
            if (year <= 4) return new[] { new[] { "GreydwarfEye", "5" }, new[] { "SurtlingCore", "1" } };
            if (year <= 6) return new[] { new[] { "SurtlingCore", "2" }, new[] { "Crystal", "2" } };
            return new[] { new[] { "Eitr", "5" }, new[] { "BlackCore", "1" } };
        }

        public static string Describe(SpellDef s)
        {
            var parts = new List<string>();
            foreach (var m in Materials(s.Year))
            {
                var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(m[0]) : null;
                var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                string name = drop != null && Localization.instance != null ? Localization.instance.Localize(drop.m_itemData.m_shared.m_name) : m[0];
                parts.Add(name + " ×" + m[1]);
            }

            return string.Join(", ", parts.ToArray());
        }

        public static bool CanCast(Player p, WizardState w, SpellDef s)
        {
            var pat = Patterns.Active(w, s);
            if (!pat.Contains("r_free") && Pieces.Nearest(p.transform.position, 10f) == null)
            {
                Casting.Message(Texts.L("Ритуал творят у Кодекса (до 10 м).", "A ritual needs a Codex within 10 m."));
                return false;
            }

            foreach (var m in Materials(s.Year))
            {
                int need = Need(w, s, m);
                if (Items.Count(p, m[0]) < need)
                {
                    Casting.Message(Texts.L("Для ритуала нужно: ", "The ritual needs: ") + Describe(s));
                    return false;
                }
            }

            return true;
        }

        private static int Need(WizardState w, SpellDef s, string[] m)
        {
            int n = int.Parse(m[1], CultureInfo.InvariantCulture);
            return Patterns.Active(w, s).Contains("r_mats") ? Mathf.Max(1, Mathf.CeilToInt(n * 0.7f)) : n;
        }

        public static bool Pay(Player p, WizardState w, SpellDef s)
        {
            if (!CanCast(p, w, s))
            {
                return false;
            }

            foreach (var m in Materials(s.Year))
            {
                Items.Take(p, m[0], Need(w, s, m));
            }

            return true;
        }

        // ------------------------------------------------------------------ Portus
        private static List<Vector3> Keys(Player p)
        {
            var list = new List<Vector3>();
            string text;
            if (p.m_customData == null || !p.m_customData.TryGetValue(PortkeyKey, out text) || string.IsNullOrEmpty(text))
            {
                return list;
            }

            foreach (string part in text.Split(';'))
            {
                var f = part.Split(',');
                float x, y, z;
                if (f.Length == 3 && float.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
                    float.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) &&
                    float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
                {
                    list.Add(new Vector3(x, y, z));
                }
            }

            return list;
        }

        private static void SaveKeys(Player p, List<Vector3> keys)
        {
            var parts = new List<string>();
            foreach (var k in keys)
            {
                parts.Add(k.x.ToString("0.0", CultureInfo.InvariantCulture) + "," + k.y.ToString("0.0", CultureInfo.InvariantCulture) + "," +
                          k.z.ToString("0.0", CultureInfo.InvariantCulture));
            }

            p.m_customData[PortkeyKey] = string.Join(";", parts.ToArray());
        }

        public static void Portkey(Player caster, WizardState w)
        {
            var codex = Pieces.Nearest(caster.transform.position, 10f);
            if (codex == null)
            {
                return;
            }

            var keys = Keys(caster);
            int here = -1;
            for (int i = 0; i < keys.Count; i++)
            {
                if (Vector3.Distance(keys[i], codex.transform.position) < 5f)
                {
                    here = i;
                }
            }

            if (here < 0)
            {
                keys.Add(codex.transform.position);
                SaveKeys(caster, keys);
                Fx.Send(FxKind.Ceremony, codex.transform.position, Vector3.up, "portus", 0f, "bind");
                Casting.Message(Texts.L("Кодекс стал порталом-ключом. Привязано: ", "This Codex is now a Portkey. Bound: ") + keys.Count +
                                (keys.Count < 2 ? Texts.L(" (привяжите ещё один Кодекс)", " (bind one more Codex)") : ""));
                return;
            }

            if (keys.Count < 2)
            {
                Casting.Message(Texts.L("Привяжите Портусом ещё один Кодекс.", "Bind another Codex with Portus first."));
                return;
            }

            if (!caster.IsTeleportable(false) && !ModConfig.PortkeyMetal.Value)
            {
                Casting.Message(Texts.L("Портал-ключ не унесёт руду и металл.", "A Portkey will not carry ore and metal."));
                return;
            }

            var to = keys[(here + 1) % keys.Count];
            Fx.Send(FxKind.Blink, caster.transform.position, Vector3.up, "portus", 0f);
            caster.TeleportTo(to + Vector3.right * 2f + Vector3.up * 0.5f, caster.transform.rotation, true);
        }

        // ------------------------------------------------------------------ the Vanishing Cabinet
        private static Container NearestChest(Vector3 p, float r)
        {
            Container best = null;
            float bd = r;
            foreach (var col in Physics.OverlapSphere(p, r))
            {
                var c = col.GetComponentInParent<Container>();
                if (c == null || c.GetInventory() == null)
                {
                    continue;
                }

                float d = Vector3.Distance(c.transform.position, p);
                if (d < bd)
                {
                    bd = d;
                    best = c;
                }
            }

            return best;
        }

        public static void Cabinet(Player caster, WizardState w)
        {
            var chest = NearestChest(caster.transform.position, 5f);
            if (chest == null)
            {
                Casting.Message(Texts.L("Поставьте сундук рядом с Кодексом.", "Put a chest next to the Codex."));
                return;
            }

            if (chest.IsInUse())
            {
                Casting.Message(Texts.L("Сундук сейчас открыт.", "The chest is open now."));
                return;
            }

            var nv = chest.GetComponent<ZNetView>();
            if (nv != null && !nv.IsOwner())
            {
                nv.ClaimOwnership();
            }

            var inv = chest.GetInventory();
            int moved = 0;
            if (inv.NrOfItems() > 0)
            {
                foreach (var it in new List<ItemDrop.ItemData>(inv.GetAllItems()))
                {
                    if (!it.m_shared.m_teleportable && !ModConfig.PortkeyMetal.Value)
                    {
                        continue;
                    }

                    Vault.Put(w, it);
                    inv.RemoveItem(it);
                    moved++;
                }

                Casting.Message(Texts.L("Исчезательный шкаф: в тайник ушло ", "Vanishing Cabinet: sent to the vault: ") + moved);
            }
            else
            {
                var list = Vault.Items(caster);
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var e = list[i];
                    if (inv.AddItem(e.Prefab, e.Stack, e.Quality, e.Variant, 0L, "", false) == null)
                    {
                        break;
                    }

                    list.RemoveAt(i);
                    moved++;
                }

                var p = Player.m_localPlayer;
                var parts = new List<string>();
                foreach (var e in list)
                {
                    parts.Add(e.Prefab + "," + e.Stack + "," + e.Quality + "," + e.Variant);
                }

                p.m_customData["Wizardry.vault"] = string.Join("|", parts.ToArray());
                Casting.Message(Texts.L("Исчезательный шкаф: из тайника пришло ", "Vanishing Cabinet: arrived from the vault: ") + moved);
            }

            chest.Save();
            Fx.Send(FxKind.Ceremony, chest.transform.position, Vector3.up, "vanishing_cabinet", 0f, "cabinet");
        }

        // ------------------------------------------------------------------ Capacious Extremis
        public const string RowsKey = "wz_rows";

        public static void ChestRow(Player caster, WizardState w)
        {
            if (!ModConfig.ChestExtension.Value)
            {
                Casting.Message(Texts.L("Расширение сундуков выключено на этом сервере.", "Chest extension is off on this server."));
                return;
            }

            var chest = NearestChest(caster.transform.position, 5f);
            var nv = chest != null ? chest.GetComponent<ZNetView>() : null;
            if (nv == null || nv.GetZDO() == null)
            {
                Casting.Message(Texts.L("Поставьте сундук рядом с Кодексом.", "Put a chest next to the Codex."));
                return;
            }

            if (chest.IsInUse())
            {
                Casting.Message(Texts.L("Сундук сейчас открыт.", "The chest is open now."));
                return;
            }

            if (!nv.IsOwner())
            {
                nv.ClaimOwnership();
            }

            int rows = nv.GetZDO().GetInt(RowsKey);
            if (rows >= 3)
            {
                Casting.Message(Texts.L("Больше этот сундук не вместит.", "This chest cannot hold more."));
                return;
            }

            nv.GetZDO().Set(RowsKey, rows + 1);
            chest.GetInventory().m_height += 1;
            chest.Save();
            Fx.Send(FxKind.Ceremony, chest.transform.position, Vector3.up, "extension_charm", 0f, "extend");
            Casting.Message(Texts.L("Сундук стал больше изнутри: +1 ряд (", "The chest is bigger inside: +1 row (") + (rows + 1) + "/3)");
        }
    }

    /// <summary>The Deathly Hallows: the cloak's concealment and the stone's echo.</summary>
    internal static class Hallows
    {
        private static StatusEffect s_cloak;
        private static float s_stoneReady;

        public static StatusEffect CloakEffect()
        {
            return s_cloak ?? (s_cloak = StatusDefs.MakeBuff("cloak", "$wz_cloak", "cloak:1;stealth:40", 0f));
        }

        /// <summary>Resurrection Stone: at low health in a fight, once in 20 minutes, the echo of a fallen friend fights beside you.</summary>
        public static void Update(Player p, WizardState w)
        {
            if (p == null || p.IsDead() || Time.time < s_stoneReady || p.GetHealth() > p.GetMaxHealth() * 0.3f ||
                Items.Find(p, Items.Stone) == null)
            {
                return;
            }

            bool fight = false;
            foreach (var c in Character.GetAllCharacters())
            {
                if (Combat.IsEnemy(p, c) && Vector3.Distance(c.transform.position, p.transform.position) < 15f)
                {
                    fight = true;
                    break;
                }
            }

            if (!fight)
            {
                return;
            }

            s_stoneReady = Time.time + 1200f;
            Summons.Echo(p, w);
            Casting.Message(Texts.L("Воскрешающий камень: рядом встаёт эхо павшего друга.", "Resurrection Stone: the echo of a fallen friend stands beside you."));
        }
    }
}
