using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

namespace Wizardry
{
    internal enum FxKind : byte
    {
        Cast, Bolt, Beam, Hit, Burst, Cone, Status, Heal, Buff, Summon, Blink, Text, Flare, DarkMark, Ceremony, Weather, Poly, Ray
    }

    /// <summary>
    ///     Spell visuals and sounds, made from code (particles, lines, lights, synthesized clips). A cast sends one
    ///     message to everybody; every game plays its own copy, so the effects cost the network almost nothing.
    /// </summary>
    internal static class Fx
    {
        private static readonly Dictionary<string, Material> s_mats = new Dictionary<string, Material>();
        private static readonly Dictionary<string, Color> s_colors = new Dictionary<string, Color>();
        private static readonly Dictionary<string, AudioClip> s_clips = new Dictionary<string, AudioClip>();
        private static Texture2D s_dot, s_streak, s_ring;
        private static AudioMixerGroup s_sfx;
        private static bool s_sfxSearched;
        private const int Rate = 22050;

        public static float Quality { get { return Mathf.Clamp(ModConfig.EffectsQuality.Value, 0.25f, 1f); } }

        // ------------------------------------------------------------------ network
        public static void Send(FxKind kind, Vector3 pos, Vector3 dir, string spell, float value, string extra = "", ZDOID target = default(ZDOID))
        {
            if (ZRoutedRpc.instance == null)
            {
                Play(kind, pos, dir, spell, value, extra, target, true);
                return;
            }

            var pkg = new ZPackage();
            pkg.Write((byte)kind);
            pkg.Write(pos);
            pkg.Write(dir);
            pkg.Write(spell ?? "");
            pkg.Write(value);
            pkg.Write(extra ?? "");
            pkg.Write(target);
            pkg.Write(ZDOMan.GetSessionID());
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, Net.RpcFx, pkg);
        }

        public static void SendStatus(Character target, string key, string variant, float duration)
        {
            var zdo = target != null && target.Nv() != null ? target.Nv().GetZDO() : null;
            if (zdo == null)
            {
                return;
            }

            Send(FxKind.Status, target.transform.position, Vector3.up, key, duration, variant ?? "", zdo.m_uid);
        }

        public static void Receive(ZPackage pkg)
        {
            try
            {
                var kind = (FxKind)pkg.ReadByte();
                var pos = pkg.ReadVector3();
                var dir = pkg.ReadVector3();
                string spell = pkg.ReadString();
                float value = pkg.ReadSingle();
                string extra = pkg.ReadString();
                var target = pkg.ReadZDOID();
                long sender = pkg.ReadLong();
                Play(kind, pos, dir, spell, value, extra, target, sender == ZDOMan.GetSessionID());
            }
            catch (Exception e)
            {
                Plugin.Report("Effect message", e);
            }
        }

        private static void Play(FxKind kind, Vector3 pos, Vector3 dir, string spell, float value, string extra, ZDOID target, bool mine)
        {
            if (Player.m_localPlayer != null && Vector3.Distance(Player.m_localPlayer.transform.position, pos) > 300f &&
                kind != FxKind.DarkMark && kind != FxKind.Weather)
            {
                return;
            }

            var def = SpellDb.Get(spell);
            Color c = def != null ? SpellColor(def) : Color.white;
            switch (kind)
            {
                case FxKind.Cast:
                    CastFlash(pos, c, def);
                    GameFx.Cast(pos, dir, def);
                    if (def != null && (def.IsGrand || def.IsRitual || def.Arch == "aoe" || def.Arch == "summ" || def.Arch == "heal" || def.Arch == "buff"))
                    {
                        MagicCircle(pos, c, def.IsGrand || def.IsRitual ? 3.2f : 2f, def.IsGrand || def.IsRitual ? 2.2f : 1.2f);
                    }

                    break;
                case FxKind.Bolt:
                    if (!mine)
                    {
                        Projectile.Visual(pos, dir, value, c, def, extra);
                    }

                    break;
                case FxKind.Beam: Beam(pos, pos + dir, c, 0.12f, Mathf.Max(0.15f, value)); break;
                case FxKind.Ray: Beam(pos, pos + dir, c, 0.08f, 0.25f); Burst(pos + dir, 0.8f, c, 0.5f); break;
                case FxKind.Hit: Burst(pos, value > 0f ? 1.4f : 0.8f, c, value > 0f ? 1.6f : 1f); GameFx.Hit(pos, def, false); break;
                case FxKind.Burst:
                    Burst(pos, Mathf.Max(1f, value), c, 1.5f);
                    Ring(pos, Mathf.Max(1f, value), c, 0.6f);
                    Shake(pos, value);
                    GameFx.Hit(pos, def, value >= 3f);
                    break;
                case FxKind.Cone: Cone(pos, dir, Mathf.Max(2f, value), c); break;
                case FxKind.Status: Status(target, spell, extra, value); break;
                case FxKind.Heal: Rising(pos, 1f, new Color(0.55f, 1f, 0.7f, 1f), 1.4f, 60); Sound(pos, "chime", 0.5f); GameFx.Heal(pos); break;
                case FxKind.Buff: Rising(pos, 0.8f, c, 1.2f, 40); Sound(pos, "chime", 0.35f); GameFx.Buff(pos, def); break;
                case FxKind.Summon:
                    Burst(pos, 1.5f, c, 1.2f);
                    Ring(pos, 2f, c, 0.8f);
                    Sound(pos, "whoosh", 0.6f);
                    GameFx.Summon(pos);
                    MagicCircle(pos, c, 2.4f, 1.6f);
                    if (!string.IsNullOrEmpty(extra))
                    {
                        Summons.Show(pos, dir, extra, value, target);
                    }

                    break;
                case FxKind.Blink: Smoke(pos, new Color(0.08f, 0.08f, 0.1f, 0.8f)); Sound(pos, "crack", 0.8f); GameFx.Blink(pos); break;
                case FxKind.Text: Text(pos, extra); break;
                case FxKind.Flare: Flare(pos, extra); break;
                case FxKind.DarkMark: DarkMark(pos, value); break;
                case FxKind.Ceremony: Ceremony(pos, extra); GameFx.Ceremony(pos); MagicCircle(pos, Ui.Gold, 3.5f, 2.5f); break;
                case FxKind.Weather: Weather.Apply(extra, value, pos); break;
                case FxKind.Poly: Polymorph.Show(target, extra, value); break;
            }
        }

