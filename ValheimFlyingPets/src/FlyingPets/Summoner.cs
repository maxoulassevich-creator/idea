using UnityEngine;

namespace FlyingPets
{
    /// <summary>What the staff does: summon, call, dismiss, choose the mount.</summary>
    internal static class Summoner
    {
        private static float s_nextUse;
        private static int s_selected;

        /// <summary>Handles an attack with the staff. Returns true when the attack input was consumed.</summary>
        public static bool UseStaff(Player player, bool secondary)
        {
            if (Time.time < s_nextUse)
            {
                return true;
            }

            if (player.InAttack() || player.InDodge() || !player.CanMove() || player.IsAttached() ||
                player.InPlaceMode() || player.IsTeleporting())
            {
                return true;
            }

            s_nextUse = Time.time + 1f;
            if (PetLibrary.Pets.Count == 0)
            {
                player.Message(MessageHud.MessageType.Center, Texts.Get("fp_msg_nopets"));
                return true;
            }

            var pet = FlyingPet.FindOwnedBy(player.GetPlayerID());
            if (!secondary)
            {
                if (pet != null)
                {
                    pet.Call(player);
                    player.Message(MessageHud.MessageType.Center, Texts.Get("fp_msg_call", pet.DisplayName));
                    Emote.DoEmote(Emotes.ComeHere);
                }
                else
                {
                    Summon(player);
                }

                return true;
            }

            if (pet != null)
            {
                if (pet.RiderId != 0L)
                {
                    player.Message(MessageHud.MessageType.Center, Texts.Get("fp_msg_busy"));
                    return true;
                }

                player.Message(MessageHud.MessageType.Center, Texts.Get("fp_msg_dismiss", pet.DisplayName));
                pet.Dismiss();
                Emote.DoEmote(Emotes.Wave);
                return true;
            }

            // nothing summoned: the secondary attack picks the next mount
            s_selected = (s_selected + 1) % PetLibrary.Pets.Count;
            player.Message(MessageHud.MessageType.Center, Texts.Get("fp_msg_select", PetLibrary.Pets[s_selected].DisplayName));
            return true;
        }

        private static void Summon(Player player)
        {
            if (s_selected >= PetLibrary.Pets.Count)
            {
                s_selected = 0;
            }

            var asset = PetLibrary.Pets[s_selected];
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(asset.PrefabName) : null;
            if (prefab == null)
            {
                prefab = asset.Prefab;
            }

            if (prefab == null)
            {
                FlyingPetsPlugin.Log.LogError(asset.PrefabName + " is not registered");
                return;
            }

            Vector3 fwd = player.transform.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            float scale = ModConfig.PetScale.Value;
            Vector3 pos = player.transform.position + fwd * (6f * scale);
            float water;
            float ground = Util.GroundY(pos, 4f, out water);
            pos.y = Mathf.Max(ground, water);

            // room check: the torso must not end up inside a wall or a tree
            Vector3 center = pos + Vector3.up * (1.55f * scale);
            if (Physics.CheckSphere(center, 0.5f * scale, Util.SolidMask, QueryTriggerInteraction.Ignore))
            {
                pos = player.transform.position + Vector3.up * 0.1f - fwd * (0.5f * scale);
                center = pos + Vector3.up * (1.55f * scale);
                if (Physics.CheckSphere(center, 0.5f * scale, Util.SolidMask, QueryTriggerInteraction.Ignore))
                {
                    player.Message(MessageHud.MessageType.Center, Texts.Get("fp_msg_noroom"));
                    return;
                }
            }

            // face the summoner
            var go = Object.Instantiate(prefab, pos, Quaternion.LookRotation(-fwd));
            var pet = go.GetComponent<FlyingPet>();
            if (pet != null)
            {
                pet.InitSummoned(player);
            }

            Util.SpawnEffect(Util.SummonEffects, pos + Vector3.up * (1.2f * scale), Quaternion.identity);
            player.Message(MessageHud.MessageType.Center, Texts.Get("fp_msg_summon", asset.DisplayName));
            Emote.DoEmote(Emotes.ComeHere);
        }
    }
}
