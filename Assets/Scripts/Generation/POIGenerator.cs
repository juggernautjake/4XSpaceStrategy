using System.Collections.Generic;
using UnityEngine;

// Places points of interest on a body's surface: current settlements (habitable worlds only),
// ancient ruins, special resource sites (tied to real ore deposits), and a rich variety of mystery
// anomalies (wreckage, caverns, unidentified ore, signals...) that must be researched to reveal
// new tech or materials. Each mystery carries a research duration and a completion report.
public static class POIGenerator
{
    static readonly string[] AncientNames =
    { "the Velari", "the Themis Compact", "Old Kadesh", "the Sunken Choir", "the Iron Prophets",
      "the Umbral Dynasty", "the First Builders", "the Ashen Court", "the Lattice Kings", "the Pale Synod" };

    static readonly string[] ColonyNames =
    { "New Meridian", "Port Absalom", "Halcyon Reach", "Fort Kestrel", "Tycho Landing",
      "Serevin City", "Camp Dauntless", "Elysium Station", "Bastion Hollow", "Cradle Point" };

    // kind, hover title, hover blurb, report on completion, base duration (s), ore reward (or None)
    struct MysterySpec
    {
        public string kind, title, blurb, report; public float dur; public OreType ore;
        public MysterySpec(string k, string t, string b, string r, float d, OreType o)
        { kind = k; title = t; blurb = b; report = r; dur = d; ore = o; }
    }

    static readonly MysterySpec[] Mysteries =
    {
        new MysterySpec("Wreckage", "Derelict Wreckage",
            "A drifting hulk of unknown make, hull scorched by some ancient battle.",
            "Salvage crews recovered intact drive components — a measurable leap in propulsion efficiency.", 18f, OreType.None),
        new MysterySpec("Cavern", "Unexplored Cavern",
            "A deep cavern system plunging far beneath the surface.",
            "Mapping revealed vast mineral veins and a sunless underground sea teeming with strange life.", 14f, OreType.Cryonite),
        new MysterySpec("Unidentified Ore", "Unidentified Ore",
            "A vein of ore that matches no sample in the archives.",
            "Assay confirmed a viable, previously-unknown material with remarkable properties.", 22f, OreType.Xenocryst),
        new MysterySpec("Signal", "Repeating Signal",
            "A looping transmission traced to this exact spot.",
            "The signal decoded into schematics for a new communications method.", 20f, OreType.None),
        new MysterySpec("Monolith", "Black Monolith",
            "A perfectly smooth monolith radiating faint, patterned energy.",
            "Prolonged study of the monolith hinted at exotic physics beyond current theory.", 28f, OreType.Quantite),
        new MysterySpec("Probe", "Crashed Probe",
            "An alien probe, half-buried, its sensors still faintly warm.",
            "The probe's databanks catalogued technologies your engineers had never imagined.", 16f, OreType.None),
        new MysterySpec("Cache", "Buried Cache",
            "A sealed vault of data-crystals and sealed containers.",
            "The cache held ancient tools and schematics, jump-starting a line of research.", 15f, OreType.None),
        new MysterySpec("Laboratory", "Ruined Laboratory",
            "The collapsed remains of a high-technology laboratory.",
            "Recovered research notes meaningfully advanced your understanding of materials science.", 24f, OreType.Platinode),
        new MysterySpec("Crater", "Impact Anomaly",
            "An impact crater with an oddly metallic, magnetized core.",
            "The impactor proved to be a fragment of exotic, ultra-dense matter.", 26f, OreType.Neutronium),
        new MysterySpec("Bloom", "Bioluminescent Bloom",
            "An expanse of glowing organisms unlike any catalogued.",
            "Study of the bloom yielded novel biochemistry with medical potential.", 19f, OreType.Luminite),
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
            for (int attempt = 0; attempt < 500 && seenOre.Count < 3; attempt++)
            {
                int x = Random.Range(0, body.surface.width);
                int y = Random.Range(0, body.surface.height);
                var tile = body.surface.tiles[x, y];
                if (tile == null || !tile.HasOre) continue;
                if (OreDatabase.Get(tile.ore).tier < 2) continue;
                if (!seenOre.Add(tile.ore)) continue;

                var oi = OreDatabase.Get(tile.ore);
                pois.Add(new PointOfInterest
                {
                    type = POIType.SpecialResource,
                    u = (x + 0.5f) / body.surface.width,
                    v = (y + 0.5f) / body.surface.height,
                    kind = "Deposit",
                    title = $"{oi.displayName} Deposit",
                    description = "A concentration far above normal background levels.",
                    relatedOre = tile.ore,
                    explored = true,
                    // Confirming an ore seam scales with how exotic the ore is: common metal is a quick
                    // assay, a tier-5 exotic is a serious piece of analysis.
                    researchDuration = (10f + oi.tier * 7f) * Random.Range(0.85f, 1.2f),
                    researchPointCost = 10 + oi.tier * 12,
                    researchReward = 15 + oi.tier * 10,
                    reportText = $"Survey confirmed a rich, workable {oi.displayName} deposit. {oi.uses}"
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
                    type = POIType.Settlement, u = u, v = v, kind = "Settlement",
                    title = ColonyNames[Random.Range(0, ColonyNames.Length)],
                    description = "An active settlement of a living civilization, trading and expanding.",
                    explored = true
                });
            }
        }

