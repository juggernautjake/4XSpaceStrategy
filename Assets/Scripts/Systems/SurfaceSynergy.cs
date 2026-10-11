using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// PLACEMENT SYNERGIES — the ONE adjacency-bonus function (2026-10-11)
//
// "Make sure that we get all of the synergies based on placement and stuff figured out."
//
// Every bonus a building gets from what it is built against comes from here, and only from here. The
// economy tick (SurfaceOutput / SurfaceBuildManager.TickOutput), the grid's generation (PowerGrid),
// the world's books (WorldEconomy and the Overview's production strip), the building card
// (BuildingGridPopup) and the placement ghost (PreviewText, via PredictedYield and the confirm panel)
// all ask Bonus() / Parts() / PreviewText(), so the "+25% from adjacent Refinery" a player is shown
// while holding a piece is the number the economy pays once it stands.
//
// ADJACENT means footprints touching edge to edge — the same contact rule the power grid conducts by.
//
// ---- THE RULES ----                                     per neighbour   coverage        cap
//   Refinery      -> Mine                                  +25%            2 mine tiles    +50%
//                    ("smelts its ore on site, no haulage")                per refinery
//                                                                          tile, SHARED
//   Mine          -> Refinery                              +25%            whole           +75%
//                    ("ore fed straight in")
//   Refinery      -> Factory                               +20%            2 factory tiles +40%
//                    ("refined stock for the line")                        per tile, shared
//   Storage Depot -> Mine / Refinery / Factory             +10%            whole           +10%
//                    ("logistics on the doorstep")
//   Housing       -> Mine / Refinery / Factory             +5%             whole           +15%
//     (Habitat, Settlement, Town, City: "workers next door")
//   Research Lab <-> Research Centre                       +10%            whole           +30%
//                    ("shared laboratories")
//   Power Distribution -> every generator                  +30%            whole           +30%
//                    (unchanged: the switchyard bonus)
//
//   A building's total synergy is capped at +75%, whatever stacks up under it.
//
// ---- WHY IT CANNOT BE FARMED ----
//   * Per-rule caps, and the +75% total.
//   * COVERAGE: a refinery's capacity is its own size. A 1-tile refinery handles 2 tiles of mine, and
//     that 2 is SHARED across every mine it touches — so a 1-tile refinery wedged between two 10-tile
//     mines gives each of them a tenth of +25%, not +25% each. Big mines need big refineries.
//   * A SOURCE HAS TO BE RUNNING: its contribution is scaled by its own power factor and condition, so a
//     browned-out refinery boosts its mines only as hard as it is itself working. (Not for the switchyard
//     -> generator rule: a switchyard draws nothing, and the grid's generation cannot depend on the grid.)
//   * Same-type buildings that touch MERGE into one building (SurfaceBuildManager.AbsorbInto), so there
//     is no "ring a mine with twenty 1-tile mines" either — they would just be one bigger mine.
//
// Memoised per frame (PowerGrid.Invalidate clears it on any mutation). Neighbour lists come from
// PowerGrid's cached topology, so this costs a handful of comparisons per building.
// ============================================================================================
public static class SurfaceSynergy
{
    /// The most synergy any one building can carry, whatever stacks up under it.
    public const float TotalCap = 0.75f;

    sealed class Rule
    {
        public string label;                                     // "Refinery smelts its ore on site"
        public System.Func<SurfaceBuildingInfo, bool> source, target;
        public float per;                                        // per neighbour; < 0 = the source's adjacencyPowerBonus
        public float ratio;                                      // target tiles one source tile serves; 0 = whole building
        public float cap;
    }

