// ============================================================================================
// WORLD CONSISTENCY — EditMode tests (Window > General > Test Runner > EditMode)
//
// The 2026-10-10 Mirfak report: a 35 °C grassland "terran world" orbiting just outside a molten world,
// inside the runaway-greenhouse zone of its sun. These tests hold the generator to the rules
// WorldConsistency checks at the end of every galaxy:
//
//   * starlight falls outward — the law and the band every world's heat is drawn from;
//   * no world's heat is one its orbit could not give it;
//   * no temperate type, living biosphere or "home" rating outside the habitable zone, unless its real
//     temperature explains it (recorded as climateException);
//   * every starting option, on every difficulty and for every species, lies inside the zone;
//   * the Terran cradle sits on the Earth ring, and is never a belt rock;
//   * the Mirfak II case itself is caught and re-derived into a hot world.
//
// In Assets/Tests/Editor, so it compiles into Assembly-CSharp-Editor and can see the game's code, which
// has no assembly definitions of its own. Guarded on UNITY_INCLUDE_TESTS so that a project opened
// without the Test Framework's references cannot fail to compile because of a test file.
//
// The galaxy sweeps generate real surfaces and take a minute or two; they are in the "Slow" category.
// ============================================================================================
#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class WorldConsistencyTests
{
    const int SeedsPerDifficulty = 6;
    const int SeedsPerSpecies = 3;

    // ---- The law ------------------------------------------------------------------------------

    [Test]
    public void StarlightFallsOutward()
    {
        float prevK = float.MaxValue, prevLo = float.MaxValue, prevHi = float.MaxValue;
        for (float rel = 0.2f; rel <= 6f; rel += 0.01f)
        {
            float k = InsolationRules.KelvinAt(rel);
            InsolationRules.HeatBand(rel, out float lo, out float hi);
            Assert.LessOrEqual(k, prevK + 0.001f, $"KelvinAt rose outward at rel {rel:0.00}");
            Assert.LessOrEqual(lo, prevLo + 0.0001f, $"band floor rose outward at rel {rel:0.00}");
            Assert.LessOrEqual(hi, prevHi + 0.0001f, $"band ceiling rose outward at rel {rel:0.00}");
            Assert.LessOrEqual(lo, hi);
            prevK = k; prevLo = lo; prevHi = hi;
        }
    }

    [Test]
    public void NoOrbitInsideTheZoneIsTemperate()
    {
        for (float rel = 0.2f; rel < StarDatabase.HzInnerRel; rel += 0.01f)
        {
            InsolationRules.HeatBand(rel, out float lo, out _);
            float c = PlanetTemperature.BaseCelsius(lo, 0f, CelestialBodyType.RockyPlanet);
            Assert.GreaterOrEqual(c, InsolationRules.InnerFloorC - 0.5f, $"rel {rel:0.00} can be {c:0} °C");
        }
    }

    [Test]
    public void ClampToOrbitLandsInsideTheBand()
    {
        foreach (float rel in new[] { 0.36f, 0.52f, 0.7f, 0.95f, 1.3f, 1.8f, 3.6f })
            foreach (float heat in new[] { 0.05f, 0.5f, 1f, 1.5f, 3f, 8f })
                Assert.AreEqual(0f, InsolationRules.OrbitMismatch(InsolationRules.ClampToOrbit(heat, rel), rel), 0.0001f);
    }

    [Test]
    public void TerranCradleBelongsOnTheEarthRing()
    {
        var star = StarDatabase.Get(StarType.G);
        var terrans = SpeciesDatabase.Get(0);
        Habitability.PlacementZone(star, terrans, out float inner, out float outer);
        var radii = new float[PlacementRings.Count];
        PlacementRings.Radii(star, radii);
        float air = AtmosphereRules.Quantize(Mathf.Clamp(terrans.cradleMass, terrans.minAtmospheres, terrans.maxAtmospheres));
        float heat = GalaxyGenerator.CradleHeat(terrans, air / AtmosphereRules.ThicknessReference,
                                                GalaxyGenerator.BestTypeFor(terrans));
        int ring = PlacementRings.RingForHeat(star, radii, inner, outer, heat);
        Assert.AreEqual(3, ring, "a Terran cradle around a G star should be on ring 4 (index 3)");
        Assert.AreEqual(0f, InsolationRules.OrbitMismatch(heat, InsolationRules.RelOf(star, radii[ring])), 0.0001f,
            "the Earth ring's own starlight should give a Terran cradle its climate without clamping");
    }

    // ---- The reported world ---------------------------------------------------------------------

    [Test]
    public void MirfakTwoIsCaughtAndComesOutHot()
    {
        var star = StarDatabase.Get(StarType.G);
        var terrans = SpeciesDatabase.Get(0);
        float r = StarDatabase.ReferenceDistance(star);

        // Mirfak II as the screenshot shows it: mass 0.9, 0.8 atmospheres, a living grassland at 35 °C —
        // on an orbit about half the Earth-warmth distance from a G sun, just outside a molten world.
        var b = new CelestialBody(CelestialBodyType.RockyPlanet)
        {
            name = "Mirfak II", mass = 0.9f, atmospheres = 0.8f, hasMagneticField = true,
            biosphereActive = true, distanceFromStar = 0.47f * r, orbitRadius = 0.47f * r, hostStar = star
        };
        b.surfaceSize = MassRules.SurfaceSize(b.mass);
        var p = b.terrainParams;
        p.heat = PlanetTemperature.HeatForCelsius(35f, b.atmosphereThickness, b.type);
        p.seaLevel = 0.45f;
        b.terrainParams = p;
        Assert.AreEqual(35f, PlanetTemperature.BodyAverageCelsius(b), 0.5f);

        string why = WorldConsistency.Problem(b, star, terrans, out _);
        Assert.IsNotNull(why, "a 35 °C living world inside the runaway zone must be flagged");

        Random.InitState(20261010);
        SolarSystemGenerator.ReconcileClimate(b, star);

        Assert.IsNull(WorldConsistency.Problem(b, star, terrans, out _));
        Assert.Greater(PlanetTemperature.BodyAverageCelsius(b), 100f, "re-derived, Mirfak II should be a hot world");
        Assert.IsFalse(b.biosphereActive);
        Assert.AreNotEqual(CelestialBodyType.RockyPlanet, b.type);
        Assert.AreNotEqual(CelestialBodyType.OceanPlanet, b.type);
    }

    // ---- Whole galaxies --------------------------------------------------------------------------

    [Test, Category("Slow")]
    public void EveryDifficultyStartsInTheZone()
    {
        var difficulties = new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard };
        Sweep((gen, log) =>
        {
            foreach (var diff in difficulties)
                for (int seed = 1; seed <= SeedsPerDifficulty; seed++)
                {
                    GameConfig.CurrentDifficulty = diff;
                    Generate(gen, 0, 1000 * (int)diff + seed, 4, $"{diff} seed {seed}", log);
                }
        });
    }

    [Test, Category("Slow")]
    public void EverySpeciesStartsInTheZone()
    {
        Sweep((gen, log) =>
        {
            GameConfig.CurrentDifficulty = Difficulty.Easy;   // the most starting options to check
            for (int s = 0; s < SpeciesDatabase.All.Count; s++)
                for (int seed = 1; seed <= SeedsPerSpecies; seed++)
                    Generate(gen, s, 5000 + 100 * s + seed, 2, $"{SpeciesDatabase.Get(s).name} seed {seed}", log);
        });
    }

    static void Sweep(System.Action<SolarSystemGenerator, List<string>> body)
    {
        var go = new GameObject("WorldConsistencyTests generator");
        var gen = go.AddComponent<SolarSystemGenerator>();
        var savedDifficulty = GameConfig.CurrentDifficulty;
        int savedSpecies = SpeciesManager.CurrentIndex;
        var log = new List<string>();
        try { body(gen, log); }
        finally
        {
            GameConfig.CurrentDifficulty = savedDifficulty;
            SpeciesManager.Select(savedSpecies);
            HomeworldOnboarding.ForgetWorlds();
            Object.DestroyImmediate(go);
        }
        foreach (var line in log) Debug.Log($"[WorldConsistencyTests] {line}");
    }

    static void Generate(SolarSystemGenerator gen, int speciesIndex, int seed, int systems, string label, List<string> log)
    {
        SpeciesManager.Select(speciesIndex);
        var species = SpeciesManager.Current;
        HomeworldOnboarding.ForgetWorlds();
        Random.InitState(seed);
        var galaxy = GalaxyGenerator.Generate(gen, systems, species);
        log.Add($"{label}: {WorldConsistency.LastFound} world(s) repaired during generation");

        var home = galaxy.Home;
        Assert.IsNotNull(home, label);
        Assert.Greater(HomeworldOnboarding.Options.Count, 0, $"{label}: no starting option");

        // Starting options: inside the star's zone, with a climate their orbit gives them.
        foreach (var o in HomeworldOnboarding.Options)
        {
            var star = o.hostStar != null ? o.hostStar : home.combinedStar;
            Assert.IsTrue(StarDatabase.InZone(star, o.distanceFromStar),
                $"{label}: starting option {o.name} at {o.distanceFromStar:0.0} is outside {star.hzInner:0.0}-{star.hzOuter:0.0}");
            Assert.LessOrEqual(InsolationRules.OrbitMismatch(o.terrainParams.heat, InsolationRules.RelOf(star, o.distanceFromStar)),
                WorldConsistency.HeatSlack, $"{label}: starting option {o.name}'s climate contradicts its orbit");
        }
        var cradle = HomeworldOnboarding.Cradle;
        Assert.IsNotNull(cradle, label);
        Assert.AreEqual(0, cradle.beltId, $"{label}: the cradle is a belt rock");

        foreach (var sys in galaxy.systems)
        {
            // No world contradicts its orbit, and no temperate world sits outside the zone unexplained.
            foreach (var b in sys.AllBodies())
            {
                var star = b.hostStar != null ? b.hostStar : sys.combinedStar;
                string why = WorldConsistency.Problem(b, star, species, out _);
                Assert.IsNull(why, $"{label}, {sys.name}: {b.name} — {why}");
            }

            // Warmer inward: an outer planet is never hotter than its inner neighbour's orbit allows, and
            // the law itself never runs the other way. (Jitter can swap two neighbours whose bands overlap
            // — compressed inner rings around a dim star — but never by more than the band.)
            var planets = new List<CelestialBody>(sys.bodies);
            planets.Sort((x, y) => x.distanceFromStar.CompareTo(y.distanceFromStar));
            for (int i = 1; i < planets.Count; i++)
            {
                var a = planets[i - 1];
                var c = planets[i];
                if (sys.combinedStar == null || sys.combinedStar.isBlackHole) break;
                float relA = InsolationRules.RelOf(sys.combinedStar, a.distanceFromStar);
                float relC = InsolationRules.RelOf(sys.combinedStar, c.distanceFromStar);
                Assert.LessOrEqual(InsolationRules.KelvinAt(relC), InsolationRules.KelvinAt(relA) + 0.001f);
                InsolationRules.HeatBand(relA, out _, out float hiA);
                Assert.LessOrEqual(c.terrainParams.heat, hiA * (1f + WorldConsistency.HeatSlack),
                    $"{label}, {sys.name}: {c.name} is hotter from starlight than {a.name}, further in, could be");
            }
        }
    }
}
#endif
