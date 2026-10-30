using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Wizardry
{
    /// <summary>One star of the talent constellation.</summary>
    internal sealed class Talent
    {
        public string Id, Ru, En, DescRu, DescEn, Mods;
        public int Branch, Index, Year, Cost;
        public Vector2 Pos;

        /// <summary>0 = small, 1 = notable, 2 = keystone.</summary>
        public int Kind;

        public string Name { get { return Texts.English ? En : Ru; } }
        public string Desc { get { return Texts.English ? DescEn : DescRu; } }
        public string Prev { get { return Index > 1 ? TalentTree.IdOf(Branch, Index - 1) : null; } }
    }

    internal sealed class TalentBranch
    {
        public string Ru, En, Glyph;
        public Color Color;
        public string Name { get { return Texts.English ? En : Ru; } }
    }

    /// <summary>
    ///     The talent constellation: six branches of eight stars around the centre. A star needs the one before it, its
    ///     year and a point (keystones two). Talents are taken anywhere and reset for free at a Codex.
    /// </summary>
    internal static class TalentTree
    {
        public static readonly int[] YearOf = { 0, 1, 1, 2, 3, 3, 4, 5, 6 };
        public static readonly List<TalentBranch> Branches = new List<TalentBranch>();
        public static readonly List<Talent> All = new List<Talent>();
        private static readonly Dictionary<string, Talent> s_byId = new Dictionary<string, Talent>();

        public static string IdOf(int branch, int index)
        {
            return "t" + branch + "_" + index;
        }

        public static Talent Get(string id)
        {
            Talent t;
            return id != null && s_byId.TryGetValue(id, out t) ? t : null;
        }

        static TalentTree()
        {
            B("Дуэль", "Duelling", "proj", new Color(0.95f, 0.38f, 0.32f),
                T("Твёрдая рука", "Steady Hand", "+6% к урону заклинаний.", "+6% spell damage.", "dmg:6"),
                T("Быстрые чары", "Quick Casting", "+6% к скорости сотворения.", "+6% casting speed.", "cast_speed:6"),
                T("Дуэльный клуб", "Duelling Club", "+15% урона по одиночной цели. После парирования следующее заклинание творится мгновенно.",
                    "+15% single-target damage. After a parry the next spell is instant.", "dmg_single:15;flag:instant_after_parry"),
                T("Меткий глаз", "Keen Eye", "+6% к шансу критического удара.", "+6% critical chance.", "crit:6"),
                T("Стремительность", "Swift Bolts", "+15% к скорости снарядов, +10% к дальности.", "+15% projectile speed, +10% range.", "proj_speed:15;range:10"),
                T("Аврор", "Auror", "+25% к урону критов. «Ступефай» раскалывается на три луча.", "+25% critical damage. Stupefy splits into three bolts.",
                    "crit_mult:25;flag:stupefy_split"),
                T("Боевой ритм", "Battle Rhythm", "−6% перезарядки, +4% урона.", "−6% cooldowns, +4% damage.", "cdr:6;dmg:4"),
                T("Кодекс дуэлянта", "Duellist's Code", "Парирования копят дуэльные заряды: каждый ускоряет сотворение и усиливает урон.",
                    "Parries build duel charges: each one speeds up casting and adds damage.", "flag:ks_duel"));
            B("Защита", "Protection", "buff", new Color(0.42f, 0.66f, 1f),
                T("Стойкость", "Fortitude", "+12 к здоровью.", "+12 health.", "hp:12"),
                T("Крепкий щит", "Strong Protego", "+12% к силе блока Протего, −10% к его цене.", "+12% Protego block power, −10% its cost.", "block:12;protego_cost:10"),
                T("Отражение", "Riposte", "Парирование Протего возвращает магию. +8% к силе блока.", "A Protego parry restores eitr. +8% block power.",
                    "flag:parry_restore;block:8"),
                T("Чародейская броня", "Charmed Armour", "+6 к броне.", "+6 armour.", "armor:6"),
                T("Стихийный оберег", "Elemental Ward", "−12% урона от огня, холода и молнии.", "−12% fire, frost and lightning damage taken.", "res_elem:12"),
                T("Неприступный", "Unyielding", "+8% к здоровью, контроль на вас короче на 25%.", "+8% health, control on you lasts 25% less.", "hp_pct:8;res_ctrl:25"),
                T("Сила оберегов", "Ward Craft", "+15% к силе и +20% к длительности защитных зон.", "+15% power and +20% duration of wards.", "ward_power:15;ward_dur:20"),
                T("Кровная защита", "Blood Protection", "Ваша кровь хранит союзников рядом: они получают меньше урона.",
                    "Your blood shields allies near you: they take less damage.", "flag:ks_blood"));
            B("Стихии", "Elements", "aoe", new Color(1f, 0.62f, 0.25f),
                T("Искра", "Spark", "+10% к урону огнём.", "+10% fire damage.", "dmg_fire:10"),
                T("Иней", "Rime", "+10% к урону холодом.", "+10% frost damage.", "dmg_frost:10"),
                T("Стихийные чары", "Elemental Charms", "Стихийные заклинания дают вам короткое усиление.", "Elemental spells give you a short boon.",
                    "flag:elemental_buffs"),
                T("Гроза", "Thunder", "+10% к урону молнией.", "+10% lightning damage.", "dmg_lightning:10"),
                T("Размах", "Reach", "+12% к радиусу областей.", "+12% area radius.", "area:12"),
                T("Повелитель стихий", "Elementalist", "+10% к урону огнём, холодом и молнией, +10% к длительности.",
                    "+10% fire, frost and lightning damage, +10% duration.", "dmg_fire:10;dmg_frost:10;dmg_lightning:10;dur:10"),
                T("Жар и стужа", "Lingering", "+20% к периодическому урону.", "+20% damage over time.", "dmg_dot:20"),
                T("Чародей-универсал", "Universalist", "+4% к урону за каждую школу магии на панели (до +32%).",
                    "+4% damage per school of magic in your bar (up to +32%).", "flag:universalist"));
            B("Целительство", "Healing", "heal", new Color(0.45f, 0.95f, 0.55f),
                T("Целебное касание", "Healing Touch", "+12% к силе исцеления.", "+12% healing.", "heal:12"),
                T("Обновление", "Renewal", "+15% к восстановлению здоровья.", "+15% health regeneration.", "hp_regen:15"),
                T("Тёплые руки", "Warm Hands", "Исцеляя союзника, вы лечитесь сами на четверть. +6% исцеления.",
                    "Healing an ally heals you for a quarter. +6% healing.", "flag:warm_hands;heal:6"),
                T("Поток магии", "Flow", "+12% к восстановлению магии.", "+12% eitr regeneration.", "eitr_regen:12"),
                T("Сортировка", "Triage", "+40% исцеления для целей ниже 35% здоровья.", "+40% healing on targets below 35% health.", "flag:triage"),
                T("Святой Мунго", "St Mungo's", "Лишнее исцеление превращается в щит. +10% исцеления.", "Overhealing becomes a shield. +10% healing.",
                    "flag:overheal_shield;heal:10"),
                T("Костерост", "Skele-Gro", "Исцелённые получают +20% к скорости на 5 с.", "Healed allies get +20% speed for 5 s.", "flag:heal_haste"),
                T("Клятва целителя", "Healer's Oath", "Исцеление ×1,5 и щит из избытка, но тёмные заклинания вам недоступны.",
                    "Healing ×1.5 and overheal shields, but dark spells are closed to you.", "flag:ks_healer"));
            B("Тёмные искусства", "Dark Arts", "dark", new Color(0.72f, 0.42f, 1f),
                T("Шёпот тьмы", "Whisper", "+10% к урону тёмной магией.", "+10% dark damage.", "dmg_dark:10"),
                T("Кровопийца", "Leech", "2% урона заклинаний возвращается здоровьем.", "2% of spell damage heals you.", "lifesteal:2"),
                T("Пожиратель", "Devourer", "Убийство тёмным заклинанием восстанавливает магию.", "A kill with a dark spell restores eitr.", "flag:kill_restore"),
                T("Проклятия", "Hexes", "+10% к длительности эффектов и периодическому урону.", "+10% effect duration and damage over time.", "dur:10;dmg_dot:10"),
                T("Ужас", "Dread", "+12% к длительности контроля.", "+12% control duration.", "ctrl_dur:12"),
                T("Владыка Тьмы", "Dark Lord", "Тьма сильнее усиливает урон. +10% к урону тёмной магией.", "Darkness boosts damage more. +10% dark damage.",
                    "flag:dark_lord;dmg_dark:10"),
                T("Тёмная воля", "Dark Will", "+20% к бонусу Тьмы, +4% урона.", "+20% Darkness bonus, +4% damage.", "dark_bonus:20;dmg:4"),
                T("Обскур", "Obscurus", "При полной магии урон заклинаний ×2, но палочка больше не восполняет магию сама.",
                    "At full eitr spell damage ×2, but the wand no longer restores eitr by itself.", "flag:ks_obscurus"));
            B("Чары", "Charms", "util", new Color(0.35f, 0.9f, 0.9f),
                T("Ясный ум", "Clear Mind", "+15 к магии.", "+15 eitr.", "eitr:15"),
                T("Бережливость", "Thrift", "Заклинания дешевле на 6%.", "Spells cost 6% less.", "cost:6"),
                T("Кукловод", "Puppeteer", "Очарование длится на 30% дольше. +10% к длительности контроля.", "Charms last 30% longer. +10% control duration.",
                    "flag:puppet_master;ctrl_dur:10"),
                T("Призыватель", "Conjurer", "+15% к урону и здоровью призванных.", "+15% summon damage and health.", "summon_dmg:15;summon_hp:15"),
                T("Перезарядка", "Recharge", "−6% перезарядки.", "−6% cooldowns.", "cdr:6"),
                T("Стена разума", "Mind Wall", "+10% к магии, иммунитет к очарованию и замешательству.", "+10% eitr, immune to charm and confusion.",
                    "eitr_pct:10;flag:mind_wall"),
                T("Дальнобойность", "Long Reach", "+15% к дальности, +10% к скорости снарядов.", "+15% range, +10% projectile speed.", "range:15;proj_speed:10"),
                T("Беспалочковая магия", "Wandless Magic", "Можно колдовать без палочки (4-я кнопка мыши), такие заклинания дороже на 20%.",
                    "You may cast without a wand (mouse button 4); such spells cost 20% more.", "flag:ks_wandless"));
        }

        private static Talent T(string ru, string en, string dru, string den, string mods)
        {
            return new Talent { Ru = ru, En = en, DescRu = dru, DescEn = den, Mods = mods };
        }

        private static void B(string ru, string en, string glyph, Color c, params Talent[] talents)
        {
            int b = Branches.Count;
            Branches.Add(new TalentBranch { Ru = ru, En = en, Glyph = glyph, Color = c });
            float ang = (-60f + 60f * b) * Mathf.Deg2Rad; // no branch runs along the book's spine
            var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            var side = new Vector2(-dir.y, dir.x);
            for (int i = 0; i < talents.Length; i++)
            {
                var t = talents[i];
                t.Branch = b;
                t.Index = i + 1;
                t.Id = IdOf(b, t.Index);
                t.Year = YearOf[t.Index];
                t.Kind = t.Index == 8 ? 2 : (t.Index == 3 || t.Index == 6) ? 1 : 0;
                t.Cost = t.Kind == 2 ? 2 : 1;
                float zig = t.Kind == 0 ? (t.Index % 2 == 0 ? 12f : -12f) : 0f;
                t.Pos = dir * (56f + 33f * t.Index) + side * zig;
                All.Add(t);
                s_byId[t.Id] = t;
            }
        }

        public static bool CanTake(WizardState w, Talent t, out string why)
        {
            why = null;
            if (w.Talents.Contains(t.Id))
            {
                why = Texts.L("Уже взят.", "Already taken.");
                return false;
            }

            if (w.Year < t.Year)
            {
                why = Texts.L("Откроется на ", "Opens in year ") + t.Year + Texts.L(" курсе.", ".");
                return false;
            }

            if (t.Prev != null && !w.Talents.Contains(t.Prev))
            {
                why = Texts.L("Сначала возьмите предыдущую звезду ветви.", "Take the previous star of the branch first.");
                return false;
            }

            if (w.PointsFree < t.Cost)
            {
                why = Texts.L("Нужно очков: ", "Points needed: ") + t.Cost + ".";
                return false;
            }

            return true;
        }

        public static void Take(WizardState w, Talent t)
        {
            string why;
            if (!CanTake(w, t, out why))
            {
                Casting.Message(why);
                return;
            }

            w.Talents.Add(t.Id);
            Wizard.Changed();
            Fx.Sound(Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero, "chime", 0.5f);
        }

        public static void Reset(WizardState w)
        {
            w.Talents.Clear();
            Wizard.Changed();
            Casting.Message(Texts.L("Таланты сброшены, очки вернулись.", "Talents reset, the points are back."));
        }

        public static int Spent(WizardState w)
        {
            int n = 0;
            foreach (var id in w.Talents)
            {
                var t = Get(id);
                if (t != null) n += t.Cost;
            }

            return n;
        }
    }

    internal enum LearnState { Known, Learnable, NeedYear, NeedBook, NeedPoints, NeedCodex, Closed }

    /// <summary>Spells are learned at a Codex for magic points; prank, ritual and grand spells also need their book read.</summary>
    internal static class Learning
    {
        public static bool Listed(SpellDef s)
        {
            if (s == null || !s.Castable)
            {
                return false;
            }

            switch (s.Role)
            {
                case "tree": case "book": case "prank": case "ritual": case "grand": case "basic": return true;
                default: return false;
            }
        }

        public static int Cost(SpellDef s)
        {
            if (s == null || s.Role == "basic") return 0;
            if (s.IsGrand) return 3;
            if (s.IsRitual) return 2;
            return s.Year >= 7 ? 3 : s.Year >= 4 ? 2 : 1;
        }

        public static string BookFor(SpellDef s)
        {
            switch (s.Role)
            {
                case "prank": return "weasley";
                case "ritual": return "rituals";
                case "grand": return "grimoire";
                default: return null;
            }
        }

        public static string BookName(string marker)
        {
            switch (marker)
            {
                case "weasley": return Localization.instance.Localize("$wz_book_weasley");
                case "rituals": return Localization.instance.Localize("$wz_book_rituals");
                case "grimoire": return Localization.instance.Localize("$wz_book_grimoire");
                default: return marker;
            }
        }

        public static LearnState State(Player p, WizardState w, SpellDef s)
        {
            if (w.KnowsSpell(s.Id)) return LearnState.Known;
            if (!Listed(s)) return LearnState.Closed;
            if (w.Year < s.Year) return LearnState.NeedYear;
            string book = BookFor(s);
            if (book != null && !w.Books.Contains(book)) return LearnState.NeedBook;
            if (w.Stats.Has("ks_healer") && s.IsDark) return LearnState.Closed;
            if (w.PointsFree < Cost(s)) return LearnState.NeedPoints;
            if (!Exams.NearCodex(p)) return LearnState.NeedCodex;
            return LearnState.Learnable;
        }

        public static string Why(LearnState st, SpellDef s)
        {
            switch (st)
            {
                case LearnState.NeedYear: return Texts.L("Откроется на ", "Opens in year ") + s.Year + Texts.L(" курсе", "");
                case LearnState.NeedBook: return Texts.L("Нужна книга «", "Needs the book \"") + BookName(BookFor(s)) + Texts.L("»", "\"");
                case LearnState.NeedPoints: return Texts.L("Не хватает очков магии", "Not enough magic points");
                case LearnState.NeedCodex: return Texts.L("Подойдите к Кодексу", "Go to a Codex");
                case LearnState.Closed: return Texts.L("Недоступно", "Closed to you");
                default: return "";
            }
        }

        public static void Learn(Player p, WizardState w, SpellDef s)
        {
            if (State(p, w, s) != LearnState.Learnable)
            {
                Casting.Message(Why(State(p, w, s), s));
                return;
            }

            w.Known.Add(s.Id);
            w.Bought.Add(s.Id);
            for (int i = 0; i < w.Bar.Length; i++)
            {
                if (w.Bar[i] == null)
                {
                    w.Bar[i] = s.Id;
                    break;
                }
            }

            Wizard.Changed();
            Fx.Send(FxKind.Buff, p.transform.position, Vector3.up, s.Id, 0f);
            Casting.Message(Texts.L("Изучено: ", "Learned: ") + s.Name);
            Pieces.Excite(p.transform.position);
        }

        public static int Spent(WizardState w)
        {
            int n = 0;
            foreach (var id in w.Bought)
            {
                n += Cost(SpellDb.Get(id));
            }

            return n;
        }
    }

    /// <summary>Years: a boss trophy shown to a Codex passes that year's exam (the trophy is kept).</summary>
    internal static class Exams
    {
        public static readonly string[] BossNamesRu = { "Эйктюр", "Древний", "Масса Костей", "Модер", "Яглут", "Королева", "Фейдер", "Король Холода" };
        public static readonly string[] BossNamesEn = { "Eikthyr", "The Elder", "Bonemass", "Moder", "Yagluth", "The Queen", "Fader", "The Frost King" };
        public static readonly string[] BiomesRu = { "Луга", "Чёрный лес", "Болото", "Горы", "Равнины", "Туманные земли", "Пепельные земли", "Глубокий Север" };
        public static readonly string[] BiomesEn = { "Meadows", "Black Forest", "Swamp", "Mountains", "Plains", "Mistlands", "Ashlands", "Deep North" };

        public static string BossName(int i)
        {
            return i < 0 || i >= BossNamesRu.Length ? "?" : Texts.English ? BossNamesEn[i] : BossNamesRu[i];
        }

        public static string Biome(int i)
        {
            return i < 0 || i >= BiomesRu.Length ? "?" : Texts.English ? BiomesEn[i] : BiomesRu[i];
        }

        /// <summary>The trophy prefab shown for a boss in the year ladder.</summary>
        public static string TrophyPrefab(int i)
        {
            string[] t;
            return i >= 0 && i < Items.Bosses.Length && Loot.Trophies.TryGetValue(Items.Bosses[i], out t) ? t[0] : null;
        }

        public static bool NearCodex(Player p)
        {
            return p != null && Pieces.Nearest(p.transform.position, 10f) != null;
        }

        /// <summary>Trophies in the inventory that would pass an exam.</summary>
        public static List<KeyValuePair<string, ItemDrop.ItemData>> Ready(Player p, WizardState w)
        {
            var list = new List<KeyValuePair<string, ItemDrop.ItemData>>();
            if (p == null || w == null)
            {
                return list;
            }

            foreach (var it in p.GetInventory().GetAllItems())
            {
                string boss = Loot.BossOfTrophy(Items.PrefabOf(it));
                if (boss != null && !w.Trophies.Contains(boss) && list.All(kv => kv.Key != boss))
                {
                    list.Add(new KeyValuePair<string, ItemDrop.ItemData>(boss, it));
                }
            }

            return list;
        }

        public static void Pass(Player p, WizardState w, string boss)
        {
            if (!NearCodex(p))
            {
                Casting.Message(Texts.L("Экзамен сдают у Кодекса.", "Exams are taken at a Codex."));
                return;
            }

            if (!w.Trophies.Add(boss))
            {
                return;
            }

            Wizard.Changed();
            Fx.Send(FxKind.Ceremony, p.transform.position, Vector3.up, "", 0f, "year");
            Pieces.Excite(p.transform.position);
            MessageHud.instance?.ShowBiomeFoundMsg(Texts.L("Курс ", "Year ") + w.Year, true);
            Casting.Message(Texts.L("Экзамен сдан! Теперь вы на ", "Exam passed! You are now in year ") + w.Year + Texts.L(" курсе: открылись новые заклинания и таланты.",
                ": new spells and talents are open."));
        }
    }

    /// <summary>Books, cards and memories, read from the grimoire.</summary>
    internal static class Reading
    {
        public static string What(Player p, WizardState w, string prefab)
        {
            if (prefab.StartsWith("WZ_Core_"))
            {
                var wand = Wands.Equipped(p) ?? Wands.Best(p);
                return wand == null ? null : Texts.L("вставить в палочку «", "set into the wand \"") + Localization.instance.Localize(wand.m_shared.m_name) +
                                                     Texts.L("» (старая сердцевина пропадёт)", "\" (the old core is lost)");
            }

            if (prefab == Items.Weasley) return w.Books.Contains("weasley") ? null : Texts.L("открывает заклинания-розыгрыши", "opens the prank spells");
            if (prefab == Items.Rituals) return w.Books.Contains("rituals") ? null : Texts.L("открывает ритуалы", "opens the rituals");
            if (prefab == Items.Grimoire) return w.Books.Contains("grimoire") ? null : Texts.L("открывает великие чары", "opens the grand spells");
            if (prefab == Items.Scroll || prefab == Items.Cup) return Texts.L("сбрасывает таланты где угодно", "resets the talents anywhere");
            if (prefab == Items.Practice) return Texts.L("+300 опыта рангов заклинанию в выбранной ячейке", "+300 rank experience to the spell in the chosen slot");
            if (prefab == Items.Card) return Texts.L("+1 ✦ за нового волшебника", "+1 ✦ for a new wizard");
            if (prefab == Items.Memory) return MemoryCount(w) < 10 ? Texts.L("+1 ✦", "+1 ✦") : null;
            if (prefab.StartsWith(Items.Memory + "_")) return w.Books.Contains("mem_" + prefab.Substring(Items.Memory.Length + 1)) ? null : Texts.L("+1 ✦", "+1 ✦");
            if (prefab.StartsWith(Items.Book))
            {
                int y;
                if (int.TryParse(prefab.Substring(Items.Book.Length), out y)) return w.Books.Contains(y.ToString()) ? null : Texts.L("+2 ✦ (один раз)", "+2 ✦ (once)");
            }

            return null;
        }

        private static int MemoryCount(WizardState w)
        {
            return w.Books.Count(b => b.StartsWith("mem#"));
        }

        public static int Points(WizardState w)
        {
            int n = 0;
            foreach (var b in w.Books)
            {
                if (b.Length == 1 && char.IsDigit(b[0])) n += 2;
                else if (b.StartsWith("mem")) n += 1;
            }

            return n;
        }

        public static void Read(Player p, WizardState w, ItemDrop.ItemData it)
        {
            string prefab = Items.PrefabOf(it) ?? "";
            if (What(p, w, prefab) == null)
            {
                return;
            }

            if (prefab.StartsWith("WZ_Core_"))
            {
                var wand = Wands.Equipped(p) ?? Wands.Best(p);
                Wands.SetCore(wand, prefab.Substring("WZ_Core_".Length));
                p.GetInventory().RemoveItem(it, 1);
                Wizard.Changed();
                Fx.Send(FxKind.Ceremony, p.transform.position, Vector3.up, "", 0f, "core");
                Casting.Message(Localization.instance.Localize(it.m_shared.m_name) + Texts.L(" — теперь сердцевина вашей палочки.", " is now your wand's core."));
                return;
            }

            if (prefab == Items.Weasley) w.Books.Add("weasley");
            else if (prefab == Items.Rituals) w.Books.Add("rituals");
            else if (prefab == Items.Grimoire) w.Books.Add("grimoire");
            else if (prefab == Items.Scroll || prefab == Items.Cup) TalentTree.Reset(w);
            else if (prefab == Items.Practice)
            {
                string id = w.Bar[Mathf.Clamp(w.Selected, 0, 7)];
                if (id == null || SpellDb.Get(id) == null || SpellDb.Get(id).Role == "basic")
                {
                    Casting.Message(Texts.L("Выберите на панели заклинание, которое хотите тренировать.", "Choose the spell to train in your bar first."));
                    return;
                }

                float xp;
                w.Xp.TryGetValue(id, out xp);
                w.Xp[id] = xp + 300f;
                Casting.Message(SpellDb.Get(id).Name + Texts.L(": +300 опыта", ": +300 experience"));
            }
            else if (prefab == Items.Memory) w.Books.Add("mem#" + MemoryCount(w));
            else if (prefab.StartsWith(Items.Memory + "_")) w.Books.Add("mem_" + prefab.Substring(Items.Memory.Length + 1));
            else if (prefab == Items.Card)
            {
                var unread = Enumerable.Range(0, Items.CardNames.Length).Where(i => !w.Cards.Contains(Items.CardNamesEn[i])).ToList();
                if (unread.Count == 0)
                {
                    Casting.Message(Texts.L("Этого волшебника вы уже знаете.", "You already know this wizard."));
                    return;
                }

                int i = unread[Random.Range(0, unread.Count)];
                w.Cards.Add(Items.CardNamesEn[i]);
                Casting.Message(Texts.L("Вкладыш: ", "Card: ") + (Texts.English ? Items.CardNamesEn[i] : Items.CardNames[i]) + Texts.L(" (+1 очко магии)", " (+1 magic point)"));
            }
            else if (prefab.StartsWith(Items.Book))
            {
                int y;
                if (int.TryParse(prefab.Substring(Items.Book.Length), out y)) w.Books.Add(y.ToString());
            }

            p.GetInventory().RemoveItem(it, 1);
            Wizard.Changed();
            Fx.Send(FxKind.Buff, p.transform.position, Vector3.up, "", 0f);
            Casting.Message(Texts.L("Прочитано: ", "Read: ") + Localization.instance.Localize(it.m_shared.m_name));
        }
    }
}
