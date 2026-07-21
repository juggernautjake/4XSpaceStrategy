// ============================================================================================
// PER-PROJECT CLIMATE PROFILES — what each terraforming project does to a world's TERRAIN KNOBS.
//
// TerraformVisuals already walks a world's terrainParams (heat/moisture/elevation/ridge) toward the
// species' ideal as habitability climbs — but that morph is GENERIC: hauling water, planting forests
// and drying a world out all pushed the same species-ideal blend, so "the action you took" was never
// what you saw change on the map.
//
// This table fixes that. Every project declares a delta on the four amplitude knobs the terrain
// generator (PlanetTerrainGenerator) reads, chosen against how its Classify() actually turns those
// knobs into biomes:
//
//   • ELEVATION gates open water. The generator floods LOW ground (Terran elev < 0.36 → Ocean; an
//     ocean world is water except where elevation lifts land out). So water-IN projects LOWER
//     elevation — low basins fill into lakes and seas first — and water-OUT projects RAISE it so land
//     emerges. Moisture does NOT create seas; that was the trap.
//   • MOISTURE gates vegetation: on temperate ground the generator runs desert → grassland → forest →
//     jungle as moisture rises. So life/forest projects raise it; a dry-loving remodel lowers it.
//   • HEAT warms or cools, moving temperate bands (and, because PlanetTemperature reads through the
//     same knob, the world's °C and the tile hover readout move with it for free).
//   • RIDGE / ELEVATION raise or level mountains.
//
// A COMPLETED project contributes its full delta; a RUNNING one contributes delta × progress, which is
// what makes seas fill WHILE a Water Convoy runs and recede WHILE Hydrosphere Venting runs. Compose()
// (TerraformVisuals) sums these, scaled by the tech-gated transformation `power`, on top of the
// species-ideal grind.
//
// SCOPE: this is WITHIN-TYPE morph. Worlds whose type has no water biome at all (barren, airless) or
// whose water is always frozen regardless of heat (ice) can't grow liquid seas from a knob alone — that
// needs a directed TYPE transition (barren → ocean, ice → temperate, jungle → lava), which is the
// separate directed-remodelling slice. Here, a rocky world greens and floods, a volcanic world cools,
// an ocean world grows islands — all correct for what their generator reads.
//
// Deltas are against the Default knob value of 1.0. Projects that change spin, orbit, magnetosphere,
// gravity or a moon do their work through TerraformManager.ApplyPhysicalEffect and carry no knob delta.
// ============================================================================================
public static class TerraformClimate
{
    // A push on the terrain generator's amplitude knobs. `scale` is deliberately absent: it is a world's
    // own geography (how big its continents are) and terraforming never moves continents — the same
    // reason TerraformVisuals.Ideal leaves it alone.
    public struct ClimateDelta
    {
        /// `seaLevel` replaces what used to be an `elevation` push. Water projects raise or lower THE
        /// SEA; they do not reshape the land. Flooding a world by flattening its elevation amplitude
        /// meant "Haul Water" quietly demolished its mountains, and reversing the project could never
        /// give them back because the relief itself had been thrown away.
        public float heat, moisture, seaLevel, ridge;

        public static ClimateDelta Zero => new ClimateDelta();

        public ClimateDelta(float heat, float moisture, float seaLevel = 0f, float ridge = 0f)
        { this.heat = heat; this.moisture = moisture; this.seaLevel = seaLevel; this.ridge = ridge; }

        public static ClimateDelta operator +(ClimateDelta a, ClimateDelta b)
            => new ClimateDelta(a.heat + b.heat, a.moisture + b.moisture, a.seaLevel + b.seaLevel, a.ridge + b.ridge);

        public static ClimateDelta operator *(ClimateDelta a, float k)
            => new ClimateDelta(a.heat * k, a.moisture * k, a.seaLevel * k, a.ridge * k);
    }

