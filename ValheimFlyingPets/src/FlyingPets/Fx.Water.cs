using System;
using UnityEngine;

namespace FlyingPets
{
    /// <summary>The water dragon's effects: the tidal breath and splashes.</summary>
    internal static partial class Fx
    {
        private static AudioClip s_rush;
        private static AudioClip s_splash;

        private static readonly Color WaterColor = new Color(0.72f, 0.88f, 1f, 0.85f);
        private static readonly Color MistColor = new Color(0.86f, 0.94f, 1f, 0.22f);

        /// <summary>
        ///     A jet of water pouring from the jaws for <paramref name="duration" /> seconds. It hangs on the head
        ///     bone (local mouth point and direction), so it follows the head; where it lands, the water splashes.
        /// </summary>
        public static void Breath(Transform head, Vector3 localMouth, Vector3 localDir, float duration, float range, float scale)
        {
            var root = new GameObject("FP_Fx_Breath");
            root.SetActive(false);
            root.transform.SetParent(head, false);
            root.transform.localPosition = localMouth;
            root.transform.localRotation = Quaternion.LookRotation(localDir.sqrMagnitude > 1e-6f ? localDir : Vector3.forward);

            float speed = Mathf.Clamp(range / 0.7f, 8f, 40f);
            var jet = NewParticles(root.transform, root.transform.position, Dot(), false);
            jet.transform.localPosition = Vector3.zero;
            jet.transform.localRotation = Quaternion.identity;
            var main = jet.main;
            main.duration = duration;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.75f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.85f, speed * 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f * scale, 0.65f * scale);
            main.startColor = WaterColor;
            main.gravityModifier = 0.6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            var emission = jet.emission;
            emission.rateOverTime = 170f;
            var shape = jet.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 7f;
            shape.radius = 0.12f * scale;
            Grow(jet, 0.6f, 1.9f);
            FadeOut(jet, WaterColor);

            var mist = NewParticles(root.transform, root.transform.position, Dot(), false);
            mist.transform.localPosition = Vector3.zero;
            mist.transform.localRotation = Quaternion.identity;
            main = mist.main;
            main.duration = duration;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.45f, speed * 0.7f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.1f * scale, 2.2f * scale);
            main.startColor = MistColor;
            main.gravityModifier = 0.05f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            emission = mist.emission;
            emission.rateOverTime = 45f;
            shape = mist.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f;
            shape.radius = 0.2f * scale;
            Grow(mist, 0.7f, 2.6f);
            FadeOut(mist, MistColor);

            var drops = NewSplashParticles(root.transform, scale);

