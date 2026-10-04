using UnityEngine;

namespace FlyingPets
{
    /// <summary>The local rider: boarding along the wing, the saddle anchor, dismounting and the camera.</summary>
    public partial class FlyingPet
    {
        private Player m_localRider;
        private bool m_attachedLocal;
        private string m_attachAnim;
        private Vector3 m_hopStartLocal;
        private bool m_allowRelease;
        private bool m_wantDismount;
        private bool m_releaseSent;
        private bool m_dropSet;
        private Vector3 m_dropPoint;
        private float m_savedCamMax = -1f;

        // last controls from the rider (ApplyControlls)
        private Vector3 m_inMove;
        private Vector3 m_inLook;
        private bool m_inRun;
        private bool m_inAuto;
        private bool m_inBlock;
        private float m_inTime = -10f;

        internal bool IsLocalRider(Player player)
        {
            return player != null && player == m_localRider;
        }

        private void BeginLocalMount(Player player, int side)
        {
            if (m_localRider != null || player.IsAttached() || player.GetDoodadController() != null || player.IsDead())
            {
                // granted, but we are busy elsewhere: give the saddle back
                if (m_localRider != player)
                {
                    m_nview.InvokeRPC("FP_Release", player.GetPlayerID());
                }

                return;
            }

            m_nview.ClaimOwnership();
            OnBecameOwner();
            m_wasOwner = true;
            m_localRider = player;
            m_attachedLocal = false;
            m_allowRelease = false;
            m_wantDismount = false;
            m_releaseSent = false;
            m_cruise = false;
            m_hopStartLocal = transform.InverseTransformPoint(player.transform.position);
            m_nview.GetZDO().Set(ZRider, player.GetPlayerID());
            player.StartDoodadControl(this);
            m_speed = 0f;
            m_side = 0f;
            m_vel = Vector3.zero;
            m_state = StateKneel;
            WriteState();
            StartSeq(SeqMount, side);
            CameraIn();
            if (ModConfig.ShowControlsHint.Value)
            {
                player.Message(MessageHud.MessageType.TopLeft, Texts.Get("fp_msg_controls"));
                string active = PetAbility.ActiveOf(m_asset.Abilities);
                if (active != null && Texts.Has("fp_msg_skill_" + active))
                {
                    player.Message(MessageHud.MessageType.TopLeft, Texts.Get("fp_msg_skill_" + active));
                }
            }
        }

        /// <summary>Runs every frame on the rider's game: attaches, swaps the sitting pose and lets go.</summary>
        private void UpdateLocalRider()
        {
            var player = m_localRider;
            if (player == null)
            {
                return;
            }

            if (player != Player.m_localPlayer || player.IsDead())
            {
                ForceReleaseLocal(false);
                return;
            }

            if (!ReferenceEquals(player.GetDoodadController(), this))
            {
                // something else took the controls away (teleport, another mod...)
                ReleaseLocal(player, false);
                return;
            }

            if (!m_nview.IsOwner())
            {
                m_nview.ClaimOwnership();
            }

            if (m_seq == SeqMount)
            {
                if (!m_attachedLocal && m_seqTime >= TKneel)
                {
                    Attach(player, ChairAnimation);
                }

                if (m_attachedLocal && m_attachAnim != RideAnimation && m_seqTime >= TLift)
                {
                    Attach(player, RideAnimation);
                }
            }
            else if (m_seq == SeqDismount)
            {
                if (m_attachedLocal && m_attachAnim != ChairAnimation && m_seqTime >= TKneel)
                {
                    Attach(player, ChairAnimation);
                }

                if (m_seqTime >= TOff && !m_releaseSent)
                {
                    m_releaseSent = true;
                    LetGo(player);
                }
            }
            else if (!m_attachedLocal || m_attachAnim != RideAnimation)
            {
                Attach(player, RideAnimation);
            }
            else
            {
                SkillInput(player);
            }
        }

