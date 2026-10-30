using System;
using System.Collections.Generic;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace Wizardry
{
    internal sealed class WandWood
    {
        public string Key, Token;
        public int Tier, Station;
        public float Power, Eitr;
        public string[] Affinity;
        public string[][] Recipe; // each entry: candidates..., last element is the amount
        public Color Tint, Glow;
        public bool Hallow;
    }

    internal sealed class WandCore
    {
        public string Key, Token, Mods;
        public string[] Material;
        public int Amount;
        public Color Color;
    }

    /// <summary>Wands: eight woods and the Elder Wand, ten cores, four fittings (the item's quality).</summary>
    internal static class Wands
    {
        public const string Prefix = "WZ_Wand_";

        public static readonly List<WandWood> Woods = new List<WandWood>
        {
            W("beech", 1, 1, 1.00f, 30, new[] { "CHA", "DEF" }, new Color(0.95f, 0.85f, 0.72f), R("Wood", 6), R("Resin", 3), R("Feathers", 2)),
            W("fir", 2, 2, 1.12f, 40, new[] { "WAR", "HER" }, new Color(0.9f, 0.62f, 0.4f), R("RoundLog", 6), R("GreydwarfEye", 4), R("Bronze", 1)),
            W("birch", 3, 2, 1.25f, 50, new[] { "TRF", "HEA" }, new Color(1f, 0.95f, 0.85f), R("FineWood", 6), R("Bronze", 2), R("Resin", 4)),
            W("ancient", 4, 3, 1.40f, 60, new[] { "DRK", "MND" }, new Color(0.55f, 0.6f, 0.45f), R("ElderBark", 8), R("Guck", 4), R("Iron", 2)),
            W("eldercopy", 5, 4, 1.50f, 65, new[] { "CON", "DRK" }, new Color(0.75f, 0.72f, 0.68f), R("FineWood", 8), R("AncientSeed", 3), R("Silver", 2)),
            W("yggdrasil", 6, 5, 1.70f, 80, new[] { "RUN", "MND" }, new Color(0.65f, 0.7f, 0.95f), R("YggdrasilWood", 8), R("Sap", 4), R("BlackMetal", 2)),
            W("ash", 7, 6, 1.85f, 90, new[] { "ELE", "DRK" }, new Color(0.45f, 0.38f, 0.35f), R("Blackwood", "Ashwood", "YggdrasilWood", 8), R("FlametalNew", "Flametal", "BlackMetal", 2), R("CharredBone", "BoneFragments", 5)),
            W("north", 8, 7, 2.00f, 100, new[] { "MOV", "WAR" }, new Color(0.9f, 0.95f, 1f), R("Timberwood", "TimberWood", "Blackwood", "YggdrasilWood", 8), R("FlametalNew", "Flametal", "BlackMetal", 4), R("FreezeGland", 5)),
            W("elder", 9, 7, 2.20f, 110, new[] { "*" }, new Color(0.82f, 0.82f, 0.8f), R("TrophyTheElder", 1), R("Wishbone", 1), R("FlametalNew", "Flametal", "BlackMetal", 5)),
        };

        public static readonly List<WandCore> Cores = new List<WandCore>
        {
            C("feather", "proj_speed:10;cast_speed:5", new Color(0.3f, 0.3f, 0.35f), 5, "Feathers"),
            C("antler", "dmg_lightning:15;flag:core_chain", new Color(0.55f, 0.75f, 1f), 1, "HardAntler"),
            C("fenris", "move_speed:6;crit:10", new Color(0.75f, 0.75f, 0.8f), 3, "WolfHairBundle", "WolfPelt"),
            C("wishbone", "dmg_spirit:15;dmg_dark:15;dark_decay:30", new Color(0.4f, 0.9f, 0.5f), 1, "Wishbone"),
            C("serpent", "dmg_frost:15;ward_power:15;flag:core_sense", new Color(0.3f, 0.8f, 0.85f), 3, "SerpentScale"),
            C("dragon", "dmg:15;flag:core_wild", new Color(1f, 0.55f, 0.2f), 3, "DragonTear"),
            C("troll", "stagger:20;hp:15", new Color(0.4f, 0.55f, 0.8f), 3, "TrollHide"),
            C("celestial", "flag:core_phoenix", new Color(1f, 0.85f, 0.4f), 3, "CelestialFeather", "Feathers"),
            C("morgen", "lifesteal:4", new Color(0.8f, 0.2f, 0.25f), 1, "MorgenHeart", "MorgenSinew", "Bloodbag"),
            C("ice", "dmg_frost:20;ctrl_dur:30", new Color(0.8f, 0.95f, 1f), 5, "FreezeGland"),
        };

        private static string[] R(params object[] x)
        {
            var list = new List<string>();
            foreach (var o in x)
            {
                list.Add(o.ToString());
            }

            return list.ToArray();
        }

        private static WandWood W(string key, int tier, int station, float power, float eitr, string[] aff, Color tint, params string[][] recipe)
        {
            return new WandWood
            {
                Key = key, Token = "$wz_wand_" + key, Tier = tier, Station = station, Power = power, Eitr = eitr, Affinity = aff,
                Recipe = recipe, Tint = tint, Hallow = key == "elder"
            };
        }

        private static WandCore C(string key, string mods, Color c, int amount, params string[] material)
        {
            return new WandCore { Key = key, Token = "$wz_core_" + key, Mods = mods, Material = material, Amount = amount, Color = c };
        }

        // ------------------------------------------------------------------ queries
        public static WandWood WoodOf(ItemDrop.ItemData item)
        {
            if (item == null || item.m_shared == null || item.m_shared.m_name == null || !item.m_shared.m_name.StartsWith("$wz_wand_"))
            {
                return null;
            }

            string key = item.m_shared.m_name.Substring("$wz_wand_".Length);
            foreach (var w in Woods)
            {
                if (w.Key == key)
                {
                    return w;
                }
            }

            return null;
        }

        public static bool IsWand(ItemDrop.ItemData item)
        {
            return WoodOf(item) != null;
        }

        public static ItemDrop.ItemData Equipped(Player p)
        {
            if (p == null)
            {
                return null;
            }

            var r = p.GetRightItem();
            return IsWand(r) ? r : null;
        }

        /// <summary>The best wand the player carries (wandless magic and the bar's numbers use it).</summary>
        public static ItemDrop.ItemData Best(Player p)
        {
            ItemDrop.ItemData best = null;
            float bp = 0f;
            if (p == null)
            {
                return null;
            }

            foreach (var it in p.GetInventory().GetAllItems())
            {
                var w = WoodOf(it);
                if (w != null && w.Power * QualityMult(it) > bp)
                {
                    best = it;
                    bp = w.Power * QualityMult(it);
                }
            }

            return best;
        }

        private static float QualityMult(ItemDrop.ItemData it)
        {
            return 1f + 0.05f * Mathf.Max(0, it.m_quality - 1);
        }

        /// <summary>Wand power: the wood × the fitting. Without a wand in hand only wandless magic works (×0.7 of the best).</summary>
        public static float Power(Player p)
        {
            var it = Equipped(p);
            if (it != null)
            {
                return WoodOf(it).Power * QualityMult(it);
            }

            var w = Wizard.State;
            if (w != null && w.Stats.Has("ks_wandless"))
            {
                var best = Best(p);
                return best != null ? WoodOf(best).Power * QualityMult(best) * 0.7f : 0.7f;
            }

            return 1f;
        }

        public static float Eitr(Player p)
        {
            var it = Equipped(p);
            if (it == null)
            {
                var w = Wizard.State;
                if (w != null && w.Stats.Has("ks_wandless") && Best(p) != null)
                {
                    it = Best(p);
                }
                else
                {
                    return 0f;
                }
            }

            return (WoodOf(it).Eitr + 5f * Mathf.Max(0, it.m_quality - 1)) * ModConfig.WandEitrMultiplier.Value;
        }

        public static float Affinity(Player p, string school)
        {
            var it = Equipped(p);
            var wood = WoodOf(it);
            if (wood == null)
            {
                return 0f;
            }

            foreach (var a in wood.Affinity)
            {
                if (a == school)
                {
                    return 10f;
                }

                if (a == "*")
                {
                    return 5f;
                }
            }

            return 0f;
        }

        public static string CoreKey(ItemDrop.ItemData item)
        {
            string c;
            return item != null && item.m_customData != null && item.m_customData.TryGetValue("wz_core", out c) ? c : null;
        }

        public static WandCore CoreOf(ItemDrop.ItemData item)
        {
            string k = CoreKey(item);
            foreach (var c in Cores)
            {
                if (c.Key == k)
                {
                    return c;
                }
            }

            return null;
        }

        public static string CoreMods(Player p)
        {
            var it = Equipped(p);
            var c = CoreOf(it);
            return c != null ? c.Mods : "";
        }

        public static void SetCore(ItemDrop.ItemData wand, string core)
        {
            if (wand.m_customData == null)
            {
                wand.m_customData = new Dictionary<string, string>();
            }

            wand.m_customData["wz_core"] = core;
        }

        // ------------------------------------------------------------------ registration
        public static string FindPrefab(params string[] candidates)
        {
            foreach (string c in candidates)
            {
                if (!string.IsNullOrEmpty(c) && PrefabManager.Instance.GetPrefab(c) != null)
                {
                    return c;
                }
            }

            return null;
        }

        public static RequirementConfig[] Requirements(string[][] recipe, int upgradeAmount = 0)
        {
            var list = new List<RequirementConfig>();
            foreach (var entry in recipe)
            {
                int amount;
                int.TryParse(entry[entry.Length - 1], out amount);
                var names = new string[entry.Length - 1];
                Array.Copy(entry, names, names.Length);
                string item = FindPrefab(names);
                if (item != null)
                {
                    list.Add(new RequirementConfig(item, Mathf.Max(1, amount), upgradeAmount, true));
                }
            }

            if (list.Count == 0)
            {
                list.Add(new RequirementConfig("Wood", 5, 0, true));
            }

            return list.ToArray();
        }

        public static void Register()
        {
            string basePrefab = FindPrefab("Club", "KnifeFlint", "Torch");
            if (basePrefab == null)
            {
                Plugin.Log.LogError("No vanilla one-handed item to build the wands on");
                return;
            }

            string[] metals = { "Bronze", "Iron", "BlackMetal" };
            foreach (var wood in Woods)
            {
                try
                {
                    var req = new List<RequirementConfig>(Requirements(wood.Recipe));
                    // the fitting: one more metal per quality level
                    string metal = FindPrefab(metals[Mathf.Clamp(wood.Tier / 3, 0, 2)]) ?? "Bronze";
                    req.Add(new RequirementConfig(metal, 0, 1, true));
                    var cfg = new ItemConfig
                    {
                        Name = wood.Token,
                        Description = "$wz_wand_desc",
                        CraftingStation = Pieces.CodexName,
                        MinStationLevel = wood.Station,
                        Requirements = req.ToArray(),
                        Icons = new[] { Icons.Make("wand_" + wood.Key, Icons.Shape.Wand, wood.Tint) },
                    };
                    var item = new CustomItem(Prefix + wood.Key, basePrefab, cfg);
                    var shared = item.ItemDrop.m_itemData.m_shared;
                    shared.m_maxQuality = 4;
                    shared.m_useDurability = false;
                    shared.m_weight = 0.4f;
                    shared.m_teleportable = true;
                    shared.m_skillType = Plugin.SkillType;
                    shared.m_blockPower = 10f + 6f * wood.Tier;
                    shared.m_blockPowerPerLevel = 3f;
                    shared.m_timedBlockBonus = 2f;
                    shared.m_deflectionForce = 20f + 5f * wood.Tier;
                    shared.m_damages = new HitData.DamageTypes { m_blunt = 2f + wood.Tier };
                    shared.m_damagesPerLevel = new HitData.DamageTypes();
                    shared.m_attackStatusEffect = null;
                    shared.m_equipStatusEffect = null;
                    if (shared.m_attack != null)
                    {
                        shared.m_attack.m_attackStamina = 0f;
                        shared.m_attack.m_attackEitr = 0f;
                    }

                    if (shared.m_secondaryAttack != null)
                    {
                        shared.m_secondaryAttack.m_attackStamina = 0f;
                        shared.m_secondaryAttack.m_attackEitr = 0f;
                    }

                    FitModel(item.ItemPrefab, wood);
                    ItemManager.Instance.AddItem(item);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Wand " + wood.Key + " could not be registered: " + e);
                }
            }

            foreach (var core in Cores)
            {
                string mat = FindPrefab(core.Material) ?? "Feathers";
                Items.Simple("WZ_Core_" + core.Key, core.Token, "$wz_core_desc", Icons.Shape.Core, core.Color, 1,
                    new[] { new RequirementConfig(mat, core.Amount, 0, true), new RequirementConfig("Resin", 2, 0, true) }, 0.1f);
            }
        }

        // ------------------------------------------------------------------ the model
        private static Material s_wandMaterial;

        /// <summary>Hides the club and puts our wand in its hand, along the club's long axis, 36 cm long.</summary>
        private static void FitModel(GameObject prefab, WandWood wood)
        {
            if (Meshes.Get("wand.wzm") == null)
            {
                return;
            }

            foreach (Transform child in prefab.transform)
            {
                if (child.name != "attach" && child.name != "attach_back")
                {
                    continue;
                }

                var renderers = child.GetComponentsInChildren<Renderer>(true);
                var b = new Bounds();
                bool any = false;
                int layer = child.gameObject.layer;
                foreach (var r in renderers)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    var mesh = mf != null ? mf.sharedMesh : null;
                    if (mesh == null)
                    {
                        continue;
                    }

                    layer = r.gameObject.layer;
                    Matrix4x4 m = child.worldToLocalMatrix * r.transform.localToWorldMatrix;
                    var mb = mesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var p = m.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? mb.min.x : mb.max.x, (i & 2) == 0 ? mb.min.y : mb.max.y,
                            (i & 4) == 0 ? mb.min.z : mb.max.z));
                        if (!any)
                        {
                            b = new Bounds(p, Vector3.zero);
                            any = true;
                        }
                        else
                        {
                            b.Encapsulate(p);
                        }
                    }

                    r.enabled = false;
                }

                foreach (var ps in child.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ps.gameObject.SetActive(false);
                }

                Vector3 axis = Vector3.up;
                if (any)
                {
                    var e = b.extents;
                    if (e.x >= e.y && e.x >= e.z) axis = new Vector3(Mathf.Sign(b.center.x == 0f ? 1f : b.center.x), 0f, 0f);
                    else if (e.y >= e.z) axis = new Vector3(0f, Mathf.Sign(b.center.y == 0f ? 1f : b.center.y), 0f);
                    else axis = new Vector3(0f, 0f, Mathf.Sign(b.center.z == 0f ? 1f : b.center.z));
                }

                const float length = 0.36f, grip = 0.16f;
                var go = new GameObject("WZ_WandModel");
                go.layer = layer;
                go.transform.SetParent(child, false);
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis);
                go.transform.localScale = Vector3.one * length;
                go.transform.localPosition = -(go.transform.localRotation * new Vector3(0f, grip * length, 0f));
                go.AddComponent<MeshFilter>().sharedMesh = Meshes.Get("wand.wzm");
                var mr = go.AddComponent<MeshRenderer>();
                if (s_wandMaterial == null)
                {
                    s_wandMaterial = Meshes.Material("wand_albedo.jpg", "wand_normal.jpg", null, Color.white, Color.black);
                }

                var mat = new Material(s_wandMaterial);
                if (mat.HasProperty("_Color"))
                {
                    mat.SetColor("_Color", wood.Tint);
                }

                mr.sharedMaterial = mat;
                var tip = new GameObject("WandTip");
                tip.transform.SetParent(go.transform, false);
                tip.transform.localPosition = new Vector3(0f, 1f, 0f);
            }
        }

        /// <summary>Where the spells come out: the wand's tip, or the right hand.</summary>
        public static Vector3 Tip(Player p)
        {
            if (p == null)
            {
                return Vector3.zero;
            }

            var vis = p.GetComponentInChildren<VisEquipment>();
            Transform hand = vis != null ? vis.m_rightHand : null;
            if (hand != null)
            {
                var tip = FindDeep(hand, "WandTip");
                if (tip != null)
                {
                    return tip.position;
                }

                return hand.position + p.transform.forward * 0.3f;
            }

            return p.transform.position + Vector3.up * 1.4f + p.transform.forward * 0.5f;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name)
            {
                return t;
            }

            foreach (Transform c in t)
            {
                var f = FindDeep(c, name);
                if (f != null)
                {
                    return f;
                }
            }

            return null;
        }
    }
}
