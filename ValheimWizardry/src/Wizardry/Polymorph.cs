using System.Collections.Generic;
using UnityEngine;

namespace Wizardry
{
    /// <summary>
    ///     Transfiguration of creatures: the owner gets the "poly" status (it cannot move or fight), every game hides the
    ///     creature's own body and shows the new shape in its place until the spell runs out or is undone.
    /// </summary>
    internal static class Polymorph
    {
        private static readonly Dictionary<string, string[]> s_creatures = new Dictionary<string, string[]>
        {
            { "Hen", new[] { "Hen", "Chicken" } },
            { "Hare", new[] { "Hare" } },
            { "Boar", new[] { "Boar_piggy", "Boar" } },
            { "fish", new[] { "Fish1", "Fish2" } },
        };

        public static void Cast(Player caster, WizardState w, Character t, string form, float duration, bool firebomb)
        {
            if (t == null || t.IsDead() || t.m_nview == null || t.m_nview.GetZDO() == null)
            {
                return;
            }

            if (t.IsBoss())
            {
                Effects.Apply(caster, w, t, "slow", duration * 0.5f, 40f);
                Casting.Message(Texts.L("Хозяин этих мест не поддаётся превращению.", "A lord of this land cannot be transfigured."));
                return;
            }

            float d = duration * (1f + w.Stats.Get(Stat.ctrl_dur) / 100f) * (1f + w.Stats.Get(Stat.poly_dur) / 100f);
            if (t.IsPlayer())
            {
                d *= ModConfig.PvpControl.Value;
                if (d <= 0.05f)
                {
                    return;
                }
            }

            Effects.Apply(caster, w, t, "poly", duration, 0f, form);
            Fx.Send(FxKind.Poly, t.transform.position, Vector3.up, "", d, form, t.m_nview.GetZDO().m_uid);
            if (w.Stats.Has("poly_restore"))
            {
                caster.AddEitr(5f);
            }

            if (firebomb)
            {
                var spell = SpellDb.Get("flintifors");
                Later.Run(d, () =>
                {
                    if (t == null || t.IsDead() || caster == null || spell == null)
                    {
                        return;
                    }

                    Vector3 p = t.transform.position;
                    Fx.Send(FxKind.Burst, p + Vector3.up * 0.5f, Vector3.up, spell.Id, 3f);
                    foreach (var c in Character.GetAllCharacters())
                    {
                        if (Combat.IsEnemy(caster, c) && Vector3.Distance(c.transform.position, p) < 3f)
                        {
                            Combat.Strike(caster, w, spell, c, 1.5f, c.GetCenterPoint(), (c.transform.position - p).normalized, 20f);
                        }
                    }
                });
            }
        }

        /// <summary>Every game: show (or, with zero duration, end) a creature's new shape.</summary>
        public static void Show(ZDOID target, string form, float duration)
        {
            if (ZNetScene.instance == null || target == ZDOID.None)
            {
                return;
            }

            var go = ZNetScene.instance.FindInstance(target);
            var ch = go != null ? go.GetComponent<Character>() : null;
            if (ch == null)
            {
                return;
            }

            var shape = ch.GetComponent<PolyShape>();
            if (duration <= 0f)
            {
                if (shape != null)
                {
                    shape.End();
                }

                return;
            }

            if (shape == null)
            {
                shape = ch.gameObject.AddComponent<PolyShape>();
            }

            shape.Begin(form, duration);
        }

        public static GameObject Build(Transform parent, string form, float height)
        {
            var holder = new GameObject("WZ_PolyModel");
            holder.SetActive(false);
            holder.transform.SetParent(parent, false);
            string[] names;
            if (s_creatures.TryGetValue(form, out names))
            {
                foreach (string n in names)
                {
                    var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(n) : null;
                    var vis = prefab != null ? prefab.transform.Find("Visual") ?? prefab.transform.Find("visual") : null;
                    if (vis == null)
                    {
                        continue;
                    }

                    var copy = Object.Instantiate(vis.gameObject, holder.transform);
                    Strip(copy);
                    copy.transform.localPosition = Vector3.zero;
                    copy.transform.localRotation = Quaternion.identity;
                    holder.SetActive(true);
                    return holder;
                }
            }

            BuildProp(holder.transform, form, height);
            holder.SetActive(true);
            return holder;
        }

        private static void Strip(GameObject copy)
        {
            foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true))
            {
                Object.DestroyImmediate(mb);
            }

            foreach (var c in copy.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(c);
            }

            foreach (var rb in copy.GetComponentsInChildren<Rigidbody>(true))
            {
                Object.DestroyImmediate(rb);
            }

            foreach (var a in copy.GetComponentsInChildren<Animator>(true))
            {
                a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
        }

