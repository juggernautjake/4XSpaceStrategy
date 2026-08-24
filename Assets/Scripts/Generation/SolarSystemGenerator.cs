using System.Collections.Generic;
using UnityEngine;

public class SolarSystemGenerator : MonoBehaviour
{
    // ============================================================================================
    // HOW MANY WORLDS A SYSTEM GETS — a budget, not a count
    //
    // There used to be a min/max body count, set from the New Game menu's "average planets per system"
    // slider. Both are gone, and the slider with them, because the two ways of deciding actively fought
    // each other: a slider that says "four planets" and a mass allowance that says "one gas giant, and
    // that is the whole allowance" cannot both be obeyed, and whichever won made the other meaningless.
    //
    // A system is now built by SPENDING. It gets a Solar System Mass allowance — 100 per solar mass of
    // its star or stars (MassRules.SystemBudget) — and lays down lanes outward from the star, taking the
    // cost of each world and each of its moons out of the pot, until the pot cannot fund another body.
    // A binary really does get to build a bigger system than a lone red dwarf; a lone red dwarf really
    // does end up with three or four small worlds. Nobody had to choose that; it falls out of the sum.
    //
    // The allowance is a CEILING and the generator is never required to reach it. What it guarantees is
    // the other direction: nothing is generated past it.
    // ============================================================================================

    /// The most orbital lanes a system will ever lay out. Nine, because that is how many placement
    /// rings a star has — see PlacementRings, which owns the ladder and the reasoning behind it.
    public const int MaxLanes = PlacementRings.Count;

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

        // THE ALLOWANCE. Everything below spends out of this and nothing may be generated once it is
        // gone — planets, their moons and every asteroid all come out of the same pot.
        float budget = MassRules.SystemBudget(stars);

        // Belt lane ids start at 1; 0 means "not in a belt", so the first belt in a system cannot be
        // confused with a body that has never been in one.
        int nextBeltId = 1;

        string systemName = NameGenerator.UniqueSystemName();
        currentSystemName = systemName;
        NameStars(systemName);

        // ---- THE RINGS -------------------------------------------------------------------------
        //
        // Nine fixed radii for this star, and the only decision per ring is fill it or skip it. See
        // PlacementRings for why the ladder is shaped the way it is, and for why the outward WALK it
        // replaces could not reliably put a planet in the habitable zone.
        var ringR = new float[PlacementRings.Count];
        PlacementRings.Radii(currentStar, ringR);
        var ringInZone = new bool[PlacementRings.Count];
        PlacementRings.InZone(currentStar, ringR, ringInZone);
        int habitableRing = PlacementRings.HabitableRing(currentStar, ringR, ringInZone);

        var fillRing = new bool[PlacementRings.Count];
        ChooseFilledRings(fillRing, habitableRing);

        // AT MOST ONE INCLINED WORLD PER SYSTEM. See RollInclination.
        bool inclinedAlready = false;

        // How far out the last placed body's whole system reaches. A ring inside this is SKIPPED
        // rather than filled and then shoved outward by OrbitSafety — being shoved would leave the
        // body off its ring, which is the one thing a fixed ladder must not allow. In practice only a
        // gas giant with a full retinue on a close-in ring ever triggers it.
        float clearedTo = 0f;

        // A world's Roman numeral is its position among the bodies that EXIST, not its ring index —
        // or a system whose first filled ring is the fourth would open with "<Star> IV".
        int placed = 0;

        // ============================================================================================
        // WHERE BELTS MAY GO, AND HOW MANY
        //
        // "Asteroid belts no closer than beyond the 3rd planet, out to the furthest orbit — anything
        // that small nearer in would have been swept up long ago."
        // "Belt count: max 4, and 4 very rare. 1 far more common, if a system gets one at all. A system
        // that rolls fewer gas giants may roll more belts instead."
        //
        // Two separate limits, and they are counted rather than rolled per lane, because both requests
        // are statements about the SYSTEM. A per-lane probability cannot express "at most four" or
        // "beyond the third planet" at all — it can only make them likely, and likely is what produced
        // the systems the request is complaining about.
        //
        // PLANETS, NOT RINGS. The gate is "beyond the 3rd PLANET", so it counts worlds actually built,
        // not ladder positions walked past. A system whose rings 1-3 were skipped for want of allowance
        // has not yet had three planets and still may not have a belt on ring 4 — which is right: the
        // reasoning is about how much material the inner system swept up, and a ring nothing accreted
        // on swept up nothing.
        //
        // Both counters are read by ChooseLane and neither is advisory — a belt that fails either test
        // becomes a terrestrial instead, and the lane is still filled.
        // ============================================================================================
        int planetsPlaced = 0, beltsPlaced = 0, giantsPlaced = 0;
        int beltCap = RollBeltCap();

