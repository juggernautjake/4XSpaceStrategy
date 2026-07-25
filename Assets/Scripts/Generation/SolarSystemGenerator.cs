using System.Collections.Generic;
using UnityEngine;

public class SolarSystemGenerator : MonoBehaviour
{
    public int minBodies = 2;
    public int maxBodies = 6;

    public StarType currentStarType;
    public StarData currentStar;     // combined physical data for the cluster (light/heat/HZ/orbits)
    public List<StarData> stars = new List<StarData>();  // 1-3 suns (or a single black hole)
    public bool isBlackHole;
    public string currentSystemName; // unique name of the most recently generated system

    int _idCounter;

    /// The finished system, written by GenerateSystemStepped.
    ///
    /// A field rather than a return value because the generator is an ITERATOR now: an IEnumerator cannot
    /// return a result, so the one place the system is built has to hand it back some other way.
    public List<CelestialBody> lastSystem;

    /// Generate a whole system in one call, by draining the stepped version — which is the single
    /// implementation, so the two can never diverge.
    ///
    /// Currently unused: every path now runs through GalaxyGenerator.AddSystemStepped, including the R
    /// debug key. Kept because "generate one system, synchronously" is the obvious thing an external
    /// caller reaches for, and it is one line to keep correct.
    public List<CelestialBody> GenerateSystem()
    {
        var it = GenerateSystemStepped();
        while (it.MoveNext()) { }
        return lastSystem;
    }

