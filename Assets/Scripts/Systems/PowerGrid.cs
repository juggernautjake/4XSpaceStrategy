using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// THE POWER GRID — electricity as a PLACE rather than a number.
//
// Energy used to be a stockpile and nothing else: a plant added to it, anything anywhere could spend
// it, and where you put the plant never mattered. This makes power LOCAL. A generator lights the ground
// around itself; a Power Node relays that reach further; anything standing on lit ground runs properly,
// and anything off it limps along on its own back-up plant.
//
// ---- WHO LIGHTS GROUND, AND WHO ONLY CARRIES IT ----
//
// This is the distinction the whole file now turns on, and it used to not exist.
//
//   A GENERATOR PROJECTS. It lights its own footprint and a disc around it, and the disc is generous
//   now — two to four tiles depending on what kind of plant it is and what tier it has reached. That
//   projection is what founds a grid: without a plant, nothing anywhere is lit.
//
//   EVERYTHING ELSE ONLY CONDUCTS. A farm, a factory, a capacitor, a relay: each lights its OWN
//   footprint and nothing beyond it, and only while it is itself connected. So a building on the grid
//   passes power through itself to whatever is built against it, and a line of factories walks the
//   supply along without a single pylon — but no non-generator ever throws light onto empty ground.
//
//   A POWER NODE is the exception that proves it, and it is deliberately awkward: it projects a wide
//   disc like a plant, but ONLY once it is itself connected. See the note on the two passes below.
//
// The old rule was one line — "two projectors are in the same grid when the ground they light
// overlaps" — and it was elegant and wrong in one specific, exploitable way: a chain of Power Nodes
// standing in empty desert, touching nothing, was a grid. Two such chains whose discs happened to
// overlap were one grid. Nothing had to be connected to anything; the projections alone did it, and a
// node dropped anywhere on the map with another node within fourteen tiles was instantly wired in.
//
// ---- WHAT A GRID IS NOW ----
//
// A grid is still a connected component, but of a graph whose EDGES are contact rather than overlap:
//
//   Two buildings are joined when their footprints TOUCH edge-to-edge, or when one of them is a
//   GENERATOR (or a connected node) whose projected disc covers a cell of the other.
//
// So power flows out of a plant, across the ground it lights, and then THROUGH the buildings it
// reaches, into whatever those touch. A node extends that reach, but only from inside it.
//
// Every behaviour still falls out of the connectivity rather than being maintained:
//
//   - Chain nodes from a city out to a mine and the two are one grid, because each node in the chain
//     is lit by the one behind it, back to a plant.
//   - Blow a node out of the middle and everything past it goes dark. Nothing splits them; the chain
//     simply no longer reaches.
//   - Drop a generator inside an existing grid and it contributes to that grid rather than starting
//     its own.
//   - Drop a node in the desert and it does nothing at all, which is the point.
//
// ---- Why this is DERIVED and not MAINTAINED ----
// Merge-on-connect and split-on-destroy are where everyone writes the bugs. The split case needs a
// graph search anyway, so maintaining incremental state buys nothing but a second source of truth that
// can drift out of agreement with the map — and a grid that disagrees with the buildings you can see is
// a miserable class of bug to chase.
//
// So there is no merge code and no split code. There is one function that derives every grid on a world
// from the buildings standing on it, memoized for the frame. Merging and splitting are things you
// OBSERVE, not things this code does.
// ============================================================================================
public class PowerNet
{
    public int index;                                   // 1-based, stable within a frame (see Compute)

    public readonly List<PlacedBuilding> projectors = new List<PlacedBuilding>();
    public readonly List<PlacedBuilding> generators = new List<PlacedBuilding>();
    public readonly List<PlacedBuilding> capacitors = new List<PlacedBuilding>();
    public readonly List<PlacedBuilding> consumers = new List<PlacedBuilding>();

    /// Every tile this grid reaches. The yellow in the overlay.
    public readonly HashSet<Vector2Int> coverage = new HashSet<Vector2Int>();

    public float generation;     // units/sec produced at current output
    public float draw;           // units/sec demanded by everything connected
    public float storage;        // total capacitor capacity

