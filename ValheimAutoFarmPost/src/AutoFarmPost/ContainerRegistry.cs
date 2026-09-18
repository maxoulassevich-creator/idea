using System.Collections.Generic;
using UnityEngine;

namespace AutoFarmPost
{
    /// <summary>
    ///     Cheap list of the chests loaded around the player, filled by a Harmony postfix on
    ///     Container.Awake. Falls back to a cached scene scan if that patch cannot be applied.
    /// </summary>
    internal static class ContainerRegistry
    {
        private static readonly List<Container> Items = new List<Container>();

        private static Container[] _fallbackCache;
        private static float _fallbackTime = -999f;

        internal static bool UseFallback;

        public static void Add(Container container)
        {
            if (container != null)
            {
                Items.Add(container);
            }
        }

        public static void CollectNear(Vector3 center, float radius, List<Container> result)
        {
            float sqr = radius * radius;

            if (UseFallback)
            {
                if (_fallbackCache == null || Time.time - _fallbackTime > 5f)
                {
                    _fallbackCache = Object.FindObjectsOfType<Container>();
                    _fallbackTime = Time.time;
                }

                for (int i = 0; i < _fallbackCache.Length; i++)
                {
                    Container c = _fallbackCache[i];
                    if (Suitable(c, center, sqr))
                    {
                        result.Add(c);
                    }
                }

                return;
            }

            for (int i = Items.Count - 1; i >= 0; i--)
            {
                Container c = Items[i];
                if (c == null)
                {
                    Items.RemoveAt(i);
                    continue;
                }

                if (Suitable(c, center, sqr))
                {
                    result.Add(c);
                }
            }
        }

        private static bool Suitable(Container container, Vector3 center, float sqrRadius)
        {
            if (container == null)
            {
                return false;
            }

            // never touch another farm post - that would just shuffle items back and forth
            if (container.GetComponent<FarmPost>() != null)
            {
                return false;
            }

            if ((container.transform.position - center).sqrMagnitude > sqrRadius)
            {
                return false;
            }

            ZNetView nview = container.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
            {
                return false;
            }

            // a chest somebody has open right now is left alone
            return !Util.IsContainerInUse(container);
        }
    }
}