            var fx = root.AddComponent<FxBreath>();
            fx.Duration = duration;
            fx.Range = range;
            fx.Scale = scale;
            fx.Systems = new[] { jet, mist };
            fx.Splash = drops;
            root.SetActive(true);
            jet.gameObject.SetActive(true);
            mist.gameObject.SetActive(true);
            drops.gameObject.SetActive(true);
            jet.Play();
            mist.Play();
            Sound(root, Clip(ref s_rush, "FP_rush", MakeRush), 0.9f, 5f, 60f);
        }

        /// <summary>A splash on the water (a fish snatched up).</summary>
        public static void Splash(Vector3 pos, float size)
        {
            var root = new GameObject("FP_Fx_Splash");
            root.transform.position = pos;
            UnityEngine.Object.Destroy(root, 3f);
            Ring(root.transform, pos + Vector3.up * 0.05f, 1.6f * size, new Color(0.75f, 0.9f, 1f, 0.8f), 0.8f, 0f, 0.35f);
            Sparks(root.transform, pos, WaterColor, (int)(45 * size), 2.5f, 6.5f, 0.5f, 1f, 0.08f, 0.22f, 1.4f);
            Sound(root, Clip(ref s_splash, "FP_splash", MakeSplash), 0.7f, 3f, 40f);
        }

        /// <summary>World-space droplets that the jet throws up where it lands (emitted by FxBreath).</summary>
        private static ParticleSystem NewSplashParticles(Transform parent, float scale)
        {
            var ps = NewParticles(parent, parent.position, Dot(), false);
            var main = ps.main;
            main.duration = 1f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f * scale, 6f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f * scale, 0.35f * scale);
            main.startColor = WaterColor;
            main.gravityModifier = 1.3f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.6f * scale;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            FadeOut(ps, WaterColor);
            return ps;
        }

        private static void Grow(ParticleSystem ps, float from, float to)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, from), new Keyframe(1f, to)));
        }

        /// <summary>Pouring water: a gush, a steady roar that flutters, and a fall-off.</summary>
        private static float[] MakeRush()
        {
            int n = (int)(Rate * 2.3f);
            var d = new float[n];
            var rnd = new System.Random(11);
            float lp1 = 0f, lp2 = 0f, slow = 0f, peak = 1e-6f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float w = (float)(rnd.NextDouble() * 2.0 - 1.0);
                lp1 += (w - lp1) * 0.35f;      // pink-ish hiss
                lp2 += (lp1 - lp2) * 0.08f;    // body of the roar
                slow += ((float)(rnd.NextDouble() * 2.0 - 1.0) - slow) * 0.0015f;
                float flutter = 0.75f + 0.25f * Mathf.Sin(t * 37f + 3f * slow) * Mathf.Sin(t * 11.3f);
                float env = Mathf.Min(1f, t / 0.06f) * (t < 1.9f ? 1f : Mathf.Max(0f, 1f - (t - 1.9f) / 0.4f));
                float gush = 1f + 0.8f * Mathf.Exp(-t * 6f);
                d[i] = ((lp1 - lp2) * 0.55f + lp2 * 1.6f) * flutter * env * gush;
                peak = Mathf.Max(peak, Mathf.Abs(d[i]));
            }

            for (int i = 0; i < n; i++)
            {
                d[i] = d[i] / peak * 0.75f;
            }

            return d;
        }

        /// <summary>A short slap of water.</summary>
        private static float[] MakeSplash()
        {
            int n = (int)(Rate * 0.7f);
            var d = new float[n];
            var rnd = new System.Random(5);
            float lp = 0f, hp = 0f, prev = 0f, peak = 1e-6f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float w = (float)(rnd.NextDouble() * 2.0 - 1.0);
                lp += (w - lp) * 0.2f;
                hp = 0.7f * (hp + lp - prev);
                prev = lp;
                float bubbles = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * (300f + 900f * t) * t);
                d[i] = (lp * 0.6f + hp) * Mathf.Exp(-t * 7f) * Mathf.Min(1f, t / 0.004f) * bubbles;
                peak = Mathf.Max(peak, Mathf.Abs(d[i]));
            }

            for (int i = 0; i < n; i++)
            {
                d[i] = d[i] / peak * 0.7f;
            }

            return d;
        }
    }

    /// <summary>Runs the jet: stops pouring after its time, splashes where the water lands.</summary>
    internal sealed class FxBreath : MonoBehaviour
    {
        public float Duration = 2f;
        public float Range = 16f;
        public float Scale = 1f;
        public ParticleSystem[] Systems;
        public ParticleSystem Splash;

        private float m_t;
        private float m_splashTimer;
        private bool m_stopped;
        private static int s_mask = -1;

        private void Update()
        {
            m_t += Time.deltaTime;
            if (!m_stopped && (m_t >= Duration || transform.parent == null))
            {
                m_stopped = true;
                foreach (var ps in Systems)
                {
                    if (ps != null)
                    {
                        ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                    }
                }

                // let the last drops fly on their own
                transform.SetParent(null, true);
                Destroy(gameObject, 1.6f);
                return;
            }

            if (m_stopped || m_t < 0.1f)
            {
                return;
            }

            m_splashTimer -= Time.deltaTime;
            if (m_splashTimer > 0f || Splash == null)
            {
                return;
            }

            m_splashTimer = 0.1f;
            Vector3 p;
            if (Landing(out p))
            {
                var e = new ParticleSystem.EmitParams { position = p, applyShapeToPosition = true };
                Splash.Emit(e, 9);
            }
        }

        /// <summary>Where the jet meets the ground or the sea, within its reach.</summary>
        private bool Landing(out Vector3 point)
        {
            if (s_mask == -1)
            {
                s_mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
            }

            Vector3 o = transform.position;
            Vector3 d = transform.forward;
            float best = Range;
            point = Vector3.zero;
            bool found = false;
            RaycastHit hit;
            if (Physics.Raycast(o, d, out hit, Range, s_mask, QueryTriggerInteraction.Ignore))
            {
                best = hit.distance;
                point = hit.point;
                found = true;
            }

            var zs = ZoneSystem.instance;
            if (zs != null && d.y < -0.02f)
            {
                float t = (zs.m_waterLevel - o.y) / d.y;
                if (t > 0f && t < best)
                {
                    point = o + d * t;
                    found = true;
                }
            }

            return found;
        }
    }
}
