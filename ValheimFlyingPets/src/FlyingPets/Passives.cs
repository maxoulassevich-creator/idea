using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FlyingPets
{
    /// <summary>
    ///     The passive abilities. Everything is decided from the pets' positions, which every game knows,
    ///     so each game handles its own part: the creature's owner moves it, the rider gets the status
    ///     effects, the owner of a dying creature rolls its drops.
    /// </summary>
    internal static class Passives
    {
        private const int ModeHeld = 1;     // in the raven's talons: no AI, no physics
        private const int ModeFrozen = 2;   // under the raven's shadow: stands still and stares
        private const int ModeFlee = 3;     // undead near a pegasus, serpents near a water dragon: keep away

        private sealed class AiOverride
        {
            public ZNetView View;
            public int Mode;
            public Vector3 From;
            public float Until;
        }

        /// <summary>Creatures grabbed by a raven ridden on this game (we own them while they hang there).</summary>
        internal static readonly HashSet<Character> Held = new HashSet<Character>();

        private static readonly Dictionary<BaseAI, AiOverride> s_ai = new Dictionary<BaseAI, AiOverride>();
        private static readonly Dictionary<Character, float> s_freezeImmune = new Dictionary<Character, float>();
        private static readonly List<BaseAI> s_tmpAi = new List<BaseAI>();
        private static readonly List<Character> s_tmpChars = new List<Character>();

        private static int s_frame = -1;
        private static float s_scanTimer;
        private static float s_rideCheck;
        private static string s_rideKey;
        private static float s_fallTop;
        private static float s_catchReady;
        private static float s_muninnTimer;
        private static float s_livingCheck;

        internal static bool HuginnActive;
        internal static bool UnderWingActive;
        internal static bool ScalesActive;

        // ------------------------------------------------------------------ private game members
        private static bool s_reflected;
        private static AccessTools.FieldRef<BaseAI, float> s_timeSinceHurt;
        private static AccessTools.FieldRef<MonsterAI, Character> s_targetCreature;
        private static AccessTools.FieldRef<MonsterAI, StaticTarget> s_targetStatic;
        private static Func<BaseAI, float, Vector3, bool> s_flee;

        private static void Reflect()
        {
            if (s_reflected)
            {
                return;
            }

            s_reflected = true;
            try
            {
                s_timeSinceHurt = AccessTools.FieldRefAccess<BaseAI, float>("m_timeSinceHurt");
            }
            catch (Exception e)
            {
                FlyingPetsPlugin.Log.LogWarning("BaseAI.m_timeSinceHurt not found (" + e.Message + "): hurt undead flee too");
            }

            try
            {
                s_targetCreature = AccessTools.FieldRefAccess<MonsterAI, Character>("m_targetCreature");
                s_targetStatic = AccessTools.FieldRefAccess<MonsterAI, StaticTarget>("m_targetStatic");
            }
            catch (Exception e)
            {
                FlyingPetsPlugin.Log.LogWarning("MonsterAI targets not found (" + e.Message + ")");
            }

            try
            {
                var m = AccessTools.Method(typeof(BaseAI), "Flee", new[] { typeof(float), typeof(Vector3) });
                if (m != null)
                {
                    s_flee = AccessTools.MethodDelegate<Func<BaseAI, float, Vector3, bool>>(m);
                }
            }
            catch (Exception e)
            {
                FlyingPetsPlugin.Log.LogWarning("BaseAI.Flee not found (" + e.Message + "): the undead just back off");
            }
        }

        // ------------------------------------------------------------------ every frame (from any pet's Update)
        public static void FrameUpdate()
        {
            if (Time.frameCount == s_frame)
            {
                return;
            }

            s_frame = Time.frameCount;
            float now = Time.time;
            s_scanTimer -= Time.deltaTime;
            if (s_scanTimer <= 0f)
            {
                s_scanTimer = 0.2f;
                try
                {
                    Scan(now);
                }
                catch (Exception e)
                {
                    FlyingPetsPlugin.Log.LogDebug("Ability scan failed: " + e.Message);
                }
            }

            try
            {
                LocalPlayer(now);
                PreyWatch.Tick(Time.deltaTime);
            }
            catch (Exception e)
            {
                FlyingPetsPlugin.Log.LogDebug("Ability update failed: " + e.Message);
            }
        }

        /// <summary>
        ///     Pets that are around: the undead flee from pegasi, serpents from water dragons, prey freezes
        ///     under flying ravens.
        /// </summary>
        private static void Scan(float now)
        {
            // forget what ran out (the held ones are released by their raven)
            s_tmpAi.Clear();
            foreach (var kv in s_ai)
            {
                if (kv.Key == null || kv.Value.View == null || (kv.Value.Mode != ModeHeld && kv.Value.Until < now))
                {
                    s_tmpAi.Add(kv.Key);
                }
            }

            foreach (var ai in s_tmpAi)
            {
                s_ai.Remove(ai);
            }

            if (s_freezeImmune.Count > 64)
            {
                s_tmpChars.Clear();
                foreach (var kv in s_freezeImmune)
                {
                    if (kv.Key == null || kv.Value < now)
                    {
                        s_tmpChars.Add(kv.Key);
                    }
                }

                foreach (var c in s_tmpChars)
                {
                    s_freezeImmune.Remove(c);
                }
            }

            float lightRadius = ModConfig.ValhallaRadius.Value;
            float truceRadius = ModConfig.SeaTruceRadius.Value;
            float freeze = ModConfig.PreyFreezeSeconds.Value;
            foreach (var pet in FlyingPet.Instances)
            {
                if (pet == null || !pet.IsAround)
                {
                    continue;
                }

                Vector3 pp = pet.transform.position;
                if (lightRadius > 0f && pet.Has(PetAbility.ValhallaLight))
                {
                    s_tmpChars.Clear();
                    Character.GetCharactersInRange(pp, lightRadius * Mathf.Max(1f, pet.Scale), s_tmpChars);
                    foreach (var c in s_tmpChars)
                    {
                        if (c == null || c.IsPlayer() || c.GetFaction() != Character.Faction.Undead || c.IsBoss() ||
                            c.IsTamed() || c.IsDead())
                        {
                            continue;
                        }

                        var ai = c.GetBaseAI() as MonsterAI;
                        if (ai != null)
                        {
                            Set(ai, c, ModeFlee, pp, now + 0.6f);
                        }
                    }
                }

                if (truceRadius > 0f && pet.Has(PetAbility.SeaTruce))
                {
                    s_tmpChars.Clear();
                    Character.GetCharactersInRange(pp, truceRadius * Mathf.Max(1f, pet.Scale), s_tmpChars);
                    foreach (var c in s_tmpChars)
                    {
                        if (c == null || c.IsPlayer() || c.GetFaction() != Character.Faction.SeaMonsters || c.IsBoss() ||
                            c.IsTamed() || c.IsDead())
                        {
                            continue;
                        }

                        var ai = c.GetBaseAI() as MonsterAI;
                        if (ai != null)
                        {
                            Set(ai, c, ModeFlee, pp, now + 0.6f);
                        }
                    }
                }

                if (freeze > 0f && pet.IsFlying && pet.Has(PetAbility.PreyShadow))
                {
                    float reach = 6f * Mathf.Max(1f, pet.Scale);
                    foreach (var c in Character.GetAllCharacters())
                    {
                        if (c == null || !IsPrey(c))
                        {
                            continue;
                        }

                        Vector3 cp = c.transform.position;
                        float dy = pp.y - cp.y;
                        float dx = cp.x - pp.x, dz = cp.z - pp.z;
                        if (dy < 1.5f || dy > 45f || dx * dx + dz * dz > reach * reach)
                        {
                            continue;
                        }

                        float immune;
                        if (s_freezeImmune.TryGetValue(c, out immune) && immune > now)
                        {
                            continue;
                        }

                        var ai = c.GetBaseAI();
                        if (ai != null && Set(ai, c, ModeFrozen, pp, now + freeze))
                        {
                            s_freezeImmune[c] = now + freeze + 4f;
                        }
                    }
                }
            }
        }

        private static bool IsPrey(Character c)
        {
            if (c.IsPlayer() || c.IsBoss() || c.IsTamed() || c.IsDead())
            {
                return false;
            }

            return c.GetFaction() == Character.Faction.AnimalsVeg || c.GetBaseAI() is AnimalAI;
        }

        /// <summary>Overrides the creature's AI (a stronger mode is never replaced by a weaker one).</summary>
        private static bool Set(BaseAI ai, Character c, int mode, Vector3 from, float until)
        {
            AiOverride o;
            if (s_ai.TryGetValue(ai, out o))
            {
                if (o.Mode < mode)
                {
                    return false;
                }

                if (o.Mode == mode)
                {
                    o.From = from;
                    o.Until = Mathf.Max(o.Until, until);
                    return mode != ModeFrozen;
                }
            }

            var view = c.GetComponent<ZNetView>();
            if (view == null)
            {
                return false;
            }

            s_ai[ai] = new AiOverride { View = view, Mode = mode, From = from, Until = until };
            return true;
        }

        // ------------------------------------------------------------------ talons (called by the raven)
        internal static void Hold(Character c)
        {
            Held.Add(c);
            var ai = c.GetBaseAI();
            if (ai != null)
            {
                Set(ai, c, ModeHeld, c.transform.position, float.MaxValue);
                ai.StopMoving();
            }
        }

        internal static void Unhold(Character c)
        {
            if (c == null)
            {
                Held.RemoveWhere(x => x == null);
                return;
            }

            Held.Remove(c);
            var ai = c.GetBaseAI();
            AiOverride o;
            if (ai != null && s_ai.TryGetValue(ai, out o) && o.Mode == ModeHeld)
            {
                s_ai.Remove(ai);
            }
        }

        internal static bool IsHeld(Character c)
        {
            return Held.Count != 0 && Held.Contains(c);
        }

        // ------------------------------------------------------------------ AI patch body
        /// <summary>Prefix of MonsterAI/AnimalAI.UpdateAI. False = skip the creature's own AI this frame.</summary>
        internal static bool OverrideAi(BaseAI ai, float dt, ref bool result)
        {
            if (s_ai.Count == 0)
            {
                return true;
            }

            AiOverride o;
            if (!s_ai.TryGetValue(ai, out o) || o.View == null || !o.View.IsValid() || !o.View.IsOwner())
            {
                return true;
            }

            float now = Time.time;
            switch (o.Mode)
            {
                case ModeHeld:
                    ai.StopMoving();
                    result = true;
                    return false;

                case ModeFrozen:
                {
                    if (now > o.Until)
                    {
                        s_ai.Remove(ai);
                        return true;
                    }

                    ai.StopMoving();
                    Vector3 look = o.From - ai.transform.position;
                    look.y = 0f;
                    if (look.sqrMagnitude > 0.01f)
                    {
                        ai.LookTowards(look.normalized);
                    }

                    result = true;
                    return false;
                }

                case ModeFlee:
                {
                    if (now > o.Until || ai.IsSleeping())
                    {
                        return true;
                    }

                    Reflect();
                    if (s_timeSinceHurt != null && s_timeSinceHurt(ai) < 20f)
                    {
                        return true; // provoked: it fights back
                    }

                    var monster = ai as MonsterAI;
                    if (monster != null && s_targetCreature != null)
                    {
                        s_targetCreature(monster) = null;
                        s_targetStatic(monster) = null;
                    }

                    bool fled = false;
                    if (s_flee != null)
                    {
                        try
                        {
                            s_flee(ai, dt, o.From);
                            fled = true;
                        }
                        catch (Exception)
                        {
                            s_flee = null;
                        }
                    }

                    if (!fled)
                    {
                        Vector3 away = ai.transform.position - o.From;
                        away.y = 0f;
                        ai.MoveTowards(away.sqrMagnitude > 0.01f ? away.normalized : ai.transform.forward, true);
                    }

                    result = true;
                    return false;
                }
            }

            return true;
        }

        // ------------------------------------------------------------------ the local player
        private static void LocalPlayer(float now)
        {
            var player = Player.m_localPlayer;
            HuginnActive = false;
            UnderWingActive = false;
            ScalesActive = false;
            if (player == null)
            {
                s_rideKey = null;
                return;
            }

            var mount = FlyingPet.MountOf(player);
            var asset = mount != null ? mount.Asset : null;

            // the saddle's status effect (the pegasus' carry weight lives in it)
            string key = asset != null ? asset.Key : null;
            s_rideCheck -= Time.deltaTime;
            if (key != s_rideKey || s_rideCheck <= 0f)
            {
                s_rideCheck = 1f;
                foreach (var a in PetLibrary.Pets)
                {
                    if (a.Abilities.Count > 0 && (a.Key == key || a.Key == s_rideKey))
                    {
                        AbilityEffects.SetRide(player, a, a.Key == key);
                    }
                }

                s_rideKey = key;
            }

            if (mount != null)
            {
                HuginnActive = mount.IsFlying && mount.Has(PetAbility.Huginn) && ModConfig.HuginnMultiplier.Value > 1f;
                UnderWingActive = mount.Has(PetAbility.UnderWing) && ModConfig.UnderWing.Value;
                if (UnderWingActive)
                {
                    var seman = player.GetSEMan();
                    if (seman.HaveStatusEffect(SEMan.s_statusEffectWet))
                    {
                        seman.RemoveStatusEffect(SEMan.s_statusEffectWet, true);
                    }

                    if (seman.HaveStatusEffect(SEMan.s_statusEffectCold))
                    {
                        seman.RemoveStatusEffect(SEMan.s_statusEffectCold, true);
                    }

                    if (seman.HaveStatusEffect(SEMan.s_statusEffectFreezing))
                    {
                        seman.RemoveStatusEffect(SEMan.s_statusEffectFreezing, true);
                    }
                }

                ScalesActive = mount.Has(PetAbility.SteamingScales) && ModConfig.SteamingScales.Value;
                if (ScalesActive && player.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectBurning))
                {
                    player.GetSEMan().RemoveStatusEffect(SEMan.s_statusEffectBurning, true);
                }
            }

            LivingWater(player, mount);
            ValkyrieCatch(player, mount, now);
            Muninn(player, mount, now);
        }

        /// <summary>The status effects the abilities keep off the local player (under the wing, steaming scales).</summary>
        internal static bool Blocks(int hash)
        {
            if (UnderWingActive &&
                (hash == SEMan.s_statusEffectWet || hash == SEMan.s_statusEffectCold || hash == SEMan.s_statusEffectFreezing))
            {
                return true;
            }

            return ScalesActive && hash == SEMan.s_statusEffectBurning;
        }

        // ------------------------------------------------------------------ living water
        private static void LivingWater(Player player, FlyingPet mount)
        {
            s_livingCheck -= Time.deltaTime;
            if (s_livingCheck > 0f)
            {
                return;
            }

            s_livingCheck = 0.5f;
            bool on = false;
            if (ModConfig.LivingWaterMultiplier.Value > 1f && player.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectWet))
            {
                if (mount != null)
                {
                    on = mount.Has(PetAbility.LivingWater);
                }
                else
                {
                    float r = Mathf.Max(1f, ModConfig.LivingWaterRadius.Value);
                    Vector3 p = player.transform.position;
                    foreach (var pet in FlyingPet.Instances)
                    {
                        float reach = pet != null ? r * Mathf.Max(1f, pet.Scale) : 0f;
                        if (pet != null && pet.IsAround && pet.Has(PetAbility.LivingWater) &&
                            (pet.transform.position - p).sqrMagnitude < reach * reach)
                        {
                            on = true;
                            break;
                        }
                    }
                }
            }

            AbilityEffects.SetLiving(player, on);
        }

        // ------------------------------------------------------------------ valkyrie's catch
        private static void ValkyrieCatch(Player player, FlyingPet mount, float now)
        {
            Vector3 p = player.transform.position;
            if (mount != null || player.IsAttached() || player.IsOnGround() || player.IsSwimming() || player.InWater() ||
                player.IsDead() || player.IsTeleporting())
            {
                s_fallTop = p.y;
                return;
            }

            s_fallTop = Mathf.Max(s_fallTop, p.y);
            if (!ModConfig.ValkyrieCatch.Value || now < s_catchReady || p.y > 3000f || s_fallTop - p.y < 2.5f)
            {
                return;
            }

            var body = player.GetComponent<Rigidbody>();
            if (body == null || body.linearVelocity.y > -9f)
            {
                return;
            }

            float water;
            float ground = Util.GroundY(p, 0.5f, out water);
            if (water > ground)
            {
                return; // a splash does not hurt
            }

            if (p.y - ground < 3f || s_fallTop - ground < 8f)
            {
                return; // too late to swoop in, or not high enough to get hurt
            }

            var pet = FlyingPet.FindOwnedBy(player.GetPlayerID());
            if (pet == null || !pet.Has(PetAbility.ValkyrieCatch) || !pet.TryCatch(player))
            {
                return;
            }

            float cd = Mathf.Max(5f, ModConfig.CatchCooldown.Value);
            s_catchReady = now + cd;
            s_fallTop = p.y;
            AbilityEffects.Timer(player, AbilityEffects.Catch, cd);
        }

        // ------------------------------------------------------------------ muninn: places on the map
        private const int CatNone = 0, CatDungeon = 1, CatTrader = 2, CatRunestone = 3, CatAltar = 4;
        private static FieldInfo s_allLocations;
        private static MethodInfo s_addPin;
        private static MethodInfo s_havePinInRange;
        private static FieldInfo s_pins;
        private static bool s_mapReflected;
        private static readonly Dictionary<int, int> s_locationCategory = new Dictionary<int, int>();
        private static readonly HashSet<long> s_pinnedHere = new HashSet<long>();

        private static void Muninn(Player player, FlyingPet mount, float now)
        {
            s_muninnTimer -= Time.deltaTime;
            if (s_muninnTimer > 0f || !ModConfig.MuninnPins.Value || Minimap.instance == null)
            {
                return;
            }

            s_muninnTimer = 1.5f;
            FlyingPet raven = mount != null && mount.Has(PetAbility.Muninn) ? mount : null;
            if (raven == null)
            {
                var own = FlyingPet.FindOwnedBy(player.GetPlayerID());
                if (own != null && own.Has(PetAbility.Muninn) && own.RiderId == 0L &&
                    Vector3.Distance(own.transform.position, player.transform.position) < 80f)
                {
                    raven = own;
                }
            }

            if (raven == null)
            {
                return;
            }

            ReflectMap();
            var all = s_allLocations != null ? s_allLocations.GetValue(null) as List<Location> : null;
            if (all == null)
            {
                return;
            }

            Vector3 center = raven.transform.position;
            float range = Mathf.Max(20f, ModConfig.MuninnRange.Value);
            int budget = 2; // a dungeon can be big: look into at most two new places per tick
            foreach (var loc in all)
            {
                if (loc == null)
                {
                    continue;
                }

                Vector3 lp = loc.transform.position;
                float dx = lp.x - center.x, dz = lp.z - center.z;
                if (dx * dx + dz * dz > range * range || lp.y > 3000f)
                {
                    continue;
                }

                long cell = ((long)Mathf.RoundToInt(lp.x / 4f) << 32) ^ (uint)Mathf.RoundToInt(lp.z / 4f);
                if (s_pinnedHere.Contains(cell))
                {
                    continue;
                }

                int id = loc.GetInstanceID();
                int cat;
                if (!s_locationCategory.TryGetValue(id, out cat))
                {
                    if (budget-- <= 0)
                    {
                        continue;
                    }

                    cat = Category(loc);
                    s_locationCategory[id] = cat;
                }

                s_pinnedHere.Add(cell);
                if (cat == CatNone || HavePin(lp))
                {
                    continue;
                }

                string label = Texts.Get(cat == CatDungeon ? "fp_pin_dungeon" : cat == CatTrader ? "fp_pin_trader"
                    : cat == CatRunestone ? "fp_pin_runestone" : "fp_pin_altar");
                var type = cat == CatDungeon ? Minimap.PinType.Icon3 : cat == CatTrader ? Minimap.PinType.Icon1
                    : cat == CatRunestone ? Minimap.PinType.Icon4 : Minimap.PinType.Boss;
                if (AddPin(lp, type, label))
                {
                    player.Message(MessageHud.MessageType.TopLeft, Texts.Get("fp_msg_muninn", label));
                }
            }
        }

        private static void ReflectMap()
        {
            if (s_mapReflected)
            {
                return;
            }

            s_mapReflected = true;
            s_allLocations = AccessTools.Field(typeof(Location), "s_allLocations");
            s_havePinInRange = AccessTools.Method(typeof(Minimap), "HavePinInRange", new[] { typeof(Vector3), typeof(float) });
            s_pins = AccessTools.Field(typeof(Minimap), "m_pins");
            foreach (var m in typeof(Minimap).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.Name != "AddPin")
                {
                    continue;
                }

                var ps = m.GetParameters();
                if (ps.Length >= 5 && ps[0].ParameterType == typeof(Vector3) && ps[1].ParameterType == typeof(Minimap.PinType) &&
                    ps[2].ParameterType == typeof(string) && ps[3].ParameterType == typeof(bool) && ps[4].ParameterType == typeof(bool))
                {
                    s_addPin = m;
                    break;
                }
            }

            if (s_allLocations == null || s_addPin == null)
            {
                FlyingPetsPlugin.Log.LogWarning("Muninn cannot read the map in this game version (locations: " +
                                                (s_allLocations != null) + ", AddPin: " + (s_addPin != null) + ")");
            }
        }

        private static int Category(Location loc)
        {
            string n = loc.name;
            if (n.StartsWith("Eikthyrnir") || n.StartsWith("GDKing") || n.StartsWith("Bonemass") || n.StartsWith("Dragonqueen") ||
                n.StartsWith("GoblinKing") || n.StartsWith("Mistlands_DvergrBossEntrance") || n.StartsWith("FaderLocation") ||
                loc.GetComponentInChildren<OfferingBowl>(true) != null)
            {
                return CatAltar;
            }

            if (n.StartsWith("Vendor_") || n.StartsWith("Hildir_camp") || n.StartsWith("BogWitch") ||
                loc.GetComponentInChildren<Trader>(true) != null)
            {
                return CatTrader;
            }

            if (loc.m_hasInterior)
            {
                return CatDungeon;
            }

            if (n.StartsWith("Runestone") || loc.GetComponentInChildren<RuneStone>(true) != null ||
                loc.GetComponentInChildren<Vegvisir>(true) != null)
            {
                return CatRunestone;
            }

            return CatNone;
        }

        private static bool HavePin(Vector3 p)
        {
            var map = Minimap.instance;
            try
            {
                if (s_havePinInRange != null)
                {
                    return (bool)s_havePinInRange.Invoke(map, new object[] { p, 15f });
                }

                var pins = s_pins != null ? s_pins.GetValue(map) as List<Minimap.PinData> : null;
                if (pins != null)
                {
                    foreach (var pin in pins)
                    {
                        float dx = pin.m_pos.x - p.x, dz = pin.m_pos.z - p.z;
                        if (dx * dx + dz * dz < 15f * 15f)
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // fall through: better a second pin than none
            }

            return false;
        }

        private static bool AddPin(Vector3 p, Minimap.PinType type, string label)
        {
            if (s_addPin == null)
            {
                return false;
            }

            try
            {
                var ps = s_addPin.GetParameters();
                var args = new object[ps.Length];
                args[0] = p;
                args[1] = type;
                args[2] = label;
                args[3] = true; // saved with the map
                args[4] = false;
                for (int i = 5; i < ps.Length; i++)
                {
                    object def = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
                    if (def == null || def == DBNull.Value)
                    {
                        def = ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null;
                    }

                    args[i] = def;
                }

                s_addPin.Invoke(Minimap.instance, args);
                return true;
            }
            catch (Exception e)
            {
                FlyingPetsPlugin.Log.LogWarning("Muninn could not add a pin: " + e.Message);
                s_addPin = null;
                return false;
            }
        }

        // ------------------------------------------------------------------ ravens' feast
        /// <summary>Is a raven with the feast near this point (horizontally, any height up to 80 m)?</summary>
        internal static bool FeastNear(Vector3 p)
        {
            float r = ModConfig.FeastRadius.Value;
            if (r <= 0f || ModConfig.FeastMultiplier.Value <= 1f)
            {
                return false;
            }

            foreach (var pet in FlyingPet.Instances)
            {
                if (pet == null || !pet.IsAround || !pet.Has(PetAbility.RavenFeast))
                {
                    continue;
                }

                Vector3 q = pet.transform.position;
                float dx = q.x - p.x, dz = q.z - p.z;
                if (dx * dx + dz * dz <= r * r && Mathf.Abs(q.y - p.y) < 80f)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
