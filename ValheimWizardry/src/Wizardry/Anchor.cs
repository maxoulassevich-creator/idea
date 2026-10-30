using System.Collections.Generic;
using UnityEngine;

namespace Wizardry
{
    /// <summary>
    ///     A spell that stays in the world: a zone, a dome, a wall, a bridge, a tent, a light, a trap. It is a network
    ///     object (so everybody sees it and it survives a restart); every game draws it, the caster's game does what it
    ///     does to creatures, and whoever owns the object removes it when its time is up.
    /// </summary>
    internal sealed class Anchor : MonoBehaviour
    {
        public static readonly List<Anchor> All = new List<Anchor>();
        private static global::Projectile[] s_projectiles = new global::Projectile[0];
        private static float s_projectilesAt = -1f;

        public string Type = "";
        public float Radius, Duration;
        public long Owner;
        public string SpellId;
        public Vector3 Dir = Vector3.forward;

        private ZNetView m_nview;
        private long m_t0;
        private bool m_built;
        private float m_tick, m_localTick, m_blockTick, m_slowTick, m_soundTick;
        private readonly Dictionary<Character, float> m_cool = new Dictionary<Character, float>();
        private Color m_color = Color.white;

        public static void Spawn(Player caster, WizardState w, SpellDef spell, string type, Vector3 pos, float r, float dur, Vector3? dir = null)
        {
            var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(Pieces.AnchorName) : null;
            if (prefab == null || caster == null || spell == null)
            {
                return;
            }

            bool ward = type.StartsWith("dome_") || type == "ward" || type == "safe" || type == "muggle" || type == "fidelius";
            dur *= 1f + w.Stats.Get(ward ? Stat.ward_dur : Stat.dur) / 100f;
            if (type == "wall" || type == "bridge" || type == "tent")
            {
                dur *= 1f + w.Stats.Get(Stat.construct_dur) / 100f;
            }

            dur *= Patterns.Duration;
            Limit(caster.GetPlayerID(), type);
            Vector3 d = dir ?? caster.transform.forward;
            d.y = 0f;
            if (d.sqrMagnitude < 0.01f)
            {
                d = Vector3.forward;
            }

            d.Normalize();
            var go = Instantiate(prefab, pos, Quaternion.LookRotation(d));
            var nv = go.GetComponent<ZNetView>();
            var zdo = nv != null ? nv.GetZDO() : null;
            if (zdo == null)
            {
                Destroy(go);
                return;
            }

            zdo.Set("wz_type", type);
            zdo.Set("wz_r", r);
            zdo.Set("wz_dur", dur);
            zdo.Set("wz_t0", ZNet.instance.GetTime().Ticks);
            zdo.Set("wz_owner", caster.GetPlayerID());
            zdo.Set("wz_spell", spell.Id);
            zdo.Set("wz_dir", d);
            Fx.Send(FxKind.Summon, pos, Vector3.up, spell.Id, r);
            if (dur > 120f && type != "spelltrap")
            {
                Casting.Message(spell.Name + ": " + Texts.L("действует ", "lasts ") + Mathf.RoundToInt(dur / 60f) + Texts.L(" мин", " min"));
            }
        }

        /// <summary>At most six anchors of one kind per wizard: the oldest goes.</summary>
        private static void Limit(long owner, string type)
        {
            var mine = new List<Anchor>();
            foreach (var a in All)
            {
                if (a != null && a.m_built && a.Owner == owner && a.Type == type)
                {
                    mine.Add(a);
                }
            }

            if (mine.Count < 6)
            {
                return;
            }

            mine.Sort((x, y) => x.m_t0.CompareTo(y.m_t0));
            mine[0].Remove();
        }

        public void Remove()
        {
            if (m_nview == null || !m_nview.IsValid())
            {
                return;
            }

            if (!m_nview.IsOwner())
            {
                m_nview.ClaimOwnership();
            }

            m_nview.Destroy();
        }

        public static bool Covers(Vector3 p, string type)
        {
            foreach (var a in All)
            {
                if (a != null && a.m_built && a.Type == type && Flat(a.transform.position, p) <= a.Radius && Mathf.Abs(a.transform.position.y - p.y) < a.Radius + 5f)
                {
                    return true;
                }
            }

            return false;
        }

        private static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        public float Age { get { return ZNet.instance != null ? (float)((ZNet.instance.GetTime().Ticks - m_t0) / 1e7) : 0f; } }

        private void Awake()
        {
            m_nview = GetComponent<ZNetView>();
            All.Add(this);
        }

        private void OnDestroy()
        {
            All.Remove(this);
        }