        private void Attach(Player player, string animation)
        {
            if (m_attachedLocal && player.IsAttached())
            {
                player.AttachStop();
            }

            player.AttachStart(m_seat, gameObject, false, false, false, animation, new Vector3(0f, 0.05f, 0f));
            m_attachedLocal = true;
            m_attachAnim = animation;
        }

        /// <summary>Ends the ride through the game's own path (StopDoodadControl -> OnUseStop).</summary>
        private void LetGo(Player player)
        {
            m_allowRelease = true;
            if (ReferenceEquals(player.GetDoodadController(), this))
            {
                player.StopDoodadControl();
            }

            if (m_localRider != null)
            {
                ReleaseLocal(player, false);
            }
        }

        /// <summary>
        ///     Harmony prefix of Player.StopDoodadControl. Returning false keeps the rider in the saddle:
        ///     the pet lands first and plays the dismount, then lets go itself.
        /// </summary>
        internal bool AllowStopControl(Player player)
        {
            if (m_allowRelease || !IsValid() || player == null || player.IsDead() || player != m_localRider)
            {
                return true;
            }

            if (Vector3.Distance(player.transform.position, GetPosition()) > 12f * m_scale)
            {
                return true;
            }

            if (m_seq == SeqMount && !m_attachedLocal)
            {
                return true; // still standing next to the pet: cancel the mount
            }

            m_wantDismount = true;
            return false;
        }

        private void ReleaseLocal(Player player, bool destroyed)
        {
            if (m_localRider == null)
            {
                return;
            }

            StopSkill();
            m_localRider = null;
            if (m_attachedLocal && player != null && player.IsAttached())
            {
                player.AttachStop();
            }

            m_attachedLocal = false;
            m_attachAnim = null;
            m_allowRelease = false;
            m_wantDismount = false;
            m_cruise = false;
            CameraOut();

            if (destroyed || m_nview == null || !m_nview.IsValid())
            {
                return;
            }

            if (m_nview.IsOwner())
            {
                m_nview.GetZDO().Set(ZRider, 0L);
                if (m_seq == SeqMount)
                {
                    // cancelled before the rider got on: stand up again
                    EndSeq();
                }

                if (m_state == StateKneel && m_seq == SeqNone)
                {
                    m_state = StateGround;
                    WriteState();
                }
            }
            else if (player != null)
            {
                m_nview.InvokeRPC("FP_Release", player.GetPlayerID());
            }
        }

        /// <summary>The pet disappeared or the rider died: get the rider off safely.</summary>
        private void ForceReleaseLocal(bool destroyed)
        {
            var player = m_localRider;
            bool wasAttached = m_attachedLocal;
            Vector3 seat = m_seat != null ? m_seat.position : transform.position;
            m_allowRelease = true;
            if (player != null && ReferenceEquals(player.GetDoodadController(), this))
            {
                player.StopDoodadControl();
            }

            ReleaseLocal(player, destroyed);
            if (player != null && wasAttached && !player.IsDead())
            {
                Util.PutOnGround(player, seat);
            }
        }

        /// <summary>Over water the rider simply drops off.</summary>
        private void DropRiderNow()
        {
            var player = m_localRider;
            if (player == null)
            {
                return;
            }

            if (m_seq != SeqNone)
            {
                EndSeq();
            }

            LetGo(player);
        }