    /// The same generation, yielding after each world's terrain is built.
    ///
    /// WHY THIS EXISTS. Terrain generation is the expensive part of making a galaxy — a mass-6 planet is
    /// 600x300 cells at roughly twenty noise lookups each — and it all used to happen inside one
    /// synchronous call. That meant the loading screen got ONE rendered frame per star system, so its bar
    /// stepped and its animated dots appeared frozen: not because the animation was wrong, but because
    /// there was nothing to draw them on. Yielding per BODY turns each system into a handful of frames
    /// instead of one, which is what actually makes the screen move.
    ///
    /// The yields are placed immediately after each GenerateSurface, because that is where the time goes.
    public System.Collections.IEnumerator GenerateSystemStepped()
    {
        // Cleared up front: if this throws before the tail, lastSystem would otherwise still hold the
        // PREVIOUS system, and a caller that ignored the exception would hand the same List to two
        // StarSystemData — LinkBodies then repoints every body.system to the second.
        lastSystem = null;
        _idCounter = 0;
        List<CelestialBody> system = new();

        RollStarSystem();

        // Body count honours minBodies/maxBodies (the galaxy generator sets these from "avg planets").
        int lo = Mathf.Max(1, minBodies);
        int hi = Mathf.Max(lo, maxBodies);
        int bodyCount = Mathf.Clamp(Random.Range(lo, hi + 1), 1, 10);
        string systemName = NameGenerator.UniqueSystemName();
        currentSystemName = systemName;
        NameStars(systemName);

        // Start the innermost planet just past the star's SURFACE plus the standard clearance, derived from
        // the star's actual render size — not a fixed 9–12 that predated the 2x-larger stars and would let a
        // big sun sit almost touching (or swallowing) its first planet. A dim red dwarf keeps its planets
        // tucked in close; a large bright star holds them further out, both by construction here and as the
        // EnforceSystem pass (below) guarantees. The small random spread keeps systems from all starting at
        // the identical radius.
        float starRadius = OrbitSafety.StarRadius(currentStar);   // whole cluster reach for a binary/ternary
        float currentRadius = starRadius + OrbitSafety.StarClearance + Random.Range(1f, 4f);
        float prevOuterReach = 0f;                     // outermost point the previous planet's system reaches

        for (int i = 0; i < bodyCount; i++)
        {
            // A 0..1 SIZE RANK. It now feeds MassRules.ByBand, which orders bodies within a band from
            // small to large — the biggest in the cold outer bands become gas giants, the smallest
            // asteroids, exactly the request's "gas giants are the largest of planet sizes". Mass is the
            // first attribute set; everything else, including the type, is derived from it downstream.
            float sizeRank = Random.value;

            // ATTRIBUTE-FIRST. Mass comes from the orbital band (MassRules.ByBand), then the whole
            // pipeline sets field, tectonics, climate, water and air and CLASSIFIES the type from them —
            // rather than the old path of picking a type and deriving mass from it.
            float rel = currentRadius / Mathf.Max(0.5f, TempReference(currentStar));

            CelestialBody body = new(CelestialBodyType.RockyPlanet) { id = _idCounter++ };
            body.name = NameGenerator.PlanetName(systemName, i);
            body.distanceFromStar = currentRadius;
            body.orbitRadius = currentRadius;
            body.mass = MassRules.ByBand(rel, sizeRank);

            ApplyWorldPipeline(body, rel, isMoon: false);
            ResourceGenerator.GenerateResources(body);   // type is settled, so resources match it
            // STEPPED. A world's terrain is by far the most expensive step in the whole load, and built
            // in one go it was a single frame lasting as long as generating an entire planet. Now it
            // yields every few milliseconds from inside its own loop, so one enormous frame becomes
            // dozens of ordinary ones and the loading screen animates at a normal rate throughout.
            {
                var surf = PlanetTerrainGenerator.BuildStepped(body, body.terrainParams,
                                                              PlanetTerrainGenerator.Octaves,
                                                              s => body.surface = s);
                while (surf.MoveNext()) yield return surf.Current;
            }
            yield return null;
            // Ore is populated against the REAL baked surface, here — not earlier, when there is no
            // surface to seed it into yet.
            OreGenerator.Populate(body);
            body.orbitSpeed = OrbitalMechanics.PlanetAngularSpeed(currentStar, currentRadius);
            body.spinSpeed = OrbitalMechanics.Spin(body, Random.Range(0.7f, 1.3f));
            body.orbitPhase = Random.Range(0f, 360f);
            body.orbitDirection = Random.value < 0.9f ? 1 : -1;
            body.inclination = Random.Range(-7f, 7f);
            body.eccentricity = Random.Range(0f, 0.14f);

            ApplyHabitability(body);
            POIGenerator.Populate(body);

            // Moons.
            // The first moon has to clear the PLANET'S OWN SURFACE, which the old fixed 2.6 start did
            // not: a large world's visual radius is surfaceSize * 0.08 * 0.5, so a big gas giant's
            // surface reached past 2.6 and its innermost moon flew through it.
            int moonCount = RollMoonCount(body.type);
            float planetVisRadius = Mathf.Max(0.6f, body.surfaceSize * 0.08f) * 0.5f;
            float moonR = planetVisRadius + MaxMoonVisRadius + MoonSurfaceGap;
            for (int m = 0; m < moonCount; m++)
            {
                CelestialBody moon = new(CelestialBodyType.Moon) { id = _idCounter++ };
                moon.name = NameGenerator.MoonName(body.name, m);
                // A moon's MASS comes from its host planet: at most 40% of the host, rarely that high (see
                // MassRules.ForMoon) — so a big gas giant can have a sizeable moon, a small planet only
                // tiny ones. Everything after mass runs through the SAME pipeline a planet does.
                moon.mass = MassRules.ForMoon(body.mass);
                moon.distanceFromStar = body.distanceFromStar;   // shares the planet's solar distance
                // Set BEFORE the pipeline so the moon's grid size cap (MapMetrics keys off parentBody) and
                // its classification both see the relationship.
                moon.parentBody = body;

                // ONE PIPELINE, moons included. `isMoon: true` is the only difference — it keeps a tiny or
                // cold moon a Moon rather than an Asteroid/Barren, but a moon that ends up massive,
                // magnetised, temperate and wet classifies to the same temperate world a planet would.
                // That is the spec's headline: a big moon of a gas giant in the habitable zone can be a
                // Terran world.
                float moonRel = moon.distanceFromStar / Mathf.Max(0.5f, TempReference(currentStar));
                ApplyWorldPipeline(moon, moonRel, isMoon: true);
                {
                    var msurf = PlanetTerrainGenerator.BuildStepped(moon, moon.terrainParams,
                                                                   PlanetTerrainGenerator.Octaves,
                                                                   s => moon.surface = s);
                    while (msurf.MoveNext()) yield return msurf.Current;
                }
                yield return null;
                OreGenerator.Populate(moon);
                ResourceGenerator.GenerateResources(moon);

                moon.orbitRadius = moonR;
                moon.orbitSpeed = OrbitalMechanics.MoonAngularSpeed(body, moonR);
                moon.spinSpeed = OrbitalMechanics.Spin(moon, Random.Range(0.7f, 1.3f));
                moon.orbitPhase = Random.Range(0f, 360f);
                moon.orbitDirection = Random.value < 0.85f ? 1 : -1;
                moon.inclination = Random.Range(-15f, 15f);
                moon.eccentricity = Random.Range(0f, 0.2f);
                ApplyHabitability(moon);
                POIGenerator.Populate(moon);

                body.moons.Add(moon);
                // Consecutive moons must clear each other's discs, not just be "some distance" apart.
                moonR += MaxMoonVisRadius * 2f + Random.Range(1.6f, 2.6f);
            }

            system.Add(body);

            // ---- Step outward to the next LANE ----
            // A planet doesn't occupy a line, it occupies a BAND: its own disc plus the whole reach of
            // its moon system, which sweeps inward and outward as the planet goes round.
            //
            // The old step added this planet's moon extent but nothing for the NEXT planet's, so a world
            // with a big moon system reached back INTO the previous planet's band and the two sets of
            // moons flew through each other. We now reserve room for both sides plus a visible gap, and
            // remember this body's outer reach so the next lane can clear it explicitly.
            float outerReach = OuterReach(body);   // moons already assigned -> real reach
            prevOuterReach = currentRadius + outerReach;

            // Reserve for the next planet reaching inward: its own disc + a typical moon system, then
            // widen the whole step by this star's spread. The reserved parts are NOT scaled — a moon
            // system is the size it is regardless of which sun it orbits — so only the empty lane between
            // worlds grows. That keeps the guarantee that nothing intersects while still letting a blue
            // giant's system read as genuinely vast next to a red dwarf's huddle.
            float spread = SystemSpread(currentStar);
            currentRadius = prevOuterReach + LaneGap + TypicalInnerReach
                          + (LaneGap + TypicalInnerReach) * (spread - 1f)
                          + Random.Range(0f, 2.5f * spread);
        }

        // Lean towards a living world: make sure at least one planet sits in the habitable zone.
        { var eh = EnsureHabitableWorld(system); while (eh.MoveNext()) yield return eh.Current; }

        // THE BACKSTOP. Everything above tries to lay bodies out with room to spare, but any of it can
        // be wrong — and EnsureHabitableWorld deliberately moves a planet after the fact. This pass is
        // what actually guarantees no two orbits ever intersect: it walks the system outward and pushes
        // anything overlapping until nothing does. Idempotent, so a correct layout passes through it
        // untouched.
        OrbitSafety.EnforceSystem(system, currentStar);

        if (!OrbitSafety.Validate(system, out string problem))
            Debug.LogWarning($"[OrbitSafety] {currentSystemName}: {problem}");

        lastSystem = system;
    }

