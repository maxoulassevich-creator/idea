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
        };

        private static readonly Dictionary<string, string> Russian = new Dictionary<string, string>
        {
            { "item_fp_staff", "Посох небесных скакунов" },
            { "item_fp_staff_desc", "Призывает крылатого скакуна.\nАтака: призвать или позвать к себе.\nВторичная атака: отпустить (или выбрать другого скакуна)." },
            { "fp_pet_pegasus", "Пегас" },
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
