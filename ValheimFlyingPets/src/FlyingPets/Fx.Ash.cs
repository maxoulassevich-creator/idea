using UnityEngine;

namespace FlyingPets
{
    /// <summary>The ash vulture's effects: the ember rain and the wisp of its death's tithe.</summary>
    internal static partial class Fx
    {
        private static AudioClip s_crackle;

        private static readonly Color EmberHot = new Color(1.0f, 0.78f, 0.30f, 1.0f);
        private static readonly Color EmberDeep = new Color(1.0f, 0.36f, 0.08f, 1.0f);
        private static readonly Color AshColor = new Color(0.20f, 0.19f, 0.18f, 0.85f);
        private static readonly Color SmokeColor = new Color(0.13f, 0.12f, 0.11f, 0.38f);

        /// <summary>
        ///     Burning ash shaken from the vulture's wings for <paramref name="duration" /> seconds: glowing embers
        ///     and grey flakes fall from under it (left behind as it flies on), a dark cloud hangs under its belly,
        ///     an orange glow flickers below, and the fire crackles.
        /// </summary>
        public static void EmberRain(Transform pet, float duration, float radius, float scale)
        {
            var root = new GameObject("FP_Fx_EmberRain");
            root.SetActive(false);
            root.transform.SetParent(pet, false);
            root.transform.localPosition = Vector3.up * (1.3f * scale);
            root.transform.localRotation = Quaternion.identity;

            var embers = NewParticles(root.transform, root.transform.position, Dot());
            embers.transform.localPosition = Vector3.zero;
            var main = embers.main;
            main.duration = duration;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f * scale, 0.2f * scale);
            main.startColor = new ParticleSystem.MinMaxGradient(EmberHot, EmberDeep);
            main.gravityModifier = 0.55f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1400;
            var emission = embers.emission;
            emission.rateOverTime = 340f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 80) });
            var shape = embers.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius * 0.75f;
            shape.radiusThickness = 1f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            Cooling(embers);
            Flutter(embers, 0.7f);

            var ash = NewParticles(root.transform, root.transform.position, Dot(), false);
            ash.transform.localPosition = Vector3.zero;
            main = ash.main;
            main.duration = duration;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.4f, 4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f * scale, 0.28f * scale);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = AshColor;
            main.gravityModifier = 0.16f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 500;
            emission = ash.emission;
            emission.rateOverTime = 80f;
            shape = ash.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius * 0.85f;
            shape.radiusThickness = 1f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            Flutter(ash, 1.2f);
            FadeOut(ash, AshColor);

            var smoke = NewParticles(root.transform, root.transform.position, Dot(), false);
            smoke.transform.localPosition = Vector3.down * (0.4f * scale);
            main = smoke.main;
            main.duration = duration;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(2.2f * scale, 4.2f * scale);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = SmokeColor;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 80;
            emission = smoke.emission;
            emission.rateOverTime = 20f;
            shape = smoke.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius * 0.45f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var vel = smoke.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            vel.y = new ParticleSystem.MinMaxCurve(-1.4f, -0.4f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            Grow(smoke, 0.7f, 1.4f);
            FadeOut(smoke, SmokeColor);

            var glow = new GameObject("Glow");
            glow.transform.SetParent(root.transform, false);
            glow.transform.localPosition = Vector3.down * (1.5f * scale);
            var light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.45f, 0.15f);
            light.range = radius * 1.6f;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            var flicker = glow.AddComponent<FxFlicker>();
            flicker.Light = light;
            flicker.Duration = duration;
            flicker.Peak = 2.2f;

            var fx = root.AddComponent<FxStorm>();
            fx.Duration = duration;
            fx.Systems = new[] { embers, ash, smoke };
            root.SetActive(true);
            foreach (var ps in fx.Systems)
            {
                ps.gameObject.SetActive(true);
                ps.Play();
            }

            Sound(root, Clip(ref s_crackle, "FP_crackle", () => MakeCrackle(duration + 1f)), 0.8f, 5f, 60f);
        }

        /// <summary>
        ///     The death's tithe: embers flare where a creature fell and a thin stream of them flies to the one the vulture
        ///     heals.
        /// </summary>
        public static void Tithe(Vector3 from, Vector3 to)
        {
            var root = new GameObject("FP_Fx_Tithe");
            root.transform.position = from;
            Sparks(root.transform, from, EmberDeep, 24, 0.5f, 2.2f, 0.5f, 1.1f, 0.05f, 0.14f, -0.25f);
            Vector3 d = to - from;
            float dist = d.magnitude;
            if (dist > 0.3f && dist < 80f)
            {
                var wisp = NewParticles(root.transform, from, Dot());
                wisp.transform.rotation = Quaternion.LookRotation(d / dist);
                var main = wisp.main;
                main.duration = 0.5f;
                main.loop = false;
                main.startLifetime = 0.55f;
                main.startSpeed = new ParticleSystem.MinMaxCurve(dist / 0.6f, dist / 0.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
                main.startColor = new ParticleSystem.MinMaxGradient(EmberHot, EmberDeep);
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 80;
                var emission = wisp.emission;
                emission.rateOverTime = 110f;
                var shape = wisp.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 4f;
                shape.radius = 0.15f;
                Flutter(wisp, 0.6f);
                FadeOut(wisp, EmberHot);
                wisp.gameObject.SetActive(true);
                wisp.Play();
            }

            Flash(root.transform, to, new Color(1f, 0.5f, 0.2f), 4f, 1.6f, 0.9f);
            Object.Destroy(root, 3f);
        }

        /// <summary>Embers cool as they fall: yellow-white to orange to a dull red, then gone.</summary>
        private static void Cooling(ParticleSystem ps)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[]
                {
                    new GradientColorKey(new Color(1f, 0.9f, 0.55f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.1f), 0.35f),
                    new GradientColorKey(new Color(0.55f, 0.1f, 0.04f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.35f)));
        }

        /// <summary>A little turbulence, so the ash drifts and the embers dance on their way down.</summary>
        private static void Flutter(ParticleSystem ps, float strength)
        {
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = strength;
            noise.frequency = 0.6f;
            noise.scrollSpeed = 0.4f;
            noise.quality = ParticleSystemNoiseQuality.Low;
        }

        /// <summary>A crackling fire: a soft roar with sharp pops and snaps scattered through it.</summary>
        private static float[] MakeCrackle(float seconds)
        {
            int n = (int)(Rate * Mathf.Clamp(seconds, 1f, 31f));
            var d = new float[n];
            var rnd = new System.Random(23);
            float lp = 0f, lp2 = 0f, pop = 0f, popDecay = 0f, peak = 1e-6f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float w = (float)(rnd.NextDouble() * 2.0 - 1.0);
                lp += (w - lp) * 0.06f;           // the roar of the flames
                lp2 += (lp - lp2) * 0.02f;
                if (rnd.NextDouble() < 0.0009)
                {
                    pop = (float)(rnd.NextDouble() * 0.8 + 0.4) * (rnd.NextDouble() < 0.5 ? -1f : 1f);
                    popDecay = (float)(0.92 + rnd.NextDouble() * 0.05);
                }

                pop *= popDecay;
                float crack = pop * (float)(rnd.NextDouble() * 2.0 - 1.0);
                float env = Mathf.Min(1f, t / 0.4f) * Mathf.Min(1f, (n / (float)Rate - t) / 1f);
                d[i] = ((lp - lp2) * 1.4f + crack * 0.9f) * env;
                peak = Mathf.Max(peak, Mathf.Abs(d[i]));
            }

            for (int i = 0; i < n; i++)
            {
                d[i] = d[i] / peak * 0.8f;
            }

            return d;
        }
    }

    /// <summary>A glow that flickers like a fire for its time, then dies down.</summary>
    internal sealed class FxFlicker : MonoBehaviour
    {
        public Light Light;
        public float Duration = 6f;
        public float Peak = 2f;
        private float m_t;

        private void Update()
        {
            m_t += Time.deltaTime;
            if (Light == null)
            {
                return;
            }

            float rise = Mathf.Clamp01(m_t / 0.5f);
            float fall = Mathf.Clamp01((Duration + 1f - m_t) / 1f);
            float flick = 0.75f + 0.25f * Mathf.PerlinNoise(m_t * 7f, 0.3f);
            Light.intensity = Peak * rise * fall * flick;
        }
    }
}
