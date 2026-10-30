using System.Collections.Generic;
using UnityEngine;

namespace Wizardry
{
    /// <summary>
    ///     The look of the mod's windows: the art from assets/ (ui_*, glyph_*, node_*, crest_*), styles on the game's own
    ///     fonts, buttons, spell icons, star-chart lines and the tooltip. Everything is drawn in a virtual space that
    ///     scales with the screen.
    /// </summary>
    internal static class Ui
    {
        public static readonly Color Ink = new Color(0.27f, 0.16f, 0.08f);
        public static readonly Color InkSoft = new Color(0.42f, 0.29f, 0.17f);
        public static readonly Color InkRed = new Color(0.55f, 0.1f, 0.08f);
        public static readonly Color Gold = new Color(1f, 0.82f, 0.45f);
        public static readonly Color Beige = new Color(0.93f, 0.85f, 0.7f);
        public static readonly Color Dim = new Color(0.55f, 0.5f, 0.44f);

        public static GUIStyle Title, Heading, HeadingCenter, Body, Bold, Small, Italic, Light, LightSmall, LightBold, LightCenter, Center, Big,
            Button, ButtonRed, Tab, TabOn, Chip, ChipOn, Card, CardOn, Panel, Note, Row, RowOn;

        private static Font s_font, s_bold, s_norse, s_os;
        private static bool s_built, s_english, s_fakeBold;
        private static string s_tipTitle, s_tipBody;
        private static Color s_tipColor;
        private static float s_scale = 1f;

        public static float Scale { get { return s_scale; } }

        public static Texture2D Tex(string name)
        {
            return Meshes.Texture(name + ".png");
        }

        // ------------------------------------------------------------------ styles
        public static void Build()
        {
            var gm = Jotunn.Managers.GUIManager.Instance;
            var bold = gm != null ? gm.AveriaSerifBold : null;
            bool english = Texts.English;
            if (s_built && s_english == english && (bold == null || !english || s_bold == bold))
            {
                return;
            }

            s_english = english;
            s_built = true;
            if (english && bold != null)
            {
                s_bold = bold;
                s_font = gm.AveriaSerif ?? bold;
                s_norse = gm.NorseBold ?? gm.Norse ?? bold;
                s_fakeBold = false;
            }
            else
            {
                // Averia and Norse have no Cyrillic: a serif font of the system, with fallbacks for the symbols
                s_font = s_os ?? (s_os = Font.CreateDynamicFontFromOSFont(new[]
                {
                    "Georgia", "Cambria", "Constantia", "Palatino Linotype", "Book Antiqua", "Times New Roman", "Liberation Serif", "DejaVu Serif",
                    "Noto Serif", "Segoe UI Symbol", "DejaVu Sans", "Arial Unicode MS", "Arial",
                }, 16));
                s_bold = s_font;
                s_norse = s_font;
                s_fakeBold = true;
            }

            Title = Label(s_norse ?? s_bold, 34, Ink, TextAnchor.MiddleLeft, false, true);
            Heading = Label(s_norse ?? s_bold, 23, Ink, TextAnchor.MiddleLeft, false, true);
            HeadingCenter = Label(s_norse ?? s_bold, 26, Ink, TextAnchor.MiddleCenter, false, true);
            Body = Label(s_font, 16, Ink, TextAnchor.UpperLeft, true);
            Bold = Label(s_bold, 17, Ink, TextAnchor.UpperLeft, true, true);
            Small = Label(s_font, 13, InkSoft, TextAnchor.UpperLeft, true);
            Italic = Label(s_font, 15, InkSoft, TextAnchor.UpperLeft, true);
            Italic.fontStyle = FontStyle.Italic;
            Center = Label(s_bold, 16, Ink, TextAnchor.MiddleCenter, true, true);
            Big = Label(s_norse ?? s_bold, 46, Ink, TextAnchor.MiddleCenter, false, true);
            Light = Label(s_font, 15, Beige, TextAnchor.UpperLeft, true);
            LightSmall = Label(s_font, 13, new Color(0.85f, 0.78f, 0.66f), TextAnchor.UpperLeft, true);
            LightBold = Label(s_bold, 17, Gold, TextAnchor.UpperLeft, true, true);
            LightCenter = Label(s_bold, 15, Beige, TextAnchor.MiddleCenter, true, true);

            Button = Box("ui_btn", "ui_btn_hover", "ui_btn_off", 12, s_bold, 16, Ink);
            ButtonRed = Box("ui_btn_red", "ui_btn_red_hover", "ui_btn_off", 12, s_bold, 17, new Color(1f, 0.92f, 0.78f));
            Tab = Box("ui_tab", "ui_tab_on", null, 14, s_norse ?? s_bold, 22, Beige);
            TabOn = Box("ui_tab_on", "ui_tab_on", null, 14, s_norse ?? s_bold, 22, Gold);
            Chip = Box("ui_chip", "ui_chip_on", null, 12, s_bold, 14, Ink);
            ChipOn = Box("ui_chip_on", "ui_chip_on", null, 12, s_bold, 14, Gold);
            Card = Box("ui_card", "ui_card_on", null, 18, s_font, 15, Ink);
            CardOn = Box("ui_card_on", "ui_card_on", null, 18, s_font, 15, Ink);
            Panel = Box("ui_panel", null, null, 22, s_font, 15, Beige);
            Note = Box("ui_note", null, null, 20, s_font, 15, Ink);
            Row = new GUIStyle { normal = { background = null }, hover = { background = Tex("ui_card") }, border = new RectOffset(18, 18, 18, 18) };
            RowOn = new GUIStyle { normal = { background = Tex("ui_card_on") }, hover = { background = Tex("ui_card_on") }, border = new RectOffset(18, 18, 18, 18) };
        }

