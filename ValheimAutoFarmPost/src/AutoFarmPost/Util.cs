using System;
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
