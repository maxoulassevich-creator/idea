using System;
using UnityEngine;
using UnityEngine.Audio;

namespace FlyingPets
{
    /// <summary>
    ///     Visual and sound effects of the abilities, made from code (lines, particles, lights and
    ///     synthesized sounds) so they do not depend on the names of vanilla effect prefabs. Local only:
    ///     the pet sends an RPC and every game plays its own copy.
    /// </summary>
    internal static class Fx
    {
        private static readonly System.Collections.Generic.Dictionary<string, Material> s_mats =
            new System.Collections.Generic.Dictionary<string, Material>();
        private static Texture2D s_dot;
        private static Texture2D s_streak;
        private static Texture2D s_feather;
        private static AudioClip s_thunder;
        private static AudioClip s_chime;
        private static AudioClip s_whoosh;
        private static AudioMixerGroup s_sfx;
        private static bool s_sfxSearched;

        // ------------------------------------------------------------------ public effects
        /// <summary>Lightning from the sky, a shockwave along the ground, sparks, dust and thunder.</summary>
        public static void Thunder(Vector3 pos, float power, float radius)
        {
            var root = new GameObject("FP_Fx_Thunder");
            root.transform.position = pos;
            UnityEngine.Object.Destroy(root, 3.5f);

            var boltColor = new Color(0.78f, 0.88f, 1f, 1f);
            Bolt(root.transform, pos, 26f + 6f * power, boltColor, 0.42f, 0.32f);
            Ring(root.transform, pos, radius, new Color(0.62f, 0.8f, 1f, 1f), 0.5f, 0f, 1.1f);
            Ring(root.transform, pos, radius * 1.15f, new Color(0.9f, 0.85f, 0.7f, 0.55f), 0.8f, 0.08f, 2f);
            Flash(root.transform, pos + Vector3.up * 1.5f, new Color(0.7f, 0.82f, 1f), 26f, 5f + power, 0.6f);
            Sparks(root.transform, pos, new Color(0.75f, 0.88f, 1f, 1f), (int)(40 * power), 5f, 13f, 0.4f, 0.9f, 0.05f, 0.14f, 1.6f);
            Dust(root.transform, pos, radius);
            Sound(root, Clip(ref s_thunder, "FP_thunder", MakeThunder), 1f, 10f, 160f);

            var cam = GameCamera.instance;
            if (cam != null)
            {
                cam.AddShake(pos, 45f, Mathf.Min(2.5f, 0.8f + 0.6f * power), false);
            }
        }

        /// <summary>Hippocrene: a glowing spring that heals the local player while they stand in it.</summary>
        public static void Spring(Vector3 pos, float duration, float radius, float healPerSecond)
        {
            var go = new GameObject("FP_Fx_Spring");
            go.SetActive(false);
            go.transform.position = pos;
            var spring = go.AddComponent<FxSpring>();
            spring.Duration = Mathf.Max(1f, duration);
            spring.Radius = radius;
            spring.HealPerSecond = healPerSecond;
            go.SetActive(true);
            Sound(go, Clip(ref s_chime, "FP_chime", MakeChime), 0.55f, 4f, 40f);
        }

        /// <summary>A burst of feathers (the raven's grab).</summary>
        public static void Feathers(Vector3 pos, Color color, int count)
        {
            var root = new GameObject("FP_Fx_Feathers");
            root.transform.position = pos;
            UnityEngine.Object.Destroy(root, 3.5f);
            var ps = NewParticles(root.transform, pos, Feather(), false);
            var main = ps.main;
            main.duration = 0.2f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.5f);
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.07f, 0.11f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(0.22f, 0.36f);
            main.startSizeZ = new ParticleSystem.MinMaxCurve(1f, 1f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = color;
            main.gravityModifier = 0.12f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.6f;
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
            FadeOut(ps, color);
            ps.gameObject.SetActive(true);
            ps.Play();
            Sound(root, Clip(ref s_whoosh, "FP_whoosh", MakeWhoosh), 0.5f, 3f, 35f);
        }

        /// <summary>The valkyrie's catch: a golden flash and sparks where the pegasus appears.</summary>
        public static void Catch(Vector3 pos)
        {
            var root = new GameObject("FP_Fx_Catch");
            root.transform.position = pos;
            UnityEngine.Object.Destroy(root, 3f);
            Flash(root.transform, pos + Vector3.up, new Color(1f, 0.86f, 0.55f), 14f, 4f, 0.8f);
            Sparks(root.transform, pos + Vector3.up, new Color(1f, 0.85f, 0.45f, 1f), 50, 2f, 6f, 0.6f, 1.3f, 0.05f, 0.12f, -0.2f);
            Sound(root, Clip(ref s_whoosh, "FP_whoosh", MakeWhoosh), 0.6f, 3f, 35f);
        }

