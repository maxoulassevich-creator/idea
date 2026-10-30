using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

[assembly: System.Runtime.CompilerServices.IgnoresAccessChecksTo("assembly_valheim")]
[assembly: System.Runtime.CompilerServices.IgnoresAccessChecksTo("assembly_utils")]

namespace System.Runtime.CompilerServices
{
    /// <summary>Asks the runtime to skip visibility checks into the named assembly where it supports that.</summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    internal sealed class IgnoresAccessChecksToAttribute : Attribute
    {
        public IgnoresAccessChecksToAttribute(string assemblyName)
        {
            AssemblyName = assemblyName;
        }

        public string AssemblyName { get; }
    }
}

namespace Wizardry
{
    /// <summary>
    /// Valheim 1.0.21 runs on Unity 6, whose Mono enforces member visibility: the publicized references let the mod
    /// compile against private and protected game members, but touching one directly throws FieldAccessException or
    /// MethodAccessException at run time. Every non-public game member goes through here instead, by a public
    /// equivalent where the game has one and by reflection otherwise. Harmony patches take private fields as
    /// <c>___name</c> parameters.
    /// </summary>
    internal static class Access
    {
        private static readonly Dictionary<string, FieldInfo> s_fields = new Dictionary<string, FieldInfo>();
        private static readonly Dictionary<string, MethodInfo> s_methods = new Dictionary<string, MethodInfo>();

        // ---- components every networked game object carries

        public static ZNetView Nv(this Component c)
        {
            return c != null ? c.GetComponent<ZNetView>() : null;
        }

        public static Rigidbody Body(this Component c)
        {
            return c != null ? c.GetComponent<Rigidbody>() : null;
        }

        // ---- typed wrappers

        public static Character Owner(this global::Projectile p)
        {
            return Get<Character>(p, "m_owner");
        }

        public static void SetMaxAirAltitude(this Character c, float y)
        {
            Set(c, "m_maxAirAltitude", y);
        }

        public static void ClearGroundTouch(this Character c)
        {
            Set(c, "m_lastGroundTouch", 0f);
        }

        public static ItemDrop.ItemData RightItem(this Humanoid h)
        {
            return Call(h, "GetRightItem", Type.EmptyTypes) as ItemDrop.ItemData;
        }

        public static ItemDrop.ItemData LeftItem(this Humanoid h)
        {
            return Call(h, "GetLeftItem", Type.EmptyTypes) as ItemDrop.ItemData;
        }

        /// <summary>New hair and beard; null keeps the current one.</summary>
        public static void SetLooks(this Humanoid h, string hair, string beard)
        {
            if (hair != null) Set(h, "m_hairItem", hair);
            if (beard != null) Set(h, "m_beardItem", beard);
            Call(h, "SetupEquipment", Type.EmptyTypes);
        }

        public static void SaveNow(this Container c)
        {
            Call(c, "Save", Type.EmptyTypes);
        }

        public static void SetHeight(this Inventory inv, int height)
        {
            Set(inv, "m_height", height);
        }

        public static Container OpenContainer(this InventoryGui gui)
        {
            return Get<Container>(gui, "m_currentContainer");
        }

        public static void TameNow(this Tameable t)
        {
            Call(t, "Tame", Type.EmptyTypes);
        }

        public static void DecreaseTameTime(this Tameable t, float seconds)
        {
            Call(t, "DecreaseRemainingTime", new[] { typeof(float) }, seconds);
        }

        public static void SetTargetCreature(this MonsterAI ai, Character target)
        {
            Call(ai, "SetTarget", new[] { typeof(Character) }, target);
        }

        public static void SetAlertedState(this BaseAI ai, bool alerted)
        {
            Call(ai, "SetAlerted", new[] { typeof(bool) }, alerted);
        }

        public static void FleeFrom(this BaseAI ai, float dt, Vector3 from)
        {
            Call(ai, "Flee", new[] { typeof(float), typeof(Vector3) }, dt, from);
        }

        public static void Wander(this BaseAI ai, float dt, Vector3 center)
        {
            Call(ai, "RandomMovement", new[] { typeof(float), typeof(Vector3), typeof(bool) }, dt, center, false);
        }

        public static void ExploreAt(this Minimap map, Vector3 pos, float radius)
        {
            Call(map, "Explore", new[] { typeof(Vector3), typeof(float) }, pos, radius);
        }

        public static string ForcedEnvironment(this EnvMan env)
        {
            return Get<string>(env, "m_forceEnv");
        }

        /// <summary>Seconds since the status effect started.</summary>
        public static float Elapsed(this StatusEffect se)
        {
            return Get<float>(se, "m_time");
        }

        // ---- reflection

        public static T Get<T>(object o, string field)
        {
            var f = o != null ? Field(o.GetType(), field) : null;
            if (f == null)
            {
                return default;
            }

            var v = f.GetValue(o);
            return v is T t ? t : default;
        }

        public static void Set(object o, string field, object value)
        {
            var f = o != null ? Field(o.GetType(), field) : null;
            f?.SetValue(o, value);
        }

        public static object Call(object o, string method, Type[] signature, params object[] args)
        {
            var m = o != null ? Method(o.GetType(), method, signature) : null;
            if (m == null)
            {
                return null;
            }

            try
            {
                return m.Invoke(o, args);
            }
            catch (TargetInvocationException e)
            {
                throw e.InnerException ?? e;
            }
        }

        private static FieldInfo Field(Type type, string name)
        {
            string key = type.FullName + ":" + name;
            if (!s_fields.TryGetValue(key, out var f))
            {
                f = AccessTools.Field(type, name);
                if (f == null)
                {
                    Plugin.Log.LogWarning("Field " + type.Name + "." + name + " not found; the feature that needs it is off.");
                }

                s_fields[key] = f;
            }

            return f;
        }

        private static MethodInfo Method(Type type, string name, Type[] signature)
        {
            string key = type.FullName + ":" + name + "(" + string.Join(",", Array.ConvertAll(signature, t => t.Name)) + ")";
            if (!s_methods.TryGetValue(key, out var m))
            {
                m = AccessTools.Method(type, name, signature);
                if (m == null)
                {
                    Plugin.Log.LogWarning("Method " + type.Name + "." + name + " not found; the feature that needs it is off.");
                }

                s_methods[key] = m;
            }

            return m;
        }
    }
}
