using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// ORGANIC CITY GROWTH
//
// You place ONE capital. Everything after that is the population housing itself: as a colony grows,
// settlements appear on their own, spread outward from what's already there, and thicken over time
// into towns and then cities. A world you've left alone for an hour should look lived-in.
//
// Habitability is the whole dial. It decides:
//   * how fast new settlements appear at all,
//   * how many the world will ever support,
//   * how large any one of them can grow.
// A marginal 50% world creeps to a few scattered towns and stops. A 98-100% world keeps going until a
// real fraction of its land is continuous city — the "super city planet".
//
// Settlements are SurfaceBuildings like anything else, so they occupy real tiles, block placement, and
// show up in the infrastructure list. They are not a decorative overlay: they compete for the ground
// you wanted to mine.
//
// The whole thing can be switched off (GameConfig.OrganicCityGrowth). Nothing else depends on it —
// turn it off and worlds simply stay as you built them.
// ============================================================================================
public class CityGrowth : MonoBehaviour
{
    public static CityGrowth Instance;

    // Checked every this-many seconds per world. Cities are a slow-burn thing; there's no reason to
    // think about them more often than this, and it keeps the cost invisible.
    const float TickSeconds = 5f;

    // Population UNITS (1 unit = 100,000 people — see Population) before a colony spills out of its
    // capital at all: 1.8 million. A homeworld starts around 10 units (a million), so it has to roughly
    // double before anyone founds a settlement of their own.
    const int FirstSettlementPop = 18;
    // ...and every settlement beyond the first needs roughly this many more people (1.2M) to justify it.
    const int PopPerSettlement = 12;

    float timer;

    public static void Create()
    {
        if (Instance != null) return;
        new GameObject("CityGrowth").AddComponent<CityGrowth>();
    }

    void Awake() { Instance = this; }

    void Update()
    {
        if (!GameConfig.OrganicCityGrowth) return;

        timer += Time.deltaTime;          // scales with game speed: fast-forward really does grow cities
        if (timer < TickSeconds) return;
        timer = 0f;

        if (SystemContext.Galaxy == null) return;
        foreach (var b in SystemContext.AllBodies())
            if (b != null && b.owner == FactionManager.Player) TickWorld(b);
    }

    // ---- The rules ----

    /// How habitable a world is, as a 0..1 dial from "barely livable" to "paradise". Below the
    /// colonisation threshold nothing grows at all — people don't spread across ground they can't
    /// breathe on.
    public static float Liveability(CelestialBody b)
    {
        if (b == null) return 0f;
        return Mathf.Clamp01((b.habitability - Colony.FoundThreshold) / (100f - Colony.FoundThreshold));
    }

    /// The most settlements this world will ever grow on its own.
    ///
    /// Scales with liveability AND with the world's size, because a big paradise has room to fill and a
    /// small one doesn't. The exponent is what creates the "super city planet": the curve is deliberately
    /// steep at the top, so 98-100% is worth far more than 90% rather than a little more.
    public static int MaxSettlements(CelestialBody b)
    {
        if (b?.surface == null) return 0;
        float live = Liveability(b);
        if (live <= 0.01f) return 0;

        int buildableTiles = BuildableTiles(b);
        // The fraction of a world's land that can ever become city, from a few percent on a marginal
        // world to most of a perfect one.
        float coverage = Mathf.Lerp(0.04f, 0.55f, Mathf.Pow(live, 1.8f));
        float tilesForCities = buildableTiles * coverage;

        // A mature settlement averages ~5 tiles.
        return Mathf.Max(1, Mathf.FloorToInt(tilesForCities / 5f));
    }

    /// Seconds between new settlements appearing. A paradise sprouts one every half-minute or so; a
    /// marginal world takes many minutes.
    public static float SpawnInterval(CelestialBody b)
    {
        float live = Liveability(b);
        if (live <= 0.01f) return float.MaxValue;
        return Mathf.Lerp(420f, 35f, Mathf.Pow(live, 1.35f));
    }

    /// The biggest a settlement may become here. Only a properly habitable world grows true cities;
    /// a marginal one never gets past hamlets.
    public static int MaxTier(CelestialBody b)
    {
        float live = Liveability(b);
        if (live >= 0.85f) return 3;   // City
        if (live >= 0.5f) return 2;    // Town
        return 1;                      // Settlement
    }

