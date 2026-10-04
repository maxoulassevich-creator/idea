using System;
using System.Collections.Generic;
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

    /// <summary>
    ///     Wing pose of the right wing (the left one is mirrored): rotations of the arm, forearm and hand
    ///     bones, and how far the feathers are folded (0 = spread as modelled, 1 = folded).
    /// </summary>
    internal struct WingShape
    {
        public Quaternion Arm;
        public Quaternion Fore;
        public Quaternion Hand;
        public float Fan;
    }

    /// <summary>
    ///     Procedural animation of a winged mount: a quadruped (pegasus) or a bird (raven). Every pose is
    ///     built from rotations about the bone pivots (bones rest unrotated), authored for the right side
    ///     and mirrored for the left with axis (x, -y, -z). Poses are blended per bone with quaternion
    ///     slerp. Feather bones carry their own folded rotation and follow the fan of their wing or tail.
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
        private readonly int[] scapula = new int[2];

        // feathers of each wing and of the tail, with their fully folded local rotations
        private readonly int[][] featherBones = new int[2][];
        private readonly Quaternion[][] featherFold = new Quaternion[2][];
        private readonly int[] tailFeathers;
        private readonly Quaternion[] tailFold;

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

                scapula[s] = asset.Bone("wing_" + sides[s] + "s");
            }

            // feathers: bones with a fold rotation, grouped by the wing (or the tail) they hang from
            var fb = new[] { new List<int>(), new List<int>() };
            var fq = new[] { new List<Quaternion>(), new List<Quaternion>() };
            var tb = new List<int>();
            var tq = new List<Quaternion>();
            var group = new int[BoneCount];
            for (int i = 0; i < BoneCount; i++)
            {
                group[i] = -1;
                var b = m.bones[i];
                if (b == null || b.fold == null || b.fold.Length < 4)
                {
                    continue;
                }

                for (int g = i; g >= 0; g = asset.BoneParents[g])
                {
                    string n = asset.BoneNames[g];
                    if (n.StartsWith("wing_R", StringComparison.Ordinal))
                    {
                        group[i] = 0;
                        break;
                    }

                    if (n.StartsWith("wing_L", StringComparison.Ordinal))
                    {
                        group[i] = 1;
                        break;
                    }

                    if (n.StartsWith("tail", StringComparison.Ordinal))
                    {
                        group[i] = 2;
                        break;
                    }
                }

                var q = AxisAngle(b.fold, 0);
                if (group[i] == 0 || group[i] == 1)
                {
                    fb[group[i]].Add(i);
                    fq[group[i]].Add(q);
                }
                else if (group[i] == 2)
                {
                    tb.Add(i);
                    tq.Add(q);
                }
            }

            tailFeathers = tb.ToArray();
            tailFold = tq.ToArray();
            for (int s = 0; s < 2; s++)
            {
                featherBones[s] = fb[s].ToArray();
                featherFold[s] = fq[s].ToArray();
                var wb = new List<int> { scapula[s], Wing[s, 0], Wing[s, 1], Wing[s, 2] };
                wb.AddRange(featherBones[s]);
                WingBones[s] = wb.ToArray();
            }

            var core = new List<int>();
            var legs = new List<int>();
            for (int i = 0; i < BoneCount; i++)
            {
                string n = asset.BoneNames[i];
                if (n.StartsWith("leg_", StringComparison.Ordinal))
                {
                    legs.Add(i);
                }
                else if (!n.StartsWith("wing_", StringComparison.Ordinal) && group[i] != 0 && group[i] != 1)
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

        private static Quaternion AxisAngle(float[] a, int o)
        {
            var axis = new Vector3(a[o], a[o + 1], a[o + 2]);
            return axis.sqrMagnitude > 1e-12f && Mathf.Abs(a[o + 3]) > 1e-6f ? Quaternion.AngleAxis(a[o + 3], axis) : Quaternion.identity;
        }

        private static WingShape Shape(float[] aa, Vector3 hand, float handDeg)
        {
            if (aa != null && aa.Length >= 12)
            {
                return new WingShape
                {
                    Arm = AxisAngle(aa, 0),
                    Fore = AxisAngle(aa, 4),
                    Hand = AxisAngle(aa, 8),
                    Fan = aa.Length >= 13 ? Mathf.Clamp01(aa[12]) : 0f,
                };
            }

            bool ok = aa != null && aa.Length >= 4;
            return new WingShape
            {
                Arm = ok ? AxisAngle(aa, 0) : Quaternion.identity,
                Fore = Quaternion.identity,
                Hand = Quaternion.AngleAxis(handDeg, hand),
                Fan = 0f,
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

        /// <summary>A right-side rotation for the given side (mirrored across x = 0 for the left).</summary>
        private static Quaternion Side(Quaternion q, int side)
        {
            return side == Right ? q : new Quaternion(q.x, -q.y, -q.z, q.w);
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

        /// <summary>0 during the downstroke, rising to 1 in the middle of the recovery stroke.</summary>
        private static float Upstroke(float ph)
        {
            return ph < 0.4f ? 0f : Mathf.Sin(Mathf.PI * (ph - 0.4f) / 0.6f);
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

        // ------------------------------------------------------------------ wings and feathers
        private void SetFan(PetPose p, int side, float fan)
        {
            int[] bones = featherBones[side];
            Quaternion[] fold = featherFold[side];
            for (int k = 0; k < bones.Length; k++)
            {
                p.Q[bones[k]] = Quaternion.Slerp(Quaternion.identity, fold[k], fan);
            }
        }

        private void SetTailFan(PetPose p, float fan)
        {
            for (int k = 0; k < tailFeathers.Length; k++)
            {
                p.Q[tailFeathers[k]] = Quaternion.Slerp(Quaternion.identity, tailFold[k], fan);
            }
        }

        /// <summary>The shoulder rotation; with a scapula helper bone both bones take half of it.</summary>
        private void SetArm(PetPose p, int side, Quaternion q)
        {
            if (scapula[side] >= 0)
            {
                var half = Quaternion.Slerp(Quaternion.identity, q, 0.5f);
                p.Q[scapula[side]] = half;
                Set(p, Wing[side, 0], half);
            }
            else
            {
                Set(p, Wing[side, 0], q);
            }
        }

        public void SetWing(PetPose p, int side, WingShape w)
        {
            SetArm(p, side, Side(w.Arm, side));
            Set(p, Wing[side, 1], Side(w.Fore, side));
            Set(p, Wing[side, 2], Side(w.Hand, side));
            SetFan(p, side, w.Fan);
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

            SetArm(p, side, Side(Quaternion.Slerp(a.Arm, b.Arm, t), side));
            Set(p, Wing[side, 1], Side(Quaternion.Slerp(a.Fore, b.Fore, t), side));
            Set(p, Wing[side, 2], Side(Quaternion.Slerp(a.Hand, b.Hand, t), side));
            SetFan(p, side, Mathf.Lerp(a.Fan, b.Fan, t));
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
            Set(p, tail[0], Rot(Y, 6f * Mathf.Sin(sw)) * Rot(X, m.tailRest));
            Set(p, tail[1], Rot(Y, 8f * Mathf.Sin(sw - 0.7f)));
            Set(p, tail[2], Rot(Y, 10f * Mathf.Sin(sw - 1.4f)));
            Set(p, tail[3], Rot(Y, 12f * Mathf.Sin(sw - 2.1f)));
            SetTailFan(p, Arr(m.tailFan, 0, 1f));
        }

        /// <summary>Flapping flight at wing-beat phase ph (0..1); legs tucked, neck stretched.</summary>
        public void Fly(float ph, float amp, PetPose p)
        {
            p.Reset();
            float s = Stroke(ph);
            float lag = Stroke(Frac(ph - 0.12f));
            float up = Upstroke(ph);
            float[] f = m.flapShape;
            float armSweep = Arr(f, 0, 6f), armBase = Arr(f, 1, 6f), foreLag = Arr(f, 2, 8f);
            float handSweep = Arr(f, 3, -8f), handLag = Arr(f, 4, 18f), handBase = Arr(f, 5, -4f), upFlex = Arr(f, 6, 0f);
            float fan = Arr(m.flyFan, 0, 0f) + Arr(m.flyFan, 1, 0f) * up;
            for (int side = 0; side < 2; side++)
            {
                Vector3 y = Side(Y, side), z = Side(Z, side);
                SetArm(p, side, Rot(y, armSweep * s) * Rot(z, armBase + amp * s));
                Set(p, Wing[side, 1], Rot(y, upFlex * up) * Rot(z, foreLag * lag));
                Set(p, Wing[side, 2], Rot(y, handSweep * s + 1.6f * upFlex * up) * Rot(z, handLag * lag + handBase));
                SetFan(p, side, fan);
                for (int i = 0; i < 4; i++)
                {
                    Set(p, legF[side, i], Rot(X, Arr(m.flyFront, i, 0f)));
                }

                for (int i = 0; i < 3; i++)
                {
                    Set(p, legR[side, i], Rot(X, Arr(m.flyRear, i, 0f)));
                }
            }

            Set(p, Body, Rot(X, m.flyPitch - 2.5f * s));
            Set(p, neck1, Rot(X, Arr(m.neckFly, 0, 22f) + 1.5f * s));
            Set(p, neck2, Rot(X, Arr(m.neckFly, 1, 8f)));
            Set(p, head, Rot(X, Arr(m.neckFly, 2, -18f)));
            float tw = Tau * ph;
            Set(p, tail[0], Rot(X, Arr(m.tailFly, 0, 22f)));
            Set(p, tail[1], Rot(Y, 5f * Mathf.Sin(tw)) * Rot(X, Arr(m.tailFly, 1, 4f)));
            Set(p, tail[2], Rot(Y, 6f * Mathf.Sin(tw - 0.8f)) * Rot(X, Arr(m.tailFly, 1, 4f)));
            Set(p, tail[3], Rot(Y, 7f * Mathf.Sin(tw - 1.6f)) * Rot(X, Arr(m.tailFly, 2, 0f)));
            SetTailFan(p, Arr(m.tailFan, 1, 0f));
            p.Root = new Vector3(0f, 0.07f * s, 0f);
        }

        /// <summary>Gliding with the wings held out and gently trimming.</summary>
        public void Glide(float t, float amp, PetPose p)
        {
            Fly(0.264f, amp, p);
            float fan = Arr(m.flyFan, 0, 0f);
            for (int side = 0; side < 2; side++)
            {
                Vector3 z = Side(Z, side);
                SetArm(p, side, Rot(z, 10f + 2f * Mathf.Sin(1.6f * t)));
                Set(p, Wing[side, 1], Rot(z, 2f));
                Set(p, Wing[side, 2], Rot(z, -6f));
                SetFan(p, side, fan);
            }

            SetTailFan(p, Arr(m.tailFan, 2, 0f));
            p.Root = Vector3.zero;
        }

        /// <summary>
        ///     Walk (4-beat) that turns into a trot (diagonal pairs) with speed; a bird with 'hop' set
        ///     hops instead, both feet together. gaitPhase advances by speed / stride per second;
        ///     negative speeds walk backwards.
        /// </summary>
        public void Walk(float t, float gaitPhase, float speed, PetPose p)
        {
            Idle(t, p);
            float sp = Mathf.Abs(speed);
            float trot = Mathf.Clamp01((sp - 3.6f) / 2.8f);
            float duty = 0.62f - 0.17f * trot;
            float amp = Mathf.Lerp(m.swingWalk > 0f ? m.swingWalk : 17f, m.swingTrot > 0f ? m.swingTrot : 24f, trot);
            float mix = Smooth(trot);
            bool hop = m.hop > 0f;
            // walk: rR 0, fR .25, rL .5, fL .75   trot: fR+rL 0, fL+rR .5   hop: rR+rL .5
            LegPair(p, Right, true, gaitPhase + Mathf.Lerp(0.25f, 0f, mix), duty, amp);
            LegPair(p, Left, true, gaitPhase + Mathf.Lerp(0.75f, 0.5f, mix), duty, amp);
            LegPair(p, Right, false, gaitPhase + Mathf.Lerp(0f, 0.5f, mix), duty, amp);
            LegPair(p, Left, false, gaitPhase + Mathf.Lerp(0.5f, hop ? 0.5f : 0f, mix), duty, amp);
            float nod = Mathf.Sin(2f * Tau * gaitPhase);
            Set(p, neck1, Rot(X, -2f + 3f * nod * (1f - 0.5f * trot)));
            Set(p, head, Rot(X, 2f - 2f * nod));
            p.Root = new Vector3(0f, -0.02f * (1f + trot) * Mathf.Abs(nod), 0f);
            if (hop)
            {
                // both feet leave the ground together and the body bounds
                float ph = Frac(gaitPhase + 0.5f);
                float air = ph >= duty ? Mathf.Sin(Mathf.PI * (ph - duty) / (1f - duty)) : 0f;
                p.Root.y += m.hop * mix * air;
            }
        }

        private void LegPair(PetPose p, int side, bool front, float phase, float duty, float amp)
        {
            float ph = Frac(phase);
            float sw, lift;
            if (ph < duty)
            {
                sw = -1f + 2f * ph / duty; // planted: the foot slides back under the body
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

        /// <summary>Kneeling for a rider on the given side: legs folded, chest low, head turned to the rider.</summary>
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
