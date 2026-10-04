using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlyingPets
{
#pragma warning disable 0649 // filled by JsonUtility
    [Serializable]
    internal class PetBoneJson
    {
        public string name;
        public int parent;
        public float[] pivot;
    }

    /// <summary>pets/&lt;key&gt;/&lt;key&gt;.json - skeleton, seat and animation tuning of one pet.</summary>
    [Serializable]
    internal class PetJson
    {
        public string name;
        public int version;
        public PetBoneJson[] bones;
        public string albedo;
        public string normal;
        public float[] seat;
        public int mountSide;
        public float[] eyeGlow;
        public string headBone;

        // wing poses as axis-angle (x, y, z, degrees), authored for the right wing
        public float[] wingFold;
        public float[] wingRamp;
        public float[] wingPerch;
        public float[] wingScoop;

        // boarding points on the right wing (rest pose; mirrored for the left)
        public float[] wingStep;
        public float[] wingTip;

        public float[] flyFront;
        public float[] flyRear;
        public float[] kneelFront;
        public float[] kneelRear;
        public float kneelDrop;
        public float kneelPitch;
        public float flapAmp;
        public float flapPeriod;
        public float[] neckFly;
        public float[] tailFly;
        public float walkStride;
        public float trotStride;
        public float swingWalk;
        public float swingTrot;
        public float[] frontFlex;
        public float[] rearFlex;
        public float[] boundsMin;
        public float[] boundsMax;
        public float bodyRadius;

        // optional
        public string nameEn;
        public string nameRu;
    }
#pragma warning restore 0649

    /// <summary>One pet loaded from disk: mesh, textures and the parsed JSON.</summary>
    internal sealed class PetAsset
    {
        public string Key;
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

        public static PetAsset Load(string dir, string key)
        {
            string jsonPath = Path.Combine(dir, key + ".json");
            string meshPath = Path.Combine(dir, key + ".vpet");
            var data = JsonUtility.FromJson<PetJson>(File.ReadAllText(jsonPath));
            if (data == null || data.bones == null || data.bones.Length == 0)
            {
                throw new InvalidDataException("no skeleton in " + jsonPath);
            }

            var asset = new PetAsset { Key = key, Data = data };
            int nb = data.bones.Length;
            asset.BoneNames = new string[nb];
            asset.BoneParents = new int[nb];
            asset.Pivots = new Vector3[nb];
            for (int i = 0; i < nb; i++)
            {
                asset.BoneNames[i] = data.bones[i].name;
                asset.BoneParents[i] = data.bones[i].parent;
                asset.Pivots[i] = V3(data.bones[i].pivot, Vector3.zero);
                if (data.bones[i].parent >= i)
                {
                    throw new InvalidDataException("bones must be listed parents first (" + data.bones[i].name + ")");
                }
            }

            asset.Mesh = ReadMesh(meshPath, key, asset);
            asset.Albedo = LoadTexture(Path.Combine(dir, data.albedo ?? (key + "_albedo.jpg")), false);
            if (!string.IsNullOrEmpty(data.normal) && File.Exists(Path.Combine(dir, data.normal)))
            {
                asset.Normal = LoadTexture(Path.Combine(dir, data.normal), true);
            }

            return asset;
        }

        // ------------------------------------------------------------------ .vpet
        // 'VPET', int version, int vertexCount, int submeshCount,
        // float pos[n*3], nrm[n*3], tan[n*4], uv[n*2], int bone[n*4], float weight[n*4],
        // then per submesh: int indexCount, int index[indexCount]   (Unity space, little endian)
        private static Mesh ReadMesh(string path, string key, PetAsset asset)
        {
            byte[] b = File.ReadAllBytes(path);
            if (b.Length < 16 || b[0] != 'V' || b[1] != 'P' || b[2] != 'E' || b[3] != 'T')
            {
                throw new InvalidDataException(path + " is not a .vpet file");
            }

            int nv = BitConverter.ToInt32(b, 8);
            int nsub = BitConverter.ToInt32(b, 12);
            int o = 16;
            if (nv <= 0 || nsub <= 0 || nsub > 8 || b.Length < o + (long)nv * 72)
            {
                throw new InvalidDataException(path + " is truncated");
            }

            var pos = Floats(b, ref o, nv * 3);
            var nrm = Floats(b, ref o, nv * 3);
            var tan = Floats(b, ref o, nv * 4);
            var uv = Floats(b, ref o, nv * 2);
            var bi = Ints(b, ref o, nv * 4);
            var bw = Floats(b, ref o, nv * 4);
            var subs = new List<int[]>();
            for (int s = 0; s < nsub; s++)
            {
                int n = BitConverter.ToInt32(b, o);
                o += 4;
                if (n < 0 || n % 3 != 0 || o + (long)n * 4 > b.Length)
                {
                    throw new InvalidDataException(path + ": bad submesh " + s);
                }

                var idx = Ints(b, ref o, n);
                for (int i = 0; i < n; i++)
                {
                    if ((uint)idx[i] >= (uint)nv)
                    {
                        throw new InvalidDataException(path + ": index out of range");
                    }
                }

                subs.Add(idx);
            }

            int nb = asset.BoneNames.Length;
            var v = new Vector3[nv];
            var nn = new Vector3[nv];
            var tt = new Vector4[nv];
            var uu = new Vector2[nv];
            var weights = new BoneWeight[nv];
            var idx4 = new int[4];
            var w4 = new float[4];
            for (int i = 0; i < nv; i++)
            {
                v[i] = new Vector3(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]);
                nn[i] = new Vector3(nrm[i * 3], nrm[i * 3 + 1], nrm[i * 3 + 2]);
                tt[i] = new Vector4(tan[i * 4], tan[i * 4 + 1], tan[i * 4 + 2], tan[i * 4 + 3]);
                uu[i] = new Vector2(uv[i * 2], uv[i * 2 + 1]);

                float sum = 0f;
                for (int k = 0; k < 4; k++)
                {
                    int bone = bi[i * 4 + k];
                    float w = bw[i * 4 + k];
                    if (bone < 0 || bone >= nb || w <= 0f)
                    {
                        bone = 0;
                        w = 0f;
                    }

                    idx4[k] = bone;
                    w4[k] = w;
                    sum += w;
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

            var mesh = new Mesh { name = "FP_" + key };
            if (nv > 65000)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            mesh.vertices = v;
            mesh.normals = nn;
            mesh.tangents = tt;
            mesh.uv = uu;
            mesh.boneWeights = weights;
            mesh.subMeshCount = subs.Count;
            for (int s = 0; s < subs.Count; s++)
            {
                mesh.SetTriangles(subs[s], s, false);
            }

            // every bone rests unrotated at its pivot, so its bind pose is a plain translation
            var bind = new Matrix4x4[nb];
            for (int i = 0; i < nb; i++)
            {
                bind[i] = Matrix4x4.Translate(-asset.Pivots[i]);
            }

            mesh.bindposes = bind;
            mesh.RecalculateBounds();

            // submesh 1 holds the eyes: remember where they are for the eye light
            if (subs.Count > 1 && subs[1].Length > 0)
            {
                Vector3 c = Vector3.zero;
                foreach (int i in subs[1])
                {
                    c += v[i];
                }

                asset.EyeCenter = c / subs[1].Length;
                asset.HasEyes = true;
            }

            return mesh;
        }

        private static float[] Floats(byte[] b, ref int o, int count)
        {
            var a = new float[count];
            Buffer.BlockCopy(b, o, a, 0, count * 4);
            o += count * 4;
            return a;
        }

        private static int[] Ints(byte[] b, ref int o, int count)
        {
            var a = new int[count];
            Buffer.BlockCopy(b, o, a, 0, count * 4);
            o += count * 4;
            return a;
        }

        private static MethodInfo s_loadImage;

        /// <summary>
        ///     ImageConversion.LoadImage through reflection: referencing UnityEngine.ImageConversionModule
        ///     directly breaks .NET Framework builds (it is compiled against netstandard 2.1).
        /// </summary>
        private static bool LoadImage(Texture2D tex, byte[] data, bool markNonReadable)
        {
            if (s_loadImage == null)
            {
                var type = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                if (type != null)
                {
                    s_loadImage = type.GetMethod("LoadImage", BindingFlags.Public | BindingFlags.Static, null,
                        new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) }, null);
                }

                if (s_loadImage == null)
                {
                    throw new MissingMethodException("UnityEngine.ImageConversion.LoadImage");
                }
            }

            return (bool)s_loadImage.Invoke(null, new object[] { tex, data, markNonReadable });
        }

        private static Texture2D LoadTexture(string path, bool normalMap)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, normalMap);
            if (!LoadImage(tex, File.ReadAllBytes(path), false))
            {
                throw new InvalidDataException("cannot read image " + path);
            }

            tex.name = Path.GetFileNameWithoutExtension(path);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;
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

            return tex;
        }
    }

    /// <summary>Finds and loads every pet folder (pets/&lt;key&gt;/&lt;key&gt;.json + .vpet).</summary>
    internal static class PetLibrary
    {
        public static readonly List<PetAsset> Pets = new List<PetAsset>();

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
            foreach (string entry in FindPetDirs(pluginDir))
            {
                int bar = entry.LastIndexOf('|');
                string dir = entry.Substring(0, bar);
                string key = entry.Substring(bar + 1);
                if (Get(key) != null)
                {
                    continue;
                }

                try
                {
                    var asset = PetAsset.Load(dir, key);
                    if (!string.IsNullOrEmpty(asset.Data.nameEn))
                    {
                        Texts.AddPetName(key, asset.Data.nameEn, asset.Data.nameRu);
                    }

                    Pets.Add(asset);
                    FlyingPetsPlugin.Log.LogInfo("Loaded pet '" + key + "' (" + asset.Mesh.vertexCount + " vertices, " +
                                                 asset.BoneNames.Length + " bones) from " + dir);
                }
                catch (Exception e)
                {
                    FlyingPetsPlugin.Log.LogError("Pet '" + key + "' in " + dir + " could not be loaded: " + e.Message);
                }
            }

            if (Pets.Count == 0)
            {
                FlyingPetsPlugin.Log.LogError("No pets found. The 'pets' folder must sit next to FlyingPets.dll.");
            }
        }

        private static IEnumerable<string> FindPetDirs(string pluginDir)
        {
            var found = new List<string>();
            if (!string.IsNullOrEmpty(pluginDir))
            {
                Collect(Path.Combine(pluginDir, "pets"), found);
                if (found.Count == 0)
                {
                    Collect(pluginDir, found);
                }
            }

            // last resort: the mod manager may have unpacked the files somewhere else under plugins
            if (found.Count == 0)
            {
                Collect(BepInEx.Paths.PluginPath, found);
            }

            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }

        /// <summary>Every folder holding a matching &lt;key&gt;.vpet + &lt;key&gt;.json pair.</summary>
        private static void Collect(string root, List<string> found)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                return;
            }

            foreach (string file in Directory.GetFiles(root, "*.vpet", SearchOption.AllDirectories))
            {
                string dir = Path.GetDirectoryName(file);
                string key = Path.GetFileNameWithoutExtension(file);
                string entry = dir + "|" + key;
                if (File.Exists(Path.Combine(dir, key + ".json")) && !found.Contains(entry))
                {
                    found.Add(entry);
                }
            }
        }
    }
}