        private void Build()
        {
            var zdo = m_nview.GetZDO();
            Type = zdo.GetString("wz_type");
            if (string.IsNullOrEmpty(Type))
            {
                return;
            }

            Radius = Mathf.Max(0.5f, zdo.GetFloat("wz_r", 4f));
            Duration = zdo.GetFloat("wz_dur", 10f);
            m_t0 = zdo.GetLong("wz_t0", ZNet.instance.GetTime().Ticks);
            Owner = zdo.GetLong("wz_owner");
            SpellId = zdo.GetString("wz_spell");
            Dir = zdo.GetVec3("wz_dir", transform.forward);
            var def = SpellDb.Get(SpellId);
            m_color = def != null ? Fx.SpellColor(def) : Color.white;
            m_built = true;
            try
            {
                AnchorLook.Build(this, m_color);
            }
            catch (System.Exception e)
            {
                Plugin.Report("Anchor " + Type, e);
            }
        }

        private void Update()
        {
            if (m_nview == null || !m_nview.IsValid() || ZNet.instance == null)
            {
                return;
            }

            if (!m_built)
            {
                Build();
                if (!m_built)
                {
                    return;
                }
            }

            if (Age > Duration)
            {
                if (m_nview.IsOwner())
                {
                    m_nview.Destroy();
                }

                return;
            }

            float dt = Time.deltaTime;
            m_localTick -= dt;
            if (m_localTick <= 0f)
            {
                m_localTick = 0.25f;
                Local(Player.m_localPlayer);
            }

            if (Type.StartsWith("dome_") || Type == "ward")
            {
                m_blockTick -= dt;
                if (m_blockTick <= 0f)
                {
                    m_blockTick = 0.1f;
                    BlockProjectiles();
                }
            }

            if (Type == "greenhouse" && m_nview.IsOwner())
            {
                m_slowTick -= dt;
                if (m_slowTick <= 0f)
                {
                    m_slowTick = 60f;
                    Utility.Grow(transform.position, Radius, Wizard.State != null && Player.m_localPlayer != null &&
                                                             Player.m_localPlayer.GetPlayerID() == Owner ? Wizard.State : null);
                }
            }

            if (Type == "fiendfyre" || Type == "firering" || Type == "fire" || Type == "diabolica")
            {
                m_soundTick -= dt;
                if (m_soundTick <= 0f)
                {
                    m_soundTick = 2.5f;
                    Fx.Sound(transform.position, "fire", Type == "fiendfyre" ? 0.8f : 0.4f, 40f);
                }
            }

            var me = Player.m_localPlayer;
            if (me == null || me.GetPlayerID() != Owner || Wizard.State == null)
            {
                return;
            }

            m_tick -= dt;
            if (m_tick <= 0f)
            {
                m_tick = Type == "willow" ? 1.5f : Type == "storm" ? 0.8f : 1f;
                try
                {
                    CasterTick(me, Wizard.State);
                }
                catch (System.Exception e)
                {
                    Plugin.Report("Zone " + Type, e);
                }
            }
        }

        // ------------------------------------------------------------------ what it does to the player of this game
        private void Local(Player me)
        {
            if (me == null || me.IsDead())
            {
                return;
            }

            float d = Flat(me.transform.position, transform.position);
            bool inside = d <= Radius && Mathf.Abs(me.transform.position.y - transform.position.y) < Radius + 3f;
            switch (Type)
            {
                case "bounce":
                    if (inside && me.IsOnGround() && me.Body() != null)
                    {
                        Effects.ApplyLocal(me, new StatusOrder { Key = "buff", SeName = "WZ_buff", Duration = 5f, Buffs = "falldmg:-100" });
                        var v = me.Body().velocity;
                        v.y = 14f;
                        me.Body().velocity = v;
                        Fx.Sound(me.transform.position, "pop", 0.5f);
                    }

                    break;
                case "ageline":
                    var w = Wizard.State;
                    if (inside && me.GetPlayerID() != Owner && (w == null || w.Year < 3) && me.Body() != null)
                    {
                        Vector3 out1 = me.transform.position - transform.position;
                        out1.y = 0f;
                        me.Body().velocity = out1.normalized * 9f + Vector3.up * 4f;
                        Casting.Message(Texts.L("Возрастная черта не пускает вас.", "The Age Line throws you back."));
                    }

                    break;
                case "flame":
                case "tent":
                    if (inside)
                    {
                        Effects.CleanseLocal(me, "cold");
                    }

                    break;
            }
        }