        for (int ring = 0; ring < PlacementRings.Count; ring++)
        {
            if (!fillRing[ring]) continue;

            float currentRadius = ringR[ring];
            // Swallowed by the previous body's reach — skip it, UNLESS it is the ring the habitable zone
            // sits on, which is the one ring worth handing to OrbitSafety to sort out rather than losing.
            if (currentRadius < clearedTo && ring != habitableRing) continue;

            int lane = placed;
            // ---- THE HABITABLE RING IS PAID FOR FIRST ------------------------------------------
            //
            // Measured (tools/system-composition-check.mjs): without this, an M dwarf put a world in
            // its own habitable zone only 81% of the time. The ring was always CHOSEN — ChooseFilledRings
            // forces it in — and then two things could still take it away: the inner rings spent the
            // whole allowance before the loop reached it, or ChooseLane rolled it as a belt.
            //
            // So a terrestrial world's worth of mass is held back until the habitable ring has been
            // built, and that ring is never anything but a terrestrial. Both are cheap: the reserve is
            // 0.6 out of an allowance of at least 32, and the belt it displaces would have been one lane
            // of rubble in the one place a system most wants a planet.
            bool habitablePending = habitableRing >= 0 && ring <= habitableRing;
            float reserve = (habitablePending && ring < habitableRing) ? MassRules.TerrestrialMin : 0f;
            float spendable = Mathf.Max(0f, budget - reserve);

            // OUT OF ALLOWANCE. The cheapest thing a lane can hold is a single 0.1 asteroid, so once
            // even that is unaffordable the system is finished however many lanes are left. This is the
            // hard ceiling the request asks for: "generate no more mass after this point".
            //
            // ...EXCEPT while the habitable ring is still ahead of us, which is the entire purpose of
            // the reserve. Breaking here would leave the 0.6 held back and never spent — and that was
            // measurably happening: an M dwarf that rolled a gas giant on an inner ring ate its whole
            // 32-mass allowance and the loop exited before ever reaching the ring the zone is on.
            // Skipping the ring instead walks on to the one that matters.
            if (spendable < MassRules.AsteroidMin)
            {
                if (!habitablePending) break;
                continue;
            }

            // How far out this lane sits, as a multiple of the star's Earth-warmth distance. Everything
            // about what the lane may CONTAIN is decided from this one number — see ChooseLane.
            float rel = currentRadius / Mathf.Max(0.5f, TempReference(currentStar));
            // A belt is only on the table beyond the third planet, while the system is under its own
            // belt cap. See the counters above.
            bool beltsAllowed = planetsPlaced >= BeltMinPlanets && beltsPlaced < beltCap;
            LaneKind kind = ring == habitableRing
                ? LaneKind.Terrestrial
                : ChooseLane(rel, spendable, beltsAllowed, giantsPlaced);

            // How far the lane's contents reach either side of its orbit line. Filled in by whichever
            // branch runs, and used to step outward to the next lane.
            float laneReach = 0f;

            if (kind == LaneKind.AsteroidBelt)
            {
                // ============================================================================
                // A BELT — several bodies sharing ONE orbit
                //
                // "There can be multiple asteroids on the same orbit line; give them the same orbit
                // speed as each other so we don't get overlapping models." Both halves matter, and the
                // second is what makes the first safe: identical radius AND identical angular speed
                // means every rock holds its angle relative to every other rock forever. They can never
                // converge, so they never need to be spaced apart in radius the way two planets do.
                // ============================================================================
                int beltId = nextBeltId++;
                string beltName = NameGenerator.PlanetName(systemName, lane);
                int wanted = Random.Range(3, 8);

                // Taken ONCE, outside the loop, and copied to every member. Deriving it per rock would
                // give the same answer today and would be exactly the line somebody later "fixes" into
                // a per-body value, at which point the belt slowly shears itself apart.
                float beltSpeed = OrbitalMechanics.PlanetAngularSpeed(currentStar, currentRadius);

                int placed = 0;
                for (int a = 0; a < wanted; a++)
                {
                    if (spendable < MassRules.AsteroidMin) break;

                    CelestialBody rock = new(CelestialBodyType.Asteroid) { id = _idCounter++ };
                    rock.name = $"{beltName} {(char)('a' + a)}";
                    rock.mass = MassRules.RollAsteroid(spendable);
                    budget -= rock.mass; spendable -= rock.mass;
                    rock.distanceFromStar = currentRadius;
                    rock.orbitRadius = currentRadius;
                    rock.beltId = beltId;

                    ApplyWorldPipeline(rock, rel, isMoon: false);
                    ResourceGenerator.GenerateResources(rock);
                    {
                        var rsurf = PlanetTerrainGenerator.BuildStepped(rock, rock.terrainParams,
                                                                       PlanetTerrainGenerator.Octaves,
                                                                       s => rock.surface = s);
                        while (rsurf.MoveNext()) yield return rsurf.Current;
                    }
                    yield return null;
                    OreGenerator.Populate(rock);

                    rock.orbitSpeed = beltSpeed;                       // identical, by construction
                    rock.orbitPhase = Random.Range(0f, 360f);          // spread around the ring
                    rock.orbitDirection = 1;                           // ...and all going the same way
                    // A LITTLE tilt and eccentricity, and the same amount for every member: a belt whose
                    // rocks had individual inclinations would sweep through each other vertically even
                    // though their radii never differ.
                    rock.inclination = 0f;
                    rock.eccentricity = 0f;
                    rock.spinSpeed = RotationRules.Roll(rock.mass, isMoon: false);
                    rock.rotationDirection = RotationRules.RollDirection(isMoon: false);
                    rock.showRing = a == 0;    // one ring drawn for the lane, not five on top of each other

                    ApplyHabitability(rock);
                    POIGenerator.Populate(rock);

                    system.Add(rock);
                    laneReach = Mathf.Max(laneReach, OrbitSafety.DiscRadius(rock));
                    placed++;
                }

                if (placed == 0) break;   // could not afford even one rock: nothing more will fit

                // ONE BELT, however many rocks are in it — the request counts belts, and so does the
                // cap. Incremented here rather than at the top of the branch so a lane that could not
                // afford a single asteroid does not spend one of the system's four.
                beltsPlaced++;
            }
            else
            {
                // ============================================================================
                // A PLANET
                //
                // ATTRIBUTE-FIRST, as before: mass is settled here and the whole pipeline then sets
                // rotation, field, tectonics, climate, water and air and CLASSIFIES the type from them —
                // rather than picking a type and deriving mass from it. What changed is where the mass
                // comes from: the orbital band and the remaining allowance, rather than a size rank.
                // ============================================================================
                CelestialBody body = new(CelestialBodyType.RockyPlanet) { id = _idCounter++ };
                body.name = NameGenerator.PlanetName(systemName, lane);
                body.distanceFromStar = currentRadius;
                body.orbitRadius = currentRadius;

                if (kind == LaneKind.GasGiant)
                {
                    // Reserve a tenth for its moons before rolling, so a giant that eats the entire
                    // allowance still leaves itself something to be orbited by. Falls back to a
                    // terrestrial if the reservation puts the smallest giant out of reach.
                    float giant = MassRules.RollGasGiant(spendable * 0.92f);
                    body.mass = giant > 0f ? giant : MassRules.RollTerrestrial(TerrestrialBandMax(rel), spendable);
                }
                else
                {
                    body.mass = MassRules.RollTerrestrial(TerrestrialBandMax(rel), spendable);
                }
                budget -= body.mass; spendable -= body.mass;

                ApplyWorldPipeline(body, rel, isMoon: false);
                ResourceGenerator.GenerateResources(body);   // type is settled, so resources match it
                // STEPPED. A world's terrain is by far the most expensive step in the whole load, and
                // built in one go it was a single frame lasting as long as generating an entire planet.
                // Now it yields every few milliseconds from inside its own loop, so one enormous frame
                // becomes dozens of ordinary ones and the loading screen animates at a normal rate.
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
                body.orbitPhase = Random.Range(0f, 360f);
                body.orbitDirection = Random.value < 0.9f ? 1 : -1;
                body.inclination = RollInclination(isMoon: false, ref inclinedAlready);
                body.eccentricity = Random.Range(0f, 0.14f);

                ApplyHabitability(body);
                POIGenerator.Populate(body);

                // ---- Moons, out of this planet's own allowance ----
                //
                // A terrestrial world may spend a QUARTER of its mass on moons, a gas giant a tenth of
                // its (see MassRules.MoonBudget) — and neither may spend more than the SYSTEM has left,
                // because moons come out of the same pot as everything else. Like the system allowance,
                // it is a ceiling: most worlds spend a fraction of it and some spend none at all.
                float moonPot = Mathf.Min(MassRules.MoonBudget(body.mass), spendable);
                bool giantHost = body.mass >= WorldClassifier.GasGiantMassFloor;

                // ---- MOST WORLDS HAVE NO MOONS AT ALL ----
                //
                // "It is far too common for planets to spawn with multiple moons. Some planets should be
                // able to spawn by themselves with no moons."
                //
                // At 0.34 a rocky world went moonless one time in three, so having moons was the common
                // case by two to one — the opposite of our own system, where two of the four terrestrials
                // have none and a third has two captured rocks a few kilometres across. At 0.62 a bare
                // rocky world is the DEFAULT and a moon is worth noticing. Giants keep their retinues,
                // but a bare one is now a real outcome rather than a one-in-seven curiosity.
                if (Random.value < (giantHost ? 0.30f : 0.62f)) moonPot = 0f;

                // "I don't want Terrestrial planets to be able to generate more than 2-3 Moons. Gas
                // Giants should not be able to generate more than 3-4 Moons." Three and four are the
                // hard ceilings; the early-stop roll below is what makes the top of each range rare
                // rather than routine.
                int maxMoons = giantHost ? 4 : 3;
                float planetVisRadius = OrbitSafety.DiscRadius(body);
                float moonR = planetVisRadius + MaxMoonVisRadius + MoonSurfaceGap;

                for (int m = 0; m < maxMoons; m++)
                {
                    if (moonPot < MassRules.AsteroidMin) break;
                    // ...and a chance to simply stop, so the allowance is genuinely a maximum rather
                    // than a quota that always gets filled to the last decimal. Raised from 0.42 to 0.55
                    // alongside the moonless roll above: with the ceiling down to 3 and 4, this is what
                    // decides whether a world with moons has ONE or has its full complement, and one
                    // should be the common answer.
                    if (m > 0 && Random.value < 0.55f) break;

                    // A GIANT'S MOONS ARE CAPPED AT 1.5 EACH, whatever its allowance can afford.
                    // "Gas Giants should only be able to generate Moons with Mass up to 1.5 and below."
                    // Without this a mass-40 giant's 4.0 pot could go on one 4.0 moon — a super-Earth in
                    // orbit around a planet, which is a double system rather than a moon.
                    float moonCap = giantHost ? Mathf.Min(moonPot, GiantMoonMassMax) : moonPot;
                    float moonMass = MassRules.RollMoon(moonCap);
                    if (moonMass < MassRules.AsteroidMin) break;

                    CelestialBody moon = new(CelestialBodyType.Moon) { id = _idCounter++ };
                    moon.name = NameGenerator.MoonName(body.name, m);
                    moon.mass = moonMass;
                    moonPot -= moonMass;
                    budget -= moonMass;
                    moon.distanceFromStar = body.distanceFromStar;   // shares the planet's solar distance
                    // Set BEFORE the pipeline so the moon's grid size cap (MapMetrics keys off
                    // parentBody) and its classification both see the relationship.
                    moon.parentBody = body;

                    // ONE PIPELINE, moons included. `isMoon: true` is the only difference — it keeps a
                    // tiny or cold moon a Moon rather than an Asteroid/Barren, but a moon that ends up
                    // massive, magnetised, temperate and wet classifies to the same temperate world a
                    // planet would. That is the spec's headline: a big moon of a gas giant in the
                    // habitable zone can be a Terran world.
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
                    moon.orbitPhase = Random.Range(0f, 360f);
                    moon.orbitDirection = Random.value < 0.85f ? 1 : -1;
                    moon.inclination = RollInclination(isMoon: true, ref inclinedAlready);
                    moon.eccentricity = Random.Range(0f, 0.2f);
                    ApplyHabitability(moon);
                    POIGenerator.Populate(moon);

                    body.moons.Add(moon);
                    // Consecutive moons must clear each other's discs, not just be "some distance" apart.
                    moonR += MaxMoonVisRadius * 2f + Random.Range(1.6f, 2.6f);
                }

                system.Add(body);
                laneReach = OuterReach(body);   // moons already assigned -> real reach

                // A PLANET, for the belt gate — and a giant is one. "Beyond the 3rd planet" counts
                // worlds, and a gas giant is emphatically a world; excluding them would let a system of
                // three giants put a belt on ring 2 on the grounds that it had no planets yet.
                planetsPlaced++;
                if (body.mass >= WorldClassifier.GasGiantMassFloor) giantsPlaced++;
            }

            // ---- WHAT THIS RING'S CONTENTS REACH ----
            //
            // Nothing steps outward any more — the next radius is already decided. This is recorded
            // only so a ring the last body's moons would sit on top of can be skipped.
            clearedTo = currentRadius + laneReach + LaneGap;
            placed++;
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
    // The widest disc a moon can render at, halved to a radius. The largest moon in the game is one a
    // mass-40 gas giant spends its whole 4.0 allowance on, and MassRules.VisualDiameter puts that at
    // 0.44 * cbrt(4) = 0.70 across. Rounded up a little, because this is a RESERVATION: undersizing it
    // means the layout reserves too little room and OrbitSafety has to push the whole system outward
    // afterwards, which is exactly the case the layout exists to avoid.
    const float MaxMoonVisRadius = 0.4f;

    /// The largest a single moon of a GAS GIANT may be, whatever its host's allowance could afford.
    /// The request's number. Ganymede is 0.025 Earths, so 1.5 is already extremely generous — it is a
    /// ceiling against a giant spending its whole pot on one body, not a target.
    const float GiantMoonMassMax = 1.5f;
    const float MoonSurfaceGap = OrbitSafety.MoonSurfaceGap;
    const float LaneGap = OrbitSafety.LaneGap;
    // TypicalInnerReach (room the NEXT planet would need) is gone with the outward walk: on a fixed
    // ladder there is no "next radius" to reserve for, only a ring to skip. See PlacementRings.

    static float OuterReach(CelestialBody body) => OrbitSafety.SystemReach(body);

    // ============================================================================================
    // HOW MANY RINGS GET FILLED, AND WHICH
    //
    // "The default amount of celestial bodies per solar system should be around 5 (not a fixed number
    // but around this should be good). And it should not be uncommon for some solar systems to have
    // fewer than 3 Celestial bodies."
    //
    // Two rolls rather than one, because a single distribution cannot say both things. A triangular
    // peaked at five puts only about 7% of systems under three — which is uncommon, not "not
    // uncommon". So the common case is the bell, and one system in five instead rolls a genuinely
    // SPARSE layout: one to three bodies, drawn flat.
    //
    //     80%  bell over 1..9, mode 5      -> the ordinary system
    //     20%  flat over 1..3              -> the sparse one
    //
    // which lands about 19% of systems under three bodies and still leaves five the single most
    // likely answer. Measured against the old walk, which produced 8.6 planets around a Sun-like star
    // before its moons were counted.
    //
    // THE BUDGET IS STILL A CEILING ON TOP OF THIS. A target of nine around a red dwarf will run out
    // of allowance somewhere around ring five and simply stop, which is the correct interaction: the
    // target says how many rings we would LIKE to fill, the budget says what can be afforded.
    // ============================================================================================

    /// Triangular 0..1, peaked in the middle. The same shape (and the same reasoning) as
    /// MassRules.Bell, which is private to that file.
    static float Bell() => (Random.value + Random.value) * 0.5f;

    /// How likely each ring is to be chosen, before the habitable ring is forced in.
    ///
    /// Leaning to the middle rather than flat. A flat weight scatters a five-body system across the
    /// whole ladder as often as not, and a system whose only worlds are ring 1 and ring 9 reads as
    /// broken rather than as sparse. The middle is also where the interesting ground is — the zone,
    /// the frost line and the belt that sits at it.
    static readonly float[] RingWeight = { 0.55f, 0.75f, 0.95f, 1.00f, 1.00f, 0.95f, 0.85f, 0.70f, 0.55f };

    /// Decide which of the nine rings this system fills. `habitableRing` is forced in when the star
    /// has a zone at all, which is what guarantees a liveable world without moving one afterwards.
    void ChooseFilledRings(bool[] fill, int habitableRing)
    {
        for (int i = 0; i < fill.Length; i++) fill[i] = false;

        int target = Random.value < 0.20f
            ? Random.Range(1, 4)                                    // the sparse system: 1..3
            : Mathf.Clamp(1 + Mathf.RoundToInt(Bell() * 8f), 1, PlacementRings.Count);

        int chosen = 0;

        // The habitable ring first and unconditionally. It counts against the target, so forcing it
        // does not quietly make every system one body larger than it rolled.
        if (habitableRing >= 0 && habitableRing < fill.Length)
        {
            fill[habitableRing] = true;
            chosen = 1;
        }

        // Weighted draw without replacement for the rest.
        while (chosen < target)
        {
            float total = 0f;
            for (int i = 0; i < PlacementRings.Count; i++) if (!fill[i]) total += RingWeight[i];
            if (total <= 0f) break;                                 // every ring already taken

            float r = Random.value * total;
            for (int i = 0; i < PlacementRings.Count; i++)
            {
                if (fill[i]) continue;
                r -= RingWeight[i];
                if (r > 0f) continue;
                fill[i] = true;
                chosen++;
                break;
            }
        }
    }

    // ============================================================================================
    // AN INCLINED ORBIT IS A STORY, SO IT HAS TO BE RARE
    //
    // "There are still too many planets with orbital inclination. Having one with an inclination
    // should already be rare and not every solar system should generate one."
    //
    // It was not rare and it was not one: EVERY planet got Random.Range(-7, 7) and every moon
    // Random.Range(-15, 15), so inclination was universal and the only variable was how much. What
    // the report describes is exactly what that code does.
    //
    // Worlds form in a disc and mostly stay in it. A tilted orbit means something HAPPENED to that
    // world — a close pass, a capture, a collision — so it is a one-in-eight event, and never twice
    // in the same system, because two independent catastrophes in one system is not a story, it is
    // noise. Everything else keeps a fraction of a degree, which is enough to stop the orbit lines
    // drawing as one perfectly flat sheet.
    // ============================================================================================
    static float RollInclination(bool isMoon, ref bool alreadyInclined)
    {
        // A moon is a little likelier: it is far easier to tilt something held by a planet than
        // something held by a star, and our own system's irregular moons are the evidence.
        float chance = isMoon ? 0.15f : 0.12f;
        if (!alreadyInclined && Random.value < chance)
        {
            alreadyInclined = true;
            return isMoon ? Random.Range(-12f, 12f) : Random.Range(-9f, 9f);
        }
        return isMoon ? Random.Range(-1.5f, 1.5f) : Random.Range(-0.8f, 0.8f);
    }

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
            // NEVER PROMOTE A BELT MEMBER. A belt's rocks are safe sharing a lane precisely because they
            // are the same size and speed and hold their formation; turning one of them into an
            // Earth-mass world would put a planet-sized disc in the middle of a ring of rubble it now
            // overlaps, and OrbitSafety would then shove the whole lane apart to fix it — which is the
            // one thing a belt must not have done to it.
            if (b.beltId != 0) continue;

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

        // ---- MOVE IT TO A RING, NOT TO A RANDOM RADIUS ----------------------------------------------
        //
        // This used to be `Random.Range(lo, hi)`, and that was the second half of the reported bug. A
        // random radius inside the zone is not necessarily a radius the layout considers legal, so
        // OrbitSafety.EnforceSystem — which runs immediately after this — would push the world straight
        // back out past the star's clearance. The zone said one thing, the safety pass said another, and
        // the safety pass ran last. That is how a promoted homeworld ended up at 35.8 with its own
        // habitable zone ending at 33.9.
        //
        // A ring radius is legal by construction (PlacementRings clears the star's reach before it
        // returns anything), so moving to one cannot be undone by the pass that follows. Nearest ring to
        // the zone centre that also fits between this world's neighbours; if none does, the world stays
        // exactly where it is and only its TYPE changes — which was always the more important half.
        var rings = new float[PlacementRings.Count];
        PlacementRings.Radii(currentStar, rings);

        float wantCentre = (inner + outer) * 0.5f;
        float chosen = -1f, chosenD = float.MaxValue;
        for (int i = 0; i < PlacementRings.Count; i++)
        {
            if (rings[i] < lo || rings[i] > hi) continue;
            float d = Mathf.Abs(rings[i] - wantCentre);
            if (d < chosenD) { chosenD = d; chosen = rings[i]; }
        }

        if (chosen > 0f)
        {
            best.distanceFromStar = chosen;
            best.orbitRadius = chosen;
            best.orbitSpeed = OrbitalMechanics.PlanetAngularSpeed(currentStar, best.orbitRadius);
        }

        // FORCE THE ATTRIBUTES, NOT THE TYPE. The old path slammed the type to Ocean/Rocky and back-
        // derived everything; now this world is given the attributes a habitable world HAS — a real
        // terrestrial mass, a guaranteed magnetic field, water in the liquid band — and the classifier
        // names it whatever those attributes amount to (Terran, Ocean, Continental, Swamp…). That is the
        // same inversion the rest of generation just made, applied to the guaranteed-liveable world.
        float bestRel = best.distanceFromStar / Mathf.Max(0.5f, TempReference(currentStar));

        // A comfortable Earth-to-super-Earth mass. "best" may have been a gas giant that happened to
        // orbit near the zone centre, whose 10-40 mass would read absurdly on a habitable world.
        //
        // NOT budgeted against the system allowance, deliberately. This is a REPLACEMENT rather than an
        // addition — the body already exists and already spent its mass — and the swap is almost always
        // downward (a giant becoming an Earth hands mass back). A promoted asteroid can cost the system
        // a couple of Earths it had not planned for, and that is the right trade: the guaranteed
        // liveable world is the one thing in a system that is not negotiable.
        best.mass = MassRules.QuantizeTerrestrial(Random.Range(0.9f, 2.6f));
        best.surfaceSize = MassRules.SurfaceSize(best.mass);
        best.beltId = 0;                                       // no longer one rock among several

        // The guarantees a liveable world cannot be allowed to lose on a coin flip: a magnetic field (or
        // its atmosphere halves away under the 0.6 floor and it sterilises) and tectonics rolled fresh
        // under a terrestrial mass.
        //
        // The field comes from ROTATION now, so it is granted by spinning the world up rather than by
        // setting a flag — otherwise a world could carry a magnetosphere while the panel beside it
        // reported a rotation far too slow to drive one. Rolled inside the fast population (see
        // RotationRules) so the figure still varies between one game and the next.
        best.spinSpeed = Mathf.Max(RotationRules.MagneticFieldSpin + 2f,
                                   RotationRules.Roll(best.mass, isMoon: false));
        best.rotationDirection = RotationRules.RollDirection(isMoon: false);
        best.hasMagneticField = true;
        best.type = CelestialBodyType.RockyPlanet;             // provisional, for the tectonics/air rolls
        best.hasTectonics = TectonicsRules.Roll(best.type, best.mass);

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

        // ROTATION FIRST, because the magnetic field is now a CONSEQUENCE of it rather than an
        // independent coin flip: a world turning fast enough stirs its core and runs a dynamo, and one
        // that has been tidally braked does not. See RotationRules. Prograde by default; retrograde
        // happens, and is stored apart from the rate so that turning backwards never costs a world its
        // magnetosphere.
        body.spinSpeed = RotationRules.Roll(body.mass, isMoon);
        body.rotationDirection = RotationRules.RollDirection(isMoon);
        body.hasMagneticField = RotationRules.GeneratesField(body.type, body.mass, body.spinSpeed);
        body.hasTectonics = TectonicsRules.Roll(body.type, body.mass);

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
        // <=, matching WorldClassifier: 0.5 IS an asteroid, per the request.
        if (mass <= WorldClassifier.AsteroidMassCeil) return isMoon ? CelestialBodyType.Moon : CelestialBodyType.Asteroid;
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
        // TectonicsRules.BoostRidge USED TO RUN HERE, multiplying a tectonic world's ridge amplitude so
        // it folded up more mountains "everywhere". That was the best available stand-in while ridge was
        // an independent noise field and there was no fault GEOMETRY to place ranges along. There is now:
        // ridge is derived from the ground the plates actually raised (PlanetTerrainGenerator.
        // RidgeFromRelief), so a tectonic world gets its mountains AT ITS MARGINS rather than as a
        // world-wide roughness bonus — and applying the old multiplier on top would raise them everywhere
        // else as well, which is the exact artefact the rework removes.
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

    // ============================================================================================
    // WHAT GOES IN A LANE — the frost line, made mechanical
    //
    // The request's physics, in one function:
    //
    //   "Closer to the star, less material is available, restricting planets to smaller rocky sizes."
    //   "Frost line: beyond this threshold, water freezes into solid ice, allowing cores to grow fast
    //    and capture massive gas envelopes."
    //   "Allow for rare exclusions that let a large CB spawn closer."
    //
    // So: past the frost line a lane is usually a giant, inside it a giant is a one-in-twenty surprise,
    // and everywhere else the lane is either a terrestrial world or a belt of rubble that never became
    // one. Nothing here consults a planet COUNT — a lane is decided from where it is and what the system
    // can still afford, and the system stops when it can no longer afford anything.
    // ============================================================================================
    enum LaneKind { Terrestrial, GasGiant, AsteroidBelt }

    /// How often a lane past the frost line comes out as a gas giant, when the allowance can fund one.
    ///
    /// Past the line there is enough solid ice for a core to run away and capture an envelope, so this is
    /// high — that is what the outer system is FOR. Not higher, and the reason is arithmetic rather than
    /// taste: a giant averages 25 of a Sun-like system's 100-mass allowance, so at 0.58 a G-type came out
    /// with three and a half giants and spent 81% of everything it had on them (measured, Node port,
    /// 4,000 systems). Four giants IS our own solar system, but it is our own solar system with a much
    /// bigger sun's worth of budget behind it, and it left nothing for anything else to be interesting
    /// with. At 0.42 a Sun-like system runs about two and a half giants and still has room for its rocky
    /// worlds, its moons and a belt.
    // RAISED from 0.42 with the ring system. That number was tuned when a system laid out eight or
    // nine lanes and the outer ones were always reached; a system now fills about four rings out of
    // nine, weighted to the middle, so the three rings past the frost line come up far less often.
    // Measured, 0.42 left a Sun-like system with 0.6 giants — most systems had none at all, which is
    // not a place to put a gas giant feature set. 0.70 lands it near one per system.
    const float OuterGiantChance = 0.70f;

    /// How many planets a system must already have before a belt is allowed. Request: "asteroid belts
    /// no closer than beyond the 3rd planet".
    public const int BeltMinPlanets = 3;

    /// The hard ceiling on belts in one system. Request: "max 4".
    public const int BeltCapMax = 4;

    // ============================================================================================
    // HOW MANY BELTS THIS SYSTEM MAY HAVE
    //
    // "Belt count: max 4, and 4 very rare. 1 far more common, if a system gets one at all."
    //
    // A CAP, NOT A TARGET — the same distinction PlacementRings draws about the nine rings. This rolls
    // the system's ceiling; whether it is reached depends on the belt odds, the allowance and the
    // three-planet gate, and most systems never come close. The shape of the roll is what makes 1 the
    // common answer and 4 the rare one, so the cap is doing the request's work even in systems that
    // never bump into it.
    //
    //   1  62%      one belt is what a system with a belt has
    //   2  25%
    //   3  10%
    //   4   3%      "very rare", and it still has to find four qualifying lanes to use it
    // ============================================================================================
    static int RollBeltCap()
    {
        float r = Random.value;
        if (r < 0.62f) return 1;
        if (r < 0.87f) return 2;
        if (r < 0.97f) return 3;
        return BeltCapMax;
    }

    static LaneKind ChooseLane(float rel, float budgetLeft, bool beltsAllowed, int giantsPlaced)
    {
        bool beyondFrost = rel >= WorldClassifier.FrostLineRel;

        // A giant is only on the table if the allowance can actually fund the smallest one.
        if (MassRules.GiantCeiling(budgetLeft * 0.92f) > 0f)
        {
            float giantChance = beyondFrost ? OuterGiantChance : WorldClassifier.HotGiantChance;
            if (Random.value < giantChance) return LaneKind.GasGiant;
        }

        // ---- NOT HERE ----------------------------------------------------------------------------
        //
        // Inside the third planet, or the system has spent its belts. "Anything that small nearer in
        // would have been swept up long ago" is the reasoning, and it is right: the inner system is
        // where accretion ran to completion.
        //
        // A REFUSED BELT IS A TERRESTRIAL, not an empty ring. This includes the out-of-allowance case
        // below, which is the one that matters: on a nearly-spent budget the old code RETURNED a belt
        // unconditionally, so an early ring on a poor star could open the system with a field of rubble
        // where the request wants a planet. RollTerrestrial takes the remaining allowance as its ceiling
        // and the caller has already guaranteed at least an asteroid's worth of it, so the small world
        // this produces is exactly the small world that budget can afford.
        if (!beltsAllowed) return LaneKind.Terrestrial;

        // A BELT is a lane whose material never accreted into anything. Two places that happens:
        //
        //   AT THE FROST LINE, where our own belt sits — near a giant, whose gravity keeps stirring the
        //   material and stops it sticking together. Not a coincidence, and the reason the odds spike
        //   in the band either side of the line rather than being flat across the system.
        //   AT THE END OF THE BUDGET, where there is simply not enough left to make a world out of. A
        //   lane that can no longer afford a planet is not an empty lane; it is a belt, which is the
        //   honest thing to do with the change left in the pot.
        float beltChance = 0.10f;
        if (rel > WorldClassifier.FrostLineRel * 0.75f && rel < WorldClassifier.FrostLineRel * 1.45f)
            beltChance = 0.28f;
        if (budgetLeft < MassRules.TerrestrialMax) beltChance = 0.50f;
        if (budgetLeft < MassRules.TerrestrialMin * 2f) return LaneKind.AsteroidBelt;

        // "A system that rolls fewer gas giants may roll more belts instead." The mechanism the frost
        // line spike models is a giant stirring the material — so a system that HAS no giant out here
        // has to be given the odds some other way, or the one clause of the request that asks for more
        // belts could never fire. Half again, beyond the frost line only, and only while the system is
        // still giant-less: it is compensation for a missing giant, not a bonus for being far out.
        if (beyondFrost && giantsPlaced == 0) beltChance *= 1.5f;

        return Random.value < beltChance ? LaneKind.AsteroidBelt : LaneKind.Terrestrial;
    }

    /// The largest terrestrial world this orbital band can build.
    ///
    /// Inside the habitable zone the young star's wind cleared out the volatiles and there was never
    /// much solid material to begin with, so the worlds are small — Mercury, Venus, Earth and Mars are
    /// all at or under one Earth mass except Earth itself. Further out there is more to work with, so a
    /// super-Earth is possible without being the norm (MassRules.RollTerrestrial still centres its roll
    /// on 1 whatever the ceiling).
    static float TerrestrialBandMax(float rel)
    {
        if (rel < WorldClassifier.HotRel) return 1.4f;                 // scorched inner: small rock
        if (rel <= WorldClassifier.TemperateRelMax) return 2.6f;       // the zone: Earth to super-Earth
        return MassRules.TerrestrialMax;                               // outer: the full range
    }

    /// The distance at which this star's warmth is Earth-normal — the yardstick every climate roll and
    /// every heat bias measures against.
    ///
    /// Now the SAME compressed law the habitable zone uses (StarDatabase.ReferenceDistance). It used to
    /// be its own copy of `sqrt(L) * AU`, which meant the two agreed only by coincidence — and around a
    /// bright star they did not agree at all: the zone said "temperate at 60 units" while every planet in
    /// the system sat between 8 and 30 and was therefore classified as scorched.
    static float TempReference(StarData star) => StarDatabase.ReferenceDistance(star);

    // SystemSpread lived here: it widened the outward WALK by the star's flux so a bright star's
    // system came out larger. PlacementRings does that job now and does it better — its ladder is
    // quoted in multiples of the star's own reference distance, so the spread is not a correction
    // applied to the spacing, it IS the spacing.

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
