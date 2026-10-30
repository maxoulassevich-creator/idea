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

        private static bool s_shaderLogged;

        /// <summary>
        ///     A lit opaque material with our textures: a fresh Standard material when the game has the shader, otherwise a
        ///     copy of a vanilla one. Every texture slot we do not fill is emptied and its keyword turned off: an empty
        ///     metallic map with _METALLICGLOSSMAP on reads as white, which makes a chrome mirror that blooms into a white
        ///     blob (the 1.0 Codex).
        /// </summary>
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

            var standard = Shader.Find("Standard");
            Material mat;
            if (standard != null)
            {
                mat = new Material(standard);
            }
            else if (template != null)
            {
                mat = new Material(template);
            }
            else
            {
                mat = new Material(Shader.Find("Sprites/Default"));
            }

            if (!s_shaderLogged)
            {
                s_shaderLogged = true;
                Plugin.Log.LogInfo("Model shader: " + (mat.shader != null ? mat.shader.name : "none"));
            }

            foreach (string prop in mat.GetTexturePropertyNames())
            {
                if (prop != "_MainTex")
                {
                    mat.SetTexture(prop, null);
                }
            }

            foreach (string kw in new[] { "_METALLICGLOSSMAP", "_SPECGLOSSMAP", "_DETAIL_MULX2", "_PARALLAXMAP", "_NORMALMAP", "_EMISSION",
                         "_ALPHATEST_ON", "_ALPHABLEND_ON", "_ALPHAPREMULTIPLY_ON", "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A" })
            {
                mat.DisableKeyword(kw);
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
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                else
                {
                    mat.SetColor("_EmissionColor", Color.black);
                }
            }

            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.25f);
            if (mat.HasProperty("_GlossMapScale")) mat.SetFloat("_GlossMapScale", 0.25f);
            if (mat.HasProperty("_SpecularHighlights")) mat.SetFloat("_SpecularHighlights", 1f);
            if (mat.HasProperty("_GlossyReflections")) mat.SetFloat("_GlossyReflections", 0f);
            mat.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
            mat.renderQueue = -1;
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