    /// Fraction of demand actually met — the number every consumer's output is scaled by.
    public float served = 1f;

    public float Net => generation - draw;

    /// A grid with nothing generating on it. A chain of relays with no plant at the end of it is still
    /// a grid by the connectivity rule — it just has no power IN it, which is a different and far more
    /// confusing failure than having no grid at all. Naming it means the map and the panel can say so
    /// instead of leaving the player to infer it from a zero.
    public bool Dead => generation <= 0.0001f;

    /// Dead AND out of bank — the moment a dead grid actually stops delivering.
    ///
    /// The distinction matters because a dead grid with charge left in it is still running everything on
    /// it at FULL output, off the capacitors. It's a grid on borrowed time, not a failed one. Reporting
    /// it as failed would put the panel in flat contradiction with the output figure printed beneath it,
    /// which really is ×1.00 — so "no plant" is the explanation, and this is the fault.
    public bool Failed => Dead && Stored <= 0f;

    /// Charge held across this grid's capacitors right now.
    public float Stored
    {
        get { float s = 0f; foreach (var c in capacitors) s += c.stored; return s; }
    }

    /// What this grid serves, computed from state alone rather than from the last tick.
    ///
    /// This exists because the two clocks disagree. Tick runs on ColonyManager's step — about once a
    /// SECOND — while the UI reads every FRAME. If `served` were only ever written by Tick, then on the
    /// other ~59 frames out of 60 every readout would be showing a number worked out without the
    /// capacitors in it: the panel would report a grid at 60% while it was actually delivering 100% off
    /// the bank, and the one building whose entire pitch is "rides a shortfall out" would never be seen
    /// doing it. Derived from state, the tick and the UI agree on every frame.
    public float SteadyServed
    {
        get
        {
            if (draw <= 0.0001f) return 1f;          // nothing drawing: trivially satisfied
            if (generation >= draw) return 1f;       // covered by the plants alone
            if (Stored > 0f) return 1f;              // covered by the bank, for as long as it lasts
            return Mathf.Clamp01(generation / draw); // short, and nothing left to make it up with
        }
    }

    /// The balance the grid can hold INDEFINITELY, with the bank discounted. What the player needs to
    /// know to decide between "build another capacitor" and "build another plant".
    public float Sustainable => draw <= 0.0001f ? 1f : Mathf.Clamp01(generation / draw);
}

public static class PowerGrid
{
    // What a building still manages with no grid under it at all: its own back-up plant, running badly.
    // Deliberately NOT zero. A hard zero would mean one demolished node silently switches a continent's
    // industry off, and the player's only clue is that all their numbers went to nothing at once. At a
    // third of output it's an obvious, diagnosable wound rather than a mystery.
    public const float UnpoweredFactor = 0.35f;

    // ---- Cache ----
    // Per world, per frame. Everything here is derived, so the only correctness requirement is that it
    // doesn't outlive a change to the buildings — and every mutation site calls Invalidate().
    //
    // KEYED ON THE BODY OBJECT, NOT b.id. `id` is NOT unique across a galaxy: SolarSystemGenerator
    // resets its counter to 0 for every system it makes, so the third world of system 1 and the third
    // world of system 7 are both id 2 — and ColonyManager ticks every body in the galaxy in one frame.
    // Keying on the id meant the second of any colliding pair silently got the FIRST one's grids: its
    // capacitors charged twice, its surplus exported twice, and every consumer on it pinned at the
    // unpowered floor forever because the owner lookup held the other world's buildings. CelestialBody
    // overrides neither Equals nor GetHashCode, so the reference is an exact, collision-free key.
    static readonly Dictionary<CelestialBody, List<PowerNet>> cache
        = new Dictionary<CelestialBody, List<PowerNet>>();

    // Building -> the grid that reaches it, built during the same walk that builds the nets. This is
    // not an optimisation so much as a necessity: the economy tick asks PowerFactor for every building
    // on every world, and answering by re-walking each footprint against each net's coverage allocated
    // a List per question, several times per building per tick.
    static readonly Dictionary<CelestialBody, Dictionary<PlacedBuilding, PowerNet>> ownerCache
        = new Dictionary<CelestialBody, Dictionary<PlacedBuilding, PowerNet>>();

