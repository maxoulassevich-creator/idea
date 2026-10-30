using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Wizardry
{
    /// <summary>
    ///     Conjured creatures (real, tamed, short-lived network creatures that follow the wizard and drop nothing), and
    ///     conjured spirits (the Patronus, flocks of birds) that every game draws while the caster's game does their work.
    /// </summary>
    internal static class Summons
    {
        public static readonly List<SummonLife> Alive = new List<SummonLife>();

        private static readonly Dictionary<string, string[]> s_alias = new Dictionary<string, string[]>
        {
            { "snake", new[] { "Asksvin_hatchling", "Leech" } },
            { "Skeleton_Friendly", new[] { "Skeleton_Friendly", "Skeleton" } },
            { "StoneGolem", new[] { "StoneGolem" } },
        };

        public static void Spawn(CastContext c, Vector3 p, FxOp o)
        {
            var caster = c.Caster;
            var w = c.W;
            string kinds = o.S(0, "Wolf");
            int count = Mathf.Max(1, (int)o.F(1, 1f)) + (c.Patterns.Contains("s_plus1") ? 1 : 0);
            float dur = o.F(2, 20f) * (1f + w.Stats.Get(Stat.summon_dur) / 100f) * (c.Patterns.Contains("s_dur") ? 1.4f : 1f);
            float hpBonus = c.Patterns.Contains("s_hp") ? 1.4f : 1f;
            string tag = o.S(3, "");
            if (tag == "patronus")
            {
                Patronus(c, dur);
                return;
            }

            if (tag == "bird")
            {
                Birds(c, p, count + (int)w.Stats.Get(Stat.birds), dur, c.Spec.Has("passive"));
                return;
            }

            string prefab = null;
            foreach (string k in kinds.Split('/'))
            {
                string[] names;
                foreach (string n in s_alias.TryGetValue(k, out names) ? names : new[] { k })
                {
                    if (ZNetScene.instance != null && ZNetScene.instance.GetPrefab(n) != null)
                    {
                        prefab = n;
                        break;
                    }
                }

                if (prefab != null)
                {
                    break;
                }
            }

            if (prefab == null)
            {
                Casting.Message(Texts.L("Этого существа нет в мире.", "That creature does not exist in this world."));
                return;
            }

            float scale = tag == "small" ? (prefab == "StoneGolem" ? 0.4f : 0.7f) : 1f;
            for (int i = 0; i < count; i++)
            {
                Vector2 q = UnityEngine.Random.insideUnitCircle * (count > 1 ? 2f : 0.5f);
                Create(caster, w, prefab, p + new Vector3(q.x, 0.3f, q.y), dur, tag, scale, false, hpBonus);
            }
        }

        /// <summary>Illusions that draw the enemies' attention (Geminio, Aquarius Scarius).</summary>
        public static void Decoy(CastContext c, Vector3 p, int count, float dur)
        {
            string prefab = ZNetScene.instance != null && ZNetScene.instance.GetPrefab("Skeleton_Friendly") != null ? "Skeleton_Friendly" : "Skeleton";
            for (int i = 0; i < Mathf.Max(1, count); i++)
            {
                Vector2 q = UnityEngine.Random.insideUnitCircle * 2.5f;
                var ch = Create(c.Caster, c.W, prefab, p + new Vector3(q.x, 0.3f, q.y), dur, "decoy", 1f, true);
                if (ch == null)
                {
                    continue;
                }

                foreach (var e in Character.GetAllCharacters())
                {
                    if (Combat.IsEnemy(c.Caster, e) && Vector3.Distance(e.transform.position, p) < 20f)
                    {
                        Effects.ApplyTaunt(c.Caster, e, ch, dur);
                    }
                }
            }

            Effects.Apply(c.Caster, c.W, c.Caster, "buff", 2f, 0f, null, null, "stealth:80");
        }

        private static Character Create(Player caster, WizardState w, string prefabName, Vector3 pos, float dur, string tag, float scale, bool decoy,
            float hpBonus = 1f)
        {
            Limit(caster.GetPlayerID(), 2 + (int)w.Stats.Get(Stat.summon_limit) + (decoy ? 2 : 0));
            var prefab = ZNetScene.instance.GetPrefab(prefabName);
            var go = Object.Instantiate(prefab, pos, Quaternion.LookRotation(caster.transform.forward));
            var ch = go.GetComponent<Character>();
            var zdo = ch != null && ch.Nv() != null ? ch.Nv().GetZDO() : null;
            if (zdo == null)
            {
                Object.Destroy(go);
                return null;
            }

            zdo.Set("wz_summoner", caster.GetPlayerID());
            zdo.Set("wz_until", ZNet.instance.GetTime().Ticks + (long)(dur * 1e7));
            zdo.Set("wz_tag", tag);
            zdo.Set("wz_scale", scale);
            ch.SetTamed(true);
            int level = 1 + (w.Year >= 5 ? 1 : 0) + (w.Year >= 8 ? 1 : 0);
            ch.SetLevel(level);
            float hpMult = (1f + w.Stats.Get(Stat.summon_hp) / 100f) * (decoy ? 0.3f : 1f) * (tag == "small" ? 0.6f : 1f) * hpBonus;
            ch.SetMaxHealth(ch.GetMaxHealth() * hpMult);
            ch.SetHealth(ch.GetMaxHealth());
            var ai = ch.GetBaseAI() as MonsterAI;
            if (ai != null && !decoy)
            {
                ai.SetFollowTarget(caster.gameObject);
            }

            Attach(ch);
            Fx.Send(FxKind.Summon, pos, Vector3.up, "", 1.5f);
            return ch;
        }

        /// <summary>The Resurrection Stone's echo: a ghostly warrior for half a minute.</summary>
        public static void Echo(Player caster, WizardState w)
        {
            foreach (string n in new[] { "Draugr_Elite", "Draugr", "Skeleton_Friendly" })
            {
                if (ZNetScene.instance != null && ZNetScene.instance.GetPrefab(n) != null)
                {
                    Create(caster, w, n, caster.transform.position + caster.transform.right * 2f, 30f, "ghost", 1f, false);
                    return;
                }
            }
        }

        private static void Limit(long owner, int max)
        {
            var mine = new List<SummonLife>();
            foreach (var s in Alive)
            {
                if (s != null && s.Summoner == owner)
                {
                    mine.Add(s);
                }
            }

            mine.Sort((a, b) => a.Until.CompareTo(b.Until));
            for (int i = 0; i <= mine.Count - Mathf.Max(1, max); i++)
            {
                mine[i].Dismiss();
            }
        }

        public static void OnCharacterAwake(Character c)
        {
            var zdo = c != null && c.Nv() != null ? c.Nv().GetZDO() : null;
            if (zdo != null && zdo.GetLong("wz_summoner") != 0L)
            {
                Attach(c);
            }
        }

        private static void Attach(Character c)
        {
            var s = c.GetComponent<SummonLife>() ?? c.gameObject.AddComponent<SummonLife>();
            s.Setup();
        }

        /// <summary>Is <paramref name="attacker" /> one of the local wizard's summons?</summary>
        public static bool IsMine(Character attacker)
        {
            var s = attacker != null ? attacker.GetComponent<SummonLife>() : null;
            return s != null && Player.m_localPlayer != null && s.Summoner == Player.m_localPlayer.GetPlayerID();
        }

        // ------------------------------------------------------------------ spirits
        private static void Patronus(CastContext c, float dur)
        {
            var caster = c.Caster;
            var w = c.W;
            var zdo = caster.Nv() != null ? caster.Nv().GetZDO() : null;
            if (zdo == null)
            {
                return;
            }

            string form = string.IsNullOrEmpty(w.Patronus) ? "Deer" : w.Patronus;
            Fx.Send(FxKind.Summon, caster.transform.position, caster.transform.forward, c.Spell.Id, dur, "patronus:" + form, zdo.m_uid);
            var spell = c.Spell;
            Repeat(1f, dur, () =>
            {
                if (caster == null || caster.IsDead())
                {
                    return;
                }

                foreach (var e in Character.GetAllCharacters())
                {
                    if (!Combat.IsEnemy(caster, e) || Vector3.Distance(e.transform.position, caster.transform.position) > 12f)
                    {
                        continue;
                    }

                    if (!e.IsBoss())
                    {
                        Effects.Apply(caster, w, e, "fear", 2f, 0f);
                    }

                    if (Combat.IsUndead(e) || Combat.Prefab(e).Contains("Ghost") || Combat.Prefab(e).Contains("Wraith"))
                    {
                        Combat.Strike(caster, w, spell, e, 0.6f, e.GetCenterPoint(), (e.transform.position - caster.transform.position).normalized, 8f);
                    }
                }
            });
        }

        private static void Birds(CastContext c, Vector3 p, int count, float dur, bool passive)
        {
            var caster = c.Caster;
            var w = c.W;
            var spell = c.Spell;
            Fx.Send(FxKind.Summon, p, Vector3.up, spell.Id, dur, "birds:" + count);
            if (passive)
            {
                return;
            }

            Repeat(1f, dur, () =>
            {
                if (caster == null)
                {
                    return;
                }

                foreach (var e in Character.GetAllCharacters())
                {
                    if (Combat.IsEnemy(caster, e) && Vector3.Distance(e.transform.position, p) < 9f)
                    {
                        Combat.Strike(caster, w, spell, e, 0.06f * count, e.GetTopPoint(), Vector3.down, 0f);
                        if (UnityEngine.Random.value < 0.25f)
                        {
                            Effects.Apply(caster, w, e, "blind", 1f, 0f);
                        }
                    }
                }
            });
        }

        public static void Repeat(float interval, float duration, Action tick)
        {
            int n = Mathf.Max(1, Mathf.FloorToInt(duration / interval));
            for (int i = 1; i <= n; i++)
            {
                Later.Run(i * interval, tick);
            }
        }

        /// <summary>Every game: draw a Patronus beside its wizard, or a flock of birds over a place.</summary>
        public static void Show(Vector3 pos, Vector3 dir, string extra, float dur, ZDOID target)
        {
            if (extra.StartsWith("patronus:"))
            {
                var go = target != ZDOID.None && ZNetScene.instance != null ? ZNetScene.instance.FindInstance(target) : null;
                var spirit = new GameObject("WZ_Patronus");
                spirit.transform.position = pos;
                var f = spirit.AddComponent<SpiritFollow>();
                f.Target = go != null ? go.transform : null;
                f.Until = Time.time + dur;
                Spirit(spirit.transform, extra.Substring(9));
                Fx.Sound(pos, "choir", 0.7f, 80f);
                return;
            }

            if (extra.StartsWith("birds:"))
            {
                int n;
                int.TryParse(extra.Substring(6), out n);
                n = Mathf.Clamp(n, 1, 20);
                for (int i = 0; i < n; i++)
                {
                    var b = new GameObject("WZ_Bird");
                    b.transform.position = pos;
                    var orbit = b.AddComponent<Orbit>();
                    orbit.Center = pos + Vector3.up * UnityEngine.Random.Range(2f, 4.5f);
                    orbit.Radius = UnityEngine.Random.Range(2f, 6f);
                    orbit.Speed = UnityEngine.Random.Range(1.2f, 2.4f) * (UnityEngine.Random.value < 0.5f ? 1f : -1f);
                    orbit.Phase = UnityEngine.Random.value * 6.28f;
                    Object.Destroy(b, dur);
                    if (!Copy(b.transform, "Crow", null) && !Copy(b.transform, "Seagal", null))
                    {
                        Pieces.Prim(b.transform, PrimitiveType.Sphere, Mats.Solid(new Color(0.15f, 0.15f, 0.18f)), Vector3.zero, new Vector3(0.25f, 0.2f, 0.4f));
                    }
                }

                Fx.Sound(pos, "whoosh", 0.6f);
            }
        }

        private static void Spirit(Transform root, string form)
        {
            var glass = Fx.Glass(new Color(0.75f, 0.9f, 1f, 0.55f));
            glass.mainTexture = Fx.Dot();
            if (!Copy(root, form, glass) && !Copy(root, "Deer", glass))
            {
                Pieces.Prim(root, PrimitiveType.Sphere, glass, Vector3.up, new Vector3(0.8f, 0.8f, 1.6f));
            }

            var ps = Fx.NewParticles(root, root.position + Vector3.up, Fx.Dot());
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 1.2f;
            main.startSpeed = 0.3f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
            main.startColor = new Color(0.8f, 0.92f, 1f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 60f * Fx.Quality;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.8f;
            Fx.FadeOut(ps, new Color(0.8f, 0.92f, 1f));
            ps.gameObject.SetActive(true);
            ps.Play();
            var lg = new GameObject("WZ_Light");
            lg.transform.SetParent(root, false);
            lg.transform.localPosition = Vector3.up;
            var l = lg.AddComponent<Light>();
            l.color = new Color(0.75f, 0.88f, 1f);
            l.range = 12f;
            l.intensity = 2f;
        }

        /// <summary>Copies a vanilla creature's look (only its renderers and bones) under <paramref name="parent" />.</summary>
        public static bool Copy(Transform parent, string prefabName, Material replace)
        {
            var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefabName) : null;
            if (prefab == null)
            {
                return false;
            }

            var vis = prefab.transform.Find("Visual") ?? prefab.transform.Find("visual");
            GameObject src = vis != null ? vis.gameObject : null;
            if (src == null)
            {
                var r = prefab.GetComponentInChildren<Renderer>(true);
                if (r == null || r.transform == prefab.transform)
                {
                    return false;
                }

                src = r.gameObject;
            }

            var holder = new GameObject("WZ_Look");
            holder.SetActive(false);
            holder.transform.SetParent(parent, false);
            var copy = Object.Instantiate(src, holder.transform);
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true))
            {
                Object.DestroyImmediate(mb);
            }

            foreach (var col in copy.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(col);
            }

            foreach (var rb in copy.GetComponentsInChildren<Rigidbody>(true))
            {
                Object.DestroyImmediate(rb);
            }

            if (replace != null)
            {
                foreach (var rend in copy.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = rend.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        mats[i] = replace;
                    }

                    rend.sharedMaterials = mats;
                    rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }

            holder.SetActive(true);
            return true;
        }
    }

    /// <summary>On a conjured creature: its lifetime, size, look, loyalty, and no loot.</summary>
    internal sealed class SummonLife : MonoBehaviour
    {
        public long Summoner, Until;
        public string Tag;
        private Character m_char;
        private ZNetView m_nview;
        private bool m_setup;
        private float m_check;

        public void Setup()
        {
            if (m_setup)
            {
                return;
            }

            m_char = GetComponent<Character>();
            m_nview = GetComponent<ZNetView>();
            var zdo = m_nview != null ? m_nview.GetZDO() : null;
            if (zdo == null)
            {
                return;
            }

            m_setup = true;
            Summoner = zdo.GetLong("wz_summoner");
            Until = zdo.GetLong("wz_until");
            Tag = zdo.GetString("wz_tag");
            float scale = zdo.GetFloat("wz_scale", 1f);
            if (Mathf.Abs(scale - 1f) > 0.01f)
            {
                transform.localScale = transform.localScale * scale;
            }

            var drop = GetComponent<CharacterDrop>();
            if (drop != null)
            {
                drop.SetDropsEnabled(false);
            }

            if (Tag == "ghost" || Tag == "decoy")
            {
                var glass = Fx.Glass(Tag == "ghost" ? new Color(0.5f, 0.7f, 1f, 0.5f) : new Color(0.7f, 0.85f, 1f, 0.35f));
                glass.mainTexture = Fx.Dot();
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                {
                    if (r is ParticleSystemRenderer)
                    {
                        continue;
                    }

                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        mats[i] = glass;
                    }

                    r.sharedMaterials = mats;
                }
            }
            else if (Tag == "armor")
            {
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                {
                    if (r is ParticleSystemRenderer)
                    {
                        continue;
                    }

                    var block = new MaterialPropertyBlock();
                    r.GetPropertyBlock(block);
                    block.SetColor("_Color", new Color(0.75f, 0.78f, 0.85f));
                    r.SetPropertyBlock(block);
                }
            }

            Summons.Alive.Add(this);
        }

        private void OnDestroy()
        {
            Summons.Alive.Remove(this);
        }

        public void Dismiss()
        {
            if (m_nview == null || !m_nview.IsValid())
            {
                return;
            }

            Fx.Send(FxKind.Blink, transform.position, Vector3.up, "", 0f);
            if (!m_nview.IsOwner())
            {
                m_nview.ClaimOwnership();
            }

            m_nview.Destroy();
        }

        private void Update()
        {
            if (!m_setup || m_nview == null || !m_nview.IsValid() || !m_nview.IsOwner() || ZNet.instance == null)
            {
                return;
            }

            m_check -= Time.deltaTime;
            if (m_check > 0f)
            {
                return;
            }

            m_check = 1f;
            if (ZNet.instance.GetTime().Ticks > Until)
            {
                Dismiss();
                return;
            }

            var me = Player.m_localPlayer;
            if (me == null || me.GetPlayerID() != Summoner || Tag == "decoy")
            {
                return;
            }

            var ai = m_char != null ? m_char.GetBaseAI() as MonsterAI : null;
            if (ai != null && ai.GetFollowTarget() == null && Vector3.Distance(me.transform.position, transform.position) < 60f)
            {
                ai.SetFollowTarget(me.gameObject);
            }
        }
    }

    /// <summary>A Patronus keeps beside its wizard and gallops a little ahead of them.</summary>
    internal sealed class SpiritFollow : MonoBehaviour
    {
        public Transform Target;
        public float Until;

        private void Update()
        {
            if (Time.time > Until)
            {
                Destroy(gameObject);
                return;
            }

            if (Target == null)
            {
                return;
            }

            float t = Time.time * 0.8f;
            Vector3 goal = Target.position + Target.forward * (2.5f + Mathf.Sin(t) * 1.5f) + Target.right * Mathf.Cos(t * 0.7f) * 2f;
            transform.position = Vector3.Lerp(transform.position, goal, Time.deltaTime * 3f);
            Vector3 look = goal - transform.position;
            look.y = 0f;
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(look.sqrMagnitude > 0.01f ? look : Target.forward), Time.deltaTime * 5f);
        }
    }

    internal sealed class Orbit : MonoBehaviour
    {
        public Vector3 Center;
        public float Radius = 3f, Speed = 1.5f, Phase;

        private void Update()
        {
            float a = Phase + Time.time * Speed;
            var p = Center + new Vector3(Mathf.Cos(a), Mathf.Sin(a * 2.3f) * 0.15f, Mathf.Sin(a)) * Radius;
            var fwd = p - transform.position;
            transform.position = p;
            if (fwd.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(fwd);
            }
        }
    }
}