    // ---- Orbit spacing ----
    // Two bodies orbiting one centre can never come closer than the difference of their orbital radii
    // (triangle inequality — inclination and tilt can't beat it). So keeping systems from intersecting
    // is purely a matter of reserving enough RADIAL room for everything each planet drags around with it.
    //
    // Sizes and clearances come from OrbitSafety — the single authority that also enforces spacing.
    // These used to be local copies of the same magic numbers, which is precisely how the layout and
    // the renderer drifted apart in the first place.
    const float MaxMoonVisRadius = 0.3f;    // widest moon disc: max(0.35, 12*0.05) * 0.5
    const float MoonSurfaceGap = OrbitSafety.MoonSurfaceGap;
    const float LaneGap = OrbitSafety.LaneGap;
    const float TypicalInnerReach = 8f;     // room reserved for the NEXT planet's disc + moon system

    static float OuterReach(CelestialBody body) => OrbitSafety.SystemReach(body);

    // If no life-friendly planet already sits in the (default-species) habitable zone, convert the
    // nearest planet into one and place it inside the zone.
    // An iterator, like the rest of the generation path: this bakes a WHOLE extra planet surface, and
    // run as a plain call it was one more multi-hundred-millisecond frame tacked onto the end of a
    // system — right where the loading screen looked most frozen.
    // Fully qualified, matching GenerateSystemStepped: the file's usings bring in
    // System.Collections.GENERIC, which supplies IEnumerator<T> but not the non-generic IEnumerator an
    // iterator method needs.
    System.Collections.IEnumerator EnsureHabitableWorld(List<CelestialBody> system)
    {
        // yield break, not return — this is an iterator block now, and a bare return is not legal in one.
        if (system.Count == 0 || currentStar == null || !currentStar.hasHabitableZone) yield break;
        if (!Habitability.GetZone(currentStar, SpeciesManager.Current, out float inner, out float outer)) yield break;

        foreach (var b in system)
            if (b.distanceFromStar >= inner && b.distanceFromStar <= outer &&
                (b.type == CelestialBodyType.RockyPlanet || b.type == CelestialBodyType.OceanPlanet))
                yield break; // already have a habitable-zone world

        float center = (inner + outer) * 0.5f;
        CelestialBody best = null; float bestD = float.MaxValue;
        foreach (var b in system)
        {
            float d = Mathf.Abs(b.distanceFromStar - center);
            if (d < bestD) { bestD = d; best = b; }
        }
        if (best == null) yield break;

        // Re-home it INSIDE ITS OWN LANE.
        //
        // This used to be `Random.Range(inner, outer)` — a random radius anywhere in the habitable zone,
        // ignoring the spacing the layout loop had just worked out. It cheerfully dropped this planet on
        // top of a neighbour, which is how worlds ended up close enough to fly through each other. The
        // planet may only move as far as its neighbours' bands allow; if that leaves no room inside the
        // zone, it stays exactly where it is and we just change its TYPE, which is the point anyway.
        float lo = inner, hi = outer;
        int idx = system.IndexOf(best);
        if (idx > 0)
        {
            var innerNb = system[idx - 1];
            lo = Mathf.Max(lo, innerNb.distanceFromStar + OrbitSafety.SystemReach(innerNb) + LaneGap + OrbitSafety.SystemReach(best));
        }
        if (idx >= 0 && idx < system.Count - 1)
        {
            var outerNb = system[idx + 1];
            hi = Mathf.Min(hi, outerNb.distanceFromStar - OrbitSafety.SystemReach(outerNb) - LaneGap - OrbitSafety.SystemReach(best));
        }

        if (hi > lo)
        {
            best.distanceFromStar = Random.Range(lo, hi);
            best.orbitRadius = best.distanceFromStar;
            best.orbitSpeed = OrbitalMechanics.PlanetAngularSpeed(currentStar, best.orbitRadius);
        }

        // FORCE THE ATTRIBUTES, NOT THE TYPE. The old path slammed the type to Ocean/Rocky and back-
        // derived everything; now this world is given the attributes a habitable world HAS — a real
        // terrestrial mass, a guaranteed magnetic field, water in the liquid band — and the classifier
        // names it whatever those attributes amount to (Terran, Ocean, Continental, Swamp…). That is the
        // same inversion the rest of generation just made, applied to the guaranteed-liveable world.
        float bestRel = best.distanceFromStar / Mathf.Max(0.5f, TempReference(currentStar));

        // A comfortable Earth-to-super-Earth mass. "best" may have been a gas giant that happened to
        // orbit near the zone centre, whose 7-13 mass would read absurdly on a habitable world.
        best.mass = Random.Range(2, 6);
        best.surfaceSize = MassRules.SurfaceSize(best.mass);

        // The guarantees a liveable world cannot be allowed to lose on a coin flip: a magnetic field (or
        // its atmosphere halves away under the 0.6 floor and it sterilises) and tectonics rolled fresh
        // under a terrestrial mass.
        best.hasMagneticField = true;
        best.type = CelestialBodyType.RockyPlanet;             // provisional, for the tectonics/air rolls
        best.hasTectonics = TectonicsRules.Roll(best.type, best.surfaceSize);

        SeedTerrain(best);
        // RE-BIAS THE CLIMATE. SeedTerrain rebuilds terrainParams (heat included) from scratch, throwing
        // away the distance-based climate the main loop applied — so re-apply it, or this world's
        // temperature would ignore its own orbit and its air would be rolled against a heat it never has.
        BiasHeat(best, best.distanceFromStar, currentStar);

        // WATER IN THE LIQUID BAND, guaranteed. A free roll could hand the one promised world a bone-dry
        // or fully-drowned surface; this keeps it in the range that classifies to a living world.
        {
            var wp = best.terrainParams;
            wp.seaLevel = Random.Range(0.35f, 0.75f);
            best.terrainParams = wp;
        }

        best.atmospheres = AtmosphereRules.RollAtmospheres(
            best.type, best.mass, true,
            AtmosphereRules.TectonicBonus(best), best.terrainParams.heat, bestRel);
        AtmosphereRules.ApplyWaterLoss(best);

        // NOW classify from the forced attributes, exactly as a normal world — and in the same order the
        // main pipeline uses: biosphere set BEFORE AmplifyBiome (so the lush/cold biome amplifications can
        // fire, see ApplyWorldPipeline), and CaptureNatural LAST (AmplifyBiome moves the climate).
        best.type = WorldClassifier.Physics(best, bestRel, isMoon: false);
        best.biosphereActive = BiosphereRules.GeneratesWithBiosphere(best);
        WorldClassifier.AmplifyBiome(best);
        TerraformVisuals.CaptureNatural(best);
        {
            var bsurf = PlanetTerrainGenerator.BuildStepped(best, best.terrainParams,
                                                           PlanetTerrainGenerator.Octaves,
                                                           s => best.surface = s);
            while (bsurf.MoveNext()) yield return bsurf.Current;
        }
        OreGenerator.Populate(best);
        best.resources = new ResourceDeposit();
        ResourceGenerator.GenerateResources(best);

        ApplyHabitability(best);
        POIGenerator.Populate(best);

        foreach (var m in best.moons) { m.distanceFromStar = best.distanceFromStar; ApplyHabitability(m); }
    }

