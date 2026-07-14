using UnityEngine;

/// Which of a world's three ceilings is the one actually stopping it.
public enum PopLimit
{
    None,      // room to grow on every axis
    Land,      // the world itself is too small or too hostile to hold more
    Housing,   // there is nowhere for more people to live
    Food       // there is not enough food to feed more people
}

// ============================================================================================
// CARRYING CAPACITY
//
// How many people a world can hold, and — more usefully — WHY it can't hold more.
//
// THE PROBLEM THIS SOLVES
// Food used to be counted in SOURCES, not in meals: ColonyFacilities.FoodLevel returned
// 0.6 + (n-1)*0.2 from the number of farms, with no reference to how many mouths there were. One farm
// fed a million people exactly as well as it fed a billion. So a world could never outgrow its food,
// overpopulation was unreachable by construction, and a farm was a box to tick once rather than
// something you had to keep building as the colony grew.
//
// Here, food is a QUANTITY that has to cover a DEMAND that scales with population. That one change is
// what makes the rest possible: growth that slows as a colony approaches what it can feed, famine when
// it overshoots, and a reason to keep developing a world you have already colonised.
//
// THREE CEILINGS, NOT ONE
// A world's population is bounded by the smallest of:
//   * LAND    — surface area, scaled steeply by how livable the world is for THIS species
//   * HOUSING — the sum of what every city, town, habitat and capitol can hold
//   * FOOD    — what the farms actually produce
// The binding one is reported, so "at capacity" is never a dead end: it tells you whether to terraform,
// build housing, or plant farms. A cap you can't diagnose is just a wall.
// ============================================================================================
public static class Carrying
{
    // ---- Species appetite ----
    /// Food each population unit needs per second, relative to baseline.
    ///
    /// Adaptable species eat anything and waste nothing; picky ones need dedicated agriculture. This is
    /// deliberately the ONE species stat that drives appetite — fertility already drives how fast they
    /// breed, and longevity how many accumulate, so tying appetite to adaptability keeps each attribute
    /// answering a different question rather than three of them stacking into "good species / bad
    /// species".
    public static float Appetite(Species s)
        => s == null ? 1f : Mathf.Lerp(1.3f, 0.72f, Mathf.InverseLerp(1f, 10f, s.adaptability));

    /// How much crowding a species will put up with before the walls close in. Durable species live
    /// stacked in tunnels quite happily; fragile ones need room. 0.8 .. 1.35.
    public static float DensityTolerance(Species s)
        => s == null ? 1f : Mathf.Lerp(0.8f, 1.35f, Mathf.InverseLerp(1f, 10f, s.durability));

    // ---- Technology ----
    /// What your tech level does for agriculture. Empire level 1 feeds people the hard way; level 10-11
    /// has vat protein and orbital light. This is the "not enough food AND TECHNOLOGY" half — a maxed
    /// empire feeds roughly 2.4x the people from the same farmland.
    public static float FarmTechMult()
        => Mathf.Lerp(1f, 2.4f, Mathf.InverseLerp(1f, 11f, EmpireTech.Level));

    /// What your tech level does for quality of life at density: sanitation, transit, arcology
    /// engineering. Lets a developed empire pack people in without the misery that would otherwise
    /// bring.
    public static float DensityTechMult()
        => Mathf.Lerp(1f, 1.6f, Mathf.InverseLerp(1f, 11f, EmpireTech.Level));

    // ---- Food ----
    /// Population units this world's farms can feed.
    ///
    /// Per FARM, not per farm-tier: a farm's yield is its own siting and level (OutputMult already folds
    /// efficiency x level together), so a farm on rich, well-watered ground feeds far more than one
    /// scratched into rock. That's what makes the Fertile index worth reading before you build.
    public static float FoodSupply(CelestialBody b)
    {
        if (b == null) return 0f;

        // SUBSISTENCE: what the world feeds people without any agriculture at all — foraging, fishing,
        // hydroponics aboard the ship you arrived in. Scaled by how livable the world is, so a paradise
        // feeds a village for free and a marginal rock feeds almost nobody.
        //
        // This is load-bearing, not flavour. Without it FoodSupply is 0 on a world with no farms, so
        // FoodCap is 0, so a colony ship that lands 3 units of people arrives ABOVE the food ceiling and
        // starts starving on turn one — every new colony would die before it could build its first farm.
        // A population model needs somewhere to start from.
        float supply = Mathf.Lerp(2f, 18f, Population.Liveability(b));

        foreach (var p in SurfaceBuildManager.On(b))
            if (p.Type == SurfaceBuildingType.Farm)
                supply += FoodPerFarm * p.OutputMult;

        // The abstract colony-level Farm from the Production tab. Counted at baseline siting, since it
        // isn't on the grid and has no ground to be good or bad.
        if (b.buildings != null && b.buildings.Contains((int)BuildingType.Farm))
            supply += FoodPerFarm * 0.8f;

        return supply * FarmTechMult();
    }

    /// Population units one well-sited, level-1 farm feeds. Sets the whole scale of the food economy:
    /// a homeworld of 10 units (a million people) is fed by a couple of farms, and a world of 200 units
    /// needs a real agricultural belt.
    public const float FoodPerFarm = 14f;

    /// Population units this world's people need fed.
    public static float FoodDemand(CelestialBody b)
        => b == null ? 0f : b.population * Appetite(SpeciesManager.Current);

    /// Supply divided by demand. Above 1 is a surplus; below 1 is a shortage and people are going
    /// hungry. Returns a large number when nobody lives here — an empty world is not starving.
    public static float FoodRatio(CelestialBody b)
    {
        float demand = FoodDemand(b);
        if (demand <= 0.001f) return 99f;
        return FoodSupply(b) / demand;
    }

