using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Wizardry
{
    /// <summary>Runs small pieces of work later (the end of a transfiguration, a phoenix's second breath).</summary>
    internal sealed class Later : MonoBehaviour
    {
        private static Later s_instance;
        private readonly List<KeyValuePair<float, Action>> m_items = new List<KeyValuePair<float, Action>>();

        public static void Run(float delay, Action action)
        {
            if (s_instance == null)
            {
                var go = new GameObject("WZ_Later");
                DontDestroyOnLoad(go);
                s_instance = go.AddComponent<Later>();
            }

            s_instance.m_items.Add(new KeyValuePair<float, Action>(Time.time + delay, action));
        }

        private void Update()
        {
            for (int i = m_items.Count - 1; i >= 0; i--)
            {
                if (i >= m_items.Count || m_items[i].Key > Time.time)
                {
                    continue;
                }

                var a = m_items[i].Value;
                m_items.RemoveAt(i);
                try
                {
                    a();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Delayed spell work failed: " + e.Message);
                }
            }
        }
    }

    /// <summary>Plain lit materials in a colour, made from a vanilla opaque material so the game's shader is used.</summary>
    internal static class Mats
    {
        private static readonly Dictionary<string, Material> s_cache = new Dictionary<string, Material>();

        public static Material Solid(Color c, float glow = 0f)
        {
            string key = ColorUtility.ToHtmlStringRGBA(c) + glow.ToString("0.00", CultureInfo.InvariantCulture);
            Material m;
            if (s_cache.TryGetValue(key, out m) && m != null)
            {
                return m;
            }

            m = new Material(Pieces.VanillaMaterial("wood_floor", "piece_workbench"));
            if (m.HasProperty("_MainTex"))
            {
                m.SetTexture("_MainTex", Texture2D.whiteTexture);
            }

            foreach (string prop in m.GetTexturePropertyNames())
            {
                string p = prop.ToLowerInvariant();
                if (prop != "_MainTex" && (p.Contains("bump") || p.Contains("normal") || p.Contains("metal") || p.Contains("detail") ||
                                           p.Contains("occlusion") || p.Contains("emission")))
                {
                    m.SetTexture(prop, null);
                }
            }

            if (m.HasProperty("_Color"))
            {
                m.SetColor("_Color", c);
            }

            if (m.HasProperty("_EmissionColor"))
            {
                m.SetColor("_EmissionColor", c * glow);
                if (glow > 0f)
                {
                    m.EnableKeyword("_EMISSION");
                }
            }

            m.name = "WZ_solid_" + key;
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            s_cache[key] = m;
            return m;
        }
    }

    /// <summary>
    ///     The Codex vault: what Teleportation sends home waits here (in the character's data) until it is taken at any
    ///     Codex. Items keep their prefab, stack, quality and style.
    /// </summary>
    internal static class Vault
    {
        private const string Key = "Wizardry.vault";

        internal struct Entry
        {
            public string Prefab;
            public int Stack, Quality, Variant;
        }

        public static List<Entry> Items(Player p)
        {
            var list = new List<Entry>();
            string text;
            if (p == null || p.m_customData == null || !p.m_customData.TryGetValue(Key, out text) || string.IsNullOrEmpty(text))
            {
                return list;
            }

            foreach (string part in text.Split('|'))
            {
                var f = part.Split(',');
                if (f.Length < 4)
                {
                    continue;
                }

                int stack, quality, variant;
                int.TryParse(f[1], out stack);
                int.TryParse(f[2], out quality);
                int.TryParse(f[3], out variant);
                list.Add(new Entry { Prefab = f[0], Stack = Mathf.Max(1, stack), Quality = Mathf.Max(1, quality), Variant = variant });
            }

            return list;
        }

        private static void Write(Player p, List<Entry> list)
        {
            var parts = new List<string>();
            foreach (var e in list)
            {
                parts.Add(e.Prefab + "," + e.Stack + "," + e.Quality + "," + e.Variant);
            }

            p.m_customData[Key] = string.Join("|", parts.ToArray());
        }

        public static void Put(WizardState w, ItemDrop.ItemData it)
        {
            var p = Player.m_localPlayer;
            string prefab = Wizardry.Items.PrefabOf(it);
            if (p == null || p.m_customData == null || string.IsNullOrEmpty(prefab))
            {
                return;
            }

            var list = Items(p);
            list.Add(new Entry { Prefab = prefab, Stack = it.m_stack, Quality = it.m_quality, Variant = it.m_variant });
            Write(p, list);
        }

        /// <summary>Moves entry <paramref name="index" /> into the inventory; false if it does not fit.</summary>
        public static bool Take(Player p, int index)
        {
            var list = Items(p);
            if (index < 0 || index >= list.Count)
            {
                return false;
            }

            var e = list[index];
            var inv = p.GetInventory();
            var added = inv.AddItem(e.Prefab, e.Stack, e.Quality, e.Variant, 0L, "", false);
            if (added == null)
            {
                Casting.Message(Texts.L("Нет места в сумке.", "No room in your bag."));
                return false;
            }

            list.RemoveAt(index);
            Write(p, list);
            return true;
        }
    }
}
