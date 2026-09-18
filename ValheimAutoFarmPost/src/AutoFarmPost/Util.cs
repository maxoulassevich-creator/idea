using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AutoFarmPost
{
    internal static class Util
    {
        /// <summary>Prefab name without the Unity "(Clone)" suffix.</summary>
        public static string PrefabName(GameObject go)
        {
            if (go == null)
            {
                return string.Empty;
            }

            string name = go.name;
            int idx = name.IndexOf("(Clone)", StringComparison.Ordinal);
            return idx >= 0 ? name.Substring(0, idx) : name;
        }

        private static MethodInfo _containerSave;
        private static bool _containerSaveChecked;

        /// <summary>
        ///     Asks the container to write itself into the network object. Containers normally do
        ///     this on their own when their inventory changes; this is just a safety net.
        /// </summary>
        public static void SaveContainer(Container container)
        {
            if (!_containerSaveChecked)
            {
                _containerSaveChecked = true;
                _containerSave = AccessTools.Method(typeof(Container), "Save", new Type[0]);
            }

            if (_containerSave == null)
            {
                return;
            }

            try
            {
                _containerSave.Invoke(container, null);
            }
            catch (Exception)
            {
                // not fatal - the container saves itself anyway
            }
        }

        private static Type _localizationType;
        private static PropertyInfo _localizationInstanceProp;
        private static FieldInfo _localizationInstanceField;
        private static MethodInfo _localizeMethod;
        private static bool _localizationChecked;

        /// <summary>
        ///     Resolves a "$token" through the game's own localization. Looked up by name so it
        ///     does not matter which game assembly the class lives in; if it cannot be found the
        ///     raw token is shown instead.
        /// </summary>
        private static MethodInfo _containerInUse;
        private static bool _containerInUseChecked;

        /// <summary>True when a player has this container open. Reached by name, so a missing
        /// method simply means "not in use" instead of breaking anything.</summary>
        public static bool IsContainerInUse(Container container)
        {
            if (!_containerInUseChecked)
            {
                _containerInUseChecked = true;
                _containerInUse = AccessTools.Method(typeof(Container), "IsInUse", new Type[0]);
            }

            if (_containerInUse == null)
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

        public static string Localize(string token)
        {
            try
            {
                if (!_localizationChecked)
                {
                    _localizationChecked = true;
                    _localizationType = AccessTools.TypeByName("Localization");

                    if (_localizationType != null)
                    {
                        _localizationInstanceProp = _localizationType.GetProperty("instance",
                            BindingFlags.Public | BindingFlags.Static);
                        _localizationInstanceField = _localizationType.GetField("instance",
                            BindingFlags.Public | BindingFlags.Static);
                        _localizeMethod = AccessTools.Method(_localizationType, "Localize", new[] { typeof(string) });
                    }
                }

                if (_localizeMethod == null)
                {
                    return token;
                }

                object instance = null;
                if (!_localizeMethod.IsStatic)
                {
                    if (_localizationInstanceProp != null)
                    {
                        instance = _localizationInstanceProp.GetValue(null, null);
                    }
                    else if (_localizationInstanceField != null)
                    {
                        instance = _localizationInstanceField.GetValue(null);
                    }

                    if (instance == null)
                    {
                        return token;
                    }
                }

                string result = _localizeMethod.Invoke(instance, new object[] { token }) as string;
                return string.IsNullOrEmpty(result) ? token : result;
            }
            catch (Exception)
            {
                return token;
            }
        }
    }
}
