using System;
using UnityEngine;

namespace FlyingPets
{
    /// <summary>Local rotation of every bone plus an offset of the whole body (model space).</summary>
    internal sealed class PetPose
    {
        public readonly Quaternion[] Q;
        public Vector3 Root;

        public PetPose(int bones)
        {
            Q = new Quaternion[bones];
            Reset();
        }

        public void Reset()
        {
            for (int i = 0; i < Q.Length; i++)
            {
                Q[i] = Quaternion.identity;
            }

            Root = Vector3.zero;
        }

        public void CopyFrom(PetPose o)
        {
            Array.Copy(o.Q, Q, Q.Length);
            Root = o.Root;
        }
    }

    /// <summary>Wing pose: rotation of the arm bone and of the hand bone (right wing; the left one is mirrored).</summary>
    internal struct WingShape
    {
        public Vector3 Arm;
        public float ArmDeg;
        public Vector3 Hand;
        public float HandDeg;
    }

    /// <summary>
    ///     Procedural animation of a winged quadruped. Every pose is built from rotations about the
    ///     bone pivots (bones rest unrotated), authored for the right side and mirrored for the left
    ///     with axis (x, -y, -z). Poses are blended per bone with quaternion slerp.
    /// </summary>
    internal sealed class PetAnimator
    {
        private static readonly Vector3 X = Vector3.right;
        private static readonly Vector3 Y = Vector3.up;
        private static readonly Vector3 Z = Vector3.forward;
        private const float Tau = 2f * Mathf.PI;

        public const int Right = 0;
        public const int Left = 1;

        private readonly PetJson m;
        public readonly int BoneCount;
        public readonly int Body;
        private readonly int neck1, neck2, head;
        private readonly int[] tail = new int[4];
        private readonly int[,] legF = new int[2, 4];
        private readonly int[,] legR = new int[2, 3];
        public readonly int[,] Wing = new int[2, 3];

        public readonly int[] Core;
        public readonly int[] Legs;
        public readonly int[][] WingBones = new int[2][];

        public readonly WingShape Fold, Ramp, Perch, Scoop;

        public PetAnimator(PetAsset asset)
        {
            m = asset.Data;
            BoneCount = asset.BoneNames.Length;
            Body = Math.Max(0, asset.Bone("body"));
            neck1 = asset.Bone("neck1");
            neck2 = asset.Bone("neck2");
            head = asset.Bone("head");
            for (int i = 0; i < 4; i++)
            {
                tail[i] = asset.Bone("tail" + i);
            }

            string[] sides = { "R", "L" };
            for (int s = 0; s < 2; s++)
            {
                for (int i = 0; i < 4; i++)
                {
                    legF[s, i] = asset.Bone("leg_f" + sides[s] + i);
                }

                for (int i = 0; i < 3; i++)
                {
                    legR[s, i] = asset.Bone("leg_r" + sides[s] + i);
                    Wing[s, i] = asset.Bone("wing_" + sides[s] + i);
                }

                WingBones[s] = new[] { Wing[s, 0], Wing[s, 1], Wing[s, 2] };
            }

            var core = new System.Collections.Generic.List<int>();
            var legs = new System.Collections.Generic.List<int>();
            for (int i = 0; i < BoneCount; i++)
            {
                string n = asset.BoneNames[i];
                if (n.StartsWith("leg_", StringComparison.Ordinal))
                {
                    legs.Add(i);
                }
                else if (!n.StartsWith("wing_", StringComparison.Ordinal))
                {
                    core.Add(i);
                }
            }

            Core = core.ToArray();
            Legs = legs.ToArray();

            Fold = Shape(m.wingFold, Y, 6f);
            Ramp = Shape(m.wingRamp, Z, 8f);
            Perch = Shape(m.wingPerch, Y, 10f);
            Scoop = Shape(m.wingScoop ?? m.wingPerch, Y, 6f);
        }

        private static WingShape Shape(float[] aa, Vector3 hand, float handDeg)
        {
            bool ok = aa != null && aa.Length >= 4;
            return new WingShape
            {
                Arm = ok ? new Vector3(aa[0], aa[1], aa[2]) : Vector3.up,
                ArmDeg = ok ? aa[3] : 0f,
                Hand = hand,
                HandDeg = handDeg,
            };
        }

        // ------------------------------------------------------------------ helpers
        private static Quaternion Rot(Vector3 axis, float deg)
        {
            return Quaternion.AngleAxis(deg, axis);
        }

        private static Vector3 Side(Vector3 axis, int side)
        {
            return side == Right ? axis : new Vector3(axis.x, -axis.y, -axis.z);
        }

        private static void Set(PetPose p, int bone, Quaternion q)
        {
            if (bone >= 0)
            {
                p.Q[bone] = q;
            }
        }

        private static float Frac(float x)
        {
            return x - Mathf.Floor(x);
        }

