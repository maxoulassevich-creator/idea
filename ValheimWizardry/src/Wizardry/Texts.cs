using System.Collections.Generic;
using Jotunn.Entities;
using Jotunn.Managers;

namespace Wizardry
{
    /// <summary>Names of items, pieces and effects (Jotunn localization) and the language of the mod's windows.</summary>
    internal static class Texts
    {
        private static bool? s_english;

        /// <summary>The windows speak Russian unless the game (or the config) says English.</summary>
        public static bool English
        {
            get
            {
                if (s_english.HasValue)
                {
                    return s_english.Value;
                }

                string cfg = ModConfig.Language != null ? ModConfig.Language.Value.Trim().ToLowerInvariant() : "auto";
                if (cfg == "ru")
                {
                    s_english = false;
                }
                else if (cfg == "en")
                {
                    s_english = true;
                }
                else
                {
                    string lang = Localization.instance != null ? Localization.instance.GetSelectedLanguage() : "Russian";
                    if (Localization.instance == null)
                    {
                        return false; // not decided yet: ask again later
                    }

                    s_english = lang != "Russian" && lang != "Ukrainian" && lang != "Belarusian";
                }

                return s_english.Value;
            }
        }

        public static string L(string ru, string en)
        {
            return English ? en : ru;
        }