    // NO SURFACE IS BAKED HERE — and that halves the cost of generating a galaxy.
    //
    // There used to be a provisional bake on this line. The caller (GenerateSystem) ALWAYS regenerates
    // body.surface once BiasHeat has set the world's real climate, building an entirely new
    // PlanetSurface/TerrainTile grid — so every planet in the galaxy had its terrain generated twice and
    // the first result thrown away untouched. At 42 Perlin samples per cell and grids up to 640x320,
    // that was the single largest cost in the load, and it bought nothing: nothing between here and the
    // real bake reads body.surface (GenerateResources works off type and size), and the terrain
    // generator draws no UnityEngine.Random, so removing it cannot shift the shared RNG stream either.
    /// THE ATTRIBUTE-FIRST PIPELINE (Advanced Planet Generation spec), shared by planets AND moons.
    ///
    /// The type is no longer chosen up front and mass derived from it. Instead every attribute is set in
    /// the spec's order and the type is CLASSIFIED from the result (WorldClassifier). One method for both
    /// planets and moons is the whole point: a moon that ends up massive enough, magnetised, temperate
    /// and wet classifies to the same temperate world a planet would — the spec's headline example.
    ///
    /// The caller must already have set `body.mass` and `body.distanceFromStar`. Everything from surface
    /// size onward happens here, in order:
    ///   size -> provisional type (mass alone) -> field -> tectonics -> terrain seed -> climate bias ->
    ///   water level -> atmosphere (with the inner-orbit cut) -> water loss -> CLASSIFY -> biome amplify
    ///   -> capture natural -> biosphere.
    void ApplyWorldPipeline(CelestialBody body, float rel, bool isMoon)
    {
        body.surfaceSize = MassRules.SurfaceSize(body.mass);

        // PROVISIONAL type from mass alone. Gas-giant and asteroid are decided by size outright, and the
        // atmosphere roll needs to know which so it hands a giant its deep air and a pebble none; a
        // terrestrial mass gets Rocky as a stand-in until the real classification at the end.
        body.type = ProvisionalType(body.mass, isMoon);

        body.hasMagneticField = AtmosphereRules.RollMagneticField(body.type, body.mass);
        body.hasTectonics = TectonicsRules.Roll(body.type, body.surfaceSize);

        SeedTerrain(body);                                     // seed, variance, ridge boost, capture
        BiasHeat(body, body.distanceFromStar, currentStar);    // climate follows distance

        SetBandWater(body, rel);                               // water level appropriate to the band

        // Atmosphere against the FINAL heat, cut for a close-in orbit. Water loss then trims any liquid a
        // thin-aired world could not hold (ice is spared — see ApplyWaterLoss).
        body.atmospheres = AtmosphereRules.RollAtmospheres(
            body.type, body.mass, body.hasMagneticField,
            AtmosphereRules.TectonicBonus(body), body.terrainParams.heat, rel);
        AtmosphereRules.ApplyWaterLoss(body);

        // THE TYPE, at last, from everything above.
        body.type = WorldClassifier.Physics(body, rel, isMoon);

        // BIOSPHERE BEFORE BIOME AMPLIFICATION. AmplifyBiome leans a world's terrain toward its
        // descriptive class (swamp wetter, tundra colder, desert drier), and that class — read from
        // WorldClassifier.Describe — only resolves to the lush/cold biomes when the world is actually
        // ALIVE. Setting biosphereActive first is what lets those cases fire at all; with it still at its
        // default false, every temperate world read as a lifeless desert/barren and only the desert
        // amplification ever ran. CaptureNatural then has to come LAST, because AmplifyBiome moves heat and
        // moisture and the natural climate terraforming lerps from must be the amplified one.
        body.biosphereActive = BiosphereRules.GeneratesWithBiosphere(body);
        WorldClassifier.AmplifyBiome(body);                    // make a flavoured world look like itself
        TerraformVisuals.CaptureNatural(body);                 // the real natural climate, post-amplify
    }

