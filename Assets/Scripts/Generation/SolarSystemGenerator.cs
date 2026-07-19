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

    public List<CelestialBody> GenerateSystem()
    {
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

        float currentRadius = Random.Range(9f, 12f);   // clear the star + inner-moon reach
        float prevOuterReach = 0f;                     // outermost point the previous planet's system reaches

        for (int i = 0; i < bodyCount; i++)
        {
            // A body's MASS (0..1) is rolled first, independently, and then drives two things at once so a
            // world's size and type agree: in the size-sensitive outer bands it decides Gas Giant vs Ice vs
            // Barren vs Asteroid (the request's "gas giants ... the largest of planet sizes", asteroids the
            // smallest), and it sets the body's actual surfaceSize within its type's range. The type
            // FREQUENCIES are unchanged from the old bare-random roll — the mass cutoffs below reproduce the
            // exact same proportions — so this links size to type without rebalancing which types appear
            // (the one part of Advanced Generation the planning doc flagged as needing playtest calibration
            // is deliberately left untouched: this is the safe, proportion-preserving half of it).
            float mass = Random.value;

            // Type is chosen by ACTUAL temperature at this radius for this star, with mass breaking the
            // size-sensitive ties in the outer (cool/cold) bands.
            CelestialBodyType type = RollBodyByTemperature(currentRadius, currentStar, mass, out float rolledWaterLevel);

            CelestialBody body = MakeBody(type, mass);
            body.name = NameGenerator.PlanetName(systemName, i);

            // The SAME roll that decided Ocean/Rocky/Barren in the temperate band also sets the body's
            // actual water coverage, so the type and the map agree (a bare-minimum "Ocean" world reads as
            // mostly islands; a maxed-out one is fully drowned — exactly the request's own description
            // of the Water Level slider). Bands where waterLevel wasn't rolled (-1 sentinel) are
            // untouched, keeping their existing TerrainVariance-rolled elevation exactly as before.
            if (rolledWaterLevel >= 0f)
            {
                var wp = body.terrainParams;
                wp.elevation = PlanetTerrainGenerator.ElevationFromWaterLevel(rolledWaterLevel);
                body.terrainParams = wp;
            }

            // Orbital layout (data-authoritative so save/load & sandbox can round-trip it).
            body.distanceFromStar = currentRadius;
            body.orbitRadius = currentRadius;
            BiasHeat(body, currentRadius, currentStar);                       // climate follows distance
            TerraformVisuals.CaptureNatural(body);   // re-capture: BiasHeat is the world's real natural climate, not the pre-bias variance SeedTerrain rolled
            // Only worlds that start out warm and wet enough get a living biosphere for free; everything
            // else stays sterile until something like Microbial Seeding starts one (see BiosphereRules).
            // MUST be set before the surface below is baked, or the very first render of a qualifying
            // world would still come out sterile (the flag the classifier reads would still be false).
            body.biosphereActive = BiosphereRules.GeneratesWithBiosphere(body);
            body.surface = PlanetTerrainGenerator.GenerateSurface(body);      // regenerate with correct heat
            // Ore has to be populated against THIS surface, not the provisional one MakeBody baked —
            // that one is already gone (a fresh grid, no tile carryover). Pre-existing bug, found while
            // reviewing an unrelated change: every top-level planet was generating with zero ore, because
            // OreGenerator.Populate used to run inside MakeBody, against the surface this line replaces.
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
            int moonCount = RollMoonCount(type);
            float planetVisRadius = Mathf.Max(0.6f, body.surfaceSize * 0.08f) * 0.5f;
            float moonR = planetVisRadius + MaxMoonVisRadius + MoonSurfaceGap;
            for (int m = 0; m < moonCount; m++)
            {
                CelestialBody moon = new(CelestialBodyType.Moon) { id = _idCounter++ };
                moon.name = NameGenerator.MoonName(body.name, m);
                moon.surfaceSize = Random.Range(3, 13);
                moon.distanceFromStar = body.distanceFromStar;   // shares the planet's solar distance

                // A moon is no longer stuck with the single airless "Moon" palette. It rolls a real
                // world-type from the SAME attributes a planet does — its temperature (taken from its
                // parent's solar distance) and, for a big enough moon, a Water Level draw — gated by its
                // small mass: only a moon large enough to hold air and surface water can come out temperate
                // or ocean, while a hot orbit can make any moon volcanic (Io) and a cold one icy (Europa),
                // and the rest stay airless rock. This is the request's "moons use the same generation
                // system as planets" — see RollMoonType. (Its body class stays a moon for orbit/spacing:
                // OrbitSafety keys moon-scale off parentBody, which is set below and restored on load.)
                float moonRel = moon.distanceFromStar / Mathf.Max(0.5f, TempReference(currentStar));
                moon.type = RollMoonType(moonRel, moon.surfaceSize, out float moonWaterLevel);

                // Atmosphere uses the MOON mass gate (ForMoon, not ForBody) so its air is capped by its
                // small mass whatever surface it rolled — a large ocean moon holds thin real air (enough to
                // pass the biosphere gate below), a small ice/volcanic one holds essentially none, matching
                // the request's "most moons ... no or thin atmosphere unless they are large moons". Tectonics
                // keys on the chosen type + size exactly like a planet's, so only a sizeable moon folds plates.
                moon.atmosphereThickness = AtmosphereRules.ForMoon(moon.type, moon.surfaceSize);
                moon.hasTectonics = TectonicsRules.Roll(moon.type, moon.surfaceSize);

                SeedTerrain(moon);
                // Feed the Water Level draw into the moon's real terrain AFTER SeedTerrain — TerrainVariance
                // (called inside it) resets terrainParams, so setting elevation earlier would be discarded;
                // this is exactly the ordering the planet path uses above. A -1 (no temperate roll) keeps
                // the variance-rolled elevation untouched.
                if (moonWaterLevel >= 0f)
                {
                    var wp = moon.terrainParams;
                    wp.elevation = PlanetTerrainGenerator.ElevationFromWaterLevel(moonWaterLevel);
                    moon.terrainParams = wp;
                }
                BiasHeat(moon, moon.distanceFromStar, currentStar);           // same climate band as its planet
                TerraformVisuals.CaptureNatural(moon);   // re-capture the post-bias climate as this moon's natural state
                // A large, warm, wet moon with an atmosphere starts out living, just like a qualifying
                // planet; GeneratesWithBiosphere still returns false for barren/airless/ice/volcanic moons,
                // so most moons stay sterile. Set before the bake so the first render agrees with the flag.
                moon.biosphereActive = BiosphereRules.GeneratesWithBiosphere(moon);
                moon.surface = PlanetTerrainGenerator.GenerateSurface(moon);
                OreGenerator.Populate(moon);
                ResourceGenerator.GenerateResources(moon);

                moon.orbitRadius = moonR;
                moon.orbitSpeed = OrbitalMechanics.MoonAngularSpeed(body, moonR);
                moon.spinSpeed = OrbitalMechanics.Spin(moon, Random.Range(0.7f, 1.3f));
                moon.orbitPhase = Random.Range(0f, 360f);
                moon.orbitDirection = Random.value < 0.85f ? 1 : -1;
                moon.inclination = Random.Range(-15f, 15f);
                moon.eccentricity = Random.Range(0f, 0.2f);
                moon.parentBody = body;
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

            // Reserve for the next planet reaching inward: its own disc + a typical moon system.
            currentRadius = prevOuterReach + LaneGap + TypicalInnerReach + Random.Range(0f, 2.5f);
        }

        // Lean towards a living world: make sure at least one planet sits in the habitable zone.
        EnsureHabitableWorld(system);

        // THE BACKSTOP. Everything above tries to lay bodies out with room to spare, but any of it can
        // be wrong — and EnsureHabitableWorld deliberately moves a planet after the fact. This pass is
        // what actually guarantees no two orbits ever intersect: it walks the system outward and pushes
        // anything overlapping until nothing does. Idempotent, so a correct layout passes through it
        // untouched.
        OrbitSafety.EnforceSystem(system, currentStar);

        if (!OrbitSafety.Validate(system, out string problem))
            Debug.LogWarning($"[OrbitSafety] {currentSystemName}: {problem}");

        return system;
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
    void EnsureHabitableWorld(List<CelestialBody> system)
    {
        if (system.Count == 0 || currentStar == null || !currentStar.hasHabitableZone) return;
        if (!Habitability.GetZone(currentStar, SpeciesManager.Current, out float inner, out float outer)) return;

        foreach (var b in system)
            if (b.distanceFromStar >= inner && b.distanceFromStar <= outer &&
                (b.type == CelestialBodyType.RockyPlanet || b.type == CelestialBodyType.OceanPlanet))
                return; // already have a habitable-zone world

        float center = (inner + outer) * 0.5f;
        CelestialBody best = null; float bestD = float.MaxValue;
        foreach (var b in system)
        {
            float d = Mathf.Abs(b.distanceFromStar - center);
            if (d < bestD) { bestD = d; best = b; }
        }
        if (best == null) return;

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

        bool cool = currentStarType == StarType.M || currentStarType == StarType.K;
        best.type = cool ? CelestialBodyType.OceanPlanet : CelestialBodyType.RockyPlanet;
        // Recomputed under the NEW type (atmosphere thickness depends on type, not just size) before the
        // biosphere check below reads it. Tectonics is re-rolled too — "best" may originally have been
        // whatever type happened to orbit near the zone centre (even a GasGiant/Asteroid, which never
        // roll tectonics), so its old roll doesn't carry over to a freshly-retyped Rocky/Ocean world.
        best.atmosphereThickness = AtmosphereRules.ForBody(best.type, best.surfaceSize);
        best.hasTectonics = TectonicsRules.Roll(best.type, best.surfaceSize);
        SeedTerrain(best);
        // This world was just force-retyped to a habitable Rocky/Ocean type specifically to guarantee the
        // system has one — it deserves the same biosphere check as any other qualifying world, computed
        // fresh under its NEW type rather than left at whatever its old type produced (or the false
        // default), and before the surface below bakes so the rendered terrain agrees with the flag.
        best.biosphereActive = BiosphereRules.GeneratesWithBiosphere(best);
        best.surface = PlanetTerrainGenerator.GenerateSurface(best);
        OreGenerator.Populate(best);
        best.resources = new ResourceDeposit();
        ResourceGenerator.GenerateResources(best);

        ApplyHabitability(best);
        POIGenerator.Populate(best);

        foreach (var m in best.moons) { m.distanceFromStar = best.distanceFromStar; ApplyHabitability(m); }
    }

    // NOTE: this bake is provisional — the caller (GenerateSystem) always regenerates body.surface a
    // second time once BiasHeat sets the world's real climate, which builds an entirely new
    // PlanetSurface/TerrainTile grid. Populating ore against THIS surface would place it on tiles that
    // get thrown away a moment later, so ore population deliberately happens in GenerateSystem instead,
    // against the final surface, once — not here.
    CelestialBody MakeBody(CelestialBodyType type, float mass)
    {
        CelestialBody body = new(type) { id = _idCounter++ };
        // Size from the same mass roll that chose the type, so the two agree: a body picked as a gas giant
        // because it was the most massive really is the largest, one picked as an asteroid the smallest.
        // Mapped into the type's own range, so the per-type spread matches what RollSurfaceSize gave.
        body.surfaceSize = SizeFromMass(type, mass);
        body.atmosphereThickness = AtmosphereRules.ForBody(body.type, body.surfaceSize);
        body.hasTectonics = TectonicsRules.Roll(body.type, body.surfaceSize);
        SeedTerrain(body);
        body.surface = PlanetTerrainGenerator.GenerateSurface(body);
        ResourceGenerator.GenerateResources(body);
        return body;
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
        body.habitability = Habitability.Rate(currentStar, species, body.type, body.distanceFromStar);
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

    // The distance at which this star delivers roughly Earth-like warmth (sqrt-of-luminosity law).
    // Planet type and climate are judged relative to this, so a bright star's "temperate" band sits
    // much further out than a dim red dwarf's.
    static float TempReference(StarData star)
    {
        float L = star != null ? Mathf.Max(0.02f, star.luminosity) : 1f;
        return Mathf.Sqrt(L) * StarDatabase.AU;
    }

    // Body type by ACTUAL temperature (physical distance vs the star's warmth) rather than ordinal
    // slot. Oceans only ever form in the temperate band, so no water worlds hug the star.
    //
    // `waterLevel` (out param) is -1 except in the temperate band, where it's a genuine, independent
    // Water Level roll (0 driest .. 1 fully covered) that the CALLER then feeds straight into the body's
    // actual terrain (see GenerateSystem, PlanetTerrainGenerator.ElevationFromWaterLevel). This is the
    // one band where the request's "planet type should emerge from Water Level" is safe to do exactly:
    // the cutoffs below (0.55 / 0.15) are chosen so a uniform roll reproduces the IDENTICAL Ocean/Rocky/
    // Barren proportions (45%/40%/15%) the old bare `r` draw already had — so this is a like-for-like
    // rewire (the type and the world's actual water coverage now come from the SAME number, instead of
    // two unrelated rolls that could disagree, e.g. an "OceanPlanet" that TerrainVariance happened to
    // roll bone-dry), not a balance change. Gas Giant / Ice / Asteroid frequency (the bands below) still
    // comes from Size/temperature-band weighting exactly as before — reworking those into a fully
    // attribute-driven pick would change how often each type appears across the whole galaxy, which
    // needs real playtesting to calibrate and isn't attempted here (see the dev-request planning doc).
    CelestialBodyType RollBodyByTemperature(float distance, StarData star, float mass, out float waterLevel)
    {
        float rel = distance / Mathf.Max(0.5f, TempReference(star));   // <1 hot, ~1 temperate, >1 cold
        float r = Random.value;
        waterLevel = -1f;

        if (rel < 0.45f)                       // scorching — right by the star
            return r < 0.65f ? CelestialBodyType.VolcanicPlanet : CelestialBodyType.BarrenPlanet;

        if (rel < 0.85f)                       // hot
        {
            if (r < 0.45f) return CelestialBodyType.RockyPlanet;
            if (r < 0.75f) return CelestialBodyType.VolcanicPlanet;
            return CelestialBodyType.BarrenPlanet;
        }

        if (rel <= 1.5f)                       // temperate (habitable band) — the ONLY place oceans form
        {
            waterLevel = r;   // the SAME draw that decides the type also becomes its water coverage
            if (waterLevel > 0.55f) return CelestialBodyType.OceanPlanet;
            if (waterLevel > 0.15f) return CelestialBodyType.RockyPlanet;
            return CelestialBodyType.BarrenPlanet;
        }

        if (rel < 3f)                          // cool — MASS sorts these by size (small -> large)
        {
            // Same proportions as the old r-based roll (Barren 10% / Rocky 25% / Ice 45% / GasGiant 20%),
            // just ordered by mass so the most massive fifth become the gas giants and the lightest tenth
            // the barren dwarfs — and SizeFromMass then makes their actual sizes agree.
            if (mass < 0.10f) return CelestialBodyType.BarrenPlanet;
            if (mass < 0.35f) return CelestialBodyType.RockyPlanet;
            if (mass < 0.80f) return CelestialBodyType.IcePlanet;
            return CelestialBodyType.GasGiant;
        }

        // Cold outer system — again mass-sorted: the biggest are gas giants, the smallest asteroids. Same
        // proportions as before (GasGiant 50% / Ice 30% / Barren 12% / Asteroid 8%).
        if (mass < 0.08f) return CelestialBodyType.Asteroid;
        if (mass < 0.20f) return CelestialBodyType.BarrenPlanet;
        if (mass < 0.50f) return CelestialBodyType.IcePlanet;
        return CelestialBodyType.GasGiant;
    }

    // A moon's world-type, rolled from the same attributes a planet uses (temperature + a Water Level
    // draw) but gated by its small mass. `rel` is the moon's temperature ratio — its parent's solar
    // distance over the star's temperate reference, exactly as RollBodyByTemperature reads it (<1 hot,
    // ~1 temperate, >1 cold).
    //
    // Only a LARGE moon (enough gravity to hold an atmosphere and keep surface water) can become a
    // temperate or ocean world — the request's own "a moon ... that has an atmosphere, has liquid water,
    // and is within the Habitable zone ... could very likely spawn as a temperate rocky type". A hot
    // orbit can turn any moon volcanic (Io's tidal volcanism needs no atmosphere), a cold one icy
    // (Europa's frozen shell likewise), and everything else stays the airless Moon rock that used to be
    // every moon's only look. Gas Giant and Asteroid are deliberately never rolled here — a moon is
    // neither. `waterLevel` is -1 except in the temperate band, where it's the same 0..1 draw the caller
    // feeds into the moon's real water coverage, so a bare-minimum ocean moon reads as islands and a
    // maxed one is fully drowned, identically to a planet.
    static CelestialBodyType RollMoonType(float rel, int surfaceSize, out float waterLevel)
    {
        waterLevel = -1f;
        bool large = surfaceSize >= AtmosphereRules.LargeMoonSurfaceSize;   // can hold air + surface water
        float r = Random.value;

        if (rel < 0.85f)                       // hot orbit: volcanic (Io) or bare sun-baked rock
            return r < 0.5f ? CelestialBodyType.VolcanicPlanet : CelestialBodyType.Moon;

        if (rel <= 1.5f)                       // temperate band — the only place a moon can be a living world
        {
            if (!large) return CelestialBodyType.Moon;   // too little mass to hold the air/water it needs
            waterLevel = r;                    // the SAME draw decides the type AND the moon's water coverage
            if (waterLevel > 0.6f)  return CelestialBodyType.OceanPlanet;
            if (waterLevel > 0.25f) return CelestialBodyType.RockyPlanet;
            return CelestialBodyType.BarrenPlanet;
        }

        // Cold outer orbit: an icy shell (frozen water needs no atmosphere) or airless rock.
        return r < 0.55f ? CelestialBodyType.IcePlanet : CelestialBodyType.Moon;
    }

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
        int count = c < 0.01f ? 3 : (c < 0.05f ? 2 : 1);   // ~1% ternary, ~4% binary, ~95% single
        for (int i = 0; i < count; i++) stars.Add(StarDatabase.Get(RollStarType()));

        currentStar = StarDatabase.Combine(stars);
        currentStarType = currentStar.type;
    }

    // Give every sun in the cluster (and the combined star) a unique name derived from the system.
    void NameStars(string systemName)
    {
        for (int i = 0; i < stars.Count; i++)
            stars[i].name = NameGenerator.StarName(systemName, i, stars.Count);
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

    // Each type's surface-size band (the same numbers RollSurfaceSize used). Kept as a range so a body's
    // size can be indexed by its MASS roll rather than an independent draw — see SizeFromMass.
    static void SizeRange(CelestialBodyType type, out int lo, out int hi)
    {
        switch (type)
        {
            // Wider spread so bodies (and their maps) vary noticeably in size.
            case CelestialBodyType.GasGiant:       lo = 18; hi = 32; break;
            case CelestialBodyType.IcePlanet:      lo = 7;  hi = 21; break;
            case CelestialBodyType.OceanPlanet:    lo = 9;  hi = 23; break;
            case CelestialBodyType.RockyPlanet:    lo = 6;  hi = 20; break;
            case CelestialBodyType.VolcanicPlanet: lo = 6;  hi = 18; break;
            case CelestialBodyType.BarrenPlanet:   lo = 5;  hi = 17; break;
            case CelestialBodyType.Moon:           lo = 3;  hi = 13; break;
            case CelestialBodyType.Asteroid:       lo = 3;  hi = 8;  break;
            default:                               lo = 10; hi = 10; break;
        }
    }

    // A body's surfaceSize from its 0..1 mass, mapped into its type's range: mass 0 = the smallest of that
    // type, mass 1 = the largest. Because mass ALSO chose the type in the size-sensitive bands, size and
    // type stay consistent across the whole system (massive -> gas giant -> large; light -> asteroid ->
    // tiny), which is the causal link the request asks for between Size and what a body becomes.
    static int SizeFromMass(CelestialBodyType type, float mass)
    {
        SizeRange(type, out int lo, out int hi);
        return Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(lo, hi, Mathf.Clamp01(mass))), lo, hi);
    }
}
