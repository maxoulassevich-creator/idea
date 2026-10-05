using System.Collections.Generic;
using Jotunn.Managers;

namespace FlyingPets
{
    /// <summary>English and Russian texts, registered as $tokens so the game localizes them.</summary>
    internal static class Texts
    {
        private static readonly Dictionary<string, string> English = new Dictionary<string, string>
        {
            { "item_fp_staff", "Skybound Staff" },
            { "item_fp_staff_desc", "Calls a winged mount.\nAttack: summon the mount or call it to you.\nSecondary attack: send it away (or pick another mount)." },
            { "fp_pet_pegasus", "Pegasus" },
            { "fp_pet_raven", "Raven" },
            { "fp_pet_dragon", "Water Dragon" },
            { "fp_hover_ride", "Ride" },
            { "fp_hover_release", "Send away" },
            { "fp_hover_busy", "(someone is riding)" },
            { "fp_hover_owner", "Companion of {0}" },
            { "fp_msg_summon", "{0} answers your call" },
            { "fp_msg_call", "{0} is coming to you" },
            { "fp_msg_dismiss", "{0} returns to the sky" },
            { "fp_msg_select", "The staff now calls: {0}" },
            { "fp_msg_notyours", "This mount does not obey you" },
            { "fp_msg_busy", "Someone is already riding" },
            { "fp_msg_nopets", "Flying Pets: the mounts failed to load - see BepInEx\\LogOutput.log" },
            { "fp_msg_noprefab", "Flying Pets: {0} is not ready - see BepInEx\\LogOutput.log" },
            { "fp_msg_landing", "Landing..." },
            { "fp_msg_noroom", "Not enough room here" },
            { "fp_msg_controls", "W - forward (where you look)   Shift - faster   Space - take off / climb   Ctrl - descend / land   E - dismount" },
            { "fp_msg_skill_thunder_hoof", "Mouse wheel (in flight) - Thunder hoof" },
            { "fp_msg_skill_talon_grab", "Mouse wheel (in flight) - Talon grab / let go" },
            { "fp_msg_skill_tidal_breath", "Mouse wheel - Tidal breath" },
            { "fp_msg_fish", "Fisher: the dragon caught {0}" },
            { "fp_msg_fish_full", "Fisher: no room for the fish, the dragon lets it go" },
            { "fp_msg_need_air", "Take off first" },
            { "fp_msg_cooldown", "{0}: {1} s more" },
            { "fp_msg_grab_none", "Nobody to grab - fly low over a creature" },
            { "fp_msg_grab_big", "{0} is too heavy for the raven" },
            { "fp_msg_grab", "The raven seizes: {0}" },
            { "fp_msg_drop", "The raven lets go: {0}" },
            { "fp_msg_caught", "The pegasus caught you!" },
            { "fp_msg_muninn", "Muninn remembers: {0}" },
            { "fp_pin_dungeon", "Dungeon" },
            { "fp_pin_trader", "Trader" },
            { "fp_pin_runestone", "Runestone" },
            { "fp_pin_altar", "Altar" },
            { "fp_se_pegasus_tip", "Draft horse: you carry more in the saddle.\nUnder the wing: rain and frost cannot reach you.\nLight of Valhalla: the undead keep away from the pegasus.\nValkyrie's catch: the pegasus catches you when you fall.\nSecondary attack in flight: Thunder hoof." },
            { "fp_se_raven_tip", "Huginn: the map opens much wider while you fly.\nMuninn: dungeons, traders, runestones and altars are pinned on your map.\nRavens' feast: trophies drop more often near the raven.\nShadow over the prey: animals freeze under the raven's shadow.\nSecondary attack in flight: Talon grab." },
            { "fp_se_thunder", "Thunder hoof" },
            { "fp_se_thunder_tip", "The hoof gathers the storm again." },
            { "fp_se_grab", "Talon grab" },
            { "fp_se_grab_tip", "The talons rest." },
            { "fp_se_hold", "In the talons" },
            { "fp_se_hold_tip", "Secondary attack: let go. From up high the prey will not survive the fall; near the ground it is set down gently." },
            { "fp_se_catch", "Valkyrie's catch" },
            { "fp_se_catch_tip", "The pegasus needs time before it can catch you again." },
            { "fp_se_dragon_tip", "Sea truce: serpents keep away from the dragon unless provoked.\nLiving water: while you are wet near the dragon, your health comes back faster.\nFisher: skim low and fast over water and the dragon brings up fish.\nSteaming scales: you do not catch fire in the saddle.\nSecondary attack: Tidal breath." },
            { "fp_se_breath", "Tidal breath" },
            { "fp_se_breath_tip", "The dragon draws in the sea again." },
            { "fp_se_living", "Living water" },
            { "fp_se_living_tip", "The dragon's water heals: your health comes back faster while you are wet." },
        };

