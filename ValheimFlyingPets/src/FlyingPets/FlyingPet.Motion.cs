using UnityEngine;

namespace FlyingPets
{
    /// <summary>Movement, simulated by the ZDO owner (the rider, or the summoner when nobody rides).</summary>
    public partial class FlyingPet
    {
        private struct Controls
        {
            public bool Hold;        // stand still (kneeling, boarding)
            public float Forward;    // wanted forward speed, m/s (negative = back up)
            public float Strafe;     // wanted sideways speed in the air, m/s
            public bool HasYaw;      // turn towards Yaw at TurnRate deg/s
            public float Yaw;
            public float TurnRate;
            public float YawDelta;   // or turn at this rate, deg/s
            public float Climb;      // wanted vertical speed in the air, m/s
            public bool TakeOff;
            public bool Land;
        }

        private int m_state = StateGround;
        private bool m_wasOwner;
        private float m_yaw;
        private float m_pitch;
        private float m_roll;
        private float m_speed;
        private float m_side;
        private float m_vyAir;
        private Vector3 m_vel;
        private Vector3 m_lastSimPos;
        private float m_takeoffTimer;
        private float m_stuck;
        private float m_landTimer;
        private float m_callTimer;
        private float m_noOwnerTimer;
        private float m_riderLostTimer;
        private bool m_cruise;
        private bool m_lastAuto;

        private void FixedUpdate()
        {
            if (!m_ready || !m_nview.IsValid())
            {
                return;
            }

            bool owner = m_nview.IsOwner();
            if (owner && !m_wasOwner)
            {
                OnBecameOwner();
            }

            m_wasOwner = owner;
            if (!owner || m_byeShown)
            {
                return;
            }

            Simulate(Time.fixedDeltaTime);
        }

        private void OnBecameOwner()
        {
            m_yaw = transform.eulerAngles.y;
            m_pitch = 0f;
            m_roll = 0f;
            m_vel = Vector3.zero;
            m_speed = 0f;
            m_side = 0f;
            m_vyAir = 0f;
            m_state = m_nview.GetZDO().GetInt(ZState, StateGround);
            if (m_state == StateKneel && m_seq == SeqNone)
            {
                m_state = StateGround;
            }

            m_lastSimPos = m_body != null ? m_body.position : transform.position;
        }

        private void WriteState()
        {
            var zdo = m_nview.GetZDO();
            if (zdo.GetInt(ZState, StateGround) != m_state)
            {
                zdo.Set(ZState, m_state);
            }
        }

        private void Simulate(float dt)
        {
            var zdo = m_nview.GetZDO();
            Vector3 pos = m_body.position;
            float waterY;
            float groundY = Util.GroundY(pos, 1.2f * m_scale, out waterY);
            bool overWater = waterY > groundY + 0.05f;
            float floorY = overWater ? waterY : groundY;
            float alt = pos.y - floorY;
            Vector3 moved = pos - m_lastSimPos;
            m_lastSimPos = pos;

            if (m_seq != SeqNone && m_seqTime >= TStand)
            {
                EndSeq();
                if (m_state == StateKneel)
                {
                    m_state = StateGround;
                }
            }

            long rider = zdo.GetLong(ZRider, 0L);
            if (rider != 0L && m_localRider == null)
            {
                // the rider left the game or walked off without telling us
                m_riderLostTimer = Util.FindPlayer(rider) == null ? m_riderLostTimer + dt : 0f;
                if (m_riderLostTimer > 3f)
                {
                    zdo.Set(ZRider, 0L);
                    rider = 0L;
                    m_riderLostTimer = 0f;
                }
            }

            Controls c;
            if (m_state == StateKneel || m_seq != SeqNone)
            {
                c = new Controls { Hold = true };
            }
            else if (m_localRider != null && m_attachedLocal)
            {
                c = RiderControls(alt);
            }
            else if (rider == 0L)
            {
                c = AiControls(dt, pos, groundY, overWater, waterY - groundY);
            }
            else
            {
                c = new Controls { Hold = true };
            }

            Integrate(c, dt, pos, moved, alt, groundY, waterY, overWater);
            WriteState();
        }

