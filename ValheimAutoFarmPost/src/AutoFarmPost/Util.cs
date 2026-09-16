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

        public static string Localize(string token)
        {
            try
            {
                if (Localization.instance != null)
                {
                    return Localization.instance.Localize(token);
                }
            }
            catch (Exception)
            {
                // fall through
            }

            return token;
        }
    }
}
