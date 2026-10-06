using System.Collections.Generic;
using UnityEngine;

namespace FlyingPets
{
    /// <summary>
    ///     The active abilities (secondary attack in the saddle): the pegasus' thunder hoof and the raven's
    ///     talon grab (the dragon's tidal breath is in FlyingPet.Dragon, the prowler's sandstorm in
    ///     FlyingPet.Prowler), and the valkyrie's catch. Run by the rider's game, which owns the pet; the effects
    ///     are sent to everybody with FP_Fx.
    /// </summary>
    public partial class FlyingPet
    {
        internal const int SkillNone = 0;
        internal const int SkillDive = 1;
        internal const int SkillHold = 2;

        private const int FxThunder = 1;
        private const int FxSpring = 2;
        private const int FxGrab = 3;
        private const int FxCatch = 4;

        private const float DiveSpeed = 32f;
        private static readonly string[] ThunderExtras = { "fx_eikthyr_stomp" };
        private static readonly int ZSkill = "fp_skill".GetStableHashCode();

        // cooldowns of the local player
        private static float s_thunderReady;
        private static float s_grabReady;
        private static float s_trollSize = -1f;
        private static readonly List<Character> s_hits = new List<Character>();

        private int m_skill;          // owner: what the pet is doing
        private int m_netSkill;       // everybody: the same, from the ZDO (for the animation)
        private float m_skillTime;
        private float m_diveStartY;

        private Character m_prey;
        private ZNetView m_preyView;
        private Rigidbody m_preyBody;
        private bool m_preyKinematic;
        private RigidbodyInterpolation m_preyInterp;
        private Vector3 m_preyFrom;
        private float m_preyTop;
        private Collider[] m_preyColliders;

        internal PetAsset Asset
        {
            get { return m_asset; }
        }

        internal bool IsAround
        {
            get { return m_ready && !m_byeShown && !m_destroyed && m_nview != null && m_nview.IsValid(); }
        }

        internal bool IsFlying
        {
            get { return IsAround && (m_nview.IsOwner() ? m_state : m_netState) == StateAir; }
        }

        internal bool Has(string ability)
        {
            return m_asset != null && m_asset.Abilities.Contains(ability);
        }

        /// <summary>The pet the given (local) player sits on, or null.</summary>
        internal static FlyingPet MountOf(Player player)
        {
            if (player == null)
            {
                return null;
            }

            foreach (var pet in Instances)
            {
                if (pet != null && pet.m_localRider == player)
                {
                    return pet;
                }
            }

            return null;
        }

        private int SkillState
        {
            get { return m_nview.IsOwner() ? m_skill : m_netSkill; }
        }

        private void SetSkill(int skill)
        {
            m_skill = skill;
            m_skillTime = 0f;
            if (m_nview != null && m_nview.IsValid() && m_nview.IsOwner() && m_nview.GetZDO().GetInt(ZSkill, 0) != skill)
            {
                m_nview.GetZDO().Set(ZSkill, skill);
            }
        }

        private void SendFx(int kind, Vector3 pos, Vector3 args)
        {
            if (m_nview != null && m_nview.IsValid())
            {
                m_nview.InvokeRPC(ZNetView.Everybody, "FP_Fx", kind, pos, args);
            }
        }

