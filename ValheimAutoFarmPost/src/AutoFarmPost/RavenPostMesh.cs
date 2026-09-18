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
            MeshBuilder b = new MeshBuilder();

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
            Vector3 prev = MeshMath.Bezier(NeckSpine[0], NeckSpine[1], NeckSpine[2], 0f);
            for (int i = 0; i <= 8; i++)
            {
                float t = i / 8f;
                Vector3 p = MeshMath.Bezier(NeckSpine[0], NeckSpine[1], NeckSpine[2], t);
                Vector3 fwd = MeshMath.Bezier(NeckSpine[0], NeckSpine[1], NeckSpine[2], Mathf.Min(1f, t + 0.02f)) -
                              MeshMath.Bezier(NeckSpine[0], NeckSpine[1], NeckSpine[2], Mathf.Max(0f, t - 0.02f));
                v += (p - prev).magnitude;
                prev = p;
                float r = MeshMath.Profile(NeckKeys, NeckRadius, t);
                rings.Add(b.Ring(p, fwd, r, r, r, SidesHead, 0f, v * UvScale));
            }
            b.Tube(rings, true, false);

            // -------------------------------------------------------------- head --
            rings.Clear();
            const int headSteps = 30;
            v = 0f;
            prev = MeshMath.Catmull(HeadSpine, 0f);
            for (int i = 0; i <= headSteps; i++)
            {
                float t = (float)i / headSteps;
                Vector3 p = MeshMath.Catmull(HeadSpine, t);
                Vector3 fwd = MeshMath.Catmull(HeadSpine, Mathf.Min(1f, t + 0.01f)) - MeshMath.Catmull(HeadSpine, Mathf.Max(0f, t - 0.01f));
                v += (p - prev).magnitude;
                prev = p;
                float r = MeshMath.Lerp(HeadRadius, t);
                rings.Add(b.Ring(p, fwd, r, r * MeshMath.Lerp(HeadTop, t), r * MeshMath.Lerp(HeadBottom, t),
                    SidesHead, MeshMath.Lerp(HeadRidge, t), v * UvScale));
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
                    Vector3 p = MeshMath.Bezier(b0, b1, b2, t);
                    Vector3 fwd = MeshMath.Bezier(b0, b1, b2, Mathf.Min(1f, t + 0.02f)) - MeshMath.Bezier(b0, b1, b2, Mathf.Max(0f, t - 0.02f));
                    v += (p - prev).magnitude;
                    prev = p;
                    float r = t < 0.5f
                        ? Mathf.Lerp(0.024f, 0.019f, MeshMath.Smooth(t / 0.5f))
                        : Mathf.Lerp(0.019f, 0.006f, MeshMath.Smooth((t - 0.5f) / 0.5f));
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
                r = Mathf.Max(r, 0.190f - 0.062f * MeshMath.Smooth(y / 0.20f));
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
    }
}