    /// The size-only physics class, used as a stand-in until the full classification. Gas giants and
    /// asteroids ARE just a matter of mass; everything in between is provisionally terrestrial.
    static CelestialBodyType ProvisionalType(float mass, bool isMoon)
    {
        if (mass >= WorldClassifier.GasGiantMassFloor) return CelestialBodyType.GasGiant;
        if (mass < WorldClassifier.AsteroidMassCeil) return isMoon ? CelestialBodyType.Moon : CelestialBodyType.Asteroid;
        return CelestialBodyType.RockyPlanet;
    }

    /// The water level a body is BORN with, before atmosphere loss trims it — set by the orbital band per
    /// the spec: near-dry inside the habitable zone (too hot, and the air is being stripped), a free
    /// full-range roll in the temperate and cold bands so everything from a desert to an ocean world (and
    /// ice worlds out cold) can appear.
    static void SetBandWater(CelestialBody body, float rel)
    {
        var p = body.terrainParams;
        p.seaLevel = rel < WorldClassifier.HotRel
            ? Random.Range(0.02f, 0.15f)     // scorched inner: near-dry
            : Random.Range(0.10f, 1.00f);    // temperate & cold: the whole range of coverage
        body.terrainParams = p;
    }

    // Stable terrain identity — must be set before generating any surface so both the low-res grid
    // and the high-res detailed map sample the same continents.
    static void SeedTerrain(CelestialBody body)
    {
        body.terrainSeed = Random.Range(0f, 10000f);
        body.continentFrequency = Mathf.Clamp(body.surfaceSize * 0.32f, 2.5f, 8f);
        TerrainVariance.Apply(body);   // give every world a distinct terrain character
        if (body.hasTectonics) TectonicsRules.BoostRidge(body);   // active plates fold up more mountains
        // The climate nature gave it. Terraforming lerps FROM here (TerraformVisuals), so it has to be
        // captured before anything moves it.
        TerraformVisuals.CaptureNatural(body);
    }