    static int BuildableTiles(CelestialBody b)
    {
        int n = 0;
        for (int x = 0; x < b.surface.width; x++)
            for (int y = 0; y < b.surface.height; y++)
                if (!PlanetTerrainGenerator.IsWater(b.surface.tiles[x, y].type)) n++;
        return n;
    }

    // ---- Per-world tick ----
    void TickWorld(CelestialBody b)
    {
        if (b.surface == null || b.placedBuildings == null) return;
        if (!b.buildings.Contains((int)BuildingType.City) &&
            SurfaceBuildManager.CountOf(b, SurfaceBuildingType.ColonyShipBase) == 0 &&
            SurfaceBuildManager.CountOf(b, SurfaceBuildingType.PlanetCapitol) == 0)
            return;   // nothing to grow OUT of — a world needs a seat of government first

        float live = Liveability(b);
        if (live <= 0.01f) return;

        b.cityGrowthTimer += TickSeconds;

        // Existing settlements thicken before new ones appear — a world fills in, it doesn't sprawl
        // as a rash of hamlets.
        if (TryPromote(b, live)) return;

        if (b.cityGrowthTimer < SpawnInterval(b)) return;
        b.cityGrowthTimer = 0f;

        TrySpawn(b, live);
    }

    // Grow an existing settlement a tier: Settlement -> Town -> City.
    bool TryPromote(CelestialBody b, float live)
    {
        int cap = MaxTier(b);
        foreach (var p in b.placedBuildings)
        {
            if (!IsSettlement(p.Type)) continue;
            int tier = TierOf(p.Type);
            if (tier >= cap) continue;

            // A settlement needs the people to justify growing, and the room to do it in.
            if (b.population < FirstSettlementPop + tier * PopPerSettlement) continue;
            if (Random.value > 0.35f) continue;   // not every eligible one, every tick

            var next = tier == 1 ? SurfaceBuildingType.Town : SurfaceBuildingType.City;
            if (!CanFitAt(b, next, p.x, p.y, p.rotation, p)) continue;

            // Replace in place: same origin, bigger footprint.
            ClearFootprint(b, p);
            p.type = (int)next;
            p.level = Mathf.Clamp(p.level, 1, PlacedBuilding.MaxLevel);
            p.health = 1f;
            StampFootprint(b, p);

            NotificationManager.Instance?.Push($"{SurfaceBuildingDatabase.Get(next).name} on {b.name}",
                $"A settlement has grown into a {SurfaceBuildingDatabase.Get(next).name.ToLower()} as {b.name}'s population spreads.",
                null, NotifKind.Info);
            return true;
        }
        return false;
    }

    void TrySpawn(CelestialBody b, float live)
    {
        int have = CountSettlements(b);
        if (have >= MaxSettlements(b)) return;

        // People only build where there are already people: settlements need enough population to
        // support the ones that exist before another appears.
        if (b.population < FirstSettlementPop + have * PopPerSettlement) return;

        if (!FindSettlementSpot(b, out int x, out int y)) return;

        if (SurfaceBuildManager.ForcePlace(b, SurfaceBuildingType.Settlement, x, y, Random.Range(0, 4)))
        {
            // Quiet: a world filling in shouldn't spam the log. Only the first is worth announcing.
            if (have == 0)
                NotificationManager.Instance?.Push($"Settlers spreading out on {b.name}",
                    "The colony has outgrown its capital and people are founding settlements of their own. " +
                    "They'll grow into towns and cities on their own — and they compete for the ground you were saving.",
                    null, NotifKind.Info);
        }
    }

    // Cities grow OUTWARD FROM CITIES. Sites are scored by closeness to what's already built (people
    // settle near people) and by how good the ground is to live on, so a world develops as recognisable
    // regions rather than a uniform scatter.
    bool FindSettlementSpot(CelestialBody b, out int fx, out int fy)
    {
        fx = fy = -1;
        var info = SurfaceBuildingDatabase.Get(SurfaceBuildingType.Settlement);
        var occupied = SurfaceBuildManager.Occupied(b);

        float bestScore = float.MinValue;
        int tries = 0;

        for (int x = 0; x < b.surface.width; x++)
            for (int y = 0; y < b.surface.height; y++)
            {
                if (++tries > 4000) break;                       // hard ceiling on a huge world
                if (occupied.Contains(new Vector2Int(x, y))) continue;
                if (!CanFitAt(b, SurfaceBuildingType.Settlement, x, y, 0, null)) continue;

                float dist = NearestBuiltDistance(b, x, y);
                if (dist > 9f) continue;                          // don't found a town on the far side

                // Close to civilisation, on ground worth living on.
                float fertile = SurfaceIndex.Get(b, SurfaceIndexKind.Fertile, x, y);
                float score = (10f - dist) * 1.2f + fertile * 4f + Random.value * 2.5f;
                if (score > bestScore) { bestScore = score; fx = x; fy = y; }
            }

        return fx >= 0;
    }

