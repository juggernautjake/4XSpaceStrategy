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
            timeScale = Time.timeScale
        };

        var bg = SpaceBackground.Instance;
        if (bg != null)
        {
            game.bgSeed = bg.Seed; game.bgEnabled = bg.Enabled; game.bgSolid = bg.SolidMode;
            game.bgR = bg.SolidColor.r; game.bgG = bg.SolidColor.g; game.bgB = bg.SolidColor.b;
        }

        game.research = new ResearchDTO
        {
            discovered = ResearchManager.ExportDiscovered(),
            researched = ResearchManager.ExportResearched(),
            points = ResearchManager.ResearchPoints,
            empireLevel = EmpireTech.Level,
            tech = TechManager.Export(),
            schematics = AncientLore.Export()
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

        int bodyCount = 0;
        if (galaxy != null)
            foreach (var sys in galaxy.systems)
            {
                game.galaxySystems.Add(SysToDTO(sys));
                foreach (var _ in sys.AllBodies()) bodyCount++;
            }

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
            isHome = sys.isHome
        };
        foreach (var s in sys.stars) sd.starTypes.Add((int)s.type);
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

    static BodyDTO ToDTO(CelestialBody b)
    {
        var dto = new BodyDTO
        {
            id = b.id, name = b.name, type = (int)b.type,
            ownerId = b.owner != null ? b.owner.id : -1,
            habitabilityLocked = b.habitabilityLocked,
            surfaceSize = b.surfaceSize,
            terrainSeed = b.terrainSeed,
            continentFrequency = b.continentFrequency,
            tScale = b.terrainParams.scale, tElev = b.terrainParams.elevation,
            tMoist = b.terrainParams.moisture, tHeat = b.terrainParams.heat, tRidge = b.terrainParams.ridge,
            orbitRadius = b.orbitRadius, orbitSpeed = b.orbitSpeed, orbitPhase = b.orbitPhase,
            orbitDirection = b.orbitDirection, inclination = b.inclination, eccentricity = b.eccentricity,
            verticalOffset = b.verticalOffset, spinSpeed = b.spinSpeed, showRing = b.showRing,
            distanceFromStar = b.distanceFromStar, habitability = b.habitability, isHabitable = b.isHabitable,
            buildings = new List<int>(b.buildings),
            shipyardLevel = b.shipyardLevel, researchCenterLevel = b.researchCenterLevel,
            population = b.population, cities = b.cities,
            terraforming = b.terraforming, terraformability = b.terraformability,
            terraformProjects = b.terraformProjects != null ? new List<int>(b.terraformProjects) : new List<int>(),
            placedBuildings = b.placedBuildings != null ? new List<PlacedBuilding>(b.placedBuildings) : new List<PlacedBuilding>(),
            deepSurveyed = b.deepSurveyed, cityGrowthTimer = b.cityGrowthTimer,
            birthrightClaim = b.birthrightClaim, visited = b.visited, explorationProgress = b.explorationProgress
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
                kind = p.kind, researchDuration = p.researchDuration, reportText = p.reportText
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
        galaxy.center = StarDatabase.BlackHole();
        galaxy.center.visualScale = 6f;

        foreach (var sd in game.galaxySystems)
        {
            var sys = new StarSystemData
            {
                name = sd.name,
                galaxyPosition = new Vector3(sd.px, sd.py, sd.pz),
                isBlackHole = sd.isBlackHole,
                isHome = sd.isHome,
                owner = sd.ownerId >= 0 ? FactionManager.Get(sd.ownerId) : null
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

            // Bodies are stored FLAT (see BodyDTO.parentId). Rebuild them all, then hang the moons off
            // their planets. Two passes, because a moon can appear before its parent in the list.
            var built = new Dictionary<int, CelestialBody>();
            foreach (var bd in sd.bodies)
            {
                var nb = FromDTO(bd, null);
                built[bd.id] = nb;
                if (bd.parentId < 0) sys.bodies.Add(nb);   // top-level planet
            }
            foreach (var bd in sd.bodies)
            {
                if (bd.parentId < 0) continue;
                if (!built.TryGetValue(bd.id, out var moon)) continue;
                if (!built.TryGetValue(bd.parentId, out var parent)) { sys.bodies.Add(moon); continue; }
                moon.parentBody = parent;
                parent.moons.Add(moon);
            }

            // Older saves nested moons inside their planet; those come back already linked by FromDTO,
            // so nothing more to do for them.
            foreach (var b in sys.AllBodies()) { b.hostStar = sys.combinedStar; b.system = sys; }

            galaxy.systems.Add(sys);
        }

        ResearchManager.Import(game.research?.discovered, game.research?.researched,
                               game.research != null ? game.research.points : 0);
        EmpireTech.SetLevel(game.research != null ? game.research.empireLevel : 1);
        TechManager.Import(game.research?.tech);

        GameManager.Instance.LoadGalaxy(galaxy);
        SpeciesManager.Select(game.speciesIndex);

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
        UnitManager.Instance?.ImportUnitDTOs(game.units, byId, home);

        // Resume in-progress work. Both run after the galaxy and fleet exist, because their schedulers
        // read the facility levels off the restored bodies to work out the available power/capacity.
        UnitManager.Instance?.ImportBuildQueue(game.buildQueue);
        TechManager.ImportQueue(game.researchQueue, game.researchPaused);
        GameConfig.SetOrganicCityGrowth(game.organicCityGrowth);
        TerraformManager.Instance?.Import(game.terraformJobs, byId);
        ControlGroups.Import(game.controlGroups);   // after the fleet exists, so members resolve

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
            birthrightClaim = dto.birthrightClaim, visited = dto.visited,
            explorationProgress = dto.explorationProgress
        };
        if (dto.buildings != null) b.buildings = new List<int>(dto.buildings);
        if (dto.terraformProjects != null) b.terraformProjects = new List<int>(dto.terraformProjects);
        b.deepSurveyed = dto.deepSurveyed;
        b.cityGrowthTimer = dto.cityGrowthTimer;
        if (dto.placedBuildings != null) b.placedBuildings = new List<PlacedBuilding>(dto.placedBuildings);
        // JsonUtility fills MISSING fields with 0, so a save written before tech levels and health
        // existed comes back with every structure at level 0 and 0 HP — a dead, non-existent tier.
        foreach (var pb in b.placedBuildings) pb.NormalizeAfterLoad();
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
            ridge = dto.tRidge <= 0f ? 1f : dto.tRidge
        };

        b.surface = PlanetTerrainGenerator.GenerateSurface(b);

        // The surface regenerates from its seed, so its `occupied` flags come back cleared. Re-stamp
        // them from the structures that are standing — otherwise the grid would happily let you build
        // straight on top of your own city. Must run AFTER the surface exists.
        if (b.surface != null && b.placedBuildings != null)
            foreach (var pb in b.placedBuildings)
                foreach (var c in SurfaceBuildingDatabase.Footprint(pb))
                    if (c.x >= 0 && c.y >= 0 && c.x < b.surface.width && c.y < b.surface.height)
                        b.surface.tiles[c.x, c.y].occupied = true;

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
                reportText = p.reportText
            });

        return b;
    }
}