    void ApplyHabitability(CelestialBody body)
    {
        var species = SpeciesManager.Current;
        body.isHabitable = Habitability.IsHabitable(currentStar, species, body.type, body.distanceFromStar);
        body.habitability = Habitability.Rate(currentStar, species, body);
        body.terraformability = Habitability.Terraformability(currentStar, species, body);
    }

    int RollMoonCount(CelestialBodyType type)
    {
        switch (type)
        {
            case CelestialBodyType.GasGiant: return Random.Range(0, 5);
            case CelestialBodyType.RockyPlanet:
            case CelestialBodyType.VolcanicPlanet:
            case CelestialBodyType.IcePlanet: return Random.Range(0, 3);
            case CelestialBodyType.BarrenPlanet:
            case CelestialBodyType.OceanPlanet: return Random.Range(0, 2);
            default: return 0;
        }
    }

    /// The distance at which this star's warmth is Earth-normal — the yardstick every climate roll and
    /// every heat bias measures against.
    ///
    /// Now the SAME compressed law the habitable zone uses (StarDatabase.ReferenceDistance). It used to
    /// be its own copy of `sqrt(L) * AU`, which meant the two agreed only by coincidence — and around a
    /// bright star they did not agree at all: the zone said "temperate at 60 units" while every planet in
    /// the system sat between 8 and 30 and was therefore classified as scorched.
    static float TempReference(StarData star) => StarDatabase.ReferenceDistance(star);

