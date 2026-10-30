using System;
using UnityEngine;

namespace Wizardry
{
    /// <summary>
    ///     A spell in flight. The caster's game flies the real one (it hits and applies the spell); everybody else
    ///     flies a look-alike that only shows the light and the trail.
    /// </summary>
    internal sealed class Projectile : MonoBehaviour
    {
        private static int s_mask = -1;

        public Vector3 Velocity;
        public float Range = 45f;
        public float Gravity;
        public int Pierce;
        public Character Homing;
        public Action<Character, Vector3, Vector3> OnHit; // null for a visual-only copy
        public Player Owner;
        private float m_travelled;
        private readonly System.Collections.Generic.HashSet<Character> m_hit = new System.Collections.Generic.HashSet<Character>();
        private ParticleSystem m_trail;

        public static int Mask
        {
            get
            {
                if (s_mask == -1)
                {
                    s_mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle", "character",
                        "character_net", "character_ghost", "hitbox", "character_noenv");
                }

                return s_mask;
            }
        }

        public static Projectile Launch(Vector3 from, Vector3 dir, float speed, Color c, SpellDef spell, Player owner,
            Action<Character, Vector3, Vector3> onHit, string extra)
        {
            var go = new GameObject("WZ_Bolt");
            go.transform.position = from;
            var p = go.AddComponent<Projectile>();
            p.Velocity = dir.normalized * Mathf.Max(5f, speed);
            p.Owner = owner;
            p.OnHit = onHit;
            if (extra != null && extra.Contains("arc"))
            {
                p.Gravity = 9f;
                p.Velocity += Vector3.up * 3f;
            }

            p.Build(c, spell);
            return p;
        }

        public static void Visual(Vector3 from, Vector3 dir, float speed, Color c, SpellDef spell, string extra)
        {
            Launch(from, dir, speed, c, spell, null, null, extra);
        }

        private void Build(Color c, SpellDef spell)
        {
            float q = Fx.Quality;
            var light = gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = c;
            light.range = 4f;
            light.intensity = 1.6f;
            light.shadows = LightShadows.None;
            m_trail = Fx.NewParticles(transform, transform.position, Fx.Dot());
            m_trail.transform.localPosition = Vector3.zero;
            var main = m_trail.main;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            main.startColor = c;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = m_trail.emission;
            em.rateOverTime = 0f;
            em.rateOverDistance = 22f * q;
            var sh = m_trail.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.06f;
            Fx.FadeOut(m_trail, c);
            m_trail.gameObject.SetActive(true);
            m_trail.Play();
            var core = Fx.NewParticles(transform, transform.position, Fx.Dot());
            core.transform.localPosition = Vector3.zero;
            var cm = core.main;
            cm.loop = true;
            cm.startLifetime = 0.08f;
            cm.startSpeed = 0f;
            cm.startSize = 0.35f;
            cm.startColor = Color.Lerp(c, Color.white, 0.5f);
            cm.simulationSpace = ParticleSystemSimulationSpace.Local;
            var cem = core.emission;
            cem.rateOverTime = 60f;
            core.gameObject.SetActive(true);
            core.Play();
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            if (Homing != null && !Homing.IsDead())
            {
                Vector3 to = (Homing.GetCenterPoint() - transform.position).normalized;
                Velocity = Vector3.RotateTowards(Velocity, to * Velocity.magnitude, 4f * dt, 0f);
            }

            Velocity += Vector3.down * Gravity * dt;
            Vector3 step = Velocity * dt;
            float len = step.magnitude;
            RaycastHit hit;
            if (Physics.SphereCast(transform.position, 0.15f, step.normalized, out hit, len, Mask, QueryTriggerInteraction.Ignore))
            {
                var c = hit.collider.GetComponentInParent<Character>();
                if (c != null && Owner != null && c == Owner)
                {
                    transform.position += step;
                }
                else if (c != null && m_hit.Contains(c))
                {
                    transform.position += step;
                }
                else
                {
                    transform.position = hit.point;
                    if (OnHit != null)
                    {
                        try
                        {
                            OnHit(c, hit.point, Velocity.normalized);
                        }
                        catch (Exception e)
                        {
                            Plugin.Log.LogWarning("Spell hit failed: " + e.Message);
                        }
                    }

                    if (c != null && Pierce > 0)
                    {
                        Pierce--;
                        m_hit.Add(c);
                        transform.position += step;
                    }
                    else
                    {
                        End();
                        return;
                    }
                }
            }
            else
            {
                transform.position += step;
            }

            m_travelled += len;
            if (m_travelled > Range)
            {
                if (OnHit != null && Gravity > 0f)
                {
                    OnHit(null, transform.position, Velocity.normalized);
                }

                End();
            }
        }

        private void End()
        {
            if (m_trail != null)
            {
                var em = m_trail.emission;
                em.enabled = false;
                m_trail.transform.SetParent(null, true);
                Destroy(m_trail.gameObject, 1f);
            }

            Destroy(gameObject);
            enabled = false;
        }
    }
}