        private void RPC_Fx(long sender, int kind, Vector3 pos, Vector3 args)
        {
            try
            {
                switch (kind)
                {
                    case FxThunder:
                        Fx.Thunder(pos, args.x, args.y);
                        Util.SpawnEffect(ThunderExtras, pos, Quaternion.identity);
                        break;
                    case FxSpring:
                        Fx.Spring(pos, args.x, args.y, args.z);
                        break;
                    case FxGrab:
                        Fx.Feathers(pos, new Color(0.07f, 0.07f, 0.09f, 1f), 16);
                        break;
                    case FxCatch:
                        Fx.Catch(pos);
                        Util.SpawnEffect(Util.SummonEffects, pos + Vector3.up * m_scale, Quaternion.identity);
                        break;
                    case FxBreath:
                        BreathFx(args.x, args.y);
                        break;
                    case FxSplash:
                        Fx.Splash(pos, args.x * Mathf.Max(1f, m_scale));
                        break;
                    case FxStorm:
                        Fx.Sandstorm(transform, args.x, args.y, Mathf.Max(0.5f, m_scale));
                        break;
                }
            }
            catch (System.Exception e)
            {
                FlyingPetsPlugin.Log.LogDebug("Effect " + kind + " failed: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ input (rider's game)
        private void SkillInput(Player player)
        {
            if (!Util.InputAllowed() || !(ZInput.GetButtonDown("SecondaryAttack") || ZInput.GetButtonDown("JoySecondaryAttack")))
            {
                return;
            }

            string active = PetAbility.ActiveOf(m_asset.Abilities);
            if (active == null)
            {
                return;
            }

            if (m_skill == SkillHold)
            {
                ReleasePrey(true);
                return;
            }

            bool thunder = active == PetAbility.ThunderHoof;
            bool breath = active == PetAbility.TidalBreath;
            bool storm = active == PetAbility.Sandstorm;
            bool enabled = thunder ? ModConfig.ThunderHoof.Value
                : breath ? ModConfig.TidalBreath.Value
                : storm ? ModConfig.Sandstorm.Value
                : ModConfig.TalonGrab.Value;
            if (m_skill != SkillNone || m_seq != SeqNone || !enabled)
            {
                return;
            }

            if (m_state != StateAir && !breath && !storm) // the breath and the storm work on the ground too
            {
                player.Message(MessageHud.MessageType.Center, Texts.Get("fp_msg_need_air"));
                return;
            }

            float ready = thunder ? s_thunderReady : breath ? s_breathReady : storm ? s_stormReady : s_grabReady;
            if (Time.time < ready)
            {
                string name = thunder ? "fp_se_thunder" : breath ? "fp_se_breath" : storm ? "fp_se_storm" : "fp_se_grab";
                player.Message(MessageHud.MessageType.Center, Texts.Get("fp_msg_cooldown", Texts.Get(name), Mathf.CeilToInt(ready - Time.time)));
                return;
            }

            if (thunder)
            {
                BeginDive(player);
            }
            else if (breath)
            {
                BeginBreath(player);
            }
            else if (storm)
            {
                BeginStorm(player);
            }
            else
            {
                TryGrab(player);
            }
        }

        /// <summary>The skill cannot go on (the rider left, the pet landed...).</summary>
        private void StopSkill()
        {
            if (m_skill == SkillHold || m_prey != null)
            {
                ReleasePrey(true);
            }
            else if (m_skill != SkillNone)
            {
                SetSkill(SkillNone);
            }
        }

        // ------------------------------------------------------------------ thunder hoof
        private void BeginDive(Player player)
        {
            SetSkill(SkillDive);
            m_diveStartY = m_body.position.y;
            m_cruise = false;
            float cd = Mathf.Max(1f, ModConfig.ThunderCooldown.Value);
            s_thunderReady = Time.time + cd;
            AbilityEffects.Timer(player, AbilityEffects.Thunder, cd);
        }

        /// <summary>Overrides the rider's controls while diving: straight down, keeping the speed.</summary>
        private void DiveControls(ref Controls c)
        {
            c.Forward = Mathf.Max(c.Forward, Mathf.Max(m_speed, 10f));
            c.Strafe = 0f;
            c.Climb = -DiveSpeed;
            c.TakeOff = false;
            c.Land = false;
        }

        /// <summary>Called by Integrate every physics step of the dive.</summary>
        private void DiveStep(float dt, Vector3 pos, float alt, float floorY, bool overWater)
        {
            m_skillTime += dt;
            if (alt + Mathf.Min(0f, m_vyAir) * dt <= 0.5f * m_scale)
            {
                DiveImpact(new Vector3(pos.x, floorY, pos.z), overWater);
            }
            else if (m_skillTime > 8f)
            {
                SetSkill(SkillNone); // never reached the ground
            }
        }

        private void DiveImpact(Vector3 at, bool overWater)
        {
            float power = 1f + Mathf.Clamp01((m_diveStartY - at.y) / 40f) * 2f;
            float radius = Mathf.Max(1f, ModConfig.ThunderRadius.Value) * Mathf.Max(1f, m_scale);
            SetSkill(SkillNone);
            StrikeGround(at, power, radius);

            // spring back up off the ground
            m_vyAir = 6f;
            m_takeoffTimer = 0.35f;
            m_speed *= 0.4f;

            SendFx(FxThunder, at, new Vector3(power, radius, 0f));
            float heal = ModConfig.SpringHealPerSecond.Value;
            if (!overWater && heal > 0f)
            {
                SendFx(FxSpring, at, new Vector3(Mathf.Max(1f, ModConfig.SpringDuration.Value), 3.5f * Mathf.Max(1f, m_scale), heal));
            }
        }

        private void StrikeGround(Vector3 at, float power, float radius)
        {
            var rider = m_localRider;
            float damage = Mathf.Max(0f, ModConfig.ThunderDamage.Value) * power;
            s_hits.Clear();
            Character.GetCharactersInRange(at, radius, s_hits);
            foreach (var c in s_hits)
            {
                if (c == null || c.IsPlayer() || c.IsDead() || c.IsTamed() || Passives.IsHeld(c))
                {
                    continue;
                }

                Vector3 dir = c.transform.position - at;
                dir.y = 0f;
                float dist = dir.magnitude;
                dir = dist > 0.05f ? dir / dist : transform.forward;
                float k = 1f - 0.5f * Mathf.Clamp01(dist / radius);
                var hit = new HitData();
                hit.m_damage.m_lightning = damage * 0.6f * k;
                hit.m_damage.m_blunt = damage * 0.4f * k;
                hit.m_point = c.GetCenterPoint();
                hit.m_dir = dir;
                hit.m_pushForce = 40f * power;
                hit.m_staggerMultiplier = 100f; // always staggers
                hit.m_hitType = HitData.HitType.PlayerHit;
                if (rider != null)
                {
                    hit.SetAttacker(rider);
                }

                c.Damage(hit);
            }
        }

        // ------------------------------------------------------------------ talon grab
        private void TryGrab(Player player)
        {
            Vector3 talons = transform.position;
            float s = Mathf.Max(1f, m_scale);
            float reach = 3.5f * s;
            float maxSize = MaxPreySize() * s;
            Character best = null, tooBig = null;
            float bestD = float.MaxValue;
            foreach (var c in Character.GetAllCharacters())
            {
                if (c == null || c.IsPlayer() || c.IsDead() || c.IsBoss() || Passives.IsHeld(c))
                {
                    continue;
                }

                Vector3 cp = c.transform.position;
                float dx = cp.x - talons.x, dz = cp.z - talons.z;
                float h2 = dx * dx + dz * dz;
                if (h2 > reach * reach)
                {
                    continue;
                }

                float top = c.GetTopPoint().y;
                if (top < talons.y - 5f * s || cp.y > talons.y + 1f * s)
                {
                    continue;
                }

                if (BodySize(c) > maxSize)
                {
                    tooBig = c;
                    continue;
                }

                float d = h2 + (talons.y - top) * (talons.y - top);
                if (d < bestD)
                {
                    bestD = d;
                    best = c;
                }
            }

            if (best == null)
            {
                player.Message(MessageHud.MessageType.Center, tooBig != null
                    ? Texts.Get("fp_msg_grab_big", tooBig.GetHoverName())
                    : Texts.Get("fp_msg_grab_none"));
                return;
            }

            Grab(player, best);
        }

        private static float BodySize(Character c)
        {
            var cap = c.GetComponent<CapsuleCollider>();
            if (cap == null)
            {
                return 0.5f;
            }

            Vector3 ls = c.transform.lossyScale;
            return cap.radius * Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.z));
        }

        /// <summary>The biggest body the raven lifts: the setting, or a troll measured from the game.</summary>
        private static float MaxPreySize()
        {
            float set = ModConfig.GrabMaxRadius.Value;
            if (set > 0f)
            {
                return set;
            }

            if (s_trollSize < 0f)
            {
                s_trollSize = 1.5f;
                var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("Troll") : null;
                var cap = prefab != null ? prefab.GetComponent<CapsuleCollider>() : null;
                if (cap != null)
                {
                    Vector3 ls = prefab.transform.localScale;
                    s_trollSize = cap.radius * Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.z));
                }

                FlyingPetsPlugin.Log.LogInfo("Talon grab: biggest prey radius " + s_trollSize.ToString("0.00") + " m (a troll)");
            }