        // token -> (ru, en)
        private static readonly Dictionary<string, string[]> s_tokens = new Dictionary<string, string[]>
        {
            { "wz_skill", new[] { "Волшебство", "Wizardry" } },
            { "wz_skill_desc", new[] { "Владение палочкой: сила заклинаний и очки Древа Чар.", "Wand mastery: spell power and points for the Tree of Charms." } },
            { "wz_codex", new[] { "Кодекс Вечных Теней", "Codex of Eternal Shadows" } },
            { "wz_codex_desc", new[] { "Стол чародея: палочки, книги и Древо Чар (клавиша K рядом).", "The wizard's table: wands, books and the Tree of Charms (K nearby)." } },
            { "wz_ext_stand", new[] { "Подставка для палочек", "Wand stand" } },
            { "wz_ext_cauldron", new[] { "Котёл зельевара", "Potioneer's cauldron" } },
            { "wz_ext_ball", new[] { "Хрустальный шар", "Crystal ball" } },
            { "wz_ext_pensieve", new[] { "Омут памяти", "Pensieve" } },
            { "wz_ext_astrolabe", new[] { "Астролябия", "Astrolabe" } },
            { "wz_ext_turner", new[] { "Маховик времени", "Time-Turner" } },
            { "wz_ext_desc", new[] { "Расширение Кодекса.", "An extension of the Codex." } },
            { "wz_anchor", new[] { "Чары", "Enchantment" } },

            { "wz_wand_beech", new[] { "Палочка из бука", "Beech wand" } },
            { "wz_wand_fir", new[] { "Палочка из хвои", "Fir wand" } },
            { "wz_wand_birch", new[] { "Палочка из берёзы", "Birch wand" } },
            { "wz_wand_ancient", new[] { "Палочка из древней коры", "Ancient-bark wand" } },
            { "wz_wand_eldercopy", new[] { "Палочка Древнего (копия Бузинной)", "Elder's wand (a copy)" } },
            { "wz_wand_yggdrasil", new[] { "Палочка из Иггдрасиля", "Yggdrasil wand" } },
            { "wz_wand_ash", new[] { "Палочка из пепельного ясеня", "Ashwood wand" } },
            { "wz_wand_north", new[] { "Палочка Глубокого Севера", "Deep North wand" } },
            { "wz_wand_elder", new[] { "Бузинная палочка", "The Elder Wand" } },
            { "wz_wand_desc", new[] { "Волшебная палочка. ЛКМ — выбранное заклинание, СКМ — Базовый выстрел, ПКМ — Протего. Сердцевину вставляют у Кодекса.", "A wand. LMB casts the chosen spell, MMB the Basic Cast, RMB is Protego. Set its core at the Codex." } },

            { "wz_core_feather", new[] { "Сердцевина: перо ворона", "Core: raven feather" } },
            { "wz_core_antler", new[] { "Сердцевина: рог громового оленя", "Core: thunder-stag antler" } },
            { "wz_core_fenris", new[] { "Сердцевина: шерсть Фенрира", "Core: Fenris hair" } },
            { "wz_core_wishbone", new[] { "Сердцевина: вилочка Костяной массы", "Core: Bonemass wishbone" } },
            { "wz_core_serpent", new[] { "Сердцевина: чешуя морского змея", "Core: sea-serpent scale" } },
            { "wz_core_dragon", new[] { "Сердцевина: сердечная жила дракона", "Core: dragon heartstring" } },
            { "wz_core_troll", new[] { "Сердцевина: шкура тролля", "Core: troll hide" } },
            { "wz_core_celestial", new[] { "Сердцевина: небесное перо", "Core: celestial feather" } },
            { "wz_core_morgen", new[] { "Сердцевина: сердце моргена", "Core: morgen heart" } },
            { "wz_core_ice", new[] { "Сердцевина: ледяное ядро", "Core: ice core" } },
            { "wz_core_desc", new[] { "Вставляется в палочку у Кодекса (вкладка «Палочка»).", "Set into a wand at the Codex (the Wand tab)." } },

            { "wz_book", new[] { "Стандартная книга заклинаний", "The Standard Book of Spells" } },
            { "wz_book_desc", new[] { "Прочитайте у Кодекса (вкладка «Книги»): учит заклинаниям своего курса.", "Read it at the Codex (Books tab): teaches the spells of its year." } },
            { "wz_book_weasley", new[] { "Набор Уизли", "Weasleys' Wizard Wheezes kit" } },
            { "wz_book_rituals", new[] { "Книга ритуалов", "Book of Rituals" } },
            { "wz_book_grimoire", new[] { "Гримуар великих чар", "Grimoire of Grand Spells" } },
            { "wz_book_practice", new[] { "Книга практики", "Practice book" } },
            { "wz_book_practice_desc", new[] { "Прочитайте у Кодекса: +300 опыта рангов выбранному заклинанию.", "Read at the Codex: +300 rank experience to a chosen spell." } },
            { "wz_scroll", new[] { "Свиток Обливиэйта", "Obliviate scroll" } },
            { "wz_scroll_desc", new[] { "Возвращает одно очко Древа (снять узел у Кодекса).", "Refunds one tree point (remove a node at the Codex)." } },
            { "wz_cup", new[] { "Чаша Омута", "Pensieve cup" } },
            { "wz_cup_desc", new[] { "Полный сброс Древа у Кодекса.", "A full reset of the tree at the Codex." } },
            { "wz_card", new[] { "Вкладыш от шоколадной лягушки", "Chocolate Frog card" } },
            { "wz_card_desc", new[] { "Знаменитый волшебник. Прочитайте у Кодекса: +1 очко Древа.", "A famous wizard. Read it at the Codex: +1 tree point." } },
            { "wz_memory", new[] { "Воспоминание", "Memory" } },
            { "wz_memory_desc", new[] { "Вставляется в гнездо Омута на Древе.", "Goes into a Pensieve socket of the tree." } },
            { "wz_stone", new[] { "Воскрешающий камень", "Resurrection Stone" } },
            { "wz_stone_desc", new[] { "Дар Смерти. Носите с собой: раз в 20 мин эхо павшего союзника (заклинание «Эхо» в книге).", "A Deathly Hallow. Carry it: once in 20 minutes an echo of a fallen friend." } },
            { "wz_cloak", new[] { "Мантия-невидимка", "Invisibility Cloak" } },
            { "wz_cloak_desc", new[] { "Дар Смерти. Пока вы крадётесь или стоите, вас не видно; первая атака из невидимости ×2.", "A Deathly Hallow. While you sneak or stand still you cannot be seen; the first attack from it deals double." } },
            { "wz_potion_felix", new[] { "Феликс Фелицис", "Felix Felicis" } },
            { "wz_potion_polyjuice", new[] { "Оборотное зелье", "Polyjuice Potion" } },
            { "wz_potion_pepperup", new[] { "Бодроперцовое зелье", "Pepperup Potion" } },
            { "wz_potion_death", new[] { "Напиток живой смерти", "Draught of Living Death" } },
            { "wz_potion_strength", new[] { "Укрепляющий раствор", "Strengthening Solution" } },
            { "wz_potion_elixir", new[] { "Эликсир жизни", "Elixir of Life" } },
            { "wz_potion_desc", new[] { "Зелье Зельевара (рецепт открывает призвание).", "A Potioneer's potion (the specialisation unlocks the recipe)." } },
        };

        public static void Register()
        {
            var loc = LocalizationManager.Instance.GetLocalization();
            var ru = new Dictionary<string, string>();
            var en = new Dictionary<string, string>();
            foreach (var kv in s_tokens)
            {
                ru[kv.Key] = kv.Value[0];
                en[kv.Key] = kv.Value[1];
            }

            foreach (var s in StatusDefs.All)
            {
                ru["wz_se_" + s.Key] = s.Ru;
                en["wz_se_" + s.Key] = s.En;
            }

            loc.AddTranslation("Russian", ru);
            loc.AddTranslation("English", en);
        }
    }
}