        // ------------------------------------------------------------------ what it does, in the caster's game
        private void CasterTick(Player p, WizardState w)
        {
            var spell = SpellDb.Get(SpellId);
            if (spell == null)
            {
                return;
            }

            Vector3 c = transform.position;
            var enemies = new List<Character>();
            var allies = new List<Character>();
            foreach (var ch in Character.GetAllCharacters())
            {
                if (ch == null || ch.IsDead() || Flat(ch.transform.position, c) > Radius || Mathf.Abs(ch.transform.position.y - c.y) > Radius + 4f)
                {
                    continue;
                }

                if (Combat.IsEnemy(p, ch))
                {
                    enemies.Add(ch);
                }
                else if (Combat.IsAlly(p, ch))
                {
                    allies.Add(ch);
                }
            }

            switch (Type)
            {
                case "sticky":
                    foreach (var e in enemies) Effects.Apply(p, w, e, "root", 1.2f, 0f, "sticky");
                    break;
                case "slip":
                    foreach (var e in enemies)
                    {
                        Effects.Apply(p, w, e, "slow", 1.2f, 50f);
                        if (Random.value < 0.25f) Effects.Apply(p, w, e, "stun", 0.8f, 0f, "slip");
                    }

                    break;
                case "fire":
                case "flagrante":
                case "trail":
                    foreach (var e in enemies) Hit(p, w, spell, e, Type == "flagrante" ? 0.25f : 0.3f, 0f);
                    break;
                case "firering":
                    foreach (var e in enemies) Hit(p, w, spell, e, 0.35f, 0f);
                    break;
                case "fiendfyre":
                    foreach (var e in enemies) Hit(p, w, spell, e, 0.7f, 2f);
                    if (Random.value < 0.35f) Combat.StrikeObjects(p, w, spell, c, Radius * 0.5f, 0.5f);
                    break;
                case "diabolica":
                    foreach (var e in enemies) Hit(p, w, spell, e, 0.9f, 0f);
                    foreach (var a in allies) Effects.Apply(p, w, a, "buff", 1.5f, 0f, null, null, "dmg:15");
                    break;
                case "firetrap":
                    foreach (var e in enemies)
                    {
                        if (Ready(e, 5f))
                        {
                            Hit(p, w, spell, e, 1.5f, 6f);
                            Fx.Send(FxKind.Burst, e.transform.position + Vector3.up * 0.3f, Vector3.up, SpellId, 2f);
                        }
                    }

                    break;
                case "gemino":
                    foreach (var e in enemies)
                    {
                        Effects.Apply(p, w, e, "slow", 1.2f, 40f);
                        Hit(p, w, spell, e, 0.15f, 0f);
                    }

                    break;
                case "storm":
                    if (enemies.Count > 0)
                    {
                        var e = enemies[Random.Range(0, enemies.Count)];
                        Vector3 sky = e.transform.position + Vector3.up * 25f + Random.insideUnitSphere * 4f;
                        Fx.Send(FxKind.Beam, sky, e.GetCenterPoint() - sky, SpellId, 0.25f);
                        Fx.Send(FxKind.Hit, e.GetCenterPoint(), Vector3.up, SpellId, 1f);
                        Hit(p, w, spell, e, 1f, 4f);
                        Effects.Apply(p, w, e, "stun", 0.5f, 0f);
                    }

                    break;
                case "ward":
                    foreach (var a in allies) Effects.Apply(p, w, a, "buff", 1.5f, 0f, null, null, "armor:" + (20f * (1f + w.Stats.Get(Stat.ward_power) / 100f)).ToString("0", System.Globalization.CultureInfo.InvariantCulture));
                    foreach (var e in enemies) Push(e, 10f);
                    break;
                case "safe":
                    foreach (var e in enemies)
                    {
                        if (e.IsBoss()) continue;
                        Effects.Apply(p, w, e, "forget", 2f, 0f);
                        Push(e, 8f);
                    }

                    foreach (var a in allies) Effects.Apply(p, w, a, "buff", 1.5f, 0f, null, null, "stealth:50");
                    break;
                case "muggle":
                    foreach (var e in enemies) if (!e.IsBoss()) Effects.Apply(p, w, e, "fear", 2f, 0f);
                    break;
                case "fidelius":
                    foreach (var e in enemies) if (!e.IsBoss()) Effects.Apply(p, w, e, "forget", 2f, 0f);
                    foreach (var a in allies) Effects.Apply(p, w, a, "buff", 1.5f, 0f, null, null, "stealth:90");
                    break;
                case "fog":
                    foreach (var e in enemies) Effects.Apply(p, w, e, "blind", 1.5f, 0f);
                    foreach (var a in allies) Effects.Apply(p, w, a, "buff", 1.5f, 0f, null, null, "stealth:40");
                    break;
                case "smoke":
                    foreach (var e in enemies) Effects.Apply(p, w, e, "blind", 1.5f, 0f);
                    break;
                case "alarm":
                    foreach (var e in enemies)
                    {
                        if (Ready(null, 15f))
                        {
                            Casting.Message(Texts.L("Чары-сторож: чужак у ", "Intruder Charm: a stranger near ") + spell.Name + "!");
                            Fx.Send(FxKind.Flare, c + Vector3.up * 2f, Vector3.up, SpellId, 0f, "red");
                            Effects.Apply(p, w, e, "mark", 20f, 0f);
                            Reveal.Mark(e, 20f);
                        }
                    }

                    break;
                case "trap":
                    foreach (var e in enemies)
                    {
                        if (Ready(null, 8f))
                        {
                            Effects.Apply(p, w, e, "stun", 2f, 0f, "scream");
                            Fx.Send(FxKind.Burst, c + Vector3.up, Vector3.up, SpellId, 2f);
                            Fx.Send(FxKind.Text, c + Vector3.up * 2.5f, Vector3.up, SpellId, 0f, Texts.L("ВОРЫ! ВОРЫ!", "THIEVES! THIEVES!"));
                            Casting.Message(Texts.L("Кошачий концерт: кто-то попался!", "Caterwauling Charm: someone is caught!"));
                        }
                    }

                    break;
                case "spelltrap":
                    if (enemies.Count > 0)
                    {
                        Fx.Send(FxKind.Burst, c + Vector3.up * 0.5f, Vector3.up, SpellId, 2.5f);
                        foreach (var e in enemies)
                        {
                            Hit(p, w, spell, e, 1.2f, 5f);
                            Effects.Apply(p, w, e, "stun", 1f, 0f);
                        }

                        Remove();
                    }

                    break;
                case "ageline":
                    foreach (var e in enemies) Push(e, 9f);
                    break;
                case "thief":
                    foreach (var e in enemies)
                    {
                        if (Ready(e, 4f))
                        {
                            Net.Cleanse(e, "buffs");
                            Effects.Apply(p, w, e, "weak", 3f, 30f);
                            Fx.Send(FxKind.Hit, e.GetCenterPoint(), Vector3.up, SpellId, 0f);
                        }
                    }

                    break;
                case "heal":
                    foreach (var a in allies)
                    {
                        a.Heal(a.GetMaxHealth() * 0.02f * (1f + w.Stats.Get(Stat.heal) / 100f), false);
                    }

                    if (allies.Count > 0) Fx.Send(FxKind.Heal, c, Vector3.up, SpellId, 0f);
                    break;
                case "thorns":
                    foreach (var e in enemies)
                    {
                        Hit(p, w, spell, e, 0.2f, 0f);
                        Effects.Apply(p, w, e, "slow", 1.2f, 30f);
                    }

                    break;
                case "tentacula":
                    foreach (var e in enemies)
                    {
                        Effects.Apply(p, w, e, "dot", 1.5f, Combat.Power(p, w, spell) * 0.15f, null, "poison");
                        if (Ready(e, 3f)) Effects.Apply(p, w, e, "root", 1f, 0f, "vines");
                    }

                    break;
                case "willow":
                    if (enemies.Count > 0)
                    {
                        var e = enemies[Random.Range(0, enemies.Count)];
                        Hit(p, w, spell, e, 1.1f, 15f);
                        Fx.Send(FxKind.Burst, e.transform.position + Vector3.up, Vector3.up, SpellId, 1.5f);
                    }

                    break;
                case "quiet":
                    foreach (var a in allies) Effects.Apply(p, w, a, "buff", 1.5f, 0f, null, null, "stealth:60");
                    foreach (var e in enemies) Effects.Apply(p, w, e, "deaf", 1.5f, 0f);
                    break;
                case "orb":
                    foreach (var e in enemies) if (Combat.IsUndead(e)) Hit(p, w, spell, e, 0.2f, 0f);
                    break;
                case "flame":
                    foreach (var a in allies) if (a != p) Net.Cleanse(a, "cold");
                    break;
                case "tent":
                    foreach (var a in allies) Effects.Apply(p, w, a, "buff", 1.5f, 0f, null, null, "hpregen:5;stamregen:30");
                    break;
                case "dome_shield":
                case "dome_repel":
                case "dome_great":
                    foreach (var e in enemies)
                    {
                        Push(e, Type == "dome_repel" ? 14f : 8f);
                        if (Type == "dome_repel") Hit(p, w, spell, e, 0.3f, 0f);
                    }

                    foreach (var a in allies) Effects.Apply(p, w, a, "buff", 1.5f, 0f, null, null, Type == "dome_great" ? "armor:30" : "armor:15");
                    break;
            }
        }

