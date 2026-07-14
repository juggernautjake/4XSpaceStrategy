using System.Collections.Generic;
using UnityEngine;

// Places points of interest on a body's surface: current settlements (habitable worlds only),
// ancient ruins, special resource sites (tied to real high-tier ore deposits), and mystery
// anomalies that must be explored to reveal new tech or materials.
public static class POIGenerator
{
    static readonly string[] AncientNames =
    { "the Velari", "the Themis Compact", "Old Kadesh", "the Sunken Choir", "the Iron Prophets",
      "the Umbral Dynasty", "the First Builders", "the Ashen Court" };

    static readonly string[] ColonyNames =
    { "New Meridian", "Port Absalom", "Halcyon Reach", "Fort Kestrel", "Tycho Landing",
      "Serevin City", "Camp Dauntless", "Elysium Station" };

    static readonly (string title, string text, bool givesOre)[] Mysteries =
    {
        ("Derelict Ship", "A drifting hulk of unknown make. Its salvaged drive hints at a faster propulsion technology.", false),
        ("Buried Vault", "A sealed vault of data-crystals describing a lost branch of science.", false),
        ("Repeating Signal", "A looping transmission traced to this exact spot. Decoding it may unlock new comms tech.", false),
        ("Crashed Probe", "An alien probe still logging materials your labs have never catalogued.", true),
        ("Impossible Deposit", "Sensors detect a substance that should not form naturally here.", true),
        ("Black Monolith", "A perfectly smooth monolith radiating faint, patterned energy.", false),
    };

    public static void Populate(CelestialBody body)
    {
        body.pointsOfInterest.Clear();
        var pois = body.pointsOfInterest;

        bool solid = body.type != CelestialBodyType.GasGiant;

        // --- Special resources: promote real high-tier ore deposits into named sites ---
        if (solid && body.surface != null)
        {
            var seenOre = new HashSet<OreType>();
            for (int attempt = 0; attempt < 400 && seenOre.Count < 2; attempt++)
            {
                int x = Random.Range(0, body.surface.width);
                int y = Random.Range(0, body.surface.height);
                var tile = body.surface.tiles[x, y];
                if (tile == null || !tile.HasOre) continue;
                if (OreDatabase.Get(tile.ore).tier < 3) continue;
                if (!seenOre.Add(tile.ore)) continue;

                pois.Add(new PointOfInterest
                {
                    type = POIType.SpecialResource,
                    u = (x + 0.5f) / body.surface.width,
                    v = (y + 0.5f) / body.surface.height,
                    title = "Rich Deposit",
                    description = "A concentration far above normal background levels.",
                    relatedOre = tile.ore,
                    explored = true
                });
            }
        }

        // --- Current settlements: only on genuinely habitable worlds ---
        if (solid && body.isHabitable)
        {
            int colonies = Random.Range(1, 4);
            for (int i = 0; i < colonies; i++)
            {
                if (!TryFindLand(body, out float u, out float v)) break;
                pois.Add(new PointOfInterest
                {
                    type = POIType.Settlement,
                    u = u, v = v,
                    title = ColonyNames[Random.Range(0, ColonyNames.Length)],
                    description = "An active settlement of a living civilization.",
                    explored = true
                });
            }
        }

        // --- Ancient ruins ---
        if (solid)
        {
            int ruins = Random.value < 0.6f ? Random.Range(0, 2) : 0;
            for (int i = 0; i < ruins; i++)
            {
                if (!TryFindLand(body, out float u, out float v)) break;
                pois.Add(new PointOfInterest
                {
                    type = POIType.AncientRuins,
                    u = u, v = v,
                    title = $"Ruins of {AncientNames[Random.Range(0, AncientNames.Length)]}",
                    description = "Weathered structures of a civilization long gone. Study may recover lost knowledge.",
                    explored = true
                });
            }
        }

        // --- Mystery anomalies (start unexplored) ---
        int mysteries = Random.Range(0, 3);
        if (!solid) mysteries = Random.Range(0, 2); // gas giants: atmospheric anomalies
        for (int i = 0; i < mysteries; i++)
        {
            float u, v;
            if (solid) { if (!TryFindLand(body, out u, out v)) { u = Random.value; v = Random.value; } }
            else { u = Random.value; v = Random.value; }

            var m = Mysteries[Random.Range(0, Mysteries.Length)];
            var poi = new PointOfInterest
            {
                type = POIType.Mystery,
                u = u, v = v,
                title = "Unknown Anomaly",
                explored = false,
                revealTitle = m.title,
                revealText = m.text
            };
            if (m.givesOre) poi.relatedOre = Random.value < 0.5f ? OreType.Xenocryst : OreType.Quantite;
            pois.Add(poi);
        }
    }

    // Rejection-sample a non-water location.
    static bool TryFindLand(CelestialBody body, out float u, out float v)
    {
        var p = PlanetTerrainGenerator.NoiseParams.Default;
        for (int i = 0; i < 30; i++)
        {
            u = Random.value; v = Random.Range(0.1f, 0.9f);
            var s = PlanetTerrainGenerator.SampleNormalized(body, u, v, p, 4);
            if (!s.water) return true;
        }
        u = Random.value; v = Random.value;
        return false;
    }
}