        public static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        /// <summary>Wing beat: +1 top .. -1 bottom; a quick downstroke (40%) and a slower recovery.</summary>
        private static float Stroke(float ph)
        {
            return ph < 0.4f ? Mathf.Cos(Mathf.PI * ph / 0.4f) : -Mathf.Cos(Mathf.PI * (ph - 0.4f) / 0.6f);
        }

        private float Arr(float[] a, int i, float fallback)
        {
            return PetAsset.At(a, i, fallback);
        }

        public static void Blend(PetPose dst, PetPose a, PetPose b, float t, int[] bones)
        {
            for (int k = 0; k < bones.Length; k++)
            {
                int i = bones[k];
                if (i >= 0)
                {
                    dst.Q[i] = Quaternion.Slerp(a.Q[i], b.Q[i], t);
                }
            }
        }

        // ------------------------------------------------------------------ wings
        public Quaternion WingArm(WingShape w, int side)
        {
            return Rot(Side(w.Arm, side), w.ArmDeg);
        }

        public void SetWing(PetPose p, int side, WingShape w)
        {
            Set(p, Wing[side, 0], Rot(Side(w.Arm, side), w.ArmDeg));
            Set(p, Wing[side, 1], Quaternion.identity);
            Set(p, Wing[side, 2], Rot(Side(w.Hand, side), w.HandDeg));
        }

        public void SetWing(PetPose p, int side, WingShape a, WingShape b, float t)
        {
            if (t <= 0f)
            {
                SetWing(p, side, a);
                return;
            }

            if (t >= 1f)
            {
                SetWing(p, side, b);
                return;
            }

            Set(p, Wing[side, 0], Quaternion.Slerp(Rot(Side(a.Arm, side), a.ArmDeg), Rot(Side(b.Arm, side), b.ArmDeg), t));
            Set(p, Wing[side, 1], Quaternion.identity);
            Set(p, Wing[side, 2], Quaternion.Slerp(Rot(Side(a.Hand, side), a.HandDeg), Rot(Side(b.Hand, side), b.HandDeg), t));
        }

        // ------------------------------------------------------------------ poses
        /// <summary>Standing: breathing, a slow look around, tail swish, wings folded.</summary>
        public void Idle(float t, PetPose p)
        {
            p.Reset();
            float b = Mathf.Sin(t * Tau / 3.6f);
            float look = Mathf.Sin(t * Tau / 7.5f);
            float sw = t * Tau / 4.2f;
            SetWing(p, Right, Fold);
            SetWing(p, Left, Fold);
            Set(p, Body, Rot(X, 0.4f * b));
            Set(p, neck1, Rot(Y, 4f * look) * Rot(X, -2f + 1.5f * b));
            Set(p, neck2, Rot(Y, 5f * look) * Rot(X, -1f + b));
            Set(p, head, Rot(Y, 4f * look) * Rot(X, 2f - 1.5f * b));
            Set(p, tail[0], Rot(Y, 6f * Mathf.Sin(sw)));
            Set(p, tail[1], Rot(Y, 8f * Mathf.Sin(sw - 0.7f)));
            Set(p, tail[2], Rot(Y, 10f * Mathf.Sin(sw - 1.4f)));
            Set(p, tail[3], Rot(Y, 12f * Mathf.Sin(sw - 2.1f)));
        }

        /// <summary>Flapping flight at wing-beat phase ph (0..1); legs tucked, neck stretched.</summary>
        public void Fly(float ph, float amp, PetPose p)
        {
            p.Reset();
            float s = Stroke(ph);
            float lag = Stroke(Frac(ph - 0.12f));
            for (int side = 0; side < 2; side++)
            {
                Vector3 y = Side(Y, side), z = Side(Z, side);
                Set(p, Wing[side, 0], Rot(y, 6f * s) * Rot(z, 6f + amp * s));
                Set(p, Wing[side, 1], Rot(z, 8f * lag));
                Set(p, Wing[side, 2], Rot(y, -8f * s) * Rot(z, 18f * lag - 4f));
                for (int i = 0; i < 4; i++)
                {
                    Set(p, legF[side, i], Rot(X, Arr(m.flyFront, i, 0f)));
                }

                for (int i = 0; i < 3; i++)
                {
                    Set(p, legR[side, i], Rot(X, Arr(m.flyRear, i, 0f)));
                }
            }

            Set(p, Body, Rot(X, -2.5f * s));
            Set(p, neck1, Rot(X, Arr(m.neckFly, 0, 22f) + 1.5f * s));
            Set(p, neck2, Rot(X, Arr(m.neckFly, 1, 8f)));
            Set(p, head, Rot(X, Arr(m.neckFly, 2, -18f)));
            float tw = Tau * ph;
            Set(p, tail[0], Rot(X, Arr(m.tailFly, 0, 22f)));
            Set(p, tail[1], Rot(Y, 5f * Mathf.Sin(tw)) * Rot(X, Arr(m.tailFly, 1, 4f)));
            Set(p, tail[2], Rot(Y, 6f * Mathf.Sin(tw - 0.8f)) * Rot(X, Arr(m.tailFly, 1, 4f)));
            Set(p, tail[3], Rot(Y, 7f * Mathf.Sin(tw - 1.6f)) * Rot(X, Arr(m.tailFly, 2, 0f)));
            p.Root = new Vector3(0f, 0.07f * s, 0f);
        }

