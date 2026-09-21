using System;
using UnityEngine;

namespace ChestSorter
{
    /// <summary>
    ///     Sits on every chest and shows its label floating above it while the player is close.
    ///     Local decoration only: the text comes from the chest's own network data and nothing
    ///     here writes anything back.
    /// </summary>
    public class ChestLabel : MonoBehaviour
    {
        private const float CheckInterval = 0.3f;

        private Container _container;
        private ZNetView _nview;
        private GameObject _label;
        private float _nextCheck;
        private float _height;
        private bool _visible;
        private string _shown;

        private void Awake()
        {
            _container = GetComponent<Container>();
            _nview = GetComponent<ZNetView>();
            _nextCheck = UnityEngine.Random.Range(0f, CheckInterval);
        }

        private void OnDestroy()
        {
            if (_label != null)
            {
                Destroy(_label);
                _label = null;
            }
        }

        private void LateUpdate()
        {
            if (_container == null || _nview == null || !_nview.IsValid())
            {
                return;
            }

            if (Time.time >= _nextCheck)
            {
                _nextCheck = Time.time + CheckInterval;
                Refresh();
            }

            if (_visible && _label != null)
            {
                FaceCamera();
            }
        }

        private void Refresh()
        {
            bool wanted = ModConfig.WriteLabels.Value && ModConfig.ShowFloatingLabels.Value &&
                          LabelVisual.Available;

            string text = wanted ? LabelStore.Get(_container) : string.Empty;

            if (string.IsNullOrEmpty(text) || !CloseEnough())
            {
                Show(false);
                return;
            }

            if (_label == null)
            {
                _label = LabelVisual.Create(transform, new Vector3(0f, Height(), 0f));
                if (_label == null)
                {
                    return;
                }

                _shown = null;
            }

            if (_shown != text)
            {
                _shown = text;
                LabelVisual.SetText(_label, text);
            }

            _label.transform.localPosition = new Vector3(0f, Height(), 0f);
            Show(true);
        }

        private bool CloseEnough()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return false;
            }

            float distance = ModConfig.LabelDistance.Value;
            return (player.transform.position - transform.position).sqrMagnitude <= distance * distance;
        }

        /// <summary>Just above the chest, worked out once from what the chest actually looks like.</summary>
        private float Height()
        {
            if (_height <= 0f)
            {
                _height = 0.6f;

                try
                {
                    Renderer[] renderers = GetComponentsInChildren<Renderer>(false);
                    float top = float.MinValue;
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        if (renderers[i] != null && renderers[i].bounds.max.y > top)
                        {
                            top = renderers[i].bounds.max.y;
                        }
                    }

                    if (top > float.MinValue)
                    {
                        _height = Mathf.Clamp(top - transform.position.y, 0.2f, 3f);
                    }
                }
                catch (Exception)
                {
                    // the default height is fine
                }
            }

            return _height + ModConfig.LabelHeight.Value;
        }

        private void Show(bool visible)
        {
            if (_visible == visible)
            {
                return;
            }

            _visible = visible;
            if (_label != null)
            {
                _label.SetActive(visible);
            }
        }

        private void FaceCamera()
        {
            Camera camera = Util.MainCamera;
            if (camera == null)
            {
                return;
            }

            Vector3 direction = _label.transform.position - camera.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            _label.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }
    }
}
