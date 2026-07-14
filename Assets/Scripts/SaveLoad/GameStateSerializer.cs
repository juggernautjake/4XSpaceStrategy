using System.Collections.Generic;
using UnityEngine;

// Converts between the live game state and the serializable SaveGame DTO tree.
public static class GameStateSerializer
{
    // ---- Capture ----
    public static SaveGame Capture(string saveName)
    {
        var gm = GameManager.Instance;
        var game = new SaveGame
        {
            saveName = saveName,
            savedAtIso = System.DateTime.UtcNow.ToString("o"),
            starType = (int)(gm.CurrentStar != null ? gm.CurrentStar.type : StarType.G),
            speciesIndex = SpeciesManager.CurrentIndex,
            timeScale = Time.timeScale
        };

        var bg = SpaceBackground.Instance;
        if (bg != null)
        {
            game.bgSeed = bg.Seed;
            game.bgEnabled = bg.Enabled;
            game.bgSolid = bg.SolidMode;
            game.bgR = bg.SolidColor.r; game.bgG = bg.SolidColor.g; game.bgB = bg.SolidColor.b;
        }

        int count = 0;
        foreach (var body in gm.CurrentBodies)
        {
            game.bodies.Add(ToDTO(body));
            count += 1 + body.moons.Count;
        }

        string starName = gm.CurrentStar != null ? gm.CurrentStar.type.ToString() : "?";
        game.summary = $"{starName}-type star · {count} bodies";

        game.research = new ResearchDTO
        {
            discovered = ResearchManager.ExportDiscovered(),
            researched = ResearchManager.ExportResearched(),
            points = ResearchManager.ResearchPoints
        };
        return game;
    }

    static BodyDTO ToDTO(CelestialBody b)
    {
        var dto = new BodyDTO
        {
            id = b.id,
            name = b.name,
            type = (int)b.type,
            surfaceSize = b.surfaceSize,
            terrainSeed = b.terrainSeed,
            continentFrequency = b.continentFrequency,
            orbitRadius = b.orbitRadius,
            orbitSpeed = b.orbitSpeed,
            orbitPhase = b.orbitPhase,
            orbitDirection = b.orbitDirection,
            inclination = b.inclination,
            eccentricity = b.eccentricity,
            verticalOffset = b.verticalOffset,
            spinSpeed = b.spinSpeed,
            showRing = b.showRing,
            distanceFromStar = b.distanceFromStar,
            habitability = b.habitability,
            isHabitable = b.isHabitable
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
                revealTitle = p.revealTitle, revealText = p.revealText
            });

        foreach (var m in b.moons)
            dto.moons.Add(ToDTO(m));

        return dto;
    }

    // ---- Apply ----
    public static void Apply(SaveGame game)
    {
        if (game == null) return;

        var bodies = new List<CelestialBody>();
        foreach (var dto in game.bodies)
            bodies.Add(FromDTO(dto, null));

        var star = StarDatabase.Get((StarType)game.starType);

        ResearchManager.Import(game.research?.discovered, game.research?.researched, game.research != null ? game.research.points : 0);

        // Show the loaded system (fires OnSystemChanged → recompute + per-map sky).
        GameManager.Instance.LoadSystem(bodies, star);

        // Restore the viewing species (re-scores every body for that perspective).
        SpeciesManager.Select(game.speciesIndex);

        // Restore the exact saved background (overrides the derived per-map reseed above).
        var bg = SpaceBackground.Instance;
        if (bg != null)
        {
            bg.SetSeed(game.bgSeed);
            bg.Rebuild();
            bg.SetSolidColor(new Color(game.bgR, game.bgG, game.bgB));
            bg.SetSolidMode(game.bgSolid);
            bg.SetEnabled(game.bgEnabled);
        }

        float ts = Mathf.Max(0f, game.timeScale <= 0f ? 1f : game.timeScale);
        Time.timeScale = ts;
        TimeController.timeScale = ts;
    }

    static CelestialBody FromDTO(BodyDTO dto, CelestialBody parent)
    {
        var b = new CelestialBody((CelestialBodyType)dto.type)
        {
            id = dto.id,
            name = dto.name,
            surfaceSize = dto.surfaceSize,
            terrainSeed = dto.terrainSeed,
            continentFrequency = dto.continentFrequency,
            orbitRadius = dto.orbitRadius,
            orbitSpeed = dto.orbitSpeed,
            orbitPhase = dto.orbitPhase,
            orbitDirection = dto.orbitDirection == 0 ? 1 : dto.orbitDirection,
            inclination = dto.inclination,
            eccentricity = dto.eccentricity,
            verticalOffset = dto.verticalOffset,
            spinSpeed = dto.spinSpeed,
            showRing = dto.showRing,
            distanceFromStar = dto.distanceFromStar,
            habitability = dto.habitability,
            isHabitable = dto.isHabitable,
            parentBody = parent
        };

        // Terrain regenerates deterministically from the seed (shapes match the saved game).
        b.surface = PlanetTerrainGenerator.GenerateSurface(b);

        // Re-apply stored ore deposits.
        if (b.surface != null)
            foreach (var o in dto.ores)
                if (o.x >= 0 && o.x < b.surface.width && o.y >= 0 && o.y < b.surface.height)
                {
                    var t = b.surface.tiles[o.x, o.y];
                    if (t != null) { t.ore = (OreType)o.ore; t.oreRichness = o.richness; }
                }

        foreach (var r in dto.resources)
            b.resources.Add((ResourceType)r.type, r.amount);

        foreach (var p in dto.pois)
            b.pointsOfInterest.Add(new PointOfInterest
            {
                type = (POIType)p.type, u = p.u, v = p.v, title = p.title, description = p.description,
                explored = p.explored, relatedOre = (OreType)p.relatedOre,
                revealTitle = p.revealTitle, revealText = p.revealText
            });

        foreach (var m in dto.moons)
            b.moons.Add(FromDTO(m, b));

        return b;
    }
}
