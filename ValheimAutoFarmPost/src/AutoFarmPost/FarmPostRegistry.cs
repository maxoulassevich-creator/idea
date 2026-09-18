using System.Collections.Generic;
using UnityEngine;

namespace AutoFarmPost
{
    /// <summary>
    ///     Every farm post currently loaded. Used by the scarecrow patch, which has to answer
    ///     "is this plant inside somebody's field?" from a damage callback.
    /// </summary>
    internal static class FarmPostRegistry
    {
        private static readonly List<FarmPost> Posts = new List<FarmPost>();

        public static void Add(FarmPost post)
        {
            if (post != null && !Posts.Contains(post))
            {
                Posts.Add(post);
            }
        }

        public static void Remove(FarmPost post)
        {
            Posts.Remove(post);
        }

        public static bool IsProtected(Vector3 position)
        {
            if (!ModConfig.Scarecrow.Value)
            {
                return false;
            }

            float radius = ModConfig.ScarecrowRadius.Value;
            float sqr = radius * radius;

            for (int i = Posts.Count - 1; i >= 0; i--)
            {
                FarmPost post = Posts[i];
                if (post == null)
                {
                    Posts.RemoveAt(i);
                    continue;
                }

                if (!post.IsLive)
                {
                    continue;
                }

                if ((post.transform.position - position).sqrMagnitude <= sqr)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