        /// <summary>Gliding with the wings held out and gently trimming.</summary>
        public void Glide(float t, float amp, PetPose p)
        {
            Fly(0.264f, amp, p);
            for (int side = 0; side < 2; side++)
            {
                Vector3 z = Side(Z, side);
                Set(p, Wing[side, 0], Rot(z, 10f + 2f * Mathf.Sin(1.6f * t)));
                Set(p, Wing[side, 1], Rot(z, 2f));
                Set(p, Wing[side, 2], Rot(z, -6f));
            }

            p.Root = Vector3.zero;
        }

        /// <summary>
        ///     Walk (4-beat) that turns into a trot (diagonal pairs) with speed. gaitPhase advances by
        ///     speed / stride per second; negative speeds walk backwards.
        /// </summary>
        public void Walk(float t, float gaitPhase, float speed, PetPose p)
        {
            Idle(t, p);
            float sp = Mathf.Abs(speed);
            float trot = Mathf.Clamp01((sp - 3.6f) / 2.8f);
            float duty = 0.62f - 0.17f * trot;
            float amp = Mathf.Lerp(m.swingWalk > 0f ? m.swingWalk : 17f, m.swingTrot > 0f ? m.swingTrot : 24f, trot);
            float mix = Smooth(trot);
            // walk: rR 0, fR .25, rL .5, fL .75   trot: fR+rL 0, fL+rR .5
            LegPair(p, Right, true, gaitPhase + Mathf.Lerp(0.25f, 0f, mix), duty, amp);
            LegPair(p, Left, true, gaitPhase + Mathf.Lerp(0.75f, 0.5f, mix), duty, amp);
            LegPair(p, Right, false, gaitPhase + Mathf.Lerp(0f, 0.5f, mix), duty, amp);
            LegPair(p, Left, false, gaitPhase + Mathf.Lerp(0.5f, 0f, mix), duty, amp);
            float nod = Mathf.Sin(2f * Tau * gaitPhase);
            Set(p, neck1, Rot(X, -2f + 3f * nod * (1f - 0.5f * trot)));
            Set(p, head, Rot(X, 2f - 2f * nod));
            p.Root = new Vector3(0f, -0.02f * (1f + trot) * Mathf.Abs(nod), 0f);
        }

        private void LegPair(PetPose p, int side, bool front, float phase, float duty, float amp)
        {
            float ph = Frac(phase);
            float sw, lift;
            if (ph < duty)
            {
                sw = -1f + 2f * ph / duty; // planted: the hoof slides back under the body
                lift = 0f;
            }
            else
            {
                float u = (ph - duty) / (1f - duty); // swing: lift and reach forward
                sw = Mathf.Cos(Mathf.PI * u);
                lift = Mathf.Sin(Mathf.PI * u);
            }

            if (front)
            {
                Set(p, legF[side, 0], Rot(X, amp * sw));
                Set(p, legF[side, 1], Rot(X, Arr(m.frontFlex, 0, -18f) * lift));
                Set(p, legF[side, 2], Rot(X, Arr(m.frontFlex, 1, 72f) * lift));
                Set(p, legF[side, 3], Rot(X, Arr(m.frontFlex, 2, 28f) * lift));
            }
            else
            {
                Set(p, legR[side, 0], Rot(X, amp * sw * 0.9f));
                Set(p, legR[side, 1], Rot(X, Arr(m.rearFlex, 0, -38f) * lift));
                Set(p, legR[side, 2], Rot(X, Arr(m.rearFlex, 1, 34f) * lift));
            }
        }

        /// <summary>Kneeling for a rider on the given side: front knees down, chest low, head turned to the rider.</summary>
        public void Kneel(int nearSide, PetPose p)
        {
            Idle(0f, p);
            for (int side = 0; side < 2; side++)
            {
                for (int i = 0; i < 4; i++)
                {
                    Set(p, legF[side, i], Rot(X, Arr(m.kneelFront, i, 0f)));
                }

                for (int i = 0; i < 3; i++)
                {
                    Set(p, legR[side, i], Rot(X, Arr(m.kneelRear, i, 0f)));
                }
            }

            Set(p, Body, Rot(X, m.kneelPitch));
            Set(p, neck1, Rot(Y, nearSide == Left ? -10f : 10f) * Rot(X, -8f));
            Set(p, head, Rot(X, 4f));
            p.Root = new Vector3(0f, -(m.kneelDrop > 0f ? m.kneelDrop : 0.5f), 0f);
            SetWing(p, 1 - nearSide, Fold);
            SetWing(p, nearSide, Ramp);
        }
    }
}