        // --- Ancient ruins ---
        if (solid)
        {
            int ruins = Random.value < 0.7f ? Random.Range(0, 3) : 0;
            for (int i = 0; i < ruins; i++)
            {
                if (!TryFindLand(body, out float u, out float v)) break;
                // Ruins are the single most valuable thing you can find in the field: a long, expensive
                // excavation that can recover a precursor schematic — the only key to the Ancients tree.
                bool major = Random.value < 0.35f;
                pois.Add(new PointOfInterest
                {
                    type = POIType.AncientRuins, u = u, v = v, kind = "Ruins",
                    title = $"Ruins of {AncientNames[Random.Range(0, AncientNames.Length)]}",
                    description = "Weathered structures of a civilization long gone. A full excavation may recover lost knowledge — or a precursor schematic.",
                    explored = false,
                    researchDuration = (major ? 55f : 30f) * Random.Range(0.85f, 1.25f),
                    researchPointCost = major ? 90 : 45,
                    researchReward = major ? 140 : 70,
                    yieldsSchematic = major,
                    reportText = major
                        ? "The excavation reached an intact vault. Among the wreckage: a precursor schematic, still readable after all this time."
                        : "The dig recovered fragments of tooling and inscription. Enough to learn from, not enough to rebuild."
                });
            }
        }

        // --- Mystery anomalies (start unexplored; must be researched) ---
        int mysteries = solid ? Random.Range(1, 4) : Random.Range(0, 2);
        for (int i = 0; i < mysteries; i++)
        {
            float u, v;
            if (solid) { if (!TryFindLand(body, out u, out v)) { u = Random.value; v = Random.value; } }
            else { u = Random.value; v = Random.value; }

            var m = Mysteries[Random.Range(0, Mysteries.Length)];
            pois.Add(new PointOfInterest
            {
                type = POIType.Mystery, u = u, v = v, kind = m.kind,
                title = "Unknown Anomaly", explored = false,
                revealTitle = m.title, revealText = m.blurb,
                reportText = m.report,
                // An anomaly's cost and payoff track how long it takes to crack: the strange ones that
                // take an age to understand are the ones worth understanding.
                researchDuration = m.dur * Random.Range(0.8f, 1.3f),
                researchPointCost = Mathf.RoundToInt(m.dur * 2.2f),
                researchReward = Mathf.RoundToInt(m.dur * 4f) + 20,
                relatedOre = m.ore
            });
        }
    }

    static bool TryFindLand(CelestialBody body, out float u, out float v)
    {
        var p = body.terrainParams;
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
