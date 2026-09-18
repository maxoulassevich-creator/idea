using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AutoFarmPost
{
    /// <summary>
    ///     Splits the container window of a farm post into two separate blocks: the seed rows on
    ///     top, the harvest rows below, with a real gap and a caption between them.
    ///
    ///     This moves the slot widgets the game already created instead of building a second
    ///     window, so vanilla drag and drop keeps working exactly as before. Everything is looked
    ///     up by name through reflection: if a future patch renames something, the window simply
    ///     keeps its default layout instead of breaking.
    /// </summary>
    internal static class FarmPostUi
    {
        private const string LabelName = "AutoFarmPostSplitLabel";

        /// <summary>Gap between the two blocks, as a fraction of one slot. Must stay below 0.5.</summary>
        private const float GapFactor = 0.26f;

        private static readonly List<RectTransform> Slots = new List<RectTransform>();
        private static readonly List<float> Columns = new List<float>();

        private static FieldInfo _currentContainerField;
        private static FieldInfo _containerGridField;
        private static FieldInfo _gridRootField;
        private static FieldInfo _elementSpaceField;
        private static FieldInfo _containerNameField;
        private static PropertyInfo _textProperty;
        private static PropertyInfo _fontSizeProperty;

        private static bool _fieldsChecked;
        private static bool _unavailable;
        private static bool _labelUnavailable;
        private static GameObject _label;

        /// <summary>Called from LateUpdate so the game has already laid the grid out this frame.</summary>
        public static void LateUpdate()
        {
            if (_unavailable)
            {
                return;
            }

            InventoryGui gui = InventoryGui.instance;
            if (gui == null)
            {
                return;
            }

            try
            {
                Apply(gui);
            }
            catch (Exception e)
            {
                _unavailable = true;
                AutoFarmPlugin.Log.LogWarning("Split container view disabled: " + e.Message);
            }
        }

        private static void Apply(InventoryGui gui)
        {
            EnsureFields();
            if (_unavailable)
            {
                return;
            }

            Container container = _currentContainerField.GetValue(gui) as Container;
            bool wanted = ModConfig.SplitView.Value &&
                          container != null &&
                          container.GetComponent<FarmPost>() != null;

            if (!wanted)
            {
                if (_label != null)
                {
                    _label.SetActive(false);
                }

                return;
            }

            Layout(gui, container);
        }

        private static void Layout(InventoryGui gui, Container container)
        {
            object grid = _containerGridField.GetValue(gui);
            if (grid == null)
            {
                return;
            }

            if (_gridRootField == null)
            {
                _gridRootField = AccessTools.Field(grid.GetType(), "m_gridRoot");
                _elementSpaceField = AccessTools.Field(grid.GetType(), "m_elementSpace");

                if (_gridRootField == null)
                {
                    _unavailable = true;
                    return;
                }
            }

            RectTransform root = _gridRootField.GetValue(grid) as RectTransform;
            if (root == null)
            {
                return;
            }

            // Collect the slot widgets and the bounds of the grid.
            Slots.Clear();
            Columns.Clear();

            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minY = float.MaxValue;

            for (int i = 0; i < root.childCount; i++)
            {
                RectTransform child = root.GetChild(i) as RectTransform;
                if (child == null || child.name == LabelName || !child.gameObject.activeSelf)
                {
                    continue;
                }

                Slots.Add(child);

                Vector2 pos = child.anchoredPosition;
                if (pos.x < minX) { minX = pos.x; }
                if (pos.x > maxX) { maxX = pos.x; }
                if (pos.y < minY) { minY = pos.y; }

                bool known = false;
                for (int c = 0; c < Columns.Count; c++)
                {
                    if (Mathf.Abs(Columns[c] - pos.x) < 1f)
                    {
                        known = true;
                        break;
                    }
                }

                if (!known)
                {
                    Columns.Add(pos.x);
                }
            }

            if (Slots.Count == 0)
            {
                return;
            }

            // The distance between slots is measured from the widgets themselves - that is exact,
            // whatever value the grid keeps in its own field.
            float pitch = MeasurePitch();
            if (pitch <= 1f)
            {
                return;
            }

            float gap = pitch * GapFactor;

            // Rows are counted from the bottom row up, and the bottom row never moves: the
            // container window has no spare space below it, so pushing the harvest block down
            // would clip its last row. The seed block is lifted instead.
            int maxK = 0;
            for (int i = 0; i < Slots.Count; i++)
            {
                int k = Mathf.RoundToInt((Slots[i].anchoredPosition.y - minY) / pitch);
                if (k > maxK)
                {
                    maxK = k;
                }
            }

            int seedRows = Mathf.Clamp(ModConfig.SeedRows.Value, 1, Mathf.Max(1, maxK));

            for (int i = 0; i < Slots.Count; i++)
            {
                RectTransform slot = Slots[i];
                Vector2 pos = slot.anchoredPosition;

                int x = Mathf.RoundToInt((pos.x - minX) / pitch);
                int k = Mathf.RoundToInt((pos.y - minY) / pitch);
                int gridY = maxK - k;                        // 0 is the top row

                float targetX = minX + x * pitch;
                float targetY = minY + k * pitch + (gridY < seedRows ? gap : 0f);

                if (Mathf.Abs(pos.x - targetX) > 0.5f || Mathf.Abs(pos.y - targetY) > 0.5f)
                {
                    slot.anchoredPosition = new Vector2(targetX, targetY);
                }
            }

            UpdateLabel(gui, root, minX, maxX, minY, pitch, maxK, seedRows, gap);
        }

        /// <summary>Smallest distance between two slot columns.</summary>
        private static float MeasurePitch()
        {
            Columns.Sort();

            float pitch = 0f;
            for (int i = 1; i < Columns.Count; i++)
            {
                float step = Columns[i] - Columns[i - 1];
                if (step > 1f && (pitch <= 0f || step < pitch))
                {
                    pitch = step;
                }
            }

            if (pitch <= 1f && _elementSpaceField != null)
            {
                try
                {
                    object grid = _containerGridField.GetValue(InventoryGui.instance);
                    if (grid != null)
                    {
                        pitch = (float)_elementSpaceField.GetValue(grid);
                    }
                }
                catch (Exception)
                {
                    pitch = 0f;
                }
            }

            return pitch;
        }

        private static void UpdateLabel(InventoryGui gui, RectTransform root, float minX, float maxX,
            float minY, float pitch, int maxK, int seedRows, float gap)
        {
            if (_labelUnavailable)
            {
                return;
            }

            try
            {
                if (_label == null && !CreateLabel(gui, root))
                {
                    _labelUnavailable = true;
                    return;
                }

                RectTransform rect = (RectTransform)_label.transform;
                RectTransform sample = Slots[0];

                rect.anchorMin = sample.anchorMin;
                rect.anchorMax = sample.anchorMax;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(maxX - minX + pitch, gap);
                rect.anchoredPosition = new Vector2(
                    (minX + maxX) * 0.5f,
                    minY + (maxK - seedRows) * pitch + pitch * 0.5f + gap * 0.5f);

                // Compare against what the widget actually shows, so anything that overwrites the
                // caption gets corrected on the next frame.
                if (_textProperty != null)
                {
                    Component text = GetTextComponent(_label);
                    if (text != null)
                    {
                        string wanted = Util.Localize("$autofarm_ui_split");
                        string current = _textProperty.GetValue(text, null) as string;
                        if (current != wanted)
                        {
                            _textProperty.SetValue(text, wanted, null);
                        }
                    }
                }

                _label.SetActive(true);
            }
            catch (Exception e)
            {
                _labelUnavailable = true;
                AutoFarmPlugin.Log.LogWarning("Split caption disabled: " + e.Message);
            }
        }

        /// <summary>Clones the window title so the caption inherits the game's font and style.</summary>
        private static bool CreateLabel(InventoryGui gui, RectTransform root)
        {
            if (_containerNameField == null)
            {
                _containerNameField = AccessTools.Field(typeof(InventoryGui), "m_containerName");
            }

            Component title = _containerNameField != null ? _containerNameField.GetValue(gui) as Component : null;
            if (title == null)
            {
                return false;
            }

            GameObject clone = UnityEngine.Object.Instantiate(title.gameObject, root, false);
            clone.name = LabelName;

            // Drop everything that could overwrite our text (localisation helpers and the like).
            Component[] components = clone.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || component is Transform)
                {
                    continue;
                }

                string typeName = component.GetType().Name;
                if (typeName == "TextMeshProUGUI" || typeName == "CanvasRenderer")
                {
                    continue;
                }

                UnityEngine.Object.Destroy(component);
            }

            Component text = GetTextComponent(clone);
            if (text == null)
            {
                UnityEngine.Object.Destroy(clone);
                return false;
            }

            _textProperty = text.GetType().GetProperty("text");
            _fontSizeProperty = text.GetType().GetProperty("fontSize");

            if (_fontSizeProperty != null)
            {
                try
                {
                    _fontSizeProperty.SetValue(text, 13f, null);
                }
                catch (Exception)
                {
                    // keep the inherited size
                }
            }

            clone.transform.SetAsLastSibling();
            _label = clone;
            return true;
        }

        private static Component GetTextComponent(GameObject go)
        {
            Component[] components = go.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null && components[i].GetType().Name == "TextMeshProUGUI")
                {
                    return components[i];
                }
            }

            return null;
        }

        private static void EnsureFields()
        {
            if (_fieldsChecked)
            {
                return;
            }

            _fieldsChecked = true;
            _currentContainerField = AccessTools.Field(typeof(InventoryGui), "m_currentContainer");
            _containerGridField = AccessTools.Field(typeof(InventoryGui), "m_containerGrid");

            if (_currentContainerField == null || _containerGridField == null)
            {
                _unavailable = true;
                AutoFarmPlugin.Log.LogWarning("Split container view disabled: inventory fields not found.");
            }
        }
    }
}
