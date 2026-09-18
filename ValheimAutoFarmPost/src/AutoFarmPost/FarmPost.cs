using System;
using System.Collections.Generic;
using UnityEngine;

namespace AutoFarmPost
{
    /// <summary>
    ///     The brain of the farm post. Runs only on the machine that owns the piece, so it works
    ///     the same in single player and on a dedicated server.
    ///
    ///     Container layout: the first rows are the seed area, the rest is the harvest area.
    ///     With the default settings that is 8x2 = 16 seed slots and 8x2 = 16 harvest slots.
    /// </summary>
    public class FarmPost : MonoBehaviour
    {
        private struct Pending
        {
            public GameObject Plant;
            public Vector3 Pos;
            public Quaternion Rot;
        }

        private static readonly int MythicKey = "autofarm_mythic".GetStableHashCode();
        private static readonly Collider[] Overlap = new Collider[64];
        private static readonly Dictionary<string, Vector2[]> GridCache = new Dictionary<string, Vector2[]>();
        private static int _spaceMask = -1;
        private static int _groundMask = -1;

        private readonly List<Pickable> _near = new List<Pickable>();
        private readonly List<Pending> _pending = new List<Pending>();
        private readonly List<string> _seedNames = new List<string>();
        private readonly List<GameObject> _usableSeeds = new List<GameObject>();
        private readonly Dictionary<string, float> _splitCredit = new Dictionary<string, float>();
        private readonly List<Container> _chests = new List<Container>();
        private readonly List<ItemDrop.ItemData> _stacks = new List<ItemDrop.ItemData>();

        private ZNetView _nview;
        private Container _container;
        private float _timer;
        private float _nextEmptyScan;
        private int _lastHarvest;
        private int _lastPlant;
        private float _minGrowRadius = 0.5f;
        private bool _outputFull;
        private bool _noSeeds;
        private int _mythicFound = -1;

        /// <summary>A real, networked post - not a build preview.</summary>
        public bool IsLive
        {
            get { return _nview != null && _nview.IsValid(); }
        }

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _container = GetComponent<Container>();
            _timer = UnityEngine.Random.Range(0f, 2f);
            FarmPostRegistry.Add(this);
        }

        private void OnDestroy()
        {
            FarmPostRegistry.Remove(this);
        }

        private void Update()
        {
            if (_nview == null || _container == null)
            {
                return;
            }

            // Not valid = build preview. Not owner = somebody else is running this piece.
            if (!_nview.IsValid() || !_nview.IsOwner())
            {
                return;
            }

            _timer += Time.deltaTime;
            if (_timer < Mathf.Max(1f, ModConfig.TickInterval.Value))
            {
                return;
            }

            _timer = 0f;

            try
            {
                Tick();
            }
            catch (Exception e)
            {
                AutoFarmPlugin.Log.LogWarning("Farm post cycle failed: " + e);
            }
        }

        private void Tick()
        {
            Inventory inv = _container.GetInventory();
            if (inv == null)
            {
                return;
            }

            FarmData.EnsureBuilt();

            int seedY0, seedY1, outY0, outY1;
            GetRows(inv, out seedY0, out seedY1, out outY0, out outY1);

            _outputFull = false;

            // first make room and restock from the chests around us, then farm
            if (ModConfig.UseChests.Value)
            {
                Logistics(inv, seedY0, seedY1, outY0, outY1);
            }

            PrepareSeeds(inv, seedY0, seedY1);

            int harvested = Harvest(inv, seedY0, seedY1, outY0, outY1);
            int planted = Replant(inv, seedY0, seedY1);

            if (ModConfig.PlantOnEmptyGround.Value)
            {
                planted += PlantEmptyGround(inv, seedY0, seedY1);
            }

            _lastHarvest = harvested;
            _lastPlant = planted;

            if (harvested > 0 || planted > 0)
            {
                Util.SaveContainer(_container);
            }
        }