        // ------------------------------------------------------------------ rider input
        private Controls RiderControls(float alt)
        {
            var c = new Controls();
            float fwdIn = 0f, sideIn = 0f;
            bool run = false, block = false, auto = false;
            Vector3 look = transform.forward;
            if (Time.time - m_inTime < 0.3f)
            {
                fwdIn = m_inMove.z;
                sideIn = m_inMove.x;
                run = m_inRun;
                block = m_inBlock;
                auto = m_inAuto;
                if (m_inLook.sqrMagnitude > 1e-4f)
                {
                    look = m_inLook.normalized;
                }
            }

            bool input = Util.InputAllowed();
            bool up = input && (ZInput.GetButton("Jump") || ZInput.GetButton("JoyJump"));
            bool down = input && (ZInput.GetButton("Crouch") || ZInput.GetButton("JoyCrouch"));

            if (auto && !m_lastAuto)
            {
                m_cruise = !m_cruise;
            }

            m_lastAuto = auto;
            if (fwdIn < -0.1f || m_wantDismount)
            {
                m_cruise = false;
            }

            if (m_cruise)
            {
                fwdIn = 1f;
            }

            Vector3 flat = new Vector3(look.x, 0f, look.z);
            float camYaw = flat.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(flat).eulerAngles.y : m_yaw;
            float camUp = Mathf.Asin(Mathf.Clamp(look.y, -1f, 1f)) * Mathf.Rad2Deg; // + looking up

            if (m_wantDismount)
            {
                c.Forward = 0f;
                c.Land = true;
                return c;
            }

            if (m_state == StateGround)
            {
                if (fwdIn > 0.1f || block)
                {
                    c.HasYaw = true;
                    c.Yaw = camYaw;
                    c.TurnRate = 150f;
                }
                else if (Mathf.Abs(sideIn) > 0.1f)
                {
                    c.YawDelta = sideIn * 100f;
                }

                c.Forward = fwdIn > 0.1f ? fwdIn * (run ? ModConfig.RunSpeed.Value : ModConfig.WalkSpeed.Value)
                    : fwdIn < -0.1f ? -1.6f : 0f;
                c.TakeOff = up;
                return c;
            }

            // air
            if (fwdIn > 0.1f || block)
            {
                c.HasYaw = true;
                c.Yaw = camYaw;
                c.TurnRate = 100f;
            }

            c.Forward = fwdIn > 0.1f ? fwdIn * (run ? ModConfig.SprintSpeed.Value : ModConfig.FlySpeed.Value)
                : fwdIn < -0.1f ? -3f : 0f;
            c.Strafe = sideIn * ModConfig.FlySpeed.Value * 0.45f;
            float climb = 0f;
            if (up)
            {
                climb += ModConfig.ClimbSpeed.Value;
            }

            if (down)
            {
                climb -= ModConfig.DiveSpeed.Value;
            }

            if (ModConfig.PitchFollowsCamera.Value && fwdIn > 0.1f && !up && !down)
            {
                float dz = Mathf.Max(0f, ModConfig.PitchDeadZone.Value);
                float eff = Mathf.Sign(camUp) * Mathf.Max(0f, Mathf.Abs(camUp) - dz);
                climb += Mathf.Sin(eff * Mathf.Deg2Rad) * Mathf.Abs(c.Forward) * 1.1f;
            }

            c.Climb = climb;
            c.Land = down && alt < 2.5f * m_scale;
            return c;
        }

        // ------------------------------------------------------------------ riderless: follow the summoner
        private Controls AiControls(float dt, Vector3 pos, float groundY, bool overWater, float waterDepth)
        {
            var c = new Controls();
            if (m_landTimer > 0f)
            {
                m_landTimer -= dt;
            }

            if (m_callTimer > 0f)
            {
                m_callTimer -= dt;
            }

            var target = Util.FindPlayer(OwnerId);
            if (target == null || target.IsDead())
            {
                m_noOwnerTimer += dt;
                if (m_noOwnerTimer > 300f)
                {
                    m_noOwnerTimer = 0f;
                    BeginBye(); // the summoner is gone for good
                }

                c.Land = true;
                return c;
            }

            m_noOwnerTimer = 0f;
            Vector3 tp = target.transform.position;
            Vector3 d = tp - pos;
            d.y = 0f;
            float dist = d.magnitude;
            float follow = Mathf.Max(2f, ModConfig.FollowDistance.Value) * Mathf.Max(1f, m_scale);
            if (dist > Mathf.Max(30f, ModConfig.TeleportDistance.Value))
            {
                TeleportNear(target);
                c.Hold = true;
                return c;
            }

            float yawTo = dist > 0.01f ? Quaternion.LookRotation(d).eulerAngles.y : m_yaw;
            if (m_state == StateGround)
            {
                bool targetHigh = tp.y - pos.y > 5f * m_scale;
                bool deepWater = overWater && waterDepth > 0.8f * m_scale;
                bool called = m_callTimer > 0f && dist > 15f;
                if (m_landTimer <= 0f && (dist > 35f || targetHigh || m_stuck > 1.2f || deepWater || called))
                {
                    c.TakeOff = true;
                    return c;
                }

                if (dist > follow)
                {
                    c.HasYaw = true;
                    c.Yaw = yawTo;
                    c.TurnRate = 160f;
                    float spd = dist > follow + 9f ? ModConfig.RunSpeed.Value : ModConfig.WalkSpeed.Value;
                    c.Forward = spd * Mathf.Clamp01((dist - follow * 0.7f) / 2f);
                }
                else if (Mathf.Abs(Mathf.DeltaAngle(m_yaw, yawTo)) > 75f)
                {
                    c.HasYaw = true;
                    c.Yaw = yawTo;
                    c.TurnRate = 70f;
                }

                return c;
            }

            // air: catch up and land next to the summoner when they are on the ground at our level
            // (not below a summoner who stands on a roof or a cliff edge - that would make the pet hop)
            bool grounded = target.IsOnGround() && Mathf.Abs(groundY - tp.y) < 2.5f * m_scale;
            c.HasYaw = dist > 1f;
            c.Yaw = yawTo;
            c.TurnRate = 120f;
            if ((dist < 14f && grounded && !overWater) || (m_landTimer > 0f && !overWater))
            {
                c.Land = true;
                c.Forward = Mathf.Clamp(dist - follow, 0f, 6f);
                c.Climb = -4f;
                return c;
            }

            float goalY = tp.y + 4f * m_scale;
            c.Climb = Mathf.Clamp((goalY - pos.y) * 1.2f, -ModConfig.DiveSpeed.Value, ModConfig.ClimbSpeed.Value);
            c.Forward = Mathf.Clamp(dist * 0.9f - follow, 0f, ModConfig.SprintSpeed.Value);
            return c;
        }

