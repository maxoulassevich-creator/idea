using UnityEngine;

namespace FlyingPets
{
    /// <summary>Animation, evaluated on every client from the networked state and the observed motion.</summary>
    public partial class FlyingPet
    {
        private PetPose m_pIdle, m_pWalk, m_pFly, m_pGlide, m_pKneel, m_pGround, m_pOut, m_pSkill;
        private int[] m_allBones;
        private float m_t;
        private float m_gait;
        private float m_flap;
        private float m_airW;
        private float m_wingAirW;
        private float m_legAirW;
        private float m_glideW;
        private float m_riderW;
        private float m_walkW;
        private float m_kneelW;
        private float m_diveW;
        private float m_holdW;
        private float m_animGroundY;
        private float m_probeTimer;
        private Vector3 m_lastPos;
        private Vector3 m_seenVel;

        private void InitAnimation()
        {
            int n = m_anim.BoneCount;
            m_pIdle = new PetPose(n);
            m_pWalk = new PetPose(n);
            m_pFly = new PetPose(n);
            m_pGlide = new PetPose(n);
            m_pKneel = new PetPose(n);
            m_pGround = new PetPose(n);
            m_pOut = new PetPose(n);
            m_pSkill = new PetPose(n);
            m_allBones = new int[n];
            for (int i = 0; i < n; i++)
            {
                m_allBones[i] = i;
            }
        }