    static bool IsMine(SurfaceBuildingInfo i) => i.type == SurfaceBuildingType.Mine;
    static bool IsRefinery(SurfaceBuildingInfo i) => i.type == SurfaceBuildingType.Refinery;
    static bool IsFactory(SurfaceBuildingInfo i) => i.type == SurfaceBuildingType.Factory;
    static bool IsIndustry(SurfaceBuildingInfo i) => IsMine(i) || IsRefinery(i) || IsFactory(i);
    static bool IsHousing(SurfaceBuildingInfo i)
        => i.type == SurfaceBuildingType.Habitat || i.type == SurfaceBuildingType.Settlement
        || i.type == SurfaceBuildingType.Town || i.type == SurfaceBuildingType.City;
    static bool IsResearch(SurfaceBuildingInfo i)
        => i.type == SurfaceBuildingType.ResearchOutpost || i.type == SurfaceBuildingType.ResearchCenter;

    static readonly Rule[] Rules =
    {
        new Rule { label = "smelted on site by the adjacent Refinery", source = IsRefinery, target = IsMine,     per = 0.25f, ratio = 2f, cap = 0.50f },
        new Rule { label = "ore fed straight in from the adjacent Mine", source = IsMine,   target = IsRefinery, per = 0.25f, ratio = 0f, cap = 0.75f },
        new Rule { label = "refined stock from the adjacent Refinery",  source = IsRefinery, target = IsFactory, per = 0.20f, ratio = 2f, cap = 0.40f },
        new Rule { label = "logistics from the adjacent Storage Depot", source = i => i.type == SurfaceBuildingType.StorageDepot,
                   target = IsIndustry, per = 0.10f, ratio = 0f, cap = 0.10f },
        new Rule { label = "workers housed next door",                  source = IsHousing,  target = IsIndustry, per = 0.05f, ratio = 0f, cap = 0.15f },
        new Rule { label = "laboratories shared with the adjacent lab", source = IsResearch, target = IsResearch, per = 0.10f, ratio = 0f, cap = 0.30f },
        new Rule { label = "switchyard: the adjacent Power Distribution hub", source = i => i.adjacencyPowerBonus > 0f,
                   target = i => i.energyPerSec > 0f, per = -1f, ratio = 0f, cap = 0.30f },
    };

    /// One contribution, for the card and the ghost.
    public struct Part
    {
        public PlacedBuilding source;   // null in a preview where the source is the piece in hand
        public string label;
        public float bonus;             // after coverage and the source's own running factor, before caps
        public float coverage;          // 0..1 share of the target the source can actually serve
    }

    static readonly Dictionary<PlacedBuilding, float> memo = new Dictionary<PlacedBuilding, float>();
    static int memoFrame = -1;

    public static void Invalidate() { memo.Clear(); memoFrame = -1; }

    /// The building's total synergy, 0 .. TotalCap. Multiply its output by (1 + this).
    public static float Bonus(CelestialBody b, PlacedBuilding p)
    {
        if (b == null || p?.Info == null) return 0f;
        if (memoFrame != Time.frameCount) { memo.Clear(); memoFrame = Time.frameCount; }
        if (memo.TryGetValue(p, out float v)) return v;
        v = Collect(b, p, null);
        memo[p] = v;
        return v;
    }

    /// Every contribution, for display. Allocates; the UI asks a couple of times a second.
    public static List<Part> Parts(CelestialBody b, PlacedBuilding p)
    {
        var list = new List<Part>();
        if (b != null && p?.Info != null) Collect(b, p, list);
        return list;
    }

