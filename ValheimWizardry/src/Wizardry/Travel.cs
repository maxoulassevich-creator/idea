using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Wizardry
{
    /// <summary>Apparition, leaps, the grappling charm, flight, the time-turner and the switching spell.</summary>
    internal static class Mobility
    {
        private struct Snap
        {
            public float T, Hp;
            public Vector3 Pos;
        }

        private static readonly List<Snap> s_history = new List<Snap>();
        private static float s_record;
        public static float FlightUntil, SafeLandingUntil;

        public static bool Flying { get { return Time.time < FlightUntil; } }

        public static void Update(Player p)
        {
            s_record -= Time.deltaTime;
            if (s_record <= 0f && p != null && !p.IsDead())
            {
                s_record = 0.25f;
                s_history.Add(new Snap { T = Time.time, Pos = p.transform.position, Hp = p.GetHealth() });
                while (s_history.Count > 0 && Time.time - s_history[0].T > 12f)
                {
                    s_history.RemoveAt(0);
                }
            }

            if (p != null && Time.time < SafeLandingUntil)
            {
                p.m_maxAirAltitude = p.transform.position.y;
            }
        }

        public static void Blink(Player caster, WizardState w, CastContext c, float dist, bool sideAlong)
        {
            Vector3 from = caster.transform.position;
            bool home = caster.IsCrouching() && c.Spec.Has("hold");
            if (Anchor.Covers(from, "noapp"))
            {
                Casting.Message(Texts.L("Здесь наложены антиаппарационные чары.", "Anti-Apparition Charm: you cannot disapparate here."));
                return;
            }

            if (home)
            {
                Home(caster, w, sideAlong);
                return;
            }

            float range = dist * (1f + w.Stats.Get(Stat.range) / 100f) * c.SpeedMult;
            Vector3 to = SpellOps.GroundAt(c, range) + Vector3.up * 0.2f;
            if (Anchor.Covers(to, "noapp"))
            {
                Casting.Message(Texts.L("Место защищено от аппарации.", "That place is protected against Apparition."));
                return;
            }

            Move(caster, from, to, sideAlong);
            Splinch(caster, w);
        }

        private static void Home(Player caster, WizardState w, bool sideAlong)
        {
            var profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
            if (profile == null)
            {
                return;
            }

            if (!caster.IsTeleportable(false) && !ModConfig.PortkeyMetal.Value)
            {
                Casting.Message(Texts.L("Металл и руда не дают трансгрессировать далеко.", "Metal and ore stop a long Apparition."));
                return;
            }

            Vector3 home = profile.HaveCustomSpawnPoint() ? profile.GetCustomSpawnPoint() : profile.GetHomePoint();
            Fx.Send(FxKind.Blink, caster.transform.position, Vector3.up, "apparition", 0f);
            caster.TeleportTo(home + Vector3.up * 0.5f, caster.transform.rotation, true);
            Casting.Message(Texts.L("Трансгрессия домой.", "Apparating home."));
        }

        private static void Move(Player caster, Vector3 from, Vector3 to, bool sideAlong)
        {
            Fx.Send(FxKind.Blink, from, Vector3.up, "apparition", 0f);
            if (sideAlong)
            {
                foreach (var c in Character.GetAllCharacters())
                {
                    if (c != caster && !c.IsDead() && Vector3.Distance(c.transform.position, from) < 4f && Combat.IsAlly(caster, c))
                    {
                        Vector3 off = c.transform.position - from;
                        c.TeleportTo(to + off, c.transform.rotation, false);
                    }
                }
            }

            caster.transform.position = to;
            if (caster.m_body != null)
            {
                caster.m_body.position = to;
                caster.m_body.velocity = Vector3.zero;
            }

            caster.m_maxAirAltitude = to.y;
            Fx.Send(FxKind.Blink, to, Vector3.up, "apparition", 0f);
        }

        /// <summary>Splinching: one Apparition in twenty tears off a little health, unless the wizard is careful.</summary>
        private static void Splinch(Player caster, WizardState w)
        {
            if (w.Stats.Has("safe_apparate") || Random.value > 0.05f)
            {
                return;
            }

            var hit = new HitData();
            hit.m_damage.m_damage = caster.GetMaxHealth() * 0.1f;
            hit.m_point = caster.GetCenterPoint();
            caster.Damage(hit);
            Casting.Message(Texts.L("Расщепление! Вы оставили кусочек себя.", "Splinched! You left a bit of yourself behind."));
        }

        public static void Leap(Player caster, float height)
        {
            if (caster.m_body == null)
            {
                return;
            }

            Vector3 fwd = caster.transform.forward;
            caster.m_body.velocity = Vector3.up * Mathf.Sqrt(2f * 9.81f * height) + fwd * 4f;
            SafeLandingUntil = Time.time + 4f;
            Fx.Send(FxKind.Burst, caster.transform.position, Vector3.up, "ascendio", 1.5f);
        }

        public static void Grapple(Player caster, CastContext c, float range)
        {
            Vector3 end;
            range *= c.SpeedMult;
            var t = SpellOps.RayTarget(c, range, out end);
            if (t != null || caster.m_body == null)
            {
                return; // creatures are pulled by the "pull" operation
            }

            Vector3 to = end - caster.transform.position;
            if (to.magnitude < 2f || to.magnitude > range + 5f)
            {
                return;
            }

            caster.m_body.velocity = to.normalized * Mathf.Min(28f, 8f + to.magnitude) + Vector3.up * 4f;
            SafeLandingUntil = Time.time + 4f;
            Fx.Send(FxKind.Beam, Wands.Tip(caster), end - Wands.Tip(caster), "carpe_retractum", 0.4f);
        }

        public static void Flight(Player caster, float seconds)
        {
            FlightUntil = Time.time + seconds;
            SafeLandingUntil = FlightUntil + 3f;
            Fx.Send(FxKind.Buff, caster.transform.position, Vector3.up, "unsupported_flight", 0f);
            Casting.Message(Texts.L("Полёт: прыжок — вверх, присесть — вниз.", "Flight: jump to rise, crouch to sink."));
        }

        public static void Rewind(Player caster, float seconds)
        {
            if (s_history.Count == 0)
            {
                return;
            }

            var snap = s_history[0];
            foreach (var s in s_history)
            {
                if (Time.time - s.T <= seconds)
                {
                    snap = s;
                    break;
                }
            }

            Fx.Send(FxKind.Blink, caster.transform.position, Vector3.up, "hour_reversal", 0f);
            caster.transform.position = snap.Pos;
            if (caster.m_body != null)
            {
                caster.m_body.position = snap.Pos;
                caster.m_body.velocity = Vector3.zero;
            }

            caster.m_maxAirAltitude = snap.Pos.y;
            if (snap.Hp > caster.GetHealth())
            {
                caster.Heal(snap.Hp - caster.GetHealth(), true);
            }

            Fx.Send(FxKind.Ceremony, snap.Pos, Vector3.up, "hour_reversal", 0f, "time");
            s_history.Clear();
        }

        public static void Swap(Player caster, CastContext c)
        {
            Vector3 end;
            var t = SpellOps.RayTarget(c, 20f * (1f + c.W.Stats.Get(Stat.range) / 100f), out end);
            if (t == null || t.IsBoss())
            {
                Casting.Message(Texts.L("Нет цели для обмена.", "Nothing to switch with."));
                return;
            }

            Vector3 a = caster.transform.position;
            Vector3 b = t.transform.position;
            t.TeleportTo(a, t.transform.rotation, false);
            Move(caster, a, b, false);
        }
    }

    /// <summary>Unsupported Flight: the local player flies where they look while the spell lasts.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.CustomFixedUpdate))]
    internal static class FlightPatch
    {
        private static void Postfix(Character __instance)
        {
            if (!Mobility.Flying || __instance != Player.m_localPlayer || __instance.m_body == null || __instance.IsDead())
            {
                return;
            }

            var p = (Player)__instance;
            float speed = p.m_run ? 14f : 8f;
            Vector3 v = p.m_moveDir * speed;
            bool up = ZInput.GetButton("Jump") || ZInput.GetButton("JoyJump");
            bool down = ZInput.GetButton("Crouch") || ZInput.GetButton("JoyCrouch");
            v.y = up ? 6f : down ? -6f : Mathf.Sin(Time.time * 2f) * 0.3f;
            p.m_body.useGravity = false;
            p.m_body.velocity = Vector3.Lerp(p.m_body.velocity, v, 0.2f);
            p.m_maxAirAltitude = p.transform.position.y;
            if (Time.frameCount % 20 == 0)
            {
                Fx.Rising(p.transform.position, 0.3f, new Color(0.8f, 0.85f, 1f), 0.5f, 6);
            }
        }
    }

    /// <summary>The rowing and wind charms push the ship the local player is on (when this game steers it).</summary>
    internal static class Ships
    {
        private static float s_rowUntil, s_windUntil;
        private static bool s_wind;

        public static void Row(Player caster, float seconds)
        {
            if (Ship.GetLocalShip() == null)
            {
                Casting.Message(Texts.L("Вы не на корабле.", "You are not on a ship."));
                return;
            }

            s_rowUntil = Time.time + seconds;
            Casting.Message(Texts.L("Вёсла гребут сами.", "The oars row by themselves."));
        }

        public static void Wind(Player caster, float seconds)
        {
            if (Ship.GetLocalShip() == null)
            {
                return;
            }

            s_windUntil = Time.time + seconds;
            Casting.Message(Texts.L("Попутный ветер.", "A fair wind."));
        }

        public static void Update()
        {
            var ship = Ship.GetLocalShip();
            if (Time.time < s_rowUntil && ship != null && ship.m_nview != null && ship.m_nview.IsOwner() && ship.m_body != null)
            {
                ship.m_body.AddForce(ship.transform.forward * 3.5f, ForceMode.Acceleration);
            }

            bool wind = Time.time < s_windUntil && ship != null && EnvMan.instance != null;
            if (wind)
            {
                Vector3 f = ship.transform.forward;
                float angle = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
                EnvMan.instance.SetDebugWind(angle, 1f);
                s_wind = true;
            }
            else if (s_wind)
            {
                s_wind = false;
                if (EnvMan.instance != null)
                {
                    EnvMan.instance.ResetDebugWind();
                }
            }
        }
    }

    /// <summary>Weather charms: every game near the caster switches to the chosen weather for a while.</summary>
    internal static class Weather
    {
        private static readonly string[] s_cycle = { "Clear", "Rain", "Misty", "ThunderStorm", "Snow" };
        private static int s_next;
        private static float s_until;
        private static string s_forced;

        public static void Cast(Player caster, string env, float seconds)
        {
            if (env == "choose")
            {
                env = s_cycle[s_next % s_cycle.Length];
                s_next++;
            }

            Fx.Send(FxKind.Weather, caster.transform.position, Vector3.up, "", seconds, env);
            Casting.Message(Texts.L("Погода: ", "Weather: ") + env);
        }

        public static void Apply(string env, float seconds, Vector3 pos)
        {
            var me = Player.m_localPlayer;
            if (EnvMan.instance == null || me == null || Vector3.Distance(me.transform.position, pos) > 250f)
            {
                return;
            }

            if (EnvMan.instance.GetEnv(env) == null)
            {
                return;
            }

            EnvMan.instance.SetForceEnvironment(env);
            s_forced = env;
            s_until = Time.time + seconds;
        }

        public static void Update()
        {
            if (s_forced != null && Time.time > s_until && EnvMan.instance != null)
            {
                if (EnvMan.instance.m_forceEnv == s_forced)
                {
                    EnvMan.instance.SetForceEnvironment("");
                }

                s_forced = null;
            }
        }
    }
}