            return s_trollSize * 1.05f;
        }

        private void Grab(Player player, Character c)
        {
            var view = c.GetComponent<ZNetView>();
            if (view == null || !view.IsValid())
            {
                return;
            }

            view.ClaimOwnership();
            m_prey = c;
            m_preyView = view;
            m_preyBody = c.GetComponent<Rigidbody>();
            if (m_preyBody != null)
            {
                m_preyKinematic = m_preyBody.isKinematic;
                m_preyInterp = m_preyBody.interpolation;
                if (!m_preyBody.isKinematic)
                {
                    m_preyBody.linearVelocity = Vector3.zero;
                }

                m_preyBody.isKinematic = true;
                m_preyBody.interpolation = RigidbodyInterpolation.None;
            }

            m_preyFrom = c.transform.position;
            m_preyTop = Mathf.Clamp(c.GetTopPoint().y - c.transform.position.y, 0.3f, 8f);
            m_preyColliders = c.GetComponentsInChildren<Collider>();
            IgnorePrey(m_capsule, m_preyColliders, true);
            Passives.Hold(c);
            SetSkill(SkillHold);

            AbilityEffects.Timer(player, AbilityEffects.Hold, Mathf.Max(3f, ModConfig.GrabMaxHold.Value));
            player.Message(MessageHud.MessageType.Center, Texts.Get("fp_msg_grab", c.GetHoverName()));
            SendFx(FxGrab, transform.position, Vector3.zero);
        }

