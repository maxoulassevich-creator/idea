using System;
using System.Collections.Generic;
using UnityEngine;

namespace AutoFarmPost
{
    /// <summary>
    ///     Builds the model of the farm post at runtime: a round carved pillar whose top grows
    ///     into a raven head. No asset bundle is needed, so the mod does not depend on a Unity
    ///     version - the mesh is plain geometry and reuses the game's own wood material.
    ///
    ///     Layout (metres, +Z is the direction the raven looks):
    ///       0.00 - 1.56  pillar with a flared foot and two carved bands
    ///       1.38 - 1.79  neck with a carved collar
    ///       1.68 - 1.89  skull, brow, eyes
    ///       1.68 - 1.86  beak, curving down to a hooked tip at z = 0.35
    /// </summary>
    internal static class RavenPostMesh
    {
        private const int SidesPost = 12;
        private const int SidesHead = 12;
        private const float PostTop = 1.56f;
        private const float UvScale = 1.0f;          // texture metres per world metre

        private static readonly float[] Bands = { 0.50f, 1.00f };

        // neck: straight out of the capital, its top hidden inside the skull
        private static readonly Vector3[] NeckSpine =
        {
            new Vector3(0f, 1.380f, 0.000f),
            new Vector3(0f, 1.600f, 0.000f),
            new Vector3(0f, 1.790f, 0.004f)
        };

        private static readonly float[] NeckKeys = { 0.00f, 0.28f, 0.52f, 0.68f, 0.78f, 0.90f, 1.00f };
        private static readonly float[] NeckRadius = { 0.122f, 0.092f, 0.086f, 0.112f, 0.104f, 0.092f, 0.086f };

        // head: one continuous sweep, nape -> cranium -> brow -> beak -> hooked tip.
        // Both ends taper to nearly nothing so no flat cap is ever visible.
        private static readonly Vector3[] HeadSpine =
        {
            new Vector3(0f, 1.779f, -0.128f),
            new Vector3(0f, 1.791f, -0.100f),
            new Vector3(0f, 1.805f, -0.060f),
            new Vector3(0f, 1.813f,  0.000f),
            new Vector3(0f, 1.813f,  0.060f),
            new Vector3(0f, 1.805f,  0.115f),
            new Vector3(0f, 1.791f,  0.165f),
            new Vector3(0f, 1.771f,  0.218f),
            new Vector3(0f, 1.747f,  0.272f),
            new Vector3(0f, 1.717f,  0.318f),
            new Vector3(0f, 1.681f,  0.352f)
        };

        private static readonly float[] HeadRadius = { 0.018f, 0.062f, 0.098f, 0.113f, 0.114f, 0.101f, 0.066f, 0.042f, 0.026f, 0.013f, 0.003f };
        private static readonly float[] HeadTop    = { 1.00f, 1.00f, 1.00f, 1.00f, 1.02f, 1.10f, 1.40f, 1.58f, 1.55f, 1.45f, 1.30f };
        private static readonly float[] HeadBottom = { 1.00f, 0.98f, 0.96f, 0.95f, 0.95f, 1.00f, 1.18f, 1.28f, 1.22f, 1.12f, 1.02f };
        private static readonly float[] HeadRidge  = { 0.00f, 0.00f, 0.02f, 0.05f, 0.08f, 0.16f, 0.26f, 0.32f, 0.32f, 0.28f, 0.24f };

        private static readonly Vector3 Eye = new Vector3(0.100f, 1.821f, 0.055f);

        private static readonly Vector3[] Brow =
        {
            new Vector3(0.055f, 1.890f, -0.020f),
            new Vector3(0.098f, 1.871f,  0.045f),
            new Vector3(0.092f, 1.823f,  0.118f)
        };

        private static Mesh _cached;

        public static Mesh Get()
        {
            if (_cached == null)
            {
                _cached = Build();
            }

            return _cached;
        }