        /// <summary>Things that are not animals: a goblet, a hay bale, a bunch of flowers, a matchbox, worms.</summary>
        private static void BuildProp(Transform root, string form, float height)
        {
            var gold = Mats.Solid(new Color(0.95f, 0.75f, 0.25f), 0.2f);
            var straw = Mats.Solid(new Color(0.85f, 0.72f, 0.35f));
            var green = Mats.Solid(new Color(0.25f, 0.6f, 0.2f));
            switch (form)
            {
                case "goblet":
                    Pieces.Prim(root, PrimitiveType.Cylinder, gold, new Vector3(0f, 0.03f, 0f), new Vector3(0.35f, 0.03f, 0.35f));
                    Pieces.Prim(root, PrimitiveType.Cylinder, gold, new Vector3(0f, 0.25f, 0f), new Vector3(0.08f, 0.2f, 0.08f));
                    Pieces.Prim(root, PrimitiveType.Sphere, gold, new Vector3(0f, 0.6f, 0f), new Vector3(0.45f, 0.4f, 0.45f));
                    Pieces.Prim(root, PrimitiveType.Cylinder, gold, new Vector3(0f, 0.75f, 0f), new Vector3(0.42f, 0.12f, 0.42f));
                    break;
                case "hay":
                    Pieces.Prim(root, PrimitiveType.Cube, straw, new Vector3(0f, 0.4f, 0f), new Vector3(1.1f, 0.8f, 0.7f));
                    Pieces.Prim(root, PrimitiveType.Cube, Mats.Solid(new Color(0.45f, 0.3f, 0.15f)), new Vector3(0.3f, 0.4f, 0f), new Vector3(0.05f, 0.82f, 0.72f));
                    Pieces.Prim(root, PrimitiveType.Cube, Mats.Solid(new Color(0.45f, 0.3f, 0.15f)), new Vector3(-0.3f, 0.4f, 0f), new Vector3(0.05f, 0.82f, 0.72f));
                    break;
                case "flowers":
                    var petals = new[] { new Color(1f, 0.4f, 0.6f), new Color(1f, 0.9f, 0.3f), new Color(0.6f, 0.5f, 1f), new Color(1f, 1f, 1f) };
                    for (int i = 0; i < 7; i++)
                    {
                        float a = i * 0.9f;
                        var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.15f * (i % 3);
                        float h = 0.4f + 0.12f * (i % 3);
                        Pieces.Prim(root, PrimitiveType.Cylinder, green, p + Vector3.up * h * 0.5f, new Vector3(0.03f, h * 0.5f, 0.03f));
                        Pieces.Prim(root, PrimitiveType.Sphere, Mats.Solid(petals[i % petals.Length], 0.1f), p + Vector3.up * h, Vector3.one * 0.16f);
                    }

                    break;
                case "matchbox":
                    Pieces.Prim(root, PrimitiveType.Cube, Mats.Solid(new Color(0.75f, 0.2f, 0.15f)), new Vector3(0f, 0.12f, 0f), new Vector3(0.5f, 0.24f, 0.32f));
                    Pieces.Prim(root, PrimitiveType.Cube, Mats.Solid(new Color(0.9f, 0.85f, 0.7f)), new Vector3(0.22f, 0.14f, 0f), new Vector3(0.1f, 0.2f, 0.33f));
                    break;
                case "worms":
                    var pink = Mats.Solid(new Color(0.9f, 0.5f, 0.5f));
                    for (int i = 0; i < 6; i++)
                    {
                        var w = Pieces.Prim(root, PrimitiveType.Capsule, pink, new Vector3(Random.Range(-0.4f, 0.4f), 0.04f, Random.Range(-0.4f, 0.4f)),
                            new Vector3(0.06f, 0.18f, 0.06f));
                        w.transform.localRotation = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);
                        w.AddComponent<Spin>().Speed = Random.Range(-90f, 90f);
                    }

                    break;
                default:
                    Pieces.Prim(root, PrimitiveType.Sphere, Mats.Solid(new Color(0.8f, 0.8f, 0.85f), 0.2f), new Vector3(0f, 0.3f, 0f), new Vector3(0.7f, 0.5f, 0.9f));
                    break;
            }
        }
    }

    /// <summary>Hides a transfigured creature's body and shows its new shape.</summary>
    internal sealed class PolyShape : MonoBehaviour
    {
        private readonly HashSet<Renderer> m_hidden = new HashSet<Renderer>();
        private GameObject m_model;
        private Character m_char;
        private float m_until, m_scan;
        private string m_form;

        public void Begin(string form, float duration)
        {
            m_char = GetComponent<Character>();
            m_until = Time.time + duration;
            if (m_model != null && m_form == form)
            {
                return;
            }

            if (m_model != null)
            {
                Destroy(m_model);
            }

            m_form = form;
            Hide();
            float h = m_char != null ? Mathf.Max(0.5f, m_char.GetHeight()) : 1f;
            m_model = Polymorph.Build(transform, form, h);
            Fx.Smoke(transform.position, new Color(0.85f, 0.85f, 1f, 0.7f));
            Fx.Sound(transform.position, "pop", 0.7f);
        }

        private void Hide()
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled || r is ParticleSystemRenderer || r is LineRenderer || IsOurs(r.transform))
                {
                    continue;
                }

                r.enabled = false;
                m_hidden.Add(r);
            }
        }

        private bool IsOurs(Transform t)
        {
            for (var x = t; x != null && x != transform; x = x.parent)
            {
                if (x.name.StartsWith("WZ_"))
                {
                    return true;
                }
            }

            return false;
        }

        public void End()
        {
            foreach (var r in m_hidden)
            {
                if (r != null)
                {
                    r.enabled = true;
                }
            }

            m_hidden.Clear();
            if (m_model != null)
            {
                Destroy(m_model);
                Fx.Smoke(transform.position, new Color(0.85f, 0.85f, 1f, 0.7f));
                Fx.Sound(transform.position, "pop", 0.6f);
            }

            Destroy(this);
        }

        private void Update()
        {
            if (Time.time > m_until || m_char == null || m_char.IsDead())
            {
                End();
                return;
            }

            m_scan -= Time.deltaTime;
            if (m_scan <= 0f)
            {
                m_scan = 0.5f;
                Hide(); // equipment and effects the game turns on again
            }
        }

        private void OnDestroy()
        {
            foreach (var r in m_hidden)
            {
                if (r != null)
                {
                    r.enabled = true;
                }
            }
        }
    }
}
