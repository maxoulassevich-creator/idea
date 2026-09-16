using System.Collections.Generic;
using UnityEngine;

namespace AutoFarmPost
{
    /// <summary>
    ///     Keeps a cheap list of every loaded <see cref="Pickable" /> so a post does not have to
    ///     scan the whole scene. Filled by a Harmony postfix on Pickable.Awake; if that patch
    ///     cannot be applied we fall back to a cached FindObjectsOfType scan.
    /// </summary>
    internal static class PickableRegistry
    {
        private static readonly List<Pickable> Items = new List<Pickable>();

        private static Pickable[] _fallbackCache;
        private static float _fallbackTime = -999f;

        internal static bool UseFallback;

        public static void Add(Pickable pickable)
        {
            if (pickable != null)
            {
                Items.Add(pickable);
            }
        }

        public static void CollectNear(Vector3 center, float radius, List<Pickable> result)
        {
            float sqr = radius * radius;

            if (UseFallback)
            {
                if (_fallbackCache == null || Time.time - _fallbackTime > 5f)
                {
                    _fallbackCache = Object.FindObjectsOfType<Pickable>();
                    _fallbackTime = Time.time;
                }

                for (int i = 0; i < _fallbackCache.Length; i++)
                {
                    Pickable p = _fallbackCache[i];
                    if (p != null && (p.transform.position - center).sqrMagnitude <= sqr)
                    {
                        result.Add(p);
                    }
                }

                return;
            }

            // Iterating backwards lets us drop destroyed entries on the way.
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                Pickable p = Items[i];
                if (p == null)
                {
                    Items.RemoveAt(i);
                    continue;
                }

                if ((p.transform.position - center).sqrMagnitude <= sqr)
                {
                    result.Add(p);
                }
            }
        }
    }
}
