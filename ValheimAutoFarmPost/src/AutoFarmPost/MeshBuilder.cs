using System.Collections.Generic;
using UnityEngine;

namespace AutoFarmPost
{
    /// <summary>
///     Vertices, uvs and triangles plus the primitives that fill them: rings swept into
///     tubes, caps and spheres. Used for every model the mod builds at runtime.
/// </summary>
internal class MeshBuilder
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

    /// <summary>Small curve helpers shared by the runtime models.</summary>
    internal static class MeshMath
    {
        public static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        public static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
        {
            float u = 1f - t;
            return u * u * p0 + 2f * u * t * p1 + t * t * p2;
        }

        /// <summary>Uniform Catmull-Rom through the points, clamped at both ends.</summary>
        public static Vector3 Catmull(Vector3[] points, float t)
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
        public static float Lerp(float[] values, float t)
        {
            int n = values.Length;
            if (t <= 0f) return values[0];
            if (t >= 1f) return values[n - 1];

            float f = t * (n - 1);
            int i = (int)f;
            float u = f - i;
            return Mathf.Lerp(values[i], values[Mathf.Min(n - 1, i + 1)], u);
        }

        public static float Profile(float[] keys, float[] values, float t)
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
    }
}