    static int cacheFrame = -1;

    // Handed back for worlds with no surface. Shared and never written to — the economy tick asks about
    // every body every frame, and minting a throwaway list per question is pure garbage.
    static readonly List<PowerNet> none = new List<PowerNet>();

    /// Every grid on a world, derived fresh at most once a frame.
    public static List<PowerNet> Nets(CelestialBody b)
    {
        if (b == null || b.surface == null) return none;
        if (cacheFrame != Time.frameCount) { cache.Clear(); ownerCache.Clear(); cacheFrame = Time.frameCount; }
        if (cache.TryGetValue(b, out var hit)) return hit;

        var nets = Compute(b, out var owner);
        cache[b] = nets;
        ownerCache[b] = owner;
        return nets;
    }

    /// Drop the cache. Every mutation calls this so the grid answers correctly on the SAME frame the
    /// map changed, rather than one frame later.
    public static void Invalidate() { cache.Clear(); ownerCache.Clear(); cacheFrame = -1; }

    // ---- Coverage ----

    /// Does this class throw light onto ground OUTSIDE its own footprint?
    ///
    /// Only generators, and the relay whose entire purpose is to be one. This is the rule that replaced
    /// "anything with a powerRange projects": a capacitor, a switchyard and a farm all have reasons to
    /// be ON the grid and no business creating one, and letting them project meant a bank of capacitors
    /// in the desert lit fourteen tiles of nothing.
    public static bool Projects(SurfaceBuildingInfo info)
        => info != null && info.powerRange > 0f
        && (info.energyPerSec > 0f || info.type == SurfaceBuildingType.PowerNode);

    /// The tiles one building lights.
    ///
    /// For a generator (or a connected node) that is a DISC of `powerRange` around every cell of its
    /// footprint — Euclidean and measured cell-centre to cell-centre, so the reach is round rather than
    /// the square a Chebyshev range would give.
    ///
    /// For EVERYTHING ELSE it is exactly its own footprint. That is not a degenerate case, it is the
    /// pass-through rule: a building standing on the grid conducts, so its own tiles are lit and
    /// anything built against them is reached — but nothing spills onto bare ground.
    public static HashSet<Vector2Int> CoverageOf(CelestialBody b, PlacedBuilding p)
    {
        var set = new HashSet<Vector2Int>();
        if (b?.surface == null || p == null) return set;

        var cells = SurfaceBuildingDatabase.Footprint(p);
        foreach (var c in cells)
            if (c.x >= 0 && c.y >= 0 && c.x < b.surface.width && c.y < b.surface.height) set.Add(c);

        if (!Projects(p.Info)) return set;

        // A projector's reach grows with its tech level: a level-3 relay genuinely covers more ground,
        // which is what makes upgrading one worth doing instead of building a second.
        float r = p.Info.powerRange * p.LevelMult;
        int ri = Mathf.CeilToInt(r);
        float r2 = r * r;

        foreach (var cell in cells)
            for (int dy = -ri; dy <= ri; dy++)
                for (int dx = -ri; dx <= ri; dx++)
                {
                    if (dx * dx + dy * dy > r2) continue;
                    int x = cell.x + dx, y = cell.y + dy;
                    if (x < 0 || y < 0 || x >= b.surface.width || y >= b.surface.height) continue;
                    set.Add(new Vector2Int(x, y));
                }
        return set;
    }

