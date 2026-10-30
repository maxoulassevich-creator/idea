using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Wizardry
{
    /// <summary>
    ///     The grimoire (K): an open book with three bookmarks.
    ///     Path — what to do next, the years and their exams, books to read.
    ///     Spells — learn spells for magic points at a Codex, put them in the bar, choose rank upgrades.
    ///     Talents — the star chart of six branches.
    ///     The first time it opens, the Sorting Hat chooses the house.
    /// </summary>
    internal static class Grimoire
    {
        public static bool Open;
        private static int s_tab;
        private static int s_filter = -1; // -1 = known spells, otherwise a year
        private static string s_spell;
        private static Vector2 s_listScroll, s_bookScroll, s_detailScroll;
        private static readonly string[] s_houses = { "gryffindor", "ravenclaw", "hufflepuff", "slytherin" };

        private const float BookW = 1280f, BookH = 800f;

        public static void Toggle()
        {
            Open = !Open;
            if (Open)
            {
                Fx.Sound(Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero, "page", 0.5f);
                if (Wizard.State != null && Wizard.State.Migrated)
                {
                    Wizard.State.Migrated = false;
                    Casting.Message(Texts.L("Волшебство 2.0: старое Древо заменено новыми талантами, все очки вернулись.",
                        "Wizardry 2.0: the old tree became the new talents, and all points came back."));
                }
            }
        }

        public static void ShowTab(int tab)
        {
            s_tab = tab;
            if (!Open)
            {
                Toggle();
            }
        }

        public static void OnNewCharacter(WizardState w)
        {
            Open = false;
            s_spell = null;
        }

        public static void Draw()
        {
            if (!Open)
            {
                return;
            }

            var p = Player.m_localPlayer;
            var w = Wizard.State;
            if (p == null || w == null || p.IsDead())
            {
                Open = false;
                return;
            }

            var e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Escape || e.keyCode == ModConfig.GrimoireKey.Value))
            {
                Open = false;
                e.Use();
                return;
            }

            Matrix4x4 old;
            var screen = Ui.Begin(1400f, 940f, out old);
            float bx = Mathf.Round((screen.x - BookW) * 0.5f), by = Mathf.Round((screen.y - BookH) * 0.5f + 22f);
            var book = new Rect(bx, by, BookW, BookH);

            // a dark veil over the world, the bookmarks, then the book over their lower ends
            Ui.Fill(new Rect(0f, 0f, screen.x, screen.y), new Color(0f, 0f, 0f, 0.45f));
            if (w.House != null)
            {
                Tabs(book);
            }

            Ui.Image(book, "ui_book", Color.white);
            Summary(p, w, book);
            if (Ui.Btn(new Rect(book.xMax - 40f, book.y - 18f, 48f, 44f), "×", true))
            {
                Open = false;
            }

            var left = new Rect(bx + 88f, by + 70f, 508f, 660f);
            var right = new Rect(bx + 684f, by + 70f, 508f, 660f);
            if (w.House == null)
            {
                Sorting(p, w, book);
            }
            else
            {
                switch (s_tab)
                {
                    case 0: PathLeft(p, w, left); PathRight(p, w, right); break;
                    case 1: SpellList(p, w, left); SpellDetail(p, w, right); break;
                    default: Talents(p, w, book); break;
                }
            }

            Ui.DrawTip(screen);
            GUI.matrix = old;
        }

        private static void Tabs(Rect book)
        {
            string[] names = { Texts.L("Путь", "Path"), Texts.L("Заклинания", "Spells"), Texts.L("Таланты", "Talents") };
            string[] glyphs = { "quest", "wand", "point" };
            for (int i = 0; i < names.Length; i++)
            {
                var r = new Rect(book.x + 90f + i * 206f, book.y - 48f, 196f, 62f);
                if (GUI.Button(r, "", i == s_tab ? Ui.TabOn : Ui.Tab) && s_tab != i)
                {
                    s_tab = i;
                    Fx.Sound(Player.m_localPlayer.transform.position, "page", 0.4f);
                }

                Ui.Glyph(new Rect(r.x + 16f, r.y + 10f, 30f, 30f), glyphs[i], i == s_tab ? Ui.Gold : Ui.Beige);
                Ui.Text(new Rect(r.x + 50f, r.y + 6f, r.width - 56f, 38f), names[i], i == s_tab ? Ui.TabOn : Ui.Tab);
            }
        }

        private static void Summary(Player p, WizardState w, Rect book)
        {
            var r = new Rect(book.xMax - 470f, book.y - 46f, 420f, 40f);
            Ui.Frame(r, Ui.Panel);
            string house = w.House != null ? HouseName(w.House) : Texts.L("без факультета", "no house");
            string codex = Exams.NearCodex(p) ? Texts.L("  ·  <color=#8fe39a>Кодекс рядом</color>", "  ·  <color=#8fe39a>Codex near</color>") : "";
            Ui.Text(new Rect(r.x + 14f, r.y + 10f, r.width - 20f, 24f),
                "<color=#ffd27a>✦ " + w.PointsFree + "</color>  ·  " + Texts.L("Курс ", "Year ") + w.Year + "  ·  " + house + codex, Ui.LightSmall);
            Ui.Tip(r, Texts.L("Очки магии ✦", "Magic points ✦"),
                Texts.L("Их дают уровни навыка «Волшебство», шаги пути, экзамены, вкладыши и учебники. Тратятся на заклинания и таланты.\nВсего: ",
                    "They come from Wizardry skill levels, path steps, exams, cards and textbooks. Spend them on spells and talents.\nTotal: ") +
                w.PointsTotal + Texts.L(", свободно: ", ", free: ") + w.PointsFree);
        }

        public static string HouseName(string h)
        {
            switch (h)
            {
                case "gryffindor": return Texts.L("Гриффиндор", "Gryffindor");
                case "ravenclaw": return Texts.L("Когтевран", "Ravenclaw");
                case "hufflepuff": return Texts.L("Пуффендуй", "Hufflepuff");
                case "slytherin": return Texts.L("Слизерин", "Slytherin");
                default: return h;
            }
        }

        // ================================================================== the Sorting Hat
        private static string Suggest(Player p)
        {
            float melee = p.GetSkillLevel(Skills.SkillType.Swords) + p.GetSkillLevel(Skills.SkillType.Axes) + p.GetSkillLevel(Skills.SkillType.Clubs);
            float wit = p.GetSkillLevel(Skills.SkillType.ElementalMagic) + p.GetSkillLevel(Skills.SkillType.Bows);
            float care = p.GetSkillLevel(Skills.SkillType.BloodMagic) + p.GetSkillLevel(Skills.SkillType.Farming) + p.GetSkillLevel(Skills.SkillType.Cooking);
            float guile = p.GetSkillLevel(Skills.SkillType.Sneak) + p.GetSkillLevel(Skills.SkillType.Knives);
            float best = Mathf.Max(Mathf.Max(melee, wit), Mathf.Max(care, guile));
            if (best <= 0.5f) return null;
            return best == melee ? "gryffindor" : best == wit ? "ravenclaw" : best == care ? "hufflepuff" : "slytherin";
        }

        private static void Sorting(Player p, WizardState w, Rect book)
        {
            var head = new Rect(book.x + 90f, book.y + 70f, book.width - 180f, 50f);
            Ui.Text(head, Texts.L("Распределяющая шляпа", "The Sorting Hat"), Ui.Title);
            string hint = Suggest(p);
            Ui.Text(new Rect(head.x, head.y + 52f, head.width, 60f),
                Texts.L("Выберите факультет. Он даёт первое заклинание и постоянный бонус. Шляпа присматривается к вашим навыкам",
                    "Choose your house. It gives your first spell and a lasting bonus. The Hat looks at your skills") +
                (hint != null ? Texts.L(" и шепчет: «Лучше всего… <b>", " and whispers: \"Better be… <b>") + HouseName(hint).ToUpperInvariant() + "</b>!»" : "."),
                Ui.Body);
            for (int i = 0; i < s_houses.Length; i++)
            {
                string h = s_houses[i];
                var card = new Rect(book.x + 92f + i * 276f + (i >= 2 ? 30f : 0f), book.y + 196f, 248f, 520f);
                Ui.Frame(card, h == hint ? Ui.CardOn : Ui.Card);
                Ui.Image(new Rect(card.x + 60f, card.y + 18f, 128f, 150f), "crest_" + h, Color.white);
                Ui.Text(new Rect(card.x + 10f, card.y + 176f, card.width - 20f, 34f), HouseName(h), Ui.HeadingCenter);
                var n = TreeDb.Get(TreeDb.HouseStart(h));
                Ui.Text(new Rect(card.x + 18f, card.y + 214f, card.width - 36f, 170f), n != null ? n.Text : "", Ui.Small);
                var spell = SpellDb.Get(WizardState.TreeData_HouseSpell(h));
                if (spell != null)
                {
                    Ui.SpellIcon(new Rect(card.x + 18f, card.y + 392f, 40f, 40f), spell);
                    Ui.Text(new Rect(card.x + 64f, card.y + 392f, card.width - 80f, 44f), Texts.L("Первое заклинание:\n", "First spell:\n") + "<b>" + spell.Name + "</b>", Ui.Small);
                }

                if (Ui.Btn(new Rect(card.x + 30f, card.y + 456f, card.width - 60f, 44f), Texts.L("Выбрать", "Choose"), true))
                {
                    SetHouse(w, h);
                    Fx.Send(FxKind.Ceremony, p.transform.position, Vector3.up, "", 0f, h);
                    Casting.Message(Texts.L("Шляпа решила: ", "The Hat has decided: ") + HouseName(h) + "!");
                    s_tab = 0;
                }
            }
        }

        private static void SetHouse(WizardState w, string h)
        {
            w.House = h;
            string spell = WizardState.TreeData_HouseSpell(h);
            if (spell != null)
            {
                w.Known.Add(spell);
                if (w.Bar[0] == null) w.Bar[0] = spell;
                w.Selected = 0;
            }

            if (w.Bar[1] == null) w.Bar[1] = "basic_cast";
            Wizard.Changed();
        }

        // ================================================================== Path
        private static void PathLeft(Player p, WizardState w, Rect page)
        {
            float y = page.y;
            Ui.Text(new Rect(page.x, y, page.width, 44f), Texts.L("Путь волшебника", "The Wizard's Path"), Ui.Title);
            y += 46f;
            var cur = Quests.Current(w);
            int done = w.Quests.Count, total = Quests.All.Count;
            Ui.Text(new Rect(page.x, y, page.width, 22f),
                (cur != null ? Texts.L("Шаг ", "Step ") + (Quests.Index(cur) + 1) + Texts.L(" из ", " of ") + total + "  ·  " : "") +
                Texts.L("пройдено ", "done ") + done + "  ·  " + Texts.L("каждый шаг даёт ✦1", "each step gives ✦1"), Ui.Small);
            y += 30f;
            if (cur == null)
            {
                var c = new Rect(page.x, y, page.width, 110f);
                Ui.Frame(c, Ui.CardOn);
                Ui.Text(new Rect(c.x + 20f, c.y + 18f, c.width - 40f, 80f), Texts.L("<b>Путь пройден!</b>\nВы — архимаг. Совершенствуйте заклинания и таланты.",
                    "<b>The path is complete!</b>\nYou are an archmage. Keep honing your spells and talents."), Ui.Body);
                y += 124f;
            }
            else
            {
                string hint = cur.Hint;
                float hintH = Ui.Body.CalcHeight(new GUIContent(hint), page.width - 92f);
                float titleH = Ui.Bold.CalcHeight(new GUIContent(cur.Title), page.width - 92f);
                bool bar = cur.Target > 1;
                float h = 22f + titleH + 8f + hintH + (bar ? 34f : 0f) + 34f;
                var c = new Rect(page.x, y, page.width, h);
                Ui.Frame(c, Ui.CardOn);
                Ui.Glyph(new Rect(c.x + 16f, c.y + 18f, 48f, 48f), "quest", Ui.InkRed);
                float tx = c.x + 76f, tw = c.width - 92f, ty = c.y + 18f;
                Ui.Text(new Rect(tx, ty, tw, titleH), cur.Title, Ui.Bold);
                ty += titleH + 8f;
                Ui.Text(new Rect(tx, ty, tw, hintH), hint, Ui.Body);
                ty += hintH + 6f;
                if (bar)
                {
                    int prog = Mathf.Min(cur.Target, cur.Progress(p, w));
                    Ui.Bar(new Rect(tx, ty + 4f, tw - 70f, 20f), prog / (float)cur.Target, new Color(0.85f, 0.55f, 0.2f));
                    Ui.Text(new Rect(tx + tw - 64f, ty + 2f, 64f, 22f), prog + " / " + cur.Target, Ui.Bold);
                    ty += 34f;
                }

                Ui.Text(new Rect(tx, ty, tw, 22f), Texts.L("Награда: <b>✦1</b>", "Reward: <b>✦1</b>"), Ui.Small);
                y += h + 14f;

                // the next steps
                Ui.Text(new Rect(page.x, y, page.width, 30f), Texts.L("Дальше", "Next"), Ui.Heading);
                y += 34f;
                int idx = Quests.Index(cur);
                for (int i = idx + 1; i < Mathf.Min(Quests.All.Count, idx + 4); i++)
                {
                    Ui.Glyph(new Rect(page.x + 4f, y + 2f, 18f, 18f), "lock", Ui.InkSoft);
                    Ui.Text(new Rect(page.x + 30f, y, page.width - 30f, 22f), Quests.All[i].Title, Ui.Small);
                    y += 24f;
                }

                y += 8f;
            }

            // books and finds to read
            var readable = p.GetInventory().GetAllItems().Where(it => Reading.What(p, w, Items.PrefabOf(it) ?? "") != null).ToList();
            Ui.Text(new Rect(page.x, y, page.width, 30f), Texts.L("Книги и находки", "Books and finds"), Ui.Heading);
            y += 34f;
            if (readable.Count == 0)
            {
                Ui.Text(new Rect(page.x, y, page.width, 60f),
                    Texts.L("Пока пусто. Учебники, книги и сердцевины для палочки делают у Кодекса; вкладыши и воспоминания находят в сундуках и у боссов.",
                        "Nothing yet. Textbooks, books and wand cores are made at a Codex; cards and memories are found in chests and on bosses."), Ui.Small);
                return;
            }

            var view = new Rect(page.x, y, page.width, page.yMax - y);
            var inner = new Rect(0f, 0f, page.width - 20f, readable.Count * 46f);
            s_bookScroll = GUI.BeginScrollView(view, s_bookScroll, inner, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            float ry = 0f;
            foreach (var it in readable)
            {
                string prefab = Items.PrefabOf(it) ?? "";
                string what = Reading.What(p, w, prefab);
                var icon = it.GetIcon();
                if (icon != null) Ui.Image(new Rect(0f, ry + 2f, 36f, 36f), icon.texture, Color.white);
                Ui.Text(new Rect(44f, ry, inner.width - 160f, 22f), Localization.instance.Localize(it.m_shared.m_name) + (it.m_stack > 1 ? " ×" + it.m_stack : ""), Ui.Bold);
                Ui.Text(new Rect(44f, ry + 20f, inner.width - 160f, 20f), what, Ui.Small);
                if (Ui.Btn(new Rect(inner.width - 110f, ry + 4f, 110f, 34f), prefab.StartsWith("WZ_Core_") ? Texts.L("Вставить", "Set") : Texts.L("Прочитать", "Read")))
                {
                    Reading.Read(p, w, it);
                }

                ry += 46f;
            }

            GUI.EndScrollView();
        }

        private static void PathRight(Player p, WizardState w, Rect page)
        {
            float y = page.y;
            Ui.Text(new Rect(page.x, y, page.width, 44f), Texts.L("Курсы и экзамены", "Years and exams"), Ui.Title);
            y += 50f;
            var ready = Exams.Ready(p, w);
            bool near = Exams.NearCodex(p);
            for (int k = 0; k < Items.Bosses.Length; k++)
            {
                string boss = Items.Bosses[k];
                bool passed = w.Trophies.Contains(boss);
                bool current = !passed && w.Year == k + 1;
                var row = new Rect(page.x, y, page.width, 56f);
                if (current)
                {
                    Ui.Frame(row, Ui.CardOn);
                }

                var icon = Ui.ItemIcon(Exams.TrophyPrefab(k));
                var ir = new Rect(row.x + 8f, row.y + 6f, 44f, 44f);
                if (icon != null) Ui.Image(ir, icon, passed || current ? Color.white : new Color(0.4f, 0.4f, 0.4f, 0.8f));
                else Ui.Glyph(ir, "trophy", Ui.InkSoft);
                string next = k + 2 <= 8 ? (k + 2) + Texts.L(" курс", "") : Texts.L("мастер", "master");
                Ui.Text(new Rect(row.x + 62f, row.y + 6f, 260f, 22f), Texts.L("Курс ", "Year ") + (k + 1) + " → " + next, Ui.Bold);
                Ui.Text(new Rect(row.x + 62f, row.y + 28f, 280f, 20f), Exams.BossName(k) + "  ·  " + Exams.Biome(k), Ui.Small);
                var act = new Rect(row.xMax - 150f, row.y + 10f, 140f, 36f);
                if (passed)
                {
                    Ui.Glyph(new Rect(act.x + 20f, act.y + 4f, 26f, 26f), "check", new Color(0.2f, 0.5f, 0.2f));
                    Ui.Text(new Rect(act.x + 52f, act.y + 6f, 90f, 24f), Texts.L("сдан", "passed"), Ui.Bold);
                }
                else if (ready.Any(kv => kv.Key == boss))
                {
                    if (Ui.Btn(act, Texts.L("Сдать экзамен", "Pass the exam"), true, near))
                    {
                        Exams.Pass(p, w, boss);
                    }

                    if (!near) Ui.Tip(act, Texts.L("Нужен Кодекс", "A Codex is needed"), Texts.L("Подойдите к Кодексу ближе чем на 10 м.", "Stand within 10 m of a Codex."));
                }
                else
                {
                    Ui.Text(new Rect(act.x, act.y + 8f, act.width, 22f), Texts.L("нужен трофей", "trophy needed"), Ui.Small);
                }

                y += 60f;
            }

            // the house and the controls
            y += 6f;
            if (w.House != null)
            {
                Ui.Image(new Rect(page.x + 4f, y, 52f, 61f), "crest_" + w.House, Color.white);
                var n = TreeDb.Get(TreeDb.HouseStart(w.House));
                Ui.Text(new Rect(page.x + 66f, y, page.width - 66f, 22f), HouseName(w.House), Ui.Bold);
                Ui.Text(new Rect(page.x + 66f, y + 22f, page.width - 66f, 44f), n != null ? n.Text : "", Ui.Small);
            }

            var help = new Rect(page.x, page.yMax - 38f, page.width, 40f);
            Ui.Text(help, Texts.L("ЛКМ — заклинание · СКМ — базовый · ПКМ — Протего · Alt+1…8 — ячейка · ",
                "LMB spell · MMB basic · RMB Protego · Alt+1…8 slot · ") + ModConfig.GrandKey.Value + Texts.L(" — великие", " grand"), Ui.Small);
        }

        // ================================================================== Spells
        private static List<SpellDef> Visible(Player p, WizardState w)
        {
            IEnumerable<SpellDef> list;
            if (s_filter < 0)
            {
                list = SpellDb.All.Where(s => s.Castable && w.KnowsSpell(s.Id) && !s.IsMeta);
                return list.OrderBy(s => s.Year).ThenBy(s => s.Name).ToList();
            }

            list = SpellDb.All.Where(s => Learning.Listed(s) && s.Year == s_filter || s.Role == "var" && s.Year == s_filter && w.KnowsSpell(s.Id));
            return list.OrderBy(s => (int)Learning.State(p, w, s)).ThenBy(s => s.Name).ToList();
        }

        private static void SpellList(Player p, WizardState w, Rect page)
        {
            float y = page.y;
            Ui.Text(new Rect(page.x, y, page.width - 120f, 44f), Texts.L("Заклинания", "Spells"), Ui.Title);
            Ui.Text(new Rect(page.xMax - 140f, y + 8f, 140f, 30f), "<color=#8a1a12>✦ " + w.PointsFree + "</color> " + Texts.L("свободно", "free"), Ui.Bold);
            y += 48f;
            if (Chip(new Rect(page.x, y, 128f, 30f), Texts.L("Изученные", "Known"), s_filter < 0)) { s_filter = -1; s_listScroll = Vector2.zero; }
            for (int yr = 1; yr <= 8; yr++)
            {
                var r = new Rect(page.x + 134f + (yr - 1) * 46f, y, 42f, 30f);
                if (Chip(r, yr.ToString(), s_filter == yr)) { s_filter = yr; s_listScroll = Vector2.zero; }
                Ui.Tip(r, yr + Texts.L(" курс", " year"), w.Year >= yr ? Texts.L("Открыт", "Open") : Texts.L("Откроется после экзамена", "Opens after an exam"));
            }

            y += 40f;
            var list = Visible(p, w);
            var view = new Rect(page.x, y, page.width, page.yMax - y);
            if (list.Count == 0)
            {
                Ui.Text(view, Texts.L("Здесь пока пусто. Выберите курс сверху, чтобы увидеть заклинания, которые можно изучить у Кодекса.",
                    "Nothing here yet. Pick a year above to see the spells you can learn at a Codex."), Ui.Body);
                return;
            }

            const float rowH = 52f;
            var inner = new Rect(0f, 0f, page.width - 18f, list.Count * rowH);
            s_listScroll = GUI.BeginScrollView(view, s_listScroll, inner, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            int first = Mathf.Max(0, Mathf.FloorToInt(s_listScroll.y / rowH) - 1);
            int last = Mathf.Min(list.Count, first + Mathf.CeilToInt(view.height / rowH) + 3);
            for (int i = first; i < last; i++)
            {
                var s = list[i];
                var r = new Rect(0f, i * rowH, inner.width, rowH - 4f);
                bool sel = s.Id == s_spell;
                if (GUI.Button(r, "", sel ? Ui.RowOn : Ui.Row))
                {
                    s_spell = s.Id;
                    s_detailScroll = Vector2.zero;
                    Ui.Click();
                }

                var st = Learning.State(p, w, s);
                bool grey = st == LearnState.NeedYear || st == LearnState.Closed || st == LearnState.NeedBook;
                Ui.SpellIcon(new Rect(r.x + 6f, r.y + 4f, 40f, 40f), s, grey);
                Ui.Text(new Rect(r.x + 54f, r.y + 5f, r.width - 60f, 22f), s.Name, Ui.Bold);
                Ui.Text(new Rect(r.x + 54f, r.y + 26f, r.width - 60f, 20f), Ui.SchoolName(s.School) + "  ·  " + StateLine(p, w, s, st), Ui.Small);
                if (w.KnowsSpell(s.Id) && PendingUpgrade(w, s) > 0)
                {
                    Ui.Glyph(new Rect(r.xMax - 30f, r.y + 12f, 24f, 24f), "point", Ui.InkRed);
                }
            }

            GUI.EndScrollView();
        }

        private static bool Chip(Rect r, string text, bool on)
        {
            bool hit = GUI.Button(r, text, on ? Ui.ChipOn : Ui.Chip);
            if (hit) Ui.Click();
            return hit;
        }

        private static string StateLine(Player p, WizardState w, SpellDef s, LearnState st)
        {
            switch (st)
            {
                case LearnState.Known:
                    return Texts.L("ранг ", "rank ") + Ui.Roman(w.Rank(s.Id)) + (Array8(w.Bar, s.Id) >= 0 ? Texts.L(" · на панели", " · in the bar") : "");
                case LearnState.Learnable: return "<color=#8a1a12><b>✦" + Learning.Cost(s) + " — " + Texts.L("можно изучить", "can be learned") + "</b></color>";
                case LearnState.NeedPoints: return "✦" + Learning.Cost(s) + " — " + Texts.L("не хватает очков", "not enough points");
                case LearnState.NeedCodex: return "✦" + Learning.Cost(s) + " — " + Texts.L("у Кодекса", "at a Codex");
                default: return Learning.Why(st, s);
            }
        }

        private static int Array8(string[] bar, string id)
        {
            for (int i = 0; i < bar.Length; i++) if (bar[i] == id) return i;
            return -1;
        }

        /// <summary>The first rank (3, 6 or 9) whose upgrade is reached but not chosen yet, or 0.</summary>
        private static int PendingUpgrade(WizardState w, SpellDef s)
        {
            if (s.Role == "basic" || s.Role == "var" || s.IsGrand)
            {
                return 0;
            }

            int rank = w.Rank(s.Id);
            foreach (int r in new[] { 3, 6, 9 })
            {
                if (rank >= r && w.AugChoice(s.Id, r) == null)
                {
                    return r;
                }
            }

            return 0;
        }

        private static string ArchName(string a)
        {
            switch (a)
            {
                case "proj": return Texts.L("снаряд", "bolt");
                case "aoe": return Texts.L("область", "area");
                case "beam": return Texts.L("луч", "beam");
                case "ctrl": return Texts.L("контроль", "control");
                case "summ": return Texts.L("призыв", "summon");
                case "buff": return Texts.L("усиление", "boon");
                case "heal": return Texts.L("исцеление", "healing");
                case "move": return Texts.L("перемещение", "movement");
                case "util": return Texts.L("бытовое", "utility");
                case "ritual": return Texts.L("ритуал", "ritual");
                default: return Texts.L("чары", "charm");
            }
        }

        private static void SpellDetail(Player p, WizardState w, Rect page)
        {
            var s = s_spell != null ? SpellDb.Get(s_spell) : null;
            var barRect = new Rect(page.x, page.yMax - 70f, page.width, 70f);
            if (s == null)
            {
                Ui.Text(new Rect(page.x, page.y, page.width, 44f), Texts.L("Как устроены заклинания", "How spells work"), Ui.Title);
                Ui.Text(new Rect(page.x, page.y + 56f, page.width, 420f), Texts.L(
                    "• Выберите заклинание в списке слева.\n\n" +
                    "• <b>Изучение.</b> Новые заклинания изучают у Кодекса за очки ✦. Каждый экзамен открывает заклинания следующего курса.\n\n" +
                    "• <b>Панель.</b> Изученное заклинание ставится в одну из 8 ячеек. В бою: ЛКМ творит выбранное, Left Alt + 1…8 выбирает ячейку.\n\n" +
                    "• <b>Ранги.</b> Заклинание растёт от применения (I–X). На рангах III, VI и IX вы выбираете ему улучшение — заклинание в списке помечено звёздочкой.",
                    "• Pick a spell in the list on the left.\n\n" +
                    "• <b>Learning.</b> New spells are learned at a Codex for ✦ points. Each exam opens the next year's spells.\n\n" +
                    "• <b>The bar.</b> A learned spell goes into one of 8 slots. In battle LMB casts the chosen one, Left Alt + 1…8 picks the slot.\n\n" +
                    "• <b>Ranks.</b> A spell grows as you use it (I–X). At ranks III, VI and IX you choose an upgrade for it; such spells are marked with a star."),
                    Ui.Body);
                BarPreview(w, barRect, null);
                return;
            }

            float y = page.y;
            Ui.SpellIcon(new Rect(page.x, y, 92f, 92f), s);
            Ui.Text(new Rect(page.x + 106f, y, page.width - 106f, 36f), s.Name, Ui.Heading);
            if (!string.IsNullOrEmpty(s.Incantation) && s.Incantation != "-")
            {
                Ui.Text(new Rect(page.x + 106f, y + 36f, page.width - 106f, 22f), "«" + s.Incantation + "»", Ui.Italic);
            }

            Ui.Text(new Rect(page.x + 106f, y + 60f, page.width - 106f, 22f),
                Ui.SchoolName(s.School) + "  ·  " + s.Year + Texts.L(" курс", " year") + "  ·  " + ArchName(s.Arch), Ui.Small);
            y += 104f;

            var st = Learning.State(p, w, s);
            bool known = st == LearnState.Known;
            int rank = known ? w.Rank(s.Id) : 1;

            // numbers
            string numbers = Texts.L("Магия: ", "Eitr: ") + "<b>" + Mathf.RoundToInt(s.BaseCost * ModConfig.CostMultiplier.Value) + "</b>    " +
                             Texts.L("Перезарядка: ", "Cooldown: ") + "<b>" + s.BaseCooldown.ToString("0.#") + Texts.L(" с", " s") + "</b>";
            if (s.Damage.Length > 0)
            {
                numbers += "    " + Texts.L("Урон: ", "Damage: ") + "<b>" + Mathf.RoundToInt(s.BaseDamage) + "</b>";
            }

            Ui.Text(new Rect(page.x, y, page.width, 22f), numbers, Ui.Body);
            y += 28f;
            if (known && s.Role != "basic")
            {
                float xp, need = WizardState.XpForRank(rank + 1), prev = WizardState.XpForRank(rank);
                w.Xp.TryGetValue(s.Id, out xp);
                Ui.Text(new Rect(page.x, y, 120f, 22f), Texts.L("Ранг ", "Rank ") + "<b>" + Ui.Roman(rank) + "</b>", Ui.Body);
                if (rank < w.MaxRank)
                {
                    Ui.Bar(new Rect(page.x + 110f, y + 2f, page.width - 230f, 18f), (xp - prev) / Mathf.Max(1f, need - prev), new Color(0.55f, 0.35f, 0.85f));
                    Ui.Text(new Rect(page.xMax - 110f, y, 110f, 22f), Texts.L("до ", "to ") + Ui.Roman(rank + 1), Ui.Small);
                }
                else
                {
                    Ui.Text(new Rect(page.x + 110f, y, page.width - 110f, 22f), Texts.L("предел этого курса", "the limit for your year"), Ui.Small);
                }

                y += 30f;
            }

            // what it does
            float descH = Mathf.Min(170f, Ui.Body.CalcHeight(new GUIContent(s.Effect), page.width));
            var descView = new Rect(page.x, y, page.width, descH);
            Ui.Text(descView, s.Effect, Ui.Body);
            y += descH + 12f;

            // actions
            if (!known)
            {
                if (st == LearnState.Learnable || st == LearnState.NeedPoints || st == LearnState.NeedCodex)
                {
                    if (Ui.Btn(new Rect(page.x, y, 260f, 46f), Texts.L("Изучить за ✦", "Learn for ✦") + Learning.Cost(s), true, st == LearnState.Learnable))
                    {
                        Learning.Learn(p, w, s);
                    }

                    if (st != LearnState.Learnable)
                    {
                        Ui.Text(new Rect(page.x + 276f, y + 12f, page.width - 276f, 24f), Learning.Why(st, s), Ui.Small);
                    }
                }
                else
                {
                    Ui.Glyph(new Rect(page.x, y + 4f, 26f, 26f), "lock", Ui.InkSoft);
                    Ui.Text(new Rect(page.x + 34f, y + 6f, page.width - 34f, 24f), Learning.Why(st, s), Ui.Bold);
                }

                y += 56f;
            }
            else if (s.IsGrand)
            {
                bool on = w.Grand == s.Id;
                if (Ui.Btn(new Rect(page.x, y, 300f, 42f), on ? Texts.L("Это ваши великие чары", "Your grand spell") : Texts.L("Сделать великими чарами", "Make it the grand spell"), !on, !on))
                {
                    w.Grand = s.Id;
                    Wizard.Changed();
                }

                y += 52f;
            }
            else
            {
                Ui.Text(new Rect(page.x, y + 10f, 120f, 24f), Texts.L("В ячейку:", "To slot:"), Ui.Bold);
                int at = Array8(w.Bar, s.Id);
                for (int i = 0; i < 8; i++)
                {
                    var r = new Rect(page.x + 108f + i * 46f, y, 42f, 42f);
                    if (GUI.Button(r, (i + 1).ToString(), at == i ? Ui.ChipOn : Ui.Chip))
                    {
                        Ui.Click();
                        for (int k = 0; k < 8; k++) if (w.Bar[k] == s.Id) w.Bar[k] = null;
                        w.Bar[i] = s.Id;
                        Wizard.Changed();
                    }
                }

                y += 52f;
            }

            // a rank upgrade waiting to be chosen
            int pending = known ? PendingUpgrade(w, s) : 0;
            if (pending > 0 && y + 120f < barRect.y)
            {
                var c = new Rect(page.x, y, page.width, 116f);
                Ui.Frame(c, Ui.CardOn);
                Ui.Text(new Rect(c.x + 16f, c.y + 10f, c.width - 32f, 24f), Texts.L("Ранг ", "Rank ") + Ui.Roman(pending) + Texts.L(": выберите улучшение", ": choose an upgrade"), Ui.Bold);
                var choices = Patterns.Choices(s, pending);
                for (int i = 0; i < choices.Length; i++)
                {
                    var r = new Rect(c.x + 16f + i * ((c.width - 40f) / 2f + 8f), c.y + 44f, (c.width - 40f) / 2f, 56f);
                    var ch = choices[i];
                    var style = new GUIStyle(Ui.Button) { wordWrap = true, fontSize = 14 };
                    if (GUI.Button(r, ch.Value, style))
                    {
                        Ui.Click();
                        w.SetAugChoice(s.Id, pending, ch.Key);
                        Wizard.Changed();
                        var v = SpellDb.Get(ch.Key);
                        Casting.Message(v != null ? Texts.L("Открыт вариант: ", "Variant opened: ") + v.Name : Texts.L("Улучшение выбрано.", "Upgrade chosen."));
                    }
                }
            }
            else if (known)
            {
                var chosen = new List<string>();
                foreach (int r in new[] { 3, 6, 9 })
                {
                    string a = w.AugChoice(s.Id, r);
                    if (a == null) continue;
                    var v = SpellDb.Get(a);
                    chosen.Add(Ui.Roman(r) + ": " + (v != null ? v.Name : a.StartsWith("pool:") ? Patterns.Label(a.Substring(5)) : Patterns.Label(a)));
                }

                if (chosen.Count > 0)
                {
                    Ui.Text(new Rect(page.x, y, page.width, 44f), Texts.L("Улучшения: ", "Upgrades: ") + string.Join(" · ", chosen.ToArray()), Ui.Small);
                }
            }

            BarPreview(w, barRect, s);
        }

        private static void BarPreview(WizardState w, Rect r, SpellDef selected)
        {
            Ui.Text(new Rect(r.x, r.y - 4f, r.width, 20f), Texts.L("Панель (ПКМ по ячейке — убрать)", "The bar (right-click a slot to clear it)"), Ui.Small);
            for (int i = 0; i < 9; i++)
            {
                bool grand = i == 8;
                var slot = new Rect(r.x + i * 54f + (grand ? 18f : 0f), r.y + 18f, 48f, 48f);
                string id = grand ? w.Grand : w.Bar[i];
                var s = id != null ? SpellDb.Get(id) : null;
                Ui.Image(slot, "ui_slot", Color.white);
                if (s != null)
                {
                    Ui.SpellIcon(new Rect(slot.x + 5f, slot.y + 5f, 38f, 38f), s);
                    Ui.Tip(slot, s.Name, grand ? Texts.L("Великие чары (", "Grand spell (") + ModConfig.GrandKey.Value + ")" : Texts.L("Ячейка ", "Slot ") + (i + 1));
                }

                Ui.Text(new Rect(slot.x + 2f, slot.y - 2f, 20f, 18f), grand ? ModConfig.GrandKey.Value.ToString() : (i + 1).ToString(), Ui.Small);
                var e = Event.current;
                if (e.type == EventType.MouseDown && slot.Contains(e.mousePosition))
                {
                    if (e.button == 1 && s != null)
                    {
                        if (grand) w.Grand = null;
                        else w.Bar[i] = null;
                        Wizard.Changed();
                        e.Use();
                    }
                    else if (e.button == 0 && s != null)
                    {
                        s_spell = s.Id;
                        e.Use();
                    }
                }
            }
        }

        // ================================================================== Talents
        private static void Talents(Player p, WizardState w, Rect book)
        {
            var center = new Vector2(book.x + book.width * 0.5f, book.y + 404f);
            var chart = new Rect(center.x - 375f, center.y - 375f, 750f, 750f);
            Ui.Image(chart, "ui_glow", new Color(0.05f, 0.07f, 0.16f, 0.7f));
            Ui.Text(new Rect(book.x + 90f, book.y + 66f, 330f, 44f), Texts.L("Таланты", "Talents"), Ui.Title);
            Ui.Text(new Rect(book.x + 92f, book.y + 112f, 300f, 60f),
                Texts.L("Свободно: ", "Free: ") + "<b><color=#8a1a12>✦ " + w.PointsFree + "</color></b>\n" + Texts.L("Взято звёзд: ", "Stars taken: ") + w.Talents.Count +
                " / " + TalentTree.All.Count, Ui.Body);
            Ui.Text(new Rect(book.x + 92f, book.yMax - 150f, 250f, 90f),
                Texts.L("Щёлкните светящуюся звезду, чтобы взять её. Звёзды открываются по курсам; самая дальняя в ветви — ключевой талант.",
                    "Click a glowing star to take it. Stars open by year; the farthest one of a branch is a keystone."), Ui.Small);
            bool near = Exams.NearCodex(p);
            var reset = new Rect(book.xMax - 330f, book.yMax - 104f, 240f, 40f);
            if (Ui.Btn(reset, Texts.L("Сбросить таланты", "Reset talents"), false, near && w.Talents.Count > 0))
            {
                TalentTree.Reset(w);
            }

            Ui.Tip(reset, Texts.L("Сброс бесплатный", "The reset is free"), near ? Texts.L("Все очки вернутся.", "All points come back.") : Texts.L("Только у Кодекса.", "Only at a Codex."));

            // lines first, then the stars
            foreach (var t in TalentTree.All)
            {
                var prev = t.Prev != null ? TalentTree.Get(t.Prev) : null;
                var a = center + (prev != null ? prev.Pos : Vector2.zero);
                var b = center + t.Pos;
                bool lit = w.Talents.Contains(t.Id);
                var c = TalentTree.Branches[t.Branch].Color;
                Ui.Dotted(a, b, lit ? new Color(1f, 0.85f, 0.5f, 0.95f) : new Color(c.r, c.g, c.b, 0.28f), lit ? 7f : 5f, lit ? 4f : 7f);
            }

            float pulse = 0.65f + 0.35f * Mathf.Sin(Time.realtimeSinceStartup * 3f);
            Ui.Image(new Rect(center.x - 34f, center.y - 34f, 68f, 68f), "ui_glow", new Color(1f, 0.85f, 0.5f, 0.8f));
            Ui.Image(new Rect(center.x - 22f, center.y - 22f, 44f, 44f), "node_notable", Ui.Gold);
            Ui.Glyph(new Rect(center.x - 12f, center.y - 12f, 24f, 24f), "wand", Ui.Gold);
            foreach (var t in TalentTree.All)
            {
                var br = TalentTree.Branches[t.Branch];
                bool owned = w.Talents.Contains(t.Id);
                string why;
                bool can = TalentTree.CanTake(w, t, out why);
                float size = t.Kind == 2 ? 54f : t.Kind == 1 ? 42f : 30f;
                var pos = center + t.Pos;
                var r = new Rect(pos.x - size * 0.5f, pos.y - size * 0.5f, size, size);
                if (owned || can)
                {
                    float g = size * (owned ? 2.1f : 1.9f);
                    Ui.Image(new Rect(pos.x - g * 0.5f, pos.y - g * 0.5f, g, g), "ui_glow",
                        owned ? new Color(1f, 0.82f, 0.45f, 0.85f) : new Color(br.Color.r, br.Color.g, br.Color.b, 0.6f * pulse));
                }

                bool open = w.Year >= t.Year;
                Color frame = owned ? Ui.Gold : can ? Color.Lerp(br.Color, Color.white, 0.35f) : open ? new Color(0.55f, 0.52f, 0.5f) : new Color(0.32f, 0.3f, 0.3f);
                Ui.Image(r, t.Kind == 2 ? "node_key" : t.Kind == 1 ? "node_notable" : "node_small", frame);
                float gp = size * 0.26f;
                Ui.Glyph(new Rect(r.x + gp, r.y + gp, size - 2f * gp, size - 2f * gp), br.Glyph,
                    owned ? Color.Lerp(br.Color, Color.white, 0.5f) : open ? new Color(br.Color.r, br.Color.g, br.Color.b, 0.75f) : new Color(0.5f, 0.5f, 0.5f, 0.5f));
                if (GUI.Button(r, "", GUIStyle.none))
                {
                    if (can) TalentTree.Take(w, t);
                    else if (!owned) Casting.Message(why);
                }

                string kind = t.Kind == 2 ? Texts.L("Ключевой талант", "Keystone") : t.Kind == 1 ? Texts.L("Значимый талант", "Notable") : br.Name;
                string state = owned ? Texts.L("<color=#9ee39a>Взят</color>", "<color=#9ee39a>Taken</color>")
                    : can ? Texts.L("<color=#ffd27a>Щёлкните, чтобы взять (✦", "<color=#ffd27a>Click to take (✦") + t.Cost + ")</color>"
                    : "<color=#e0a090>" + why + "</color>";
                Ui.Tip(r, t.Name, "<i>" + kind + "</i>\n" + t.Desc + "\n\n" + state, br.Color);
            }

            // branch names at the tips
            for (int b = 0; b < TalentTree.Branches.Count; b++)
            {
                var br = TalentTree.Branches[b];
                float ang = (-60f + 60f * b) * Mathf.Deg2Rad;
                float rad = Mathf.Abs(Mathf.Sin(ang)) < 0.1f ? 440f : 372f; // the side labels go past the keystone
                var pos = center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;
                var lr = new Rect(pos.x - 90f, pos.y - 13f, 180f, 26f);
                Ui.Frame(new Rect(lr.x + 20f, lr.y - 2f, lr.width - 40f, lr.height + 4f), Ui.Panel);
                var style = Ui.LightCenter;
                var oc = style.normal.textColor;
                style.normal.textColor = Color.Lerp(br.Color, Color.white, 0.45f);
                Ui.Text(lr, br.Name, style);
                style.normal.textColor = oc;
            }
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