        private void LateUpdate()
        {
            if (!m_ready || !m_nview.IsValid())
            {
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            ObserveMotion(dt);
            Animate(dt);
            UpdateAnchor();
            UpdatePrey(dt);
        }

        private void ObserveMotion(float dt)
        {
            Vector3 p = transform.position;
            Vector3 v = (p - m_lastPos) / dt;
            m_lastPos = p;
            if (v.sqrMagnitude > 60f * 60f)
            {
                v = Vector3.zero; // teleported
            }

            m_seenVel = Vector3.Lerp(m_seenVel, v, 1f - Mathf.Exp(-dt * 8f));

            m_probeTimer -= dt;
            if (m_probeTimer <= 0f)
            {
                m_probeTimer = 0.1f;
                float water;
                m_animGroundY = Mathf.Max(Util.GroundY(p, 1.2f * m_scale, out water), water);
            }
        }

        private void Animate(float dt)
        {
            var d = m_asset.Data;
            int state = m_nview.IsOwner() ? m_state : m_netState;
            bool air = state == StateAir;
            Vector3 flatFwd = transform.forward;
            flatFwd.y = 0f;
            flatFwd.Normalize();
            float fwd = Vector3.Dot(m_seenVel, flatFwd) / m_scale; // model units: a bigger pet takes longer strides
            float vy = m_seenVel.y / m_scale;
            float alt = (transform.position.y - m_animGroundY) / m_scale;
            m_t += dt;

            // ---- layer weights
            m_airW = Mathf.MoveTowards(m_airW, air ? 1f : 0f, dt * 2.4f);
            m_wingAirW = Mathf.MoveTowards(m_wingAirW, air ? 1f : 0f, dt * 4f);
            m_legAirW = Mathf.MoveTowards(m_legAirW, air ? Mathf.Clamp01((alt - 0.25f) / 1.4f) : 0f, dt * 3f);
            // at cruise speed: glide for a while, then a few beats, then glide again; climbing always flaps
            bool cruising = air && fwd > 11f && vy < 1.5f;
            bool glidePhase = vy < -2.5f || Mathf.Repeat(m_t, 6f) < 3.6f;
            m_glideW = Mathf.MoveTowards(m_glideW, cruising && glidePhase ? 1f : 0f, dt * 1.2f);
            bool aboard = m_netRider != 0L &&
                          !(m_seq == SeqMount && m_seqTime < TLift) &&
                          !(m_seq == SeqDismount && m_seqTime >= TOff);
            m_riderW = Mathf.MoveTowards(m_riderW, aboard ? 1f : 0f, dt * 2f);
            m_walkW = Mathf.MoveTowards(m_walkW, air ? 0f : Mathf.Clamp01(Mathf.Abs(fwd) / 0.8f), dt * 4f);
            int skill = SkillState;
            m_diveW = Mathf.MoveTowards(m_diveW, air && skill == SkillDive ? 1f : 0f, dt * 4f);
            m_holdW = Mathf.MoveTowards(m_holdW, air && skill == SkillHold ? 1f : 0f, dt * 3f);
            // diving: hooves / talons forward; carrying: legs down around the prey
            float legsOut = 1f - Mathf.Max(m_diveW * 0.8f, m_holdW);

            // ---- clocks: stride follows the ground speed, wing beats quicken when hovering or climbing
            float trot = Mathf.Clamp01((Mathf.Abs(fwd) - 3.6f) / 2.8f);
            float stride = Mathf.Lerp(d.walkStride > 0f ? d.walkStride : 1.7f, d.trotStride > 0f ? d.trotStride : 2.9f, trot);
            m_gait = Mathf.Repeat(m_gait + dt * fwd / stride, 1f);
            float effort = Mathf.Max(1f - Mathf.Clamp01(Mathf.Abs(fwd) / 6f), Mathf.Clamp01(vy / 4f));
            float period = (d.flapPeriod > 0f ? d.flapPeriod : 1.25f) * Mathf.Lerp(1f, 0.82f, effort);
            float amp = (d.flapAmp > 0f ? d.flapAmp : 36f) * Mathf.Lerp(1f, 1.15f, effort);
            m_flap = Mathf.Repeat(m_flap + dt / period, 1f);

            // ---- ground pose: idle <-> walk/trot, wings folded or half raised with a rider
            m_anim.Idle(m_t, m_pIdle);
            m_pGround.CopyFrom(m_pIdle);
            if (m_walkW > 0.001f)
            {
                m_anim.Walk(m_t, m_gait, fwd, m_pWalk);
                PetAnimator.Blend(m_pGround, m_pIdle, m_pWalk, m_walkW, m_anim.Core);
                PetAnimator.Blend(m_pGround, m_pIdle, m_pWalk, m_walkW, m_anim.Legs);
                m_pGround.Root = Vector3.Lerp(m_pIdle.Root, m_pWalk.Root, m_walkW);
            }

            float perch = PetAnimator.Smooth(m_riderW);
            m_anim.SetWing(m_pGround, PetAnimator.Right, m_anim.Fold, m_anim.Perch, perch);
            m_anim.SetWing(m_pGround, PetAnimator.Left, m_anim.Fold, m_anim.Perch, perch);

            // ---- air pose: flapping <-> gliding, mixed per body part (legs stay down until clear of the ground)
            if (m_airW > 0.001f || m_wingAirW > 0.001f)
            {
                m_anim.Fly(m_flap, amp, m_pFly);
                if (m_glideW > 0.001f)
                {
                    m_anim.Glide(m_t, d.flapAmp > 0f ? d.flapAmp : 36f, m_pGlide);
                    PetAnimator.Blend(m_pFly, m_pFly, m_pGlide, m_glideW, m_allBones);
                    m_pFly.Root = Vector3.Lerp(m_pFly.Root, m_pGlide.Root, m_glideW);
                }

                m_pOut.CopyFrom(m_pGround);
                PetAnimator.Blend(m_pOut, m_pGround, m_pFly, m_airW, m_anim.Core);
                PetAnimator.Blend(m_pOut, m_pGround, m_pFly, Mathf.Min(m_airW, m_legAirW) * legsOut, m_anim.Legs);
                PetAnimator.Blend(m_pOut, m_pGround, m_pFly, m_wingAirW, m_anim.WingBones[0]);
                PetAnimator.Blend(m_pOut, m_pGround, m_pFly, m_wingAirW, m_anim.WingBones[1]);
                m_pOut.Root = Vector3.Lerp(m_pGround.Root, m_pFly.Root, m_airW);

                if (m_diveW > 0.001f)
                {
                    // the stoop: wings drawn in against the body
                    m_pSkill.CopyFrom(m_pOut);
                    m_anim.SetWing(m_pSkill, PetAnimator.Right, m_anim.Fold);
                    m_anim.SetWing(m_pSkill, PetAnimator.Left, m_anim.Fold);
                    float w = PetAnimator.Smooth(m_diveW) * 0.75f;
                    PetAnimator.Blend(m_pOut, m_pOut, m_pSkill, w, m_anim.WingBones[0]);
                    PetAnimator.Blend(m_pOut, m_pOut, m_pSkill, w, m_anim.WingBones[1]);
                }
            }
            else
            {
                m_pOut.CopyFrom(m_pGround);
            }

            // ---- kneeling and the boarding sequence
            int near = m_seq != SeqNone ? m_seqSide : (d.mountSide > 0 ? PetAnimator.Right : PetAnimator.Left);
            float kneel = KneelWeight(state, dt);
            if (kneel > 0.001f)
            {
                m_anim.Kneel(near, m_pKneel);
                PetAnimator.Blend(m_pOut, m_pOut, m_pKneel, kneel, m_anim.Core);
                PetAnimator.Blend(m_pOut, m_pOut, m_pKneel, kneel, m_anim.Legs);
                m_pOut.Root = Vector3.Lerp(m_pOut.Root, m_pKneel.Root, kneel);
            }

            if (m_seq != SeqNone)
            {
                SequenceWings(m_pOut, near);
            }

            // the tidal breath: jaws wide open
            m_breathW = Mathf.MoveTowards(m_breathW, skill == SkillBreath ? 1f : 0f, dt * 5f);
            if (m_breathW > 0.001f)
            {
                m_anim.SetJaw(m_pOut, PetAnimator.Smooth(m_breathW));
            }

            // the sandstorm: the wings beat hard where it stands, the jaws open in a roar
            m_stormW = Mathf.MoveTowards(m_stormW, skill == SkillStorm ? 1f : 0f, dt * 3f);
            if (m_stormW > 0.001f)
            {
                float w = PetAnimator.Smooth(m_stormW);
                m_anim.Fly(m_flap, amp * 1.15f, m_pSkill);
                PetAnimator.Blend(m_pOut, m_pOut, m_pSkill, w, m_anim.WingBones[0]);
                PetAnimator.Blend(m_pOut, m_pOut, m_pSkill, w, m_anim.WingBones[1]);
                m_anim.SetJaw(m_pOut, 0.55f * w);
            }

            // the ember rain: the wings shake the burning ash loose in short hard beats, the jaws open in a screech
            m_emberW = Mathf.MoveTowards(m_emberW, skill == SkillEmbers ? 1f : 0f, dt * 3f);
            if (m_emberW > 0.001f)
            {
                float w = PetAnimator.Smooth(m_emberW);
                m_emberBeat = Mathf.Repeat(m_emberBeat + dt * 1.6f / period, 1f);
                m_anim.Fly(m_emberBeat, amp * 0.9f, m_pSkill);
                PetAnimator.Blend(m_pOut, m_pOut, m_pSkill, w, m_anim.WingBones[0]);
                PetAnimator.Blend(m_pOut, m_pOut, m_pSkill, w, m_anim.WingBones[1]);
                m_anim.SetJaw(m_pOut, 0.45f * w);
            }

            // ---- apply
            for (int i = 0; i < m_bones.Length; i++)
            {
                m_bones[i].localRotation = m_pOut.Q[i];
            }

            int root = m_anim.Body;
            m_bones[root].localPosition = m_restLocal[root] + m_pOut.Root;
        }

        private float KneelWeight(int state, float dt)
        {
            if (m_seq != SeqNone)
            {
                float t = m_seqTime;
                float k = t < TKneel ? PetAnimator.Smooth(t / TKneel)
                    : t < TLift ? 1f
                    : 1f - PetAnimator.Smooth((t - TLift) / (TStand - TLift));
                m_kneelW = k;
                return k;
            }

            m_kneelW = Mathf.MoveTowards(m_kneelW, state == StateKneel ? 1f : 0f, dt * 2f);
            return PetAnimator.Smooth(m_kneelW);
        }

        /// <summary>
        ///     The near wing during boarding. Mount: fold -> ramp (rider steps on) -> scoop (lifts the rider
        ///     over the back) -> perch. Dismount plays it backwards.
        /// </summary>
        private void SequenceWings(PetPose p, int near)
        {
            int far = 1 - near;
            float t = m_seqTime;
            var a = m_anim;
            if (m_seq == SeqMount)
            {
                if (t < TKneel)
                {
                    a.SetWing(p, near, a.Fold, a.Ramp, PetAnimator.Smooth(t / TKneel));
                }
                else if (t < THop)
                {
                    a.SetWing(p, near, a.Ramp);
                }
                else if (t < TLift)
                {
                    a.SetWing(p, near, a.Ramp, a.Scoop, PetAnimator.Smooth((t - THop) / (TLift - THop)));
                }
                else
                {
                    float v = PetAnimator.Smooth((t - TLift) / (TStand - TLift));
                    a.SetWing(p, near, a.Scoop, a.Perch, v);
                    a.SetWing(p, far, a.Fold, a.Perch, v);
                }
            }
            else
            {
                if (t < TKneel)
                {
                    a.SetWing(p, near, a.Perch, a.Scoop, PetAnimator.Smooth(t / TKneel));
                    a.SetWing(p, far, a.Perch);
                }
                else if (t < TDropWing)
                {
                    float u = PetAnimator.Smooth((t - TKneel) / (TDropWing - TKneel));
                    a.SetWing(p, near, a.Scoop, a.Ramp, u);
                    a.SetWing(p, far, a.Perch, a.Fold, u);
                }
                else if (t < TOff)
                {
                    a.SetWing(p, near, a.Ramp);
                    a.SetWing(p, far, a.Fold);
                }
                else
                {
                    a.SetWing(p, near, a.Ramp, a.Fold, PetAnimator.Smooth((t - TOff) / (TStand - TOff)));
                    a.SetWing(p, far, a.Fold);
                }
            }
        }
    }
}
