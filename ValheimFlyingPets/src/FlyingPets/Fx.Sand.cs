using UnityEngine;

namespace FlyingPets
{
    /// <summary>The sand prowler's effects: the sandstorm.</summary>
    internal static partial class Fx
    {
        private static AudioClip s_wind;

        private static readonly Color DustColor = new Color(0.80f, 0.66f, 0.45f, 0.30f);
        private static readonly Color GrainColor = new Color(0.90f, 0.78f, 0.55f, 0.85f);

        /// <summary>
        ///     A storm of sand whirling round the pet for <paramref name="duration" /> seconds: a ring of dust clouds
        ///     and a swarm of grains, both carried along with it, and the howl of the wind.
        /// </summary>
        public static void Sandstorm(Transform pet, float duration, float radius, float scale)
        {
            var root = new GameObject("FP_Fx_Sandstorm");
            root.SetActive(false);
            root.transform.SetParent(pet, false);
            root.transform.localPosition = Vector3.up * (0.6f * scale);
            root.transform.localRotation = Quaternion.identity;

            var cloud = NewParticles(root.transform, root.transform.position, Dot(), false);
            cloud.transform.localPosition = Vector3.zero;
            var main = cloud.main;
            main.duration = duration;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.4f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(2.5f * scale, 5f * scale);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = DustColor;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 300;
            var emission = cloud.emission;
            emission.rateOverTime = 55f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 30) });
            var shape = cloud.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius * 0.75f;
            shape.radiusThickness = 0.6f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            Swirl(cloud, 0.9f, -0.15f, 0.2f, 1.1f);
            Grow(cloud, 0.6f, 1.5f);
            FadeOut(cloud, DustColor);

            var grains = NewParticles(root.transform, root.transform.position, Dot(), false);
            grains.transform.localPosition = Vector3.zero;
            main = grains.main;
            main.duration = duration;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 1.8f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f * scale, 0.14f * scale);
            main.startColor = GrainColor;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 900;
            emission = grains.emission;
            emission.rateOverTime = 320f;
            shape = grains.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius * 0.9f;
            shape.radiusThickness = 0.8f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            Swirl(grains, 2.2f, -0.3f, 0.5f, 3f);
            FadeOut(grains, GrainColor);

            var fx = root.AddComponent<FxStorm>();
            fx.Duration = duration;
            fx.Systems = new[] { cloud, grains };
            root.SetActive(true);
            cloud.gameObject.SetActive(true);
            grains.gameObject.SetActive(true);
            cloud.Play();
            grains.Play();
            Dust(root.transform, pet.position, radius * 0.6f);
            Sound(root, Clip(ref s_wind, "FP_wind", () => MakeWind(duration + 1f)), 0.85f, 6f, 70f);
        }

        /// <summary>Orbit round the pet's vertical axis, drift in or out, and rise.</summary>
        private static void Swirl(ParticleSystem ps, float orbit, float radial, float riseMin, float riseMax)
        {
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.orbitalX = new ParticleSystem.MinMaxCurve(0f);
            vel.orbitalY = new ParticleSystem.MinMaxCurve(orbit * 0.8f, orbit * 1.2f);
            vel.orbitalZ = new ParticleSystem.MinMaxCurve(0f);
            vel.radial = new ParticleSystem.MinMaxCurve(radial);
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(riseMin, riseMax);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        }

        /// <summary>Howling wind: noise swept through a slowly wandering band, swelling and falling in gusts.</summary>
        private static float[] MakeWind(float seconds)
        {
            int n = (int)(Rate * Mathf.Clamp(seconds, 1f, 31f));
            var d = new float[n];
            var rnd = new System.Random(17);
            float lp = 0f, bp = 0f, slow = 0f, peak = 1e-6f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float w = (float)(rnd.NextDouble() * 2.0 - 1.0);
                slow += ((float)(rnd.NextDouble() * 2.0 - 1.0) - slow) * 0.0004f;
                float cut = 0.015f + 0.05f * (0.5f + 0.5f * Mathf.Sin(t * 1.7f + 6f * slow));
                lp += (w - lp) * cut;
                bp += (lp - bp) * cut * 0.4f;
                float gust = 0.55f + 0.45f * Mathf.Sin(t * 2.3f) * Mathf.Sin(t * 0.9f + 1f);
                float env = Mathf.Min(1f, t / 0.6f) * Mathf.Min(1f, (n / (float)Rate - t) / 1f);
                d[i] = (lp - bp) * gust * env;
                peak = Mathf.Max(peak, Mathf.Abs(d[i]));
            }

            for (int i = 0; i < n; i++)
            {
                d[i] = d[i] / peak * 0.8f;
            }

            return d;
        }
    }

    /// <summary>Stops the storm after its time and clears it away once the last sand has settled.</summary>
    internal sealed class FxStorm : MonoBehaviour
    {
        public float Duration = 6f;
        public ParticleSystem[] Systems;
        private float m_t;
        private bool m_stopped;

        private void Update()
        {
            m_t += Time.deltaTime;
            if (m_stopped || m_t < Duration)
            {
                return;
            }

            m_stopped = true;
            foreach (var ps in Systems)
            {
                if (ps != null)
                {
                    ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                }
            }

            Destroy(gameObject, 3.5f);
        }
    }
}
