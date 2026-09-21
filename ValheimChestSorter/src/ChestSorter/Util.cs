using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ChestSorter
{
    /// <summary>
    ///     Small helpers around the game. Everything that the game keeps private, or that might be
    ///     renamed between versions, is reached by name so the mod degrades instead of breaking.
    /// </summary>
    internal static class Util
    {
        private static FieldInfo _inventoryItems;
        private static MethodInfo _inventoryChanged;
        private static MethodInfo _containerSave;
        private static MethodInfo _containerInUse;
        private static FieldInfo _customData;
        private static bool _reflectionReady;

        private static Type _localizationType;
        private static MethodInfo _selectedLanguage;
        private static bool _languageChecked;
        private static bool _russian;

        private static void EnsureReflection()
        {
            if (_reflectionReady)
            {
                return;
            }

            _reflectionReady = true;
            _inventoryItems = AccessTools.Field(typeof(Inventory), "m_inventory");
            _inventoryChanged = AccessTools.Method(typeof(Inventory), "Changed", new Type[0]);
            _containerSave = AccessTools.Method(typeof(Container), "Save", new Type[0]);
            _containerInUse = AccessTools.Method(typeof(Container), "IsInUse", new Type[0]);
            _customData = AccessTools.Field(typeof(ItemDrop.ItemData), "m_customData");
        }

        /// <summary>The live item list of an inventory. Null means we must not touch this inventory.</summary>
        public static List<ItemDrop.ItemData> GetItems(Inventory inventory)
        {
            EnsureReflection();

            if (inventory == null || _inventoryItems == null)
            {
                return null;
            }

            try
            {
                return _inventoryItems.GetValue(inventory) as List<ItemDrop.ItemData>;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void NotifyChanged(Inventory inventory)
        {
            EnsureReflection();

            if (inventory == null || _inventoryChanged == null)
            {
                return;
            }

            try
            {
                _inventoryChanged.Invoke(inventory, null);
            }
            catch (Exception)
            {
                // the container is saved explicitly as well
            }
        }

        public static void SaveContainer(Container container)
        {
            EnsureReflection();

            if (container == null || _containerSave == null)
            {
                return;
            }

            try
            {
                _containerSave.Invoke(container, null);
            }
            catch (Exception)
            {
                // not fatal
            }
        }

        public static bool IsContainerInUse(Container container)
        {
            EnsureReflection();

            if (container == null || _containerInUse == null)
            {
                return false;
            }

            try
            {
                return (bool)_containerInUse.Invoke(container, null);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        ///     Custom data carries what other mods attach to an item (Epic Loot enchantments,
        ///     backpack contents). Items that have any are never merged with others.
        /// </summary>
        public static bool HasCustomData(ItemDrop.ItemData item)
        {
            EnsureReflection();

            if (item == null || _customData == null)
            {
                return false;
            }

            try
            {
                ICollection data = _customData.GetValue(item) as ICollection;
                return data != null && data.Count > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static string CustomDataText(ItemDrop.ItemData item)
        {
            EnsureReflection();

            if (item == null || _customData == null)
            {
                return string.Empty;
            }

            try
            {
                IDictionary data = _customData.GetValue(item) as IDictionary;
                if (data == null || data.Count == 0)
                {
                    return string.Empty;
                }

                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                foreach (DictionaryEntry entry in data)
                {
                    sb.Append(entry.Key).Append('=').Append(entry.Value).Append(';');
                }

                return sb.ToString();
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>Prefab name of an item, or its localisation token when the prefab is unknown.</summary>
        public static string PrefabName(ItemDrop.ItemData item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            if (item.m_dropPrefab != null)
            {
                return StripClone(item.m_dropPrefab.name);
            }

            return item.m_shared != null ? item.m_shared.m_name : string.Empty;
        }

        public static string StripClone(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            int idx = name.IndexOf("(Clone)", StringComparison.Ordinal);
            return idx >= 0 ? name.Substring(0, idx) : name;
        }

        public static string Localize(string text)
        {
            try
            {
                EnsureLanguage();
                if (_localizationType == null)
                {
                    return text;
                }

                object instance = _localizationType.GetProperty("instance",
                    BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
                if (instance == null)
                {
                    FieldInfo field = _localizationType.GetField("instance", BindingFlags.Public | BindingFlags.Static);
                    instance = field != null ? field.GetValue(null) : null;
                }

                MethodInfo localize = AccessTools.Method(_localizationType, "Localize", new[] { typeof(string) });
                if (localize == null)
                {
                    return text;
                }

                object result = localize.Invoke(localize.IsStatic ? null : instance, new object[] { text });
                return result as string ?? text;
            }
            catch (Exception)
            {
                return text;
            }
        }

        /// <summary>Used to pick the language of the chest labels and the plan report.</summary>
        public static bool IsRussian
        {
            get
            {
                EnsureLanguage();
                return _russian;
            }
        }

        private static void EnsureLanguage()
        {
            if (_languageChecked)
            {
                return;
            }

            try
            {
                _localizationType = AccessTools.TypeByName("Localization");
                if (_localizationType == null)
                {
                    return;
                }

                object instance = _localizationType.GetProperty("instance",
                    BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
                if (instance == null)
                {
                    FieldInfo field = _localizationType.GetField("instance", BindingFlags.Public | BindingFlags.Static);
                    instance = field != null ? field.GetValue(null) : null;
                }

                if (instance == null)
                {
                    return;
                }

                _selectedLanguage = AccessTools.Method(_localizationType, "GetSelectedLanguage", new Type[0]);
                if (_selectedLanguage == null)
                {
                    return;
                }

                string language = _selectedLanguage.Invoke(instance, null) as string;
                _russian = language != null &&
                           language.IndexOf("russian", StringComparison.OrdinalIgnoreCase) >= 0;
                _languageChecked = true;
            }
            catch (Exception)
            {
                _languageChecked = true;
            }
        }

        private static Camera _camera;
        private static float _cameraTime;

        /// <summary>The camera the labels turn towards, looked up again now and then.</summary>
        public static Camera MainCamera
        {
            get
            {
                if (_camera == null || Time.time - _cameraTime > 5f)
                {
                    _camera = Camera.main;
                    _cameraTime = Time.time;
                }

                return _camera;
            }
        }

        private static MethodInfo _chatHasFocus;
        private static Type _chatType;
        private static bool _chatChecked;

        /// <summary>True while the player is typing, so hotkeys are not stolen from the chat.</summary>
        public static bool IsTyping()
        {
            try
            {
                if (!_chatChecked)
                {
                    _chatChecked = true;
                    _chatType = AccessTools.TypeByName("Chat");
                    if (_chatType != null)
                    {
                        _chatHasFocus = AccessTools.Method(_chatType, "HasFocus", new Type[0]);
                    }
                }

                if (_chatType == null || _chatHasFocus == null)
                {
                    return false;
                }

                object instance = _chatType.GetField("instance", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null);
                if (instance == null)
                {
                    return false;
                }

                return (bool)_chatHasFocus.Invoke(instance, null);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>"12 m to the NE" - so the player can tell which chest a plan line is about.</summary>
        public static string Describe(Vector3 from, Vector3 target)
        {
            Vector3 delta = target - from;
            float distance = new Vector2(delta.x, delta.z).magnitude;
            string[] compassRu = { "С", "СВ", "В", "ЮВ", "Ю", "ЮЗ", "З", "СЗ" };
            string[] compassEn = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

            float angle = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            if (angle < 0f)
            {
                angle += 360f;
            }

            int sector = Mathf.RoundToInt(angle / 45f) % 8;
            string compass = IsRussian ? compassRu[sector] : compassEn[sector];

            return IsRussian
                ? string.Format("{0:0} м на {1}", distance, compass)
                : string.Format("{0:0} m {1}", distance, compass);
        }
    }
}
