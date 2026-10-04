using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlyingPets
{
    /// <summary>
    ///     The staff's own model ("Magic Staff" by petersoon, CC BY 4.0), put into the item that was cloned from
    ///     a vanilla staff. The vanilla model is measured (its length, which end is the head, where the hand
    ///     holds it) and ours is fitted the same way, so the game's holding pose and the dropped item still fit.
    /// </summary>
    internal static class StaffModel
    {
        internal sealed class StaffJson
        {
            public string albedo = "staff_albedo.jpg";
            public string normal = "staff_normal.png";
            public string emission = "staff_emission.jpg";
            public float[][] centerLine = null;     // (height 0..1, x, z) of the shaft's centre, filled from the json
        }

        private static Mesh s_mesh;
        private static Material s_material;
        private static StaffJson s_data;
        private static bool s_failed;

        /// <summary>Replaces the visuals under "attach" (in the hand, on the ground) and "attach_back".</summary>
        public static void Apply(GameObject itemPrefab)
        {
            if (!ModConfig.StaffOwnModel.Value || itemPrefab == null || !Load())
            {
                return;
            }

            int done = 0;
            for (int i = 0; i < itemPrefab.transform.childCount; i++)
            {
                var child = itemPrefab.transform.GetChild(i);
                if (child.name == "attach" || child.name == "attach_back")
                {
                    try
                    {
                        if (Replace(child))
                        {
                            done++;
                        }
                    }
                    catch (Exception e)
                    {
                        FlyingPetsPlugin.Log.LogError("Staff model: could not fit it into " + child.name + ": " + e);
                    }
                }
            }

            if (done == 0)
            {
                FlyingPetsPlugin.Log.LogWarning("Staff model: the base item has no attach model, keeping its look");
            }
        }

        // ------------------------------------------------------------------ loading
        private static byte[] Read(string file)
        {
            var asm = typeof(StaffModel).Assembly;
            string dir = Path.GetDirectoryName(asm.Location);
            string disk = string.IsNullOrEmpty(dir) ? null : Path.Combine(Path.Combine(dir, "staff"), file);
            if (disk != null && File.Exists(disk))
            {
                return File.ReadAllBytes(disk);
            }

            using (var s = asm.GetManifestResourceStream("staff/" + file))
            {
                if (s == null)
                {
                    throw new FileNotFoundException("staff/" + file + " is neither built in nor next to the mod");
                }

                var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        private static bool Load()
        {
            if (s_mesh != null)
            {
                return true;
            }

            if (s_failed)
            {
                return false;
            }

            try
            {
                s_data = PetJsonReader.ParseAs<StaffJson>(Encoding.UTF8.GetString(Read("staff.json")));

                var d = PetMeshData.Parse(Read("staff.vpet"), 1);
                int nv = d.VertexCount;
                var v = new Vector3[nv];
                var n = new Vector3[nv];
                var t = new Vector4[nv];
                var uv = new Vector2[nv];
                for (int i = 0; i < nv; i++)
                {
                    v[i] = new Vector3(d.Pos[i * 3], d.Pos[i * 3 + 1], d.Pos[i * 3 + 2]);
                    n[i] = new Vector3(d.Nrm[i * 3], d.Nrm[i * 3 + 1], d.Nrm[i * 3 + 2]);
                    t[i] = new Vector4(d.Tan[i * 4], d.Tan[i * 4 + 1], d.Tan[i * 4 + 2], d.Tan[i * 4 + 3]);
                    uv[i] = new Vector2(d.Uv[i * 2], d.Uv[i * 2 + 1]);
                }

                var mesh = new Mesh { name = "FP_Staff" };
                if (nv > 65000)
                {
                    mesh.indexFormat = IndexFormat.UInt32;
                }

                mesh.vertices = v;
                mesh.normals = n;
                mesh.tangents = t;
                mesh.uv = uv;
                var tris = new List<int>();
                foreach (var sub in d.Submeshes)
                {
                    tris.AddRange(sub);
                }

                mesh.SetTriangles(tris, 0, true);
                mesh.UploadMeshData(false);
                mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
                s_mesh = mesh;
                FlyingPetsPlugin.Log.LogInfo("Staff model loaded: " + nv + " vertices, " + tris.Count / 3 + " triangles");
                return true;
            }
            catch (Exception e)
            {
                s_failed = true;
                FlyingPetsPlugin.Log.LogError("Staff model could not be loaded, the staff keeps the vanilla look: " + e);
                return false;
            }
        }

        private static Material MakeMaterial(Material template)
        {
            if (s_material != null)
            {
                return s_material;
            }

            Texture2D albedo = null, normal = null, emission = null;
            try
            {
                albedo = PetAsset.LoadTexture(Read(s_data.albedo), s_data.albedo, false);
            }
            catch (Exception e)
            {
                FlyingPetsPlugin.Log.LogError("Staff albedo failed: " + e.Message);
            }

            try
            {
                normal = PetAsset.LoadTexture(Read(s_data.normal), s_data.normal, true);
            }
            catch (Exception e)
            {
                FlyingPetsPlugin.Log.LogWarning("Staff normal map failed: " + e.Message);
            }

            try
            {
                emission = PetAsset.LoadTexture(Read(s_data.emission), s_data.emission, false);
            }
            catch (Exception e)
            {
                FlyingPetsPlugin.Log.LogWarning("Staff glow map failed: " + e.Message);
            }

            Material mat;
            if (template != null)
            {
                mat = new Material(template);
                foreach (string prop in mat.GetTexturePropertyNames())
                {
                    string p = prop.ToLowerInvariant();
                    if (prop != "_MainTex" && prop != "_BumpMap" && prop != "_EmissionMap" &&
                        (p.Contains("emission") || p.Contains("metal") || p.Contains("gloss") || p.Contains("spec") ||
                         p.Contains("occlusion") || p.Contains("detail") || p.Contains("rough") || p.Contains("mask") ||
                         p.Contains("height") || p.Contains("parallax") || p.Contains("bump") || p.Contains("normal")))
                    {
                        mat.SetTexture(prop, null);
                    }
                }
            }
            else
            {
                mat = new Material(Shader.Find("Standard"));
            }

            mat.name = "FP_Staff";
            if (albedo != null)
            {
                mat.SetTexture("_MainTex", albedo);
                mat.SetTextureScale("_MainTex", Vector2.one);
                mat.SetTextureOffset("_MainTex", Vector2.zero);
            }

            if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", Color.white);
            }

            if (normal != null && mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", normal);
                mat.SetTextureScale("_BumpMap", Vector2.one);
                mat.SetTextureOffset("_BumpMap", Vector2.zero);
                mat.EnableKeyword("_NORMALMAP");
            }

            if (mat.HasProperty("_EmissionColor"))
            {
                if (emission != null && mat.HasProperty("_EmissionMap"))
                {
                    mat.SetTexture("_EmissionMap", emission);
                    mat.SetTextureScale("_EmissionMap", Vector2.one);
                    mat.SetTextureOffset("_EmissionMap", Vector2.zero);
                    mat.SetColor("_EmissionColor", Color.white * 1.3f);
                    mat.EnableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                else
                {
                    mat.SetColor("_EmissionColor", Color.black);
                }
            }

            if (mat.HasProperty("_Metallic"))
            {
                mat.SetFloat("_Metallic", 0f);
            }

            if (mat.HasProperty("_Glossiness"))
            {
                mat.SetFloat("_Glossiness", 0.25f);
            }

            FlyingPetsPlugin.Log.LogInfo("Staff material: shader " + (mat.shader != null ? mat.shader.name : "?") +
                                         (mat.HasProperty("_EmissionMap") ? ", glowing runes" : ", no glow map in this shader"));
            s_material = mat;
            return mat;
        }

        // ------------------------------------------------------------------ fitting into the vanilla attach
        private static bool Replace(Transform attach)
        {
            // what the base staff looks like in the attach's space
            var renderers = new List<Renderer>();
            foreach (var r in attach.GetComponentsInChildren<Renderer>(true))
            {
                if (r is MeshRenderer || r is SkinnedMeshRenderer)
                {
                    renderers.Add(r);
                }
            }

            if (renderers.Count == 0)
            {
                return false;
            }

            var points = new List<Vector3>();
            Material template = null;
            int layer = attach.gameObject.layer;
            foreach (var r in renderers)
            {
                Mesh mesh = null;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null)
                {
                    mesh = mf.sharedMesh;
                }
                else if (r is SkinnedMeshRenderer)
                {
                    mesh = ((SkinnedMeshRenderer)r).sharedMesh;
                }

                if (mesh == null)
                {
                    continue;
                }

                if (layer == attach.gameObject.layer)
                {
                    layer = r.gameObject.layer;
                }

                Matrix4x4 toAttach = attach.worldToLocalMatrix * r.transform.localToWorldMatrix;
                Vector3[] verts = null;
                try
                {
                    if (mesh.isReadable)
                    {
                        verts = mesh.vertices;
                    }
                }
                catch (Exception)
                {
                    verts = null;
                }

                if (verts != null && verts.Length > 0)
                {
                    int step = Math.Max(1, verts.Length / 4000);
                    for (int i = 0; i < verts.Length; i += step)
                    {
                        points.Add(toAttach.MultiplyPoint3x4(verts[i]));
                    }
                }
                else
                {
                    // not readable: the corners of its bounds
                    var b = mesh.bounds;
                    for (int c = 0; c < 8; c++)
                    {
                        var corner = new Vector3((c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y,
                            (c & 4) == 0 ? b.min.z : b.max.z);
                        points.Add(toAttach.MultiplyPoint3x4(corner));
                    }
                }
            }

            if (points.Count < 2)
            {
                return false;
            }

            Vector3 axis;
            float lo, hi;
            Vector3 head = LongAxis(points, out axis, out lo, out hi);
            float length = hi - lo;
            if (length < 0.2f)
            {
                return false;
            }

            // the hand is at the attach origin: where along the staff is that?
            float grip = Mathf.Clamp01(-lo / length);
            if (ModConfig.StaffFlip.Value)
            {
                head = -head;
                grip = 1f - grip;
            }

            grip = Mathf.Clamp01(grip + ModConfig.StaffGripShift.Value);
            float scale = length * Mathf.Max(0.1f, ModConfig.StaffScale.Value);

            template = PickTemplate(renderers);

            // hide the base staff (and its own glow, sparks, trails and lights)
            foreach (var r in attach.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = false;
            }

            foreach (var ps in attach.GetComponentsInChildren<ParticleSystem>(true))
            {
                var em = ps.emission;
                em.enabled = false;
                ps.gameObject.SetActive(false);
            }

            foreach (var l in attach.GetComponentsInChildren<Light>(true))
            {
                l.enabled = false;
            }

            // ours: y up from the bottom tip, length 1; the hand on the shaft at 'grip'
            Vector2 c2 = CenterAt(grip);
            Vector3 gripLocal = new Vector3(c2.x, grip, c2.y);
            Quaternion rot = Quaternion.AngleAxis(ModConfig.StaffRoll.Value, head) * Quaternion.FromToRotation(Vector3.up, head);
            var go = new GameObject("FP_StaffModel");
            go.layer = layer;
            go.transform.SetParent(attach, false);
            go.transform.localRotation = rot;
            go.transform.localScale = Vector3.one * scale;
            go.transform.localPosition = -(rot * (gripLocal * scale));
            go.AddComponent<MeshFilter>().sharedMesh = s_mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MakeMaterial(template);
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;
            FlyingPetsPlugin.Log.LogInfo("Staff model fitted into " + attach.name + ": length " + length.ToString("0.00") +
                                         " m, head " + head.ToString("F2") + ", hand at " + (grip * 100f).ToString("0") + "% of the length");
            return true;
        }

        /// <summary>
        ///     A plain opaque material of the base staff to copy (its biggest mesh, not the ice or glow), or of a
        ///     wooden club; the copy gets our textures.
        /// </summary>
        private static Material PickTemplate(List<Renderer> renderers)
        {
            Material best = Opaque(renderers);
            if (best == null)
            {
                var club = Jotunn.Managers.PrefabManager.Instance.GetPrefab("Club");
                if (club != null)
                {
                    best = Opaque(new List<Renderer>(club.GetComponentsInChildren<MeshRenderer>(true)));
                }
            }

            return best;
        }

        private static Material Opaque(List<Renderer> renderers)
        {
            Material best = null;
            int bestVerts = -1;
            foreach (var r in renderers)
            {
                var mf = r.GetComponent<MeshFilter>();
                Mesh mesh = mf != null ? mf.sharedMesh : r is SkinnedMeshRenderer ? ((SkinnedMeshRenderer)r).sharedMesh : null;
                int verts = mesh != null ? mesh.vertexCount : 0;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null || m.renderQueue >= 2450)
                    {
                        continue;
                    }

                    string sn = m.shader.name.ToLowerInvariant();
                    if (sn.Contains("particle") || sn.Contains("transparent") || sn.Contains("ice") || sn.Contains("glass") ||
                        sn.Contains("water") || sn.Contains("distort") || sn.Contains("unlit") || sn.Contains("additive"))
                    {
                        continue;
                    }

                    if (verts > bestVerts)
                    {
                        best = m;
                        bestVerts = verts;
                    }
                }
            }

            return best;
        }

        /// <summary>
        ///     The staff's long axis (through the points), its extent along it and which way the head points:
        ///     the end that is wider across, or else the end farther from the hand.
        /// </summary>
        private static Vector3 LongAxis(List<Vector3> pts, out Vector3 axis, out float lo, out float hi)
        {
            Vector3 mean = Vector3.zero;
            foreach (var p in pts)
            {
                mean += p;
            }

            mean /= pts.Count;
            // power iteration on the covariance
            float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (var p in pts)
            {
                Vector3 d = p - mean;
                xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z;
                yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z;
            }

            Vector3 a = Vector3.one.normalized;
            for (int it = 0; it < 50; it++)
            {
                var b = new Vector3(xx * a.x + xy * a.y + xz * a.z, xy * a.x + yy * a.y + yz * a.z, xz * a.x + yz * a.y + zz * a.z);
                if (b.sqrMagnitude < 1e-12f)
                {
                    break;
                }

                a = b.normalized;
            }

            axis = a;
            lo = float.MaxValue;
            hi = float.MinValue;
            foreach (var p in pts)
            {
                float s = Vector3.Dot(p, axis);
                lo = Mathf.Min(lo, s);
                hi = Mathf.Max(hi, s);
            }

            // how wide each end is (only meaningful with real vertices; bounds corners give equal widths)
            float len = hi - lo, wLo = 0f, wHi = 0f;
            int nLo = 0, nHi = 0;
            foreach (var p in pts)
            {
                float s = Vector3.Dot(p, axis);
                Vector3 across = p - axis * s;
                Vector3 centre = mean - axis * Vector3.Dot(mean, axis);
                float w = (across - centre).magnitude;
                if (s < lo + len * 0.2f)
                {
                    wLo = Mathf.Max(wLo, w);
                    nLo++;
                }
                else if (s > hi - len * 0.2f)
                {
                    wHi = Mathf.Max(wHi, w);
                    nHi++;
                }
            }

            bool headHigh;
            if (nLo > 8 && nHi > 8 && Mathf.Abs(wHi - wLo) > 0.15f * Mathf.Max(wHi, wLo))
            {
                headHigh = wHi > wLo;
            }
            else
            {
                headHigh = Mathf.Abs(hi) >= Mathf.Abs(lo);
            }

            if (!headHigh)
            {
                axis = -axis;
                float t = lo;
                lo = -hi;
                hi = -t;
            }

            return axis;
        }

        /// <summary>The shaft's centre (x, z) at a height of the staff (0 = bottom tip, 1 = top).</summary>
        private static Vector2 CenterAt(float y)
        {
            var rows = s_data != null ? s_data.centerLine : null;
            if (rows == null || rows.Length == 0)
            {
                return Vector2.zero;
            }

            for (int i = 1; i < rows.Length; i++)
            {
                if (rows[i] == null || rows[i].Length < 3 || rows[i - 1] == null || rows[i - 1].Length < 3)
                {
                    continue;
                }

                if (y <= rows[i][0])
                {
                    float t = Mathf.InverseLerp(rows[i - 1][0], rows[i][0], y);
                    return new Vector2(Mathf.Lerp(rows[i - 1][1], rows[i][1], t), Mathf.Lerp(rows[i - 1][2], rows[i][2], t));
                }
            }

            var last = rows[rows.Length - 1];
            return last != null && last.Length >= 3 ? new Vector2(last[1], last[2]) : Vector2.zero;
        }
    }
}
