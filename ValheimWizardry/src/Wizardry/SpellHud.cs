using UnityEngine;

namespace Wizardry
{
    /// <summary>
    ///     On the screen: the spell bar (8 slots and the grand slot) with cooldowns, the cast bar, Focus and Darkness while a
    ///     wand is in hand, and the tracker of the wizard's path under the minimap.
    /// </summary>
    internal static class SpellHud
    {
        private static float s_flash;

        public static void FlashEitr()
        {
            s_flash = Time.time + 0.45f;
        }

        public static void Draw()
        {
            var p = Player.m_localPlayer;
            var w = Wizard.State;
            if (p == null || w == null || p.IsDead() || Grimoire.Open || Hud.IsUserHidden() || Menu.IsVisible() || Minimap.IsOpen())
            {
                return;
            }

            Matrix4x4 old;
            var screen = Ui.BeginHud(out old);
            if (!InventoryGui.IsVisible())
            {
                Tracker(p, w, screen);
            }

            bool wand = Wands.Equipped(p) != null;
            if ((wand || w.Stats.Has("ks_wandless")) && !InventoryGui.IsVisible())
            {
                Bar(p, w, screen);
            }

            Ui.DrawTip(screen);
            GUI.matrix = old;
        }

        // ------------------------------------------------------------------ the spell bar
        private static void Bar(Player p, WizardState w, Vector2 screen)
        {
            const float size = 56f, gap = 6f;
            float total = 9 * size + 8 * gap + 16f;
            float x0 = Mathf.Round((screen.x - total) * 0.5f);
            float y0 = screen.y - 210f;

            float frac;
            var casting = Casting.Progress(out frac);
            if (casting != null)
            {
                var r = new Rect(screen.x * 0.5f - 160f, y0 - 58f, 320f, 20f);
                Ui.Bar(r, frac, Fx.SpellColor(casting));
                Ui.Shadowed(new Rect(r.x, r.y - 24f, r.width, 22f), casting.Name, Ui.LightCenter);
            }
            else
            {
                string selId = w.Bar[Mathf.Clamp(w.Selected, 0, 7)] ?? "basic_cast";
                var sel = SpellDb.Get(selId);
                if (sel != null)
                {
                    Ui.Shadowed(new Rect(screen.x * 0.5f - 220f, y0 - 30f, 440f, 24f), sel.Name, Ui.LightCenter);
                }
            }

            bool flash = Time.time < s_flash;
            for (int i = 0; i < 8; i++)
            {
                Slot(new Rect(x0 + i * (size + gap), y0, size, size), w, w.Bar[i], (i + 1).ToString(), i == w.Selected, flash);
            }

            var grand = new Rect(x0 + 8 * (size + gap) + 16f, y0 - 4f, size + 8f, size + 8f);
            Slot(grand, w, w.Grand, ModConfig.GrandKey.Value.ToString(), false, flash);
            if (w.Grand != null)
            {
                Ui.Bar(new Rect(grand.x, grand.yMax + 4f, grand.width, 12f), w.Focus / 100f, new Color(1f, 0.8f, 0.3f));
                Ui.Tip(grand, Texts.L("Средоточие ", "Focus ") + Mathf.FloorToInt(w.Focus) + "/100",
                    Texts.L("Копится от заклинаний. Когда полно, клавиша ", "Builds up as you cast. When full, ") + ModConfig.GrandKey.Value +
                    Texts.L(" творит великие чары.", " casts the grand spell."));
            }

            if (w.Darkness > 0.5f)
            {
                var dr = new Rect(x0, y0 + size + 8f, 8 * (size + gap) - gap, 10f);
                Ui.Bar(dr, w.Darkness / 100f, new Color(0.55f, 0.2f, 0.75f));
                Ui.Tip(dr, Texts.L("Тьма ", "Darkness ") + Mathf.FloorToInt(w.Darkness),
                    Texts.L("Тёмные заклинания копят Тьму. Она усиливает тёмную магию, но на 100 ночью приходят призраки.",
                        "Dark spells build Darkness. It strengthens dark magic, but at 100 wraiths come at night."));
            }

            int duel = Casting.DuelCharges;
            if (duel > 0)
            {
                Ui.Shadowed(new Rect(x0, y0 - 30f, 160f, 22f), Texts.L("Дуэль ×", "Duel ×") + duel, Ui.LightBold);
            }

            if (w.PointsFree > 0)
            {
                Ui.Shadowed(new Rect(screen.x * 0.5f - 200f, y0 + size + (w.Darkness > 0.5f ? 24f : 10f), 400f, 22f),
                    "<color=#ffd27a>✦ " + w.PointsFree + "</color> " + Texts.L("— откройте гримуар (", "— open the grimoire (") + ModConfig.GrimoireKey.Value + ")",
                    Ui.LightCenter);
            }
        }