        // ------------------------------------------------------------------ building blocks
        private static Shader FindShader(bool additive)
        {
            var names = additive
                ? new[] { "Legacy Shaders/Particles/Additive", "Particles/Additive", "Sprites/Default" }
                : new[] { "Sprites/Default", "Legacy Shaders/Particles/Alpha Blended", "Particles/Alpha Blended" };
            foreach (string name in names)
            {
                var shader = Shader.Find(name);
                if (shader != null)
                {
                    return shader;
                }
            }

            return null;
        }

        /// <summary>
        ///     One shared material per texture and blending; colours come from the vertices (lines,
        ///     particles, discs). Additive for light (lightning, sparks, the spring), blended for dust and feathers.
        /// </summary>
        private static Material Mat(Texture2D tex, bool additive = true)
        {
            Material m;
            string key = tex.name + (additive ? "+" : "~");
            if (s_mats.TryGetValue(key, out m) && m != null)
            {
                return m;
            }

            var shader = FindShader(additive);
            m = shader != null ? new Material(shader) : new Material(Shader.Find("Sprites/Default"));
            m.name = "FP_Fx_" + key;
            m.mainTexture = tex;
            if (m.HasProperty("_TintColor"))
            {
                m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f)); // neutral for the legacy additive shader
            }

            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            s_mats[key] = m;
            return m;
        }

        /// <summary>A soft round dot (particles, the spring's pool).</summary>
        internal static Texture2D Dot()
        {
            if (s_dot == null)
            {
                s_dot = Procedural("dot", 32, 32, (u, v) =>
                {
                    float r = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f;
                    float a = Mathf.Clamp01(1f - r);
                    return a * a;
                });
            }

            return s_dot;
        }

        /// <summary>Bright in the middle of its width (lines).</summary>
        private static Texture2D Streak()
        {
            if (s_streak == null)
            {
                s_streak = Procedural("streak", 4, 32, (u, v) =>
                {
                    float a = Mathf.Clamp01(1f - Mathf.Abs(v - 0.5f) * 2f);
                    return Mathf.Pow(a, 1.5f);
                });
            }

            return s_streak;
        }

        private static Texture2D Feather()
        {
            if (s_feather == null)
            {
                s_feather = Procedural("feather", 16, 64, (u, v) =>
                {
                    float x = (u - 0.5f) * 2f;
                    float y = v * 2f - 1f;
                    float body = 1f - (x * x) / Mathf.Max(0.05f, 1f - y * y) - y * y * 0.2f;
                    float a = Mathf.Clamp01(body * 3f);
                    if (Mathf.Abs(x) < 0.08f && y < 0.9f)
                    {
                        a = Mathf.Max(a, 0.8f); // the quill
                    }

                    return a;
                });
            }

            return s_feather;
        }

        private static Texture2D Procedural(string name, int w, int h, Func<float, float, float> alpha)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "FP_" + name };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float a = Mathf.Clamp01(alpha((x + 0.5f) / w, (y + 0.5f) / h));
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            tex.SetPixels32(px);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply(false, true);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return tex;
        }

        private static LineRenderer Line(Transform parent, string name, Color color, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = Mat(Streak());
            lr.useWorldSpace = true;
            lr.startColor = color;
            lr.endColor = color;
            lr.widthMultiplier = width;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.textureMode = LineTextureMode.Stretch;
            return lr;
        }

        private static void Bolt(Transform parent, Vector3 ground, float height, Color color, float width, float life)
        {
            var go = new GameObject("Bolt");
            go.transform.SetParent(parent, false);
            var bolt = go.AddComponent<FxBolt>();
            Vector2 off = UnityEngine.Random.insideUnitCircle * 4f;
            bolt.Top = ground + new Vector3(off.x, height, off.y);
            bolt.Bottom = ground;
            bolt.Life = life;
            bolt.Core = Line(go.transform, "Core", color, width);
            bolt.Glow = Line(go.transform, "Glow", new Color(color.r * 0.6f, color.g * 0.75f, color.b, 0.35f), width * 4f);
            bolt.Branch = Line(go.transform, "Branch", color, width * 0.5f);
        }

        private static void Ring(Transform parent, Vector3 center, float radius, Color color, float life, float delay, float width)
        {
            var go = new GameObject("Ring");
            go.transform.SetParent(parent, false);
            var ring = go.AddComponent<FxRing>();
            ring.Center = center;
            ring.Radius = radius;
            ring.Life = life;
            ring.Delay = delay;
            ring.Color = color;
            ring.Width = width;
            ring.Line = Line(go.transform, "Line", new Color(color.r, color.g, color.b, 0f), width);
            ring.Line.loop = true;
        }

        private static void Flash(Transform parent, Vector3 pos, Color color, float range, float intensity, float life)
        {
            var go = new GameObject("Flash");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            var flash = go.AddComponent<FxFlash>();
            flash.Light = light;
            flash.Life = life;
            flash.Peak = intensity;
        }

        private static ParticleSystem NewParticles(Transform parent, Vector3 pos, Texture2D tex, bool additive = true)
        {
            var go = new GameObject("Particles");
            go.SetActive(false); // configure before it starts playing
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Mat(tex, additive);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        private static void FadeOut(ParticleSystem ps, Color color)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(color.a, 0f), new GradientAlphaKey(color.a * 0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
        }

        private static void Sparks(Transform parent, Vector3 pos, Color color, int count, float speedMin, float speedMax,
            float lifeMin, float lifeMax, float sizeMin, float sizeMax, float gravity)
        {
            var ps = NewParticles(parent, pos, Dot());
            var main = ps.main;
            main.duration = 0.1f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor = color;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Clamp(count, 1, 400)) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.4f;
            shape.rotation = new Vector3(-90f, 0f, 0f); // the dome points up
            FadeOut(ps, color);
            ps.gameObject.SetActive(true);
            ps.Play();
        }

        private static void Dust(Transform parent, Vector3 pos, float radius)
        {
            var color = new Color(0.55f, 0.48f, 0.4f, 0.45f);
            var ps = NewParticles(parent, pos + Vector3.up * 0.3f, Dot(), false);
            var main = ps.main;
            main.duration = 0.1f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(radius * 0.5f, radius * 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
            main.startColor = color;
            main.gravityModifier = -0.03f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 36) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.5f;
            shape.rotation = new Vector3(-90f, 0f, 0f); // flat on the ground, pushing outwards
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.12f;
            limit.limit = 0.5f;
            FadeOut(ps, color);
            ps.gameObject.SetActive(true);
            ps.Play();
        }

        internal static ParticleSystem Rising(Transform parent, Vector3 pos, float radius, Color color)
        {
            var ps = NewParticles(parent, pos, Dot());
            var main = ps.main;
            main.duration = 1f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.4f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            var emission = ps.emission;
            emission.rateOverTime = 30f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius * 0.9f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
            vel.y = new ParticleSystem.MinMaxCurve(0.5f, 1.4f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
            FadeOut(ps, color);
            ps.gameObject.SetActive(true);
            ps.Play();
            return ps;
        }

        /// <summary>A flat glowing disc lying on the ground.</summary>
        internal static MeshRenderer Disc(Transform parent, Vector3 pos, Vector3 normal, float radius, Color color)
        {
            var go = new GameObject("Disc");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal);
            const int n = 40;
            var v = new Vector3[n + 1];
            var uv = new Vector2[n + 1];
            var tri = new int[n * 3];
            v[0] = Vector3.zero;
            uv[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                v[i + 1] = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                uv[i + 1] = new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f);
                tri[i * 3] = 0;
                tri[i * 3 + 1] = i == n - 1 ? 1 : i + 2;
                tri[i * 3 + 2] = i + 1;
            }

            var colors = new Color[n + 1];
            for (int i = 0; i <= n; i++)
            {
                colors[i] = color;
            }

            var mesh = new Mesh { name = "FP_Disc", vertices = v, uv = uv, triangles = tri, colors = colors };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Mat(Dot());
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mr;
        }

        /// <summary>Recolours a disc made by <see cref="Disc" />.</summary>
        internal static void Tint(MeshRenderer disc, Color color)
        {
            var mf = disc != null ? disc.GetComponent<MeshFilter>() : null;
            var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null)
            {
                return;
            }

            var colors = new Color[mesh.vertexCount];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = color;
            }

            mesh.colors = colors;
        }

        // ------------------------------------------------------------------ sound
        private static void Sound(GameObject host, AudioClip clip, float volume, float minDistance, float maxDistance)
        {
            if (clip == null)
            {
                return;
            }

            try
            {
                var src = host.AddComponent<AudioSource>();
                src.clip = clip;
                src.volume = volume;
                src.spatialBlend = 1f;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = minDistance;
                src.maxDistance = maxDistance;
                src.dopplerLevel = 0f;
                src.outputAudioMixerGroup = SfxGroup();
                src.Play();
            }
            catch (Exception e)
            {
                FlyingPetsPlugin.Log.LogDebug("Sound failed: " + e.Message);
            }
        }

        /// <summary>The game's sound-effect mixer group, so the volume slider applies.</summary>
        private static AudioMixerGroup SfxGroup()
        {
            if (s_sfxSearched)
            {
                return s_sfx;
            }

            s_sfxSearched = true;
            try
            {
                var scene = ZNetScene.instance;
                if (scene != null && scene.m_prefabs != null)
                {
                    foreach (var prefab in scene.m_prefabs)
                    {
                        if (prefab == null || !prefab.name.StartsWith("sfx_"))
                        {
                            continue;
                        }

                        var src = prefab.GetComponentInChildren<AudioSource>(true);
                        if (src != null && src.outputAudioMixerGroup != null)
                        {
                            s_sfx = src.outputAudioMixerGroup;
                            break;
                        }
                    }
                }
            }
            catch (Exception)
            {
                s_sfx = null;
            }

            return s_sfx;
        }

        private static AudioClip Clip(ref AudioClip cache, string name, Func<float[]> make)
        {
            if (cache != null)
            {
                return cache;
            }

            try
            {
                float[] data = make();
                cache = AudioClip.Create(name, data.Length, 1, Rate, false);
                // SetData(float[], int) through reflection: its Span overload does not compile for .NET Framework
                var setData = typeof(AudioClip).GetMethod("SetData", new[] { typeof(float[]), typeof(int) });
                if (setData == null)
                {
                    throw new MissingMethodException("AudioClip.SetData(float[], int)");
                }

                setData.Invoke(cache, new object[] { data, 0 });
                cache.hideFlags = HideFlags.DontUnloadUnusedAsset;
            }
            catch (Exception e)
            {
                FlyingPetsPlugin.Log.LogDebug("Sound " + name + " could not be made: " + e.Message);
                cache = null;
            }

            return cache;
        }

        private const int Rate = 44100;

        /// <summary>A sharp crack, then a long rolling rumble.</summary>
        private static float[] MakeThunder()
        {
            int n = (int)(Rate * 3.2f);
            var d = new float[n];
            var rnd = new System.Random(7);
            float brown = 0f, lp = 0f, lp2 = 0f, hp = 0f, prev = 0f;
            float peak = 1e-6f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float w = (float)(rnd.NextDouble() * 2.0 - 1.0);
                brown = (brown + 0.03f * w) / 1.015f;
                lp += (brown - lp) * 0.05f;
                lp2 += (lp - lp2) * 0.05f;
                float swell = 0.7f + 0.3f * Mathf.Sin(t * 5.3f) * Mathf.Sin(t * 2.1f + 1.3f);
                float env = Mathf.Min(1f, t / 0.05f) * Mathf.Exp(-t * 1.05f) * swell;
                d[i] = lp2 * env;
                peak = Mathf.Max(peak, Mathf.Abs(d[i]));
            }

            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                d[i] = d[i] / peak * 0.8f;
                if (t < 0.25f)
                {
                    float w = (float)(rnd.NextDouble() * 2.0 - 1.0);
                    hp = 0.6f * (hp + w - prev);
                    prev = w;
                    d[i] += hp * Mathf.Exp(-t * 22f) * 0.9f;
                }

                d[i] = Mathf.Clamp(d[i], -1f, 1f);
            }

            return d;
        }

        /// <summary>Three soft bell notes (the spring).</summary>
        private static float[] MakeChime()
        {
            int n = (int)(Rate * 2.2f);
            var d = new float[n];
            float[] notes = { 1318.5f, 1661.2f, 1975.5f };
            for (int k = 0; k < notes.Length; k++)
            {
                float start = k * 0.12f;
                float f = notes[k];
                for (int i = (int)(start * Rate); i < n; i++)
                {
                    float t = (float)i / Rate - start;
                    float env = Mathf.Min(1f, t / 0.005f) * Mathf.Exp(-t * 2.6f);
                    float s = Mathf.Sin(2f * Mathf.PI * f * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * f * 2.01f * t) +
                              0.12f * Mathf.Sin(2f * Mathf.PI * f * 3.03f * t);
                    d[i] += s * env * 0.22f;
                }
            }

            return d;
        }

        /// <summary>A rush of air (wings).</summary>
        private static float[] MakeWhoosh()
        {
            int n = (int)(Rate * 0.6f);
            var d = new float[n];
            var rnd = new System.Random(3);
            float a = 0f, b = 0f, peak = 1e-6f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float w = (float)(rnd.NextDouble() * 2.0 - 1.0);
                float cut = Mathf.Lerp(0.02f, 0.18f, Mathf.Sin(Mathf.PI * t));
                a += (w - a) * cut;
                b += (a - b) * cut * 0.5f;
                float env = Mathf.Sin(Mathf.PI * t);
                d[i] = (a - b) * env * env;
                peak = Mathf.Max(peak, Mathf.Abs(d[i]));
            }

            for (int i = 0; i < n; i++)
            {
                d[i] = d[i] / peak * 0.7f;
            }

            return d;
        }
    }

    // ---------------------------------------------------------------------- effect behaviours
    internal sealed class FxBolt : MonoBehaviour
    {
        public Vector3 Top;
        public Vector3 Bottom;
        public float Life;
        public LineRenderer Core;
        public LineRenderer Glow;
        public LineRenderer Branch;
        private float m_t;
        private float m_next;
        private readonly Vector3[] m_pts = new Vector3[16];

        private void Update()
        {
            m_t += Time.deltaTime;
            if (m_t > Life)
            {
                Destroy(gameObject);
                return;
            }

            if (m_t >= m_next)
            {
                m_next = m_t + 0.045f;
                Shape();
            }

            float a = 1f - m_t / Life;
            float flicker = UnityEngine.Random.value < 0.25f ? 0.4f : 1f;
            SetAlpha(Core, a * flicker);
            SetAlpha(Glow, a * 0.35f * flicker);
            SetAlpha(Branch, a * 0.8f * flicker);
        }

        private static void SetAlpha(LineRenderer lr, float a)
        {
            var c = lr.startColor;
            c.a = a;
            lr.startColor = c;
            lr.endColor = c;
        }

        private void Shape()
        {
            Vector3 axis = Bottom - Top;
            Vector3 side = Vector3.Cross(axis, Vector3.up).normalized;
            if (side.sqrMagnitude < 0.5f)
            {
                side = Vector3.right;
            }

            Vector3 side2 = Vector3.Cross(axis.normalized, side);
            int n = m_pts.Length;
            for (int i = 0; i < n; i++)
            {
                float t = i / (n - 1f);
                float amp = Mathf.Sin(Mathf.PI * t) * 2.2f + 0.2f;
                m_pts[i] = Top + axis * t + side * (UnityEngine.Random.Range(-1f, 1f) * amp) +
                           side2 * (UnityEngine.Random.Range(-1f, 1f) * amp);
            }

            m_pts[n - 1] = Bottom;
            Core.positionCount = n;
            Core.SetPositions(m_pts);
            Glow.positionCount = n;
            Glow.SetPositions(m_pts);

            int from = UnityEngine.Random.Range(n / 4, n / 2);
            Vector3 start = m_pts[from];
            Vector3 dir = (axis.normalized + side * UnityEngine.Random.Range(-0.9f, 0.9f)).normalized;
            var br = new Vector3[6];
            for (int i = 0; i < br.Length; i++)
            {
                br[i] = start + dir * (i * axis.magnitude * 0.06f) + UnityEngine.Random.insideUnitSphere * 0.8f;
            }

            br[0] = start;
            Branch.positionCount = br.Length;
            Branch.SetPositions(br);
        }
    }

    internal sealed class FxRing : MonoBehaviour
    {
        public Vector3 Center;
        public float Radius;
        public float Life;
        public float Delay;
        public Color Color;
        public float Width;
        public LineRenderer Line;
        private float m_t;
        private readonly Vector3[] m_pts = new Vector3[56];

        private void Update()
        {
            m_t += Time.deltaTime;
            float t = (m_t - Delay) / Life;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            if (t < 0f)
            {
                return;
            }

            float r = Mathf.Max(0.3f, Radius * (1f - Mathf.Pow(1f - t, 3f)));
            for (int i = 0; i < m_pts.Length; i++)
            {
                float a = i * Mathf.PI * 2f / m_pts.Length;
                Vector3 p = Center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                float water;
                float g = Util.GroundY(p, 3f, out water);
                p.y = Mathf.Max(g, water, Center.y - 3f) + 0.15f;
                m_pts[i] = p;
            }

            Line.positionCount = m_pts.Length;
            Line.SetPositions(m_pts);
            Line.widthMultiplier = Width * (1f - t * 0.8f);
            var c = Color;
            c.a = Color.a * (1f - t);
            Line.startColor = c;
            Line.endColor = c;
        }
    }

    internal sealed class FxFlash : MonoBehaviour
    {
        public Light Light;
        public float Life;
        public float Peak;
        private float m_t;

        private void Update()
        {
            m_t += Time.deltaTime;
            if (m_t >= Life)
            {
                Destroy(gameObject);
                return;
            }

            float k = 1f - m_t / Life;
            Light.intensity = Peak * k * k;
        }
    }

    /// <summary>Hippocrene, the spring struck by the hoof: glows for a while and heals whoever stands in it.</summary>
    internal sealed class FxSpring : MonoBehaviour
    {
        public float Duration = 10f;
        public float Radius = 3.5f;
        public float HealPerSecond = 8f;
        private float m_t;
        private float m_tick;
        private MeshRenderer m_pool;
        private MeshRenderer m_core;
        private ParticleSystem m_motes;
        private Light m_light;
        private Color m_poolColor = new Color(0.35f, 0.8f, 1f, 0.55f);
        private Color m_coreColor = new Color(0.75f, 0.95f, 1f, 0.6f);

        private void Start()
        {
            Vector3 p = transform.position;
            Vector3 normal = Vector3.up;
            RaycastHit hit;
            if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out hit, 6f, Util.SolidMask, QueryTriggerInteraction.Ignore))
            {
                p = hit.point;
                normal = Vector3.Slerp(Vector3.up, hit.normal, 0.7f);
            }

            m_pool = Fx.Disc(transform, p + normal * 0.06f, normal, Radius, m_poolColor);
            m_core = Fx.Disc(transform, p + normal * 0.08f, normal, Radius * 0.45f, m_coreColor);
            m_motes = Fx.Rising(transform, p + normal * 0.1f, Radius, new Color(0.6f, 0.95f, 1f, 0.9f));
            var lg = new GameObject("Light");
            lg.transform.SetParent(transform, false);
            lg.transform.position = p + Vector3.up * 1.2f;
            m_light = lg.AddComponent<Light>();
            m_light.type = LightType.Point;
            m_light.color = new Color(0.45f, 0.9f, 1f);
            m_light.range = Radius * 2.2f;
            m_light.shadows = LightShadows.None;
        }

        private void OnDestroy()
        {
            foreach (var mr in new[] { m_pool, m_core })
            {
                var mf = mr != null ? mr.GetComponent<MeshFilter>() : null;
                if (mf != null && mf.sharedMesh != null)
                {
                    Destroy(mf.sharedMesh);
                }
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            m_t += dt;
            if (m_t > Duration)
            {
                Destroy(gameObject);
                return;
            }

            float fade = Mathf.Min(Mathf.Clamp01(m_t / 0.4f), Mathf.Clamp01((Duration - m_t) / 1.2f));
            float pulse = 0.85f + 0.15f * Mathf.Sin(m_t * 3.1f);
            if (m_pool != null)
            {
                var c = m_poolColor;
                c.a *= fade * pulse;
                Fx.Tint(m_pool, c);
                float s = 0.92f + 0.08f * Mathf.Sin(m_t * 1.7f);
                m_pool.transform.localScale = new Vector3(s, 1f, s);
            }

            if (m_core != null)
            {
                var c = m_coreColor;
                c.a *= fade * (0.7f + 0.3f * Mathf.Sin(m_t * 4.3f + 1f));
                Fx.Tint(m_core, c);
            }

            if (m_light != null)
            {
                m_light.intensity = 1.6f * fade * pulse;
            }

            if (m_motes != null && Duration - m_t < 1.2f)
            {
                var em = m_motes.emission;
                em.enabled = false;
            }

            // each game heals its own player
            m_tick -= dt;
            if (m_tick > 0f || HealPerSecond <= 0f)
            {
                return;
            }

            m_tick = 0.5f;
            var player = Player.m_localPlayer;
            if (player == null || player.IsDead())
            {
                return;
            }

            Vector3 d = player.transform.position - transform.position;
            if (Mathf.Abs(d.y) < 3f && d.x * d.x + d.z * d.z <= Radius * Radius &&
                player.GetHealth() < player.GetMaxHealth())
            {
                player.Heal(HealPerSecond * 0.5f, true);
            }
        }
    }
}
