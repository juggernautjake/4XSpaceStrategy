using System.Collections.Generic;
using UnityEngine;

// Converts between the live galaxy and the serializable SaveGame DTO tree.
public static class GameStateSerializer
{
    // ---- Capture ----
    public static SaveGame Capture(string saveName)
    {
        var gm = GameManager.Instance;
        var galaxy = gm != null ? gm.Galaxy : null;

        var game = new SaveGame
        {
            saveName = saveName,
            savedAtIso = System.DateTime.UtcNow.ToString("o"),
            speciesIndex = SpeciesManager.CurrentIndex,
            difficulty = (int)GameConfig.CurrentDifficulty,
            factionName = FactionManager.Player != null ? FactionManager.Player.name : "Your Empire",
            homeIndex = galaxy != null ? galaxy.homeIndex : 0,
            galaxyName = galaxy != null ? galaxy.name : "",
            galaxySeed = galaxy != null ? galaxy.visualSeed : 0,
            centerHideReason = galaxy != null && galaxy.center != null ? Persist(galaxy.center.hideReason) : 0,
            timeScale = Time.timeScale
        };

        var bg = SpaceBackground.Instance;
        if (bg != null)
        {
            game.bgSeed = bg.Seed; game.bgEnabled = bg.Enabled; game.bgSolid = bg.SolidMode;
            game.bgR = bg.SolidColor.r; game.bgG = bg.SolidColor.g; game.bgB = bg.SolidColor.b;
        }

        // Resources save exactly as they stand, in either mode — including anything granted from the Dev
        // panel, which the player is meant to keep.
        //
        // The GRANTED TECH TREE is different, and is deliberately not saved. It is a temporary switch,
        // not something earned, and a save taken with it up would bake the entire tech tree into the
        // file permanently — the player drops into Dev to look at something, saves out of habit, and has
        // quietly ended their own campaign. BaselineTech is the set from before the grant.
        game.research = new ResearchDTO
        {
            discovered = ResearchManager.ExportDiscovered(),
            researched = ResearchManager.ExportResearched(),
            points = ResearchManager.ResearchPoints,
            empireLevel = EmpireTech.Level,
            tech = DevCheats.BaselineTech,
            schematics = AncientLore.Export(),
            cluesFound = AncientClues.Export()
        };

        game.ecoMetal = PlayerEconomy.Get(ResourceType.Metal);
        game.ecoEnergy = PlayerEconomy.Get(ResourceType.Energy);
        game.ecoWater = PlayerEconomy.Get(ResourceType.Water);
        game.units = UnitManager.Instance != null ? UnitManager.Instance.ExportUnitDTOs() : new List<UnitDTO>();
        game.buildQueue = UnitManager.Instance != null ? UnitManager.Instance.ExportBuildQueue() : new List<BuildOrderDTO>();
        game.researchQueue = TechManager.ExportQueue();
        game.researchPaused = TechManager.Paused;
        game.organicCityGrowth = GameConfig.OrganicCityGrowth;
        game.terraformJobs = TerraformManager.Instance != null ? TerraformManager.Instance.Export() : new List<TerraformJobDTO>();
        game.controlGroups = ControlGroups.Export();
        game.factionAI = FactionAI.ToDTOs();   // rival civilisations' races + personalities (their worlds save via body ownerId)

        int bodyCount = 0;
        if (galaxy != null)
            foreach (var sys in galaxy.systems)
            {
                game.galaxySystems.Add(SysToDTO(sys));
                foreach (var _ in sys.AllBodies()) bodyCount++;
            }

        if (galaxy != null && galaxy.derelicts != null)
            foreach (var d in galaxy.derelicts)
                game.derelicts.Add(new DerelictDTO
                {
                    id = d.id, systemIndex = d.systemIndex, orbit = (int)d.orbit,
                    dsX = d.deadSpacePos.x, dsY = d.deadSpacePos.y, dsZ = d.deadSpacePos.z,
                    orbitRadius = d.orbitRadius, orbitPhase = d.orbitPhase, orbitSpeed = d.orbitSpeed,
                    clueIndex = d.clueIndex,
                    rewardMetal = d.rewardMetal, rewardEnergy = d.rewardEnergy, rewardResearch = d.rewardResearch,
                    studied = d.studied
                });

        int sysCount = galaxy != null ? galaxy.systems.Count : 0;
        game.summary = $"{sysCount} systems · {bodyCount} bodies · {GameConfig.CurrentDifficulty}";
        return game;
    }

