using UnityEngine;

// ============================================================================================
// TERRAFORMING YOU CAN WATCH HAPPEN
//
// THE PROBLEM
// TickTerraform did exactly one thing: b.habitability += gain. A number went up. The world it described
// never changed — same continents, same ice, same colour, for the entire minutes-long process. You could
// take a frozen rock to 90% habitability and it still rendered as a frozen rock. Terraforming was a
// progress bar wearing a planet.
//
// THE IDEA
// A world's terrain is generated from terrainParams — how hot, how wet, how mountainous, how big its
// features are. Those are the same knobs the Dev Mode sandbox exposes, and the same ones GalaxyGenerator
// uses to make a species' HOME world match its biology (a Pyrothian cradle reads hot, a Cryithn one
// frozen). So terraforming already had somewhere to go: it should walk a world's terrainParams from what
// nature gave it toward what the species would build for itself.
//
// Then the map is not a readout of terraforming, it IS terraforming. Ice retreats, seas fill, green
// spreads — because the generator is being asked a slightly different question every second.
//
// WHY IT'S A LERP AND NOT A SCRIPT
// The world keeps its identity: same seed, same continents, same coastlines. Only the climate moves. A
// world you terraformed still looks like the world you found, which is what makes it YOURS rather than a
// new planet that replaced it. That falls straight out of holding terrainSeed fixed and moving only the
// amplitude knobs.
// ============================================================================================
public static class TerraformVisuals
{
    /// The climate this species would give a world if it could.
    ///
    /// The heat mapping is GalaxyGenerator's, deliberately — that's what makes a species' home world read
    /// as their kind of place, and terraforming is the act of making somewhere ELSE read that way. Two
    /// different curves for "what this species considers pleasant" would drift apart, and the tell would
    /// be a fully terraformed world that looked nothing like the homeworld.
    public static PlanetTerrainGenerator.NoiseParams Ideal(Species s)
    {
        var p = PlanetTerrainGenerator.NoiseParams.Default;
        if (s == null) return p;

        p.heat = Mathf.Lerp(0.55f, 1.7f, Mathf.Clamp01(s.idealTemp));

        // Wet species want water; dry ones want it gone. PrefersDry already encodes this as "would an
        // ocean world be a drowning hazard or a paradise" — the same question, so the same answer.
        p.moisture = s.PrefersDry ? 0.55f : 1.35f;

        // A balanced land/sea split and gentler ground. Nobody's ideal world is all mountain.
        p.elevation = 1.0f;
        p.ridge = 0.75f;

        // NOTE: `scale` is meaningless here and Blend never reads it. Feature scale is the world's own
        // geography — how big its continents are — and terraforming does not move continents.
        return p;
    }

    /// Natural params blended toward the ideal by `t` (0 = untouched, 1 = fully terraformed).
    public static PlanetTerrainGenerator.NoiseParams Blend(
        PlanetTerrainGenerator.NoiseParams natural, PlanetTerrainGenerator.NoiseParams ideal, float t)
    {
        t = Mathf.Clamp01(t);
        var p = natural;
        p.heat = Mathf.Lerp(natural.heat, ideal.heat, t);
        p.moisture = Mathf.Lerp(natural.moisture, ideal.moisture, t);
        p.elevation = Mathf.Lerp(natural.elevation, ideal.elevation, t);
        p.ridge = Mathf.Lerp(natural.ridge, ideal.ridge, t);
        // scale is NOT blended — see Ideal. The continents stay put.
        return p;
    }

    /// How far along the reshaping is, 0..1.
    ///
    /// Keyed on habitability against the colonisation floor rather than against 100: reaching the floor
    /// is the point of terraforming, so a world that just became liveable should LOOK liveable, not 40%
    /// liveable. Past the floor it keeps improving toward the ideal, more slowly.
    public static float Progress(CelestialBody b)
    {
        if (b == null) return 0f;
        float floor = Colony.FoundThreshold;
        if (b.habitability <= 0f) return 0f;
        if (b.habitability >= floor)
            return Mathf.Lerp(0.75f, 1f, Mathf.InverseLerp(floor, 100f, b.habitability));
        return Mathf.Lerp(0f, 0.75f, b.habitability / Mathf.Max(1f, floor));
    }

    /// Habitability must move this far before the surface is regenerated.
    ///
    /// Regenerating is ~12,000 cells x 6 octaves x 3 noise fields, so it is not a per-frame operation —
    /// but it's cheap enough to do every second or so, which is what makes the change readable as motion
    /// rather than as a jump. Small enough to look continuous, big enough not to melt the CPU.
    public const float RegenStep = 1.5f;

    /// Push the world's terrain toward the species' ideal for its current habitability, and rebuild the
    /// visuals if it moved enough to be worth redrawing. Returns true if it regenerated.
    ///
    /// Called from the terraform tick. Safe to call every tick: it does nothing until the world has
    /// actually changed by RegenStep.
    public static bool Advance(CelestialBody b, Species s, bool force = false)
    {
        if (b == null || b.surface == null) return false;
        if (!force && Mathf.Abs(b.habitability - b.lastTerraformRenderHab) < RegenStep) return false;

        b.lastTerraformRenderHab = b.habitability;
        b.terrainParams = Blend(b.naturalParams, Ideal(s), Progress(b));

        // Same seed, so the same continents — only the climate on them changes.
        b.surface = PlanetTerrainGenerator.GenerateSurface(b);
        OreGenerator.Populate(b);

        // The survey indexes cache a per-world distribution derived from the terrain field. That field
        // just moved, so the cache now describes the planet this used to be — the overlays would be
        // scoring new ground against old statistics.
        SurfaceIndex.InvalidateStats(b);

        // Both places a world is drawn: the globe in space, and the map if you happen to be watching it.
        PlanetAppearance.RefreshTexture(b, b.visualObject);
        PlanetViewWindow.Instance?.RefreshIfShowing(b);
        return true;
    }

    /// Capture the world's untouched climate, once. Everything above lerps FROM this, so it has to be
    /// whatever generation produced — before terraforming has moved anything.
    public static void CaptureNatural(CelestialBody b)
    {
        if (b == null) return;
        b.naturalParams = b.terrainParams;
        b.lastTerraformRenderHab = b.habitability;
    }
}
