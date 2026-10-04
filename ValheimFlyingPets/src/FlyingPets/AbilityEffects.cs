using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FlyingPets
{
    /// <summary>
    ///     The status effects the abilities show on the local player: one per pet while in its saddle
    ///     (it carries the pegasus' extra carry weight and lists the pet's passives in its tooltip), and
    ///     the timers of the active abilities and of the valkyrie's catch.
    /// </summary>
    internal static class AbilityEffects
    {
        public const string Thunder = "thunder";
        public const string Grab = "grab";
        public const string Hold = "hold";
        public const string Catch = "catch";

        private static readonly Dictionary<string, Sprite> Icons = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, StatusEffect> Effects = new Dictionary<string, StatusEffect>();
        private static readonly HashSet<string> MissingIcons = new HashSet<string>();

        /// <summary>An icon from icons/&lt;name&gt;.png (built into the dll, or a file next to it).</summary>
        public static Sprite Icon(string name)
        {
            Sprite sprite;
            if (Icons.TryGetValue(name, out sprite) || MissingIcons.Contains(name))
            {
                return sprite;
            }

            try
            {
                byte[] bytes = null;
                var asm = typeof(AbilityEffects).Assembly;
                using (var stream = asm.GetManifestResourceStream("icons/" + name + ".png"))
                {
                    if (stream != null)
                    {
                        var ms = new MemoryStream();
                        stream.CopyTo(ms);
                        bytes = ms.ToArray();
                    }
                }

                string dir = Path.GetDirectoryName(asm.Location);
                string file = string.IsNullOrEmpty(dir) ? null : Path.Combine(Path.Combine(dir, "icons"), name + ".png");
                if (file != null && File.Exists(file))
                {
                    bytes = File.ReadAllBytes(file);
                }

                if (bytes != null)
                {
                    var tex = PetAsset.LoadIconTexture(bytes, "FP_icon_" + name);
                    tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                    sprite.name = "FP_icon_" + name;
                    sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
                }
            }
            catch (Exception e)
            {
                FlyingPetsPlugin.Log.LogWarning("Icon " + name + " could not be loaded: " + e.Message);
                sprite = null;
            }

            if (sprite == null)
            {
                MissingIcons.Add(name);
            }
            else
            {
                Icons[name] = sprite;
            }

            return sprite;
        }

        private static T Make<T>(string id, string nameToken, string tooltipToken, string icon) where T : StatusEffect
        {
            StatusEffect se;
            if (Effects.TryGetValue(id, out se))
            {
                return (T)se;
            }

            var t = ScriptableObject.CreateInstance<T>();
            t.name = "FP_SE_" + id;
            t.m_name = "$" + nameToken;
            t.m_tooltip = Texts.Has(tooltipToken) ? "$" + tooltipToken : "";
            t.m_icon = Icon(icon);
            t.m_category = "FP_" + id;
            t.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Effects[id] = t;
            return t;
        }

        /// <summary>In the saddle: the pet's passives, and for the pegasus the extra carry weight.</summary>
        public static void SetRide(Player player, PetAsset asset, bool on)
        {
            if (player == null || asset == null)
            {
                return;
            }

            try
            {
                var se = Make<SE_Stats>("ride_" + asset.Key, asset.NameToken, "fp_se_" + asset.Key + "_tip", asset.Key);
                if (!asset.Abilities.Contains(PetAbility.DraftHorse))
                {
                    se.m_addMaxCarryWeight = 0f;
                }
                else
                {
                    se.m_addMaxCarryWeight = Mathf.Max(0f, ModConfig.CarryBonus.Value);
                }

                var seman = player.GetSEMan();
                bool have = seman.HaveStatusEffect(se.NameHash());
                if (on && !have)
                {
                    seman.AddStatusEffect(se, false, 0, 0f);
                }
                else if (!on && have)
                {
                    seman.RemoveStatusEffect(se.NameHash(), true);
                }
            }
            catch (Exception e)
            {
                FlyingPetsPlugin.Log.LogWarning("Ride status effect failed: " + e.Message);
            }
        }

        /// <summary>Shows a timer (an ability recharging, or how long the raven can still hold its prey).</summary>
        public static void Timer(Player player, string id, float seconds)
        {
            if (player == null || seconds <= 0f)
            {
                return;
            }

            try
            {
                StatusEffect se;
                switch (id)
                {
                    case Thunder:
                        se = Make<StatusEffect>(id, "fp_se_thunder", "fp_se_thunder_tip", "thunder");
                        break;
                    case Grab:
                        se = Make<StatusEffect>(id, "fp_se_grab", "fp_se_grab_tip", "talon");
                        break;
                    case Hold:
                        se = Make<StatusEffect>(id, "fp_se_hold", "fp_se_hold_tip", "hold");
                        break;
                    default:
                        se = Make<StatusEffect>(id, "fp_se_catch", "fp_se_catch_tip", "valkyrie");
                        break;
                }

                se.m_ttl = seconds;
                se.m_cooldownIcon = id != Hold;
                var seman = player.GetSEMan();
                seman.RemoveStatusEffect(se.NameHash(), true);
                seman.AddStatusEffect(se, false, 0, 0f);
            }
            catch (Exception e)
            {
                FlyingPetsPlugin.Log.LogWarning("Timer status effect failed: " + e.Message);
            }
        }

        public static void ClearTimer(Player player, string id)
        {
            StatusEffect se;
            if (player != null && Effects.TryGetValue(id, out se))
            {
                player.GetSEMan().RemoveStatusEffect(se.NameHash(), true);
            }
        }
    }
}
