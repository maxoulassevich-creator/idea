using UnityEngine;

namespace FlyingPets
{
    /// <summary>
    ///     The sand prowler's sandstorm (the active ability) and its quicksand. Run by whoever simulates the pet
    ///     (the rider, or the summoner when nobody rides); the storm is shown to everybody with FP_Fx, and
    ///     the creatures caught in it are blinded by their own owners (Passives).
    /// </summary>
    public partial class FlyingPet
    {
        internal const int SkillStorm = 4;

        private const int FxStorm = 7;

        private static float s_stormReady;
        private float m_stormTick;
        private float m_stormW;        // every client: wings beating, jaws open
        private float m_quicksandTick;

        /// <summary>Is this pet raging in a sandstorm now (every client, from the ZDO)?</summary>
        internal bool IsStorming
        {
            get { return IsAround && SkillState == SkillStorm; }
        }

        internal float StormRadius
        {
            get { return Mathf.Max(2f, ModConfig.StormRadius.Value) * Mathf.Max(1f, m_scale); }
        }

        // ------------------------------------------------------------------ sandstorm
        private void BeginStorm(Player player)
        {
            SetSkill(SkillStorm);
            m_stormTick = 0.4f; // the sand needs a moment to rise
            float cd = Mathf.Max(1f, ModConfig.StormCooldown.Value);
            s_stormReady = Time.time + cd;
            AbilityEffects.Timer(player, AbilityEffects.Storm, cd);
            SendFx(FxStorm, transform.position, new Vector3(StormTime, StormRadius, 0f));
        }

        private static float StormTime
        {
            get { return Mathf.Clamp(ModConfig.StormDuration.Value, 1f, 30f); }
        }

        /// <summary>Every physics step of the storm (owner): the sand scours everything inside once a second.</summary>
        private void StormStep(float dt)
        {
            m_skillTime += dt;
            m_stormTick -= dt;
            if (m_stormTick <= 0f)
            {
                m_stormTick += 1f;
                StormScour();
            }

            if (m_skillTime >= StormTime)
            {
                SetSkill(SkillNone);
            }
        }

        private void StormScour()
        {
            Vector3 c = transform.position;
            float r = StormRadius;
            float damage = Mathf.Max(0f, ModConfig.StormDamage.Value);
            var rider = m_localRider;
            s_hits.Clear();
            Character.GetCharactersInRange(c, r, s_hits);
            foreach (var ch in s_hits)
            {
                if (ch == null || ch.IsPlayer() || ch.IsDead() || ch.IsTamed() || Passives.IsHeld(ch))
                {
                    continue;
                }

                Vector3 dir = ch.transform.position - c;
                dir.y = 0f;
                dir = dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward;
                var hit = new HitData();
                hit.m_damage.m_slash = damage;
                hit.m_point = ch.GetCenterPoint();
                hit.m_dir = dir;
                hit.m_pushForce = 6f;
                hit.m_staggerMultiplier = 0.3f;
                hit.m_statusEffectHash = ch.IsBoss() ? 0 : AbilityEffects.QuicksandHash; // the sand drags at their feet
                hit.m_hitType = HitData.HitType.PlayerHit;
                if (rider != null)
                {
                    hit.SetAttacker(rider);
                }

                ch.Damage(hit);
            }
        }

        // ------------------------------------------------------------------ quicksand
        /// <summary>
        ///     Every physics step (owner): while the prowler stands or walks on the ground, the creatures around it
        ///     sink in and slow down (a shared status effect, applied by each creature's own game).
        /// </summary>
        private void QuicksandStep(float dt)
        {
            float r = ModConfig.QuicksandRadius.Value;
            if (r <= 0f || m_state == StateAir)
            {
                return;
            }

            m_quicksandTick -= dt;
            if (m_quicksandTick > 0f)
            {
                return;
            }

            m_quicksandTick = 1f;
            s_hits.Clear();
            Character.GetCharactersInRange(transform.position, r * Mathf.Max(1f, m_scale), s_hits);
            foreach (var ch in s_hits)
            {
                if (ch == null || ch.IsPlayer() || ch.IsDead() || ch.IsTamed() || ch.IsBoss() || Passives.IsHeld(ch))
                {
                    continue;
                }

                ch.GetSEMan().AddStatusEffect(AbilityEffects.QuicksandHash, true, 0, 0f);
            }
        }
    }
}
