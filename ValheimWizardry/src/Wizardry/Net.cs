using System;
using HarmonyLib;
using UnityEngine;

namespace Wizardry
{
    /// <summary>
    ///     The mod's network messages. Statuses, pushes and cleansing go to the owner of the character (only the
    ///     owner may change its speed, AI and status effects); effects go to everybody, and every game plays its own.
    /// </summary>
    internal static class Net
    {
        public const string RpcStatus = "WZ_Status";
        public const string RpcImpulse = "WZ_Impulse";
        public const string RpcCleanse = "WZ_Cleanse";
        public const string RpcFx = "WZ_Fx";

        public static void RegisterCharacter(Character c)
        {
            var nv = c != null ? c.m_nview : null;
            if (nv == null || nv.GetZDO() == null)
            {
                return;
            }

            nv.Register<ZPackage>(RpcStatus, (sender, pkg) => OnStatus(c, pkg));
            nv.Register<Vector3>(RpcImpulse, (sender, v) => OnImpulse(c, v));
            nv.Register<string>(RpcCleanse, (sender, what) => Effects.CleanseLocal(c, what));
        }

        public static void RegisterRouted()
        {
            if (ZRoutedRpc.instance == null)
            {
                return;
            }

            ZRoutedRpc.instance.Register<ZPackage>(RpcFx, (sender, pkg) => Fx.Receive(pkg));
        }

        // ------------------------------------------------------------------ status
        public static void SendStatus(Character target, StatusOrder o)
        {
            var nv = target != null ? target.m_nview : null;
            if (nv == null || !nv.IsValid())
            {
                return;
            }

            if (nv.IsOwner())
            {
                Effects.ApplyLocal(target, o);
                return;
            }

            var pkg = new ZPackage();
            o.Write(pkg);
            nv.InvokeRPC(RpcStatus, pkg);
        }

        private static void OnStatus(Character c, ZPackage pkg)
        {
            if (c == null || c.m_nview == null || !c.m_nview.IsOwner())
            {
                return;
            }

            try
            {
                Effects.ApplyLocal(c, StatusOrder.Read(pkg));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Status message failed: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ pushes
        public static void Impulse(Character target, Vector3 velocity)
        {
            var nv = target != null ? target.m_nview : null;
            if (nv == null || !nv.IsValid())
            {
                return;
            }

            if (nv.IsOwner())
            {
                OnImpulse(target, velocity);
            }
            else
            {
                nv.InvokeRPC(RpcImpulse, velocity);
            }
        }

        private static void OnImpulse(Character c, Vector3 v)
        {
            if (c == null || c.m_body == null || (c.m_nview != null && !c.m_nview.IsOwner()) || c.IsBoss())
            {
                return;
            }

            c.m_body.velocity += v;
            if (v.y > 1f)
            {
                c.m_lastGroundTouch = 0f;
            }
        }

        public static void Cleanse(Character target, string what)
        {
            var nv = target != null ? target.m_nview : null;
            if (nv == null || !nv.IsValid())
            {
                return;
            }

            if (nv.IsOwner())
            {
                Effects.CleanseLocal(target, what);
            }
            else
            {
                nv.InvokeRPC(RpcCleanse, what);
            }
        }
    }

    /// <summary>A status to put on a character, as sent over the network.</summary>
    internal sealed class StatusOrder
    {
        public string Key;          // slow, stun, buff ...
        public string SeName;       // the registered status effect (WZ_slow, WZ_buff_<spell>)
        public float Duration;
        public float Power;
        public string Variant = "";
        public string DamageType = "";
        public string Buffs = "";
        public ZDOID Source = ZDOID.None;

        public void Write(ZPackage p)
        {
            p.Write(Key ?? "");
            p.Write(SeName ?? "");
            p.Write(Duration);
            p.Write(Power);
            p.Write(Variant ?? "");
            p.Write(DamageType ?? "");
            p.Write(Buffs ?? "");
            p.Write(Source);
        }

        public static StatusOrder Read(ZPackage p)
        {
            return new StatusOrder
            {
                Key = p.ReadString(), SeName = p.ReadString(), Duration = p.ReadSingle(), Power = p.ReadSingle(),
                Variant = p.ReadString(), DamageType = p.ReadString(), Buffs = p.ReadString(), Source = p.ReadZDOID()
            };
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Awake))]
    internal static class CharacterAwakePatch
    {
        private static void Postfix(Character __instance)
        {
            try
            {
                Net.RegisterCharacter(__instance);
                Summons.OnCharacterAwake(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug("Character setup: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
    internal static class SceneAwakePatch
    {
        private static void Postfix()
        {
            Net.RegisterRouted();
        }
    }
}