        // ------------------------------------------------------------------ the saddle anchor
        /// <summary>Moves the attach point along the boarding path (after the pose is applied).</summary>
        private void UpdateAnchor()
        {
            float lift = ModConfig.RiderSeatHeight.Value / Mathf.Max(0.05f, m_scale);
            Vector3 restLocal = m_seatRest + new Vector3(0f, lift, 0f);
            if (m_localRider == null || m_seq == SeqNone)
            {
                m_seat.localPosition = restLocal;
                m_seat.localRotation = Quaternion.identity;
                return;
            }

            Transform body = m_seat.parent;
            Vector3 seat = body.TransformPoint(restLocal);
            Vector3 wing = WingStep(m_seqSide);
            Vector3 up = transform.up;
            float t = m_seqTime;
            Vector3 a;
            bool onSaddle = false;
            if (m_seq == SeqMount)
            {
                Vector3 start = transform.TransformPoint(m_hopStartLocal);
                if (t < TKneel)
                {
                    a = start;
                }
                else if (t < THop)
                {
                    float s = PetAnimator.Smooth((t - TKneel) / (THop - TKneel));
                    a = Vector3.Lerp(start, wing, s) + up * (0.6f * m_scale * Mathf.Sin(Mathf.PI * s));
                }
                else if (t < TLift)
                {
                    float u = (t - THop) / (TLift - THop);
                    if (u < 0.65f)
                    {
                        a = wing;
                    }
                    else
                    {
                        float s = PetAnimator.Smooth((u - 0.65f) / 0.35f);
                        a = Vector3.Lerp(wing, seat, s) + up * (0.25f * m_scale * Mathf.Sin(Mathf.PI * s));
                        onSaddle = s > 0.99f;
                    }
                }
                else
                {
                    a = seat;
                    onSaddle = true;
                }
            }
            else
            {
                if (!m_dropSet && t >= TDropWing)
                {
                    m_dropPoint = DropPoint(wing, m_seqSide);
                    m_dropSet = true;
                }

                if (t < TKneel)
                {
                    a = seat;
                    onSaddle = true;
                }
                else if (t < TDropWing)
                {
                    float u = (t - TKneel) / (TDropWing - TKneel);
                    if (u < 0.35f)
                    {
                        float s = PetAnimator.Smooth(u / 0.35f);
                        a = Vector3.Lerp(seat, wing, s) + up * (0.25f * m_scale * Mathf.Sin(Mathf.PI * s));
                    }
                    else
                    {
                        a = wing;
                    }
                }
                else if (t < TOff)
                {
                    float s = PetAnimator.Smooth((t - TDropWing) / (TOff - TDropWing));
                    a = Vector3.Lerp(wing, m_dropPoint, s) + up * (0.5f * m_scale * Mathf.Sin(Mathf.PI * s));
                }
                else
                {
                    a = m_dropPoint;
                }
            }

            m_seat.position = a;
            m_seat.rotation = onSaddle ? body.rotation : transform.rotation;
        }

        /// <summary>World position of the step on the near wing (where the rider sits while being lifted).</summary>
        private Vector3 WingStep(int side)
        {
            int bone = m_anim.Wing[side, 1];
            if (bone < 0)
            {
                return m_seat.parent.TransformPoint(m_seatRest);
            }

            Vector3 rest = PetAsset.V3(m_asset.Data.wingStep, new Vector3(1.4f, 2.05f, -0.1f));
            if (side == PetAnimator.Left)
            {
                rest.x = -rest.x;
            }

            return m_bones[bone].TransformPoint(rest - m_asset.Pivots[bone]) + transform.up * (0.1f * m_scale);
        }

        private Vector3 DropPoint(Vector3 wing, int side)
        {
            Vector3 outward = transform.right * (side == PetAnimator.Right ? 1f : -1f);
            outward.y = 0f;
            Vector3 p = wing + outward.normalized * (1.1f * m_scale);
            float water;
            p.y = Util.GroundY(p, 2f, out water);
            if (water > p.y)
            {
                p.y = water;
            }

            return p;
        }

        // ------------------------------------------------------------------ camera
        private void CameraIn()
        {
            var cam = GameCamera.instance;
            if (cam == null)
            {
                return;
            }

            if (m_savedCamMax < 0f)
            {
                m_savedCamMax = cam.m_maxDistance;
            }

            float want = ModConfig.CameraDistance.Value * Mathf.Max(1f, m_scale);
            cam.m_maxDistance = Mathf.Max(m_savedCamMax, want);
            Util.SetCameraDistance(cam, Mathf.Max(Util.GetCameraDistance(cam), want * 0.8f));
        }

        private void CameraOut()
        {
            var cam = GameCamera.instance;
            if (cam != null && m_savedCamMax > 0f)
            {
                cam.m_maxDistance = m_savedCamMax;
            }

            m_savedCamMax = -1f;
        }
    }
}