    static SystemDTO SysToDTO(StarSystemData sys)
    {
        var sd = new SystemDTO
        {
            name = sys.name,
            px = sys.galaxyPosition.x, py = sys.galaxyPosition.y, pz = sys.galaxyPosition.z,
            isBlackHole = sys.isBlackHole,
            ownerId = sys.owner != null ? sys.owner.id : -1,
            isHome = sys.isHome,
            hideReason = Persist(sys.hideReason)
        };
        foreach (var s in sys.stars) sd.starTypes.Add((int)s.type);
        // Null-guarded, and it has to stay aligned with starTypes above — the loader zips the two by
        // index, so a skipped entry would shift every later sun's concealment onto its neighbour.
        foreach (var s in sys.stars) sd.starHideReasons.Add(s != null ? Persist(s.hideReason) : 0);
        // Flat: planets first, then their moons tagged with parentId. See BodyDTO.parentId for why.
        foreach (var b in sys.bodies)
        {
            sd.bodies.Add(ToDTO(b));
            if (b.moons != null)
                foreach (var m in b.moons)
                {
                    var md = ToDTO(m);
                    md.parentId = b.id;
                    sd.bodies.Add(md);
                }
        }
        return sd;
    }

    /// What sea level reproduces the map a PRE-seaLevel save used to render.
    ///
    /// Old generator: a cell was water when `raw * amp < 0.36`, i.e. `raw < 0.36 / amp`.
    /// New generator: water when `0.5 + (raw - 0.5) * amp - (sea - 0.5) < 0.36`,
    ///                i.e. `raw < 0.5 + (sea - 0.64) / amp`.
    /// Setting the two thresholds equal and solving for sea gives `sea = 1 - 0.5 * amp`.
    ///
    /// This matters more than it looks. The obvious conversion — reusing WaterLevelFromElevation, which
    /// is a straight remap of the 0.3..2 amplitude window — is systematically too wet: it turns a
    /// typical amp-1.0 world from about an eighth water into a third, and an amp-0.6 ocean world from
    /// 80% ocean into 99%. Only the algebraic inverse reproduces the map the player saved.
    static float SeaLevelFromLegacyElevation(float amp)
    {
        if (amp <= 0f) amp = 1f;
        return Mathf.Clamp01(1f - 0.5f * amp);
    }

    // ============================================================================================
    // `Sequence` IS RENDER STATE, NOT SAVE STATE — AND SAVING IT IS UNRECOVERABLE
    //
    // The genesis sequence conceals the whole galaxy while it is born and gives it back at the end
    // (GenesisReveal). The pause menu is reachable during that window — Escape is not blocked by the
    // loading panel — so a player can save while every system in the galaxy is flagged Sequence.
    //
    // Written to disk, that flag is restored faithfully on load and then NOTHING EVER CLEARS IT:
    // GenesisReveal.Running is false on a loaded game, so Finish() early-returns, and no other code
    // path touches Sequence. The player's galaxy would be invisible forever, with the Dev-Mode object
    // panel's "Reveal all" as the only way out — and only if they knew to look.
    //
    // So it is flattened to "visible" on the way out. Dev, Cloaked and Undiscovered are all real,
    // durable facts about the world and persist unchanged; this one is a frame of a cinematic.
    static int Persist(HideReason r) => r == HideReason.Sequence ? 0 : (int)r;

    /// The same rule on the way in, for a save written before Persist existed.
    static HideReason Restore(int v)
    {
        var r = (HideReason)v;
        return r == HideReason.Sequence ? HideReason.None : r;
    }

