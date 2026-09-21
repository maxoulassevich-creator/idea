using System;
using System.Reflection;
using UnityEngine;

namespace ChestSorter
{
    /// <summary>
    ///     The floating label over a chest. It is a copy of the text object the game uses on
    ///     signs, so it already has the right font, material and shader; we only change the text
    ///     and the size. Every step is guarded - if the game ever changes the sign, the mod falls
    ///     back to showing the label only when you look at the chest.
    /// </summary>
    internal static class LabelVisual
    {
        private static GameObject _template;
        private static bool _checked;
        private static bool _broken;

        private static PropertyInfo _textProperty;
        private static PropertyInfo _fontSizeProperty;
        private static float _templateFontSize = 4f;

        public static bool Available
        {
            get { return !_broken; }
        }

        public static GameObject Create(Transform parent, Vector3 localPosition)
        {
            if (_broken)
            {
                return null;
            }

            try
            {
                GameObject template = GetTemplate();
                if (template == null)
                {
                    _broken = true;
                    return null;
                }

                GameObject copy = UnityEngine.Object.Instantiate(template, parent);
                copy.name = "ChestSorterLabel";
                copy.transform.localPosition = localPosition;
                copy.transform.localRotation = Quaternion.identity;
                copy.SetActive(true);

                Component text = FindText(copy);
                if (text != null && _fontSizeProperty != null)
                {
                    try
                    {
                        _fontSizeProperty.SetValue(text, _templateFontSize * ModConfig.LabelSize.Value, null);
                    }
                    catch (Exception)
                    {
                        // keep the sign's own size
                    }
                }

                return copy;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Floating labels disabled: " + e.Message);
                _broken = true;
                return null;
            }
        }

        public static void SetText(GameObject label, string text)
        {
            if (label == null || _textProperty == null)
            {
                return;
            }

            try
            {
                Component component = FindText(label);
                if (component == null)
                {
                    return;
                }

                string current = _textProperty.GetValue(component, null) as string;
                if (current != text)
                {
                    _textProperty.SetValue(component, text, null);
                }
            }
            catch (Exception)
            {
                // a label that will not update is not worth breaking anything over
            }
        }

        private static Component FindText(GameObject go)
        {
            Component[] components = go.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null && IsTextComponent(components[i].GetType()))
                {
                    return components[i];
                }
            }

            return null;
        }

        private static bool IsTextComponent(Type type)
        {
            // the 3D variant, not the UI one
            return type.Name == "TextMeshPro";
        }

        private static GameObject GetTemplate()
        {
            if (_checked)
            {
                return _template;
            }

            _checked = true;

            if (ZNetScene.instance == null)
            {
                _checked = false;
                return null;
            }

            string[] candidates = { "sign", "sign_notext", "itemstand" };

            for (int i = 0; i < candidates.Length; i++)
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(candidates[i]);
                if (prefab == null)
                {
                    continue;
                }

                Component text = FindText(prefab);
                if (text == null)
                {
                    continue;
                }

                _textProperty = text.GetType().GetProperty("text");
                _fontSizeProperty = text.GetType().GetProperty("fontSize");

                if (_fontSizeProperty != null)
                {
                    try
                    {
                        _templateFontSize = (float)_fontSizeProperty.GetValue(text, null);
                    }
                    catch (Exception)
                    {
                        _templateFontSize = 4f;
                    }
                }

                GameObject copy = UnityEngine.Object.Instantiate(text.gameObject);
                copy.name = "ChestSorterLabelTemplate";

                // keep only what draws the text
                Component[] components = copy.GetComponentsInChildren<Component>(true);
                for (int c = 0; c < components.Length; c++)
                {
                    Component component = components[c];
                    if (component == null || component is Transform || component is Renderer ||
                        IsTextComponent(component.GetType()))
                    {
                        continue;
                    }

                    UnityEngine.Object.Destroy(component);
                }

                copy.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(copy);
                _template = copy;
                Plugin.Log.LogInfo("Floating labels use the text of '" + candidates[i] + "'.");
                return _template;
            }

            Plugin.Log.LogInfo("No sign text found - chest labels will only show on hover.");
            return null;
        }
    }
}