        private static void Slot(Rect r, WizardState w, string id, string key, bool selected, bool flash)
        {
            var s = id != null ? SpellDb.Get(id) : null;
            Ui.Image(r, "ui_slot", Color.white);
            if (s != null)
            {
                float pad = r.width * 0.1f;
                var ir = new Rect(r.x + pad, r.y + pad, r.width - 2f * pad, r.height - 2f * pad);
                Ui.SpellIcon(ir, s);
                if (flash)
                {
                    Ui.Image(ir, "ui_disc", new Color(0.9f, 0.1f, 0.1f, 0.45f));
                }

                float cd = Casting.Cooldown(s.Id);
                if (cd > 0f)
                {
                    float total = Mathf.Max(cd, s.BaseCooldown * ModConfig.CooldownMultiplier.Value);
                    float f = Mathf.Clamp01(cd / total);
                    Ui.Image(ir, "ui_disc", new Color(0f, 0f, 0f, 0.35f + 0.4f * f));
                    Ui.Shadowed(r, cd >= 10f ? Mathf.CeilToInt(cd).ToString() : cd.ToString("0.0"), Ui.LightCenter);
                }

                int rank = w.Rank(s.Id);
                if (rank > 1 && s.Role != "basic")
                {
                    Ui.Shadowed(new Rect(r.xMax - 24f, r.yMax - 20f, 24f, 18f), Ui.Roman(rank), Ui.LightSmall);
                }

                Ui.Tip(r, s.Name, Ui.SchoolName(s.School) + Texts.L(" · ранг ", " · rank ") + Ui.Roman(rank));
            }

            if (selected)
            {
                float g = r.width * 0.22f;
                Ui.Image(new Rect(r.x - g, r.y - g, r.width + 2f * g, r.height + 2f * g), "ui_slot_sel", Color.white);
            }

            Ui.Shadowed(new Rect(r.x + 3f, r.y - 4f, 24f, 18f), key, Ui.LightSmall);
        }

        // ------------------------------------------------------------------ the path tracker
        private static void Tracker(Player p, WizardState w, Vector2 screen)
        {
            if (!ModConfig.ShowTracker.Value)
            {
                return;
            }

            var step = Quests.Current(w);
            if (step == null)
            {
                return;
            }

            const float width = 330f;
            string title = step.Title;
            string hint = step.Hint;
            if (hint.Length > 150)
            {
                int cut = hint.IndexOf(". ", 60, System.StringComparison.Ordinal);
                hint = cut > 0 && cut < 160 ? hint.Substring(0, cut + 1) : hint.Substring(0, 147) + "…";
            }

            float titleH = Ui.Bold.CalcHeight(new GUIContent(title), width - 70f);
            float hintH = Ui.Small.CalcHeight(new GUIContent(hint), width - 36f);
            bool bar = step.Target > 1;
            float h = 34f + titleH + 6f + hintH + (bar ? 24f : 0f) + 30f;
            var r = new Rect(screen.x - width - 18f, 300f, width, h);
            Ui.Frame(r, Ui.Note);
            Ui.Glyph(new Rect(r.x + 14f, r.y + 12f, 34f, 34f), "quest", Ui.InkRed);
            Ui.Text(new Rect(r.x + 56f, r.y + 10f, width - 70f, 20f),
                Texts.L("Путь волшебника · шаг ", "Wizard's path · step ") + (Quests.Index(step) + 1) + "/" + Quests.All.Count, Ui.Small);
            Ui.Text(new Rect(r.x + 56f, r.y + 30f, width - 70f, titleH), title, Ui.Bold);
            float y = r.y + 34f + titleH + 6f;
            Ui.Text(new Rect(r.x + 18f, y, width - 36f, hintH), hint, Ui.Small);
            y += hintH + 4f;
            if (bar)
            {
                int prog = Mathf.Min(step.Target, step.Progress(p, w));
                Ui.Bar(new Rect(r.x + 18f, y + 2f, width - 100f, 16f), prog / (float)step.Target, new Color(0.85f, 0.55f, 0.2f));
                Ui.Text(new Rect(r.xMax - 76f, y, 64f, 20f), prog + "/" + step.Target, Ui.Bold);
                y += 24f;
            }

            Ui.Text(new Rect(r.x + 18f, y + 2f, width - 36f, 20f),
                Texts.L("Подробнее: гримуар (", "Details: the grimoire (") + ModConfig.GrimoireKey.Value + ")", Ui.Small);
        }
    }
}