    static BodyDTO ToDTO(CelestialBody b)
    {
        var dto = new BodyDTO
        {
            id = b.id, name = b.name, type = (int)b.type,
            ownerId = b.owner != null ? b.owner.id : -1,
            habitabilityLocked = b.habitabilityLocked,
            surfaceSize = b.surfaceSize,
            mass = b.mass,
            terrainSeed = b.terrainSeed,
            naturalSeed = b.naturalSeed,
            continentFrequency = b.continentFrequency,
            tScale = b.terrainParams.scale, tElev = b.terrainParams.elevation,
            tMoist = b.terrainParams.moisture, tHeat = b.terrainParams.heat, tRidge = b.terrainParams.ridge,
            // The RAW value, not the neutral-substituted one: a genuinely dry world stores 0 and must
            // load back as 0, not as the half-flooded default.
            tSea = b.terrainParams.HasSeaLevel ? b.terrainParams.seaLevel : 0.5f,
            nScale = b.naturalParams.scale, nElev = b.naturalParams.elevation,
            nMoist = b.naturalParams.moisture, nHeat = b.naturalParams.heat, nRidge = b.naturalParams.ridge,
            nSea = b.naturalParams.HasSeaLevel ? b.naturalParams.seaLevel : 0.5f,
            orbitRadius = b.orbitRadius, orbitSpeed = b.orbitSpeed, orbitPhase = b.orbitPhase,
            naturalOrbitRadius = b.naturalOrbitRadius,
            orbitDirection = b.orbitDirection, inclination = b.inclination, eccentricity = b.eccentricity,
            verticalOffset = b.verticalOffset, spinSpeed = b.spinSpeed, showRing = b.showRing,
            distanceFromStar = b.distanceFromStar, habitability = b.habitability, isHabitable = b.isHabitable,
            buildings = new List<int>(b.buildings),
            shipyardLevel = b.shipyardLevel, researchCenterLevel = b.researchCenterLevel,
            population = b.population, cities = b.cities,
            terraforming = b.terraforming, terraformability = b.terraformability,
            biosphereActive = b.biosphereActive,
            // Both written: `atmospheres` is the truth, `atmosphereThickness` is the derived 0..1 kept
            // in the save purely so a NEWER save stays readable by an older build rather than reloading
            // every world as an airless rock.
            atmospheres = b.atmospheres, atmosphereThickness = b.atmosphereThickness,
            hasMagneticField = b.hasMagneticField,
            hasTectonics = b.hasTectonics,
            terraformProjects = b.terraformProjects != null ? new List<int>(b.terraformProjects) : new List<int>(),
            placedBuildings = b.placedBuildings != null ? new List<PlacedBuilding>(b.placedBuildings) : new List<PlacedBuilding>(),
            deepSurveyed = b.deepSurveyed, researchLevel = b.researchLevel,
            clueIndex = b.clueIndex, cityGrowthTimer = b.cityGrowthTimer,
            birthrightClaim = b.birthrightClaim, settled = b.settled,
            visited = b.visited, explorationProgress = b.explorationProgress,
            hideReason = Persist(b.hideReason), ringHideReason = Persist(b.ringHideReason)
        };

        if (b.resources != null)
            foreach (var kv in b.resources.resources)
                dto.resources.Add(new ResourceDTO { type = (int)kv.Key, amount = kv.Value });

        if (b.surface != null)
            for (int x = 0; x < b.surface.width; x++)
                for (int y = 0; y < b.surface.height; y++)
                {
                    var t = b.surface.tiles[x, y];
                    if (t != null && t.HasOre)
                        dto.ores.Add(new OreCellDTO { x = x, y = y, ore = (int)t.ore, richness = t.oreRichness });
                }

        foreach (var p in b.pointsOfInterest)
            dto.pois.Add(new POIDTO
            {
                type = (int)p.type, u = p.u, v = p.v, title = p.title, description = p.description,
                explored = p.explored, relatedOre = (int)p.relatedOre,
                revealTitle = p.revealTitle, revealText = p.revealText,
                kind = p.kind, researchDuration = p.researchDuration, reportText = p.reportText,
                researchPointCost = p.researchPointCost, researchReward = p.researchReward,
                yieldsSchematic = p.yieldsSchematic, surveyed = p.surveyed
            });

        return dto;
    }

