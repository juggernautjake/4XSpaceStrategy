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
            // Type is chosen by ACTUAL temperature at this radius for this star.
            CelestialBodyType type = RollBodyByTemperature(currentRadius, currentStar);

            CelestialBody body = MakeBody(type);
            body.name = NameGenerator.PlanetName(systemName, i);

            // Orbital layout (data-authoritative so save/load & sandbox can round-trip it).
            body.distanceFromStar = currentRadius;
            body.orbitRadius = currentRadius;
            BiasHeat(body, currentRadius, currentStar);                       // climate follows distance
            body.surface = PlanetTerrainGenerator.GenerateSurface(body);      // regenerate with correct heat
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
                SeedTerrain(moon);
                BiasHeat(moon, moon.distanceFromStar, currentStar);           // same climate band as its planet
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
            float outerReach = OuterReach(body, moonCount, moonR);
            prevOuterReach = currentRadius + outerReach;

            // Reserve for the next planet reaching inward: its own disc + a typical moon system.
            currentRadius = prevOuterReach + LaneGap + TypicalInnerReach + Random.Range(0f, 2.5f);
        }

        // ---- Orbit spacing ----
    // Two bodies orbiting one centre can never come closer than the difference of their orbital radii
    // (triangle inequality — inclination and tilt can't beat it). So keeping systems from intersecting
    // is purely a matter of reserving enough RADIAL room for everything each planet drags around with it.

    const float MaxMoonVisRadius = 0.3f;    // moon scale = max(0.35, surfaceSize*0.05), surfaceSize <= 12
    const float MoonSurfaceGap = 0.9f;      // clear air between a planet's surface and its first moon
    const float LaneGap = 6f;               // visible empty space between one planet's band and the next
    const float TypicalInnerReach = 8f;     // room reserved for the NEXT planet's disc + moon system

    /// How far a finished body's system reaches from its own orbit — its disc, or its outermost moon
    /// plus that moon's radius. Used to keep bands apart after the fact.
    public static float SystemReach(CelestialBody b)
    {
        float discRadius = Mathf.Max(0.6f, b.surfaceSize * 0.08f) * 0.5f;
        float reach = discRadius;
        if (b.moons != null)
            foreach (var m in b.moons)
                reach = Mathf.Max(reach, m.orbitRadius + Mathf.Max(0.35f, m.surfaceSize * 0.05f) * 0.5f);
        return reach;
    }

    // How far this planet's system reaches beyond its own orbit: its disc, or the outermost moon's
    // orbit plus that moon's own radius — whichever is greater.
    static float OuterReach(CelestialBody body, int moonCount, float lastMoonR)
    {
        float discRadius = Mathf.Max(0.6f, body.surfaceSize * 0.08f) * 0.5f;
        if (moonCount <= 0) return discRadius;
        // lastMoonR has already been advanced past the final moon, so step back one spacing.
        float outermost = lastMoonR - (MaxMoonVisRadius * 2f + 1.6f);
        return Mathf.Max(discRadius, outermost + MaxMoonVisRadius + 0.5f);
    }

    // Lean towards a living world: make sure at least one planet sits in the habitable zone.
        EnsureHabitableWorld(system);

        return system;
    }

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
            lo = Mathf.Max(lo, innerNb.distanceFromStar + SystemReach(innerNb) + LaneGap + SystemReach(best));
        }
        if (idx >= 0 && idx < system.Count - 1)
        {
            var outerNb = system[idx + 1];
            hi = Mathf.Min(hi, outerNb.distanceFromStar - SystemReach(outerNb) - LaneGap - SystemReach(best));
        }

        if (hi > lo)
        {
            best.distanceFromStar = Random.Range(lo, hi);
            best.orbitRadius = best.distanceFromStar;
            best.orbitSpeed = OrbitalMechanics.PlanetAngularSpeed(currentStar, best.orbitRadius);
        }

        bool cool = currentStarType == StarType.M || currentStarType == StarType.K;
        best.type = cool ? CelestialBodyType.OceanPlanet : CelestialBodyType.RockyPlanet;
        SeedTerrain(best);
        best.surface = PlanetTerrainGenerator.GenerateSurface(best);
        OreGenerator.Populate(best);
        best.resources = new ResourceDeposit();
        ResourceGenerator.GenerateResources(best);

        ApplyHabitability(best);
        POIGenerator.Populate(best);

        foreach (var m in best.moons) { m.distanceFromStar = best.distanceFromStar; ApplyHabitability(m); }
    }

    CelestialBody MakeBody(CelestialBodyType type)
    {
        CelestialBody body = new(type) { id = _idCounter++ };
        body.surfaceSize = RollSurfaceSize(type, currentStarType);
        SeedTerrain(body);
        body.surface = PlanetTerrainGenerator.GenerateSurface(body);
        OreGenerator.Populate(body);
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
    CelestialBodyType RollBodyByTemperature(float distance, StarData star)
    {
        float rel = distance / Mathf.Max(0.5f, TempReference(star));   // <1 hot, ~1 temperate, >1 cold
        float r = Random.value;

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
            if (r < 0.45f) return CelestialBodyType.OceanPlanet;
            if (r < 0.85f) return CelestialBodyType.RockyPlanet;
            return CelestialBodyType.BarrenPlanet;
        }

        if (rel < 3f)                          // cool
        {
            if (r < 0.45f) return CelestialBodyType.IcePlanet;
            if (r < 0.70f) return CelestialBodyType.RockyPlanet;
            if (r < 0.90f) return CelestialBodyType.GasGiant;
            return CelestialBodyType.BarrenPlanet;
        }

        // Cold outer system
        if (r < 0.50f) return CelestialBodyType.GasGiant;
        if (r < 0.80f) return CelestialBodyType.IcePlanet;
        if (r < 0.92f) return CelestialBodyType.BarrenPlanet;
        return CelestialBodyType.Asteroid;
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

    int RollSurfaceSize(CelestialBodyType type, StarType starType)
    {
        switch (type)
        {
            // Wider spread so bodies (and their maps) vary noticeably in size.
            case CelestialBodyType.GasGiant:       return Random.Range(18, 32);
            case CelestialBodyType.IcePlanet:      return Random.Range(7, 21);
            case CelestialBodyType.OceanPlanet:    return Random.Range(9, 23);
            case CelestialBodyType.RockyPlanet:    return Random.Range(6, 20);
            case CelestialBodyType.VolcanicPlanet: return Random.Range(6, 18);
            case CelestialBodyType.BarrenPlanet:   return Random.Range(5, 17);
            case CelestialBodyType.Moon:           return Random.Range(3, 13);
            case CelestialBodyType.Asteroid:       return Random.Range(3, 8);
            default:                               return 10;
        }
    }
}