        private static Mesh Build()
        {
            Builder b = new Builder();

            // ------------------------------------------------------------ pillar --
            const int postSteps = 54;
            List<int[]> rings = new List<int[]>();
            float v = 0f;
            for (int i = 0; i <= postSteps; i++)
            {
                float y = PostTop * i / postSteps;
                float r = PostRadius(y);
                rings.Add(b.Ring(new Vector3(0f, y, 0f), Vector3.up, r, r, r, SidesPost, 0f, y * UvScale));
            }
            b.Tube(rings, true, false);

            // -------------------------------------------------------------- neck --
            rings.Clear();
            v = 0f;
            Vector3 prev = Bezier(NeckSpine[0], NeckSpine[1], NeckSpine[2], 0f);
            for (int i = 0; i <= 8; i++)
            {
                float t = i / 8f;
                Vector3 p = Bezier(NeckSpine[0], NeckSpine[1], NeckSpine[2], t);
                Vector3 fwd = Bezier(NeckSpine[0], NeckSpine[1], NeckSpine[2], Mathf.Min(1f, t + 0.02f)) -
                              Bezier(NeckSpine[0], NeckSpine[1], NeckSpine[2], Mathf.Max(0f, t - 0.02f));
                v += (p - prev).magnitude;
                prev = p;
                float r = Profile(NeckKeys, NeckRadius, t);
                rings.Add(b.Ring(p, fwd, r, r, r, SidesHead, 0f, v * UvScale));
            }
            b.Tube(rings, true, false);

            // -------------------------------------------------------------- head --
            rings.Clear();
            const int headSteps = 30;
            v = 0f;
            prev = Catmull(HeadSpine, 0f);
            for (int i = 0; i <= headSteps; i++)
            {
                float t = (float)i / headSteps;
                Vector3 p = Catmull(HeadSpine, t);
                Vector3 fwd = Catmull(HeadSpine, Mathf.Min(1f, t + 0.01f)) - Catmull(HeadSpine, Mathf.Max(0f, t - 0.01f));
                v += (p - prev).magnitude;
                prev = p;
                float r = Lerp(HeadRadius, t);
                rings.Add(b.Ring(p, fwd, r, r * Lerp(HeadTop, t), r * Lerp(HeadBottom, t),
                    SidesHead, Lerp(HeadRidge, t), v * UvScale));
            }
            b.Tube(rings, true, true);

            // ------------------------------------------------------ eyes and brows -
            for (int s = 0; s < 2; s++)
            {
                float sx = s == 0 ? 1f : -1f;

                b.Sphere(new Vector3(Eye.x * sx, Eye.y, Eye.z), 0.024f, 0.030f, 0.030f, 10, 6);

                // carved ring around the eye
                rings.Clear();
                Vector3 c = new Vector3(Eye.x * sx * 0.92f, Eye.y, Eye.z);
                for (int i = 0; i <= 16; i++)
                {
                    float a = 2f * Mathf.PI * i / 16f;
                    Vector3 p = new Vector3(c.x, c.y + 0.040f * Mathf.Cos(a), c.z + 0.040f * Mathf.Sin(a));
                    Vector3 tang = new Vector3(0f, -Mathf.Sin(a), Mathf.Cos(a));
                    rings.Add(b.Ring(p, tang, 0.011f, 0.007f, 0.007f, 6, 0f, a * 0.04f * UvScale));
                }
                b.Tube(rings, false, false);

                // brow ridge
                rings.Clear();
                Vector3 b0 = new Vector3(Brow[0].x * sx, Brow[0].y, Brow[0].z);
                Vector3 b1 = new Vector3(Brow[1].x * sx, Brow[1].y, Brow[1].z);
                Vector3 b2 = new Vector3(Brow[2].x * sx, Brow[2].y, Brow[2].z);
                v = 0f;
                prev = b0;
                for (int i = 0; i <= 6; i++)
                {
                    float t = i / 6f;
                    Vector3 p = Bezier(b0, b1, b2, t);
                    Vector3 fwd = Bezier(b0, b1, b2, Mathf.Min(1f, t + 0.02f)) - Bezier(b0, b1, b2, Mathf.Max(0f, t - 0.02f));
                    v += (p - prev).magnitude;
                    prev = p;
                    float r = t < 0.5f
                        ? Mathf.Lerp(0.024f, 0.019f, Smooth(t / 0.5f))
                        : Mathf.Lerp(0.019f, 0.006f, Smooth((t - 0.5f) / 0.5f));
                    rings.Add(b.Ring(p, fwd, r, r * 0.5f, r * 0.5f, 8, 0f, v * UvScale));
                }
                b.Tube(rings, true, true);
            }

            Mesh mesh = new Mesh();
            mesh.name = "AutoFarmPost_RavenPost";
            mesh.vertices = b.Vertices.ToArray();
            mesh.uv = b.Uvs.ToArray();
            mesh.triangles = b.Triangles.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static float PostRadius(float y)
        {
            float r = 0.125f - 0.020f * (y / PostTop);

            if (y < 0.20f)
            {
                r = Mathf.Max(r, 0.190f - 0.062f * Smooth(y / 0.20f));
            }

            for (int i = 0; i < Bands.Length; i++)
            {
                float d = Mathf.Abs(y - Bands[i]);
                if (d < 0.075f)
                {
                    float k = d / 0.075f;
                    r += 0.030f * (1f - k * k);
                }
            }

            if (y > PostTop - 0.17f)
            {
                float t = Mathf.Min(1f, (y - (PostTop - 0.17f)) / 0.17f);
                r += 0.060f * Mathf.Sin(t * Mathf.PI);
            }

            return r;
        }

        // ------------------------------------------------------------- helpers ---

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
        {
            float u = 1f - t;
            return u * u * p0 + 2f * u * t * p1 + t * t * p2;
        }

        /// <summary>Uniform Catmull-Rom through the points, clamped at both ends.</summary>
        private static Vector3 Catmull(Vector3[] points, float t)
        {
            int n = points.Length;
            if (t <= 0f) return points[0];
            if (t >= 1f) return points[n - 1];

            float f = t * (n - 1);
            int i = (int)f;
            float u = f - i;

            Vector3 p0 = points[Mathf.Max(0, i - 1)];
            Vector3 p1 = points[i];
            Vector3 p2 = points[Mathf.Min(n - 1, i + 1)];
            Vector3 p3 = points[Mathf.Min(n - 1, i + 2)];

            float u2 = u * u;
            float u3 = u2 * u;
            return 0.5f * ((2f * p1) + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 +
                           (-p0 + 3f * p1 - 3f * p2 + p3) * u3);
        }

        /// <summary>Linear interpolation over values spread evenly across [0, 1].</summary>
        private static float Lerp(float[] values, float t)
        {
            int n = values.Length;
            if (t <= 0f) return values[0];
            if (t >= 1f) return values[n - 1];

            float f = t * (n - 1);
            int i = (int)f;
            float u = f - i;
            return Mathf.Lerp(values[i], values[Mathf.Min(n - 1, i + 1)], u);
        }

        private static float Profile(float[] keys, float[] values, float t)
        {
            if (t <= keys[0]) return values[0];
            if (t >= keys[keys.Length - 1]) return values[values.Length - 1];

            for (int i = 1; i < keys.Length; i++)
            {
                if (t <= keys[i])
                {
                    float k = Smooth((t - keys[i - 1]) / (keys[i] - keys[i - 1]));
                    return Mathf.Lerp(values[i - 1], values[i], k);
                }
            }

            return values[values.Length - 1];
        }

        /// <summary>Vertices, uvs and triangles plus the primitives that fill them.</summary>
        private class Builder
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Triangles = new List<int>();

            private int Add(Vector3 p, Vector2 uv)
            {
                Vertices.Add(p);
                Uvs.Add(uv);
                return Vertices.Count - 1;
            }

            /// <summary>
            ///     One cross-section. topScale/bottomScale shape the upper and lower half
            ///     separately, ridge narrows the section towards the top so it reads as a crest.
            /// </summary>
            public int[] Ring(Vector3 center, Vector3 forward, float rx, float ryTop, float ryBottom,
                int sides, float ridge, float v)
            {
                Vector3 f = forward.normalized;
                Vector3 hint = Mathf.Abs(Vector3.Dot(f, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
                Vector3 right = Vector3.Cross(hint, f).normalized;
                Vector3 up = Vector3.Cross(f, right);

                int[] idx = new int[sides];
                for (int i = 0; i < sides; i++)
                {
                    float a = 2f * Mathf.PI * i / sides;
                    float ca = Mathf.Cos(a);
                    float sa = Mathf.Sin(a);
                    float k = ridge > 0f && sa > 0f ? 1f - ridge * Mathf.Pow(sa, 1.6f) : 1f;
                    float vy = sa * (sa >= 0f ? ryTop : ryBottom);
                    Vector3 p = center + right * (ca * rx * k) + up * vy;
                    idx[i] = Add(p, new Vector2(a * rx, v));
                }

                return idx;
            }

            public void Tube(List<int[]> rings, bool capStart, bool capEnd)
            {
                if (rings.Count < 2)
                {
                    return;
                }

                Vector3 c0 = Center(rings[0]);
                Vector3 c1 = Center(rings[1]);
                Vector3 a0 = Vertices[rings[0][0]];
                Vector3 nb0 = Vertices[rings[1][0]];
                Vector3 nb1 = Vertices[rings[1][1 % rings[1].Length]];

                // the winding is derived from the geometry, so faces always end up outwards
                bool flip = Vector3.Dot(Vector3.Cross(nb0 - a0, nb1 - a0), a0 - c0) < 0f;

                for (int i = 0; i < rings.Count - 1; i++)
                {
                    int[] a = rings[i];
                    int[] b = rings[i + 1];
                    for (int j = 0; j < a.Length; j++)
                    {
                        int k = (j + 1) % a.Length;
                        if (flip)
                        {
                            Quad(a[j], a[k], b[k], b[j]);
                        }
                        else
                        {
                            Quad(a[j], b[j], b[k], a[k]);
                        }
                    }
                }

                if (capStart)
                {
                    Cap(rings[0], c0 - c1);
                }

                if (capEnd)
                {
                    Vector3 cn = Center(rings[rings.Count - 1]);
                    Vector3 cp = Center(rings[rings.Count - 2]);
                    Cap(rings[rings.Count - 1], cn - cp);
                }
            }

            private void Cap(int[] ring, Vector3 outward)
            {
                Vector3 center = Center(ring);
                int c = Add(center, new Vector2(0f, 0f));

                Vector3 n = Vector3.Cross(Vertices[ring[0]] - center, Vertices[ring[1]] - center);
                bool flip = Vector3.Dot(n, outward) < 0f;

                for (int i = 0; i < ring.Length; i++)
                {
                    int x = ring[i];
                    int y = ring[(i + 1) % ring.Length];
                    if (flip)
                    {
                        Tri(c, y, x);
                    }
                    else
                    {
                        Tri(c, x, y);
                    }
                }
            }

            public void Sphere(Vector3 center, float rx, float ry, float rz, int segments, int rings)
            {
                int top = Add(new Vector3(center.x, center.y + ry, center.z), new Vector2(0.5f, 1f));
                int bottom = Add(new Vector3(center.x, center.y - ry, center.z), new Vector2(0.5f, 0f));

                int[][] grid = new int[rings - 1][];
                for (int r = 1; r < rings; r++)
                {
                    float phi = Mathf.PI * r / rings;
                    int[] row = new int[segments];
                    for (int s = 0; s < segments; s++)
                    {
                        float th = 2f * Mathf.PI * s / segments;
                        Vector3 p = new Vector3(
                            center.x + rx * Mathf.Sin(phi) * Mathf.Cos(th),
                            center.y + ry * Mathf.Cos(phi),
                            center.z + rz * Mathf.Sin(phi) * Mathf.Sin(th));
                        row[s] = Add(p, new Vector2((float)s / segments, 1f - (float)r / rings));
                    }

                    grid[r - 1] = row;
                }

                Vector3 probe = Vector3.Cross(Vertices[grid[1][0]] - Vertices[grid[0][0]],
                    Vertices[grid[1][1]] - Vertices[grid[0][0]]);
                bool flip = Vector3.Dot(probe, Vertices[grid[0][0]] - center) < 0f;

                for (int s = 0; s < segments; s++)
                {
                    int k = (s + 1) % segments;
                    if (flip)
                    {
                        Tri(top, grid[0][k], grid[0][s]);
                        Tri(bottom, grid[grid.Length - 1][s], grid[grid.Length - 1][k]);
                    }
                    else
                    {
                        Tri(top, grid[0][s], grid[0][k]);
                        Tri(bottom, grid[grid.Length - 1][k], grid[grid.Length - 1][s]);
                    }
                }

                for (int r = 0; r < grid.Length - 1; r++)
                {
                    for (int s = 0; s < segments; s++)
                    {
                        int k = (s + 1) % segments;
                        if (flip)
                        {
                            Quad(grid[r][s], grid[r][k], grid[r + 1][k], grid[r + 1][s]);
                        }
                        else
                        {
                            Quad(grid[r][s], grid[r + 1][s], grid[r + 1][k], grid[r][k]);
                        }
                    }
                }
            }

            private Vector3 Center(int[] ring)
            {
                Vector3 sum = Vector3.zero;
                for (int i = 0; i < ring.Length; i++)
                {
                    sum += Vertices[ring[i]];
                }

                return sum / ring.Length;
            }

            private void Quad(int a, int b, int c, int d)
            {
                Tri(a, b, c);
                Tri(a, c, d);
            }

            private void Tri(int a, int b, int c)
            {
                Triangles.Add(a);
                Triangles.Add(b);
                Triangles.Add(c);
            }
        }
    }
}
