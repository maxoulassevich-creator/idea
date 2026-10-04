using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlyingPets
{
    /// <summary>One pet ready for the game: mesh, textures and the parsed JSON.</summary>
    internal sealed class PetAsset
    {
        public string Key;
        public string Origin;
        public PetJson Data;
        public Mesh Mesh;
        public Texture2D Albedo;
        public Texture2D Normal;
        public string[] BoneNames;
        public int[] BoneParents;
        public Vector3[] Pivots;
        public Vector3 EyeCenter;
        public bool HasEyes;
        public GameObject Prefab;
        public HashSet<string> Abilities = new HashSet<string>();

        public string PrefabName
        {
            get { return "FP_Pet_" + Key; }
        }

        public string NameToken
        {
            get { return "fp_pet_" + Key; }
        }

        public string DisplayName
        {
            get { return Texts.Get(NameToken); }
        }

        public int Bone(string name)
        {
            return Array.IndexOf(BoneNames, name);
        }

        public static Vector3 V3(float[] a, Vector3 fallback)
        {
            return a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : fallback;
        }

        public static float At(float[] a, int i, float fallback)
        {
            return a != null && a.Length > i ? a[i] : fallback;
        }

        public static PetAsset Load(PetSource src)
        {
            var data = PetJsonReader.Parse(Encoding.UTF8.GetString(src.Read(src.Key + ".json")));
            var asset = new PetAsset { Key = src.Key, Origin = src.Origin, Data = data };
            asset.Abilities = PetAbility.Read(src.Key, data.abilities);
            int nb = data.bones.Length;
            asset.BoneNames = new string[nb];
            asset.BoneParents = new int[nb];
            asset.Pivots = new Vector3[nb];
            for (int i = 0; i < nb; i++)
            {
                var b = data.bones[i];
                if (b == null || string.IsNullOrEmpty(b.name) || b.parent >= i)
                {
                    throw new InvalidDataException("bone " + i + " is invalid (bones must be listed parents first)");
                }

                asset.BoneNames[i] = b.name;
                asset.BoneParents[i] = b.parent;
                asset.Pivots[i] = V3(b.pivot, Vector3.zero);
            }

            asset.Mesh = BuildMesh(PetMeshData.Parse(src.Read(src.Key + ".vpet"), nb), asset);

            // a texture problem must not cost us the whole pet: fall back to a plain colour
            string albedo = string.IsNullOrEmpty(data.albedo) ? src.Key + "_albedo.jpg" : data.albedo;
            try
            {
                asset.Albedo = LoadTexture(src.Read(albedo), albedo, false);
            }
            catch (Exception e)
            {
                FlyingPetsPlugin.Log.LogError("Pet '" + src.Key + "': texture " + albedo + " failed (" + e.GetType().Name + ": " +
                                              e.Message + "), using a plain colour instead");
                asset.Albedo = Plain(new Color32(150, 150, 152, 255), false);
            }

            if (!string.IsNullOrEmpty(data.normal) && src.Has(data.normal))
            {
                try
                {
                    asset.Normal = LoadTexture(src.Read(data.normal), data.normal, true);
                }
                catch (Exception e)
                {
                    FlyingPetsPlugin.Log.LogWarning("Pet '" + src.Key + "': normal map " + data.normal + " failed (" +
                                                    e.GetType().Name + ": " + e.Message + "), continuing without it");
                    asset.Normal = null;
                }
            }

            return asset;
        }

        private static Mesh BuildMesh(PetMeshData d, PetAsset asset)
        {
            int nv = d.VertexCount;
            var v = new Vector3[nv];
            var nn = new Vector3[nv];
            var tt = new Vector4[nv];
            var uu = new Vector2[nv];
            var weights = new BoneWeight[nv];
            var idx4 = new int[4];
            var w4 = new float[4];
            for (int i = 0; i < nv; i++)
            {
                v[i] = new Vector3(d.Pos[i * 3], d.Pos[i * 3 + 1], d.Pos[i * 3 + 2]);
                nn[i] = new Vector3(d.Nrm[i * 3], d.Nrm[i * 3 + 1], d.Nrm[i * 3 + 2]);
                tt[i] = new Vector4(d.Tan[i * 4], d.Tan[i * 4 + 1], d.Tan[i * 4 + 2], d.Tan[i * 4 + 3]);
                uu[i] = new Vector2(d.Uv[i * 2], d.Uv[i * 2 + 1]);

                float sum = 0f;
                for (int k = 0; k < 4; k++)
                {
                    idx4[k] = d.BoneIndex[i * 4 + k];
                    w4[k] = Mathf.Max(0f, d.BoneWeight[i * 4 + k]);
                    sum += w4[k];
                }

                // heaviest first, as Unity expects
                for (int a = 1; a < 4; a++)
                {
                    for (int c = a; c > 0 && w4[c] > w4[c - 1]; c--)
                    {
                        float tw = w4[c];
                        w4[c] = w4[c - 1];
                        w4[c - 1] = tw;
                        int ti = idx4[c];
                        idx4[c] = idx4[c - 1];
                        idx4[c - 1] = ti;
                    }
                }

                if (sum <= 1e-6f)
                {
                    w4[0] = 1f;
                    sum = 1f;
                }

                weights[i] = new BoneWeight
                {
                    boneIndex0 = idx4[0], weight0 = w4[0] / sum,
                    boneIndex1 = idx4[1], weight1 = w4[1] / sum,
                    boneIndex2 = idx4[2], weight2 = w4[2] / sum,
                    boneIndex3 = idx4[3], weight3 = w4[3] / sum,
                };
            }

            var mesh = new Mesh { name = "FP_" + asset.Key };
            if (nv > 65000)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            mesh.vertices = v;
            mesh.normals = nn;
            mesh.tangents = tt;
            mesh.uv = uu;
            mesh.boneWeights = weights;
            mesh.subMeshCount = d.Submeshes.Count;
            for (int s = 0; s < d.Submeshes.Count; s++)
            {
                mesh.SetTriangles(d.Submeshes[s], s, false);
            }

            // every bone rests unrotated at its pivot, so its bind pose is a plain translation
            int nb = asset.Pivots.Length;
            var bind = new Matrix4x4[nb];
            for (int i = 0; i < nb; i++)
            {
                bind[i] = Matrix4x4.Translate(-asset.Pivots[i]);
            }

            mesh.bindposes = bind;
            mesh.RecalculateBounds();

            // submesh 1 holds the eyes: remember where they are for the eye light
            if (d.Submeshes.Count > 1 && d.Submeshes[1].Length > 0)
            {
                Vector3 c = Vector3.zero;
                foreach (int i in d.Submeshes[1])
                {
                    c += v[i];
                }

                asset.EyeCenter = c / d.Submeshes[1].Length;
                asset.HasEyes = true;
            }

            return mesh;
        }

        // ------------------------------------------------------------------ textures
        private static MethodInfo s_loadImage;

        /// <summary>
        ///     ImageConversion.LoadImage through reflection: referencing UnityEngine.ImageConversionModule
        ///     directly breaks .NET Framework builds (it is compiled against netstandard 2.1).
        /// </summary>
        private static bool LoadImage(Texture2D tex, byte[] data, bool markNonReadable)
        {
            if (s_loadImage == null)
            {
                Type type = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = asm.GetType("UnityEngine.ImageConversion", false);
                    if (type != null)
                    {
                        break;
                    }
                }

                if (type == null)
                {
                    type = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule", false);
                }

                if (type == null)
                {
                    type = Assembly.Load("UnityEngine.ImageConversionModule").GetType("UnityEngine.ImageConversion", true);
                }

                s_loadImage = type.GetMethod("LoadImage", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) }, null);
                if (s_loadImage == null)
                {
                    throw new MissingMethodException("UnityEngine.ImageConversion.LoadImage(Texture2D, byte[], bool)");
                }
            }

            return (bool)s_loadImage.Invoke(null, new object[] { tex, data, markNonReadable });
        }

        private static Texture2D LoadTexture(byte[] bytes, string name, bool normalMap)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, normalMap);
            if (!LoadImage(tex, bytes, false))
            {
                throw new InvalidDataException("the image could not be decoded");
            }

            tex.name = Path.GetFileNameWithoutExtension(name);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;
            try
            {
                if (normalMap)
                {
                    // (1, y, z, x): read correctly both as an RG and as a DXT5nm (AG) normal map
                    var px = tex.GetPixels32();
                    for (int i = 0; i < px.Length; i++)
                    {
                        var c = px[i];
                        px[i] = new Color32(255, c.g, c.b, c.r);
                    }

                    tex.SetPixels32(px);
                    tex.Apply(true, true);
                }
                else
                {
                    tex.Compress(true);
                    tex.Apply(false, true);
                }
            }
            catch (Exception e)
            {
                // keep the texture as decoded; only the optimisation failed
                FlyingPetsPlugin.Log.LogWarning("Texture " + name + ": post-processing skipped (" + e.Message + ")");
            }

            return tex;
        }

        /// <summary>A small UI image (status effect icons): no mipmaps, no compression.</summary>
        internal static Texture2D LoadIconTexture(byte[] bytes, string name)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!LoadImage(tex, bytes, false))
            {
                throw new InvalidDataException("the image could not be decoded");
            }

            tex.name = name;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        private static Texture2D Plain(Color32 color, bool linear)
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false, linear);
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++)
            {
                px[i] = color;
            }

            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }

    /// <summary>
    ///     All pets: the ones built into FlyingPets.dll, plus pets/&lt;key&gt; folders on disk (a folder
    ///     with the same key replaces the built-in pet when it loads correctly).
    /// </summary>
    internal static class PetLibrary
    {
        public static readonly List<PetAsset> Pets = new List<PetAsset>();
        public static string LastError = "";

        public static PetAsset Get(string key)
        {
            foreach (var p in Pets)
            {
                if (p.Key == key)
                {
                    return p;
                }
            }

            return null;
        }

        public static void LoadAll(string pluginDir)
        {
            Pets.Clear();
            LastError = "";
            var log = FlyingPetsPlugin.Log;

            var embedded = new List<PetSource>();
            try
            {
                embedded = PetSource.Embedded(typeof(PetLibrary).Assembly);
            }
            catch (Exception e)
            {
                log.LogError("Reading the built-in pets failed: " + e);
            }

            var roots = new List<string>();
            if (!string.IsNullOrEmpty(pluginDir))
            {
                roots.Add(Path.Combine(pluginDir, "pets"));
                roots.Add(pluginDir);
            }

            roots.Add(Path.Combine(BepInEx.Paths.PluginPath, "pets"));
            var external = PetSource.FromFolders(roots, m => log.LogWarning(m));
            log.LogInfo("Looking for pets: " + embedded.Count + " built in, " + external.Count + " on disk (plugin folder: " + pluginDir + ")");

            // per key: folders on disk first (so a pet can be updated by dropping files), then the built-in one
            var keys = new List<string>();
            foreach (var s in external)
            {
                if (!keys.Contains(s.Key))
                {
                    keys.Add(s.Key);
                }
            }

            foreach (var s in embedded)
            {
                if (!keys.Contains(s.Key))
                {
                    keys.Add(s.Key);
                }
            }

            foreach (string key in keys)
            {
                var candidates = new List<PetSource>();
                candidates.AddRange(external.FindAll(s => s.Key == key));
                candidates.AddRange(embedded.FindAll(s => s.Key == key));
                foreach (var src in candidates)
                {
                    try
                    {
                        var asset = PetAsset.Load(src);
                        if (!string.IsNullOrEmpty(asset.Data.nameEn))
                        {
                            Texts.AddPetName(key, asset.Data.nameEn, asset.Data.nameRu);
                        }

                        Pets.Add(asset);
                        log.LogInfo("Loaded pet '" + key + "' (" + asset.Mesh.vertexCount + " vertices, " +
                                    asset.BoneNames.Length + " bones) from " + src.Origin);
                        break;
                    }
                    catch (Exception e)
                    {
                        LastError = key + ": " + e.Message;
                        log.LogError("Pet '" + key + "' from " + src.Origin + " could not be loaded: " + e);
                    }
                }
            }

            if (Pets.Count == 0)
            {
                log.LogError("No pets could be loaded. " + (LastError.Length > 0 ? "Last error: " + LastError : "No pet files were found."));
            }
        }
    }
}
