using UnityEngine;

// ============================================================================================
// WORLD CLASSIFIER — the type EMERGES from the attributes, it is not chosen up front.
//
// The old generator picked a CelestialBodyType from the orbital band and then derived mass,
// atmosphere and the rest from that type. This inverts it, per the Advanced Planet Generation
// spec: the generator sets Mass -> Magnetic Field -> Tectonics -> Atmosphere -> Temperature ->
// Water -> BioSphere, in that order, and THEN asks this class what it built.
//
// TWO OUTPUTS, because they answer two different questions:
//
//   Physics(...)  -> the CelestialBodyType enum. This is the PHYSICS class, and it drives the
//                    machinery: which terrain classifier paints the surface, whether there is an
//                    atmosphere shell, the temperature type-modifier, habitability affinity. Kept
//                    to the existing eight values on purpose — growing the enum would touch the
//                    per-species affinity array, the save format and a dozen switch statements,
//                    none of it verifiable without a compiler.
//
//   Describe(...) -> a player-facing NAME. This is where the spec's variety lives — Continental,
//                    Archipelago, Desert, Savanna, Swamp, Tundra, Toxic, Molten — derived from the
//                    live attributes rather than stored, so it re-reads correctly the moment a
//                    world is terraformed, with no new field and no migration.
//
// WHY THE HOT END IS BAND-DRIVEN. The temperature model (PlanetTemperature) tops out around 160C
// from orbit and greenhouse alone; a lava world's real heat comes from the type MODIFIER (+90C for
// Volcanic), which is a consequence of the type, not an input to it. So "how close to the star"
// (`rel`) decides the scorched/hot/temperate/cold band, exactly as the spec frames it, and the
// attributes decide the type within the band.
// ============================================================================================
public static class WorldClassifier
{
    // Physics-class mass gates.
    public const float GasGiantMassFloor = 7f;    // a real gas giant; no solid surface
    public const float AsteroidMassCeil = 0.5f;   // too little gravity to be a world

    // Orbital-band cuts, as a fraction of the star's Earth-warmth distance (`rel`). Shared with the
    // old RollBodyByTemperature bands so the galaxy's overall hot/temperate/cold mix is unchanged.
    public const float ScorchingRel = 0.45f;      // right by the star — Mercury/lava
    public const float HotRel = 0.85f;            // inside the habitable zone
    public const float TemperateRelMax = 1.5f;    // the habitable band's outer edge
    public const float CoolRelMax = 3f;           // beyond it, genuinely cold

    // Water-coverage cuts.
    public const float OceanWater = 0.85f;        // spec: 85%+ is an ocean world
    public const float LandWater = 0.35f;         // below this a "temperate" world is really dry rock

    /// The type-INDEPENDENT surface temperature, in Celsius — heat and greenhouse only, with the type
    /// modifier deliberately left out because the type is the very thing being decided. Classifying on
    /// the finished BodyAverageCelsius would be circular: a world would have to already be Volcanic to
    /// read hot enough to be classified Volcanic.
    public static float ClassifyC(CelestialBody b)
        => b == null ? 15f : PlanetTemperature.BaseCelsius(b.terrainParams.heat, b.atmosphereThickness, CelestialBodyType.RockyPlanet);

    static float Water(CelestialBody b)
        => b == null ? 0f : PlanetTerrainGenerator.WaterLevelFromSeaLevel(b.terrainParams.SeaLevelOrNeutral);

    // ---- The physics class ---------------------------------------------------------------------

    /// The CelestialBodyType a set of attributes amounts to. `rel` is distance / the star's
    /// Earth-warmth distance; `isMoon` keeps a tiny cold moon a Moon rather than an Asteroid.
    public static CelestialBodyType Physics(CelestialBody b, float rel, bool isMoon)
    {
        if (b == null) return CelestialBodyType.BarrenPlanet;

        // Size decides the extremes outright, before anything else.
        if (b.mass >= GasGiantMassFloor) return CelestialBodyType.GasGiant;
        if (b.mass < AsteroidMassCeil) return isMoon ? CelestialBodyType.Moon : CelestialBodyType.Asteroid;

        float c = ClassifyC(b);
        float water = Water(b);
        bool warmEnoughForLiquid = c >= BiosphereRules.MinLiquidC && c <= BiosphereRules.MaxLiquidC;

        // --- Scorching / hot: right by the star ---
        // Tectonic activity vents magma to the surface — that is what makes a hot world VOLCANIC rather
        // than merely a baked rock. Without plates a hot world is barren, however close it sits.
        if (rel < HotRel)
        {
            if (b.hasTectonics) return CelestialBodyType.VolcanicPlanet;
            return CelestialBodyType.BarrenPlanet;
        }

        // --- Temperate: the habitable band, the only place worlds can be alive ---
        if (rel <= TemperateRelMax)
        {
            // Frozen despite sitting in the band — a thin-aired world here still runs cold, and its
            // water is ice. Needs real water to be an ICE world rather than a bare cold rock.
            if (c < BiosphereRules.MinLiquidC)
                return water >= LandWater ? CelestialBodyType.IcePlanet : CelestialBodyType.BarrenPlanet;

            if (warmEnoughForLiquid)
            {
                if (water >= OceanWater) return CelestialBodyType.OceanPlanet;
                if (water >= LandWater) return CelestialBodyType.RockyPlanet;   // Terran-family
            }
            // Warm but bone dry, or too hot for liquid: a desert-rock world, mechanically Rocky if it
            // has any air to speak of, Barren if not.
            return b.atmospheres >= AtmosphereRules.LifeFloor ? CelestialBodyType.RockyPlanet
                                                              : CelestialBodyType.BarrenPlanet;
        }

        // --- Cool / cold: the outer system ---
        // Water freezes out here; a body with water is an ice world, one without is a bare rock (or, for
        // a moon, the airless Moon default the old generator always produced out here).
        if (water >= LandWater) return CelestialBodyType.IcePlanet;
        return isMoon ? CelestialBodyType.Moon : CelestialBodyType.BarrenPlanet;
    }

