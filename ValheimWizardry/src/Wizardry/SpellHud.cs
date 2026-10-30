using UnityEngine;

namespace Wizardry
{
    /// <summary>
    ///     The spell bar (8 slots and the grand slot), cooldowns, the cast bar, Focus and Darkness. Shown while a wand
    ///     is in hand (or for the Wandless keystone).
    /// </summary>
    internal static class SpellHud
    {
        private static float s_flash;
        private static GUIStyle s_small, s_name, s_center, s_big;

        public static void FlashEitr()
        {
            s_flash = Time.time + 0.45f;
        }

        private static void Styles()
        {
            if (s_small != null)
            {
                return;
            }

            s_small = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.UpperLeft, normal = { textColor = Color.white } };
            s_name = new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.MiddleCenter, wordWrap = true, normal = { textColor = Color.white } };
            s_center = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            s_big = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        }

        public static void Draw()
        {
            var p = Player.m_localPlayer;
            var w = Wizard.State;
            if (p == null || w == null || p.IsDead() || Grimoire.Open || Hud.IsUserHidden() || Menu.IsVisible())
            {
                return;
            }

            bool wand = Wands.Equipped(p) != null;
            if (!wand && !w.Stats.Has("ks_wandless"))
            {
                Hint(w);
                return;
            }

            Styles();
            float scale = Mathf.Clamp(Screen.height / 1080f, 0.6f, 2f);
            var old = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            float sw = Screen.width / scale, sh = Screen.height / scale;
            const float size = 54f, gap = 6f;
            float total = 9 * size + 8 * gap + 14f;
            float x0 = (sw - total) * 0.5f;
            float y0 = sh - 200f;

            // the cast bar
            float frac;
            var casting = Casting.Progress(out frac);
            if (casting != null)
            {
                var r = new Rect(sw * 0.5f - 150f, y0 - 46f, 300f, 14f);
                Box(r, new Color(0f, 0f, 0f, 0.6f));
                Box(new Rect(r.x + 2f, r.y + 2f, (r.width - 4f) * frac, r.height - 4f), Fx.SpellColor(casting));
                GUI.Label(new Rect(r.x, r.y - 18f, r.width, 18f), casting.Name, s_center);
            }

            // the chosen spell's name
            string selId = w.Bar[Mathf.Clamp(w.Selected, 0, 7)] ?? "basic_cast";
            var sel = SpellDb.Get(selId);
            if (sel != null && casting == null)
            {
                GUI.Label(new Rect(sw * 0.5f - 200f, y0 - 26f, 400f, 22f), sel.Name, s_center);
            }

            bool flash = Time.time < s_flash;
            for (int i = 0; i < 8; i++)
            {
                Slot(new Rect(x0 + i * (size + gap), y0, size, size), w, w.Bar[i], (i + 1).ToString(), i == w.Selected, flash);
            }

            // the grand slot
            var grandRect = new Rect(x0 + 8 * (size + gap) + 14f, y0, size, size);
            Slot(grandRect, w, w.Grand, ModConfig.GrandKey.Value.ToString(), false, flash);
            if (w.Grand != null)
            {
                float need = 100f;
                float f = Mathf.Clamp01(w.Focus / need);
                Box(new Rect(grandRect.x, grandRect.yMax + 2f, grandRect.width * f, 4f), new Color(1f, 0.85f, 0.3f, 0.9f));
            }

            // Focus and Darkness
            float bw = 8 * (size + gap) - gap;
            Bar(new Rect(x0, y0 + size + 6f, bw, 7f), w.Focus / 100f, new Color(1f, 0.82f, 0.3f), Texts.L("Средоточие", "Focus"));
            if (w.Darkness > 0.5f)
            {
                Bar(new Rect(x0, y0 + size + 16f, bw, 7f), w.Darkness / 100f, new Color(0.55f, 0.2f, 0.75f), Texts.L("Тьма", "Darkness"));
            }

            int duel = Casting.DuelCharges;
            if (duel > 0)
            {
                GUI.Label(new Rect(x0 + bw - 80f, y0 - 24f, 80f, 20f), Texts.L("Дуэль ×", "Duel ×") + duel, s_small);
            }

            Hint(w, x0, y0 - 24f);
            GUI.matrix = old;
        }

        private static void Hint(WizardState w, float x = -1f, float y = -1f)
        {
            if (w.PointsFree <= 0 && !(w.SpecPointsFree > 0 && w.Spec != null))
            {
                return;
            }

            Styles();
            string t = Texts.L("Очки Древа: ", "Tree points: ") + w.PointsFree + Texts.L("  (K — гримуар)", "  (K — grimoire)");
            if (x < 0f)
            {
                GUI.Label(new Rect(Screen.width * 0.5f - 150f, Screen.height - 60f, 300f, 22f), t, s_center);
            }
            else
            {
                GUI.Label(new Rect(x, y, 300f, 20f), t, s_small);
            }
        }

        private static void Slot(Rect r, WizardState w, string id, string key, bool selected, bool flash)
        {
            var s = id != null ? SpellDb.Get(id) : null;
            Color bg = s != null ? TreeDb.SchoolColor(s.School) * 0.45f : new Color(0.1f, 0.1f, 0.1f);
            bg.a = 0.8f;
            if (flash && s != null)
            {
                bg = Color.Lerp(bg, new Color(0.8f, 0.1f, 0.1f, 0.9f), 0.6f);
            }

            if (selected)
            {
                Box(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), new Color(1f, 0.85f, 0.35f, 0.95f));
            }

            Box(r, bg);
            GUI.Label(new Rect(r.x + 3f, r.y + 1f, 30f, 16f), key, s_small);
            if (s == null)
            {
                return;
            }

            GUI.Label(new Rect(r.x + 2f, r.y + 12f, r.width - 4f, r.height - 14f), Short(s.Name), s_name);
            GUI.Label(new Rect(r.x + r.width - 22f, r.y + r.height - 15f, 22f, 15f), "r" + w.Rank(s.Id), s_small);
            float cd = Casting.Cooldown(s.Id);
            if (cd > 0f)
            {
                float total = Mathf.Max(cd, s.BaseCooldown * ModConfig.CooldownMultiplier.Value);
                Box(new Rect(r.x, r.y, r.width, r.height * Mathf.Clamp01(cd / total)), new Color(0f, 0f, 0f, 0.65f));
                GUI.Label(r, cd >= 10f ? Mathf.CeilToInt(cd).ToString() : cd.ToString("0.0"), s_big);
            }
        }

        private static string Short(string name)
        {
            return name.Length <= 22 ? name : name.Substring(0, 20) + "…";
        }

        private static void Bar(Rect r, float f, Color c, string label)
        {
            Box(r, new Color(0f, 0f, 0f, 0.55f));
            Box(new Rect(r.x + 1f, r.y + 1f, (r.width - 2f) * Mathf.Clamp01(f), r.height - 2f), c);
        }

        public static void Box(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