        private static void IgnorePrey(Collider mine, Collider[] theirs, bool ignore)
        {
            if (mine == null || theirs == null)
            {
                return;
            }

            foreach (var col in theirs)
            {
                if (col != null)
                {
                    Physics.IgnoreCollision(mine, col, ignore);
                }
            }
        }

        /// <summary>Carries the prey under the talons (rider's game, after the pet has moved).</summary>
        private void UpdatePrey(float dt)
        {
            if (m_skill != SkillHold && m_prey == null)
            {
                return;
            }

            if (m_skill != SkillHold || m_prey == null || m_preyView == null || !m_preyView.IsValid() || m_prey.IsDead() ||
                !m_preyView.IsOwner() || !m_nview.IsOwner())
            {
                ReleasePrey(false);
                return;
            }

            m_skillTime += dt;
            Vector3 hold = transform.position + Vector3.up * (0.05f * m_scale - m_preyTop);
            float k = PetAnimator.Smooth(Mathf.Clamp01(m_skillTime / 0.35f));
            Vector3 p = Vector3.Lerp(m_preyFrom, hold, k);
            Quaternion r = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            m_prey.transform.SetPositionAndRotation(p, r);
            if (m_preyBody != null)
            {
                m_preyBody.position = p;
                m_preyBody.rotation = r;
            }

            if (m_skillTime > Mathf.Max(3f, ModConfig.GrabMaxHold.Value))
            {
                ReleasePrey(true);
            }
        }

        private void ReleasePrey(bool tell)
        {
            var c = m_prey;
            var body = m_preyBody;
            var colliders = m_preyColliders;
            m_prey = null;
            m_preyView = null;
            m_preyBody = null;
            m_preyColliders = null;
            if (m_skill == SkillHold)
            {
                SetSkill(SkillNone);
            }

            Passives.Unhold(c);
            PreyWatch.RestoreLater(m_capsule, colliders);
            if (c != null)
            {
                if (body != null)
                {
                    body.isKinematic = m_preyKinematic;
                    body.interpolation = m_preyInterp;
                    if (!body.isKinematic)
                    {
                        body.linearVelocity = m_vel * 0.8f;
                    }
                }

                var by = m_localRider != null ? m_localRider : Player.m_localPlayer;
                PreyWatch.Fall(c, by);
                if (tell && m_localRider != null)
                {
                    m_localRider.Message(MessageHud.MessageType.Center, Texts.Get("fp_msg_drop", c.GetHoverName()));
                }
            }

            var player = m_localRider != null ? m_localRider : Player.m_localPlayer;
            float cd = Mathf.Max(1f, ModConfig.GrabCooldown.Value);
            s_grabReady = Time.time + cd;
            if (player != null)
            {
                AbilityEffects.ClearTimer(player, AbilityEffects.Hold);
                AbilityEffects.Timer(player, AbilityEffects.Grab, cd);
            }
        }

