using System;
using UnityEngine;

namespace ChestSorter
{
    /// <summary>
    ///     The label of a chest lives in the chest itself (its network object), so it survives
    ///     reloads and is the same for every player on the server.
    /// </summary>
    internal static class LabelStore
    {
        private static readonly int Key = "chestsorter_label".GetStableHashCode();

        public static string Get(Container container)
        {
            try
            {
                if (container == null)
                {
                    return string.Empty;
                }

                ZNetView nview = container.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid())
                {
                    return string.Empty;
                }

                ZDO zdo = nview.GetZDO();
                return zdo != null ? zdo.GetString(Key, string.Empty) : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        public static void Set(Container container, string label)
        {
            try
            {
                if (container == null)
                {
                    return;
                }

                ZNetView nview = container.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid())
                {
                    return;
                }

                if (!nview.IsOwner())
                {
                    nview.ClaimOwnership();
                }

                ZDO zdo = nview.GetZDO();
                if (zdo != null)
                {
                    zdo.Set(Key, label ?? string.Empty);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not write a chest label: " + e.Message);
            }
        }
    }
}