        private void TeleportNear(Player target)
        {
            Vector3 back = -target.transform.forward;
            back.y = 0f;
            if (back.sqrMagnitude < 1e-4f)
            {
                back = Vector3.back;
            }

            back.Normalize();
            Vector3 p = target.transform.position + back * (4f * m_scale) + Vector3.Cross(Vector3.up, back) * (2f * m_scale);
            float water;
            p.y = Mathf.Max(Util.GroundY(p, 3f, out water), water);
            Util.SpawnEffect(Util.SummonEffects, p + Vector3.up * m_scale, Quaternion.identity);
            m_body.position = p;
            transform.position = p;
            m_body.linearVelocity = Vector3.zero;
            m_vel = Vector3.zero;
            m_speed = 0f;
            m_vyAir = 0f;
            m_yaw = Quaternion.LookRotation(-back).eulerAngles.y;
            m_state = StateGround;
            m_lastSimPos = p;
            Physics.SyncTransforms();
        }

        // ------------------------------------------------------------------ physics
        private void Integrate(Controls c, float dt, Vector3 pos, Vector3 moved, float alt, float groundY, float waterY, bool overWater)
        {
            float yawRate = 0f;
            if (!c.Hold)
            {
                if (c.HasYaw)
                {
                    float ny = Mathf.MoveTowardsAngle(m_yaw, c.Yaw, c.TurnRate * dt);
                    yawRate = Mathf.DeltaAngle(m_yaw, ny) / dt;
                    m_yaw = ny;
                }
                else if (c.YawDelta != 0f)
                {
                    m_yaw += c.YawDelta * dt;
                    yawRate = c.YawDelta;
                }
            }

            m_yaw = Mathf.Repeat(m_yaw, 360f);
            Quaternion yawQ = Quaternion.Euler(0f, m_yaw, 0f);
            Vector3 fwd = yawQ * Vector3.forward;
            Vector3 right = yawQ * Vector3.right;
            float waterDepth = overWater ? waterY - groundY : 0f;

            if (m_state != StateAir)
            {
                float want = c.Hold ? 0f : c.Forward;
                m_speed = Mathf.MoveTowards(m_speed, want, (Mathf.Abs(want) > Mathf.Abs(m_speed) ? 5f : 9f) * dt);
                m_side = 0f;
                float standY = overWater && waterDepth > 0.8f * m_scale ? waterY - 0.8f * m_scale : groundY;
                float vy = Mathf.Clamp((standY - pos.y) * 10f, -14f, 8f);
                m_vel = fwd * m_speed + Vector3.up * vy;

                float slope = c.Hold ? m_pitch : SlopePitch(pos, fwd);
                m_pitch = Mathf.MoveTowards(m_pitch, slope, 40f * dt);
                m_roll = Mathf.MoveTowards(m_roll, 0f, 60f * dt);

                float actual = Vector3.Dot(moved, fwd) / dt;
                m_stuck = Mathf.Abs(want) > 1f && Mathf.Abs(actual) < 0.35f ? m_stuck + dt : Mathf.Max(0f, m_stuck - dt);

                if (m_state == StateGround && !c.Hold && m_seq == SeqNone)
                {
                    if (c.TakeOff)
                    {
                        StartFlying(5f);
                    }
                    else if (alt > 1.6f * m_scale || (overWater && waterDepth > 0.8f * m_scale))
                    {
                        StartFlying(Mathf.Max(0f, m_vel.y));
                    }
                }

                // the rider asked to get off and the pet has stopped
                if (m_wantDismount && m_localRider != null && m_attachedLocal && m_seq == SeqNone &&
                    m_state == StateGround && Mathf.Abs(m_speed) < 1.5f)
                {
                    m_wantDismount = false;
                    if (overWater && waterDepth > 0.8f * m_scale)
                    {
                        DropRiderNow();
                    }
                    else
                    {
                        m_speed = 0f;
                        m_state = StateKneel;
                        StartSeq(SeqDismount, NearSide(m_localRider));
                    }
                }
            }
            else
            {
                m_speed = Mathf.MoveTowards(m_speed, c.Forward, (Mathf.Abs(c.Forward) > Mathf.Abs(m_speed) ? 7f : 10f) * dt);
                m_side = Mathf.MoveTowards(m_side, c.Strafe, 8f * dt);
                bool landing = c.Land || (m_wantDismount && m_localRider != null);
                float climb = c.Climb;
                if (m_takeoffTimer > 0f)
                {
                    m_takeoffTimer -= dt;
                    climb = Mathf.Max(climb, 5f);
                }
                else if (landing)
                {
                    climb = Mathf.Min(climb, alt > 6f * m_scale ? -ModConfig.DiveSpeed.Value : -3f);
                }

                if (alt > ModConfig.MaxAltitude.Value || pos.y > 2000f)
                {
                    climb = Mathf.Min(climb, 0f);
                }

                m_vyAir = Mathf.MoveTowards(m_vyAir, climb, 14f * dt);
                float minAlt = landing || climb < -0.5f ? 0f : 0.6f * m_scale;
                if (alt < minAlt)
                {
                    m_vyAir = Mathf.Max(m_vyAir, (minAlt - alt) * 6f);
                }

                if (overWater)
                {
                    float above = pos.y - waterY;
                    if (above < 1.2f * m_scale)
                    {
                        m_vyAir = Mathf.Max(m_vyAir, (1.2f * m_scale - above) * 4f);
                    }
                }

                m_vel = fwd * m_speed + right * m_side + Vector3.up * m_vyAir;

                if (!overWater && m_takeoffTimer <= 0f && alt < 0.3f * m_scale && m_vyAir <= 0.5f &&
                    (landing || Mathf.Abs(m_speed) < 4f))
                {
                    m_state = StateGround; // touch down
                    m_vyAir = 0f;
                    m_speed = Mathf.Clamp(m_speed, -2f, ModConfig.WalkSpeed.Value);
                    m_landTimer = 0f;
                }

                if (m_wantDismount && overWater && m_localRider != null && pos.y - waterY < 2.5f * m_scale)
                {
                    m_wantDismount = false;
                    DropRiderNow();
                }

                float pitchT = Mathf.Clamp(-Mathf.Atan2(m_vyAir, Mathf.Max(Mathf.Abs(m_speed), 5f)) * Mathf.Rad2Deg * 0.7f, -24f, 24f);
                m_pitch = Mathf.MoveTowards(m_pitch, pitchT, 45f * dt);
                float rollT = Mathf.Clamp(-yawRate * 0.28f - m_side * 1.5f, -32f, 32f);
                m_roll = Mathf.MoveTowards(m_roll, rollT, 60f * dt);
            }

            m_body.linearVelocity = m_vel;
            m_body.angularVelocity = Vector3.zero;
            m_body.MoveRotation(Quaternion.Euler(m_pitch, m_yaw, m_roll));
        }

        private void StartFlying(float vy)
        {
            m_state = StateAir;
            m_vyAir = vy;
            m_takeoffTimer = vy > 1f ? 0.45f : 0f;
            m_stuck = 0f;
            m_cruise = false;
        }

        private float SlopePitch(Vector3 pos, Vector3 fwd)
        {
            float l = 1.0f * m_scale, w;
            float front = Util.GroundY(pos + fwd * l, 1.5f * m_scale, out w);
            float back = Util.GroundY(pos - fwd * l, 1.5f * m_scale, out w);
            return Mathf.Clamp(-Mathf.Atan2(front - back, 2f * l) * Mathf.Rad2Deg, -18f, 18f);
        }

        private int NearSide(Player player)
        {
            if (player == null)
            {
                return m_asset.Data.mountSide > 0 ? PetAnimator.Right : PetAnimator.Left;
            }

            Vector3 cam = GameCamera.instance != null ? GameCamera.instance.transform.position : player.transform.position;
            return transform.InverseTransformPoint(cam).x >= 0f ? PetAnimator.Right : PetAnimator.Left;
        }
    }
}
