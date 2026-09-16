using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace AutoFarmPost
{
    /// <summary>
    ///     Draws a line between the seed rows and the harvest rows of an open farm post.
    ///     Everything here is reached by reflection and is purely cosmetic: if the game ever
    ///     renames these fields the line simply disappears.
    /// </summary>
    internal static class FarmPostUi
    {
        private const string DividerName = "AutoFarmPostDivider";

        private static FieldInfo _currentContainerField;
        private static FieldInfo _containerGridField;
        private static FieldInfo _elementSpaceField;
        private static FieldInfo _gridRootField;

        private static bool _fieldsChecked;
        private static bool _unavailable;
        private static GameObject _divider;

        public static void Update()
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
                AutoFarmPlugin.Log.LogWarning("Container divider disabled: " + e.Message);
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
            bool wanted = ModConfig.ShowDivider.Value &&
                          container != null &&
                          container.GetComponent<FarmPost>() != null;

            if (!wanted)
            {
                if (_divider != null)
                {
                    _divider.SetActive(false);
                }

                return;
            }

            if (Build(gui, container) && _divider != null)
            {
                _divider.SetActive(true);
            }
        }

        private static bool Build(InventoryGui gui, Container container)
        {
            object grid = _containerGridField.GetValue(gui);
            if (grid == null)
            {
                return false;
            }

            if (_elementSpaceField == null)
            {
                _elementSpaceField = AccessTools.Field(grid.GetType(), "m_elementSpace");
                _gridRootField = AccessTools.Field(grid.GetType(), "m_gridRoot");

                if (_elementSpaceField == null || _gridRootField == null)
                {
                    _unavailable = true;
                    return false;
                }
            }

            RectTransform root = _gridRootField.GetValue(grid) as RectTransform;
            if (root == null)
            {
                return false;
            }

            float space = (float)_elementSpaceField.GetValue(grid);
            if (space <= 1f)
            {
                return false;
            }

            // Copy the anchoring of a real slot so the line lands exactly on the grid.
            RectTransform sample = null;
            for (int i = 0; i < root.childCount; i++)
            {
                RectTransform child = root.GetChild(i) as RectTransform;
                if (child != null && child.name != DividerName)
                {
                    sample = child;
                    break;
                }
            }

            if (sample == null)
            {
                return false;
            }

            if (_divider == null)
            {
                _divider = new GameObject(DividerName, typeof(RectTransform), typeof(Image));
                _divider.transform.SetParent(root, false);

                Image image = _divider.GetComponent<Image>();
                image.color = new Color(0.92f, 0.78f, 0.38f, 0.7f);
                image.raycastTarget = false;
            }

            int height = Mathf.Max(2, container.m_height);
            int seedRows = Mathf.Clamp(ModConfig.SeedRows.Value, 1, height - 1);
            int width = Mathf.Max(1, container.m_width);

            RectTransform rect = (RectTransform)_divider.transform;
            rect.anchorMin = sample.anchorMin;
            rect.anchorMax = sample.anchorMax;
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(space * width, 3f);
            rect.anchoredPosition = new Vector2(
                sample.anchoredPosition.x - space * 0.5f,
                sample.anchoredPosition.y - space * (seedRows - 0.5f));
            rect.SetAsLastSibling();

            return true;
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
                AutoFarmPlugin.Log.LogWarning("Container divider disabled: inventory fields not found.");
            }
        }
    }
}