    // ============================================================================================
    // DERIVATION — grow outward from the plants, twice
    //
    // The old derivation was a union-find over everything that had a powerRange, joined wherever two
    // discs overlapped. It could not express the rule this now needs, for a reason worth stating: union-
    // find has no notion of a SOURCE. Every participant is symmetric, so "these two nodes are joined
    // because their light overlaps" and "this node is joined because a plant reaches it" are the same
    // statement to it, and there is no way to say that the second is required for the first.
    //
    // So this is a flood fill from the generators instead, and it runs TWICE:
    //
    //   PASS 1 grows the grid using only what is definitely energised — the plants' discs, and then
    //          contact through the buildings those discs reach. Nodes joined during this pass are
    //          CONNECTED, and being connected is what switches their own projection on.
    //   PASS 2 re-runs the same fill with those nodes now projecting, which reaches further buildings,
    //          which may energise further nodes...
    //
    // ...so it repeats until nothing new lights up. That fixed point is exactly the node chain: each
    // pylon is lit by the one behind it, back to a plant, and a pylon that never gets lit never lights
    // anything. A chain in the empty desert stays dark however long it is, which is the whole point.
    //
    // Every round only ever ADDS unions, so the set of live nodes grows monotonically and the loop
    // converges — in at most one round per node, and in practice in two or three. Bounded anyway,
    // because a grid derivation that failed to terminate would hang the game rather than look wrong.
    //
    // ---- WHAT COUNTS AS AN EDGE ----
    //
    //   SHARED COVERAGE   two buildings that light the same cell are joined. This is the old rule, kept,
    //                     and it is what makes two reactors standing side by side one grid rather than
    //                     two — their discs overlap over ground neither of them occupies. What CHANGED
    //                     is which buildings have coverage beyond their own tiles at all.
    //   CONTACT           two buildings whose footprints touch edge-to-edge are joined. This is new, and
    //                     it is the pass-through rule: a building on the grid conducts, so a line of
    //                     factories carries the supply along without a pylon.
    //
    // A COMPONENT IS A GRID ONLY IF IT CONTAINS A PLANT. That one line is what kills the relay chain in
    // the desert: those nodes are perfectly well connected to each other and to nothing that generates,
    // so they form a component with no plant in it and simply are not a grid.
    // ============================================================================================
    static List<PowerNet> Compute(CelestialBody b, out Dictionary<PlacedBuilding, PowerNet> ownerByBuilding)
    {
        var nets = new List<PowerNet>();
        ownerByBuilding = new Dictionary<PlacedBuilding, PowerNet>();

        var all = SurfaceBuildManager.On(b);
        if (all.Count == 0) return nets;

        // Every building's own tiles, and a cell -> building index so contact is a lookup rather than a
        // comparison of every footprint against every other.
        int n = all.Count;
        var own = new List<List<Vector2Int>>(n);
        var cellOwner = new Dictionary<Vector2Int, int>();
        var generators = new List<int>();

        for (int i = 0; i < n; i++)
        {
            var cells = SurfaceBuildingDatabase.Footprint(all[i]);
            own.Add(cells);
            foreach (var c in cells) cellOwner[c] = i;
            if (all[i].Info.energyPerSec > 0f && all[i].Info.powerRange > 0f) generators.Add(i);
        }

        // NO PLANT, NO GRID — the whole world at once, before doing any work for it.
        if (generators.Count == 0) return nets;

        // Union-find over BUILDINGS now, rather than over projectors. Joining two buildings is the
        // primitive; what the passes below decide is which joins exist.
        var parent = new int[n];

        // Which NODES are energised, and so are projecting. Grows across rounds; never shrinks, because
        // rounds only add unions.
        var liveNodes = new HashSet<int>();
        var energised = new HashSet<int>();

        for (int round = 0; round <= n; round++)
        {
            for (int i = 0; i < n; i++) parent[i] = i;

            // ---- Edge kind 1: shared coverage ----
            // A cell claimed twice joins its two claimants. Transitive by construction: if A meets B on
            // one tile and B meets C on another, all three are one component without anyone comparing A
            // to C.
            var claimed = new Dictionary<Vector2Int, int>();
            for (int i = 0; i < n; i++)
                foreach (var c in CoverageFor(b, all[i], liveNodes.Contains(i)))
                {
                    if (claimed.TryGetValue(c, out int j)) Union(parent, i, j);
                    else claimed[c] = i;
                }

            // ---- Edge kind 2: contact ----
            for (int i = 0; i < n; i++)
                foreach (var c in own[i])
                {
                    TryContact(cellOwner, parent, i, c + Vector2Int.up);
                    TryContact(cellOwner, parent, i, c + Vector2Int.down);
                    TryContact(cellOwner, parent, i, c + Vector2Int.left);
                    TryContact(cellOwner, parent, i, c + Vector2Int.right);
                }

            // ---- Which components have a plant in them? Those, and only those, are grids. ----
            var powered = new HashSet<int>();
            foreach (int g in generators) powered.Add(Find(parent, g));

            energised.Clear();
            for (int i = 0; i < n; i++)
                if (powered.Contains(Find(parent, i))) energised.Add(i);

            // ---- Did any new node light up? If not, this is the fixed point. ----
            bool grew = false;
            for (int i = 0; i < n; i++)
                if (all[i].Info.type == SurfaceBuildingType.PowerNode
                    && energised.Contains(i) && liveNodes.Add(i)) grew = true;
            if (!grew) break;
        }

        // ---- Turn the components into nets ----
        var byRoot = new Dictionary<int, PowerNet>();
        var netOf = new PowerNet[n];

        foreach (int i in energised)
        {
            int root = Find(parent, i);
            if (!byRoot.TryGetValue(root, out var net))
            {
                net = new PowerNet();
                byRoot[root] = net;
                nets.Add(net);
            }
            netOf[i] = net;
            net.projectors.Add(all[i]);

            // COVERAGE IS WHAT THE GRID LIGHTS, which for a non-projector is its own footprint. That is
            // the "buildings receiving power highlight their own tiles in the Power overlay" behaviour,
            // and it falls out of the coverage rule rather than being drawn as a special case.
            //
            // A node contributes its disc only if it is actually live — the same CoverageFor the
            // derivation used, so what the overlay paints yellow is exactly the ground the connectivity
            // was computed from. Anything else would be a map that disagrees with the simulation.
            net.coverage.UnionWith(CoverageFor(b, all[i], liveNodes.Contains(i)));
        }

        if (nets.Count == 0) return nets;

        // NUMBER THEM DETERMINISTICALLY, by their topmost-leftmost lit tile. The derivation above walks
        // the building list, so numbering in discovery order would mean demolishing something early in
        // that list silently renumbers every grid after it — and "Grid 2" is a label the player reads on
        // the map, in the status bar and in the panel, all of which must agree and stay put.
        int w = b.surface.width;
        nets.Sort((x, y) => Anchor(x, w).CompareTo(Anchor(y, w)));
        for (int i = 0; i < nets.Count; i++) nets[i].index = i + 1;

        // Hang every building off the grid that reaches it.
        for (int i = 0; i < n; i++)
        {
            var net = netOf[i];
            if (net == null) continue;
            var p = all[i];
            ownerByBuilding[p] = net;
            var info = p.Info;

            if (info.energyPerSec > 0f)
            {
                net.generators.Add(p);
                // Matches TickOutput exactly, including the Power Distribution adjacency bonus — the
                // number the grid runs on has to be the number the player was shown on the card.
                net.generation += info.energyPerSec * p.OutputMult * (1f + SurfaceBuildManager.AdjacencyBonus(b, p));
            }
            if (info.powerStorage > 0f)
            {
                net.capacitors.Add(p);
                net.storage += info.powerStorage * p.LevelMult;
            }
            if (info.powerDraw > 0f)
            {
                net.consumers.Add(p);
                net.draw += info.powerDraw * p.LevelMult;
            }
        }

        foreach (var net in nets) net.served = net.SteadyServed;
        return nets;
    }