        private bool Ready(Character key, float cooldown)
        {
            if (key == null)
            {
                if (m_globalCool > Time.time) return false;
                m_globalCool = Time.time + cooldown;
                return true;
            }

            float t;
            if (m_cool.TryGetValue(key, out t) && t > Time.time) return false;
            m_cool[key] = Time.time + cooldown;
            return true;
        }

        private float m_globalCool;

        private void Hit(Player p, WizardState w, SpellDef spell, Character e, float mult, float push)
        {
            Vector3 dir = e.transform.position - transform.position;
            dir.y = 0f;
            Combat.Strike(p, w, spell, e, mult, e.GetCenterPoint(), dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.up, push);
        }

        private void Push(Character e, float force)
        {
            Vector3 o = e.transform.position - transform.position;
            o.y = 0f;
            if (o.sqrMagnitude < 0.01f) o = Random.insideUnitSphere;
            o.y = 0f;
            Net.Impulse(e, o.normalized * force + Vector3.up * 2f);
        }

        /// <summary>Domes stop the arrows, spit and spells of monsters (each game stops the ones it owns).</summary>
        private void BlockProjectiles()
        {
            if (Time.time - s_projectilesAt > 0.1f)
            {
                s_projectilesAt = Time.time;
                s_projectiles = FindObjectsByType<global::Projectile>(FindObjectsSortMode.None);
            }

            foreach (var pr in s_projectiles)
            {
                if (pr == null || pr.Nv() == null || !pr.Nv().IsValid() || !pr.Nv().IsOwner())
                {
                    continue;
                }

                var owner = pr.Owner();
                if (owner != null && (owner.IsPlayer() || owner.IsTamed()))
                {
                    continue;
                }

                if (Vector3.Distance(pr.transform.position, transform.position) <= Radius)
                {
                    Fx.Burst(pr.transform.position, 0.6f, m_color, 0.6f);
                    pr.Nv().Destroy();
                }
            }
        }
    }