    /// How much wider this star's system is laid out than a Sun-like one's.
    ///
    /// Tied to the same FluxScale as the habitable zone, so THE TWO CAN NEVER DRIFT APART: a star that
    /// pushes its zone outward pushes its planets outward with it, and a habitable world therefore lands
    /// among its siblings rather than ten times further out. That was the actual bug — not that the zone
    /// was in the wrong place, but that nothing else knew where it had gone.
    ///
    /// Damped to 70% of the full flux scale so a blue giant's system does not sprawl past what the camera
    /// can frame, then floored at 1 and multiplied by a flat 1.18.
    ///
    /// THE FLOOR MATTERS. Without it the damped lerp returns 0.81 for a red dwarf, so the dimmest systems
    /// would have come out TIGHTER than before — the opposite of "spread the planets out a little", and
    /// on precisely the systems that are already the most cramped. Flooring at 1 means the 1.18 is a
    /// genuine across-the-board loosening, and brighter stars widen from there.
    static float SystemSpread(StarData star)
        => 1.18f * Mathf.Max(1f, Mathf.Lerp(1f, StarDatabase.FluxScale(star), 0.70f));

    // Bias a world's terrain temperature by how close it is to the star: closer = hotter climate,
    // further = colder. Call before generating the surface so biomes reflect it.
    static void BiasHeat(CelestialBody b, float distance, StarData star)
    {
        float rel = TempReference(star) / Mathf.Max(1f, distance);    // >1 hot (close), <1 cold (far)
        var p = b.terrainParams;
        p.heat = Mathf.Clamp(rel * Random.Range(0.9f, 1.15f), 0.45f, 1.85f);
        b.terrainParams = p;
    }

