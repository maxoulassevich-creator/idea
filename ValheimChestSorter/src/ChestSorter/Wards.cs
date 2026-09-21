using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ChestSorter
{
    /// <summary>
    ///     Ward check. The game's method has changed shape over the years, so it is called by
    ///     name with whatever arguments it happens to take; if it cannot be found the mod simply
    ///     assumes access is allowed - the game itself would still refuse to open a warded chest.
    /// </summary>
    internal static class Wards
    {
        private static MethodInfo _checkAccess;
        private static ParameterInfo[] _parameters;
        private static bool _checked;

        public static bool HasAccess(Vector3 position)
        {
            if (!_checked)
            {
                _checked = true;
                Type type = AccessTools.TypeByName("PrivateArea");
                if (type != null)
                {
                    foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (method.Name != "CheckAccess" || method.ReturnType != typeof(bool))
                        {
                            continue;
                        }

                        ParameterInfo[] parameters = method.GetParameters();
                        if (parameters.Length == 0 || parameters[0].ParameterType != typeof(Vector3))
                        {
                            continue;
                        }

                        _checkAccess = method;
                        _parameters = parameters;
                        break;
                    }
                }
            }

            if (_checkAccess == null)
            {
                return true;
            }

            try
            {
                object[] args = new object[_parameters.Length];
                args[0] = position;
                for (int i = 1; i < _parameters.Length; i++)
                {
                    // radius 0, and never flash the ward or play effects
                    args[i] = _parameters[i].ParameterType == typeof(bool)
                        ? (object)false
                        : _parameters[i].ParameterType == typeof(float) ? (object)0f : GetDefault(_parameters[i]);
                }

                return (bool)_checkAccess.Invoke(null, args);
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static object GetDefault(ParameterInfo parameter)
        {
            if (parameter.HasDefaultValue)
            {
                return parameter.DefaultValue;
            }

            Type type = parameter.ParameterType;
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }
}