    /// <summary>How each kind of anchor looks.</summary>
    internal static class AnchorLook
    {
        private static int s_solid = -2;

        private static int SolidLayer
        {
            get
            {
                if (s_solid == -2)
                {
                    s_solid = LayerMask.NameToLayer("static_solid");
                    if (s_solid < 0) s_solid = 0;
                }

                return s_solid;
            }
        }

        public static void Build(Anchor a, Color c)
        {
            var t = a.transform;
            float r = a.Radius;
            Color fire = new Color(1f, 0.45f, 0.1f);
            switch (a.Type)
            {
                case "sticky":
                    Ring(t, new Color(0.9f, 0.8f, 0.3f), r, 0.2f);
                    Loop(t, new Color(0.9f, 0.8f, 0.3f, 0.6f), r, 10f, 1.2f, 0.2f, 0.1f, 0.25f, false);
                    break;
                case "slip":
                    Ring(t, new Color(0.6f, 0.9f, 1f), r, 0.15f);
                    Loop(t, new Color(0.7f, 0.95f, 1f, 0.5f), r, 15f, 1f, 0.1f, 0.15f, 0.4f, true);
                    break;
                case "fire":
                case "flagrante":
                case "firering":
                    Ring(t, fire, r, 0.3f);
                    Loop(t, fire, r, 40f * r / 3f, 0.8f, 2.5f, 0.2f, 0.6f, true, a.Type == "firering");
                    Light(t, fire, r * 2f, 1.5f, 0.8f);
                    break;
                case "fiendfyre":
                    Ring(t, new Color(1f, 0.25f, 0.05f), r, 0.5f);
                    Loop(t, new Color(1f, 0.35f, 0.05f), r, 160f, 1.4f, 5f, 0.5f, 1.6f, true);
                    Loop(t, new Color(0.15f, 0.08f, 0.05f, 0.6f), r, 30f, 3f, 4f, 1.5f, 3f, false);
                    Light(t, new Color(1f, 0.3f, 0.05f), r * 2.5f, 3f, 2f);
                    break;
                case "diabolica":
                    Ring(t, new Color(0.15f, 0.05f, 0.25f), r, 0.6f);
                    Loop(t, new Color(0.25f, 0.05f, 0.4f), r, 90f, 1.2f, 3.5f, 0.4f, 1f, true, true);
                    Light(t, new Color(0.4f, 0.1f, 0.8f), r * 2f, 1.5f, 1f);
                    break;
                case "storm":
                    var cloud = new GameObject("WZ_Cloud");
                    cloud.transform.SetParent(t, false);
                    cloud.transform.localPosition = Vector3.up * 22f;
                    Loop(cloud.transform, new Color(0.2f, 0.22f, 0.28f, 0.7f), r, 25f, 6f, 0.3f, 6f, 12f, false);
                    Ring(t, new Color(0.6f, 0.8f, 1f), r, 0.12f);
                    break;
                case "ward":
                case "dome_shield":
                case "dome_repel":
                case "dome_great":
                case "greenhouse":
                    Color dc = a.Type == "greenhouse" ? new Color(0.4f, 1f, 0.5f) : a.Type == "dome_repel" ? new Color(1f, 0.55f, 0.3f) : new Color(0.6f, 0.8f, 1f);
                    Dome(t, dc, r, a.Type == "greenhouse" ? 0.08f : 0.16f);
                    Ring(t, dc, r, 0.15f);
                    if (a.Type == "greenhouse") Area(t, r, EffectArea.Type.Heat | EffectArea.Type.WarmCozyArea);
                    if (a.Type == "dome_great") Area(t, r, EffectArea.Type.PlayerBase | EffectArea.Type.NoMonsters);
                    break;
                case "safe":
                case "muggle":
                case "fidelius":
                    Ring(t, new Color(0.75f, 0.8f, 1f, 0.6f), r, 0.1f);
                    Dome(t, new Color(0.75f, 0.8f, 1f), r, 0.05f);
                    Area(t, r, EffectArea.Type.NoMonsters | EffectArea.Type.PlayerBase);
                    break;
                case "fog":
                case "smoke":
                    Color fc = a.Type == "fog" ? new Color(0.85f, 0.88f, 0.9f, 0.55f) : new Color(0.3f, 0.3f, 0.32f, 0.7f);
                    Loop(t, fc, r, a.Type == "fog" ? 60f : 45f, 6f, 0.3f, 2.5f, 5f, false);
                    break;
                case "alarm":
                case "trap":
                case "firetrap":
                case "spelltrap":
                case "thief":
                case "noapp":
                    Ring(t, new Color(c.r, c.g, c.b, 0.35f), r, 0.06f);
                    if (a.Type == "thief")
                    {
                        Loop(t, new Color(0.5f, 0.7f, 1f, 0.5f), r, 30f, 1.5f, -0.5f, 0.05f, 0.12f, true);
                    }

                    break;
                case "ageline":
                    Ring(t, new Color(1f, 0.85f, 0.3f), r, 0.12f);
                    Loop(t, new Color(1f, 0.85f, 0.3f), r, 50f, 1.5f, 1.5f, 0.04f, 0.1f, true, true);
                    break;
                case "bounce":
                    Ring(t, new Color(1f, 0.9f, 0.4f), r, 0.4f);
                    Pieces.Prim(t, PrimitiveType.Cylinder, Mats.Solid(new Color(1f, 0.9f, 0.4f), 0.3f), new Vector3(0f, 0.05f, 0f), new Vector3(r * 2f, 0.05f, r * 2f));
                    break;
                case "heal":
                    Ring(t, new Color(0.5f, 1f, 0.6f), r, 0.15f);
                    Loop(t, new Color(0.5f, 1f, 0.6f), r, 25f, 1.5f, 1.2f, 0.06f, 0.16f, true);
                    Flowers(t, r, 10);
                    break;
                case "thorns":
                    Ring(t, new Color(0.7f, 0.2f, 0.25f), r, 0.12f);
                    Flowers(t, r, 14);
                    break;
                case "tentacula":
                    Tentacles(t, r);
                    break;
                case "willow":
                    Willow(t);
                    break;
                case "quiet":
                    Ring(t, new Color(0.7f, 0.7f, 0.9f, 0.4f), r, 0.08f);
                    Dome(t, new Color(0.7f, 0.7f, 0.9f), r, 0.05f);
                    break;
                case "fortify":
                    Ring(t, new Color(0.6f, 0.75f, 1f), r, 0.15f);
                    Loop(t, new Color(0.6f, 0.75f, 1f, 0.6f), r, 20f, 2.5f, 0.6f, 0.05f, 0.12f, true);
                    break;
                case "orb":
                    var orb = Pieces.Prim(t, PrimitiveType.Sphere, Mats.Solid(new Color(1f, 0.97f, 0.85f), 3f), Vector3.zero, Vector3.one * 0.5f);
                    orb.AddComponent<Bob>();
                    Light(t, new Color(1f, 0.96f, 0.85f), r, 2.2f, 0f, true);
                    Loop(t, new Color(1f, 0.95f, 0.8f), 0.4f, 20f, 1f, 0.3f, 0.05f, 0.1f, true);
                    break;
                case "flame":
                    Color blue = new Color(0.35f, 0.55f, 1f);
                    Loop(t, blue, 0.25f, 40f, 0.7f, 1.5f, 0.15f, 0.35f, true);
                    Light(t, blue, 8f, 1.6f, 0.5f);
                    Area(t, r, EffectArea.Type.Heat);
                    break;
                case "tent":
                    Tent(t);
                    Area(t, r, EffectArea.Type.Heat | EffectArea.Type.WarmCozyArea);
                    break;
                case "sign":
                    Sign(t, fire);
                    break;
                case "wall":
                    Wall(t, a, c);
                    break;
                case "bridge":
                    Bridge(t, a, c);
                    break;
                case "trail":
                    Ring(t, c, r, 0.2f);
                    Loop(t, c, r, 30f * r / 3f, 0.8f, 1.2f, 0.1f, 0.3f, true);
                    break;
                default:
                    Ring(t, c, r, 0.15f);
                    break;
            }
        }