    static float NearestBuiltDistance(CelestialBody b, int x, int y)
    {
        float best = float.MaxValue;
        foreach (var p in b.placedBuildings)
            foreach (var c in SurfaceBuildingDatabase.Footprint(p))
                best = Mathf.Min(best, Vector2.Distance(new Vector2(x, y), new Vector2(c.x, c.y)));
        return best == float.MaxValue ? 999f : best;
    }

    // Would this footprint fit here? `ignore` lets a settlement grow into its own current tiles.
    static bool CanFitAt(CelestialBody b, SurfaceBuildingType t, int x, int y, int rot, PlacedBuilding ignore)
    {
        var info = SurfaceBuildingDatabase.Get(t);
        var own = new HashSet<Vector2Int>();
        if (ignore != null) foreach (var c in SurfaceBuildingDatabase.Footprint(ignore)) own.Add(c);

        foreach (var c in SurfaceBuildingDatabase.Footprint(t, x, y, rot))
        {
            if (!SurfaceBuildManager.CellBuildable(b, info, c.x, c.y, out _)) return false;
            if (own.Contains(c)) continue;
            if (SurfaceBuildManager.At(b, c.x, c.y) != null) return false;
        }
        return true;
    }

    static void ClearFootprint(CelestialBody b, PlacedBuilding p)
    {
        foreach (var c in SurfaceBuildingDatabase.Footprint(p))
            if (c.x >= 0 && c.y >= 0 && c.x < b.surface.width && c.y < b.surface.height)
                b.surface.tiles[c.x, c.y].occupied = false;
    }

    static void StampFootprint(CelestialBody b, PlacedBuilding p)
    {
        foreach (var c in SurfaceBuildingDatabase.Footprint(p))
            if (c.x >= 0 && c.y >= 0 && c.x < b.surface.width && c.y < b.surface.height)
                b.surface.tiles[c.x, c.y].occupied = true;
    }

    // ---- Helpers ----
    public static bool IsSettlement(SurfaceBuildingType t)
        => t == SurfaceBuildingType.Settlement || t == SurfaceBuildingType.Town || t == SurfaceBuildingType.City;

    public static int TierOf(SurfaceBuildingType t)
        => t == SurfaceBuildingType.City ? 3 : t == SurfaceBuildingType.Town ? 2 : 1;

    public static int CountSettlements(CelestialBody b)
    {
        int n = 0;
        foreach (var p in SurfaceBuildManager.On(b)) if (IsSettlement(p.Type)) n++;
        return n;
    }

    /// What fraction of this world's buildable land is under settlement — the "is this a city planet?"
    /// number, shown in the Planet View.
    public static float UrbanFraction(CelestialBody b)
    {
        if (b?.surface == null) return 0f;
        int buildable = BuildableTiles(b);
        if (buildable == 0) return 0f;

        int urban = 0;
        foreach (var p in SurfaceBuildManager.On(b))
            if (IsSettlement(p.Type) || p.Type == SurfaceBuildingType.PlanetCapitol ||
                p.Type == SurfaceBuildingType.ColonyShipBase)
                urban += SurfaceBuildingDatabase.Footprint(p).Count;
        return Mathf.Clamp01(urban / (float)buildable);
    }

    public static string UrbanLabel(CelestialBody b)
    {
        float f = UrbanFraction(b);
        if (f >= 0.45f) return "Ecumenopolis";     // the whole world is one city
        if (f >= 0.28f) return "City world";
        if (f >= 0.15f) return "Urbanised";
        if (f >= 0.05f) return "Settled";
        if (f > 0f) return "Frontier";
        return "Uninhabited";
    }
}