        // ------------------------------------------------------------------ colours
        public static Color SpellColor(SpellDef s)
        {
            Color c;
            if (s_colors.TryGetValue(s.Id, out c))
            {
                return c;
            }

            string v = ((s.Vfx ?? "") + " ").ToLowerInvariant();
            if (v.Contains("алый") || v.Contains("алая")) c = new Color(1f, 0.12f, 0.15f);
            else if (v.Contains("красн")) c = new Color(1f, 0.2f, 0.2f);
            else if (v.Contains("изумруд") || v.Contains("зелён")) c = new Color(0.25f, 1f, 0.4f);
            else if (v.Contains("бирюз")) c = new Color(0.2f, 0.95f, 0.85f);
            else if (v.Contains("голуб") || v.Contains("ледян")) c = new Color(0.55f, 0.85f, 1f);
            else if (v.Contains("синий") || v.Contains("синее") || v.Contains("синяя") || v.Contains("сине")) c = new Color(0.3f, 0.5f, 1f);
            else if (v.Contains("розов")) c = new Color(1f, 0.45f, 0.8f);
            else if (v.Contains("лилов") || v.Contains("фиолет")) c = new Color(0.7f, 0.35f, 1f);
            else if (v.Contains("оранж") || v.Contains("огн") || v.Contains("плам")) c = new Color(1f, 0.55f, 0.15f);
            else if (v.Contains("золот") || v.Contains("жёлт")) c = new Color(1f, 0.85f, 0.35f);
            else if (v.Contains("серебр")) c = new Color(0.85f, 0.9f, 1f);
            else if (v.Contains("чёрн") || v.Contains("тёмн")) c = new Color(0.25f, 0.2f, 0.3f);
            else if (v.Contains("бел")) c = new Color(1f, 1f, 1f);
            else if (s.Damage.Length > 0)
            {
                switch (s.Damage[0])
                {
                    case "fire": c = new Color(1f, 0.5f, 0.1f); break;
                    case "frost": c = new Color(0.6f, 0.9f, 1f); break;
                    case "lightning": c = new Color(0.75f, 0.7f, 1f); break;
                    case "poison": c = new Color(0.5f, 0.9f, 0.2f); break;
                    case "spirit": c = new Color(0.85f, 0.95f, 1f); break;
                    default: c = TreeDb.SchoolColor(s.School); break;
                }
            }
            else
            {
                c = TreeDb.SchoolColor(s.School);
            }

            var w = Wizard.State;
            if (w != null && !string.IsNullOrEmpty(w.Colour) && Player.m_localPlayer != null)
            {
                Color custom;
                if (ColorUtility.TryParseHtmlString(w.Colour, out custom) && s.Role != "lore")
                {
                    c = Color.Lerp(c, custom, 0.0f); // the bar's own colour only (Colovaria tints the trail, see Casting)
                }
            }

            c.a = 1f;
            s_colors[s.Id] = c;
            return c;
        }