        // ------------------------------------------------------------------ pieces of the look
        private static void Ring(Transform parent, Color c, float r, float width)
        {
            var lr = Fx.Line(parent, "WZ_Ring", c, width);
            lr.useWorldSpace = false;
            lr.loop = true;
            int n = Mathf.Clamp((int)(r * 8f), 24, 128);
            lr.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                float ang = i * Mathf.PI * 2f / n;
                lr.SetPosition(i, new Vector3(Mathf.Cos(ang) * r, 0.15f, Mathf.Sin(ang) * r));
            }
        }

        private static void Loop(Transform parent, Color c, float r, float rate, float life, float speedUp, float sizeMin, float sizeMax, bool additive,
            bool edge = false)
        {
            var ps = Fx.NewParticles(parent, parent.position, Fx.Dot(), additive);
            var main = ps.main;
            main.loop = true;
            main.duration = 5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life * 1.3f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor = c;
            main.maxParticles = 2000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = Mathf.Min(400f, rate * Fx.Quality);
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Circle;
            sh.radius = r;
            sh.radiusThickness = edge ? 0.05f : 1f;
            sh.rotation = new Vector3(-90f, 0f, 0f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
            vel.y = new ParticleSystem.MinMaxCurve(speedUp * 0.6f, speedUp * 1.2f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
            Fx.FadeOut(ps, c);
            ps.gameObject.SetActive(true);
            ps.Play();
        }

        private static void Light(Transform parent, Color c, float range, float intensity, float height, bool shadows = false)
        {
            var go = new GameObject("WZ_Light");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.up * height;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.range = range;
            l.intensity = intensity;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        }

        private static void Dome(Transform parent, Color c, float r, float alpha)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = "WZ_Dome";
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * r * 2f;
            var m = Fx.Glass(new Color(c.r, c.g, c.b, alpha));
            var rend = go.GetComponent<Renderer>();
            rend.sharedMaterial = m;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<Pulse>().Base = alpha;
        }

        /// <summary>A vanilla effect area (warmth, a base, no monsters) so the game's own rules see the spell.</summary>
        private static void Area(Transform parent, float r, EffectArea.Type type)
        {
            var go = new GameObject("WZ_Area");
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            int layer = LayerMask.NameToLayer("effect_area");
            if (layer >= 0)
            {
                go.layer = layer;
            }

            var col = go.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = r;
            var area = go.AddComponent<EffectArea>();
            area.m_type = type;
            area.m_statusEffect = "";
            go.SetActive(true);
        }

        private static void Flowers(Transform parent, float r, int n)
        {
            var stem = Mats.Solid(new Color(0.2f, 0.5f, 0.2f));
            var cols = new[] { new Color(1f, 0.4f, 0.6f), new Color(1f, 0.85f, 0.3f), new Color(0.7f, 0.5f, 1f), new Color(0.9f, 0.15f, 0.2f) };
            for (int i = 0; i < n; i++)
            {
                Vector2 q = Random.insideUnitCircle * r * 0.9f;
                var p = new Vector3(q.x, 0f, q.y);
                float h = Random.Range(0.3f, 0.7f);
                Pieces.Prim(parent, PrimitiveType.Cylinder, stem, p + Vector3.up * h * 0.5f, new Vector3(0.04f, h * 0.5f, 0.04f));
                Pieces.Prim(parent, PrimitiveType.Sphere, Mats.Solid(cols[i % cols.Length], 0.2f), p + Vector3.up * h, Vector3.one * 0.18f);
            }
        }

        private static void Tentacles(Transform parent, float r)
        {
            var mat = Mats.Solid(new Color(0.3f, 0.55f, 0.25f));
            var tip = Mats.Solid(new Color(0.6f, 0.2f, 0.5f), 0.3f);
            for (int i = 0; i < 6; i++)
            {
                Vector2 q = Random.insideUnitCircle * r * 0.7f;
                var root = new GameObject("WZ_Tentacle");
                root.transform.SetParent(parent, false);
                root.transform.localPosition = new Vector3(q.x, 0f, q.y);
                Pieces.Prim(root.transform, PrimitiveType.Capsule, mat, Vector3.up * 0.9f, new Vector3(0.25f, 0.9f, 0.25f));
                Pieces.Prim(root.transform, PrimitiveType.Sphere, tip, Vector3.up * 1.8f, Vector3.one * 0.3f);
                var sway = root.AddComponent<Sway>();
                sway.Amount = 25f;
                sway.Speed = Random.Range(1.5f, 3f);
            }
        }

        private static void Willow(Transform parent)
        {
            var bark = Mats.Solid(new Color(0.35f, 0.25f, 0.18f));
            var leaves = Mats.Solid(new Color(0.25f, 0.45f, 0.2f));
            Pieces.Prim(parent, PrimitiveType.Cylinder, bark, Vector3.up * 2.5f, new Vector3(0.9f, 2.5f, 0.9f));
            Pieces.Prim(parent, PrimitiveType.Sphere, leaves, Vector3.up * 6f, new Vector3(5f, 3f, 5f));
            for (int i = 0; i < 4; i++)
            {
                var arm = new GameObject("WZ_Branch");
                arm.transform.SetParent(parent, false);
                arm.transform.localPosition = Vector3.up * 4f;
                arm.transform.localRotation = Quaternion.Euler(0f, i * 90f, 0f);
                var b = Pieces.Prim(arm.transform, PrimitiveType.Cylinder, bark, new Vector3(0f, 0f, 2.5f), new Vector3(0.3f, 2.5f, 0.3f));
                b.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var s = arm.AddComponent<Sway>();
                s.Amount = 40f;
                s.Speed = 2f + i * 0.3f;
                s.Axis = Vector3.right;
            }
        }

        private static void Tent(Transform parent)
        {
            var cloth = Mats.Solid(new Color(0.75f, 0.68f, 0.5f));
            var wood = Mats.Solid(new Color(0.4f, 0.28f, 0.18f));
            Pieces.Prim(parent, PrimitiveType.Cylinder, Mats.Solid(new Color(0.55f, 0.2f, 0.2f)), new Vector3(0f, 0.02f, 0f), new Vector3(4f, 0.02f, 4f));
            foreach (var x in new[] { -1.6f, 1.6f })
            {
                foreach (var z in new[] { -1.6f, 1.6f })
                {
                    Pieces.Prim(parent, PrimitiveType.Cylinder, wood, new Vector3(x, 1.2f, z), new Vector3(0.1f, 1.2f, 0.1f));
                }
            }

            foreach (var side in new[] { -1f, 1f })
            {
                var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slab.name = "WZ_Roof";
                slab.layer = SolidLayer;
                slab.transform.SetParent(parent, false);
                slab.transform.localPosition = new Vector3(side * 1f, 3f, 0f);
                slab.transform.localRotation = Quaternion.Euler(0f, 0f, -side * 32f);
                slab.transform.localScale = new Vector3(2.5f, 0.06f, 4f);
                slab.GetComponent<Renderer>().sharedMaterial = cloth;
            }

            Light(parent, new Color(1f, 0.8f, 0.55f), 6f, 1f, 2f);
        }

        private static void Sign(Transform parent, Color c)
        {
            int strokes = Random.Range(3, 6);
            for (int i = 0; i < strokes; i++)
            {
                var lr = Fx.Line(parent, "WZ_Stroke", c, 0.08f);
                lr.useWorldSpace = false;
                lr.positionCount = 4;
                Vector3 a = new Vector3(Random.Range(-0.8f, 0.8f), Random.Range(-0.8f, 0.8f), 0f);
                for (int k = 0; k < 4; k++)
                {
                    lr.SetPosition(k, a);
                    a += new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f), 0f);
                }
            }