    // Rolls the centre of the system: almost always a single sun, occasionally binary/ternary,
    // very rarely a black hole.
    void RollStarSystem()
    {
        stars = new List<StarData>();

        if (Random.value < 0.015f)   // very rare black hole
        {
            isBlackHole = true;
            stars.Add(StarDatabase.BlackHole());
            currentStar = stars[0];
            currentStarType = currentStar.type;
            return;
        }

        isBlackHole = false;
        float c = Random.value;
        int count = c < 0.05f ? 3 : (c < 0.20f ? 2 : 1);   // ~5% ternary, ~15% binary, ~80% single (common enough to meet)

        // A single star rolls the realistic (red-dwarf-heavy) distribution. A CLUSTER instead rolls a
        // flatter spread AND keeps its suns to DISTINCT spectral classes — so a binary/ternary reads as
        // genuinely different, differently-coloured suns (a red dwarf beside a white F, say) rather than
        // two near-identical red dwarfs. With seven classes and at most three suns, distinctness is always
        // reachable; the guard just stops an unlucky run of repeats.
        var usedTypes = new HashSet<StarType>();
        for (int i = 0; i < count; i++)
        {
            StarType t;
            int guard = 0;
            do { t = count > 1 ? RollClusterStarType() : RollStarType(); }
            while (count > 1 && usedTypes.Contains(t) && guard++ < 16);
            usedTypes.Add(t);
            stars.Add(StarDatabase.Get(t));
        }

        currentStar = StarDatabase.Combine(stars);
        currentStarType = currentStar.type;
    }

    // Give every sun in the cluster (and the combined star) a unique name derived from the system. In a
    // multi-star system the suffix goes by MASS — the most massive sun is "<System> A", then B, then C —
    // independent of generation order. The `stars` list order itself is left alone (the renderer keys the
    // inner pair off stars[0]/[1]); only the NAMES are ranked.
    void NameStars(string systemName)
    {
        if (stars.Count == 1)
        {
            stars[0].name = systemName;
        }
        else if (stars.Count > 1)
        {
            var byMass = new List<StarData>(stars);
            byMass.Sort((a, b) => b.mass.CompareTo(a.mass));   // most massive first
            for (int rank = 0; rank < byMass.Count; rank++)
                byMass[rank].name = $"{systemName} {(char)('A' + rank)}";
        }
        if (currentStar != null) currentStar.name = systemName;
    }

    StarType RollStarType()
    {
        float roll = Random.value;
        if (roll < 0.45f) return StarType.M;
        if (roll < 0.65f) return StarType.K;
        if (roll < 0.80f) return StarType.G;
        if (roll < 0.90f) return StarType.F;
        if (roll < 0.96f) return StarType.A;
        if (roll < 0.99f) return StarType.B;
        return StarType.O;
    }

    // A flatter spread across the spectral classes for the suns of a bound cluster, so their colours span a
    // visibly wider range than the red-dwarf-heavy single-star odds would give. Paired with the distinct-
    // type guarantee in RollStarSystem, a binary/ternary shows off genuinely different suns.
    StarType RollClusterStarType()
    {
        float roll = Random.value;
        if (roll < 0.22f) return StarType.M;
        if (roll < 0.42f) return StarType.K;
        if (roll < 0.60f) return StarType.G;
        if (roll < 0.76f) return StarType.F;
        if (roll < 0.88f) return StarType.A;
        if (roll < 0.96f) return StarType.B;
        return StarType.O;
    }

}
