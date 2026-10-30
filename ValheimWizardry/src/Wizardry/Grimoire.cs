using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Wizardry
{
    /// <summary>
    ///     The grimoire window (K): the house, the Tree of Charms, spells with their patterns and metamagic, books and
    ///     trophies, the wand, the specialisation, the bestiary and the ledger. Things that change the character for good
    ///     (reading, showing a trophy, refunds, cores) need a Codex within 10 m.
    /// </summary>
    internal static class Grimoire
    {
        public static bool Open;
        private static int s_tab;
        private static Vector2 s_pan, s_scrollA, s_scrollB, s_scrollC;
        private static float s_zoom = 1f;
        private static string s_node, s_spell, s_school;
        private static Rect s_window;
        private static bool s_dragging;
        private static Vector2 s_dragFrom;
        private static GUIStyle s_title, s_text, s_small, s_wrap, s_button, s_tabOn, s_tabOff, s_box;
        private static readonly string[] s_houses = { "gryffindor", "ravenclaw", "hufflepuff", "slytherin" };

        public static void Toggle()
        {
            Open = !Open;
            if (Open && Wizard.State != null)
            {
                s_tab = Wizard.State.House == null ? 0 : s_tab;
            }
        }

        public static void OnNewCharacter(WizardState w)
        {
            Open = false;
            s_node = null;
            s_spell = null;
        }

        private static bool NearCodex(Player p)
        {
            return p != null && Pieces.Nearest(p.transform.position, 10f) != null;
        }

        private static string[] Tabs()
        {
            return new[]
            {
                Texts.L("Факультет", "House"), Texts.L("Древо", "Tree"), Texts.L("Заклинания", "Spells"), Texts.L("Книги", "Books"),
                Texts.L("Палочка", "Wand"), Texts.L("Призвание", "Calling"), Texts.L("Бестиарий", "Bestiary"), Texts.L("Журнал", "Ledger")
            };
        }

        private static void Styles()
        {
            if (s_title != null)
            {
                return;
            }

            s_title = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.88f, 0.6f) } };
            s_text = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = Color.white } };
            s_small = new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = new Color(0.85f, 0.85f, 0.85f) } };
            s_wrap = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, normal = { textColor = Color.white } };
            s_button = new GUIStyle(GUI.skin.button) { fontSize = 13 };
            s_tabOn = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.85f, 0.4f) } };
            s_tabOff = new GUIStyle(GUI.skin.button) { fontSize = 14 };
            s_box = new GUIStyle(GUI.skin.box);
        }

        public static void Draw()
        {
            if (!Open)
            {
                return;
            }

            var p = Player.m_localPlayer;
            var w = Wizard.State;
            if (p == null || w == null)
            {
                Open = false;
                return;
            }

            if (Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Escape || Event.current.keyCode == ModConfig.GrimoireKey.Value))
            {
                Open = false;
                Event.current.Use();
                return;
            }

            Styles();
            float scale = Mathf.Clamp(Screen.height / 1080f, 0.7f, 2f);
            var old = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            float sw = Screen.width / scale, sh = Screen.height / scale;
            s_window = new Rect((sw - 1200f) * 0.5f, (sh - 760f) * 0.5f, 1200f, 760f);
            SpellHud.Box(s_window, new Color(0.05f, 0.05f, 0.07f, 0.94f));
            GUILayout.BeginArea(new Rect(s_window.x + 12f, s_window.y + 8f, s_window.width - 24f, s_window.height - 16f));
            GUILayout.BeginHorizontal();
            GUILayout.Label(Texts.L("Гримуар волшебника", "The Wizard's Grimoire"), s_title, GUILayout.Width(330f));
            GUILayout.Label(Texts.L("Курс ", "Year ") + w.Year + "   " + Texts.L("Очки Древа: ", "Tree points: ") + w.PointsFree + "/" + w.PointsTotal +
                            (w.House != null ? "   " + HouseName(w.House) : "") + (NearCodex(p) ? Texts.L("   ✦ Кодекс рядом", "   ✦ Codex near") : ""), s_text);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(Texts.L("Закрыть", "Close"), s_button, GUILayout.Width(90f)))
            {
                Open = false;
            }

            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            var tabs = Tabs();
            for (int i = 0; i < tabs.Length; i++)
            {
                if (GUILayout.Button(tabs[i], i == s_tab ? s_tabOn : s_tabOff, GUILayout.Height(30f)))
                {
                    s_tab = i;
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(6f);
            if (w.House == null && s_tab != 0)
            {
                GUILayout.Label(Texts.L("Сначала выберите факультет.", "Choose your house first."), s_text);
                s_tab = 0;
            }

            switch (s_tab)
            {
                case 0: House(p, w); break;
                case 1: Tree(p, w); break;
                case 2: Spells(p, w); break;
                case 3: Books(p, w); break;
                case 4: Wand(p, w); break;
                case 5: Calling(p, w); break;
                case 6: Bestiary(w); break;
                default: Ledger(p, w); break;
            }

            GUILayout.EndArea();
            GUI.matrix = old;
        }

        // ------------------------------------------------------------------ house
        private static string HouseName(string h)
        {
            var n = TreeDb.Get(TreeDb.HouseStart(h));
            return n != null ? n.Name : h;
        }

        private static string Suggest(Player p)
        {
            float melee = p.GetSkillLevel(Skills.SkillType.Swords) + p.GetSkillLevel(Skills.SkillType.Axes) + p.GetSkillLevel(Skills.SkillType.Clubs);
            float wit = p.GetSkillLevel(Skills.SkillType.Bows) + p.GetSkillLevel(Skills.SkillType.ElementalMagic);
            float care = p.GetSkillLevel(Skills.SkillType.Farming) + p.GetSkillLevel(Skills.SkillType.Cooking) + p.GetSkillLevel(Skills.SkillType.BloodMagic);
            float guile = p.GetSkillLevel(Skills.SkillType.Sneak) + p.GetSkillLevel(Skills.SkillType.Knives);
            float best = Mathf.Max(Mathf.Max(melee, wit), Mathf.Max(care, guile));
            if (best <= 0.5f) return null;
            return best == melee ? "gryffindor" : best == wit ? "ravenclaw" : best == care ? "hufflepuff" : "slytherin";
        }

        private static void House(Player p, WizardState w)
        {
            if (w.House != null)
            {
                GUILayout.Label(Texts.L("Ваш факультет: ", "Your house: ") + HouseName(w.House), s_title);
                var n = TreeDb.Get(TreeDb.HouseStart(w.House));
                if (n != null) GUILayout.Label(n.Text, s_wrap);
                GUILayout.Space(10f);
                GUILayout.Label(Texts.L("Сменить факультет можно в «Журнале» (трофей босса и 100 монет у Кодекса).",
                    "You may change house in the Ledger (a boss trophy and 100 coins, at a Codex)."), s_small);
                return;
            }

            GUILayout.Label(Texts.L("Распределяющая шляпа ждёт. Выберите факультет: он даёт стартовое заклинание, первое место на Древе и пассивный бонус.",
                "The Sorting Hat is waiting. Choose a house: it gives a starting spell, your place on the Tree and a passive bonus."), s_wrap);
            string hint = Suggest(p);
            if (hint != null)
            {
                GUILayout.Label(Texts.L("Шляпа шепчет: «Лучше всего… ", "The Hat whispers: \"Better be… ") + HouseName(hint).ToUpperInvariant() + "!»", s_text);
            }

            GUILayout.Space(8f);
            foreach (var h in s_houses)
            {
                var n = TreeDb.Get(TreeDb.HouseStart(h));
                GUILayout.BeginHorizontal(s_box);
                GUILayout.BeginVertical(GUILayout.Width(900f));
                GUILayout.Label(HouseName(h), s_title);
                if (n != null) GUILayout.Label(n.Text, s_wrap);
                var spell = SpellDb.Get(WizardState.TreeData_HouseSpell(h));
                if (spell != null) GUILayout.Label(Texts.L("Стартовое заклинание: ", "Starting spell: ") + spell.Name, s_small);
                GUILayout.EndVertical();
                if (GUILayout.Button(Texts.L("Выбрать", "Choose"), s_button, GUILayout.Width(140f), GUILayout.Height(48f)))
                {
                    SetHouse(w, h);
                    Casting.Message(Texts.L("Шляпа решила: ", "The Hat has decided: ") + HouseName(h) + "!");
                    s_tab = 1;
                }

                GUILayout.EndHorizontal();
            }
        }

        private static void SetHouse(WizardState w, string h)
        {
            if (w.House != null)
            {
                w.Nodes.Remove(TreeDb.HouseStart(w.House));
                w.ResetTree();
            }

            w.House = h;
            w.Nodes.Add(TreeDb.HouseStart(h));
            string spell = WizardState.TreeData_HouseSpell(h);
            if (spell != null)
            {
                w.Known.Add(spell);
                if (w.Bar[0] == null) w.Bar[0] = spell;
            }

            Wizard.Changed();
        }

        // ------------------------------------------------------------------ tree
        private static void Tree(Player p, WizardState w)
        {
            GUILayout.BeginHorizontal();
            var canvas = GUILayoutUtility.GetRect(820f, 640f, GUILayout.Width(820f), GUILayout.Height(640f));
            DrawTree(canvas, w);
            GUILayout.BeginVertical(GUILayout.Width(340f));
            NodePanel(p, w);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private static Vector2 ToScreen(Rect canvas, float x, float y)
        {
            float u = 33f * s_zoom;
            return new Vector2(canvas.center.x + s_pan.x + x * u, canvas.center.y + s_pan.y - y * u);
        }

        private static void DrawTree(Rect canvas, WizardState w)
        {
            SpellHud.Box(canvas, new Color(0.02f, 0.02f, 0.04f, 1f));
            var e = Event.current;
            if (canvas.Contains(e.mousePosition))
            {
                if (e.type == EventType.ScrollWheel)
                {
                    float before = s_zoom;
                    s_zoom = Mathf.Clamp(s_zoom * (e.delta.y > 0 ? 0.9f : 1.1f), 0.5f, 4f);
                    Vector2 m = e.mousePosition - canvas.center - s_pan;
                    s_pan -= m * (s_zoom / before - 1f);
                    e.Use();
                }
                else if (e.type == EventType.MouseDown && e.button == 2)
                {
                    s_dragging = true;
                    s_dragFrom = e.mousePosition;
                    e.Use();
                }
            }

            if (s_dragging && e.type == EventType.MouseDrag)
            {
                s_pan += e.mousePosition - s_dragFrom;
                s_dragFrom = e.mousePosition;
                e.Use();
            }

            if (e.type == EventType.MouseUp && e.button == 2)
            {
                s_dragging = false;
            }

            GUI.BeginClip(canvas);
            var local = new Rect(0f, 0f, canvas.width, canvas.height);
            var nodes = TreeDb.Nodes;
            NodeDef hover = null;
            List<int> path = null;
            if (local.Contains(e.mousePosition))
            {
                float best = 12f;
                foreach (var n in nodes)
                {
                    float d = Vector2.Distance(ToScreen(local, n.X, n.Y), e.mousePosition);
                    if (d < best)
                    {
                        best = d;
                        hover = n;
                    }
                }
            }

            if (hover != null && !w.Allocated(hover.Id))
            {
                path = w.PathTo(hover);
            }

            var pathSet = path != null ? new HashSet<int>(path) : new HashSet<int>();
            var edges = TreeDb.Edges;
            for (int i = 0; i + 1 < edges.Length; i += 2)
            {
                var a = nodes[edges[i]];
                var b = nodes[edges[i + 1]];
                bool on = w.Allocated(a.Id) && w.Allocated(b.Id);
                bool lit = (pathSet.Contains(a.Index) || w.Allocated(a.Id)) && (pathSet.Contains(b.Index) || w.Allocated(b.Id)) && !on;
                Color c = on ? new Color(1f, 0.85f, 0.45f, 0.9f) : lit ? new Color(0.5f, 1f, 0.6f, 0.9f) : new Color(0.35f, 0.35f, 0.4f, 0.45f);
                Line(ToScreen(local, a.X, a.Y), ToScreen(local, b.X, b.Y), c, on || lit ? 3f : 1.5f);
            }

            foreach (var n in nodes)
            {
                Vector2 pos = ToScreen(local, n.X, n.Y);
                if (pos.x < -20f || pos.y < -20f || pos.x > local.width + 20f || pos.y > local.height + 20f)
                {
                    continue;
                }

                float size = NodeSize(n) * Mathf.Sqrt(s_zoom);
                Color sc = n.School != null ? TreeDb.SchoolColor(n.School) : new Color(1f, 0.85f, 0.5f);
                bool taken = w.Nodes.Contains(n.Id);
                bool pending = w.Pending.Contains(n.Id);
                bool year = w.YearAllows(n);
                Color fill = taken ? sc : pending ? Color.Lerp(sc, Color.yellow, 0.5f) : pathSet.Contains(n.Index) ? new Color(0.5f, 1f, 0.6f) :
                    year ? sc * 0.35f : new Color(0.15f, 0.15f, 0.15f);
                fill.a = 1f;
                var r = new Rect(pos.x - size * 0.5f, pos.y - size * 0.5f, size, size);
                var old = GUI.color;
                GUI.color = fill;
                GUI.DrawTexture(r, n.Type == "small" ? Fx.Dot() : Fx.Ring());
                if (taken || pending || n == hover)
                {
                    GUI.color = pending ? Color.yellow : n == hover ? Color.white : new Color(1f, 0.9f, 0.6f);
                    GUI.DrawTexture(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), Fx.Ring());
                }

                GUI.color = old;
                if (s_zoom > 1.6f && n.Type != "small")
                {
                    GUI.Label(new Rect(pos.x - 60f, pos.y + size * 0.5f, 120f, 18f), n.Name, s_small);
                }
            }

            if (hover != null)
            {
                Vector2 tp = e.mousePosition + new Vector2(16f, 8f);
                string tip = hover.Name + "\n" + hover.Text + (path != null ? "\n" + Texts.L("Путь: ", "Path: ") + path.Count + Texts.L(" очк.", " pts") : "") +
                             (!w.YearAllows(hover) ? "\n" + Texts.L("Откроется на курсе ", "Opens in year ") + hover.Year : "");
                var size = s_wrap.CalcHeight(new GUIContent(tip), 300f);
                var tr = new Rect(Mathf.Min(tp.x, local.width - 310f), Mathf.Min(tp.y, local.height - size - 10f), 310f, size + 10f);
                SpellHud.Box(tr, new Color(0f, 0f, 0f, 0.9f));
                GUI.Label(new Rect(tr.x + 5f, tr.y + 5f, 300f, size), tip, s_wrap);
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    s_node = hover.Id;
                    if (path != null && path.Count <= w.PointsFree)
                    {
                        foreach (int i in path) w.Pending.Add(nodes[i].Id);
                    }
                    else if (path != null)
                    {
                        Casting.Message(Texts.L("Не хватает очков Древа.", "Not enough tree points."));
                    }

                    e.Use();
                }
                else if (e.type == EventType.MouseDown && e.button == 1)
                {
                    s_node = hover.Id;
                    if (w.Pending.Contains(hover.Id) && w.StaysConnectedWithout(hover.Id))
                    {
                        w.Pending.Remove(hover.Id);
                    }

                    e.Use();
                }
            }

            GUI.EndClip();
            GUI.Label(new Rect(canvas.x + 8f, canvas.yMax - 22f, canvas.width, 20f),
                Texts.L("ЛКМ — взять путь до узла · ПКМ — убрать невзятый · колесо — масштаб · СКМ — двигать", "LMB take the path · RMB drop a pending node · wheel zoom · MMB pan"),
                s_small);
        }

        private static float NodeSize(NodeDef n)
        {
            switch (n.Type)
            {
                case "keystone": return 26f;
                case "house": return 28f;
                case "notable": case "mastery": case "socket": return 18f;
                case "spell": return 20f;
                default: return 10f;
            }
        }

        private static void Line(Vector2 a, Vector2 b, Color c, float width)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.5f)
            {
                return;
            }

            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(ang, a);
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, len, width), Texture2D.whiteTexture);
            GUI.color = old;
            GUI.matrix = m;
        }

        private static void NodePanel(Player p, WizardState w)
        {
            bool codex = NearCodex(p);
            GUILayout.Label(Texts.L("Ожидают подтверждения: ", "Pending: ") + w.Pending.Count, s_text);
            GUILayout.BeginHorizontal();
            GUI.enabled = w.Pending.Count > 0;
            if (GUILayout.Button(Texts.L("Подтвердить", "Confirm"), s_button))
            {
                w.ConfirmPending();
                Wizard.Changed();
                Casting.Message(Texts.L("Древо выросло.", "The tree has grown."));
            }

            if (GUILayout.Button(Texts.L("Отменить", "Cancel"), s_button))
            {
                w.Pending.Clear();
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);
            var n = s_node != null ? TreeDb.Get(s_node) : null;
            s_scrollB = GUILayout.BeginScrollView(s_scrollB, GUILayout.Height(470f));
            if (n == null)
            {
                GUILayout.Label(Texts.L("Наведите на узел, чтобы прочесть его; щёлкните, чтобы выбрать.", "Hover a node to read it; click to select."), s_wrap);
            }
            else
            {
                GUILayout.Label(n.Name, s_title);
                GUILayout.Label(TypeName(n.Type) + (n.School != null ? " · " + SchoolName(n.School) : "") + " · " + Texts.L("курс ", "year ") + n.Year, s_small);
                GUILayout.Label(n.Text, s_wrap);
                if (n.Spell != null)
                {
                    var s = SpellDb.Get(n.Spell);
                    if (s != null) GUILayout.Label(Texts.L("Заклинание: ", "Spell: ") + s.Name + " — " + s.Effect, s_wrap);
                }

                if (n.Type == "mastery" && w.Nodes.Contains(n.Id))
                {
                    GUILayout.Label(Texts.L("Мастерство — выберите одно:", "Mastery — choose one:"), s_text);
                    int cur;
                    w.Mastery.TryGetValue(n.Id, out cur);
                    if (!w.Mastery.ContainsKey(n.Id)) cur = -1;
                    for (int i = 0; i < n.Options.Length; i++)
                    {
                        if (GUILayout.Toggle(cur == i, n.Options[i], s_button) && cur != i)
                        {
                            w.Mastery[n.Id] = i;
                            Wizard.Changed();
                        }
                    }
                }

                if (n.Type == "socket" && w.Nodes.Contains(n.Id))
                {
                    string mods;
                    GUILayout.Label(Texts.L("Гнездо Омута: ", "Pensieve socket: ") + (w.Sockets.TryGetValue(n.Id, out mods) ? mods : Texts.L("пусто", "empty")), s_wrap);
                    foreach (var it in p.GetInventory().GetAllItems().ToList())
                    {
                        string prefab = Items.PrefabOf(it) ?? "";
                        if (!prefab.StartsWith(Items.Memory))
                        {
                            continue;
                        }

                        string m = Items.MemoryMods(it);
                        GUI.enabled = codex;
                        if (GUILayout.Button(Texts.L("Вставить: ", "Insert: ") + m, s_button))
                        {
                            w.Sockets[n.Id] = m;
                            p.GetInventory().RemoveItem(it, 1);
                            Wizard.Changed();
                        }

                        GUI.enabled = true;
                    }
                }

                if (w.Nodes.Contains(n.Id) && n.Type != "house")
                {
                    bool canRefund = codex && w.StaysConnectedWithout(n.Id) && (w.FreeRefunds > 0 || Items.Count(p, Items.Scroll) > 0);
                    GUI.enabled = canRefund;
                    if (GUILayout.Button(Texts.L("Обливиэйт: снять узел", "Obliviate: remove the node") +
                                         (w.FreeRefunds > 0 ? " (" + w.FreeRefunds + Texts.L(" бесплатно)", " free)") : Texts.L(" (свиток)", " (scroll)")), s_button))
                    {
                        if (w.FreeRefunds > 0) w.FreeRefunds--;
                        else Items.Take(p, Items.Scroll);
                        w.Nodes.Remove(n.Id);
                        w.Mastery.Remove(n.Id);
                        w.Sockets.Remove(n.Id);
                        if (n.Spell != null) w.Known.Remove(n.Spell);
                        Wizard.Changed();
                    }

                    GUI.enabled = true;
                    if (!codex) GUILayout.Label(Texts.L("Снимать узлы можно только у Кодекса.", "Nodes are removed only at a Codex."), s_small);
                }
            }

            GUILayout.EndScrollView();
            GUILayout.Label(Texts.L("Вклад в характеристики:", "What the tree gives:"), s_text);
            s_scrollC = GUILayout.BeginScrollView(s_scrollC, GUILayout.Height(80f));
            GUILayout.Label(StatSummary(w), s_small);
            GUILayout.EndScrollView();
        }

        private static string StatSummary(WizardState w)
        {
            var parts = new List<string>();
            for (int i = 0; i < (int)Stat.Count; i++)
            {
                float v = w.Stats.Get((Stat)i);
                if (Mathf.Abs(v) > 0.01f) parts.Add(((Stat)i) + " " + (v > 0 ? "+" : "") + v.ToString("0"));
            }

            return parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "—";
        }

        private static string TypeName(string t)
        {
            switch (t)
            {
                case "small": return Texts.L("малый узел", "small node");
                case "notable": return Texts.L("значимый узел", "notable");
                case "keystone": return Texts.L("краеугольный камень", "keystone");
                case "spell": return Texts.L("узел заклинания", "spell node");
                case "mastery": return Texts.L("мастерство", "mastery");
                case "socket": return Texts.L("гнездо Омута", "socket");
                default: return Texts.L("факультет", "house");
            }
        }

        private static string SchoolName(string code)
        {
            var s = TreeDb.School(code);
            return s == null ? code : Texts.English ? s.NameEn : s.Name;
        }

        // ------------------------------------------------------------------ spells
        private static IEnumerable<SpellDef> Visible(WizardState w)
        {
            return SpellDb.All.Where(s => s.Castable && (w.KnowsSpell(s.Id) || s.Role == "basic") && (s_school == null || s.School == s_school))
                .OrderBy(s => s.Year).ThenBy(s => s.Name);
        }

        private static void Spells(Player p, WizardState w)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Texts.L("Все", "All"), s_school == null ? s_tabOn : s_tabOff, GUILayout.Width(60f))) s_school = null;
            foreach (var sc in TreeDb.Schools)
            {
                if (GUILayout.Button(Texts.English ? sc.NameEn : sc.Name, s_school == sc.Code ? s_tabOn : s_tabOff)) s_school = sc.Code;
            }

            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            s_scrollA = GUILayout.BeginScrollView(s_scrollA, GUILayout.Width(360f), GUILayout.Height(620f));
            foreach (var s in Visible(w))
            {
                string label = s.Name + "  r" + w.Rank(s.Id) + (s.Year > w.Year ? Texts.L(" (курс ", " (year ") + s.Year + ")" : "");
                if (GUILayout.Button(label, s_spell == s.Id ? s_tabOn : s_button))
                {
                    s_spell = s.Id;
                }
            }

            int unknown = SpellDb.All.Count(s => s.Castable && !w.KnowsSpell(s.Id));
            GUILayout.Label(Texts.L("Ещё не изучено: ", "Not learned yet: ") + unknown, s_small);
            GUILayout.EndScrollView();
            GUILayout.BeginVertical();
            s_scrollB = GUILayout.BeginScrollView(s_scrollB, GUILayout.Height(620f));
            var sp = s_spell != null ? SpellDb.Get(s_spell) : null;
            if (sp == null)
            {
                GUILayout.Label(Texts.L("Выберите заклинание слева. Заклинания приходят с Древа, из книг (вкладка «Книги») и как варианты на рангах 3/6/9.",
                    "Pick a spell on the left. Spells come from the tree, from books (Books tab) and as variants at ranks 3/6/9."), s_wrap);
            }
            else
            {
                SpellPanel(p, w, sp);
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private static void SpellPanel(Player p, WizardState w, SpellDef s)
        {
            GUILayout.Label(s.Name + (string.IsNullOrEmpty(s.Incantation) ? "" : "  «" + s.Incantation + "»"), s_title);
            GUILayout.Label(SchoolName(s.School) + " · " + Texts.L("курс ", "year ") + s.Year + " · " + RoleName(s.Role), s_small);
            GUILayout.Label(s.Effect, s_wrap);
            if (!string.IsNullOrEmpty(s.Vfx)) GUILayout.Label(s.Vfx, s_small);
            int rank = w.Rank(s.Id);
            float xp;
            w.Xp.TryGetValue(s.Id, out xp);
            float next = WizardState.XpForRank(Mathf.Min(10, rank + 1));
            GUILayout.Label(Texts.L("Ранг ", "Rank ") + rank + " / " + w.MaxRank + "   " + Texts.L("опыт ", "xp ") + Mathf.FloorToInt(xp) + (rank < 10 ? " / " + Mathf.CeilToInt(next) : ""), s_text);
            GUILayout.Label(Texts.L("Стоимость: ", "Cost: ") + Mathf.RoundToInt(Casting.Cost(p, w, s)) + Texts.L(" МС · перезарядка ", " eitr · cooldown ") +
                            s.BaseCooldown.ToString("0.#") + Texts.L(" с · сила ", " s · power ") + Mathf.RoundToInt(Combat.Power(p, w, s)), s_small);
            if (s.IsRitual) GUILayout.Label(Texts.L("Ритуал: ", "Ritual: ") + Rituals.Describe(s), s_small);

            // the bar
            GUILayout.Space(6f);
            if (s.IsGrand)
            {
                if (GUILayout.Button(w.Grand == s.Id ? Texts.L("✓ В слоте великих чар", "✓ In the grand slot") : Texts.L("Поставить в слот великих чар (G)", "Put in the grand slot (G)"), s_button))
                {
                    w.Grand = s.Id;
                    Wizard.Changed();
                }
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(Texts.L("В панель:", "To the bar:"), s_text, GUILayout.Width(90f));
                for (int i = 0; i < 8; i++)
                {
                    if (GUILayout.Button((w.Bar[i] == s.Id ? "✓" : "") + (i + 1), s_button, GUILayout.Width(44f)))
                    {
                        for (int k = 0; k < 8; k++) if (w.Bar[k] == s.Id) w.Bar[k] = null;
                        w.Bar[i] = s.Id;
                        Wizard.Changed();
                    }
                }

                if (GUILayout.Button(Texts.L("Убрать", "Remove"), s_button, GUILayout.Width(80f)))
                {
                    for (int k = 0; k < 8; k++) if (w.Bar[k] == s.Id) w.Bar[k] = null;
                    Wizard.Changed();
                }

                GUILayout.EndHorizontal();
            }

            // patterns
            GUILayout.Space(8f);
            GUILayout.Label(Texts.L("Узоры (ранги 3, 6, 9)", "Patterns (ranks 3, 6, 9)"), s_text);
            foreach (int r in new[] { 3, 6, 9 })
            {
                var choices = Patterns.Choices(s, r);
                string cur = w.AugChoice(s.Id, r);
                GUILayout.BeginHorizontal();
                GUILayout.Label("r" + r, s_text, GUILayout.Width(30f));
                GUI.enabled = rank >= r;
                foreach (var ch in choices)
                {
                    if (GUILayout.Toggle(cur == ch.Key, ch.Value, s_button, GUILayout.Width(390f)) && cur != ch.Key)
                    {
                        w.SetAugChoice(s.Id, r, ch.Key);
                        Wizard.Changed();
                        var v = SpellDb.Get(ch.Key);
                        if (v != null) Casting.Message(Texts.L("Открыт вариант: ", "Variant opened: ") + v.Name);
                    }
                }

                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }

            // metamagic
            int slots = w.MetaSlots(s.Id);
            var metas = w.Metas(s.Id);
            GUILayout.Space(8f);
            GUILayout.Label(Texts.L("Метамагия: слотов ", "Metamagic: slots ") + slots + Texts.L(" (открываются на рангах 2, 5, 8)", " (open at ranks 2, 5, 8)"), s_text);
            var known = SpellDb.All.Where(m => m.IsMeta && w.Known.Contains(m.Id)).ToList();
            if (known.Count == 0)
            {
                GUILayout.Label(Texts.L("Метамагию изучают по книгам и значимым узлам.", "Metamagic is learned from books and notables."), s_small);
            }

            foreach (var m in known)
            {
                bool on = metas.Contains(m.Id);
                GUI.enabled = on || metas.Count < slots;
                bool want = GUILayout.Toggle(on, m.Name + " — " + m.Effect, s_button);
                GUI.enabled = true;
                if (want != on)
                {
                    if (want) metas.Add(m.Id);
                    else metas.Remove(m.Id);
                    w.Meta[s.Id] = string.Join(",", metas.ToArray());
                    Wizard.Changed();
                }
            }

            // practice book
            if (Items.Count(p, Items.Practice) > 0)
            {
                GUILayout.Space(8f);
                GUI.enabled = NearCodex(p);
                if (GUILayout.Button(Texts.L("Прочитать «Книгу практики» (+300 опыта)", "Read a Practice book (+300 xp)"), s_button))
                {
                    Items.Take(p, Items.Practice);
                    Casting.AddXp(p, w, s, 300f / Mathf.Max(0.1f, ModConfig.RankXpMultiplier.Value));
                    Wizard.Changed();
                }

                GUI.enabled = true;
            }
        }

        private static string RoleName(string r)
        {
            switch (r)
            {
                case "tree": return Texts.L("с Древа", "from the tree");
                case "book": return Texts.L("из книги", "from a book");
                case "var": return Texts.L("вариант", "variant");
                case "meta": return Texts.L("метамагия", "metamagic");
                case "ritual": return Texts.L("ритуал", "ritual");
                case "grand": return Texts.L("великие чары", "grand spell");
                case "prank": return Texts.L("розыгрыш", "prank");
                case "basic": return Texts.L("базовое", "basic");
                default: return r;
            }
        }

        // ------------------------------------------------------------------ books, cards, trophies
        private static void Books(Player p, WizardState w)
        {
            bool codex = NearCodex(p);
            if (!codex)
            {
                GUILayout.Label(Texts.L("Читать книги и показывать трофеи можно у Кодекса (до 10 м).", "Books are read and trophies shown at a Codex (within 10 m)."), s_text);
            }

            s_scrollA = GUILayout.BeginScrollView(s_scrollA, GUILayout.Height(640f));
            GUILayout.Label(Texts.L("Трофеи боссов (курс = 1 + показанные трофеи; трофей не тратится)", "Boss trophies (year = 1 + trophies shown; the trophy is kept)"), s_title);
            GUILayout.Label(Texts.L("Показано: ", "Shown: ") + (w.Trophies.Count == 0 ? "—" : string.Join(", ", w.Trophies.ToArray())), s_small);
            foreach (var it in p.GetInventory().GetAllItems().ToList())
            {
                string boss = Loot.BossOfTrophy(Items.PrefabOf(it));
                if (boss == null || w.Trophies.Contains(boss))
                {
                    continue;
                }

                GUI.enabled = codex;
                if (GUILayout.Button(Texts.L("Показать Кодексу: ", "Show to the Codex: ") + Localization.instance.Localize(it.m_shared.m_name), s_button))
                {
                    w.Trophies.Add(boss);
                    Wizard.Changed();
                    Fx.Send(FxKind.Ceremony, p.transform.position, Vector3.up, "", 0f, "year");
                    Casting.Message(Texts.L("Вы перешли на ", "You are now in year ") + w.Year + Texts.L(" курс!", "!"));
                }

                GUI.enabled = true;
            }

            GUILayout.Space(10f);
            GUILayout.Label(Texts.L("Книги и находки", "Books and finds"), s_title);
            bool any = false;
            foreach (var it in p.GetInventory().GetAllItems().ToList())
            {
                string prefab = Items.PrefabOf(it) ?? "";
                string action = BookAction(w, prefab);
                if (action == null)
                {
                    continue;
                }

                any = true;
                GUILayout.BeginHorizontal(s_box);
                GUILayout.Label(Localization.instance.Localize(it.m_shared.m_name) + (it.m_stack > 1 ? " ×" + it.m_stack : "") + " — " + action, s_wrap, GUILayout.Width(900f));
                GUI.enabled = codex;
                if (GUILayout.Button(Texts.L("Прочитать", "Read"), s_button, GUILayout.Width(140f)))
                {
                    Read(p, w, it, prefab);
                }

                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }

            if (!any)
            {
                GUILayout.Label(Texts.L("Нет книг. Книги, свитки и чашу делают у Кодекса; вкладыши и воспоминания находят в сокровищницах.",
                    "No books. Books, scrolls and the cup are made at a Codex; cards and memories are found in treasure chests."), s_wrap);
            }

            GUILayout.Space(10f);
            GUILayout.Label(Texts.L("Прочитанные книги: ", "Books read: ") + (w.Books.Count == 0 ? "—" : string.Join(", ", w.Books.OrderBy(b => b).ToArray())), s_small);
            GUILayout.Label(Texts.L("Вкладыши: ", "Cards: ") + w.Cards.Count + "/" + Items.CardNames.Length, s_small);
            GUILayout.EndScrollView();
        }

        private static string BookAction(WizardState w, string prefab)
        {
            if (prefab == Items.Weasley) return Texts.L("учит розыгрышам", "teaches the pranks");
            if (prefab == Items.Rituals) return Texts.L("учит ритуалам", "teaches the rituals");
            if (prefab == Items.Grimoire) return Texts.L("учит великим чарам", "teaches the grand spells");
            if (prefab == Items.Scroll) return Texts.L("+1 бесплатное снятие узла Древа", "+1 free node removal");
            if (prefab == Items.Cup) return Texts.L("полный сброс Древа (очки вернутся)", "a full tree reset (points come back)");
            if (prefab == Items.Card) return Texts.L("+1 очко Древа (каждый волшебник — один раз)", "+1 tree point (each wizard once)");
            if (prefab.StartsWith(Items.Book))
            {
                int y;
                if (int.TryParse(prefab.Substring(Items.Book.Length), out y)) return Texts.L("учит заклинаниям ", "teaches the spells of year ") + y + Texts.L(" курса", "");
            }

            return null;
        }

        private static void Read(Player p, WizardState w, ItemDrop.ItemData it, string prefab)
        {
            int learned = 0;
            if (prefab == Items.Weasley) learned = Learn(w, s => s.Role == "prank");
            else if (prefab == Items.Rituals) learned = Learn(w, s => s.Role == "ritual");
            else if (prefab == Items.Grimoire) learned = Learn(w, s => s.Role == "grand");
            else if (prefab == Items.Scroll) w.FreeRefunds++;
            else if (prefab == Items.Cup)
            {
                w.ResetTree();
                Casting.Message(Texts.L("Омут памяти: Древо очищено.", "Pensieve: the tree is cleared."));
            }
            else if (prefab == Items.Card)
            {
                var unread = Enumerable.Range(0, Items.CardNames.Length).Where(i => !w.Cards.Contains(Items.CardNamesEn[i])).ToList();
                if (unread.Count == 0)
                {
                    Casting.Message(Texts.L("Этого волшебника вы уже знаете.", "You already know this wizard."));
                }
                else
                {
                    int i = unread[Random.Range(0, unread.Count)];
                    w.Cards.Add(Items.CardNamesEn[i]);
                    Casting.Message(Texts.L("Вкладыш: ", "Card: ") + (Texts.English ? Items.CardNamesEn[i] : Items.CardNames[i]) + Texts.L(" — +1 очко Древа", " — +1 tree point"));
                }
            }
            else if (prefab.StartsWith(Items.Book))
            {
                int y;
                if (int.TryParse(prefab.Substring(Items.Book.Length), out y))
                {
                    w.Books.Add(y.ToString());
                    learned = Learn(w, s => (s.Role == "book" || s.Role == "meta") && (s.Year == y || (y == 7 && s.Year >= 7)));
                }
            }

            bool book = prefab.StartsWith(Items.Book);
            if (!book || learned > 0)
            {
                p.GetInventory().RemoveItem(it, 1);
            }
            else
            {
                Casting.Message(Texts.L("Всё из этой книги вы уже знаете.", "You already know everything in this book."));
            }

            if (learned > 0)
            {
                Casting.Message(Texts.L("Изучено заклинаний: ", "Spells learned: ") + learned);
                Fx.Send(FxKind.Buff, p.transform.position, Vector3.up, "", 0f);
            }

            Wizard.Changed();
        }

        private static int Learn(WizardState w, System.Func<SpellDef, bool> which)
        {
            int n = 0;
            foreach (var s in SpellDb.All)
            {
                if (which(s) && w.Known.Add(s.Id))
                {
                    n++;
                }
            }

            return n;
        }

        // ------------------------------------------------------------------ wand
        private static void Wand(Player p, WizardState w)
        {
            var wand = Wands.Equipped(p) ?? Wands.Best(p);
            if (wand == null)
            {
                GUILayout.Label(Texts.L("У вас нет палочки. Её делают у Кодекса (молот → Кодекс, затем станция крафта).",
                    "You have no wand. Wands are made at a Codex (build it with the hammer, then craft)."), s_wrap);
                return;
            }

            var wood = Wands.WoodOf(wand);
            GUILayout.Label(Localization.instance.Localize(wand.m_shared.m_name) + (Wands.Equipped(p) == null ? Texts.L(" (в сумке)", " (in the bag)") : ""), s_title);
            if (wood != null)
            {
                GUILayout.Label(Texts.L("Тир ", "Tier ") + wood.Tier + Texts.L(" · сила ×", " · power ×") + wood.Power.ToString("0.00") + Texts.L(" · МС +", " · eitr +") + wood.Eitr +
                                Texts.L(" · сродство: ", " · affinity: ") + string.Join(", ", wood.Affinity.Select(SchoolName).ToArray()), s_text);
            }

            var core = Wands.CoreOf(wand);
            GUILayout.Label(Texts.L("Сердцевина: ", "Core: ") + (core != null ? Localization.instance.Localize(core.Token) + " — " + core.Mods : Texts.L("нет", "none")), s_wrap);
            GUILayout.Space(10f);
            bool codex = NearCodex(p);
            GUILayout.Label(Texts.L("Сердцевины в сумке (вставка у Кодекса, старая теряется):", "Cores in the bag (set at a Codex; the old one is lost):"), s_text);
            foreach (var it in p.GetInventory().GetAllItems().ToList())
            {
                string prefab = Items.PrefabOf(it) ?? "";
                if (!prefab.StartsWith("WZ_Core_"))
                {
                    continue;
                }

                string key = prefab.Substring(8);
                GUI.enabled = codex;
                if (GUILayout.Button(Texts.L("Вставить: ", "Set: ") + Localization.instance.Localize(it.m_shared.m_name), s_button))
                {
                    Wands.SetCore(wand, key);
                    p.GetInventory().RemoveItem(it, 1);
                    Wizard.Changed();
                    Fx.Send(FxKind.Ceremony, p.transform.position, Vector3.up, "", 0f, "core");
                }

                GUI.enabled = true;
            }
        }

        // ------------------------------------------------------------------ specialisation
        private static void Calling(Player p, WizardState w)
        {
            if (w.Year < 5)
            {
                GUILayout.Label(Texts.L("Призвание выбирают с 5 курса (после четвёртого показанного трофея).", "A calling is chosen from year 5 (after the fourth trophy)."), s_wrap);
            }

            s_scrollA = GUILayout.BeginScrollView(s_scrollA, GUILayout.Height(640f));
            var spec = TreeDb.Spec(w.Spec);
            if (spec == null)
            {
                foreach (var sd in TreeDb.Specs)
                {
                    GUILayout.BeginHorizontal(s_box);
                    GUILayout.Label(sd.Name + " — " + sd.Role, s_text, GUILayout.Width(900f));
                    GUI.enabled = w.Year >= 5 && NearCodex(p);
                    if (GUILayout.Button(Texts.L("Избрать", "Choose"), s_button, GUILayout.Width(140f)))
                    {
                        w.Spec = sd.Id;
                        w.SpecNodes.Clear();
                        w.SpecNodes.Add(sd.Nodes[0].Id);
                        Wizard.Changed();
                    }

                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                }
            }
            else
            {
                GUILayout.Label(spec.Name + " — " + spec.Role, s_title);
                GUILayout.Label(Texts.L("Очки призвания: ", "Calling points: ") + w.SpecPointsFree + " / " + w.SpecPointsTotal + Texts.L(" (по 2 за курсы 5–8)", " (2 per year 5–8)"), s_text);
                for (int i = 0; i < spec.Nodes.Length; i++)
                {
                    var n = spec.Nodes[i];
                    bool taken = w.SpecNodes.Contains(n.Id);
                    bool reach = spec.Adj[i].Any(j => w.SpecNodes.Contains(spec.Nodes[j].Id));
                    GUILayout.BeginHorizontal(s_box);
                    GUILayout.Label((taken ? "✦ " : "") + n.Name + " — " + n.Text, s_wrap, GUILayout.Width(900f));
                    GUI.enabled = !taken && reach && w.SpecPointsFree > 0;
                    if (GUILayout.Button(taken ? Texts.L("взято", "taken") : Texts.L("Взять", "Take"), s_button, GUILayout.Width(140f)))
                    {
                        w.SpecNodes.Add(n.Id);
                        Wizard.Changed();
                    }

                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                }

                GUILayout.Space(10f);
                GUI.enabled = NearCodex(p) && Items.Count(p, Items.Cup) > 0;
                if (GUILayout.Button(Texts.L("Сменить призвание (Чаша Омута)", "Change calling (a Pensieve cup)"), s_button))
                {
                    Items.Take(p, Items.Cup);
                    w.Spec = null;
                    w.SpecNodes.Clear();
                    Wizard.Changed();
                }

                GUI.enabled = true;
            }

            GUILayout.EndScrollView();
        }

        // ------------------------------------------------------------------ bestiary
        private static void Bestiary(WizardState w)
        {
            GUILayout.Label(Texts.L("Победы и изученные существа дают бонус урона по виду: 10/25/50/100 побед → +2/4/6/8%, изучение (Информус) → +3%.",
                "Kills and studies give damage against the kind: 10/25/50/100 kills → +2/4/6/8%, a study (Informous) → +3%."), s_wrap);
            s_scrollA = GUILayout.BeginScrollView(s_scrollA, GUILayout.Height(600f));
            var all = new HashSet<string>(w.Kills.Keys);
            all.UnionWith(w.Informs.Keys);
            foreach (var k in all.OrderByDescending(x => w.Kills.ContainsKey(x) ? w.Kills[x] : 0))
            {
                int kills;
                w.Kills.TryGetValue(k, out kills);
                GUILayout.Label(Creature(k) + " — " + kills + Texts.L(" побед", " kills") + (w.Informs.ContainsKey(k) ? Texts.L(", изучен", ", studied") : ""), s_text);
            }

            if (all.Count == 0) GUILayout.Label("—", s_text);
            GUILayout.EndScrollView();
        }

        private static string Creature(string prefab)
        {
            var go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null;
            var c = go != null ? go.GetComponent<Character>() : null;
            return c != null && Localization.instance != null ? Localization.instance.Localize(c.m_name) : prefab;
        }

        // ------------------------------------------------------------------ ledger
        private static readonly string[] s_forms = { "Deer", "Wolf", "Boar", "Lox", "Hare", "Fox", "Hen", "Neck" };

        private static void Ledger(Player p, WizardState w)
        {
            s_scrollA = GUILayout.BeginScrollView(s_scrollA, GUILayout.Height(640f));
            GUILayout.Label(Texts.L("Очки Древа: навык ", "Tree points: skill ") + w.SkillHigh + Texts.L(" + факультет ", " + house ") + (w.House != null ? 2 : 0) +
                            Texts.L(" + трофеи ", " + trophies ") + 2 * w.Trophies.Count + Texts.L(" + вкладыши ", " + cards ") + w.Cards.Count, s_text);
            GUILayout.Label(Texts.L("Очки факультета: ", "House points: ") + w.HousePoints + Texts.L(" · заклинаний сотворено: ", " · spells cast: ") + w.CastCount, s_text);
            GUILayout.Label(Texts.L("Тьма: ", "Darkness: ") + Mathf.RoundToInt(w.Darkness) + "/100" +
                            Texts.L(" (от тёмных чар; на 50 Патронус не приходит, на 100 ночами приходят призраки)", " (from dark spells; at 50 no Patronus, at 100 wraiths come at night)"), s_wrap);
            GUILayout.Space(8f);
            GUILayout.Label(Texts.L("Облик Патронуса", "Patronus form"), s_text);
            GUILayout.BeginHorizontal();
            foreach (var f in s_forms)
            {
                if (ZNetScene.instance != null && ZNetScene.instance.GetPrefab(f) == null) continue;
                if (GUILayout.Toggle(w.Patronus == f, Creature(f), s_button) && w.Patronus != f)
                {
                    w.Patronus = f;
                    Wizard.Changed();
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(8f);
            bool codex = NearCodex(p);
            var vault = Vault.Items(p);
            GUILayout.Label(Texts.L("Тайник Кодекса (Телепортация и Исчезательный шкаф): ", "Codex vault (Teleportation, the Vanishing Cabinet): ") + vault.Count, s_text);
            for (int i = 0; i < vault.Count; i++)
            {
                var e = vault[i];
                var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(e.Prefab) : null;
                var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                string name = drop != null ? Localization.instance.Localize(drop.m_itemData.m_shared.m_name) : e.Prefab;
                GUILayout.BeginHorizontal();
                GUILayout.Label(name + " ×" + e.Stack, s_text, GUILayout.Width(500f));
                GUI.enabled = codex;
                if (GUILayout.Button(Texts.L("Забрать", "Take"), s_button, GUILayout.Width(120f)))
                {
                    Vault.Take(p, i);
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                    break;
                }

                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10f);
            GUILayout.Label(Texts.L("Пересортировка (у Кодекса: трофей любого босса из сумки остаётся, 100 монет тратятся; Древо сбрасывается)",
                "Re-sorting (at a Codex: any boss trophy in the bag, 100 coins are spent; the tree is reset)"), s_wrap);
            GUILayout.BeginHorizontal();
            bool hasTrophy = p.GetInventory().GetAllItems().Any(it => Loot.BossOfTrophy(Items.PrefabOf(it)) != null);
            foreach (var h in s_houses)
            {
                GUI.enabled = codex && hasTrophy && Items.Count(p, "Coins") >= 100 && w.House != h;
                if (GUILayout.Button(HouseName(h), s_button))
                {
                    Items.Take(p, "Coins", 100);
                    if (w.House != null) w.Known.Remove(WizardState.TreeData_HouseSpell(w.House));
                    SetHouse(w, h);
                }
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
        }
    }

    /// <summary>While the grimoire is open the mouse is free and the character does not turn or attack.</summary>
    [HarmonyPatch(typeof(GameCamera), "UpdateMouseCapture")]
    internal static class GrimoireMouse
    {
        private static bool Prefix()
        {
            if (!Grimoire.Open)
            {
                return true;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerController), "TakeInput")]
    internal static class GrimoireInput
    {
        private static void Postfix(ref bool __result)
        {
            if (Grimoire.Open)
            {
                __result = false;
            }
        }
    }
}