        private static GUIStyle Label(Font f, int size, Color c, TextAnchor a, bool wrap = false, bool bold = false)
        {
            var s = new GUIStyle(GUI.skin.label) { font = f, fontSize = size, alignment = a, wordWrap = wrap, richText = true, clipping = TextClipping.Clip };
            if (bold && s_fakeBold)
            {
                s.fontStyle = FontStyle.Bold;
            }

            s.normal.textColor = c;
            s.hover.textColor = c;
            s.padding = new RectOffset(0, 0, 0, 0);
            s.margin = new RectOffset(0, 0, 0, 0);
            return s;
        }

        private static GUIStyle Box(string normal, string hover, string off, int border, Font f, int size, Color text)
        {
            var s = new GUIStyle(GUI.skin.button)
            {
                font = f, fontSize = size, alignment = TextAnchor.MiddleCenter, richText = true, wordWrap = false,
                border = new RectOffset(border, border, border, border), padding = new RectOffset(10, 10, 4, 4),
            };
            s.normal.background = Tex(normal);
            s.hover.background = Tex(hover ?? normal);
            s.active.background = Tex(hover ?? normal);
            s.focused.background = Tex(normal);
            s.onNormal.background = Tex(hover ?? normal);
            s.normal.textColor = text;
            s.hover.textColor = text;
            s.active.textColor = text;
            s.focused.textColor = text;
            if (s_fakeBold)
            {
                s.fontStyle = FontStyle.Bold;
            }

            if (off != null)
            {
                s.onActive.background = Tex(off);
            }

            return s;
        }

