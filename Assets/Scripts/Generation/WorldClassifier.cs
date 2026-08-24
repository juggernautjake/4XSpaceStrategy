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
    //
    // Nothing is GENERATED between 4 (the terrestrial ceiling) and 10 (the gas-giant floor) — that gap
    // is the whole point of the mass scheme. The cut sits at 7, in the middle of the gap, because this
    // function also has to classify masses nobody rolled: a body a developer typed a number into, a
    // world a terraforming project remodelled, a save from before the scheme changed. Putting the line
    // at 10 would call a hand-made mass-8 body a rocky planet with eight Earths of gravity; putting it
    // at 4 would call a mass-5 one a gas giant. The middle of an empty band is the one place it cannot
    // be wrong about anything the generator actually produces.
    public const float GasGiantMassFloor = 7f;    // a real gas giant; no solid surface
    /// INCLUSIVE — 0.5 IS an asteroid, per the request. Everything that tests it uses <=.
    public const float AsteroidMassCeil = 0.5f;   // too little gravity to be a world

    // Orbital-band cuts, as a fraction of the star's Earth-warmth distance (`rel`). Shared with the
    // old RollBodyByTemperature bands so the galaxy's overall hot/temperate/cold mix is unchanged.
    public const float ScorchingRel = 0.45f;      // right by the star — Mercury/lava
    public const float HotRel = 0.85f;            // inside the habitable zone
    public const float TemperateRelMax = 1.5f;    // the habitable band's outer edge
    public const float CoolRelMax = 3f;           // beyond it, genuinely cold

    // ============================================================================================
    // THE FROST LINE — why gas giants live in the outer system
    //
    // Inside this distance a protoplanetary disc is warm enough that water, ammonia and methane stay
    // vapour. A forming core there has only rock and metal to work with, there is not much of either,
    // and it never gets massive enough for its gravity to start pulling in hydrogen. That is the whole
    // reason the inner solar system is four small rocky worlds and the outer is four giants — not an
    // aesthetic preference, a supply problem.
    //
    // Past the line the same water is ICE, which is both far more abundant and already solid. Cores
    // grow fast, pass the runaway-accretion threshold, and capture enormous gas envelopes.
    //
    // In `rel` — distance over the star's Earth-warmth distance — 2.7 is where it sits in our own
    // system (the asteroid belt straddles it, which is not a coincidence: that is material that never
    // got to be a planet). It is set a little inside that here, at 2.2, purely because these systems are
    // laid out in eight to twelve lanes rather than in AU, and 2.7 would push the first giant out to a
    // lane most systems never reach.
    public const float FrostLineRel = 2.2f;

    /// How often a giant turns up INSIDE the frost line anyway. Hot Jupiters are real — they form out
    /// past the line and then migrate inward through the disc — and the request asks for exactly this:
    /// "allow for rare exclusions that let a large CB spawn closer". Rare enough that finding one is an
    /// event; common enough that it happens across a galaxy of a dozen systems.
    public const float HotGiantChance = 0.05f;

    // Water-coverage cuts.
    public const float OceanWater = 0.85f;        // spec: 85%+ is an ocean world
    public const float LandWater = 0.35f;         // below this a "temperate" world is really dry rock

    /// Ground this hot is liquid rock. Basalt melts around here, so it is where "magma field" stops
    /// being a decoration and starts being what the surface literally is — the request's 650-1000 °C
    /// band. Shared with the terrain generator so the world's NAME and its map cannot disagree about
    /// whether it is molten.
    // 800, up from 650. "Lets also raise the minimum requirement of temperature for magma fields to
    // 800 C." The screenshot that came with it shows the reason it mattered: a MagmaField tile reading
    // 794 degrees, which is to say a tile called molten rock that was a hundred and fifty degrees short
    // of the basalt solidus and is now correctly demoted to LavaRock.
    public const float MagmaMinC = 800f;

    /// ...and how much of that heat has to come from INSIDE the world for its ground to be molten.
    ///
    /// Rock melts from below. Without this the magma gate reads a tile total that includes starlight,
    /// so a world orbiting close to its sun grew liquid rock everywhere — and the Geothermal index then
    /// read that liquid rock back as evidence of a hot crust and reported 95% across the whole map.
    /// 300 degrees of genuine internal heat is about half what a fully active volcanic world carries
    /// (PlanetTemperature.InternalMaxC is 620), so a quiet volcanic world still qualifies and a merely
    /// scorched rocky one never does.
    public const float MagmaInternalMinC = 300f;

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

        // Size decides the extremes outright, before anything else. The asteroid test is <=, not <: the
        // request draws the line at "0.5 and below", and a body sitting exactly on it is an asteroid.
        if (b.mass >= GasGiantMassFloor) return CelestialBodyType.GasGiant;
        if (b.mass <= AsteroidMassCeil) return isMoon ? CelestialBodyType.Moon : CelestialBodyType.Asteroid;

        float c = ClassifyC(b);
        float water = Water(b);
        // The PHYSICAL window, which depends on how much air is holding the water down — a
        // four-atmosphere world keeps oceans well past the point a thin-aired one has lost them.
        BiosphereRules.LiquidRange(b, out float freezeC, out float boilC);
        bool warmEnoughForLiquid = c >= freezeC && c <= boilC;

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
            if (c < freezeC)
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
                // MOLTEN is a real threshold now, not a descriptive flourish: at and above it the
                // terrain generator lays down magma fields, so the word and the map agree. Read off the
                // world's ACTUAL temperature — which includes its internal heat, the term that makes a
                // world molten in the first place — rather than off `c`, which is deliberately blind to
                // the type it is trying to decide.
                return PlanetTemperature.BodyAverageCelsius(b) >= MagmaMinC ? "molten world" : "volcanic world";

            case CelestialBodyType.IcePlanet:
                return "frozen world";

            case CelestialBodyType.OceanPlanet:
                // Fully drowned is an ocean; high-but-broken water reads as island chains.
                return water >= 0.95f ? "ocean world" : "archipelago world";

            case CelestialBodyType.BarrenPlanet:
                // Venus: a thick, poisoned sky over a baked surface.
                if (thickAir && c > 60f) return "toxic world";
                if (c > 90f) return "scorched world";
                if (c < BiosphereRules.FreezingC(b.atmospheres)) return "frozen rock";
                return "barren world";

            case CelestialBodyType.RockyPlanet:
            default:
                // The temperate family — where the spec wants real variety. Names gate lush biomes on an
                // actual biosphere: a wet world with no life is a wetland of bare rock, not a swamp.
                if (!alive)
                    return water < LandWater ? "desert world" : "barren rocky world";

                if (c < 4f) return "tundra world";                    // cold edge of the band

                // ---- DROWNED FIRST, and this ordering is the whole fix -------------------------
                //
                // "swamp world" used to be `water >= 0.65`, tested before anything else, so any warm
                // living world with two thirds of its surface under water was called a swamp. It was
                // reported as "mostly just ocean", and it was: at 65% coverage the water has closed
                // over the land, and a world with no land is not a wetland.
                //
                // A SWAMP IS WET LAND. So drowned worlds are named for their water first, and swamp is
                // left to describe what it actually describes — a coastline you can still walk on,
                // waterlogged rather than submerged.
                if (water >= OceanWater) return "ocean world";
                if (water >= 0.62f) return "archipelago world";

                // Waterlogged land: high moisture, warm enough to rot, and enough water present to
                // keep the ground saturated — but not so much that there is nothing left to stand on.
                if (moisture >= 1.05f && c >= 12f && water >= 0.3f) return "swamp world";

                if (water < 0.2f || moisture < 0.85f) return
                    (c > 24f) ? "desert world" : "savanna world";     // dry: harsher hot, milder warm
                if (water > 0.5f) return "continental world";         // land-heavy but well-watered
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
                // AND HOLD THE SEA DOWN. Amplifying moisture without touching the water level produced
                // exactly the world that got reported: the moisture made every low tile a wetland, and
                // then the sea rose straight over the top of it, so the "swamp world" was open ocean
                // with a fringe. A swamp is the SHORE — it needs the water near the land, not above it.
                //
                // 0.42-0.58 keeps the coastline in the band where lowland floods but does not drown,
                // which is the same neutral-ish range a terran world sits in. The classifier now refuses
                // to call anything wetter than 0.62 a swamp at all; this stops generation from drifting
                // a world out of its own class after it has been named.
                p.seaLevel = Mathf.Clamp(p.SeaLevelOrNeutral, 0.42f, 0.58f);
                break;

            case "archipelago world":
                // Broken land in a high sea. Left alone before, which meant nothing guaranteed there
                // was any land left to break — an archipelago could generate as an unbroken ocean.
                p.seaLevel = Mathf.Clamp(p.SeaLevelOrNeutral, 0.62f, 0.76f);
                p.elevation = Mathf.Max(p.elevation, 1.05f);   // relief, so islands clear the water
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