    // ---- The descriptive name ------------------------------------------------------------------

    /// The distance ratio (distance / the star's Earth-warmth distance) for a placed body — the same
    /// `rel` generation classifies on. Falls back to the temperate band when there is no host star yet.
    public static float RelOf(CelestialBody b)
    {
        if (b == null || b.hostStar == null) return 1f;
        return b.distanceFromStar / Mathf.Max(0.5f, StarDatabase.ReferenceDistance(b.hostStar));
    }

    /// The player-facing world class from a body's CURRENT type. See the overload for a live one.
    public static string Describe(CelestialBody b) => Describe(b, b == null ? CelestialBodyType.BarrenPlanet : b.type);

    /// Reclassify from live attributes and name THAT — for the Dev sandbox, where the sliders move the
    /// attributes but nothing re-runs generation, so `body.type` lags. Computes the physics type into a
    /// local and names it without touching the body.
    public static string DescribeLive(CelestialBody b)
    {
        if (b == null) return "unknown";
        return Describe(b, Physics(b, RelOf(b), b.parentBody != null));
    }

    /// The player-facing world class, derived from live attributes and a given physics type. `Moon`
    /// suffix is added by the caller (TerraformDiagnosis.Pretty).
    public static string Describe(CelestialBody b, CelestialBodyType physics)
    {
        if (b == null) return "unknown";

        if (physics == CelestialBodyType.GasGiant) return "gas giant";
        if (physics == CelestialBodyType.Asteroid) return "asteroid";

        float c = ClassifyC(b);
        float water = Water(b);
        float moisture = b.terrainParams.moisture;
        bool alive = b.biosphereActive;
        bool thickAir = b.atmospheres >= 4f;

        switch (physics)
        {
            case CelestialBodyType.VolcanicPlanet:
                // A truly scorched world reads as molten; a merely hot, active one as volcanic.
                return c > 140f ? "molten world" : "volcanic world";

            case CelestialBodyType.IcePlanet:
                return "frozen world";

            case CelestialBodyType.OceanPlanet:
                // Fully drowned is an ocean; high-but-broken water reads as island chains.
                return water >= 0.95f ? "ocean world" : "archipelago world";

            case CelestialBodyType.BarrenPlanet:
                // Venus: a thick, poisoned sky over a baked surface.
                if (thickAir && c > 60f) return "toxic world";
                if (c > 90f) return "scorched world";
                if (c < BiosphereRules.MinLiquidC) return "frozen rock";
                return "barren world";

            case CelestialBodyType.RockyPlanet:
            default:
                // The temperate family — where the spec wants real variety. Names gate lush biomes on an
                // actual biosphere: a wet world with no life is a wetland of bare rock, not a swamp.
                if (!alive)
                    return water < LandWater ? "desert world" : "barren rocky world";

                if (c < 4f) return "tundra world";                    // cold edge of the band
                if (water >= 0.65f) return "swamp world";             // wet and warm and living
                if (water < 0.2f || moisture < 0.85f) return
                    (c > 24f) ? "desert world" : "savanna world";     // dry: harsher hot, milder warm
                if (water > 0.55f) return "continental world";        // land-heavy but well-watered
                return "terran world";                                // the balanced default
        }
    }

    // ---- Terrain amplification -----------------------------------------------------------------

    /// Nudge the terrain noise so a world that classified as a strongly-flavoured biome actually LOOKS
    /// like one, rather than being one dry patch short of "terran".
    ///
    /// Gentle on purpose — this leans the existing TerrainVariance roll toward its dominant biome, it
    /// does not overwrite it, so two desert worlds still differ. Applied at generation, before the bake,
    /// and only to the temperate rocky family; the other types already read unambiguously.
    public static void AmplifyBiome(CelestialBody b)
    {
        if (b == null || b.type != CelestialBodyType.RockyPlanet) return;

        string cls = Describe(b);
        var p = b.terrainParams;

        switch (cls)
        {
            case "desert world":
                p.moisture = Mathf.Min(p.moisture, 0.55f);
                p.heat = Mathf.Max(p.heat, 1.15f);
                break;
            case "savanna world":
                p.moisture = Mathf.Clamp(p.moisture, 0.7f, 1.0f);
                break;
            case "swamp world":
                p.moisture = Mathf.Max(p.moisture, 1.2f);
                break;
            case "tundra world":
                p.heat = Mathf.Min(p.heat, 0.75f);
                break;
            // Continental / Terran / Archipelago keep their natural roll — their character is the
            // land/water SPLIT, which is already set by the water level, not by moisture or heat.
        }

        b.terrainParams = p;
    }
}