        // ------------------------------------------------------------------ effects
        public static void CastFlash(Vector3 pos, Color c, SpellDef s)
        {
            var root = Root("WZ_Cast", pos, 1.5f);
            Sparks(root.transform, pos, c, Count(18), 1f, 3.5f, 0.2f, 0.5f, 0.02f, 0.07f, 0f);
            Light(root.transform, pos, c, 6f, 2.5f, 0.3f);
            if (s != null)
            {
                switch (s.Damage.Length > 0 ? s.Damage[0] : "")
                {
                    case "fire": Sound(pos, "fire", 0.5f); break;
                    case "frost": Sound(pos, "chime", 0.4f); break;
                    case "lightning": Sound(pos, "zap", 0.6f); break;
                    default: Sound(pos, s.Arch == "heal" || s.Arch == "buff" ? "chime" : "zap", 0.45f); break;
                }
            }
        }

        /// <summary>A glowing rune circle on the ground under the caster that turns and fades.</summary>
        public static void MagicCircle(Vector3 pos, Color c, float size, float life)
        {
            var tex = Meshes.Texture("glyph_ritual.png");
            if (tex == null)
            {
                return;
            }

            var ground = pos;
            RaycastHit hit;
            if (Physics.Raycast(pos + Vector3.up * 0.3f, Vector3.down, out hit, 4f, LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain"),
                    QueryTriggerInteraction.Ignore))
            {
                ground = hit.point;
            }

            ground += Vector3.up * 0.05f;
            var root = Root("WZ_Circle", ground, life + 0.3f);
            foreach (float k in new[] { 1f, 0.62f })
            {
                var ps = NewParticles(root.transform, ground, tex);
                var r = ps.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                r.alignment = ParticleSystemRenderSpace.World;
                var main = ps.main;
                main.loop = false;
                main.duration = life;
                main.startLifetime = life;
                main.startSpeed = 0f;
                main.startSize = size * k;
                main.startRotation = UnityEngine.Random.value * Mathf.PI * 2f;
                main.startColor = c;
                main.maxParticles = 1;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var sh = ps.shape;
                sh.enabled = false;
                var em = ps.emission;
                em.rateOverTime = 0f;
                em.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
                var rot = ps.rotationOverLifetime;
                rot.enabled = true;
                rot.z = (k < 1f ? -1f : 1f) * 50f * Mathf.Deg2Rad;
                var sol = ps.sizeOverLifetime;
                sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.18f, 1f), new Keyframe(1f, 1.06f)));
                FadeOut(ps, c);
                ps.gameObject.SetActive(true);
                ps.Play();
            }

            Light(root.transform, ground + Vector3.up * 0.4f, c, size * 1.5f, 1.4f, life * 0.8f);
        }

        public static void Burst(Vector3 pos, float radius, Color c, float power)
        {
            var root = Root("WZ_Burst", pos, 2.5f);
            Sparks(root.transform, pos, c, Count((int)(30 * power)), 2f * radius, 6f * radius, 0.3f, 0.8f, 0.04f, 0.12f, 0.4f);
            Light(root.transform, pos + Vector3.up * 0.5f, c, 4f + 4f * radius, 2f + power, 0.4f);
            Sound(pos, radius > 2f ? "boom" : "pop", Mathf.Clamp01(0.3f + 0.15f * radius));
        }

        public static void Shake(Vector3 pos, float radius)
        {
            if (!ModConfig.ScreenEffects.Value || radius < 3f || GameCamera.instance == null)
            {
                return;
            }

            GameCamera.instance.AddShake(pos, 25f + radius * 2f, Mathf.Min(2f, 0.3f + radius * 0.08f), false);
        }

        public static void Cone(Vector3 pos, Vector3 dir, float range, Color c)
        {
            var root = Root("WZ_Cone", pos, 2f);
            var ps = NewParticles(root.transform, pos, Dot());
            ps.transform.rotation = Quaternion.LookRotation(dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward);
            var main = ps.main;
            main.duration = 0.25f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(range * 1.8f, range * 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.3f);
            main.startColor = c;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Count(70)) });
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 25f;
            sh.radius = 0.15f;
            FadeOut(ps, c);
            ps.gameObject.SetActive(true);
            ps.Play();
            Sound(pos, "whoosh", 0.5f);
        }

        public static void Beam(Vector3 from, Vector3 to, Color c, float width, float life)
        {
            var go = new GameObject("WZ_Beam");
            var b = go.AddComponent<FxBeam>();
            b.From = from;
            b.To = to;
            b.Life = life;
            b.Core = Line(go.transform, "Core", c, width);
            b.Glow = Line(go.transform, "Glow", new Color(c.r, c.g, c.b, 0.35f), width * 4f);
            Object.Destroy(go, life + 0.1f);
        }

        public static void Ring(Vector3 center, float radius, Color c, float life)
        {
            var go = new GameObject("WZ_Ring");
            go.transform.position = center;
            var r = go.AddComponent<FxRing>();
            r.Radius = radius;
            r.Life = life;
            r.Color = c;
            r.Line = Line(go.transform, "Line", c, 0.25f);
            r.Line.loop = true;
            Object.Destroy(go, life + 0.1f);
        }

        public static void Rising(Vector3 pos, float radius, Color c, float life, int count)
        {
            var root = Root("WZ_Rise", pos, life + 1.5f);
            var ps = NewParticles(root.transform, pos, Dot());
            var main = ps.main;
            main.duration = life;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
            main.startColor = c;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = Count(count) / Mathf.Max(0.3f, life);
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Circle;
            sh.radius = radius;
            sh.rotation = new Vector3(-90f, 0f, 0f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
            vel.y = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
            FadeOut(ps, c);
            ps.gameObject.SetActive(true);
            ps.Play();
        }

        public static void Smoke(Vector3 pos, Color c)
        {
            var root = Root("WZ_Smoke", pos, 3f);
            var ps = NewParticles(root.transform, pos + Vector3.up, Dot(), false);
            var main = ps.main;
            main.duration = 0.2f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            main.startColor = c;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Count(40)) });
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.6f;
            FadeOut(ps, c);
            ps.gameObject.SetActive(true);
            ps.Play();
        }

        public static void Text(Vector3 pos, string text)
        {
            if (string.IsNullOrEmpty(text) || !ModConfig.ShowIncantation.Value || DamageText.instance == null)
            {
                return;
            }

            DamageText.instance.ShowText(DamageText.TextType.Bonus, pos + Vector3.up * 2.1f, text + "!", false);
        }

        private static void Flare(Vector3 pos, string colour)
        {
            Color c;
            switch (colour)
            {
                case "blue": c = new Color(0.3f, 0.5f, 1f); break;
                case "green": c = new Color(0.3f, 1f, 0.4f); break;
                case "white": c = Color.white; break;
                default: c = new Color(1f, 0.25f, 0.2f); break;
            }

            var top = pos + Vector3.up * 40f;
            Beam(pos, top, c, 0.2f, 1.2f);
            var root = Root("WZ_Flare", top, 6f);
            Sparks(root.transform, top, c, Count(160), 4f, 14f, 1.2f, 2.8f, 0.15f, 0.4f, 0.3f);
            Light(root.transform, top, c, 80f, 6f, 3f);
            Sound(top, "boom", 1f, 300f);
        }

        /// <summary>The Dark Mark: a skull of green light with a snake from its jaw, high in the sky.</summary>
        private static void DarkMark(Vector3 ground, float duration)
        {
            var root = new GameObject("WZ_DarkMark");
            root.transform.position = ground + Vector3.up * 60f;
            var mark = root.AddComponent<FxDarkMark>();
            mark.Life = Mathf.Max(10f, duration);
            var green = new Color(0.25f, 1f, 0.45f, 0.9f);
            var lines = new List<LineRenderer>();
            for (int i = 0; i < 6; i++)
            {
                lines.Add(Line(root.transform, "Mark" + i, green, 0.9f));
            }

            mark.Lines = lines.ToArray();
            Light(root.transform, root.transform.position, green, 120f, 4f, mark.Life);
            Sound(ground, "whisper", 1f, 400f);
        }

        private static void Ceremony(Vector3 pos, string house)
        {
            Color hc;
            switch (house)
            {
                case "gryffindor": hc = new Color(0.9f, 0.15f, 0.1f); break;
                case "ravenclaw": hc = new Color(0.2f, 0.4f, 1f); break;
                case "hufflepuff": hc = new Color(1f, 0.8f, 0.2f); break;
                case "slytherin": hc = new Color(0.15f, 0.8f, 0.3f); break;
                default: hc = new Color(0.4f, 1f, 0.55f); break;
            }

            var green = new Color(0.4f, 1f, 0.55f);
            Beam(pos, pos + Vector3.up * 30f, green, 0.6f, 5f);
            var root = Root("WZ_Ceremony", pos, 8f);
            Sparks(root.transform, pos + Vector3.up * 3f, hc, Count(220), 2f, 7f, 1.5f, 3.5f, 0.06f, 0.18f, 0.2f);
            Light(root.transform, pos + Vector3.up * 2f, green, 25f, 5f, 6f);
            Ring(pos, 4f, hc, 2.5f);
            Sound(pos, "choir", 1f, 120f);
        }

        private static void Status(ZDOID target, string key, string variant, float duration)
        {
            if (ZNetScene.instance == null || target == ZDOID.None)
            {
                return;
            }

            Effects.Seen(target, key, duration);
            var go = ZNetScene.instance.FindInstance(target);
            var c = go != null ? go.GetComponent<Character>() : null;
            if (c == null)
            {
                return;
            }

            var def = StatusDefs.Get(key);
            Color col = def != null ? def.Tint : Color.white;
            switch (variant)
            {
                case "petrify": col = new Color(0.6f, 0.6f, 0.6f); break;
                case "freeze": col = new Color(0.6f, 0.9f, 1f); break;
                case "laugh": case "dance": col = new Color(1f, 0.7f, 0.2f); break;
                case "bubble": col = new Color(0.6f, 0.9f, 1f); break;
            }

            var aura = new GameObject("WZ_Aura_" + key);
            aura.transform.SetParent(c.transform, false);
            aura.transform.localPosition = Vector3.up * Mathf.Max(0.5f, c.GetHeight() * 0.5f);
            var a = aura.AddComponent<FxAura>();
            a.Life = duration;
            a.Ps = NewParticles(aura.transform, aura.transform.position, Dot());
            a.Ps.transform.localPosition = Vector3.zero;
            var main = a.Ps.main;
            main.duration = 1f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.16f);
            main.startColor = col;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = a.Ps.emission;
            em.rateOverTime = Count(variant == "bubble" || variant == "petrify" ? 60 : 25);
            var sh = a.Ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = Mathf.Max(0.4f, c.GetRadius());
            FadeOut(a.Ps, col);
            a.Ps.gameObject.SetActive(true);
            a.Ps.Play();
            if (key == "lift" || key == "stun" || key == "poly")
            {
                Sound(c.transform.position, "pop", 0.4f);
            }
        }

        // ------------------------------------------------------------------ building blocks
        private static int Count(int n)
        {
            return Mathf.Max(1, Mathf.RoundToInt(n * Quality));
        }

        public static GameObject Root(string name, Vector3 pos, float life)
        {
            var root = new GameObject(name);
            root.transform.position = pos;
            Object.Destroy(root, life);
            return root;
        }

        public static void Light(Transform parent, Vector3 pos, Color c, float range, float intensity, float life)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            var f = go.AddComponent<FxFade>();
            f.Light = l;
            f.Life = life;
            f.Peak = intensity;
        }

        public static void Sparks(Transform parent, Vector3 pos, Color c, int count, float speedMin, float speedMax, float lifeMin,
            float lifeMax, float sizeMin, float sizeMax, float gravity)
        {
            var ps = NewParticles(parent, pos, Dot());
            var main = ps.main;
            main.duration = 0.1f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor = c;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Clamp(count, 1, 600)) });
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.2f;
            FadeOut(ps, c);
            ps.gameObject.SetActive(true);
            ps.Play();
        }

        public static ParticleSystem NewParticles(Transform parent, Vector3 pos, Texture2D tex, bool additive = true)
        {
            var go = new GameObject("Particles");
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Mat(tex, additive);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        public static void FadeOut(ParticleSystem ps, Color c)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(c.a, 0f), new GradientAlphaKey(c.a * 0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
        }

        public static LineRenderer Line(Transform parent, string name, Color c, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = Mat(Streak());
            lr.useWorldSpace = true;
            lr.startColor = c;
            lr.endColor = c;
            lr.widthMultiplier = width;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        private static Material s_vanillaAdd, s_vanillaBlend;
        private static bool s_searched;

        private static Shader FindShader(bool additive)
        {
            var names = additive
                ? new[] { "Legacy Shaders/Particles/Additive", "Particles/Additive", "Mobile/Particles/Additive" }
                : new[] { "Legacy Shaders/Particles/Alpha Blended", "Particles/Alpha Blended", "Mobile/Particles/Alpha Blended" };
            foreach (string n in names)
            {
                var s = Shader.Find(n);
                if (s != null)
                {
                    return s;
                }
            }

            return null;
        }

        /// <summary>The particle materials of the game's own magic effects: their shaders surely exist in the build.</summary>
        private static void SearchVanilla()
        {
            if (s_searched)
            {
                return;
            }

            s_searched = true;
            foreach (string prefab in new[] { "fx_DvergerMage_Support_start", "vfx_HealthUpgrade", "fx_guardstone_activate", "vfx_FireAddFuel",
                         "vfx_Potion_eitr_minor", "fx_DvergerMage_Ice_hit" })
            {
                var go = Jotunn.Managers.PrefabManager.Instance.GetPrefab(prefab);
                if (go == null)
                {
                    continue;
                }

                foreach (var r in go.GetComponentsInChildren<ParticleSystemRenderer>(true))
                {
                    var m = r.sharedMaterial;
                    if (m == null || m.shader == null)
                    {
                        continue;
                    }

                    string n = m.shader.name.ToLowerInvariant();
                    bool add = n.Contains("add") || m.GetTag("RenderType", false) == "Transparent" && n.Contains("particle") && m.HasProperty("_TintColor");
                    if (add && s_vanillaAdd == null) s_vanillaAdd = m;
                    else if (!add && s_vanillaBlend == null && n.Contains("particle")) s_vanillaBlend = m;
                }

                if (s_vanillaAdd != null && s_vanillaBlend != null)
                {
                    break;
                }
            }

            Plugin.Log.LogInfo("Particle materials: " + (s_vanillaAdd != null ? s_vanillaAdd.shader.name : "-") + " / " +
                               (s_vanillaBlend != null ? s_vanillaBlend.shader.name : "-"));
        }

        public static Material Mat(Texture2D tex, bool additive = true)
        {
            Material m;
            string key = tex.name + (additive ? "+" : "~");
            if (s_mats.TryGetValue(key, out m) && m != null)
            {
                return m;
            }

            var shader = FindShader(additive);
            if (shader != null)
            {
                m = new Material(shader);
            }
            else
            {
                SearchVanilla();
                var src = additive ? s_vanillaAdd ?? s_vanillaBlend : s_vanillaBlend ?? s_vanillaAdd;
                m = src != null ? new Material(src) : new Material(Shader.Find("Sprites/Default"));
                foreach (string prop in m.GetTexturePropertyNames())
                {
                    if (prop != "_MainTex") m.SetTexture(prop, null);
                }
            }

            m.name = "WZ_" + key;
            m.mainTexture = tex;
            if (m.HasProperty("_TintColor"))
            {
                m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            }

            if (m.HasProperty("_Color"))
            {
                m.SetColor("_Color", Color.white);
            }

            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            s_mats[key] = m;
            return m;
        }

        /// <summary>A see-through material for domes and walls.</summary>
        public static Material Glass(Color c)
        {
            var m = new Material(Mat(Dot(), true));
            m.mainTexture = Ring();
            m.color = c;
            return m;
        }

        public static Texture2D Dot()
        {
            return s_dot ?? (s_dot = Procedural("dot", 32, 32, (u, v) =>
            {
                float r = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f;
                float a = Mathf.Clamp01(1f - r);
                return a * a;
            }));
        }

        public static Texture2D Streak()
        {
            return s_streak ?? (s_streak = Procedural("streak", 4, 32, (u, v) => Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(v - 0.5f) * 2f), 1.5f)));
        }

        public static Texture2D Ring()
        {
            return s_ring ?? (s_ring = Procedural("ring", 64, 64, (u, v) =>
            {
                float r = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f;
                return Mathf.Clamp01(0.25f + 0.75f * Mathf.Pow(Mathf.Clamp01(r), 4f)) * (r > 1f ? 0f : 1f);
            }));
        }

        private static Texture2D Procedural(string name, int w, int h, Func<float, float, float> alpha)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "WZ_" + name };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float a = Mathf.Clamp01(alpha((x + 0.5f) / w, (y + 0.5f) / h));
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            tex.SetPixels32(px);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply(false, true);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return tex;
        }

        // ------------------------------------------------------------------ sound
        public static void Sound(Vector3 pos, string clip, float volume, float maxDistance = 60f)
        {
            var c = Clip(clip);
            if (c == null)
            {
                return;
            }

            try
            {
                var go = new GameObject("WZ_Sfx");
                go.transform.position = pos;
                var src = go.AddComponent<AudioSource>();
                src.clip = c;
                src.volume = volume;
                src.spatialBlend = 1f;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 3f;
                src.maxDistance = maxDistance;
                src.dopplerLevel = 0f;
                src.outputAudioMixerGroup = SfxGroup();
                src.Play();
                Object.Destroy(go, c.length + 0.2f);
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug("Sound failed: " + e.Message);
            }
        }

        private static AudioMixerGroup SfxGroup()
        {
            if (s_sfxSearched)
            {
                return s_sfx;
            }

            s_sfxSearched = true;
            try
            {
                if (ZNetScene.instance != null)
                {
                    foreach (var prefab in ZNetScene.instance.m_prefabs)
                    {
                        if (prefab == null || !prefab.name.StartsWith("sfx_"))
                        {
                            continue;
                        }

                        var src = prefab.GetComponentInChildren<AudioSource>(true);
                        if (src != null && src.outputAudioMixerGroup != null)
                        {
                            s_sfx = src.outputAudioMixerGroup;
                            break;
                        }
                    }
                }
            }
            catch (Exception)
            {
                s_sfx = null;
            }

            return s_sfx;
        }

        private static AudioClip Clip(string name)
        {
            AudioClip c;
            if (s_clips.TryGetValue(name, out c) && c != null)
            {
                return c;
            }

            float[] data;
            switch (name)
            {
                case "zap": data = Synth(0.35f, (t, r) => Noise(r) * Mathf.Exp(-t * 14f) * 0.7f + Mathf.Sin(t * 2400f * (1f - t)) * Mathf.Exp(-t * 9f) * 0.3f); break;
                case "pop": data = Synth(0.3f, (t, r) => Mathf.Sin(t * 900f * (1f - t * 2f)) * Mathf.Exp(-t * 18f) + Noise(r) * Mathf.Exp(-t * 40f) * 0.4f); break;
                case "boom": data = Synth(1.6f, (t, r) => Lowpass(r, 0.03f) * Mathf.Exp(-t * 2.5f) * 1.6f + Mathf.Sin(t * 260f * (1f - t * 0.3f)) * Mathf.Exp(-t * 5f) * 0.6f); break;
                case "click": data = Synth(0.07f, (t, r) => Mathf.Sin(t * 2f * Mathf.PI * 1700f) * Mathf.Exp(-t * 70f) * 0.5f); break;
                case "page": data = Synth(0.32f, (t, r) => Lowpass(r, 0.25f) * Mathf.Sin(Mathf.Clamp01(t / 0.32f) * Mathf.PI) * 0.7f); break;
                case "whoosh": data = Synth(0.8f, (t, r) => Lowpass(r, 0.08f) * Mathf.Sin(t / 0.8f * Mathf.PI) * 1.2f); break;
                case "crack": data = Synth(0.5f, (t, r) => (t < 0.03f ? Noise(r) : Lowpass(r, 0.05f) * Mathf.Exp(-t * 8f)) * 1.2f); break;
                case "chime": data = Synth(1.4f, (t, r) => (Mathf.Sin(t * 2f * Mathf.PI * 880f) + 0.6f * Mathf.Sin(t * 2f * Mathf.PI * 1320f) + 0.3f * Mathf.Sin(t * 2f * Mathf.PI * 1760f)) * Mathf.Exp(-t * 3.5f) * 0.35f); break;
                case "fire": data = Synth(0.9f, (t, r) => Lowpass(r, 0.12f) * Mathf.Exp(-t * 3f) + (r.NextDouble() < 0.002 ? 0.8f : 0f)); break;
                case "hum": data = Synth(2f, (t, r) => (Mathf.Sin(t * 2f * Mathf.PI * 110f) + 0.5f * Mathf.Sin(t * 2f * Mathf.PI * 112.5f)) * Mathf.Min(1f, t * 3f) * Mathf.Min(1f, (2f - t) * 3f) * 0.4f); break;
                case "whisper": data = Synth(3f, (t, r) => Bandpass(r) * (0.5f + 0.5f * Mathf.Sin(t * 9f)) * Mathf.Min(1f, t) * Mathf.Min(1f, 3f - t) * 0.8f); break;
                case "choir": data = Synth(4f, (t, r) =>
                {
                    float s = 0f;
                    foreach (float f in new[] { 220f, 277.2f, 329.6f, 440f })
                    {
                        s += Mathf.Sin(t * 2f * Mathf.PI * f * (1f + 0.003f * Mathf.Sin(t * 5f + f)));
                    }

                    return s * 0.15f * Mathf.Min(1f, t / 1.2f) * Mathf.Min(1f, (4f - t) / 1.5f);
                }); break;
                case "thunder": data = Synth(2.5f, (t, r) => Lowpass(r, 0.02f) * Mathf.Exp(-t * 1.2f) * (1f + 0.5f * Mathf.Sin(t * 23f)) * 1.8f); break;
                default: return null;
            }

            float peak = 1e-5f;
            foreach (float x in data)
            {
                peak = Mathf.Max(peak, Mathf.Abs(x));
            }

            for (int i = 0; i < data.Length; i++)
            {
                data[i] = data[i] / peak * 0.85f;
            }

            c = AudioClip.Create("WZ_" + name, data.Length, 1, Rate, false);
            c.SetData(data, 0);
            s_clips[name] = c;
            return c;
        }

        private static float[] Synth(float seconds, Func<float, System.Random, float> f)
        {
            int n = (int)(Rate * seconds);
            var d = new float[n];
            var r = new System.Random(name_seed++);
            s_lp = 0f;
            s_lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                d[i] = f((float)i / Rate, r);
            }

            return d;
        }

        private static int name_seed = 7;
        private static float s_lp, s_lp2;

        private static float Noise(System.Random r)
        {
            return (float)(r.NextDouble() * 2.0 - 1.0);
        }

        private static float Lowpass(System.Random r, float k)
        {
            s_lp += (Noise(r) - s_lp) * k;
            return s_lp * 4f;
        }

        private static float Bandpass(System.Random r)
        {
            s_lp += (Noise(r) - s_lp) * 0.3f;
            s_lp2 += (s_lp - s_lp2) * 0.05f;
            return (s_lp - s_lp2) * 2f;
        }
    }

    // ---------------------------------------------------------------------- tiny animators
    internal sealed class FxFade : MonoBehaviour
    {
        public Light Light;
        public float Life = 0.3f, Peak = 2f;
        private float m_t;

        private void Update()
        {
            m_t += Time.deltaTime;
            if (Light != null)
            {
                Light.intensity = Peak * Mathf.Clamp01(1f - m_t / Mathf.Max(0.05f, Life));
            }
        }
    }

    internal sealed class FxBeam : MonoBehaviour
    {
        public Vector3 From, To;
        public float Life = 0.3f;
        public LineRenderer Core, Glow;
        private float m_t;

        private void Update()
        {
            m_t += Time.deltaTime;
            float k = Mathf.Clamp01(1f - m_t / Mathf.Max(0.05f, Life));
            const int n = 10;
            foreach (var lr in new[] { Core, Glow })
            {
                if (lr == null)
                {
                    continue;
                }

                lr.positionCount = n;
                for (int i = 0; i < n; i++)
                {
                    float f = i / (n - 1f);
                    Vector3 p = Vector3.Lerp(From, To, f);
                    if (i > 0 && i < n - 1)
                    {
                        p += UnityEngine.Random.insideUnitSphere * 0.06f;
                    }

                    lr.SetPosition(i, p);
                }

                var c = lr.startColor;
                c.a = (lr == Glow ? 0.35f : 1f) * k;
                lr.startColor = c;
                lr.endColor = c;
            }
        }
    }

    internal sealed class FxRing : MonoBehaviour
    {
        public float Radius = 2f, Life = 0.6f;
        public Color Color;
        public LineRenderer Line;
        private float m_t;

        private void Update()
        {
            m_t += Time.deltaTime;
            float f = Mathf.Clamp01(m_t / Life);
            const int n = 40;
            Line.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                Line.SetPosition(i, transform.position + new Vector3(Mathf.Cos(a), 0.15f, Mathf.Sin(a)) * Radius * (0.3f + 0.7f * f));
            }

            var c = Color;
            c.a = 1f - f;
            Line.startColor = c;
            Line.endColor = c;
        }
    }

    internal sealed class FxAura : MonoBehaviour
    {
        public ParticleSystem Ps;
        public float Life = 3f;
        private float m_t;

        private void Update()
        {
            m_t += Time.deltaTime;
            if (m_t > Life)
            {
                if (Ps != null)
                {
                    var em = Ps.emission;
                    em.enabled = false;
                }

                Destroy(gameObject, 1.2f);
                enabled = false;
            }
        }
    }

    /// <summary>The Dark Mark's lines: a skull outline and a snake winding out of its jaw, facing the camera.</summary>
    internal sealed class FxDarkMark : MonoBehaviour
    {
        public LineRenderer[] Lines;
        public float Life = 60f;
        private float m_t;

        private void Update()
        {
            m_t += Time.deltaTime;
            if (m_t > Life)
            {
                Destroy(gameObject);
                return;
            }

            var cam = Camera.main;
            if (cam != null)
            {
                transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
            }

            float s = 12f * Mathf.Min(1f, m_t / 2f);
            float fade = Mathf.Clamp01((Life - m_t) / 5f);
            var c = new Color(0.25f, 1f, 0.45f, 0.9f * fade);
            Shape(0, 24, i => Circle(i, 24, 0f, 0.2f, 1f, 1.1f, 0f, Mathf.PI * 2f), s, c);      // cranium
            Shape(1, 12, i => Circle(i, 12, -0.38f, 0.25f, 0.22f, 0.25f, 0f, Mathf.PI * 2f), s, c); // left eye
            Shape(2, 12, i => Circle(i, 12, 0.38f, 0.25f, 0.22f, 0.25f, 0f, Mathf.PI * 2f), s, c);  // right eye
            Shape(3, 10, i => Circle(i, 10, 0f, -0.7f, 0.55f, 0.35f, Mathf.PI, Mathf.PI * 2f), s, c); // jaw
            Shape(4, 6, i => new Vector3(-0.08f + 0.16f * (i % 2), -0.1f - 0.06f * i, 0f), s, c);      // nose
            Shape(5, 40, i =>
            {
                float f = i / 39f;
                return new Vector3(0.5f * Mathf.Sin(f * 12f + m_t * 2f) * (1f - f * 0.5f), -0.8f - f * 2.2f, 0f);
            }, s, c); // the snake
        }

        private static Vector3 Circle(int i, int n, float cx, float cy, float rx, float ry, float a0, float a1)
        {
            float a = Mathf.Lerp(a0, a1, i / (n - 1f));
            return new Vector3(cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry, 0f);
        }

        private void Shape(int idx, int n, Func<int, Vector3> f, float s, Color c)
        {
            var lr = Lines[idx];
            lr.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                lr.SetPosition(i, transform.TransformPoint(f(i) * s));
            }

            lr.startColor = c;
            lr.endColor = c;
        }
    }
}