    // ---- Apply ----
    public static void Apply(SaveGame game)
    {
        if (game == null) return;

        GameConfig.CurrentDifficulty = (Difficulty)Mathf.Clamp(game.difficulty, 0, 2);
        if (!string.IsNullOrEmpty(game.factionName) && FactionManager.Player != null)
            FactionManager.Player.name = game.factionName;

        var galaxy = new Galaxy { homeIndex = game.homeIndex };
        // Saves written before the galaxy was named carry neither field. Re-roll rather than leave the
        // widest zoom showing "Unnamed Galaxy" over a seed-0 spiral — an old save gets a name it didn't
        // have, which is a better outcome than a blank one, and every save after this keeps its own.
        galaxy.name = !string.IsNullOrEmpty(game.galaxyName) ? game.galaxyName : NameGenerator.GalaxyName();
        galaxy.visualSeed = game.galaxySeed != 0 ? game.galaxySeed : Random.Range(1, 1000000);
        galaxy.center = StarDatabase.BlackHole();
        galaxy.center.visualScale = 6f;
        galaxy.center.name = $"{galaxy.name} Core";
        // Rebuilt from scratch above, so its concealment has to be put back by hand.
        galaxy.center.hideReason = Restore(game.centerHideReason);

        foreach (var sd in game.galaxySystems)
        {
            var sys = new StarSystemData
            {
                name = sd.name,
                galaxyPosition = new Vector3(sd.px, sd.py, sd.pz),
                isBlackHole = sd.isBlackHole,
                isHome = sd.isHome,
                owner = sd.ownerId >= 0 ? FactionManager.Get(sd.ownerId) : null,
                hideReason = Restore(sd.hideReason)
            };

            if (sd.isBlackHole)
            {
                sys.stars.Add(StarDatabase.BlackHole());
                sys.combinedStar = sys.stars[0];
            }
            else
            {
                foreach (var t in sd.starTypes) sys.stars.Add(StarDatabase.Get((StarType)t));
                if (sys.stars.Count == 0) sys.stars.Add(StarDatabase.Get(StarType.G));
                sys.combinedStar = StarDatabase.Combine(sys.stars);
            }

            // Per-sun concealment. Indexed defensively rather than zipped: a save written before this
            // existed carries an EMPTY list, and a black-hole system substitutes one star for a star list
            // that may have held none — so the two lists are not guaranteed to be the same length, and a
            // missing entry means "visible", which is the right reading either way.
            for (int si = 0; si < sys.stars.Count && si < sd.starHideReasons.Count; si++)
                sys.stars[si].hideReason = Restore(sd.starHideReasons[si]);

            // Bodies are stored FLAT (see BodyDTO.parentId), and a moon can appear before its parent in
            // the list — so this is two passes: PLANETS FIRST, then moons with their parent resolved.
            //
            // This used to build every body with `FromDTO(bd, null)` and link parents in a second pass.
            // That is no longer safe: FromDTO bakes the surface, and a moon's GRID SIZE is capped at half
            // its host's (MapMetrics.SurfW), which reads parentBody. Building with a null parent baked the
            // moon at its uncapped size — so a moon's grid changed dimensions across a save/load round
            // trip, terrain is sampled by normalized u/v so the world came back DIFFERENT, and the
            // re-stamped building coordinates landed on tiles they were never placed on.
            //
            // A moon's parent is always a top-level planet (parentId < 0 identifies planets), so one
            // ordering pass is enough — no dependency sort needed.
            var built = new Dictionary<int, CelestialBody>();
            foreach (var bd in sd.bodies)
            {
                if (bd.parentId >= 0) continue;            // moons in the next pass
                var nb = FromDTO(bd, null);
                built[bd.id] = nb;
                sys.bodies.Add(nb);                        // top-level planet
            }
            foreach (var bd in sd.bodies)
            {
                if (bd.parentId < 0) continue;
                built.TryGetValue(bd.parentId, out var parent);
                var moon = FromDTO(bd, parent);            // parent set BEFORE the surface is baked
                built[bd.id] = moon;
                if (parent != null) parent.moons.Add(moon);
                else sys.bodies.Add(moon);                 // orphaned moon — keep it rather than lose it
            }

            // Older saves nested moons inside their planet; those come back already linked by FromDTO,
            // so nothing more to do for them.
            foreach (var b in sys.AllBodies()) { b.hostStar = sys.combinedStar; b.system = sys; }

            galaxy.systems.Add(sys);
        }

        // Ancient derelict stations — restored to the galaxy before it's handed to the renderer, so their
        // hulls (and any Vael fragment or salvage they still hold) come back exactly where they drifted.
        if (game.derelicts != null)
            foreach (var dd in game.derelicts)
                galaxy.derelicts.Add(new Derelict
                {
                    id = dd.id, systemIndex = dd.systemIndex, orbit = (Derelict.Orbit)dd.orbit,
                    deadSpacePos = new Vector3(dd.dsX, dd.dsY, dd.dsZ),
                    orbitRadius = dd.orbitRadius, orbitPhase = dd.orbitPhase, orbitSpeed = dd.orbitSpeed,
                    clueIndex = dd.clueIndex,
                    rewardMetal = dd.rewardMetal, rewardEnergy = dd.rewardEnergy, rewardResearch = dd.rewardResearch,
                    studied = dd.studied
                });

        ResearchManager.Import(game.research?.discovered, game.research?.researched,
                               game.research != null ? game.research.points : 0);
        EmpireTech.SetLevel(game.research != null ? game.research.empireLevel : 1);
        TechManager.Import(game.research?.tech);

        GameManager.Instance.LoadGalaxy(galaxy);
        SpeciesManager.Select(game.speciesIndex);
        // Rival civilisations' races + personalities. Their owned worlds already came back via each body's
        // ownerId (and the renderer draws their owner rings from body.owner), so this just restores who they
        // are and where their expansion clock stands.
        FactionAI.LoadDTOs(game.factionAI);

        // Restore the player economy, ancient schematics, and fleet now the galaxy bodies exist.
        var byId = new Dictionary<int, CelestialBody>();
        foreach (var sys in galaxy.systems)
            foreach (var b in sys.AllBodies())
                byId[b.id] = b;

        CelestialBody home = null;
        var homeSys = galaxy.Home;
        if (homeSys != null)
        {
            foreach (var b in homeSys.bodies) if (b.owner == FactionManager.Player) { home = b; break; }
            if (home == null && homeSys.bodies.Count > 0) home = homeSys.bodies[0];
        }

        if (game.ecoMetal <= 0f && game.ecoEnergy <= 0f && game.ecoWater <= 0f)
            PlayerEconomy.NewGame(home, SpeciesManager.Current);   // older save without economy: seed a start
        else
            PlayerEconomy.Import(game.ecoMetal, game.ecoEnergy, game.ecoWater);

        AncientLore.Import(game.research != null ? game.research.schematics : 0);
        AncientClues.Import(game.research != null ? game.research.cluesFound : null);
        UnitManager.Instance?.ImportUnitDTOs(game.units, byId, home);

        // Resume in-progress work. Both run after the galaxy and fleet exist, because their schedulers
        // read the facility levels off the restored bodies to work out the available power/capacity.
        UnitManager.Instance?.ImportBuildQueue(game.buildQueue);
        TechManager.ImportQueue(game.researchQueue, game.researchPaused);
        GameConfig.SetOrganicCityGrowth(game.organicCityGrowth);
        TerraformManager.Instance?.Import(game.terraformJobs, byId);
        ControlGroups.Import(game.controlGroups);   // after the fleet exists, so members resolve

        // The game underneath Dev Mode has just been swapped out. Re-baseline against what was loaded,
        // so leaving Dev Mode restores THIS save rather than whatever was on screen before it. Last,
        // after every Import above, because it snapshots the state they just wrote.
        DevCheats.OnGameReplaced();
        // Same reason: the deleted systems sitting in the object bin came out of the galaxy this save
        // just replaced.
        GalaxyTrash.OnGameReplaced();
        // ...and every build job belonged to a world in the galaxy that just went away.
        // Refund first — the queue is not persisted yet, so dropping it would take the materials too.

        SurfaceBuildQueue.RefundAll();

        SurfaceBuildQueue.Clear();

        var bg = SpaceBackground.Instance;
        if (bg != null)
        {
            bg.SetSeed(game.bgSeed); bg.Rebuild();
            bg.SetSolidColor(new Color(game.bgR, game.bgG, game.bgB));
            bg.SetSolidMode(game.bgSolid); bg.SetEnabled(game.bgEnabled);
        }

        float ts = Mathf.Max(0f, game.timeScale <= 0f ? 1f : game.timeScale);
        Time.timeScale = ts;
        TimeController.timeScale = ts;
    }

