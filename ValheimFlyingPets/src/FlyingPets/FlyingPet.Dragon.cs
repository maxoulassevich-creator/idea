using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace FlyingPets
{
    /// <summary>
    ///     The water dragon's tidal breath (the active ability) and the fisher. Run by the rider's game, which
    ///     owns the pet; the jet and the splashes are sent to everybody with FP_Fx.
    /// </summary>
    public partial class FlyingPet
    {
        internal const int SkillBreath = 3;

        private const int FxBreath = 5;
        private const int FxSplash = 6;

        private const float BreathTime = 2f;       // how long the jet lasts, s
        private const float BreathGushEvery = 0.25f;
        private const float BreathHalfAngle = 28f;  // the cone of the jet, degrees

        private static readonly string[] FishOcean = { "Fish3", "Fish3", "Fish6", "Fish8", "Fish12" };

        private static float s_breathReady;
        private float m_breathTick;
        private float m_breathW;      // every client: how far the jaws are open
        private float m_fishTimer = -1f;
        private float m_sprayTimer;

        // ------------------------------------------------------------------ tidal breath
        private void BeginBreath(Player player)
        {
            SetSkill(SkillBreath);
            m_breathTick = 0.12f; // the jaws open first
            float cd = Mathf.Max(1f, ModConfig.BreathCooldown.Value);
            s_breathReady = Time.time + cd;
            AbilityEffects.Timer(player, AbilityEffects.Breath, cd);
            float range = Mathf.Max(2f, ModConfig.BreathRange.Value) * Mathf.Max(1f, m_scale);
            SendFx(FxBreath, transform.position, new Vector3(BreathTime, range, 0f));
        }

        /// <summary>Every physics step of the breath (owner): a gush of water every quarter of a second.</summary>
        private void BreathStep(float dt)
        {
            m_skillTime += dt;
            m_breathTick -= dt;
            if (m_breathTick <= 0f)
            {
                m_breathTick += BreathGushEvery;
                BreathGush();
            }

            if (m_skillTime >= BreathTime)
            {
                SetSkill(SkillNone);
            }
        }

        /// <summary>Where the jet leaves the jaws and where it points, as the head is posed now.</summary>
        private bool MouthNow(out Vector3 mouth, out Vector3 dir)
        {
            int head = m_asset != null ? m_asset.HeadIndex : -1;
            if (head < 0 || m_bones == null || head >= m_bones.Length || m_bones[head] == null)
            {
                mouth = transform.position + transform.forward * (2f * m_scale) + Vector3.up * (1.6f * m_scale);
                dir = transform.forward;
                return false;
            }

            var hb = m_bones[head];
            mouth = hb.TransformPoint(m_asset.Mouth - m_asset.Pivots[head]);
            dir = hb.TransformDirection(m_asset.MouthDir).normalized;
            return true;
        }

        private void BreathGush()
        {
            Vector3 mouth, dir;
            MouthNow(out mouth, out dir);
            float s = Mathf.Max(1f, m_scale);
            float range = Mathf.Max(2f, ModConfig.BreathRange.Value) * s;
            float damage = Mathf.Max(0f, ModConfig.BreathDamage.Value);
            float spread = Mathf.Tan(BreathHalfAngle * Mathf.Deg2Rad);
            var rider = m_localRider;
            foreach (var c in Character.GetAllCharacters())
            {
                if (c == null || c.IsPlayer() || c.IsDead() || c.IsTamed() || Passives.IsHeld(c))
                {
                    continue;
                }

                Vector3 to = c.GetCenterPoint() - mouth;
                float along = Vector3.Dot(to, dir);
                float r = c.GetRadius();
                if (along < -r || along > range + r)
                {
                    continue;
                }

                float off = (to - dir * along).magnitude;
                if (off > Mathf.Max(0f, along) * spread + 0.8f * s + r)
                {
                    continue;
                }

                float k = 1f - 0.5f * Mathf.Clamp01(along / range);
                var hit = new HitData();
                hit.m_damage.m_frost = damage * 0.7f * k;
                hit.m_damage.m_blunt = damage * 0.3f * k;
                hit.m_point = c.GetCenterPoint();
                hit.m_dir = dir;
                hit.m_pushForce = 22f * k;
                hit.m_staggerMultiplier = 0.5f;
                hit.m_statusEffectHash = SEMan.s_statusEffectWet; // soaked
                hit.m_hitType = HitData.HitType.PlayerHit;
                if (rider != null)
                {
                    hit.SetAttacker(rider);
                }

                c.Damage(hit);
            }
        }

        /// <summary>The jet on this game: water pouring from the jaws of the posed head for the given time.</summary>
        private void BreathFx(float duration, float range)
        {
            int head = m_asset != null ? m_asset.HeadIndex : -1;
            bool ok = head >= 0 && m_bones != null && head < m_bones.Length && m_bones[head] != null;
            Transform parent = ok ? m_bones[head] : transform;
            Vector3 local = ok ? m_asset.Mouth - m_asset.Pivots[head] : new Vector3(0f, 1.6f, 2f);
            Vector3 localDir = ok ? m_asset.MouthDir : Vector3.forward;
            Fx.Breath(parent, local, localDir, duration, range, Mathf.Max(0.5f, m_scale));
        }

        // ------------------------------------------------------------------ fisher
        /// <summary>
        ///     Every physics step in flight (owner, with its rider aboard): skimming low and fast over water
        ///     soaks the rider in spray and now and then brings up a fish.
        /// </summary>
        private void FisherStep(float dt, Vector3 pos, bool overWater, float waterY, float depth)
        {
            float every = ModConfig.FisherInterval.Value;
            var player = m_localRider;
            float s = Mathf.Max(1f, m_scale);
            if (every <= 0f || player == null || !overWater || depth < 1.5f ||
                pos.y - waterY > Mathf.Max(1.5f, ModConfig.FisherHeight.Value) * s || Mathf.Abs(m_speed) < 8f)
            {
                return;
            }

            m_sprayTimer -= dt;
            if (m_sprayTimer <= 0f)
            {
                m_sprayTimer = 1f;
                try
                {
                    player.GetSEMan().AddStatusEffect(SEMan.s_statusEffectWet, true, 0, 0f);
                }
                catch (Exception)
                {
                    // no spray then
                }
            }

            if (m_fishTimer < 0f)
            {
                m_fishTimer = every * Random.Range(0.5f, 1f);
            }

            m_fishTimer -= dt;
            if (m_fishTimer > 0f)
            {
                return;
            }

            m_fishTimer = every * Random.Range(0.7f, 1.3f);
            CatchFish(player, new Vector3(pos.x, waterY, pos.z) - transform.forward * (1.5f * s));
        }

        private void CatchFish(Player player, Vector3 at)
        {
            var odb = ObjectDB.instance;
            if (odb == null)
            {
                return;
            }

            string[] pool = FishPool(at);
            GameObject prefab = null;
            for (int i = 0; i < 4 && prefab == null; i++)
            {
                prefab = odb.GetItemPrefab(pool[Random.Range(0, pool.Length)]);
            }

            if (prefab == null)
            {
                prefab = odb.GetItemPrefab("Fish1");
            }

            if (prefab == null)
            {
                return;
            }

            SendFx(FxSplash, at, new Vector3(1f, 0f, 0f));
            var inv = player.GetInventory();
            if (inv == null || !inv.CanAddItem(prefab, 1))
            {
                player.Message(MessageHud.MessageType.TopLeft, Texts.Get("fp_msg_fish_full"));
                return;
            }

            inv.AddItem(prefab, 1);
            var drop = prefab.GetComponent<ItemDrop>();
            string fish = prefab.name;
            Sprite icon = null;
            try
            {
                if (drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null)
                {
                    fish = Localization.instance != null
                        ? Localization.instance.Localize(drop.m_itemData.m_shared.m_name)
                        : drop.m_itemData.m_shared.m_name;
                    icon = drop.m_itemData.GetIcon();
                }
            }
            catch (Exception)
            {
                icon = null;
            }

            player.Message(MessageHud.MessageType.TopLeft, Texts.Get("fp_msg_fish", fish), 0, icon);
        }

        /// <summary>The fish of the water below (as the fishing rod finds them in each biome).</summary>
        private static string[] FishPool(Vector3 p)
        {
            var wg = WorldGenerator.instance;
            var biome = wg != null ? wg.GetBiome(p) : Heightmap.Biome.Ocean;
            switch (biome)
            {
                case Heightmap.Biome.Meadows:
                    return new[] { "Fish1", "Fish1", "Fish2" };
                case Heightmap.Biome.BlackForest:
                    return new[] { "Fish2", "Fish1" };
                case Heightmap.Biome.Swamp:
                    return new[] { "Fish5" };
                case Heightmap.Biome.Mountain:
                    return new[] { "Fish4_cave" };
                case Heightmap.Biome.Plains:
                    return new[] { "Fish7", "Fish2" };
                case Heightmap.Biome.Mistlands:
                    return new[] { "Fish9" };
                case Heightmap.Biome.AshLands:
                    return new[] { "Fish11" };
                case Heightmap.Biome.DeepNorth:
                    return new[] { "Fish10", "Fish6" };
                default:
                    return FishOcean;
            }
        }
    }
}