    // The signature terrain change each project drives. Review the SIGN against what the project is FOR:
    // water in RAISES the sea (basins flood first, then the hills), water out LOWERS it (land emerges),
    // warmers raise heat, coolers lower it, life raises moisture (green spreads).
    //
    // The water signs are the opposite of what they were, and deliberately so: these used to push the
    // elevation amplitude DOWN to flood a world, which drowned it by flattening it. The sea moves now,
    // not the land.
    public static ClimateDelta Delta(TerraformProjectType t)
    {
        switch (t)
        {
            // ---- Water in: the sea rises and low ground floods into lakes and seas; land greens a little
            //      (moisture up); some also warm the world ----
            case TerraformProjectType.HaulWater:         return new ClimateDelta(0f,    0.20f,  0.30f);
            case TerraformProjectType.TapAquifers:       return new ClimateDelta(0f,    0.20f,  0.28f);
            case TerraformProjectType.MeltIceCaps:       return new ClimateDelta(0.12f, 0.15f,  0.28f);  // melting also warms
            case TerraformProjectType.CometBombardment:  return new ClimateDelta(0.06f, 0.22f,  0.34f);  // ice + volatiles

            // ---- Air: a breathable envelope nudges the world toward temperate and holds a little water ----
            case TerraformProjectType.SeedAtmosphere:    return new ClimateDelta(0.05f, 0.10f);
            case TerraformProjectType.ScrubAtmosphere:   return new ClimateDelta(-0.05f, 0.05f);  // clears a choking greenhouse
            case TerraformProjectType.OxygenSeeding:     return new ClimateDelta(0f,    0.08f);

            // ---- Life: green needs water; the generator runs grassland → forest → jungle as moisture rises ----
            case TerraformProjectType.MicrobialSeeding:  return new ClimateDelta(0f,    0.12f);
            case TerraformProjectType.PlantForests:      return new ClimateDelta(0f,    0.28f);

            // ---- Temperature ----
            case TerraformProjectType.OrbitalMirrors:    return new ClimateDelta(0.35f, 0f);      // warm a frozen world
            case TerraformProjectType.OrbitalShades:     return new ClimateDelta(-0.35f, 0f);     // shade a baking one
            case TerraformProjectType.CoreCooling:       return new ClimateDelta(-0.40f, 0f);     // bleed a furnace world's own heat
            case TerraformProjectType.CoreIgnition:      return new ClimateDelta(0.15f, 0f);      // a restarted core warms the crust

            // ---- Taking water away: the sea drops and land emerges, climate dries (moisture down) ----
            case TerraformProjectType.HydrosphereVenting:    return new ClimateDelta(0f, -0.35f, -0.35f);
            case TerraformProjectType.CrustalSequestration:  return new ClimateDelta(0f, -0.28f, -0.28f);
            case TerraformProjectType.AtmosphericThinning:   return new ClimateDelta(-0.10f, -0.05f);

            // Spin / orbit / magnetosphere / gravity / moons / shellworld / directed remodelling:
            // no direct terrain-knob change here. (Directed remodelling drives a TYPE transition instead —
            // handled where directed remodelling lives, not as a fixed knob delta.)
            default: return ClimateDelta.Zero;
        }
    }

    // The combined terrain push already applied to a world by its COMPLETED projects (full strength) plus
    // its RUNNING projects (scaled by each job's progress — the live preview that advances with the bar).
    // Pure and deterministic: everything it reads is either serialized (terraformProjects) or live job
    // state, so it round-trips through save/load without any new per-tile data.
    public static ClimateDelta Accumulated(CelestialBody b)
    {
        var sum = ClimateDelta.Zero;
        if (b == null) return sum;

        int typeCount = System.Enum.GetValues(typeof(TerraformProjectType)).Length;

        // Completed projects: their full delta is baked in.
        if (b.terraformProjects != null)
            foreach (int id in b.terraformProjects)
            {
                if (id < 0 || id >= typeCount) continue;
                sum = sum + Delta((TerraformProjectType)id);
            }

        // Running projects: a partial preview that fills in as the loading bar loads. A completed project
        // is already counted above, and TerraformManager only keeps a job in its list while it runs (it
        // removes the job before marking it done), so there is no double-count at the completion boundary.
        var mgr = TerraformManager.Instance;
        if (mgr != null)
            foreach (var j in mgr.JobsFor(b))
                if (j != null && !j.paused)
                    sum = sum + Delta(j.type) * j.Progress;

        return sum;
    }
}
