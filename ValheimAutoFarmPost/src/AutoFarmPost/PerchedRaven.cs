using System;
using System.Reflection;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;

namespace AutoFarmPost
{
    /// <summary>
    ///     Makes the post feel alive: a real raven lands on it now and then, and the carved eyes
    ///     glow at night. Purely local decoration - it runs on every client regardless of who
    ///     owns the piece, and nothing here touches the world state.
    /// </summary>
    public class PerchedRaven : MonoBehaviour
    {
        private const string BirdName = "AutoFarmPostRaven";

        private static GameObject _template;
        private static bool _templateChecked;

        private GameObject _bird;
        private Light _glow;
        private float _nextChange;
        private bool _perched;

        private void Start()
        {
            ZNetView nview = GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
            {
                // build preview - no bird, no light
                enabled = false;
                return;
            }

            _nextChange = Time.time + UnityEngine.Random.Range(15f, 90f);
            CreateGlow();
        }

        private void OnDestroy()
        {
            if (_bird != null)
            {
                Destroy(_bird);
                _bird = null;
            }
        }

        private void Update()
        {
            if (!ModConfig.LivingRaven.Value)
            {
                if (_perched)
                {
                    SetPerched(false);
                }

                if (_glow != null)
                {
                    _glow.enabled = false;
                }

                return;
            }

            if (_glow != null)
            {
                _glow.enabled = IsNight();
            }

            if (Time.time < _nextChange)
            {
                return;
            }

            if (_perched)
            {
                SetPerched(false);
                _nextChange = Time.time + UnityEngine.Random.Range(60f, 240f);
            }
            else
            {
                SetPerched(true);
                _nextChange = Time.time + UnityEngine.Random.Range(25f, 70f);
            }
        }

        private void CreateGlow()
        {
            try
            {
                GameObject go = new GameObject("AutoFarmPostGlow");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, 1.82f, 0.10f);

                _glow = go.AddComponent<Light>();
                _glow.type = LightType.Point;
                _glow.color = new Color(1f, 0.72f, 0.28f, 1f);
                _glow.range = 2.4f;
                _glow.intensity = 0.8f;
                _glow.shadows = LightShadows.None;
                _glow.enabled = false;
            }
            catch (Exception e)
            {
                AutoFarmPlugin.Log.LogWarning("Raven eye glow skipped: " + e.Message);
                _glow = null;
            }
        }

        private void SetPerched(bool perched)
        {
            _perched = perched;

            if (!perched)
            {
                if (_bird != null)
                {
                    _bird.SetActive(false);
                }

                return;
            }

            if (_bird == null)
            {
                GameObject template = GetTemplate();
                if (template == null)
                {
                    _perched = false;
                    return;
                }

                try
                {
                    _bird = Instantiate(template, transform);
                    _bird.name = BirdName;
                    _bird.transform.localPosition = new Vector3(0f, 1.93f, -0.05f);
                    _bird.transform.localRotation = Quaternion.Euler(0f, UnityEngine.Random.Range(-35f, 35f), 0f);
                    _bird.transform.localScale = Vector3.one * 0.55f;
                }
                catch (Exception e)
                {
                    AutoFarmPlugin.Log.LogWarning("Perched raven skipped: " + e.Message);
                    _bird = null;
                    _perched = false;
                    return;
                }
            }

            _bird.SetActive(true);
        }

        /// <summary>
        ///     A visual-only copy of the game's raven: instantiated with networking disabled and
        ///     stripped of everything that is not a renderer, so it just sits there and idles.
        /// </summary>
        private static GameObject GetTemplate()
        {
            if (_templateChecked)
            {
                return _template;
            }

            _templateChecked = true;

            try
            {
                GameObject source = PrefabManager.Instance.GetPrefab("Raven");
                if (source == null)
                {
                    AutoFarmPlugin.Log.LogInfo("No raven prefab found - the post keeps only its glowing eyes.");
                    return null;
                }

                FieldInfo disableInit = AccessTools.Field(typeof(ZNetView), "m_forceDisableInit");
                bool previous = false;
                if (disableInit != null)
                {
                    previous = (bool)disableInit.GetValue(null);
                    disableInit.SetValue(null, true);
                }

                GameObject copy;
                try
                {
                    copy = Instantiate(source);
                }
                finally
                {
                    if (disableInit != null)
                    {
                        disableInit.SetValue(null, previous);
                    }
                }

                Component[] components = copy.GetComponentsInChildren<Component>(true);
                for (int i = 0; i < components.Length; i++)
                {
                    Component component = components[i];
                    if (component == null || component is Transform || component is Renderer ||
                        component is MeshFilter || component is Animator)
                    {
                        continue;
                    }

                    Destroy(component);
                }

                copy.name = "AutoFarmPostRavenTemplate";
                copy.SetActive(false);
                DontDestroyOnLoad(copy);
                _template = copy;
                return _template;
            }
            catch (Exception e)
            {
                AutoFarmPlugin.Log.LogWarning("Could not prepare the raven visual: " + e.Message);
                return null;
            }
        }

        private static bool IsNight()
        {
            try
            {
                // static in current versions of the game
                return EnvMan.instance != null && EnvMan.IsNight();
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
