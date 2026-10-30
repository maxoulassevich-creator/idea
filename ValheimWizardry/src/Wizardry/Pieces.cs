using System;
using System.Collections.Generic;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace Wizardry
{
    /// <summary>The wizard's table with the Codex of Eternal Shadows on it, its six extensions, and the enchantment anchor.</summary>
    internal static class Pieces
    {
        public const string CodexName = "WZ_Codex";
        public const string AnchorName = "WZ_Anchor";
        public static readonly List<CraftingStation> Codexes = new List<CraftingStation>();

        public static void Register()
        {
            try
            {
                Codex();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("The Codex could not be registered: " + e);
            }

            var ext = new[]
            {
                new object[] { "stand", "$wz_ext_stand", new[] { new[] { "FineWood", "6" }, new[] { "Bronze", "2" } } },
                new object[] { "cauldron", "$wz_ext_cauldron", new[] { new[] { "Iron", "4" }, new[] { "Stone", "6" } } },
                new object[] { "ball", "$wz_ext_ball", new[] { new[] { "Crystal", "4" }, new[] { "Silver", "2" } } },
                new object[] { "pensieve", "$wz_ext_pensieve", new[] { new[] { "Silver", "6" }, new[] { "Stone", "10" }, new[] { "BlackMetal", "2" } } },
                new object[] { "astrolabe", "$wz_ext_astrolabe", new[] { new[] { "BlackCore", "1" }, new[] { "Eitr", "BlackMetal", "4" }, new[] { "Copper", "6" } } },
                new object[] { "turner", "$wz_ext_turner", new[] { new[] { "FlametalNew", "Flametal", "BlackMetal", "4" }, new[] { "Crystal", "4" }, new[] { "Silver", "4" } } },
            };
            foreach (var e in ext)
            {
                try
                {
                    Extension((string)e[0], (string)e[1], (string[][])e[2]);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError("Extension " + e[0] + " could not be registered: " + ex.Message);
                }
            }

            try
            {
                RegisterAnchor();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("The anchor prefab could not be registered: " + e);
            }
        }

        // ------------------------------------------------------------------ the Codex
        private static void Codex()
        {
            var cfg = new PieceConfig
            {
                Name = "$wz_codex",
                Description = "$wz_codex_desc",
                PieceTable = "Hammer",
                Category = "Crafting",
                Icon = Icons.Make("codex", Icons.Shape.Book, new Color(0.25f, 0.9f, 0.45f)),
                Requirements = Wands.Requirements(new[] { new[] { "Wood", "10" }, new[] { "Stone", "6" }, new[] { "Feathers", "3" }, new[] { "Resin", "5" } }),
            };
            var piece = new CustomPiece(CodexName, "piece_workbench", cfg);
            var prefab = piece.PiecePrefab;
            var station = prefab.GetComponent<CraftingStation>();
            if (station != null)
            {
                station.m_name = "$wz_codex";
                station.m_icon = cfg.Icon;
                station.m_craftRequireRoof = false;
                station.m_discoverRange = 6f;
                station.m_rangeBuild = 0f;
            }

            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = false;
            }

            var visual = new GameObject("WZ_CodexVisual");
            visual.transform.SetParent(prefab.transform, false);
            float top = TableVisual(visual.transform);
            var book = new GameObject("Codex");
            book.transform.SetParent(visual.transform, false);
            book.transform.localPosition = new Vector3(0f, top, 0f);
            book.transform.localScale = Vector3.one * 0.9f;
            var mat = Meshes.Material("codex_albedo.png", null, "codex_emission.png", Color.white, new Color(0.6f, 1.6f, 0.8f));
            Meshes.Model(book.transform, "codex_base.wzm", mat, "Base");
            var crystals = Meshes.Model(book.transform, "codex_crystals.wzm", mat, "Crystals");
            var anim = visual.AddComponent<CodexAnim>();
            anim.Crystals = crystals.transform;
            var green = new GameObject("Glow");
            green.transform.SetParent(book.transform, false);
            green.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            anim.Glow = green.AddComponent<Light>();
            anim.Glow.type = LightType.Point;
            anim.Glow.color = new Color(0.35f, 1f, 0.5f);
            anim.Glow.range = 4f;
            anim.Glow.intensity = 1.2f;
            anim.Glow.shadows = LightShadows.None;
            foreach (var tip in Candles())
            {
                var c = new GameObject("Candle");
                c.transform.SetParent(book.transform, false);
                c.transform.localPosition = tip + Vector3.up * 0.02f;
                anim.Flames.Add(Flame(c.transform));
            }

            var flameLight = new GameObject("CandleLight");
            flameLight.transform.SetParent(book.transform, false);
            flameLight.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            anim.Candle = flameLight.AddComponent<Light>();
            anim.Candle.type = LightType.Point;
            anim.Candle.color = new Color(1f, 0.75f, 0.4f);
            anim.Candle.range = 3f;
            anim.Candle.intensity = 0.8f;
            anim.Candle.shadows = LightShadows.None;
            visual.AddComponent<CodexRegistry>();
            PieceManager.Instance.AddPiece(piece);
        }

        /// <summary>A vanilla table under the Codex; returns the height of its top.</summary>
        private static float TableVisual(Transform parent)
        {
            foreach (string name in new[] { "piece_table_round", "piece_table", "piece_table_oak" })
            {
                var src = PrefabManager.Instance.GetPrefab(name);
                if (src == null)
                {
                    continue;
                }

                var copy = new GameObject("Table");
                copy.transform.SetParent(parent, false);
                var b = new Bounds();
                bool any = false;
                foreach (var mf in src.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || mf.sharedMesh == null || !mr.enabled || mf.gameObject.name.ToLowerInvariant().Contains("lod1") ||
                        mf.gameObject.name.ToLowerInvariant().Contains("lod2"))
                    {
                        continue;
                    }

                    var go = new GameObject(mf.name);
                    go.transform.SetParent(copy.transform, false);
                    Matrix4x4 m = src.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    go.transform.localPosition = m.GetColumn(3);
                    go.transform.localRotation = m.rotation;
                    go.transform.localScale = m.lossyScale;
                    go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                    go.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
                    var mb = mf.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var p = m.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? mb.min.x : mb.max.x, (i & 2) == 0 ? mb.min.y : mb.max.y,
                            (i & 4) == 0 ? mb.min.z : mb.max.z));
                        if (!any)
                        {
                            b = new Bounds(p, Vector3.zero);
                            any = true;
                        }
                        else
                        {
                            b.Encapsulate(p);
                        }
                    }
                }

                if (any)
                {
                    return b.max.y;
                }
            }

            // no vanilla table found: a simple stone pedestal
            var ped = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            UnityEngine.Object.Destroy(ped.GetComponent<Collider>());
            ped.transform.SetParent(parent, false);
            ped.transform.localScale = new Vector3(1f, 0.45f, 1f);
            ped.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            ped.GetComponent<Renderer>().sharedMaterial = VanillaMaterial("stone_wall_1x1", "Stone");
            return 0.9f;
        }

        private static List<Vector3> Candles()
        {
            var list = new List<Vector3>();
            var data = Meshes.Resource("codex.json");
            if (data == null)
            {
                return list;
            }

            string text = System.Text.Encoding.UTF8.GetString(data);
            int open = text.IndexOf("[[", StringComparison.Ordinal);
            if (open < 0)
            {
                return list;
            }

            foreach (string chunk in text.Substring(open + 1).Split(']'))
            {
                var nums = chunk.Trim(' ', ',', '[', '}', '\n').Split(',');
                float x, y, z;
                if (nums.Length == 3 && float.TryParse(nums[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x) &&
                    float.TryParse(nums[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y) &&
                    float.TryParse(nums[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out z))
                {
                    list.Add(new Vector3(x, y, z));
                }
            }

            return list;
        }

        private static ParticleSystem Flame(Transform parent)
        {
            var c = new Color(1f, 0.75f, 0.35f, 1f);
            var ps = Fx.NewParticles(parent, parent.position, Fx.Dot());
            ps.transform.localPosition = Vector3.zero;
            var main = ps.main;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.05f);
            main.startColor = c;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.playOnAwake = true;
            var em = ps.emission;
            em.rateOverTime = 25f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.004f;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(0.08f, 0.15f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            Fx.FadeOut(ps, c);
            ps.gameObject.SetActive(true);
            return ps;
        }

        public static Material VanillaMaterial(string prefab, string fallbackPrefab)
        {
            foreach (string name in new[] { prefab, fallbackPrefab, "stone_wall_1x1", "wood_floor" })
            {
                var go = PrefabManager.Instance.GetPrefab(name);
                if (go == null)
                {
                    continue;
                }

                foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r.sharedMaterial != null && r.sharedMaterial.renderQueue < 2450)
                    {
                        return r.sharedMaterial;
                    }
                }
            }

            return new Material(Shader.Find("Standard"));
        }

        // ------------------------------------------------------------------ extensions
        private static void Extension(string key, string token, string[][] recipe)
        {
            string basePrefab = Wands.FindPrefab("piece_workbench_ext1", "piece_workbench_ext2", "piece_workbench_ext3");
            if (basePrefab == null)
            {
                return;
            }

            var cfg = new PieceConfig
            {
                Name = token,
                Description = "$wz_ext_desc",
                PieceTable = "Hammer",
                Category = "Crafting",
                ExtendStation = CodexName,
                Icon = Icons.Make("ext_" + key, Icons.Shape.Orb, new Color(0.4f, 0.9f, 0.6f)),
                Requirements = Wands.Requirements(recipe),
            };
            var piece = new CustomPiece("WZ_Ext_" + key, basePrefab, cfg);
            var prefab = piece.PiecePrefab;
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = false;
            }

            var v = new GameObject("WZ_ExtVisual");
            v.transform.SetParent(prefab.transform, false);
            var stone = VanillaMaterial("stone_wall_1x1", "Stone");
            var wood = VanillaMaterial("wood_beam", "wood_floor");
            var metal = VanillaMaterial("iron_wall_1x1", "piece_workbench");
            Color glowCol = new Color(0.4f, 1f, 0.6f);
            switch (key)
            {
                case "stand":
                    Prim(v.transform, PrimitiveType.Cube, wood, new Vector3(0f, 0.05f, 0f), new Vector3(0.6f, 0.1f, 0.3f));
                    for (int i = 0; i < 5; i++)
                    {
                        Prim(v.transform, PrimitiveType.Cylinder, wood, new Vector3(-0.24f + 0.12f * i, 0.3f, 0f), new Vector3(0.025f, 0.2f, 0.025f));
                    }

                    break;
                case "cauldron":
                    Prim(v.transform, PrimitiveType.Sphere, metal, new Vector3(0f, 0.4f, 0f), new Vector3(0.8f, 0.6f, 0.8f));
                    Glow(v.transform, new Vector3(0f, 0.68f, 0f), new Color(0.3f, 1f, 0.3f), 0.32f);
                    break;
                case "ball":
                    Prim(v.transform, PrimitiveType.Cylinder, wood, new Vector3(0f, 0.25f, 0f), new Vector3(0.25f, 0.25f, 0.25f));
                    Glow(v.transform, new Vector3(0f, 0.68f, 0f), new Color(0.6f, 0.8f, 1f), 0.38f);
                    glowCol = new Color(0.6f, 0.8f, 1f);
                    break;
                case "pensieve":
                    Prim(v.transform, PrimitiveType.Cylinder, stone, new Vector3(0f, 0.35f, 0f), new Vector3(0.9f, 0.35f, 0.9f));
                    Glow(v.transform, new Vector3(0f, 0.72f, 0f), new Color(0.85f, 0.9f, 1f), 0.7f);
                    glowCol = new Color(0.85f, 0.9f, 1f);
                    break;
                case "astrolabe":
                    Prim(v.transform, PrimitiveType.Cylinder, metal, new Vector3(0f, 0.4f, 0f), new Vector3(0.06f, 0.4f, 0.06f));
                    for (int i = 0; i < 3; i++)
                    {
                        var ring = Prim(v.transform, PrimitiveType.Cylinder, metal, new Vector3(0f, 0.95f, 0f), new Vector3(0.7f - 0.15f * i, 0.01f, 0.7f - 0.15f * i));
                        ring.transform.localRotation = Quaternion.Euler(60f * i, 30f * i, 0f);
                        ring.AddComponent<Spin>().Speed = 15f + 10f * i;
                    }

                    break;
                default:
                    Prim(v.transform, PrimitiveType.Cylinder, metal, new Vector3(0f, 0.3f, 0f), new Vector3(0.08f, 0.3f, 0.08f));
                    var glass = Prim(v.transform, PrimitiveType.Sphere, metal, new Vector3(0f, 0.85f, 0f), new Vector3(0.3f, 0.5f, 0.3f));
                    glass.AddComponent<Spin>().Speed = 40f;
                    Glow(v.transform, new Vector3(0f, 0.85f, 0f), new Color(1f, 0.85f, 0.4f), 0.2f);
                    glowCol = new Color(1f, 0.85f, 0.4f);
                    break;
            }

            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(v.transform, false);
            lightGo.transform.localPosition = Vector3.up * 0.9f;
            var l = lightGo.AddComponent<Light>();
            l.color = glowCol;
            l.range = 3f;
            l.intensity = 0.8f;
            l.shadows = LightShadows.None;
            PieceManager.Instance.AddPiece(piece);
        }

        public static GameObject Prim(Transform parent, PrimitiveType type, Material mat, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        private static void Glow(Transform parent, Vector3 pos, Color c, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = Vector3.one * size;
            var m = new Material(Fx.Mat(Fx.Dot(), true));
            m.color = c;
            m.mainTexture = Fx.Ring();
            go.GetComponent<Renderer>().sharedMaterial = m;
        }

        // ------------------------------------------------------------------ the anchor of zones, domes, walls, bridges
        private static void RegisterAnchor()
        {
            var go = PrefabManager.Instance.CreateEmptyPrefab(AnchorName, true);
            var nv = go.GetComponent<ZNetView>();
            nv.m_persistent = true;
            nv.m_type = ZDO.ObjectType.Default;
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
            {
                UnityEngine.Object.Destroy(c);
            }

            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                UnityEngine.Object.Destroy(r);
            }

            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                UnityEngine.Object.Destroy(mf);
            }

            go.AddComponent<Anchor>();
            PrefabManager.Instance.AddPrefab(go);
        }

        public static CraftingStation Nearest(Vector3 pos, float range)
        {
            CraftingStation best = null;
            float bd = range;
            foreach (var s in Codexes)
            {
                if (s == null)
                {
                    continue;
                }

                float d = Vector3.Distance(s.transform.position, pos);
                if (d <= bd)
                {
                    bd = d;
                    best = s;
                }
            }

            return best;
        }

        public static int Level(CraftingStation s)
        {
            return s != null ? s.GetLevel() : 0;
        }
    }

    /// <summary>The Codex comes alive: crystals float and turn, the green glow breathes, candles flicker.</summary>
    internal sealed class CodexAnim : MonoBehaviour
    {
        public Transform Crystals;
        public Light Glow, Candle;
        public readonly List<ParticleSystem> Flames = new List<ParticleSystem>();
        public float Excite;
        private float m_t;

        private void Update()
        {
            m_t += Time.deltaTime;
            Excite = Mathf.Max(0f, Excite - Time.deltaTime * 0.5f);
            if (Crystals != null)
            {
                Crystals.localRotation = Quaternion.Euler(0f, m_t * (12f + 60f * Excite), 0f);
                Crystals.localPosition = new Vector3(0f, 0.015f * Mathf.Sin(m_t * 1.3f) + 0.05f * Excite, 0f);
            }

            if (Glow != null)
            {
                Glow.intensity = 1.1f + 0.35f * Mathf.Sin(m_t * 1.7f) + 2.5f * Excite;
            }

            if (Candle != null)
            {
                Candle.intensity = 0.7f + 0.15f * Mathf.PerlinNoise(m_t * 6f, 0.5f);
            }
        }
    }

    /// <summary>Keeps the list of Codexes in the world (for the K key and "near the Codex" checks).</summary>
    internal sealed class CodexRegistry : MonoBehaviour
    {
        private CraftingStation m_station;

        private void Start()
        {
            m_station = GetComponentInParent<CraftingStation>();
            if (m_station != null && GetComponentInParent<ZNetView>() != null && GetComponentInParent<ZNetView>().GetZDO() != null)
            {
                Pieces.Codexes.Add(m_station);
            }
        }

        private void OnDestroy()
        {
            Pieces.Codexes.Remove(m_station);
        }
    }

    internal sealed class Spin : MonoBehaviour
    {
        public float Speed = 20f;

        private void Update()
        {
            transform.Rotate(Vector3.up, Speed * Time.deltaTime, Space.Self);
        }
    }
}