    // ---- The three ceilings ----
    /// What the world's own surface can hold, before anything is built on it.
    ///
    /// The habitability curve is steep at the top (pow 1.6) on purpose: it's what makes a 98-100% world
    /// a genuinely different proposition from a 90% one rather than a marginally better one. The "super
    /// city planet" only exists if the last few points are worth far more than the ones before them.
    public static int LandCap(CelestialBody b)
    {
        if (b == null) return 0;
        float live = Population.Liveability(b);
        float land = Mathf.Max(4, b.surfaceSize) * 6f;
        float natural = land * Mathf.Lerp(0.35f, 3.2f, Mathf.Pow(live, 1.6f));
        natural *= DensityTolerance(SpeciesManager.Current) * DensityTechMult();
        return Mathf.Max(1, Mathf.RoundToInt(natural * Population.LongevityFactor(SpeciesManager.Current)));
    }

    /// What one settlement can hold. Per-city caps are the reason a world fills up as a set of places
    /// rather than as one number: a Town is full at its own ceiling and has to become a City to hold
    /// more, so growth reads as somewhere developing rather than a bar sliding right.
    public static int CityCap(PlacedBuilding p)
    {
        if (p == null) return 0;
        float b;
        switch (p.Type)
        {
            case SurfaceBuildingType.City:          b = 55f; break;
            case SurfaceBuildingType.Town:          b = 22f; break;
            case SurfaceBuildingType.Settlement:    b = 8f;  break;
            case SurfaceBuildingType.PlanetCapitol: b = 40f; break;
            case SurfaceBuildingType.ColonyShipBase:b = 10f; break;
            case SurfaceBuildingType.Habitat:       b = 30f; break;
            default: return 0;
        }
        return Mathf.RoundToInt(b * p.LevelMult * DensityTolerance(SpeciesManager.Current) * DensityTechMult());
    }

    /// Everywhere there is to live, summed. Includes a small allowance for people living rough, so a
    /// brand-new colony with nothing built isn't instantly "at capacity" at zero.
    public static int HousingCap(CelestialBody b)
    {
        if (b == null) return 0;
        int sum = 0;
        foreach (var p in SurfaceBuildManager.On(b)) sum += CityCap(p);
        if (b.buildings != null && b.buildings.Contains((int)BuildingType.City)) sum += 30;
        return sum + Landless;
    }

    /// People a world supports with no housing at all — tents, ships, whatever the colonists arrived in.
    /// Small, but non-zero: a colony has to be able to get started.
    public const int Landless = 6;

    /// How many people the food can feed.
    public static int FoodCap(CelestialBody b)
    {
        float appetite = Appetite(SpeciesManager.Current);
        if (appetite <= 0.001f) return int.MaxValue;
        return Mathf.Max(0, Mathf.RoundToInt(FoodSupply(b) / appetite));
    }

    /// The ceiling that is actually binding, and which one it is.
    public static int Limit(CelestialBody b, out PopLimit bound)
    {
        bound = PopLimit.None;
        if (b == null) return 0;

        int land = LandCap(b), housing = HousingCap(b), food = FoodCap(b);
        int limit = land;
        bound = PopLimit.Land;
        if (housing < limit) { limit = housing; bound = PopLimit.Housing; }
        if (food < limit) { limit = food; bound = PopLimit.Food; }

        // Only call it a limit if it's actually near: reporting "Food" on a world at 3% of its ceiling
        // is technically true and completely useless.
        if (b.population < limit * 0.85f) bound = PopLimit.None;
        return Mathf.Max(1, limit);
    }

    public static int Limit(CelestialBody b) => Limit(b, out _);

    /// Plain-language name of what to do about the binding ceiling.
    public static string Advice(PopLimit l)
    {
        switch (l)
        {
            case PopLimit.Land:    return "the world itself is full — terraform it, or settle another";
            case PopLimit.Housing: return "nowhere left to live — build habitats, or grow the cities";
            case PopLimit.Food:    return "the farms can't feed any more — plant more, or improve the ones you have";
            default: return null;
        }
    }

    // ---- Overpopulation ----
    /// 0 when the colony is fed, rising toward 1 as the shortfall deepens. This is what turns "you have
    /// outgrown your farms" from a stalled growth bar into an actual crisis.
    public static float Famine(CelestialBody b)
    {
        float r = FoodRatio(b);
        if (r >= 1f) return 0f;
        return Mathf.Clamp01(1f - r);
    }

    /// Population units lost per second to starvation. Deliberately gentler than growth: a famine should
    /// be a problem you get a chance to fix, not a colony that evaporates while you're in another system.
    public static float StarvationRate(CelestialBody b)
    {
        if (b == null || b.population <= 0) return 0f;
        float f = Famine(b);
        if (f <= 0.05f) return 0f;                    // a rounding-error shortfall is just a tight year
        return b.population * f * StarvationScale;
    }

    public const float StarvationScale = 0.004f;

    /// A colony's food situation in one line, for the UI.
    public static string FoodLine(CelestialBody b)
    {
        if (b == null || b.population <= 0) return "nobody to feed";
        float r = FoodRatio(b);
        int fed = FoodCap(b);
        if (r < 0.6f)  return $"FAMINE — food for {Population.Short(fed)} of {Population.Short(b.population)}";
        if (r < 1f)    return $"going hungry — food for {Population.Short(fed)} of {Population.Short(b.population)}";
        if (r < 1.25f) return $"fed, with nothing spare — food for {Population.Short(fed)}";
        return $"well fed — food for {Population.Short(fed)}, a {(r - 1f) * 100f:F0}% surplus";
    }
}