    static float Collect(CelestialBody b, PlacedBuilding p, List<Part> into)
    {
        var info = p.Info;
        var nbrs = PowerGrid.NeighboursOf(b, p);
        if (nbrs.Count == 0) return 0f;
        bool targetGenerates = info.energyPerSec > 0f;

        float total = 0f;
        foreach (var rule in Rules)
        {
            if (!rule.target(info)) continue;
            float sum = 0f;
            // Indexed, not foreach: nbrs is an IReadOnlyList, and foreach over the interface boxes an
            // enumerator — per rule, per building, per frame on the economy's hot path.
            for (int n = 0; n < nbrs.Count; n++)
            {
                var src = nbrs[n];
                var si = src.Info;
                if (si == null || src == p || !rule.source(si)) continue;
                float cov = Coverage(b, rule, src);
                // A source has to be working to help. Not for generator targets: a switchyard draws
                // nothing, and the grid's own generation must never depend on the grid.
                float run = targetGenerates ? 1f : PowerGrid.PowerFactor(b, src) * Mathf.Clamp01(src.health);
                float per = rule.per < 0f ? si.adjacencyPowerBonus : rule.per;
                float add = per * cov * run;
                if (add <= 0.0001f) continue;
                into?.Add(new Part { source = src, label = rule.label, bonus = add, coverage = cov });
                sum += add;
            }
            total += Mathf.Min(sum, rule.cap);
        }
        return Mathf.Min(total, TotalCap);
    }

    /// The share of its targets a source can serve: its tiles x the rule's ratio, spread over EVERY
    /// target of that rule it touches. 1 for whole-building rules.
    static float Coverage(CelestialBody b, Rule rule, PlacedBuilding src)
    {
        if (rule.ratio <= 0f) return 1f;
        int targets = 0;
        var around = PowerGrid.NeighboursOf(b, src);
        for (int n = 0; n < around.Count; n++)
        {
            var q = around[n];
            if (q.Info != null && rule.target(q.Info)) targets += q.TileCount;
        }
        if (targets <= 0) return 0f;
        return Mathf.Clamp01(src.TileCount * rule.ratio / targets);
    }

    /// What this building does for its neighbours: (neighbour, part) pairs where it is the source.
    public static List<KeyValuePair<PlacedBuilding, Part>> Gives(CelestialBody b, PlacedBuilding p)
    {
        var list = new List<KeyValuePair<PlacedBuilding, Part>>();
        if (b == null || p == null) return list;
        foreach (var q in PowerGrid.NeighboursOf(b, p))
            foreach (var part in Parts(b, q))
                if (part.source == p) list.Add(new KeyValuePair<PlacedBuilding, Part>(q, part));
        return list;
    }

