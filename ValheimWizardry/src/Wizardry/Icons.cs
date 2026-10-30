using System.Collections.Generic;
using UnityEngine;

namespace Wizardry
{
    /// <summary>Item and status icons drawn in code: a coloured disc with a simple sign.</summary>
    internal static class Icons
    {
        private static readonly Dictionary<string, Sprite> s_cache = new Dictionary<string, Sprite>();

        public enum Shape { Disc, Book, Scroll, Card, Orb, Wand, Core, Cup, Potion, Stone, Cloak }

        public static Sprite Status(string key, Color c)
        {
            return Make("se_" + key, Shape.Disc, c);
        }

        public static Sprite Make(string key, Shape shape, Color c)
        {
            Sprite s;
            if (s_cache.TryGetValue(key, out s) && s != null)
            {
                return s;
            }

            // a rendered icon from tools/make_models.py (assets/icon_<key>.png) if there is one
            var rendered = Meshes.Texture("icon_" + key + ".png");
            if (rendered != null)
            {
                s = Sprite.Create(rendered, new Rect(0, 0, rendered.width, rendered.height), new Vector2(0.5f, 0.5f), 100f);
                s.name = "WZ_icon_" + key;
                s_cache[key] = s;
                return s;
            }

            if (shape == Shape.Disc || key == "skill")
            {
                s = Glyph(key, key == "skill" ? "wand" : StatusGlyph(key), c);
                if (s != null)
                {
                    s_cache[key] = s;
                    return s;
                }
            }

            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "WZ_icon_" + key };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    px[y * n + x] = Paint(shape, u, v, c);
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false, false);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            s.name = tex.name;
            s_cache[key] = s;
            return s;
        }

        private static string StatusGlyph(string key)
        {
            string k = key.ToLowerInvariant();
            if (k.Contains("heal") || k.Contains("regen") || k.Contains("elixir")) return "heal";
            if (k.Contains("shield") || k.Contains("protego") || k.Contains("ward") || k.Contains("armor") || k.Contains("cloak")) return "buff";
            if (k.Contains("burn") || k.Contains("fire") || k.Contains("pepper")) return "aoe";
            if (k.Contains("dark") || k.Contains("curse") || k.Contains("mark") || k.Contains("vuln") || k.Contains("death")) return "dark";
            if (k.Contains("speed") || k.Contains("haste") || k.Contains("fly") || k.Contains("slow")) return "move";
            if (k.Contains("stun") || k.Contains("root") || k.Contains("fear") || k.Contains("charm") || k.Contains("confus") || k.Contains("blind") ||
                k.Contains("silence") || k.Contains("disarm") || k.Contains("lift") || k.Contains("poly") || k.Contains("petrif")) return "ctrl";
            if (k.Contains("buff") || k.Contains("potion") || k.Contains("felix") || k.Contains("strength")) return "point";
            return "wand";
        }

        /// <summary>A round icon: a disc in the colour, the glyph in light, a bronze rim (the art of the spell icons).</summary>
        private static Sprite Glyph(string key, string glyph, Color c)
        {
            var disc = Meshes.Texture("ui_disc.png");
            var ring = Meshes.Texture("ui_ring.png");
            var sign = Meshes.Texture("glyph_" + glyph + ".png");
            if (disc == null || ring == null || sign == null || !disc.isReadable || !ring.isReadable || !sign.isReadable)
            {
                return null;
            }

            const int n = 128;
            var d = disc.GetPixels32();
            var r = ring.GetPixels32();
            var g = sign.GetPixels32();
            if (d.Length != n * n || r.Length != n * n || g.Length != n * n)
            {
                return null;
            }

            var px = new Color32[n * n];
            Color dark = new Color(c.r * 0.4f, c.g * 0.4f, c.b * 0.4f), light = Color.Lerp(c, Color.white, 0.6f), rim = new Color(0.86f, 0.66f, 0.36f);
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    // the glyph sits a little smaller in the middle of the disc
                    int gx = Mathf.Clamp((int)((x - n * 0.5f) / 0.68f + n * 0.5f), 0, n - 1), gy = Mathf.Clamp((int)((y - n * 0.5f) / 0.68f + n * 0.5f), 0, n - 1);
                    float da = d[i].a / 255f, ga = g[gy * n + gx].a / 255f, ra = r[i].a / 255f;
                    Color col = dark * (d[i].r / 255f);
                    col = Color.Lerp(col, light, ga);
                    col = Color.Lerp(col, rim * (r[i].r / 255f), ra);
                    col.a = Mathf.Max(da, ra);
                    px[i] = col;
                }
            }

            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "WZ_icon_" + key };
            tex.SetPixels32(px);
            tex.Apply(false, false);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            var s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            s.name = tex.name;
            return s;
        }

        private static Color32 Paint(Shape shape, float u, float v, Color c)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            Color dark = new Color(c.r * 0.35f, c.g * 0.35f, c.b * 0.35f, 1f);
            Color light = Color.Lerp(c, Color.white, 0.45f);
            bool inside;
            Color col;
            switch (shape)
            {
                case Shape.Book:
                    inside = Mathf.Abs(u) < 0.62f && Mathf.Abs(v) < 0.78f;
                    col = Mathf.Abs(u + 0.5f) < 0.08f ? dark : (Mathf.Abs(v) < 0.08f && u > -0.3f && u < 0.4f ? light : c);
                    break;
                case Shape.Scroll:
                    inside = Mathf.Abs(u) < 0.7f && Mathf.Abs(v) < 0.55f || (Mathf.Abs(Mathf.Abs(v) - 0.6f) < 0.12f && Mathf.Abs(u) < 0.78f);
                    col = Mathf.Abs(Mathf.Abs(v) - 0.6f) < 0.12f ? dark : (Mathf.Abs(v - 0.2f) < 0.05f || Mathf.Abs(v + 0.15f) < 0.05f ? dark : light);
                    break;
                case Shape.Card:
                    inside = Mathf.Abs(u) < 0.58f && Mathf.Abs(v) < 0.82f;
                    col = Mathf.Abs(u) > 0.48f || Mathf.Abs(v) > 0.72f ? new Color(0.85f, 0.7f, 0.25f) : (Mathf.Sqrt(u * u + (v - 0.15f) * (v - 0.15f)) < 0.3f ? light : c);
                    break;
                case Shape.Orb:
                    inside = r < 0.8f;
                    col = Color.Lerp(light, c, Mathf.Clamp01(Mathf.Sqrt((u + 0.3f) * (u + 0.3f) + (v - 0.3f) * (v - 0.3f))));
                    break;
                case Shape.Wand:
                    inside = Mathf.Abs(u - v) < 0.14f && Mathf.Abs(u + v) < 1.6f;
                    col = u + v < -0.6f ? dark : c;
                    if (Mathf.Sqrt((u - 0.62f) * (u - 0.62f) + (v - 0.62f) * (v - 0.62f)) < 0.16f)
                    {
                        inside = true;
                        col = Color.white;
                    }

                    break;
                case Shape.Core:
                    inside = Mathf.Abs(u) + Mathf.Abs(v) < 0.8f;
                    col = Color.Lerp(light, c, Mathf.Abs(u) + Mathf.Abs(v));
                    break;
                case Shape.Cup:
                    inside = (v > -0.1f && v < 0.7f && Mathf.Abs(u) < 0.55f - (0.7f - v) * 0.2f) || (Mathf.Abs(u) < 0.1f && v < -0.1f && v > -0.6f) ||
                             (Mathf.Abs(u) < 0.4f && Mathf.Abs(v + 0.68f) < 0.08f);
                    col = v > 0.45f ? light : c;
                    break;
                case Shape.Potion:
                    inside = (Mathf.Sqrt(u * u + (v + 0.2f) * (v + 0.2f)) < 0.55f) || (Mathf.Abs(u) < 0.14f && v > 0.2f && v < 0.8f);
                    col = v > 0.25f ? new Color(0.8f, 0.8f, 0.85f) : c;
                    break;
                case Shape.Stone:
                    inside = r < 0.7f;
                    col = (Mathf.Abs(u) < 0.04f && Mathf.Abs(v) < 0.45f) || Mathf.Abs(r - 0.3f) < 0.04f ? light : dark;
                    break;
                case Shape.Cloak:
                    inside = v < 0.7f && Mathf.Abs(u) < 0.25f + (0.7f - v) * 0.3f;
                    col = Color.Lerp(c, new Color(0.6f, 0.65f, 0.75f, 1f), (v + 1f) * 0.3f);
                    break;
                default:
                    inside = r < 0.85f;
                    col = r > 0.72f ? dark : Color.Lerp(light, c, r);
                    break;
            }

            if (!inside)
            {
                return new Color32(0, 0, 0, 0);
            }

            col.a = 1f;
            return col;
        }
    }
}
