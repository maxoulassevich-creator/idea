using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace FlyingPets
{
    // Plain .NET only (no Unity): parsing and finding pet files. Kept separate so it can be tested
    // outside the game.

#pragma warning disable 0649 // filled by PetJsonReader through reflection
    [Serializable]
    public class PetBoneJson
    {
        public string name;
        public int parent;
        public float[] pivot;
    }

    /// <summary>&lt;key&gt;.json - skeleton, seat and animation tuning of one pet.</summary>
    [Serializable]
    public class PetJson
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

    /// <summary>The files of one pet: inside FlyingPets.dll or in a folder on disk.</summary>
    internal sealed class PetSource
    {
        private readonly Func<string, byte[]> m_read;
        private readonly Func<string, bool> m_has;

        public PetSource(string key, string origin, Func<string, byte[]> read, Func<string, bool> has)
        {
            Key = key;
            Origin = origin;
            m_read = read;
            m_has = has;
        }

        public string Key { get; private set; }
        public string Origin { get; private set; }

        public bool Has(string file)
        {
            return !string.IsNullOrEmpty(file) && m_has(file);
        }

        public byte[] Read(string file)
        {
            if (!Has(file))
            {
                throw new FileNotFoundException(file + " is missing (" + Origin + ")");
            }

            return m_read(file);
        }

        /// <summary>Resources named pets/&lt;key&gt;/&lt;file&gt; built into the mod.</summary>
        public static List<PetSource> Embedded(Assembly assembly)
        {
            var byKey = new SortedDictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string res in assembly.GetManifestResourceNames())
            {
                string norm = res.Replace('\\', '/');
                int a = norm.IndexOf("pets/", StringComparison.OrdinalIgnoreCase);
                if (a < 0)
                {
                    continue;
                }

                string[] parts = norm.Substring(a + 5).Split('/');
                if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
                {
                    continue;
                }

                Dictionary<string, string> files;
                if (!byKey.TryGetValue(parts[0], out files))
                {
                    files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    byKey[parts[0]] = files;
                }

                files[parts[1]] = res;
            }

            var list = new List<PetSource>();
            foreach (var kv in byKey)
            {
                var files = kv.Value;
                if (!files.ContainsKey(kv.Key + ".json") || !files.ContainsKey(kv.Key + ".vpet"))
                {
                    continue;
                }

                list.Add(new PetSource(kv.Key, "built into FlyingPets.dll",
                    f =>
                    {
                        using (var s = assembly.GetManifestResourceStream(files[f]))
                        using (var m = new MemoryStream())
                        {
                            s.CopyTo(m);
                            return m.ToArray();
                        }
                    },
                    f => files.ContainsKey(f)));
            }

            return list;
        }

        /// <summary>Every folder under the given roots that holds a &lt;key&gt;.vpet + &lt;key&gt;.json pair.</summary>
        public static List<PetSource> FromFolders(IEnumerable<string> roots, Action<string> log)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<PetSource>();
            foreach (string root in roots)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                {
                    continue;
                }

                string[] found;
                try
                {
                    found = Directory.GetFiles(root, "*.vpet", SearchOption.AllDirectories);
                }
                catch (Exception e)
                {
                    log("Could not search " + root + ": " + e.Message);
                    continue;
                }

                foreach (string file in found)
                {
                    string dir = Path.GetDirectoryName(file);
                    string key = Path.GetFileNameWithoutExtension(file);
                    if (!seen.Add(dir + "|" + key) || !File.Exists(Path.Combine(dir, key + ".json")))
                    {
                        continue;
                    }

                    string d = dir;
                    list.Add(new PetSource(key, d, f => File.ReadAllBytes(Path.Combine(d, f)), f => File.Exists(Path.Combine(d, f))));
                }
            }

            return list;
        }
    }

    /// <summary>
    ///     .vpet: 'VPET', int version, int vertexCount, int submeshCount, float pos[n*3], nrm[n*3],
    ///     tan[n*4], uv[n*2], int bone[n*4], float weight[n*4], then per submesh: int indexCount,
    ///     int index[indexCount]. Unity space, little endian.
    /// </summary>
    internal sealed class PetMeshData
    {
        public int VertexCount;
        public float[] Pos;
        public float[] Nrm;
        public float[] Tan;
        public float[] Uv;
        public int[] BoneIndex;
        public float[] BoneWeight;
        public readonly List<int[]> Submeshes = new List<int[]>();

        public static PetMeshData Parse(byte[] b, int boneCount)
        {
            if (b == null || b.Length < 16 || b[0] != 'V' || b[1] != 'P' || b[2] != 'E' || b[3] != 'T')
            {
                throw new InvalidDataException("not a .vpet file");
            }

            int nv = BitConverter.ToInt32(b, 8);
            int nsub = BitConverter.ToInt32(b, 12);
            int o = 16;
            if (nv <= 0 || nsub <= 0 || nsub > 8 || b.Length < o + (long)nv * 72)
            {
                throw new InvalidDataException(".vpet is truncated (" + b.Length + " bytes for " + nv + " vertices)");
            }

            var m = new PetMeshData { VertexCount = nv };
            m.Pos = Floats(b, ref o, nv * 3);
            m.Nrm = Floats(b, ref o, nv * 3);
            m.Tan = Floats(b, ref o, nv * 4);
            m.Uv = Floats(b, ref o, nv * 2);
            m.BoneIndex = Ints(b, ref o, nv * 4);
            m.BoneWeight = Floats(b, ref o, nv * 4);
            for (int s = 0; s < nsub; s++)
            {
                if (o + 4 > b.Length)
                {
                    throw new InvalidDataException(".vpet is truncated in submesh " + s);
                }

                int n = BitConverter.ToInt32(b, o);
                o += 4;
                if (n < 0 || n % 3 != 0 || o + (long)n * 4 > b.Length)
                {
                    throw new InvalidDataException(".vpet has a bad submesh " + s);
                }

                var idx = Ints(b, ref o, n);
                for (int i = 0; i < n; i++)
                {
                    if ((uint)idx[i] >= (uint)nv)
                    {
                        throw new InvalidDataException(".vpet index out of range");
                    }
                }

                m.Submeshes.Add(idx);
            }

            for (int i = 0; i < m.BoneIndex.Length; i++)
            {
                if (m.BoneIndex[i] < 0 || m.BoneIndex[i] >= boneCount)
                {
                    m.BoneIndex[i] = 0;
                    m.BoneWeight[i] = 0f;
                }
            }

            return m;
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
    }

    /// <summary>A small JSON reader for the pet files (objects, arrays, numbers, strings, true/false/null).</summary>
    internal static class PetJsonReader
    {
        public static PetJson Parse(string text)
        {
            var r = new Reader(text);
            object root = r.Value();
            r.End();
            var obj = root as Dictionary<string, object>;
            if (obj == null)
            {
                throw new InvalidDataException("the pet json must be an object");
            }

            var data = (PetJson)Fill(typeof(PetJson), obj);
            if (data.bones == null || data.bones.Length == 0)
            {
                throw new InvalidDataException("the pet json has no bones");
            }

            return data;
        }

        private static object Fill(Type type, Dictionary<string, object> obj)
        {
            object target = Activator.CreateInstance(type);
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object v;
                if (!obj.TryGetValue(f.Name, out v) || v == null)
                {
                    continue;
                }

                f.SetValue(target, Convert(f.FieldType, v, f.Name));
            }

            return target;
        }

        private static object Convert(Type type, object v, string field)
        {
            if (type == typeof(string))
            {
                return v as string ?? System.Convert.ToString(v, CultureInfo.InvariantCulture);
            }

            if (type == typeof(float))
            {
                return (float)Number(v, field);
            }

            if (type == typeof(int))
            {
                return (int)Math.Round(Number(v, field));
            }

            if (type == typeof(bool))
            {
                return v is bool ? (bool)v : Number(v, field) != 0.0;
            }

            if (type.IsArray)
            {
                var list = v as List<object>;
                if (list == null)
                {
                    throw new InvalidDataException(field + " must be an array");
                }

                var element = type.GetElementType();
                var arr = Array.CreateInstance(element, list.Count);
                for (int i = 0; i < list.Count; i++)
                {
                    arr.SetValue(Convert(element, list[i], field + "[" + i + "]"), i);
                }

                return arr;
            }

            var obj = v as Dictionary<string, object>;
            if (obj != null && type.IsClass)
            {
                return Fill(type, obj);
            }

            throw new InvalidDataException(field + " has an unexpected value");
        }

        private static double Number(object v, string field)
        {
            if (v is double)
            {
                return (double)v;
            }

            if (v is bool)
            {
                return (bool)v ? 1.0 : 0.0;
            }

            double d;
            if (v is string && double.TryParse((string)v, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
            {
                return d;
            }

            throw new InvalidDataException(field + " must be a number");
        }

        private sealed class Reader
        {
            private readonly string s;
            private int i;

            public Reader(string text)
            {
                s = text ?? "";
                if (s.Length > 0 && s[0] == '﻿')
                {
                    i = 1; // byte order mark
                }
            }

            public void End()
            {
                Skip();
                if (i < s.Length)
                {
                    throw Error("unexpected text after the end");
                }
            }

            public object Value()
            {
                Skip();
                if (i >= s.Length)
                {
                    throw Error("unexpected end");
                }

                char c = s[i];
                if (c == '{')
                {
                    return Object();
                }

                if (c == '[')
                {
                    return Array();
                }

                if (c == '"')
                {
                    return String();
                }

                if (Word("true"))
                {
                    return true;
                }

                if (Word("false"))
                {
                    return false;
                }

                if (Word("null"))
                {
                    return null;
                }

                return NumberValue();
            }

            private Dictionary<string, object> Object()
            {
                var d = new Dictionary<string, object>();
                i++; // {
                Skip();
                if (Peek() == '}')
                {
                    i++;
                    return d;
                }

                while (true)
                {
                    Skip();
                    if (Peek() != '"')
                    {
                        throw Error("expected a name");
                    }

                    string key = String();
                    Skip();
                    Expect(':');
                    d[key] = Value();
                    Skip();
                    char c = Peek();
                    i++;
                    if (c == ',')
                    {
                        continue;
                    }

                    if (c == '}')
                    {
                        return d;
                    }

                    throw Error("expected , or }");
                }
            }

            private List<object> Array()
            {
                var l = new List<object>();
                i++; // [
                Skip();
                if (Peek() == ']')
                {
                    i++;
                    return l;
                }

                while (true)
                {
                    l.Add(Value());
                    Skip();
                    char c = Peek();
                    i++;
                    if (c == ',')
                    {
                        continue;
                    }

                    if (c == ']')
                    {
                        return l;
                    }

                    throw Error("expected , or ]");
                }
            }

            private string String()
            {
                i++; // opening quote
                var sb = new StringBuilder();
                while (i < s.Length)
                {
                    char c = s[i++];
                    if (c == '"')
                    {
                        return sb.ToString();
                    }

                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }

                    if (i >= s.Length)
                    {
                        break;
                    }

                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 > s.Length)
                            {
                                throw Error("bad \\u escape");
                            }

                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                            break;
                        default: sb.Append(e); break;
                    }
                }

                throw Error("unterminated string");
            }

            private object NumberValue()
            {
                int start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0)
                {
                    i++;
                }

                double d;
                if (i == start || !double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                {
                    throw Error("bad value");
                }

                return d;
            }

            private bool Word(string w)
            {
                if (string.CompareOrdinal(s, i, w, 0, w.Length) == 0)
                {
                    i += w.Length;
                    return true;
                }

                return false;
            }

            private char Peek()
            {
                return i < s.Length ? s[i] : '\0';
            }

            private void Expect(char c)
            {
                if (Peek() != c)
                {
                    throw Error("expected '" + c + "'");
                }

                i++;
            }

            private void Skip()
            {
                while (i < s.Length && char.IsWhiteSpace(s[i]))
                {
                    i++;
                }
            }

            private Exception Error(string what)
            {
                return new InvalidDataException("json: " + what + " at character " + i);
            }
        }
    }
}