    // ============================================================================================
    // THE GHOST: what a piece in hand would get, and give, at these cells
    //
    // Hypothetical, so it cannot use the cached neighbour lists — it reads the neighbours off the cell
    // index directly. The new piece is assumed to run at full power (the power line beside it says
    // whether it will), and existing sources at their current running factor, as the economy would.
    // Coverage is approximate for the hypothetical side (the new piece's own other targets are counted,
    // standing buildings' existing shares are not re-split), which is good enough to say "+20%".
    // ============================================================================================
    public static string PreviewText(CelestialBody b, SurfaceBuildingType t, IEnumerable<Vector2Int> cells,
                                     PlacedBuilding extending = null)
    {
        var info = SurfaceBuildingDatabase.Get(t);
        if (b?.surface == null || info == null || cells == null) return null;

        var mine = new HashSet<Vector2Int>(cells);
        int tiles = mine.Count + (extending != null ? extending.TileCount : 0);
        if (tiles <= 0) return null;

        // Distinct standing neighbours of the shape.
        var nbrs = new List<PlacedBuilding>();
        foreach (var c in mine)
        {
            AddNeighbour(b, c + Vector2Int.up, mine, extending, nbrs);
            AddNeighbour(b, c + Vector2Int.down, mine, extending, nbrs);
            AddNeighbour(b, c + Vector2Int.left, mine, extending, nbrs);
            AddNeighbour(b, c + Vector2Int.right, mine, extending, nbrs);
        }
        if (extending != null)
            foreach (var q in PowerGrid.NeighboursOf(b, extending))
                if (q != extending && !nbrs.Contains(q)) nbrs.Add(q);
        if (nbrs.Count == 0) return null;

        bool gen = info.energyPerSec > 0f;
        var gets = new List<string>();
        float total = 0f;
        foreach (var rule in Rules)
        {
            if (!rule.target(info)) continue;
            float sum = 0f; string who = null;
            foreach (var src in nbrs)
            {
                if (src.Info == null || !rule.source(src.Info)) continue;
                float cov = 1f;
                if (rule.ratio > 0f)
                {
                    int targets = tiles;
                    foreach (var q in PowerGrid.NeighboursOf(b, src))
                        if (q != extending && q.Info != null && rule.target(q.Info)) targets += q.TileCount;
                    cov = Mathf.Clamp01(src.TileCount * rule.ratio / Mathf.Max(1, targets));
                }
                float run = gen ? 1f : PowerGrid.PowerFactor(b, src) * Mathf.Clamp01(src.health);
                float per = rule.per < 0f ? src.Info.adjacencyPowerBonus : rule.per;
                sum += per * cov * run;
                who = src.Info.name;
            }
            sum = Mathf.Min(sum, rule.cap);
            if (sum > 0.004f) { gets.Add($"+{sum * 100f:0}% {who}"); total += sum; }
        }

        var gives = new List<string>();
        foreach (var q in nbrs)
        {
            if (q.Info == null) continue;
            foreach (var rule in Rules)
            {
                if (!rule.source(info) || !rule.target(q.Info)) continue;
                float cov = 1f;
                if (rule.ratio > 0f)
                {
                    int targets = 0;
                    foreach (var o in nbrs) if (o.Info != null && rule.target(o.Info)) targets += o.TileCount;
                    cov = Mathf.Clamp01(tiles * rule.ratio / Mathf.Max(1, targets));
                }
                float per = rule.per < 0f ? info.adjacencyPowerBonus : rule.per;
                float add = Mathf.Min(per * cov, rule.cap);
                if (add > 0.004f) gives.Add($"{q.Info.name} +{add * 100f:0}%");
            }
        }

        if (gets.Count == 0 && gives.Count == 0) return null;
        var sb = new System.Text.StringBuilder("<color=#8FD0FF>");
        if (gets.Count > 0)
        {
            sb.Append("synergy ").Append(string.Join(", ", gets));
            if (total > TotalCap) sb.Append($" (capped at +{TotalCap * 100f:0}%)");
        }
        if (gives.Count > 0)
        {
            if (gets.Count > 0) sb.Append(" · ");
            sb.Append("boosts ").Append(string.Join(", ", gives));
        }
        sb.Append("</color>");
        return sb.ToString();
    }

    static void AddNeighbour(CelestialBody b, Vector2Int c, HashSet<Vector2Int> mine, PlacedBuilding extending,
                             List<PlacedBuilding> into)
    {
        if (mine.Contains(c)) return;
        var q = PowerGrid.OwnerAt(b, c);
        if (q == null || q == extending || into.Contains(q)) return;
        into.Add(q);
    }

    /// Everything the placement readouts say about a shape in hand: its synergies and its power route.
    /// Null when there is nothing to say. Allocates; callers throttle it.
    public static string PlacementSummary(CelestialBody b, SurfaceBuildingInfo info, IEnumerable<Vector2Int> cells,
                                          PlacedBuilding extending = null)
    {
        if (b == null || info == null || cells == null) return null;
        var list = new List<Vector2Int>(cells);
        if (list.Count == 0) return null;
        int tiles = list.Count + (extending != null ? extending.TileCount : 0);
        bool drawn = info.drawMode != BuildDrawMode.Fixed;
        string power = PowerGrid.ProspectText(b, info, list, tiles, drawn, extending);
        string syn = PreviewText(b, info.type, list, extending);
        string role = Industry.RoleLine(info);
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(role)) parts.Add(role);
        if (!string.IsNullOrEmpty(syn)) parts.Add(syn);
        if (!string.IsNullOrEmpty(power)) parts.Add(power);
        return parts.Count == 0 ? null : string.Join("\n", parts);
    }
}