    static CelestialBody FromDTO(BodyDTO dto, CelestialBody parent)
    {
        var b = new CelestialBody((CelestialBodyType)dto.type)
        {
            id = dto.id, name = dto.name, surfaceSize = dto.surfaceSize,
            terrainSeed = dto.terrainSeed, continentFrequency = dto.continentFrequency,
            orbitRadius = dto.orbitRadius, orbitSpeed = dto.orbitSpeed, orbitPhase = dto.orbitPhase,
            orbitDirection = dto.orbitDirection == 0 ? 1 : dto.orbitDirection,
            inclination = dto.inclination, eccentricity = dto.eccentricity,
            verticalOffset = dto.verticalOffset, spinSpeed = dto.spinSpeed, showRing = dto.showRing,
            distanceFromStar = dto.distanceFromStar, habitability = dto.habitability,
            isHabitable = dto.isHabitable, habitabilityLocked = dto.habitabilityLocked,
            owner = dto.ownerId >= 0 ? FactionManager.Get(dto.ownerId) : null,
            parentBody = parent,
            shipyardLevel = dto.shipyardLevel, researchCenterLevel = dto.researchCenterLevel,
            population = dto.population, cities = dto.cities,
            terraforming = dto.terraforming, terraformability = dto.terraformability,
            biosphereActive = dto.biosphereActive,
            atmospheres = dto.atmospheres, hasMagneticField = dto.hasMagneticField,
            hasTectonics = dto.hasTectonics,
            birthrightClaim = dto.birthrightClaim, settled = dto.settled,
            visited = dto.visited, explorationProgress = dto.explorationProgress,
            hideReason = Restore(dto.hideReason), ringHideReason = Restore(dto.ringHideReason)
        };
        // Saves written before `settled` existed have it false everywhere, which would silently
        // un-colonise every world in an old save. A world with a City on it was settled by definition —
        // that inference is exactly what Claim.cs replaces, and this is the one place it's still correct
        // to make it, because it's the only evidence such a save carries.
        if (!b.settled && (dto.cities > 0 || (dto.buildings != null && dto.buildings.Contains((int)BuildingType.City))))
            b.settled = true;
        if (dto.buildings != null) b.buildings = new List<int>(dto.buildings);
        if (dto.terraformProjects != null) b.terraformProjects = new List<int>(dto.terraformProjects);
        // researchLevel is the truth; `deepSurveyed` is the legacy flag. A save written before the ladder
        // existed carries researchLevel = 0 (JsonUtility's default for an absent field) and the boolean,
        // so falling back to it means a world studied under the old system keeps the overlays it had.
        b.researchLevel = Mathf.Clamp(
            dto.researchLevel > 0 ? dto.researchLevel : (dto.deepSurveyed ? 1 : 0),
            0, CelestialBody.MaxResearchLevel);
        b.clueIndex = dto.clueIndex;
        b.cityGrowthTimer = dto.cityGrowthTimer;
        if (dto.placedBuildings != null) b.placedBuildings = new List<PlacedBuilding>(dto.placedBuildings);

        // DROP records whose type this build doesn't have. The ordinal indexes the database directly,
        // so a save written by a LATER build — one with structures this one has never heard of — would
        // sail through the load and then throw on the first colony tick, which is a far worse place to
        // find out. Losing the odd unknown structure is the honest outcome: we cannot place, draw, or
        // cost something we have no definition for.
        b.placedBuildings.RemoveAll(pb => pb == null || pb.type < 0 || pb.type >= SurfaceBuildingDatabase.All.Length);

        // JsonUtility fills MISSING fields with 0, so a save written before tech levels and health
        // existed comes back with every structure at level 0 and 0 HP — a dead, non-existent tier.
        foreach (var pb in b.placedBuildings) pb.NormalizeAfterLoad();

        // The power grid memoizes per world for the frame, and this world's buildings just changed under
        // it. Cheap insurance: a load that lands on the same frame as a read would otherwise answer from
        // whatever was derived before the save was applied.
        PowerGrid.Invalidate();
        // Saves from before research-centre tiers existed record only that the building is there, so
        // give any existing centre its base tier rather than leaving it at level 0 (= no laboratory).
        if (b.researchCenterLevel < 1 && b.buildings.Contains((int)BuildingType.ResearchCenter))
            b.researchCenterLevel = 1;

        b.terrainParams = new PlanetTerrainGenerator.NoiseParams
        {
            scale = dto.tScale <= 0f ? 1f : dto.tScale,
            elevation = dto.tElev <= 0f ? 1f : dto.tElev,
            moisture = dto.tMoist <= 0f ? 1f : dto.tMoist,
            heat = dto.tHeat <= 0f ? 1f : dto.tHeat,
            ridge = dto.tRidge <= 0f ? 1f : dto.tRidge,
            seaLevel = dto.tSea >= 0f ? dto.tSea : SeaLevelFromLegacyElevation(dto.tElev)
        };

        // The untouched climate terraforming lerps FROM. A save written before this existed has zeros,
        // and the only honest answer there is the current params: such a save has no record of what the
        // world looked like before, so we call where it is now its natural state. It costs that world its
        // terraforming history and nothing else — progress from here still works.
        bool hasNatural = dto.nHeat > 0f || dto.nMoist > 0f || dto.nElev > 0f;
        b.naturalParams = hasNatural
            ? new PlanetTerrainGenerator.NoiseParams
            {
                scale = dto.nScale <= 0f ? 1f : dto.nScale,
                elevation = dto.nElev <= 0f ? 1f : dto.nElev,
                moisture = dto.nMoist <= 0f ? 1f : dto.nMoist,
                heat = dto.nHeat <= 0f ? 1f : dto.nHeat,
                ridge = dto.nRidge <= 0f ? 1f : dto.nRidge,
                // Same pre-seaLevel conversion as the live params above.
                seaLevel = dto.nSea >= 0f ? dto.nSea : SeaLevelFromLegacyElevation(dto.nElev)
            }
            : b.terrainParams;

        // The generation seed "Reset to default" restores. A save written before this existed has 0
        // here; the only honest answer is the current seed — that's the world it recorded, and there's
        // no earlier one to recover.
        b.naturalSeed = dto.naturalSeed > 0f ? dto.naturalSeed : b.terrainSeed;
        // Mass Value. A save from before it existed reads 0 — recover it from the saved surfaceSize (the
        // world's size is known; MassRules.FromSurfaceSize is the inverse of how size is now derived).
        b.mass = dto.mass > 0f ? dto.mass : MassRules.FromSurfaceSize(dto.surfaceSize);

        // ---- ATMOSPHERE MIGRATION ----
        //
        // DELIBERATELY DOWN HERE, not up with the other field restores. It reads `b.mass` and
        // `b.terrainParams.heat`, and both are assigned late — mass on the line above, terrainParams a
        // few dozen lines up. Run any earlier and it reads the FIELD INITIALIZERS instead: mass would be
        // the default 3 and heat the default 1, so every body in the galaxy — a 0.1-mass moon, a gas
        // giant, a barren rock — would migrate to exactly 3.0 atmospheres with a magnetic field, on
        // every load, and then persist that on the next save.
        //
        // Three generations of save to handle:
        //
        //   1. Written by this build: `atmospheres` and `hasMagneticField` are both present and correct.
        //      Detected by the DTO carrying either a positive atmosphere OR a positive thickness — see
        //      the sentinel note below.
        //   2. Written when atmosphere was a 0..1 THICKNESS: invert the conversion to recover pressure,
        //      and derive the field from mass, since it was never stored.
        //   3. Older than atmosphere entirely: rebuild from Mass the way generation now would.
        //
        // THE SENTINEL PROBLEM. `atmospheres == 0` is a LEGAL generated value — a small airless moon
        // really does store 0.0 — so "is it zero?" cannot distinguish a real vacuum from a missing
        // field. `savedAir` below tests whether the DTO carried EITHER atmosphere field, which a
        // pre-atmosphere save cannot have done. Without this, every airless moon in a current save would
        // be "migrated" into a 3-atmosphere world on its first reload.
        bool savedAir = dto.atmospheres > 0f || dto.atmosphereThickness > 0f;

        if (!savedAir)
        {
            // Case 3. Re-DERIVED from mass rather than re-rolled: a roll here would mean the same save
            // loading differently every time, with half the galaxy's air changing on each load.
            b.hasMagneticField = b.type != CelestialBodyType.Asteroid && b.mass >= AtmosphereRules.FieldMassThreshold;
            b.atmospheres = AtmosphereRules.Quantize(
                AtmosphereRules.Ceiling(b.type, b.mass, b.hasMagneticField, AtmosphereRules.TectonicBonus(b))
                * AtmosphereRules.HeatRetention(b.mass, b.terrainParams.heat));
        }
        else if (dto.atmospheres <= 0f && dto.atmosphereThickness > 0f)
        {
            // Case 2. The field was never written in this generation of save, so it has to be derived
            // here too — otherwise every world in an existing game loads fieldless, which would show a
            // homeworld holding more air than its own stated ceiling and put a "no magnetosphere" fault
            // on every body in the galaxy at once.
            b.atmospheres = AtmosphereRules.Quantize(dto.atmosphereThickness * AtmosphereRules.ThicknessReference);
            b.hasMagneticField = b.type == CelestialBodyType.GasGiant
                || (b.type != CelestialBodyType.Asteroid && b.mass >= AtmosphereRules.FieldMassThreshold);
        }
        // Case 1 falls through untouched: the object initializer already took both values from the DTO.
        // The orbit "Reset" restores this. A save from before it existed reads 0; the only honest answer is
        // the orbit the world is at now — there's no earlier one recorded.
        b.naturalOrbitRadius = dto.naturalOrbitRadius > 0f ? dto.naturalOrbitRadius : b.orbitRadius;
        b.lastTerraformRenderHab = b.habitability;   // don't regenerate on the first tick after loading

        b.surface = PlanetTerrainGenerator.GenerateSurface(b);

        // The surface regenerates from its seed, so its `occupied` flags come back cleared. Re-stamp
        // them from the structures that are standing — otherwise the grid would happily let you build
        // straight on top of your own city. Must run AFTER the surface exists.
        if (b.surface != null && b.placedBuildings != null)
            foreach (var pb in b.placedBuildings)
                foreach (var c in SurfaceBuildingDatabase.Footprint(pb))
                    if (c.x >= 0 && c.y >= 0 && c.x < b.surface.width && c.y < b.surface.height)
                        b.surface.tiles[c.x, c.y].occupied = true;

        // A settled world must have a seat of government standing on it — it carries the colony's
        // founding reactor, and a save written before the power grid existed has no such structure on
        // ANY of its worlds. Without this every colony in every old save would load with its industry
        // at the unpowered floor. Runs after the re-stamp above, so it packs around what's already down.
        SurfaceBuildManager.EnsureColonySeat(b);

        if (b.surface != null)
            foreach (var o in dto.ores)
                if (o.x >= 0 && o.x < b.surface.width && o.y >= 0 && o.y < b.surface.height)
                {
                    var t = b.surface.tiles[o.x, o.y];
                    if (t != null) { t.ore = (OreType)o.ore; t.oreRichness = o.richness; }
                }

        foreach (var r in dto.resources) b.resources.Add((ResourceType)r.type, r.amount);

        foreach (var p in dto.pois)
            b.pointsOfInterest.Add(new PointOfInterest
            {
                type = (POIType)p.type, u = p.u, v = p.v, title = p.title, description = p.description,
                explored = p.explored, relatedOre = (OreType)p.relatedOre,
                revealTitle = p.revealTitle, revealText = p.revealText,
                kind = p.kind, researchDuration = p.researchDuration <= 0f ? 12f : p.researchDuration,
                reportText = p.reportText,
                // Old saves have 0 here (the field did not exist); fall back to the class defaults
                // rather than handing out free excavations.
                researchPointCost = p.researchPointCost <= 0 ? 20 : p.researchPointCost,
                researchReward = p.researchReward <= 0 ? 25 : p.researchReward,
                // Old saves predate the charting step. Infer it from deepSurveyed on the body below —
                // set after this list is built, so read the DTO's own flag here.
                surveyed = p.surveyed || dto.deepSurveyed,
                // Old saves have no yieldsSchematic at all, and false is indistinguishable from a
                // genuine minor ruin — so those games would permanently lose every major ruin's
                // schematic, the only entrance to the Ancients branch. Infer it instead: POIGenerator
                // gives major ruins a reward of 140, minor ones far less.
                yieldsSchematic = p.yieldsSchematic ||
                                  ((POIType)p.type == POIType.AncientRuins && p.researchReward >= 140)
            });

        return b;
    }
}
