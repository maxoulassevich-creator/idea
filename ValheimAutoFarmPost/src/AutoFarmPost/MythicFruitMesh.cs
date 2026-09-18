using System.Collections.Generic;
using UnityEngine;

namespace AutoFarmPost
{
    /// <summary>
    ///     The mythic fruit: a small round fruit with a stem and a single leaf, about 12 cm tall.
    ///     Built with the same primitives as the post, so no asset bundle is needed here either.
    /// </summary>
    internal static class MythicFruitMesh
    {
        private const int Sides = 12;

        private static readonly float[] BodyKeys = { 0.00f, 0.10f, 0.28f, 0.50f, 0.72f, 0.88f, 1.00f };
        private static readonly float[] BodyRadius = { 0.004f, 0.028f, 0.046f, 0.052f, 0.046f, 0.030f, 0.006f };
        private const float BodyHeight = 0.115f;

        private static readonly Vector3[] Stem =
        {
            new Vector3(0.000f, 0.112f, 0.000f),
            new Vector3(0.004f, 0.132f, 0.004f),
            new Vector3(0.014f, 0.150f, 0.010f)
        };

        private static readonly Vector3[] Leaf =
        {
            new Vector3(0.012f, 0.146f, 0.008f),
            new Vector3(0.052f, 0.154f, 0.028f),
            new Vector3(0.092f, 0.132f, 0.048f)
        };

        private static readonly float[] LeafKeys = { 0.0f, 0.30f, 0.70f, 1.0f };
        private static readonly float[] LeafRadius = { 0.005f, 0.022f, 0.016f, 0.002f };

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
            List<int[]> rings = new List<int[]>();

            // body
            const int steps = 16;
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                float y = BodyHeight * t;
                float r = MeshMath.Profile(BodyKeys, BodyRadius, t);
                rings.Add(b.Ring(new Vector3(0f, y, 0f), Vector3.up, r, r, r, Sides, 0f, y * 4f));
            }

            b.Tube(rings, true, true);

            // stem
            rings.Clear();
            for (int i = 0; i <= 5; i++)
            {
                float t = i / 5f;
                Vector3 p = MeshMath.Bezier(Stem[0], Stem[1], Stem[2], t);
                Vector3 fwd = MeshMath.Bezier(Stem[0], Stem[1], Stem[2], Mathf.Min(1f, t + 0.02f)) -
                              MeshMath.Bezier(Stem[0], Stem[1], Stem[2], Mathf.Max(0f, t - 0.02f));
                float r = Mathf.Lerp(0.009f, 0.005f, t);
                rings.Add(b.Ring(p, fwd, r, r, r, 6, 0f, t));
            }

            b.Tube(rings, true, true);

            // leaf
            rings.Clear();
            for (int i = 0; i <= 8; i++)
            {
                float t = i / 8f;
                Vector3 p = MeshMath.Bezier(Leaf[0], Leaf[1], Leaf[2], t);
                Vector3 fwd = MeshMath.Bezier(Leaf[0], Leaf[1], Leaf[2], Mathf.Min(1f, t + 0.02f)) -
                              MeshMath.Bezier(Leaf[0], Leaf[1], Leaf[2], Mathf.Max(0f, t - 0.02f));
                float r = MeshMath.Profile(LeafKeys, LeafRadius, t);
                rings.Add(b.Ring(p, fwd, r, r * 0.20f, r * 0.20f, 8, 0f, t));
            }

            b.Tube(rings, true, true);

            Mesh mesh = new Mesh();
            mesh.name = "AutoFarmPost_MythicFruit";
            mesh.vertices = b.Vertices.ToArray();
            mesh.uv = b.Uvs.ToArray();
            mesh.triangles = b.Triangles.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