        // ------------------------------------------------------------------ valkyrie's catch
        /// <summary>
        ///     Swoops in under the falling owner and catches them into the saddle (no boarding sequence).
        ///     Returns false when the pet cannot do it right now.
        /// </summary>
        internal bool TryCatch(Player player)
        {
            if (!IsAround || m_localRider != null || RiderId != 0L || m_seq != SeqNone || player == null ||
                player.IsAttached() || player.GetDoodadController() != null || m_nview.GetZDO().GetBool(ZBye, false))
            {
                return false;
            }

            m_nview.ClaimOwnership();
            OnBecameOwner();
            m_wasOwner = true;

            Vector3 look = player.transform.forward;
            look.y = 0f;
            float yaw = look.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(look).eulerAngles.y : transform.eulerAngles.y;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            float lift = ModConfig.RiderSeatHeight.Value + 0.05f;
            Vector3 seat = PetAsset.V3(m_asset.Data.seat, new Vector3(0f, 2f, 0f)) * m_scale + Vector3.up * lift;
            Vector3 p = player.transform.position - rot * seat;

            var pbody = player.GetComponent<Rigidbody>();
            Vector3 pv = pbody != null ? pbody.linearVelocity : Vector3.zero;
            m_yaw = yaw;
            m_pitch = 0f;
            m_roll = 0f;
            m_body.position = p;
            m_body.rotation = rot;
            transform.SetPositionAndRotation(p, rot);
            m_body.linearVelocity = Vector3.zero;
            m_lastSimPos = p;
            m_state = StateAir;
            m_vyAir = Mathf.Clamp(pv.y * 0.4f, -8f, 0f);
            m_speed = Mathf.Clamp(Vector3.Dot(new Vector3(pv.x, 0f, pv.z), rot * Vector3.forward), 0f, 12f);
            m_side = 0f;
            m_takeoffTimer = 0f;
            Physics.SyncTransforms();

            m_nview.GetZDO().Set(ZRider, player.GetPlayerID());
            WriteState();
            m_localRider = player;
            m_attachedLocal = false;
            m_allowRelease = false;
            m_wantDismount = false;
            m_releaseSent = false;
            m_cruise = false;
            player.StartDoodadControl(this);
            Attach(player, RideAnimation);
            CameraIn();

            SendFx(FxCatch, p, Vector3.zero);
            player.Message(MessageHud.MessageType.Center, Texts.Get("fp_msg_caught"));
            return true;
        }
    }

    /// <summary>Prey the raven let go of: hurt by the fall when it lands, and touching the raven again later.</summary>
    internal static class PreyWatch
    {
        private sealed class Falling
        {
            public Character Who;
            public ZNetView View;
            public Player By;
            public float MaxY;
            public float LastY;
            public float Time;
            public float Still;
        }

        private sealed class Ignored
        {
            public Collider Mine;
            public Collider[] Theirs;
            public float At;
        }

        private static readonly List<Falling> s_falling = new List<Falling>();
        private static readonly List<Ignored> s_ignored = new List<Ignored>();

        public static void Fall(Character c, Player by)
        {
            var view = c.GetComponent<ZNetView>();
            float y = c.transform.position.y;
            s_falling.Add(new Falling { Who = c, View = view, By = by, MaxY = y, LastY = y });
        }

        public static void RestoreLater(Collider mine, Collider[] theirs)
        {
            if (mine != null && theirs != null)
            {
                s_ignored.Add(new Ignored { Mine = mine, Theirs = theirs, At = UnityEngine.Time.time + 1.5f });
            }
        }

        public static void Tick(float dt)
        {
            for (int i = s_ignored.Count - 1; i >= 0; i--)
            {
                var e = s_ignored[i];
                if (UnityEngine.Time.time < e.At)
                {
                    continue;
                }

                s_ignored.RemoveAt(i);
                if (e.Mine == null)
                {
                    continue;
                }

                foreach (var col in e.Theirs)
                {
                    if (col != null)
                    {
                        Physics.IgnoreCollision(e.Mine, col, false);
                    }
                }
            }

            for (int i = s_falling.Count - 1; i >= 0; i--)
            {
                var f = s_falling[i];
                if (f.Who == null || f.View == null || !f.View.IsValid() || f.Who.IsDead() || Passives.IsHeld(f.Who))
                {
                    s_falling.RemoveAt(i);
                    continue;
                }

                float y = f.Who.transform.position.y;
                f.MaxY = Mathf.Max(f.MaxY, y);
                f.Time += dt;
                f.Still = Mathf.Abs(y - f.LastY) < 0.01f ? f.Still + dt : 0f;
                f.LastY = y;
                bool water = f.Who.InWater();
                bool landed = (f.Time > 0.2f && f.Who.IsOnGround()) || water || f.Still > 0.5f || f.Time > 12f;
                if (!landed)
                {
                    continue;
                }

                s_falling.RemoveAt(i);
                float h = f.MaxY - y;
                if (water || f.Who.IsTamed() || h <= 6f)
                {
                    continue; // a splash, a pet of somebody's, or set down gently
                }

                float lethal = Mathf.Max(7f, ModConfig.GrabLethalHeight.Value);
                float frac = Mathf.Clamp01((h - 6f) / (lethal - 6f));
                var hit = new HitData();
                hit.m_damage.m_damage = f.Who.GetMaxHealth() * frac + (frac >= 1f ? 10f : 0f);
                hit.m_point = f.Who.transform.position;
                hit.m_dir = Vector3.down;
                hit.m_hitType = HitData.HitType.Fall;
                if (f.By != null)
                {
                    hit.SetAttacker(f.By);
                }

                f.Who.Damage(hit);
            }
        }
    }
}
