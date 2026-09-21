// Compile-check stubs only. NOT shipped - they merely mirror the API the mod expects.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object { public string name; public static void DontDestroyOnLoad(Object o) { } public static T Instantiate<T>(T o) where T : Object { return o; } public static T Instantiate<T>(T o, Transform parent) where T : Object { return o; } public static T Instantiate<T>(T o, Transform parent, bool worldPositionStays) where T : Object { return o; } public static T Instantiate<T>(T o, Vector3 p, Quaternion r) where T : Object { return o; } public static void Destroy(Object o) { } public static T[] FindObjectsOfType<T>() where T : Object { return new T[0]; } }
    public struct Vector2 { public float x, y; public Vector2(float a, float b) { x = a; y = b; } public float magnitude { get { return 0; } } public float sqrMagnitude { get { return 0; } } }
    public struct Vector4 { public float x, y, z, w; }
    public struct Vector3 {
        public float x, y, z;
        public Vector3(float a, float b, float c) { x = a; y = b; z = c; }
        public float sqrMagnitude { get { return 0; } }
        public float magnitude { get { return 0; } }
        public Vector3 normalized { get { return this; } }
        public static Vector3 up, down, forward, zero, one;
        public static Vector3 operator *(Vector3 a, Vector3 b) { return a; }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return a; }
        public static Vector3 operator -(Vector3 a) { return a; }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return a; }
        public static Vector3 operator *(Vector3 a, float s) { return a; }
        public static Vector3 operator *(float s, Vector3 a) { return a; }
        public static Vector3 operator /(Vector3 a, float s) { return a; }
        public static Vector3 Cross(Vector3 a, Vector3 b) { return a; }
        public static float Dot(Vector3 a, Vector3 b) { return 0; }
    }
    public struct Quaternion { public static Quaternion identity; public static Quaternion LookRotation(Vector3 f, Vector3 u) { return identity; } public static Quaternion Euler(float a, float b, float c) { return default(Quaternion); } }
    public struct Color { public Color(float r, float g, float b, float a) { } }
    public enum KeyCode { None, F8, LeftShift, LeftControl, RightShift }
    public class Camera : Behaviour { public static Camera main; }
    public struct Bounds { public Vector3 max, min, center, size; }
    public static class Mathf {
        public static float Atan2(float y, float x) { return (float)System.Math.Atan2(y, x); }
        public const float Rad2Deg = 57.29578f;
        public const float PI = 3.14159265f;
        public static int Max(int a, int b) { return a > b ? a : b; }
        public static float Max(float a, float b) { return a > b ? a : b; }
        public static int Min(int a, int b) { return a < b ? a : b; }
        public static float Min(float a, float b) { return a < b ? a : b; }
        public static int Clamp(int v, int a, int b) { return v < a ? a : (v > b ? b : v); }
        public static float Clamp(float v, float a, float b) { return v < a ? a : (v > b ? b : v); }
        public static float Clamp01(float f) { return Clamp(f, 0f, 1f); }
        public static int CeilToInt(float f) { return (int)System.Math.Ceiling(f); }
        public static int RoundToInt(float f) { return (int)System.Math.Round(f); }
        public static int FloorToInt(float f) { return (int)System.Math.Floor(f); }
        public static float Abs(float f) { return System.Math.Abs(f); }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
        public static float Sin(float f) { return (float)System.Math.Sin(f); }
        public static float Cos(float f) { return (float)System.Math.Cos(f); }
        public static float Pow(float a, float b) { return (float)System.Math.Pow(a, b); }
    }
    public static class Time { public static float time; public static float deltaTime; }
    public static class Random { public static float Range(float a, float b) { return a; } }
    public enum QueryTriggerInteraction { UseGlobal, Ignore, Collide }
    public struct RaycastHit { public Vector3 point; public Vector3 normal; public Collider collider; }
    public static class Physics {
        public static int OverlapSphereNonAlloc(Vector3 p, float r, Collider[] res, int mask, QueryTriggerInteraction q) { return 0; }
        public static bool Raycast(Vector3 o, Vector3 d, out RaycastHit hit, float max, int mask, QueryTriggerInteraction q) { hit = default(RaycastHit); return false; }
    }
    public static class LayerMask { public static int GetMask(params string[] names) { return 0; } }
    public class Component : Object {
        public Transform transform; public GameObject gameObject;
        public T GetComponent<T>() where T : class { return null; }
        public T GetComponentInChildren<T>() where T : class { return null; }
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : class { return new T[0]; }
        public T GetComponentInParent<T>() where T : class { return null; }
        public Component[] GetComponents<Component>() { return new Component[0]; }
    }
    public class Behaviour : Component { public bool enabled; }
    public enum LightType { Point, Directional, Spot }
    public enum LightShadows { None, Hard, Soft }
    public class Light : Behaviour { public LightType type; public Color color; public float range, intensity; public LightShadows shadows; }
    public class Renderer : Component { public Material sharedMaterial; public Material[] sharedMaterials; public Bounds bounds; }
    public class Animator : Behaviour { }
    public class ScriptableObject : Object { public static T CreateInstance<T>() where T : ScriptableObject { return null; } }
    public class MonoBehaviour : Behaviour {
        public static void Destroy(Object o) { }
        public static void DontDestroyOnLoad(Object o) { }
        public static T Instantiate<T>(T o) where T : Object { return o; }
        public static T Instantiate<T>(T o, Transform parent) where T : Object { return o; }
    }
    public class Transform : Component { public Vector3 position, localPosition, localScale; public Quaternion rotation, localRotation; public Transform root; public int childCount; public Transform GetChild(int i) { return null; } public void SetParent(Transform p, bool worldPositionStays) { } public void SetAsLastSibling() { } }
    public class RectTransform : Transform { public Vector2 anchorMin, anchorMax, pivot, sizeDelta, anchoredPosition; }
    public class GameObject : Object {
        public GameObject(string n, params Type[] components) { name = n; }
        public Transform transform; public bool activeSelf; public void SetActive(bool v) { }
        public T[] GetComponents<T>() { return new T[0]; }
        public T GetComponent<T>() where T : class { return null; }
        public T GetComponentInChildren<T>() where T : class { return null; }
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : class { return new T[0]; }
        public T AddComponent<T>() where T : Component { return null; }
    }
    public class Collider : Component { public bool enabled; }
    public class BoxCollider : Collider { public Vector3 center, size; }
    public class MeshCollider : Collider { public Mesh sharedMesh; public bool convex; }
    public class Mesh : Object { public Vector3[] vertices; public Vector2[] uv; public int[] triangles; public void RecalculateNormals() { } public void RecalculateTangents() { } public void RecalculateBounds() { } }
    public class MeshFilter : Component { public Mesh sharedMesh; public Mesh mesh; }
    public class MeshRenderer : Renderer { }
    public class Material : Object { public Material(Material source) { } public bool HasProperty(string name) { return false; } public void SetColor(string name, Color c) { } }
    public class Sprite : Object { }
    namespace UI { public class Image : Component { public Color color; public bool raycastTarget; } }
}

namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)] public class BepInPlugin : Attribute { public BepInPlugin(string g, string n, string v) { } }
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)] public class BepInDependency : Attribute { public BepInDependency(string g) { } }
    public class BaseUnityPlugin : UnityEngine.MonoBehaviour { protected Configuration.ConfigFile Config; protected Logging.ManualLogSource Logger; }
    namespace Logging { public class ManualLogSource { public void LogInfo(object o) { } public void LogWarning(object o) { } public void LogError(object o) { } public void LogDebug(object o) { } } }
    namespace Configuration {
        public class AcceptableValueBase { }
        public class AcceptableValueRange<T> : AcceptableValueBase { public AcceptableValueRange(T min, T max) { } }
        public class ConfigDescription { public ConfigDescription(string desc, AcceptableValueBase range = null, params object[] tags) { } }
        public class ConfigEntry<T> { public T Value; public event EventHandler SettingChanged; }
        public class ConfigFile { public ConfigEntry<T> Bind<T>(string section, string key, T def, ConfigDescription desc) { return new ConfigEntry<T>(); } }
        public struct KeyboardShortcut {
            public KeyboardShortcut(UnityEngine.KeyCode main, params UnityEngine.KeyCode[] modifiers) { }
            public bool IsDown() { return false; }
        }
    }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)] public class HarmonyPatch : Attribute { public HarmonyPatch(Type t, string m) { } }
    [AttributeUsage(AttributeTargets.Method)] public class HarmonyPostfix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class HarmonyPrefix : Attribute { }
    public class Harmony { public Harmony(string id) { } public void PatchAll(Type t) { } public void UnpatchSelf() { } }
    public static class AccessTools {
        public static System.Reflection.MethodInfo Method(Type t, string n, Type[] args) { return null; }
        public static System.Reflection.FieldInfo Field(Type t, string n) { return null; }
        public static Type TypeByName(string name) { return null; }
    }
}

namespace Jotunn
{
    public static class Main { public const string ModGuid = "com.jotunn.jotunn"; }
    namespace Utils {
        public enum CompatibilityLevel { EveryoneMustHaveMod }
        public enum VersionStrictness { Minor }
        [AttributeUsage(AttributeTargets.Class)] public class NetworkCompatibilityAttribute : Attribute { public NetworkCompatibilityAttribute(CompatibilityLevel c, VersionStrictness v) { } }
    }
    namespace Configs {
        public class ConfigurationManagerAttributes { public bool IsAdminOnly; }
        public class RequirementConfig { public string Item; public int Amount; public bool Recover; }
        public class PieceConfig { public string Name, Description, PieceTable, Category, CraftingStation; public bool AllowedInDungeons; public RequirementConfig[] Requirements; public UnityEngine.Sprite Icon; }
    }
    namespace Entities {
        public class CustomPiece { public CustomPiece(UnityEngine.GameObject prefab, bool fixReference, Configs.PieceConfig config) { } }
        public class CustomLocalization { public void AddTranslation(string language, Dictionary<string, string> tokens) { } }
        public class CustomItem { public CustomItem(UnityEngine.GameObject prefab, bool fixReference) { } }
        public class CustomStatusEffect { public CustomStatusEffect(StatusEffect effect, bool fixReference) { } }
    }
    namespace Managers {
        public class PrefabManager {
            public static PrefabManager Instance;
            public static event Action OnVanillaPrefabsAvailable;
            public UnityEngine.GameObject GetPrefab(string name) { return null; }
            public UnityEngine.GameObject CreateClonedPrefab(string newName, UnityEngine.GameObject basePrefab) { return null; }
        }
        public class PieceManager { public static PieceManager Instance; public void AddPiece(Entities.CustomPiece piece) { } }
        public class LocalizationManager { public static LocalizationManager Instance; public Entities.CustomLocalization GetLocalization() { return null; } }
        public class ItemManager {
            public static ItemManager Instance;
            public void AddItem(Entities.CustomItem item) { }
            public void AddStatusEffect(Entities.CustomStatusEffect effect) { }
        }
        public class RenderManager {
            public static RenderManager Instance { get { return null; } }
            public static UnityEngine.Quaternion IsometricRotation;
            public class RenderRequest { public RenderRequest(UnityEngine.GameObject target) { } public UnityEngine.Quaternion Rotation { get; set; } }
            public UnityEngine.Sprite Render(RenderRequest request) { return null; }
        }
    }
}
