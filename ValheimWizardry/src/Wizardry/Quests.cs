using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Wizardry
{
    /// <summary>One step of the wizard's path: what to do, how to do it, and how far along it is.</summary>
    internal sealed class Step
    {
        public string Id, Ru, En, HintRu, HintEn;
        public int Year, Target = 1;
        public bool Hugin;
        public Func<Player, WizardState, int> Progress;

        public string Title { get { return Localization.instance.Localize(Texts.English ? En : Ru); } }
        public string Hint { get { return Quests.Fill(Localization.instance.Localize(Texts.English ? HintEn : HintRu)); } }
    }

    /// <summary>
    ///     The wizard's path: a guided chain of steps from the first Codex to the Elder Wand. The current step is shown in
    ///     the tracker and the grimoire; each finished step gives a magic point. Hugin explains the important ones.
    /// </summary>
    internal static class Quests
    {
        public static readonly List<Step> All = new List<Step>();
        private static float s_timer;
        private static string s_announced;

        static Quests()
        {
            // ---------------------------------------------------------------- year 1: the Meadows
            S(1, "codex", "Постройте Кодекс", "Build the Codex",
                "Возьмите молот → вкладка «Ремесло» → «$wz_codex». Нужно: дерево ×10, камень ×6, перья ×3, смола ×5.",
                "Take the hammer → the Crafting tab → \"$wz_codex\". It needs wood ×10, stone ×6, feathers ×3, resin ×5.",
                (p, w) => Pieces.Codexes.Count > 0 ? 1 : 0, 1, true);
            S(1, "wand", "Создайте первую палочку", "Craft your first wand",
                "Подойдите к Кодексу и нажмите E: откроется крафт. Создайте «$wz_wand_beech».",
                "Walk up to the Codex and press E to open crafting. Make a \"$wz_wand_beech\".",
                (p, w) => WandTier(p) >= 1 ? 1 : 0, 1, true);
            S(1, "equip", "Возьмите палочку в руку", "Hold the wand",
                "Откройте инвентарь (Tab) и экипируйте палочку, как оружие.", "Open the inventory (Tab) and equip the wand like a weapon.",
                (p, w) => Wands.Equipped(p) != null ? 1 : 0);
            S(1, "house", "Пройдите распределение", "Get sorted",
                "Нажмите K: откроется Гримуар. Распределяющая шляпа предложит факультет. Он даст первое заклинание и постоянный бонус.",
                "Press K to open the Grimoire. The Sorting Hat will offer a house. It gives your first spell and a lasting bonus.",
                (p, w) => w.House != null ? 1 : 0, 1, true);
            S(1, "cast", "Сотворите 15 заклинаний", "Cast 15 spells",
                "ЛКМ: выбранное заклинание. СКМ: бесплатный «Базовый выстрел». ПКМ: щит Протего. Заклинания тратят магию (синюю шкалу), палочка её восполняет.",
                "LMB casts the chosen spell. MMB is the free Basic Cast. RMB is the Protego shield. Spells use eitr (the blue bar); the wand refills it.",
                (p, w) => w.CastCount, 15, true);
            S(1, "learn", "Изучите новое заклинание", "Learn a new spell",
                "У Кодекса откройте Гримуар (K) → «Заклинания». Выберите заклинание со значком ✦ и нажмите «Изучить». Очки ✦ дают уровни навыка «Волшебство» и шаги этого пути.",
                "At a Codex open the Grimoire (K) → Spells. Pick a spell marked ✦ and press Learn. ✦ points come from Wizardry skill levels and from the steps of this path.",
                (p, w) => w.Bought.Count, 1, true);
            S(1, "bar", "Соберите панель заклинаний", "Fill your spell bar",
                "В «Заклинаниях» выберите изученное заклинание и нажмите номер ячейки внизу. В бою ячейки переключаются клавишами Left Alt + 1…8 или Alt + колесо.",
                "In Spells, pick a learned spell and press a slot number at the bottom. In battle, switch slots with Left Alt + 1…8 or Alt + wheel.",
                (p, w) => w.Bar.Count(b => b != null), 2);
            S(1, "talent", "Возьмите первый талант", "Take your first talent",
                "Гримуар → «Таланты» → щёлкните светящуюся звезду у центра. Таланты бесплатно сбрасываются у Кодекса.",
                "Grimoire → Talents → click a glowing star near the centre. Talents are reset for free at a Codex.",
                (p, w) => w.Talents.Count, 1, true);
            S(1, "skill5", "Навык «Волшебство» 5", "Wizardry skill 5",
                "Навык растёт, пока вы колдуете. Каждый его уровень даёт ✦1.", "The skill grows as you cast. Every level gives ✦1.",
                (p, w) => Mathf.FloorToInt(p.GetSkillLevel(Plugin.SkillType)), 5);
            Exam(0, true);

            // ---------------------------------------------------------------- year 2: the Black Forest
            Wand(2, "fir");
            S(2, "ext1", "Поставьте «$wz_ext_stand»", "Place the \"$wz_ext_stand\"",
                "Молот → «Ремесло» → «$wz_ext_stand» рядом с Кодексом. Кодекс поднимется до 2 уровня, и откроются палочки сильнее.",
                "Hammer → Crafting → \"$wz_ext_stand\" next to the Codex. The Codex reaches level 2 and stronger wands open.",
                (p, w) => CodexLevel(p) >= 2 ? 1 : 0);
            Learn(2);
            S(2, "rank3", "Ранг III", "Rank III",
                "Ранг заклинания растёт от применения. На рангах III, VI и IX вы выбираете ему улучшение во вкладке «Заклинания».",
                "A spell's rank grows as you use it. At ranks III, VI and IX you choose an upgrade for it in the Spells tab.",
                (p, w) => MaxRank(w), 3, true);
            Exam(1);

            // ---------------------------------------------------------------- year 3: the Swamp
            Wand(3, "birch");
            S(3, "ext2", "Поставьте «$wz_ext_cauldron»", "Place the \"$wz_ext_cauldron\"",
                "Котёл рядом с Кодексом поднимет его до 3 уровня и откроет зелья.", "A cauldron next to the Codex raises it to level 3 and opens the potions.",
                (p, w) => CodexLevel(p) >= 3 ? 1 : 0);
            S(3, "potion", "Сварите зелье", "Brew a potion",
                "У Кодекса с котлом сварите любое зелье, например «$wz_potion_pepperup»: оно согревает в холоде.",
                "At a Codex with a cauldron brew any potion, such as \"$wz_potion_pepperup\", which keeps you warm.",
                (p, w) => HasPotion(p) ? 1 : 0);
            Learn(3);
            Exam(2);

            // ---------------------------------------------------------------- year 4: the Mountains
            Wand(4, "ancient");
            S(4, "ext3", "Поставьте «$wz_ext_ball»", "Place the \"$wz_ext_ball\"", "Хрустальный шар поднимет Кодекс до 4 уровня.",
                "The crystal ball raises the Codex to level 4.", (p, w) => CodexLevel(p) >= 4 ? 1 : 0);
            Learn(4);
            Exam(3);

            // ---------------------------------------------------------------- year 5: the Plains
            Wand(5, "eldercopy");
            S(5, "ext4", "Поставьте «$wz_ext_pensieve»", "Place the \"$wz_ext_pensieve\"", "Омут памяти поднимет Кодекс до 5 уровня.",
                "The Pensieve raises the Codex to level 5.", (p, w) => CodexLevel(p) >= 5 ? 1 : 0);
            S(5, "grand", "Великие чары", "Grand spells",
                "Создайте у Кодекса «$wz_book_grimoire» и прочитайте его в Гримуаре («Путь» → «Книги»). Великие чары творятся клавишей " + "{grand}" +
                ", когда шкала Средоточия полна.",
                "Make the \"$wz_book_grimoire\" at a Codex and read it in the Grimoire (Path → Books). Grand spells are cast with " + "{grand}" +
                " when the Focus bar is full.",
                (p, w) => w.Books.Contains("grimoire") ? 1 : 0, 1, true);
            Learn(5);
            Exam(4);

            // ---------------------------------------------------------------- year 6: the Mistlands
            Wand(6, "yggdrasil");
            S(6, "ext5", "Поставьте «$wz_ext_astrolabe»", "Place the \"$wz_ext_astrolabe\"", "Астролябия поднимет Кодекс до 6 уровня.",
                "The astrolabe raises the Codex to level 6.", (p, w) => CodexLevel(p) >= 6 ? 1 : 0);
            S(6, "keystone", "Ключевой талант", "A keystone talent",
                "Дойдите по любой ветви талантов до последней, самой большой звезды. Ключевые таланты сильно меняют стиль игры.",
                "Follow any talent branch to its last and largest star. Keystones change how you play.",
                (p, w) => w.Talents.Any(id => TalentTree.Get(id) != null && TalentTree.Get(id).Kind == 2) ? 1 : 0);
            Learn(6);
            Exam(5);

            // ---------------------------------------------------------------- year 7: the Ashlands
            Wand(7, "ash");
            S(7, "ext6", "Поставьте «$wz_ext_turner»", "Place the \"$wz_ext_turner\"", "Маховик времени поднимет Кодекс до 7, высшего уровня.",
                "The Time-Turner raises the Codex to level 7, the highest.", (p, w) => CodexLevel(p) >= 7 ? 1 : 0);
            Learn(7);
            Exam(6);

            // ---------------------------------------------------------------- the Deep North and the end
            Wand(8, "north");
            Exam(7);
            S(9, "elder", "Бузинная палочка", "The Elder Wand",
                "Сильнейшая палочка: трофей Древнего, «Косточка желаний» и пламенный металл. Владейте ею, и путь пройден.",
                "The strongest wand: the Elder's trophy, a Wishbone and flametal. Wield it and the path is complete.",
                (p, w) => WandTier(p) >= 9 ? 1 : 0, 1, true);
        }

        private static void S(int year, string id, string ru, string en, string hru, string hen, Func<Player, WizardState, int> progress, int target = 1,
            bool hugin = false)
        {
            All.Add(new Step { Id = id, Year = year, Ru = ru, En = en, HintRu = hru, HintEn = hen, Progress = progress, Target = target, Hugin = hugin });
        }

        private static void Wand(int year, string wood)
        {
            int tier = Wands.Woods.First(x => x.Key == wood).Tier;
            S(year, "wand_" + wood, "Создайте «$wz_wand_" + wood + "»", "Craft the \"$wz_wand_" + wood + "\"",
                "Палочка этого курса сильнее и даёт больше магии. Её делают у Кодекса из материалов нового биома.",
                "This year's wand is stronger and holds more eitr. It is made at the Codex from the new biome's materials.",
                (p, w) => WandTier(p) >= tier ? 1 : 0);
        }

        private static void Learn(int year)
        {
            S(year, "learn" + year, "Изучите 2 заклинания " + year + " курса", "Learn 2 year-" + year + " spells",
                "У Кодекса: Гримуар → «Заклинания» → фильтр «" + year + " курс». Выбирайте то, что подходит вашему стилю.",
                "At a Codex: Grimoire → Spells → the \"Year " + year + "\" filter. Pick what suits your style.",
                (p, w) => w.Known.Count(id => SpellDb.Get(id) != null && SpellDb.Get(id).Year == year && SpellDb.Get(id).Role != "basic"), 2);
        }

        private static void Exam(int boss, bool hugin = false)
        {
            string key = Items.Bosses[boss];
            int year = boss + 1;
            S(year, "exam_" + key, "Экзамен " + year + " курса: " + Exams.BossNamesRu[boss], "Year " + year + " exam: " + Exams.BossNamesEn[boss],
                "Победите босса (" + Exams.BiomesRu[boss] + "), принесите его трофей к Кодексу и нажмите «Сдать экзамен» во вкладке «Путь». Трофей останется у вас.",
                "Defeat the boss (" + Exams.BiomesEn[boss] + "), bring the trophy to a Codex and press Pass the exam in the Path tab. You keep the trophy.",
                (p, w) => w.Trophies.Contains(key) ? 1 : 0, 1, hugin);
        }

        // ------------------------------------------------------------------ progress helpers
        public static int WandTier(Player p)
        {
            int best = 0;
            foreach (var it in p.GetInventory().GetAllItems())
            {
                var wood = Wands.WoodOf(it);
                if (wood != null && wood.Tier > best) best = wood.Tier;
            }

            return best;
        }

        public static int CodexLevel(Player p)
        {
            int best = 0;
            foreach (var s in Pieces.Codexes)
            {
                if (s != null && Vector3.Distance(s.transform.position, p.transform.position) < 60f)
                {
                    best = Mathf.Max(best, Pieces.Level(s));
                }
            }

            return best;
        }

        private static int MaxRank(WizardState w)
        {
            int best = 0;
            foreach (var id in w.Known)
            {
                best = Mathf.Max(best, w.Rank(id));
            }

            return best;
        }

        private static bool HasPotion(Player p)
        {
            return p.GetInventory().GetAllItems().Any(it => (Items.PrefabOf(it) ?? "").StartsWith("WZ_Potion_"));
        }

        // ------------------------------------------------------------------ state
        public static Step Current(WizardState w)
        {
            foreach (var s in All)
            {
                if (!w.Quests.Contains(s.Id))
                {
                    return s;
                }
            }

            return null;
        }

        public static int Index(Step s)
        {
            return All.IndexOf(s);
        }

        public static string Fill(string text)
        {
            return text.Replace("{grand}", ModConfig.GrandKey.Value.ToString());
        }

        public static void Update(Player p, WizardState w)
        {
            s_timer -= Time.deltaTime;
            if (s_timer > 0f)
            {
                return;
            }

            s_timer = 0.5f;
            int done = 0;
            string last = null;
            for (int guard = 0; guard < All.Count; guard++)
            {
                var s = Current(w);
                if (s == null || s.Progress(p, w) < s.Target)
                {
                    break;
                }

                w.Quests.Add(s.Id);
                last = s.Title;
                done++;
            }

            if (done > 0)
            {
                Wizard.Changed();
                Fx.Sound(p.transform.position, "chime", 0.6f);
                Casting.Message(done == 1
                    ? Texts.L("Шаг пути выполнен: ", "Step done: ") + last + Texts.L(" (+1 очко магии)", " (+1 magic point)")
                    : Texts.L("Выполнено шагов пути: ", "Path steps done: ") + done + " (+" + done + Texts.L(" очк. магии)", " magic points)"));
            }

            var cur = Current(w);
            if (cur != null && cur.Id != s_announced)
            {
                s_announced = cur.Id;
                if (cur.Hugin)
                {
                    Hugin(p, cur);
                }
            }
        }

        /// <summary>Hugin the raven explains the step, once per character (the game remembers which tutorials were shown).</summary>
        private static void Hugin(Player p, Step s)
        {
            try
            {
                if (!ModConfig.HuginTips.Value || Tutorial.instance == null)
                {
                    return;
                }

                string name = "wz_" + s.Id;
                if (p.HaveSeenTutorial(name))
                {
                    return;
                }

                Tutorial.instance.m_texts.RemoveAll(x => x.m_name == name);
                Tutorial.instance.m_texts.Add(new Tutorial.TutorialText
                {
                    m_name = name,
                    m_label = Texts.L("Волшебство", "Wizardry"),
                    m_topic = s.Title,
                    m_text = s.Hint,
                });
                p.ShowTutorial(name);
            }
            catch (Exception e)
            {
                Plugin.Report("Hugin", e);
            }
        }
    }
}