        /// <summary>Seed rows come first, the harvest rows fill the rest of the container.</summary>
        private static void GetRows(Inventory inv, out int seedY0, out int seedY1, out int outY0, out int outY1)
        {
            int height = Mathf.Max(2, inv.GetHeight());
            int seedRows = Mathf.Clamp(ModConfig.SeedRows.Value, 1, height - 1);

            seedY0 = 0;
            seedY1 = seedRows - 1;
            outY0 = seedRows;
            outY1 = height - 1;
        }

        /// <summary>Which seeds are currently available in the seed rows.</summary>
        private void PrepareSeeds(Inventory inv, int seedY0, int seedY1)
        {
            _seedNames.Clear();
            _usableSeeds.Clear();
            InventoryUtils.CollectItemNames(inv, seedY0, seedY1, _seedNames);

            for (int i = 0; i < _seedNames.Count; i++)
            {
                GameObject plantPrefab;
                if (FarmData.SeedToPlant.TryGetValue(_seedNames[i], out plantPrefab))
                {
                    _usableSeeds.Add(plantPrefab);
                }
            }

            _minGrowRadius = 0.5f;
            for (int i = 0; i < _usableSeeds.Count; i++)
            {
                Plant plant = _usableSeeds[i].GetComponent<Plant>();
                if (plant != null && plant.m_growRadius > 0f)
                {
                    _minGrowRadius = i == 0 ? plant.m_growRadius : Mathf.Min(_minGrowRadius, plant.m_growRadius);
                }
            }

            _noSeeds = _usableSeeds.Count == 0;
        }

        private int Harvest(Inventory inv, int seedY0, int seedY1, int outY0, int outY1)
        {
            if (!ModConfig.HarvestCrops.Value && !ModConfig.HarvestBerries.Value &&
                ModConfig.IncludeSet.Count == 0)
            {
                return 0;
            }

            Vector3 center = transform.position;
            _near.Clear();
            PickableRegistry.CollectNear(center, ModConfig.HarvestRadius.Value, _near);

            int max = ModConfig.MaxHarvestPerTick.Value;
            int count = 0;

            for (int i = 0; i < _near.Count && count < max; i++)
            {
                Pickable pickable = _near[i];
                if (pickable == null || !FarmData.IsHarvestable(pickable))
                {
                    continue;
                }

                ZNetView nview = pickable.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid() || PickableUtil.IsPicked(pickable, nview))
                {
                    continue;
                }

                GameObject itemPrefab = pickable.m_itemPrefab;
                if (itemPrefab == null)
                {
                    continue;
                }

                int amount = Mathf.Max(1, pickable.m_amount);

                int roomSeeds = InventoryUtils.Room(inv, itemPrefab, seedY0, seedY1);
                int roomOutput = InventoryUtils.Room(inv, itemPrefab, outY0, outY1);
                if (roomSeeds + roomOutput < amount)
                {
                    // Nothing is picked when there is no room - the crop simply stays in the field.
                    _outputFull = true;
                    break;
                }

                int toSeeds, toOutput;
                SplitHarvest(itemPrefab, amount, out toSeeds, out toOutput);

                // whatever does not fit in its own half goes to the other one
                if (toSeeds > roomSeeds)
                {
                    toOutput += toSeeds - roomSeeds;
                    toSeeds = roomSeeds;
                }

                if (toOutput > roomOutput)
                {
                    toSeeds += toOutput - roomOutput;
                    toOutput = roomOutput;
                }

                // Remember the spot before the plant disappears.
                GameObject plantPrefab;
                if (ModConfig.ReplantEnabled.Value && _pending.Count < 256 &&
                    FarmData.CropToPlant.TryGetValue(Util.PrefabName(pickable.gameObject), out plantPrefab))
                {
                    Pending pending;
                    pending.Plant = plantPrefab;
                    pending.Pos = pickable.transform.position;
                    pending.Rot = pickable.transform.rotation;
                    _pending.Add(pending);
                }

                int stored = 0;
                if (toSeeds > 0)
                {
                    stored += InventoryUtils.Store(inv, itemPrefab, toSeeds, seedY0, seedY1);
                }

                if (toOutput > 0)
                {
                    stored += InventoryUtils.Store(inv, itemPrefab, toOutput, outY0, outY1);
                }

                if (stored <= 0)
                {
                    _outputFull = true;
                    break;
                }

                PickableUtil.Pick(pickable, nview);
                count++;

                if (ModConfig.MythicEnabled.Value)
                {
                    TryMythicFind(inv, seedY0, seedY1, outY0, outY1, pickable.transform.position);
                }
            }