    /// A grid's position, as its topmost-leftmost lit tile, flattened to one comparable number.
    static long Anchor(PowerNet n, int width)
    {
        long best = long.MaxValue;
        foreach (var c in n.coverage)
        {
            long k = (long)c.y * width + c.x;
            if (k < best) best = k;
        }
        return best;
    }

    /// What this building lights, given whether it is a node that is currently live.
    ///
    /// The derivation and the overlay both go through this, so the ground the grid was computed from is
    /// exactly the ground the map paints. A node's disc is conditional — a pylon whose chain has been
    /// cut behind it is a building on the grid, not a relay — and CoverageOf cannot know that on its
    /// own, because "is this node connected" is the very thing the derivation is working out.
    static HashSet<Vector2Int> CoverageFor(CelestialBody b, PlacedBuilding p, bool nodeIsLive)
    {
        bool isNode = p.Info.type == SurfaceBuildingType.PowerNode;
        if (isNode && !nodeIsLive)
        {
            // Its own tiles only: it conducts if something reaches it, and projects nothing.
            var set = new HashSet<Vector2Int>();
            foreach (var c in SurfaceBuildingDatabase.Footprint(p))
                if (c.x >= 0 && c.y >= 0 && c.x < b.surface.width && c.y < b.surface.height) set.Add(c);
            return set;
        }
        return CoverageOf(b, p);
    }

