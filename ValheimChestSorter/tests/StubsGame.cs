// Compile-check stubs for the Valheim assemblies (global namespace).
using System;
using System.Collections.Generic;
using UnityEngine;

public static class StringExtensionMethods { public static int GetStableHashCode(this string s) { return 0; } }

public struct Vector2i { public int x, y; public Vector2i(int a, int b) { x = a; y = b; } }

public class ZDO {
    public string GetString(int hash, string def) { return def; }
    public void Set(int hash, string value) { }
    public bool GetBool(int hash, bool def) { return false; }
    public int GetInt(int hash, int def) { return 0; }
    public void Set(int hash, int value) { }
    public void Set(int hash, bool value) { }
    public void Set(int hash, long value) { }
}

public class ZNetView : MonoBehaviour {
    public static bool m_forceDisableInit;
    public bool IsValid() { return false; }
    public bool IsOwner() { return false; }
    public void ClaimOwnership() { }
    public ZDO GetZDO() { return null; }
    public void Destroy() { }
}

public class ZNet { public static ZNet instance; public DateTime GetTime() { return DateTime.Now; } }
public class ZNetScene : MonoBehaviour { public static ZNetScene instance; public List<GameObject> m_prefabs; public GameObject GetPrefab(string name) { return null; } }

public class EffectList { public GameObject[] Create(Vector3 pos, Quaternion rot) { return null; } }

public class Heightmap : MonoBehaviour {
    [Flags] public enum Biome { None = 0, Meadows = 1, Swamp = 2 }
    public static Heightmap FindHeightmap(Vector3 point) { return null; }
    public static Biome FindBiome(Vector3 point) { return Biome.None; }
    public bool IsCultivated(Vector3 point) { return false; }
}

public class Pickable : MonoBehaviour {
    public GameObject m_itemPrefab;
    public int m_amount;
    public float m_respawnTimeMinutes;
    public bool m_picked;
    public GameObject m_hideWhenPicked;
}

public class Plant : MonoBehaviour {
    public GameObject[] m_grownPrefabs;
    public float m_growRadius;
    public bool m_needCultivatedGround;
    public Heightmap.Biome m_biome;
}

public class ItemDrop : MonoBehaviour {
    public class ItemData {
        public enum ItemType { None, Material, Consumable, OneHandedWeapon, Bow, Shield, Helmet, Chest, Ammo, Customization, Legs, Hands, Trophy, TwoHandedWeapon, Torch, Misc, Shoulder, Utility, Tool, Attach_Atgeir, Fish, AmmoNonEquipable, TwoHandedWeaponLeft }
        public class SharedData {
            public string m_name, m_description;
            public int m_maxStackSize;
            public int m_value;
            public ItemType m_itemType;
            public float m_weight, m_food, m_foodStamina, m_foodEitr, m_foodRegen, m_foodBurnTime;
            public bool m_teleportable, m_questItem;
            public Sprite[] m_icons;
            public StatusEffect m_consumeStatusEffect;
        }
        public SharedData m_shared;
        public int m_stack, m_quality, m_variant;
        public long m_crafterID;
        public string m_crafterName;
        public float m_durability;
        public Vector2i m_gridPos;
        public GameObject m_dropPrefab;
        public ItemData Clone() { return (ItemData)MemberwiseClone(); }
    }
    public ItemData m_itemData;
}

public class Piece : MonoBehaviour {
    public class Requirement { public ItemDrop m_resItem; public int m_amount; }
    public Requirement[] m_resources;
    public EffectList m_placeEffect;
    public string m_name, m_description;
}

public class Inventory {
    public List<ItemDrop.ItemData> m_inventory;
    public int GetWidth() { return 0; }
    public int GetHeight() { return 0; }
    public ItemDrop.ItemData GetItemAt(int x, int y) { return null; }
    public bool AddItem(ItemDrop.ItemData item, int amount, int x, int y) { return true; }
    public void RemoveItem(ItemDrop.ItemData item, int amount) { }
    public void Changed() { }
}

public class Container : MonoBehaviour {
    public string m_name;
    public int m_width, m_height;
    public bool m_autoDestroyEmpty;
    public EffectList m_openEffects, m_closeEffects;
    public Inventory GetInventory() { return null; }
    public void Save() { }
    public string GetHoverText() { return null; }
}

public class WearNTear : MonoBehaviour { public bool m_noRoofWear, m_noSupportWear; }
public class Localization { public static Localization instance; public string Localize(string word) { return word; } }
public class InventoryGui : MonoBehaviour { public static InventoryGui instance; }


public class StatusEffect : ScriptableObject { public string m_name, m_tooltip; public Sprite m_icon; public float m_ttl; }
public class SE_Stats : StatusEffect { }
public class Destructible : MonoBehaviour { public void Damage(HitData hit) { } }
public class HitData { public Character GetAttacker() { return null; } }
public class Character : MonoBehaviour { }
public class Player : Character { public static Player m_localPlayer; }
public class MessageHud : MonoBehaviour {
    public enum MessageType { TopLeft, Center }
    public static MessageHud instance;
    public void ShowMessage(MessageType type, string text, int amount = 1, Sprite icon = null) { }
}
public class EnvMan : MonoBehaviour { public static EnvMan instance; public static bool IsNight() { return false; } }