            return count;
        }

        /// <summary>
        ///     Chests around the post: the harvest is moved out so the post never clogs up, and
        ///     the seed rows are restocked. With a chest next to it the post needs no hand work
        ///     at all.
        /// </summary>
        private void Logistics(Inventory inv, int seedY0, int seedY1, int outY0, int outY1)
        {
            _chests.Clear();
            ContainerRegistry.CollectNear(transform.position, ModConfig.ChestRadius.Value, _chests);
            if (_chests.Count == 0)
            {
                return;
            }

            int budget = Mathf.Max(1, ModConfig.MaxTransferPerTick.Value);

            if (ModConfig.PushHarvestToChests.Value)
            {
                budget -= PushHarvest(inv, outY0, outY1, budget);
            }

            if (budget > 0 && ModConfig.PullSeedsFromChests.Value)
            {
                PullSeeds(inv, seedY0, seedY1, budget);
            }
        }

        private int PushHarvest(Inventory inv, int outY0, int outY1, int budget)
        {
            float threshold = Mathf.Clamp01(ModConfig.PushThresholdPercent.Value / 100f);
            if (InventoryUtils.Fullness(inv, outY0, outY1) < threshold)
            {
                return 0;
            }

            _stacks.Clear();
            InventoryUtils.CollectItems(inv, outY0, outY1, _stacks);

            int moved = 0;

            // from the last slots backwards, so the first rows keep showing the latest harvest
            for (int i = _stacks.Count - 1; i >= 0 && moved < budget; i--)
            {
                ItemDrop.ItemData item = _stacks[i];

                // first into a chest that already holds this item, then into any chest
                for (int pass = 0; pass < 2 && moved < budget && item.m_stack > 0; pass++)
                {
                    for (int c = 0; c < _chests.Count && moved < budget && item.m_stack > 0; c++)
                    {
                        Container chest = _chests[c];
                        Inventory target = Claim(chest);
                        if (target == null)
                        {
                            continue;
                        }

                        bool known = InventoryUtils.CountItem(target, item.m_shared.m_name, 0,
                                         target.GetHeight() - 1) > 0;
                        bool wantKnown = pass == 0;
                        if (known != wantKnown)
                        {
                            continue;
                        }

                        int n = InventoryUtils.Transfer(inv, item, target, 0, target.GetHeight() - 1,
                            budget - moved);
                        if (n > 0)
                        {
                            moved += n;
                            Util.SaveContainer(chest);
                        }
                    }
                }
            }

            return moved;
        }

        private int PullSeeds(Inventory inv, int seedY0, int seedY1, int budget)
        {
            // crops we are already using as seed - those may be restocked as well
            _seedNames.Clear();
            InventoryUtils.CollectItemNames(inv, seedY0, seedY1, _seedNames);

            int moved = 0;

            for (int c = 0; c < _chests.Count && moved < budget; c++)
            {
                Container chest = _chests[c];
                Inventory source = Claim(chest);
                if (source == null)
                {
                    continue;
                }

                _stacks.Clear();
                InventoryUtils.CollectItems(source, 0, source.GetHeight() - 1, _stacks);

                for (int i = 0; i < _stacks.Count && moved < budget; i++)
                {
                    ItemDrop.ItemData item = _stacks[i];
                    if (item.m_shared == null)
                    {
                        continue;
                    }

                    string name = item.m_shared.m_name;
                    if (!FarmData.IsPlantableName(name))
                    {
                        continue;
                    }

                    if (!FarmData.IsSeedName(name))
                    {
                        // a plain vegetable is only taken when the post already plants it,
                        // otherwise it would empty the food storage into the field
                        if (ModConfig.PullOnlySeedItems.Value || !_seedNames.Contains(name))
                        {
                            continue;
                        }
                    }

                    // keep a modest stock only, or the post would drag the whole storage into the field
                    int have = InventoryUtils.CountItem(inv, name, seedY0, seedY1);
                    int want = ModConfig.SeedStockTarget.Value - have;
                    if (want <= 0)
                    {
                        continue;
                    }

                    int n = InventoryUtils.Transfer(source, item, inv, seedY0, seedY1,
                        Mathf.Min(budget - moved, want));
                    if (n > 0)
                    {
                        moved += n;
                        Util.SaveContainer(chest);
                    }
                }
            }

            return moved;
        }

        private static Inventory Claim(Container chest)
        {
            if (chest == null)
            {
                return null;
            }

            ZNetView nview = chest.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
            {
                return null;
            }

            if (!nview.IsOwner())
            {
                nview.ClaimOwnership();
            }

            return chest.GetInventory();
        }

        /// <summary>
        ///     The rare find. Rolled once per harvested plant; the fruit goes into the harvest
        ///     rows, or into the seed rows if those are full.
        /// </summary>
        private void TryMythicFind(Inventory inv, int seedY0, int seedY1, int outY0, int outY1, Vector3 where)
        {
            GameObject prefab = MythicFruit.Prefab;
            if (prefab == null || ModConfig.MythicChance.Value <= 0f)
            {
                return;
            }

            if (UnityEngine.Random.Range(0f, 100f) > ModConfig.MythicChance.Value)
            {
                return;
            }

            int stored = InventoryUtils.Store(inv, prefab, 1, outY0, outY1);
            if (stored <= 0)
            {
                stored = InventoryUtils.Store(inv, prefab, 1, seedY0, seedY1);
            }

            if (stored <= 0)
            {
                return;
            }

            if (_mythicFound < 0)
            {
                _mythicFound = ReadMythicCount();
            }

            _mythicFound++;
            WriteMythicCount(_mythicFound);
            Announce(where);
        }

        private static void Announce(Vector3 where)
        {
            try
            {
                Player player = Player.m_localPlayer;
                if (player == null || (player.transform.position - where).sqrMagnitude > 900f)
                {
                    return;
                }

                MessageHud hud = MessageHud.instance;
                if (hud != null)
                {
                    hud.ShowMessage(MessageHud.MessageType.TopLeft, Util.Localize("$msg_autofarm_mythic"));
                }
            }
            catch (Exception)
            {
                // feedback is optional
            }
        }

        private int ReadMythicCount()
        {
            try
            {
                ZDO zdo = _nview.GetZDO();
                return zdo != null ? zdo.GetInt(MythicKey, 0) : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private void WriteMythicCount(int value)
        {
            try
            {
                ZDO zdo = _nview.GetZDO();
                if (zdo != null)
                {
                    zdo.Set(MythicKey, value);
                }
            }
            catch (Exception)
            {
                // not worth a log line
            }
        }

        /// <summary>
        ///     Decides where a harvested stack goes.
        ///
        ///     Seeds go to the seed rows in full - they are only useful for planting. Vegetables
        ///     and berries that can themselves be planted are shared between the two halves
        ///     (half by default), so the post keeps growing the seed version of the crop while
        ///     still filling the harvest rows. Anything that cannot be planted goes down in full.
        ///
        ///     Most crops drop a single item, so an exact half is impossible per pick; the
        ///     remainder is carried over and the share comes out exact over a few harvests.
        /// </summary>
        private void SplitHarvest(GameObject itemPrefab, int amount, out int toSeeds, out int toOutput)
        {
            toSeeds = 0;
            toOutput = amount;

            if (!FarmData.IsPlantable(itemPrefab))
            {
                return;
            }

            if (FarmData.IsSeedItem(itemPrefab) && ModConfig.SeedsToSeedRows.Value)
            {
                toSeeds = amount;
                toOutput = 0;
                return;
            }

            float share = Mathf.Clamp01(ModConfig.SeedShare.Value);
            if (share <= 0f)
            {
                return;
            }

            if (share >= 1f)
            {
                toSeeds = amount;
                toOutput = 0;
                return;
            }

            string key = Util.PrefabName(itemPrefab);
            float credit;
            if (!_splitCredit.TryGetValue(key, out credit))
            {
                credit = share;          // the very first pick already counts, so seeding starts at once
            }

            credit += amount * share;
            int up = Mathf.FloorToInt(credit + 0.0001f);
            if (up > amount)
            {
                up = amount;
            }

            _splitCredit[key] = credit - up;
            toSeeds = up;
            toOutput = amount - up;
        }

        /// <summary>
        ///     Puts a new seed on every spot we just harvested. Spots that do not fit into this
        ///     cycle stay in the queue for the next one.
        /// </summary>
        private int Replant(Inventory inv, int seedY0, int seedY1)
        {
            int max = ModConfig.MaxPlantPerTick.Value;
            int planted = 0;

            while (_pending.Count > 0 && planted < max)
            {
                Pending pending = _pending[0];
                _pending.RemoveAt(0);

                if (TryPlant(inv, pending.Plant, pending.Pos, pending.Rot, seedY0, seedY1))
                {
                    planted++;
                }
            }

            return planted;
        }

        /// <summary>Fills free cultivated ground inside the planting radius.</summary>
        private int PlantEmptyGround(Inventory inv, int seedY0, int seedY1)
        {
            if (Time.time < _nextEmptyScan)
            {
                return 0;
            }

            if (_usableSeeds.Count == 0)
            {
                _nextEmptyScan = Time.time + ModConfig.EmptyScanCooldown;
                return 0;
            }

            Vector3 center = transform.position;
            float radius = ModConfig.PlantRadius.Value;
            Vector2[] offsets = GetOffsets(radius, Mathf.Max(0.5f, ModConfig.PlantSpacing.Value),
                ModConfig.Pattern.Value);
            int max = ModConfig.MaxPlantPerTick.Value;
            int planted = 0;

            for (int i = 0; i < offsets.Length && planted < max; i++)
            {
                Vector3 point = new Vector3(center.x + offsets[i].x, center.y, center.z + offsets[i].y);

                // keep the post itself reachable
                if ((point - center).sqrMagnitude < 0.81f)
                {
                    continue;
                }

                if (!GroundPoint(ref point, center.y))
                {
                    continue;
                }

                // If not even the smallest plant fits here, no seed will.
                if (!HaveGrowSpace(point, _minGrowRadius))
                {
                    continue;
                }

                for (int s = 0; s < _usableSeeds.Count; s++)
                {
                    Quaternion rot = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
                    if (TryPlant(inv, _usableSeeds[s], point, rot, seedY0, seedY1))
                    {
                        planted++;
                        break;
                    }
                }
            }

            if (planted == 0)
            {
                _nextEmptyScan = Time.time + ModConfig.EmptyScanCooldown;
            }

            return planted;
        }

        private bool TryPlant(Inventory inv, GameObject plantPrefab, Vector3 pos, Quaternion rot, int seedY0, int seedY1)
        {
            if (plantPrefab == null)
            {
                return false;
            }

            Plant plant = plantPrefab.GetComponent<Plant>();
            if (plant == null)
            {
                return false;
            }

            SeedInfo seed;
            if (!FarmData.PlantToSeed.TryGetValue(plantPrefab.name, out seed))
            {
                return false;
            }

            if (InventoryUtils.CountItem(inv, seed.ItemName, seedY0, seedY1) < seed.Amount)
            {
                return false;
            }

            if (ModConfig.RequireCultivated.Value || plant.m_needCultivatedGround)
            {
                Heightmap heightmap = Heightmap.FindHeightmap(pos);
                if (heightmap == null || !heightmap.IsCultivated(pos))
                {
                    return false;
                }
            }

            if (plant.m_biome != 0 && (plant.m_biome & Heightmap.FindBiome(pos)) == 0)
            {
                return false;
            }

            if (!HaveGrowSpace(pos, plant.m_growRadius))
            {
                return false;
            }

            if (!InventoryUtils.RemoveItems(inv, seed.ItemName, seed.Amount, seedY0, seedY1))
            {
                return false;
            }

            GameObject planted = UnityEngine.Object.Instantiate(plantPrefab, pos, rot);
            if (planted == null)
            {
                return false;
            }

            Piece piece = plantPrefab.GetComponent<Piece>();
            if (piece != null && piece.m_placeEffect != null)
            {
                piece.m_placeEffect.Create(pos, rot);
            }

            return true;
        }

        /// <summary>
        ///     Free enough for a new plant? Only other plants, crops and building pieces block a
        ///     spot - terrain and dropped items do not.
        /// </summary>
        private static bool HaveGrowSpace(Vector3 pos, float growRadius)
        {
            if (_spaceMask < 0)
            {
                _spaceMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");
            }

            float radius = Mathf.Max(0.1f, growRadius);
            int hits = Physics.OverlapSphereNonAlloc(pos, radius, Overlap, _spaceMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits; i++)
            {
                Collider col = Overlap[i];
                if (col == null || !col.enabled)
                {
                    continue;
                }

                if (col.GetComponentInParent<Plant>() != null ||
                    col.GetComponentInParent<Pickable>() != null ||
                    col.GetComponentInParent<Piece>() != null)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool GroundPoint(ref Vector3 point, float startY)
        {
            if (_groundMask < 0)
            {
                _groundMask = LayerMask.GetMask("terrain");
            }

            RaycastHit hit;
            if (!Physics.Raycast(new Vector3(point.x, startY + 4f, point.z), Vector3.down, out hit, 12f,
                    _groundMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            if (hit.normal.y < 0.6f)
            {
                // too steep for a field
                return false;
            }

            point = hit.point;
            return true;
        }

        /// <summary>
        ///     Spots to try, closest first. Hex packing offsets every other row by half a step
        ///     and moves the rows closer together, which fits about 15% more plants into the same
        ///     field while keeping the same distance between neighbours.
        /// </summary>
        private static Vector2[] GetOffsets(float radius, float spacing, PlantPattern pattern)
        {
            string key = radius.ToString("0.##") + "/" + spacing.ToString("0.##") + "/" + pattern;

            Vector2[] cached;
            if (GridCache.TryGetValue(key, out cached))
            {
                return cached;
            }

            List<Vector2> points = new List<Vector2>();
            float rowStep = pattern == PlantPattern.Hex ? spacing * 0.8660254f : spacing;
            int rows = Mathf.Max(1, Mathf.CeilToInt(radius / rowStep));
            int cols = Mathf.Max(1, Mathf.CeilToInt(radius / spacing) + 1);

            for (int iz = -rows; iz <= rows; iz++)
            {
                float z = iz * rowStep;
                float shift = pattern == PlantPattern.Hex && (iz & 1) != 0 ? spacing * 0.5f : 0f;

                for (int ix = -cols; ix <= cols; ix++)
                {
                    Vector2 offset = new Vector2(ix * spacing + shift, z);
                    if (offset.magnitude <= radius)
                    {
                        points.Add(offset);
                    }
                }
            }

            // closest spots first
            points.Sort((a, b) => a.sqrMagnitude.CompareTo(b.sqrMagnitude));

            if (GridCache.Count > 8)
            {
                GridCache.Clear();
            }

            cached = points.ToArray();
            GridCache[key] = cached;
            return cached;
        }

        public string GetStatusText()
        {
            try
            {
                string text = string.Format(Util.Localize("$autofarm_hover_range"),
                    ModConfig.HarvestRadius.Value.ToString("0.#"),
                    ModConfig.PlantRadius.Value.ToString("0.#"));

                text += "\n" + string.Format(Util.Localize("$autofarm_hover_last"), _lastHarvest, _lastPlant);

                if (_outputFull)
                {
                    text += "\n" + Util.Localize("$autofarm_hover_full");
                }
                else if (_noSeeds)
                {
                    text += "\n" + Util.Localize("$autofarm_hover_noseeds");
                }

                if (_mythicFound < 0)
                {
                    _mythicFound = ReadMythicCount();
                }

                if (_mythicFound > 0)
                {
                    text += "\n" + string.Format(Util.Localize("$autofarm_hover_mythic"), _mythicFound);
                }

                return text;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
