using System;
using System.Reflection;
using Jotunn.Managers;
using UnityEngine;

namespace AutoFarmPost
{
    /// <summary>
    ///     Asks Jotunn to render an icon of a finished prefab. Everything goes through
    ///     reflection, so a renamed API costs us the icon and never the build.
    /// </summary>
    internal static class IconRenderer
    {
        private static bool _unavailable;

        public static Sprite Render(GameObject prefab)
        {
            if (_unavailable || prefab == null)
            {
                return null;
            }

            try
            {
                Type managerType = typeof(RenderManager);
                object manager = managerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null, null);
                Type requestType = managerType.GetNestedType("RenderRequest");

                if (manager == null || requestType == null)
                {
                    _unavailable = true;
                    return null;
                }

                object request = Activator.CreateInstance(requestType, new object[] { prefab });

                object rotation = managerType.GetField("IsometricRotation", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null);
                if (rotation != null)
                {
                    PropertyInfo rotationProperty = requestType.GetProperty("Rotation");
                    if (rotationProperty != null)
                    {
                        rotationProperty.SetValue(request, rotation, null);
                    }
                }

                MethodInfo render = managerType.GetMethod("Render", new[] { requestType });
                return render != null ? render.Invoke(manager, new[] { request }) as Sprite : null;
            }
            catch (Exception e)
            {
                AutoFarmPlugin.Log.LogDebug("Icon rendering skipped: " + e.Message);
                return null;
            }
        }
    }
}