            Light(parent, c, 6f, 1.2f, 0f);
        }

        private static void Wall(Transform parent, Anchor a, Color c)
        {
            float width = Mathf.Max(2f, a.Radius);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "WZ_Wall";
            wall.layer = SolidLayer;
            wall.transform.SetParent(parent, false);
            wall.transform.localPosition = Vector3.up * 2f;
            wall.transform.localRotation = Quaternion.identity;
            wall.transform.localScale = new Vector3(width, 4f, 0.4f);
            var rend = wall.GetComponent<Renderer>();
            rend.sharedMaterial = Fx.Glass(new Color(c.r, c.g, c.b, 0.3f));
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            wall.AddComponent<Pulse>().Base = 0.3f;
        }

        private static void Bridge(Transform parent, Anchor a, Color c)
        {
            float length = Mathf.Max(4f, a.Radius);
            var deck = GameObject.CreatePrimitive(PrimitiveType.Cube);
            deck.name = "WZ_Bridge";
            deck.layer = SolidLayer;
            deck.transform.SetParent(parent, false);
            deck.transform.localPosition = new Vector3(0f, -0.1f, length * 0.5f + 0.5f);
            deck.transform.localScale = new Vector3(2.4f, 0.2f, length);
            deck.GetComponent<Renderer>().sharedMaterial = Pieces.VanillaMaterial("wood_floor", "wood_floor_1x1");
            foreach (var side in new[] { -1.2f, 1.2f })
            {
                var rail = Fx.Line(parent, "WZ_Rail", c, 0.1f);
                rail.useWorldSpace = false;
                rail.positionCount = 2;
                rail.SetPosition(0, new Vector3(side, 0.9f, 0.5f));
                rail.SetPosition(1, new Vector3(side, 0.9f, length + 0.5f));
            }
        }
    }

    internal sealed class Bob : MonoBehaviour
    {
        private Vector3 m_base;

        private void Start()
        {
            m_base = transform.localPosition;
        }

        private void Update()
        {
            transform.localPosition = m_base + Vector3.up * Mathf.Sin(Time.time * 1.5f) * 0.25f;
        }
    }

    internal sealed class Sway : MonoBehaviour
    {
        public float Amount = 20f, Speed = 2f;
        public Vector3 Axis = Vector3.forward;
        private Quaternion m_base;
        private float m_phase;

        private void Start()
        {
            m_base = transform.localRotation;
            m_phase = Random.value * 10f;
        }

        private void Update()
        {
            transform.localRotation = m_base * Quaternion.AngleAxis(Mathf.Sin(Time.time * Speed + m_phase) * Amount, Axis);
        }
    }

    internal sealed class Pulse : MonoBehaviour
    {
        public float Base = 0.15f;
        private Renderer m_r;
        private Color m_c;

        private void Start()
        {
            m_r = GetComponent<Renderer>();
            if (m_r != null)
            {
                m_r.material = new Material(m_r.sharedMaterial);
                m_c = m_r.material.color;
            }
        }

        private void Update()
        {
            if (m_r == null)
            {
                return;
            }

            var c = m_c;
            c.a = Base * (0.75f + 0.25f * Mathf.Sin(Time.time * 2f));
            m_r.material.color = c;
        }

        private void OnDestroy()
        {
            if (m_r != null && m_r.material != null)
            {
                Destroy(m_r.material);
            }
        }
    }
}
