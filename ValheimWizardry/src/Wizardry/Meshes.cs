using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Wizardry
{
    /// <summary>The wand and the Codex: meshes (WZM1 files made by tools/export_models.py) and textures inside the DLL.</summary>
    internal static class Meshes
    {
        private static readonly Dictionary<string, Mesh> s_meshes = new Dictionary<string, Mesh>();
        private static readonly Dictionary<string, Texture2D> s_textures = new Dictionary<string, Texture2D>();

        public static byte[] Resource(string file)
        {
            var asm = typeof(Meshes).Assembly;
            using (var s = asm.GetManifestResourceStream("assets/" + file))
            {
                if (s == null)
                {
                    return null;
                }

                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }

        public static Mesh Get(string file)
        {
            Mesh m;
            if (s_meshes.TryGetValue(file, out m) && m != null)
            {
                return m;
            }

            var data = Resource(file);
            if (data == null)
            {
                Plugin.Log.LogError("Missing model " + file);
                return null;
            }

            using (var br = new BinaryReader(new MemoryStream(data)))
            {
                if (new string(br.ReadChars(4)) != "WZM1")
                {
                    throw new InvalidDataException(file + " is not a WZM1 mesh");
                }

                int nv = br.ReadInt32(), ni = br.ReadInt32();
                var v = new Vector3[nv];
                var n = new Vector3[nv];
                var uv = new Vector2[nv];
                var tri = new int[ni];
                for (int i = 0; i < nv; i++) v[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                for (int i = 0; i < nv; i++) n[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                for (int i = 0; i < nv; i++) uv[i] = new Vector2(br.ReadSingle(), br.ReadSingle());
                for (int i = 0; i < ni; i++) tri[i] = br.ReadInt32();
                m = new Mesh { name = "WZ_" + file };
                if (nv > 65000)
                {
                    m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                }

                m.vertices = v;
                m.normals = n;
                m.uv = uv;
                m.triangles = tri;
                m.RecalculateBounds();
                try
                {
                    m.RecalculateTangents();
                }
                catch (Exception)
                {
                    // normal maps just look flatter
                }

                m.UploadMeshData(false);
            }

            s_meshes[file] = m;
            return m;
        }

        public static Texture2D Texture(string file, bool linear = false)
        {
            Texture2D t;
            if (s_textures.TryGetValue(file, out t) && t != null)
            {
                return t;
            }

            var data = Resource(file);
            if (data == null)
            {
                return null;
            }

            t = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear) { name = "WZ_" + file };
            if (!ImageConversion.LoadImage(t, data, false))
            {
                return null;
            }

            t.wrapMode = TextureWrapMode.Repeat;
            t.hideFlags = HideFlags.DontUnloadUnusedAsset;
            s_textures[file] = t;
            return t;
        }

        /// <summary>A copy of a plain opaque vanilla material (so the game's own lit shader is used) with our textures.</summary>
        public static Material Material(string albedo, string normal, string emission, Color tint, Color glow)
        {
            Material template = null;
            foreach (string prefab in new[] { "Club", "piece_workbench", "wood_floor" })
            {
                var go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null;
                if (go == null)
                {
                    go = Jotunn.Managers.PrefabManager.Instance.GetPrefab(prefab);
                }

                if (go == null)
                {
                    continue;
                }

                foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var m = r.sharedMaterial;
                    if (m != null && m.shader != null && m.renderQueue < 2450 && m.HasProperty("_MainTex"))
                    {
                        template = m;
                        break;
                    }
                }

                if (template != null)
                {
                    break;
                }
            }

            var mat = template != null ? new Material(template) : new Material(Shader.Find("Standard"));
            foreach (string prop in mat.GetTexturePropertyNames())
            {
                string p = prop.ToLowerInvariant();
                if (prop != "_MainTex" && (p.Contains("emission") || p.Contains("metal") || p.Contains("gloss") || p.Contains("spec") ||
                                           p.Contains("occlusion") || p.Contains("detail") || p.Contains("rough") || p.Contains("mask") ||
                                           p.Contains("bump") || p.Contains("normal")))
                {
                    mat.SetTexture(prop, null);
                }
            }

            var a = Texture(albedo);
            if (a != null)
            {
                mat.SetTexture("_MainTex", a);
                mat.SetTextureScale("_MainTex", Vector2.one);
                mat.SetTextureOffset("_MainTex", Vector2.zero);
            }

            if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", tint);
            }

            var nrm = normal != null ? Texture(normal, true) : null;
            if (nrm != null && mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", nrm);
                mat.EnableKeyword("_NORMALMAP");
            }

            var em = emission != null ? Texture(emission) : null;
            if (mat.HasProperty("_EmissionColor"))
            {
                if (em != null && mat.HasProperty("_EmissionMap"))
                {
                    mat.SetTexture("_EmissionMap", em);
                    mat.SetColor("_EmissionColor", glow);
                    mat.EnableKeyword("_EMISSION");
                }
                else
                {
                    mat.SetColor("_EmissionColor", glow.maxColorComponent > 0f ? glow * 0.2f : Color.black);
                }
            }

            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.3f);
            mat.name = "WZ_" + albedo;
            return mat;
        }

        public static GameObject Model(Transform parent, string mesh, Material mat, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = Get(mesh);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            return go;
        }
    }
}
