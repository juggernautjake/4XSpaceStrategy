using System.Collections.Generic;
using UnityEngine;

// Static definition of every ore: display name, rarity/value, lore description, uses, refining
// requirements, colour, and how much research effort it takes to fully understand it.
// This is pure data (no scene dependency) so it is safe to query from anywhere, including save/load.
public class OreInfo
{
    public OreType type;
    public string displayName;
    public int tier;                 // 1 = common .. 5 = exotic
    public int baseValue;            // credits per unit
    public string description;       // what it is
    public string uses;             // what it's used for
    public string refining;         // what's needed to refine it
    public int researchCost;         // research points to fully study it
    public Color color;              // marker colour in the viewer / codex

    public OreInfo(OreType type, string displayName, int tier, int baseValue,
                   string description, string uses, string refining, int researchCost, Color color)
    {
        this.type = type;
        this.displayName = displayName;
        this.tier = tier;
        this.baseValue = baseValue;
        this.description = description;
        this.uses = uses;
        this.refining = refining;
        this.researchCost = researchCost;
        this.color = color;
    }
}

public static class OreDatabase
{
    static Dictionary<OreType, OreInfo> _db;

    public static OreInfo Get(OreType type)
    {
        if (_db == null) Build();
        return _db.TryGetValue(type, out var info) ? info : _db[OreType.Ferralite];
    }

    public static IEnumerable<OreInfo> All()
    {
        if (_db == null) Build();
        foreach (var kv in _db)
            if (kv.Key != OreType.None) yield return kv.Value;
    }

    static void Add(OreInfo i) { _db[i.type] = i; }

    static void Build()
    {
        _db = new Dictionary<OreType, OreInfo>();

        Add(new OreInfo(OreType.Ferralite, "Ferralite", 1, 8,
            "A dull grey iron-bearing ore, the workhorse mineral of any young industry.",
            "Structural hulls, station framing, basic tools and rails.",
            "Smelted in a standard blast furnace above 1500K; no exotic handling required.",
            20, new Color(0.62f, 0.45f, 0.35f)));

        Add(new OreInfo(OreType.Cuprion, "Cuprion", 1, 14,
            "A red-brown conductive ore prized since the earliest electrical ages.",
            "Wiring, power distribution, coils and heat exchangers.",
            "Electrolytic refining in an acidic bath; scales cheaply.",
            25, new Color(0.80f, 0.45f, 0.30f)));

        Add(new OreInfo(OreType.Titanex, "Titanex", 2, 35,
            "A silver-white ore yielding an alloy base with an outstanding strength-to-weight ratio.",
            "Ship frames, landing struts, high-stress mechanical parts.",
            "Requires a vacuum arc furnace to avoid embrittlement; moderate energy cost.",
            45, new Color(0.72f, 0.74f, 0.78f)));

        Add(new OreInfo(OreType.Aurelium, "Aurelium", 3, 120,
            "A soft, gleaming golden ore that never tarnishes.",
            "Currency backing, precision contacts, radiation shielding foils.",
            "Simple chemical leaching; value is in scarcity, not difficulty.",
            50, new Color(0.95f, 0.80f, 0.28f)));

        Add(new OreInfo(OreType.Argenite, "Argenite", 3, 90,
            "A bright reflective silver-bearing ore with the finest conductivity known.",
            "Optical mirrors, superconductors, high-end sensors.",
            "Flotation then electrolytic polishing; sensitive to contamination.",
            55, new Color(0.85f, 0.87f, 0.90f)));

        Add(new OreInfo(OreType.Platinode, "Platinode", 4, 200,
            "A dense, inert platinum-group ore that catalyses reactions without being consumed.",
            "Catalysts, fuel cells, corrosion-proof reactor linings.",
            "High-temperature aqua-regia dissolution; hazardous byproducts.",
            70, new Color(0.78f, 0.80f, 0.82f)));

        Add(new OreInfo(OreType.Pyronium, "Pyronium", 3, 150,
            "A glowing ember-red crystal that stores volcanic heat almost indefinitely.",
            "Thermal batteries, plasma torches, planetary heaters.",
            "Must be quenched in inert gas the instant it is mined or it self-discharges.",
            60, new Color(1.00f, 0.45f, 0.15f)));

        Add(new OreInfo(OreType.Cryonite, "Cryonite", 2, 60,
            "A pale blue ice-locked ore holding volatile compounds under pressure.",
            "Cryogenic coolant, life-support reserves, low-thrust propellant.",
            "Kept below 90K throughout refining or it flashes off; needs cryo-plants.",
            50, new Color(0.60f, 0.82f, 0.92f)));

        Add(new OreInfo(OreType.Helium3, "Helium-3", 3, 130,
            "A rare fusion-fuel isotope skimmed from gas-giant crusts and regolith.",
            "Clean fusion reactors, high-yield ship drives.",
            "Extracted by heating regolith and cryo-trapping the outgassed isotope.",
            65, new Color(0.70f, 0.85f, 1.00f)));

        Add(new OreInfo(OreType.Uranite, "Uranite", 4, 170,
            "A heavy, faintly warm ore that hums with radioactive potential.",
            "Fission reactors, penetrator munitions, deep-space RTGs.",
            "Enriched in shielded centrifuge cascades; strict containment required.",
            80, new Color(0.45f, 0.70f, 0.35f)));

        Add(new OreInfo(OreType.Luminite, "Luminite", 4, 220,
            "A crystal that drinks in radiation and re-emits it as coherent light.",
            "Laser weapons, comm lasers, photonic computing cores.",
            "Cleaved along optical axes in a clean-room; flaws ruin the yield.",
            85, new Color(0.65f, 0.90f, 0.85f)));

        Add(new OreInfo(OreType.Adamantine, "Adamantine", 4, 260,
            "A near-black ore of legendary hardness, almost impossible to scratch.",
            "Capital-ship armour, drill heads, blast doors.",
            "Sintered under immense pressure; consumes enormous energy to work.",
            90, new Color(0.30f, 0.32f, 0.38f)));

        Add(new OreInfo(OreType.Neutronium, "Neutronium", 5, 600,
            "An impossibly dense exotic material, a shard of collapsed-star matter.",
            "Gravitic devices, warp anchors, ultimate armour plating.",
            "Contained only in magnetic-confinement forges; a lab curiosity at scale.",
            140, new Color(0.55f, 0.55f, 0.70f)));

        Add(new OreInfo(OreType.Quantite, "Quantite", 5, 500,
            "A shimmering crystal whose lattice stays quantum-coherent at room temperature.",
            "Quantum computers, entangled comms, cloaking fields.",
            "Grown, not smelted, in vibration-isolated vacuum chambers.",
            130, new Color(0.60f, 0.75f, 0.95f)));

        Add(new OreInfo(OreType.Xenocryst, "Xenocryst", 5, 750,
            "An ore of no known chemistry, apparently not of natural origin.",
            "Unknown — early tests hint at reality-bending applications.",
            "No reliable process exists yet; every sample behaves differently.",
            160, new Color(0.80f, 0.45f, 0.90f)));
    }
}