    /// If `c` belongs to another building, join the two. This is the pass-through rule: contact
    /// conducts, whether or not either building generates anything.
    static void TryContact(Dictionary<Vector2Int, int> cellOwner, int[] parent, int from, Vector2Int c)
    {
        if (cellOwner.TryGetValue(c, out int other) && other != from) Union(parent, from, other);
    }

    static int Find(int[] parent, int i)
    {
        while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
        return i;
    }

    static void Union(int[] parent, int a, int b)
    {
        int ra = Find(parent, a), rb = Find(parent, b);
        if (ra != rb) parent[ra] = rb;
    }

    // ---- Queries ----
    /// The grid feeding a building, or null if nothing reaches it.
    public static PowerNet NetOf(CelestialBody b, PlacedBuilding p)
    {
        if (b == null || p == null) return null;
        Nets(b);   // ensures this frame's cache exists
        return ownerCache.TryGetValue(b, out var map) && map.TryGetValue(p, out var net) ? net : null;
    }

    /// Is this tile lit by anything?
    public static PowerNet NetAt(CelestialBody b, int x, int y)
    {
        var cell = new Vector2Int(x, y);
        foreach (var net in Nets(b)) if (net.coverage.Contains(cell)) return net;
        return null;
    }

    /// The grid a structure WOULD join if it were placed here — the placement preview's question.
    ///
    /// ANY cell of the footprint is enough, matching the derivation. Testing only the origin cell (the
    /// obvious shortcut) would tell a player "no grid here" for a four-tile plant whose origin happens
    /// to sit one tile off the light, and then power it fully the moment they placed it anyway.
    public static PowerNet NetForFootprint(CelestialBody b, SurfaceBuildingType t, int x, int y, int rotation)
        => NetForCells(b, SurfaceBuildingDatabase.Footprint(t, x, y, rotation));

    /// As above, for a drawn footprint.
    public static PowerNet NetForCells(CelestialBody b, IEnumerable<Vector2Int> cells)
    {
        if (cells == null) return null;
        foreach (var c in cells)
        {
            var net = NetAt(b, c.x, c.y);
            if (net != null) return net;
        }
        return null;
    }

    // ============================================================================================
    // WHERE A RELAY MAY BE PLANTED
    //
    // A Power Node is the one building whose placement is gated on the grid rather than on the ground,
    // and it has to be: its entire function is to EXTEND a grid, and a relay that can be planted in
    // empty desert is not extending anything — under the old rules two such relays fourteen tiles apart
    // were a functioning grid with no plant anywhere near them.
    //
    // TWO WAYS TO QUALIFY, and the second matters as much as the first:
    //
    //   ON LIT GROUND. A cell the grid already reaches — the yellow in the overlay. This is the ordinary
    //   case: you walk a chain outward, each pylon planted at the edge of what the last one lit.
    //
    //   TOUCHING A POWERED BUILDING. Because a building on the grid conducts (see the header), the tile
    //   against a powered factory is a legitimate place to start a chain even though the factory itself
    //   throws no light onto it. Without this the rule would be "you may only build a relay where you
    //   already have a relay", and a city block full of powered industry would somehow be an invalid
    //   place to begin.
    // ============================================================================================
    public static bool CanPlantNodeAt(CelestialBody b, IEnumerable<Vector2Int> cells, out string why)
    {
        why = null;
        if (b?.surface == null || cells == null) { why = "no ground here"; return false; }

        foreach (var c in cells)
        {
            // On the grid already.
            if (NetAt(b, c.x, c.y) != null) return true;

            // Or against something that is on it.
            if (PoweredNeighbour(b, c) != null) return true;
        }

        why = "a relay has to start from power — put it on lit ground, or against a building that " +
              "already has some";
        return false;
    }