        // ------------------------------------------------------------------ the virtual screen
        /// <summary>Scales the GUI so that a w×h layout fits the screen; returns the virtual screen size.</summary>
        public static Vector2 Begin(float w, float h, out Matrix4x4 old)
        {
            Build();
            old = GUI.matrix;
            s_scale = Mathf.Clamp(Mathf.Min(Screen.width / w, Screen.height / h), 0.5f, 2.5f);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s_scale, s_scale, 1f));
            return new Vector2(Screen.width / s_scale, Screen.height / s_scale);
        }

        public static Vector2 BeginHud(out Matrix4x4 old)
        {
            Build();
            old = GUI.matrix;
            s_scale = Mathf.Clamp(Screen.height / 1080f, 0.6f, 2.5f);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s_scale, s_scale, 1f));
            return new Vector2(Screen.width / s_scale, Screen.height / s_scale);
        }

        // ------------------------------------------------------------------ drawing
        public static void Image(Rect r, string tex, Color c)
        {
            var t = Tex(tex);
            if (t == null || Event.current.type != EventType.Repaint)
            {
                return;
            }

            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, t, ScaleMode.StretchToFill, true);
            GUI.color = old;
        }

        public static void Image(Rect r, Texture t, Color c)
        {
            if (t == null || Event.current.type != EventType.Repaint)
            {
                return;
            }

            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, t, ScaleMode.ScaleToFit, true);
            GUI.color = old;
        }

        public static void Frame(Rect r, GUIStyle style)
        {
            if (Event.current.type == EventType.Repaint)
            {
                style.Draw(r, GUIContent.none, false, false, false, false);
            }
        }

        public static void Fill(Rect r, Color c)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        public static void Text(Rect r, string text, GUIStyle style)
        {
            GUI.Label(r, text, style);
        }

        /// <summary>Light text with a dark shadow, readable over the game world.</summary>
        public static void Shadowed(Rect r, string text, GUIStyle style)
        {
            var c = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, style);
            style.normal.textColor = c;
            GUI.Label(r, text, style);
        }

        public static bool Btn(Rect r, string text, bool primary = false, bool enabled = true)
        {
            var old = GUI.enabled;
            GUI.enabled = enabled;
            var oldColor = GUI.color;
            if (!enabled)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
            }

            bool hit = GUI.Button(r, text, primary ? ButtonRed : Button);
            GUI.color = oldColor;
            GUI.enabled = old;
            if (hit)
            {
                Click();
            }

            return hit;
        }

        public static void Click()
        {
            var p = Player.m_localPlayer;
            if (p != null)
            {
                Fx.Sound(p.transform.position, "click", 0.25f);
            }
        }

        public static void Bar(Rect r, float f, Color c)
        {
            Frame(r, s_barBg ?? (s_barBg = new GUIStyle { normal = { background = Tex("ui_bar_bg") }, border = new RectOffset(6, 6, 6, 6) }));
            f = Mathf.Clamp01(f);
            if (f <= 0.001f || Event.current.type != EventType.Repaint)
            {
                return;
            }

            var old = GUI.color;
            GUI.color = c;
            var fill = s_barFill ?? (s_barFill = new GUIStyle { normal = { background = Tex("ui_bar_fill") }, border = new RectOffset(6, 6, 6, 6) });
            fill.Draw(new Rect(r.x + 2f, r.y + 2f, Mathf.Max(10f, (r.width - 4f) * f), r.height - 4f), GUIContent.none, false, false, false, false);
            GUI.color = old;
        }

        private static GUIStyle s_barBg, s_barFill;

        // ------------------------------------------------------------------ spell icons
        public static string Glyph(SpellDef s)
        {
            if (s == null) return "wand";
            if (s.IsGrand) return "grand";
            if (s.IsRitual) return "ritual";
            if (s.IsDark) return "dark";
            switch (s.Arch)
            {
                case "proj": return "proj";
                case "aoe": return "aoe";
                case "beam": return "beam";
                case "ctrl": return "ctrl";
                case "summ": return "summ";
                case "buff": return "buff";
                case "heal": return "heal";
                case "move": return "move";
                case "util": return "util";
                default: return "wand";
            }
        }

        public static Color SchoolColor(SpellDef s)
        {
            return s != null ? TreeDb.SchoolColor(s.School) : new Color(0.8f, 0.75f, 0.6f);
        }

        public static string SchoolName(string code)
        {
            var sd = TreeDb.School(code);
            return sd == null ? code : Texts.English ? sd.NameEn : sd.Name;
        }

        /// <summary>A spell's icon: a disc in the school's colour, the archetype's sign, a bronze rim.</summary>
        public static void SpellIcon(Rect r, SpellDef s, bool grey = false)
        {
            Color c = SchoolColor(s);
            if (grey)
            {
                float l = c.grayscale;
                c = new Color(l, l, l);
            }

            Image(r, "ui_disc", new Color(c.r * 0.45f, c.g * 0.45f, c.b * 0.45f, 1f));
            float pad = r.width * 0.2f;
            Image(new Rect(r.x + pad, r.y + pad, r.width - 2f * pad, r.height - 2f * pad), "glyph_" + Glyph(s),
                grey ? new Color(0.8f, 0.8f, 0.8f, 0.7f) : Color.Lerp(c, Color.white, 0.55f));
            Image(r, "ui_ring", grey ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.85f, 0.65f, 0.35f));
        }

        public static void Glyph(Rect r, string glyph, Color c)
        {
            Image(r, "glyph_" + glyph, c);
        }

        /// <summary>A dotted line, like the lines of a star chart.</summary>
        public static void Dotted(Vector2 a, Vector2 b, Color c, float size, float step)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            var t = Tex("ui_glow");
            float len = Vector2.Distance(a, b);
            int n = Mathf.Max(1, Mathf.CeilToInt(len / step));
            var old = GUI.color;
            GUI.color = c;
            for (int i = 0; i <= n; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)n);
                GUI.DrawTexture(new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size), t);
            }

            GUI.color = old;
        }

        // ------------------------------------------------------------------ the tooltip
        public static void Tip(Rect over, string title, string body, Color? c = null)
        {
            if (over.Contains(Event.current.mousePosition))
            {
                s_tipTitle = title;
                s_tipBody = body;
                s_tipColor = c ?? Gold;
            }
        }

        public static void DrawTip(Vector2 screen)
        {
            if (s_tipTitle == null)
            {
                return;
            }

            if (Event.current.type == EventType.Repaint)
            {
                const float w = 340f;
                float hBody = string.IsNullOrEmpty(s_tipBody) ? 0f : Light.CalcHeight(new GUIContent(s_tipBody), w - 32f);
                float h = 46f + hBody;
                var m = Event.current.mousePosition;
                float x = Mathf.Min(m.x + 18f, screen.x - w - 6f);
                float y = Mathf.Min(m.y + 18f, screen.y - h - 6f);
                var r = new Rect(x, y, w, h);
                Frame(r, Panel);
                var oc = LightBold.normal.textColor;
                LightBold.normal.textColor = s_tipColor;
                GUI.Label(new Rect(r.x + 16f, r.y + 12f, w - 32f, 24f), s_tipTitle, LightBold);
                LightBold.normal.textColor = oc;
                if (hBody > 0f)
                {
                    GUI.Label(new Rect(r.x + 16f, r.y + 38f, w - 32f, hBody), s_tipBody, Light);
                }
            }

            if (Event.current.type == EventType.Repaint)
            {
                s_tipTitle = null;
                s_tipBody = null;
            }
        }

        public static string Roman(int n)
        {
            string[] r = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
            return n >= 0 && n < r.Length ? r[n] : n.ToString();
        }

        /// <summary>The icon of a vanilla item (trophies), or null.</summary>
        public static Texture ItemIcon(string prefab)
        {
            Texture t;
            if (prefab == null)
            {
                return null;
            }

            if (s_items.TryGetValue(prefab, out t))
            {
                return t;
            }

            var go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            var drop = go != null ? go.GetComponent<ItemDrop>() : null;
            var icon = drop != null && drop.m_itemData.m_shared.m_icons != null && drop.m_itemData.m_shared.m_icons.Length > 0
                ? drop.m_itemData.m_shared.m_icons[0]
                : null;
            t = icon != null ? Crop(icon) : null;
            if (t != null || ObjectDB.instance != null)
            {
                s_items[prefab] = t;
            }

            return t;
        }

        private static readonly Dictionary<string, Texture> s_items = new Dictionary<string, Texture>();

        /// <summary>Icons live in atlases: copy the sprite's own rectangle into a texture of its own.</summary>
        private static Texture Crop(Sprite s)
        {
            var tr = s.textureRect;
            if (Mathf.Approximately(tr.width, s.texture.width) && Mathf.Approximately(tr.height, s.texture.height))
            {
                return s.texture;
            }

            try
            {
                var rt = RenderTexture.GetTemporary(s.texture.width, s.texture.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(s.texture, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var t = new Texture2D((int)tr.width, (int)tr.height, TextureFormat.RGBA32, false);
                t.ReadPixels(new Rect(tr.x, tr.y, tr.width, tr.height), 0, 0);
                t.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                return t;
            }
            catch (System.Exception)
            {
                return s.texture;
            }
        }
    }
}
