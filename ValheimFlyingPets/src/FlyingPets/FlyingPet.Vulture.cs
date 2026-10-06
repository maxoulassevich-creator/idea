using UnityEngine;

namespace FlyingPets
{
    /// <summary>
    ///     The ash vulture's ember rain (the active ability) and the wisp of its death's tithe. The rain is run by the
    ///     rider's game, which owns the pet; the ash and the wisp are shown to everybody with FP_Fx.
    /// </summary>
    public partial class FlyingPet
    {
        internal const int SkillEmbers = 5;

        private const int FxEmbers = 8;
        private const int FxTithe = 9;

        private static float s_embersReady;
        private float m_emberTick;
        private float m_emberW;        // every client: wings shaking out the ash
        private float m_emberBeat;     // every client: their own quicker beat

        internal float EmberRadius
        {
            get { return Mathf.Max(2f, ModConfig.EmberRadius.Value) * Mathf.Max(1f, m_scale); }
        }

        private static float EmberTime
        {
            get { return Mathf.Clamp(ModConfig.EmberDuration.Value, 1f, 30f); }
        }

        // ------------------------------------------------------------------ ember rain
        private void BeginEmbers(Player player)
        {
            SetSkill(SkillEmbers);
            m_emberTick = 0.5f; // the first ash needs a moment to reach the ground
            float cd = Mathf.Max(1f, ModConfig.EmberCooldown.Value);
            s_embersReady = Time.time + cd;
            AbilityEffects.Timer(player, AbilityEffects.Embers, cd);
            SendFx(FxEmbers, transform.position, new Vector3(EmberTime, EmberRadius, 0f));
        }

        /// <summary>Every physics step of the rain (owner): whatever is beneath the vulture burns once a second.</summary>
        private void EmberStep(float dt)
        {
            m_skillTime += dt;
            m_emberTick -= dt;
            if (m_emberTick <= 0f)
            {
                m_emberTick += 1f;
                EmberScorch();
            }

            if (m_skillTime >= EmberTime)
            {
                SetSkill(SkillNone);
            }
        }

        /// <summary>The column under the vulture: everything within the radius, from far below up to its belly.</summary>
        private void EmberScorch()
        {
            Vector3 c = transform.position;
            float r = EmberRadius;
            float damage = Mathf.Max(0f, ModConfig.EmberDamage.Value);
            var rider = m_localRider;
            s_hits.Clear();
            foreach (var ch in Character.GetAllCharacters())
            {
                if (ch == null || ch.IsPlayer() || ch.IsDead() || ch.IsTamed() || Passives.IsHeld(ch))
                {
                    continue;
                }

                Vector3 p = ch.transform.position;
                float dx = p.x - c.x, dz = p.z - c.z;
                if (dx * dx + dz * dz <= r * r && p.y <= c.y + 4f && p.y >= c.y - 60f)
                {
                    s_hits.Add(ch); // gathered first: a hit may end a life, and the game's list with it
                }
            }

            foreach (var ch in s_hits)
            {
                var hit = new HitData();
                hit.m_damage.m_fire = damage; // the game turns fire into burning
                hit.m_point = ch.GetCenterPoint();
                hit.m_dir = Vector3.down;
                hit.m_pushForce = 0f;
                hit.m_staggerMultiplier = 0.2f;
                hit.m_hitType = HitData.HitType.PlayerHit;
                if (rider != null)
                {
                    hit.SetAttacker(rider);
                }

                Passives.OwnHit = true;
                try
                {
                    ch.Damage(hit);
                }
                finally
                {
                    Passives.OwnHit = false;
                }
            }
        }

        // ------------------------------------------------------------------ death's tithe
        /// <summary>A wisp of embers from the fallen creature to the one it heals (shown to everybody).</summary>
        internal void TitheFx(Vector3 from, Player to)
        {
            if (to != null)
            {
                SendFx(FxTithe, from, to.GetCenterPoint());
            }
        }
    }
}