    // ============================================================================================
    // THE LINES BETWEEN PYLONS
    //
    // A chain of relays reads as a row of unrelated blue dots. Whether two of them are actually carrying
    // power to each other — the one thing a chain is for — is invisible: it depends on their reach and
    // their tiers, which are numbers on a card, and the yellow puddles they light are the same colour
    // whether they are one grid or five.
    //
    // So the overlay draws the connection. A pair of relays ON THE SAME GRID and within reach of each
    // other gets a line, and the line means exactly what it looks like: power flows along it.
    //
    // WITHIN REACH OF EACH OTHER, not merely on the same grid. Six pylons around a city are all one
    // grid; joining every pair of them would draw a cat's cradle that says nothing. The reach test
    // leaves the actual chain — each mast to its neighbours — which is the shape the player laid down.
    // ============================================================================================
    public struct NodeLink
    {
        public Vector2Int a, b;
        public int net;        // which grid, so the overlay can colour a failing chain differently
    }

    /// Every relay-to-relay connection on this world.
    public static List<NodeLink> NodeLinks(CelestialBody b)
    {
        var links = new List<NodeLink>();
        if (b?.surface == null) return links;

        // Relays only — a plant is not a pylon and a line from a reactor to a factory would be drawing
        // the grid's whole adjacency graph rather than its transmission line.
        var relays = new List<PlacedBuilding>();
        foreach (var p in SurfaceBuildManager.On(b))
            if (p.Type == SurfaceBuildingType.PowerNode && NetOf(b, p) != null) relays.Add(p);

        for (int i = 0; i < relays.Count; i++)
            for (int j = i + 1; j < relays.Count; j++)
            {
                var pa = relays[i];
                var pb = relays[j];

                var na = NetOf(b, pa);
                if (na == null || na != NetOf(b, pb)) continue;

                // Reach is the LARGER of the two, because a link exists if either can reach the other —
                // a level-3 mast talking to a level-1 one is still one span of wire.
                float r = Mathf.Max(pa.Info.powerRange * pa.LevelMult, pb.Info.powerRange * pb.LevelMult);

                var ca = new Vector2Int(pa.x, pa.y);
                var cb = new Vector2Int(pb.x, pb.y);
                float dx = ca.x - cb.x, dy = ca.y - cb.y;
                if (dx * dx + dy * dy > r * r) continue;

                links.Add(new NodeLink { a = ca, b = cb, net = na.index });
            }

        return links;
    }

    /// A building on a live grid whose footprint touches this cell, or null.
    static PlacedBuilding PoweredNeighbour(CelestialBody b, Vector2Int c)
    {
        var up = SurfaceBuildManager.At(b, c.x, c.y + 1);
        if (up != null && NetOf(b, up) != null) return up;
        var down = SurfaceBuildManager.At(b, c.x, c.y - 1);
        if (down != null && NetOf(b, down) != null) return down;
        var left = SurfaceBuildManager.At(b, c.x - 1, c.y);
        if (left != null && NetOf(b, left) != null) return left;
        var right = SurfaceBuildManager.At(b, c.x + 1, c.y);
        if (right != null && NetOf(b, right) != null) return right;
        return null;
    }

    /// How much of its rated output a building actually manages, given the power reaching it.
    ///
    /// Ramped rather than a cliff: a grid meeting half its demand runs its buildings partway between the
    /// unpowered floor and full, so a browning-out grid degrades visibly instead of holding at 100% and
    /// then falling off a step. Things that draw nothing (a node, a plant, a farm) are always 1.
    public static float PowerFactor(CelestialBody b, PlacedBuilding p)
    {
        if (p == null || p.Info.powerDraw <= 0f) return 1f;
        var net = NetOf(b, p);
        if (net == null) return UnpoweredFactor;
        return Mathf.Lerp(UnpoweredFactor, 1f, Mathf.Clamp01(net.served));
    }