        private static readonly Dictionary<string, string> Russian = new Dictionary<string, string>
        {
            { "item_fp_staff", "Посох небесных скакунов" },
            { "item_fp_staff_desc", "Призывает крылатого скакуна.\nАтака: призвать или позвать к себе.\nВторичная атака: отпустить (или выбрать другого скакуна)." },
            { "fp_pet_pegasus", "Пегас" },
            { "fp_pet_raven", "Ворон" },
            { "fp_pet_dragon", "Водяной дракон" },
            { "fp_hover_ride", "Оседлать" },
            { "fp_hover_release", "Отпустить" },
            { "fp_hover_busy", "(на нём кто-то сидит)" },
            { "fp_hover_owner", "Питомец: {0}" },
            { "fp_msg_summon", "{0} откликается на зов" },
            { "fp_msg_call", "{0} спешит к вам" },
            { "fp_msg_dismiss", "{0} возвращается в небо" },
            { "fp_msg_select", "Посох теперь зовёт: {0}" },
            { "fp_msg_notyours", "Этот скакун вас не слушается" },
            { "fp_msg_busy", "На нём уже кто-то сидит" },
            { "fp_msg_nopets", "Flying Pets: скакуны не загрузились - подробности в BepInEx\\LogOutput.log" },
            { "fp_msg_noprefab", "Flying Pets: {0} не готов - подробности в BepInEx\\LogOutput.log" },
            { "fp_msg_landing", "Снижаемся..." },
            { "fp_msg_noroom", "Здесь слишком тесно" },
            { "fp_msg_controls", "W - вперёд (куда смотрите)   Shift - быстрее   Пробел - взлёт / вверх   Ctrl - вниз / посадка   E - спешиться" },
            { "fp_msg_skill_thunder_hoof", "Колесо мыши (в полёте) - Громовое копыто" },
            { "fp_msg_skill_talon_grab", "Колесо мыши (в полёте) - Хватка / отпустить" },
            { "fp_msg_skill_tidal_breath", "Колесо мыши - Дыхание прилива" },
            { "fp_msg_fish", "Рыболов: дракон поймал {0}" },
            { "fp_msg_fish_full", "Рыболов: рыбу некуда положить, дракон её отпустил" },
            { "fp_msg_need_air", "Сначала взлетите" },
            { "fp_msg_cooldown", "{0}: ещё {1} с" },
            { "fp_msg_grab_none", "Хватать некого - снизьтесь над существом" },
            { "fp_msg_grab_big", "{0} - слишком тяжело для ворона" },
            { "fp_msg_grab", "Ворон схватил: {0}" },
            { "fp_msg_drop", "Ворон отпустил: {0}" },
            { "fp_msg_caught", "Пегас подхватил вас!" },
            { "fp_msg_muninn", "Мунин запомнил: {0}" },
            { "fp_pin_dungeon", "Подземелье" },
            { "fp_pin_trader", "Торговец" },
            { "fp_pin_runestone", "Рунный камень" },
            { "fp_pin_altar", "Алтарь" },
            { "fp_se_pegasus_tip", "Тяжеловоз: в седле можно унести больше.\nПод крылом: ни дождь, ни мороз до вас не достают.\nСвет Вальгаллы: нежить держится подальше от пегаса.\nПодхват валькирии: пегас ловит вас при падении.\nВторичная атака в полёте: Громовое копыто." },
            { "fp_se_raven_tip", "Хугин: в полёте карта открывается намного шире.\nМунин: подземелья, торговцы, рунные камни и алтари сами отмечаются на карте.\nПир воронья: рядом с вороном трофеи выпадают чаще.\nТень над добычей: звери замирают под тенью ворона.\nВторичная атака в полёте: Хватка." },
            { "fp_se_thunder", "Громовое копыто" },
            { "fp_se_thunder_tip", "Копыто снова набирается грозы." },
            { "fp_se_grab", "Хватка" },
            { "fp_se_grab_tip", "Когти отдыхают." },
            { "fp_se_hold", "В когтях" },
            { "fp_se_hold_tip", "Вторичная атака: отпустить. С высоты добыча не переживёт падения, у самой земли ворон опустит её бережно." },
            { "fp_se_catch", "Подхват валькирии" },
            { "fp_se_catch_tip", "Пегасу нужно время, прежде чем он снова сможет вас поймать." },
            { "fp_se_dragon_tip", "Морское перемирие: морские змеи не трогают дракона, пока их не разозлить.\nЖивая вода: мокрым рядом с драконом здоровье восстанавливается быстрее.\nРыболов: летите низко и быстро над водой - дракон выхватывает рыбу.\nШипящая чешуя: в седле вы не загораетесь.\nВторичная атака: Дыхание прилива." },
            { "fp_se_breath", "Дыхание прилива" },
            { "fp_se_breath_tip", "Дракон снова набирает в грудь море." },
            { "fp_se_living", "Живая вода" },
            { "fp_se_living_tip", "Вода дракона лечит: пока вы мокрые, здоровье восстанавливается быстрее." },
        };

        public static void Register()
        {
            var loc = LocalizationManager.Instance.GetLocalization();
            loc.AddTranslation("English", English);
            loc.AddTranslation("Russian", Russian);
        }

        /// <summary>Localized text for a token (without the $), with optional {0} arguments.</summary>
        public static string Get(string token, params object[] args)
        {
            string text = Localization.instance != null ? Localization.instance.Localize("$" + token) : token;
            if (string.IsNullOrEmpty(text) || text.StartsWith("[")) // missing token -> built-in English
            {
                string en;
                text = English.TryGetValue(token, out en) ? en : token;
            }

            return args != null && args.Length > 0 ? string.Format(text, args) : text;
        }

        public static bool Has(string token)
        {
            return English.ContainsKey(token);
        }

        /// <summary>Registers a pet name token on the fly (for pets added by dropping a folder into pets/).</summary>
        public static void AddPetName(string key, string english, string russian)
        {
            string token = "fp_pet_" + key;
            if (English.ContainsKey(token))
            {
                return;
            }

            var loc = LocalizationManager.Instance.GetLocalization();
            loc.AddTranslation("English", new Dictionary<string, string> { { token, english } });
            loc.AddTranslation("Russian", new Dictionary<string, string> { { token, string.IsNullOrEmpty(russian) ? english : russian } });
            English[token] = english;
        }
    }
}