    /// Everything on this world that wants a grid and hasn't got a working one. The Power tab's headline
    /// problem — a list of what to go and fix.
    public static List<PlacedBuilding> Unpowered(CelestialBody b)
    {
        var list = new List<PlacedBuilding>();
        foreach (var p in SurfaceBuildManager.On(b))
        {
            var info = p.Info;
            bool wantsGrid = info.powerDraw > 0f || info.powerStorage > 0f;
            if (!wantsGrid) continue;

            var net = NetOf(b, p);
            // Nothing reaches it, OR what reaches it has no plant AND no charge left. Both are the same
            // 35% to the player, and listing only the first would send someone hunting for a connection
            // they already have — the actual fault being that their node chain ends in nothing.
            //
            // `Failed` rather than `Dead`: a dead grid still coasting on its capacitors is delivering
            // full output, and listing its buildings as "in the dark" would be a lie the output figure
            // would immediately contradict.
            if (net == null || (info.powerDraw > 0f && net.Failed)) list.Add(p);
        }
        return list;
    }

    public static float TotalGeneration(CelestialBody b)
    { float s = 0f; foreach (var n in Nets(b)) s += n.generation; return s; }

    public static float TotalDraw(CelestialBody b)
    { float s = 0f; foreach (var n in Nets(b)) s += n.draw; return s; }

    public static float TotalStored(CelestialBody b)
    { float s = 0f; foreach (var n in Nets(b)) s += n.Stored; return s; }

    public static float TotalStorage(CelestialBody b)
    { float s = 0f; foreach (var n in Nets(b)) s += n.storage; return s; }

    // ---- Tick ----
    // Runs before the world's structures produce anything, so `served` is this instant's truth rather
    // than last frame's. Called from SurfaceBuildManager.TickOutput.
    public static void Tick(CelestialBody b, float dt)
    {
        if (dt <= 0f) return;
        foreach (var net in Nets(b))
        {
            float made = net.generation * dt;
            float need = net.draw * dt;

            if (made >= need)
            {
                net.served = 1f;
                // Surplus tops the capacitors up first and only then leaves the world. That ordering is
                // what makes a capacitor worth building: it's the difference between a solar grid that
                // dies every time demand spikes and one that rides through on what it banked.
                float left = Charge(net, made - need);
                if (left > 0f) PlayerEconomy.Add(ResourceType.Energy, left);
            }
            else
            {
                // Short. Make up the difference from the bank if there's anything in it.
                float pulled = Drain(net, need - made);
                net.served = need <= 0.0001f ? 1f : Mathf.Clamp01((made + pulled) / need);
            }
        }
    }

    /// Push energy into a grid's capacitors. Returns what wouldn't fit.
    static float Charge(PowerNet net, float amount)
    {
        if (amount <= 0f) return 0f;
        foreach (var c in net.capacitors)
        {
            if (amount <= 0f) break;
            float cap = c.Info.powerStorage * c.LevelMult;
            float room = cap - c.stored;
            if (room <= 0f) continue;
            float put = Mathf.Min(room, amount);
            c.stored += put;
            amount -= put;
        }
        return amount;
    }

    /// Pull energy out of a grid's capacitors. Returns what was actually available.
    static float Drain(PowerNet net, float amount)
    {
        if (amount <= 0f) return 0f;
        float got = 0f;
        foreach (var c in net.capacitors)
        {
            if (amount <= 0f) break;
            float take = Mathf.Min(c.stored, amount);
            c.stored -= take;
            got += take;
            amount -= take;
        }
        return got;
    }

    // ---- Presentation ----
    public static string SupplyLabel(PowerNet net)
    {
        if (net.Failed) return net.draw > 0.0001f ? "no plant — dead grid" : "no plant on it";
        if (net.Dead) return "no plant — running on the bank";
        if (net.draw <= 0.0001f) return "spare capacity";
        if (net.served >= 0.999f) return net.Sustainable >= 0.999f ? "fully supplied" : "running on the bank";
        if (net.served >= 0.75f) return "strained";
        if (net.served >= 0.4f) return "browning out";
        return "failing";
    }

    public static Color SupplyColor(PowerNet net)
    {
        if (net.Failed) return UITheme.Bad;
        if (net.Dead) return UITheme.Warn;                 // coasting on the bank: a warning, not a fault
        if (net.draw <= 0.0001f) return UITheme.SubText;
        if (net.served >= 0.999f) return net.Sustainable >= 0.999f ? UITheme.Good : UITheme.Warn;
        if (net.served >= 0.6f) return UITheme.Warn;
        return UITheme.Bad;
    }
}
