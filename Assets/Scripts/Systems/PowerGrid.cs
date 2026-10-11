using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// THE POWER GRID — electricity as a PLACE rather than a number.
//
// Energy used to be a stockpile and nothing else: a plant added to it, anything anywhere could spend
// it, and where you put the plant never mattered. This makes power LOCAL. A generator lights the ground
// around itself; a Power Node relays that reach further; buildings pass power on to the buildings they
// touch, a few at a time; and anything off the grid limps along on its own emergency backup.
//
// ---- WHO LIGHTS GROUND, AND WHO ONLY CARRIES IT ----
//
//   A GENERATOR PROJECTS. It lights its own footprint and a disc around it (two to four tiles by plant
//   type, times its tier). That projection is what founds a grid: without a plant, nothing is lit.
//
//   A POWER NODE projects a wide disc like a plant, but ONLY once it is itself connected. A pylon is
//   a REPEATER: whatever its disc touches is powered directly again, so a chain of pylons carries power
//   as far as you care to build it.
//
//   EVERY OTHER BUILDING CONDUCTS — UP TO A POINT (2026-10-11). "If a building is placed next to another
//   building that is already powered, it should be powered too, up to a point."
//
// ---- THE ADJACENCY RULE: HOPS ----
//
//   HOP 0   a plant, a live pylon, or any building with a tile inside a plant's or pylon's disc.
//   HOP 1   a building touching (edge to edge) a hop-0 building.
//   HOP k   a building touching a hop-(k-1) building.
//
//   A building is ON THE GRID at hop 0 .. MaxContactHops (3). Past that it is TOO FAR: it touches the
//   grid, it is drawn and listed as "too far", and it runs on its emergency backup like anything else
//   off-grid. A building counts as ONE hop however many tiles it has — the unit is "a building", which
//   is how the player reads it ("powered through 2 buildings, 1 hop left").
//
//   A pylon planted against a hop-3 building is energised (it is at hop <= 3), and being a pylon it
//   then projects, so everything in its disc is hop 0 again. That is the pylon's job in one line.
//
// ---- GRIDS ----
//
//   A grid is a connected component of energised buildings, joined by: a disc covering another
//   building, two discs overlapping, or two energised buildings touching. Every component contains a
//   plant by construction (everything energised was reached FROM a plant), so a relay chain in the
//   empty desert is still not a grid.
//
// ---- SUPPLY, DEMAND, AND WHO GOES DARK FIRST ----
//
//   Generation vs draw decides each grid's supply. Surplus charges the capacitors, then goes to the
//   empire stockpile; a shortfall drains the capacitors first. When the bank is empty and demand still
//   exceeds supply, power is shared out NEAREST FIRST: every hop-0 consumer is served before any hop-1
//   consumer gets anything, and so on outward. So a short grid browns out from the END OF ITS CHAINS
//   inward — the buildings hanging off the most neighbours lose power first, the ones under a plant's
//   disc last. Within one hop it is an even split. One rule, visible on the map (the hop tags), the
//   building card ("this building gets 40% — it is 3 hops out") and the grid list.
//
//   A consumer's output runs at Lerp(UnpoweredFactor, 1, its served fraction). UnpoweredFactor is the
//   EMERGENCY BACKUP, now 15% (was 35%): a severely short grid, or no grid at all, very nearly halts a
//   building. It is deliberately not zero, and it is the SAME floor for both cases, so being connected
//   to a starved grid is never worse than being disconnected — otherwise demolishing your own pylon
//   would be an optimisation — and a colony whose power collapses can still trickle the metal it needs
//   to build the plant that fixes it.
//
//   Demand scales with what you built: DrawOf = powerDraw x tech level x BuildScaling's cost curve for
//   a drawn footprint (a 6-tile mine draws 6.75x a 1-tile one — "upkeep scales like cost", as
//   BuildScaling always said; the draw used to ignore size entirely, so a 20-tile mine drew what a
//   1-tile mine drew and power never constrained anything).
//
// ---- DERIVED, NOT MAINTAINED; AND MEMOISED ACROSS FRAMES ----
//
//   There is no merge code and no split code: one function derives every grid on a world from the
//   buildings standing on it. The TOPOLOGY (who is energised, hops, components, coverage) is cached
//   per world and rebuilt only when the buildings' structural signature changes (positions, shapes,
//   types, tiers) or Invalidate() is called. The NUMBERS (generation, draw, served) are refreshed at
//   most once a frame from that topology — cheap, allocation-free. Every mutation site still calls
//   Invalidate(), so a placement is answered correctly on the same frame.
// ============================================================================================
public class PowerNet
{
    public int index;                                   // 1-based, stable while the topology holds

    /// What PROJECTS on this grid: its plants and its live relays. (It used to hold every member, which
    /// made every "pylon" count on the cards also count farms and mines.)
    public readonly List<PlacedBuilding> projectors = new List<PlacedBuilding>();
    public readonly List<PlacedBuilding> generators = new List<PlacedBuilding>();
    public readonly List<PlacedBuilding> capacitors = new List<PlacedBuilding>();

    /// Everything drawing power here, NEAREST FIRST (by hop) — the order a shortfall is shared out in.
    public readonly List<PlacedBuilding> consumers = new List<PlacedBuilding>();

    /// Every building on this grid, whatever it does.
    public readonly List<PlacedBuilding> members = new List<PlacedBuilding>();

    /// Every tile this grid reaches: its projectors' discs plus every member's own footprint.
    public readonly HashSet<Vector2Int> coverage = new HashSet<Vector2Int>();

    public float generation;     // units/sec produced at current output
    public float draw;           // units/sec demanded by everything connected
    public float storage;        // total capacitor capacity

    /// Fraction of TOTAL demand actually met.
    public float served = 1f;

    /// Per consumer: the fraction of ITS demand met under nearest-first sharing. Absent = 1.
    public readonly Dictionary<PlacedBuilding, float> servedOf = new Dictionary<PlacedBuilding, float>();

    /// The furthest hop any member sits at, and how many members are fed only through other buildings.
    public int deepestHop;
    public int viaContact;

    public float Net => generation - draw;

    /// A grid with nothing generating on it right now (a plant at zero efficiency, say).
    public bool Dead => generation <= 0.0001f;

    /// Dead AND out of bank — the moment a dead grid actually stops delivering.
    public bool Failed => Dead && Stored <= 0f;

    /// Charge held across this grid's capacitors right now.
    public float Stored
    {
        get { float s = 0f; foreach (var c in capacitors) s += c.stored; return s; }
    }

    /// What this grid serves, computed from state alone rather than from the last tick (the UI reads
    /// every frame; the economy ticks about once a second).
    public float SteadyServed
    {
        get
        {
            if (draw <= 0.0001f) return 1f;
            if (generation >= draw) return 1f;
            if (Stored > 0f) return 1f;
            return Mathf.Clamp01(generation / draw);
        }
    }

    /// The supply rate the grid can deliver right now, bank included (see SteadyServed).
    public float SteadySupply
    {
        get
        {
            if (draw <= 0.0001f) return 0f;
            if (generation >= draw || Stored > 0f) return draw;
            return generation;
        }
    }

    /// The balance the grid can hold INDEFINITELY, with the bank discounted.
    public float Sustainable => draw <= 0.0001f ? 1f : Mathf.Clamp01(generation / draw);
}

public static class PowerGrid
{
    /// The EMERGENCY BACKUP: what a consumer manages with no grid, or on a grid giving it nothing. See
    /// the header on why it is 15% and why it is the same floor for both.
    public const float UnpoweredFactor = 0.15f;

    /// How many buildings power passes through by contact before it gives out. See the header.
    public const int MaxContactHops = 3;

    /// How far past the limit the derivation keeps measuring, so a too-far building can say how far.
    const int FarSearch = 6;

    /// One building's place in the power graph.
    public struct Link
    {
        /// 0 = a plant, a live pylon, or lit directly by a disc. 1..MaxContactHops = fed through that
        /// many buildings. Above MaxContactHops = touching the grid but too far along the chain.
        public int hop;
        /// The building it takes its power from by contact (null at hop 0).
        public PlacedBuilding via;
        /// For a hop-0 building lit by a disc: the plant or pylon whose disc it is (null otherwise).
        public PlacedBuilding source;
        public bool Powered => hop <= MaxContactHops;
    }

    // ---- The memo ----
    // KEYED ON THE BODY OBJECT, NOT b.id: ids repeat across systems (SolarSystemGenerator restarts its
    // counter per system), and two worlds sharing a key once shared one set of grids.
    sealed class Topology
    {
        public long sig;
        public int checkedFrame = -1, numbersFrame = -1, lastUsed;
        public readonly List<PlacedBuilding> all = new List<PlacedBuilding>();
        public readonly Dictionary<PlacedBuilding, int> indexOf = new Dictionary<PlacedBuilding, int>();
        public readonly Dictionary<Vector2Int, int> cellOwner = new Dictionary<Vector2Int, int>();
        public List<PlacedBuilding>[] neighbours = new List<PlacedBuilding>[0];
        public readonly List<PowerNet> nets = new List<PowerNet>();
        public readonly Dictionary<PlacedBuilding, PowerNet> owner = new Dictionary<PlacedBuilding, PowerNet>();
        public readonly Dictionary<PlacedBuilding, Link> links = new Dictionary<PlacedBuilding, Link>();
    }

    static readonly Dictionary<CelestialBody, Topology> topo = new Dictionary<CelestialBody, Topology>();
    static readonly List<PowerNet> none = new List<PowerNet>();
    static readonly List<PlacedBuilding> noBuildings = new List<PlacedBuilding>();
    static readonly List<CelestialBody> pruneScratch = new List<CelestialBody>();
    static int pruneFrame = -1;

    /// Every grid on a world, with this frame's numbers.
    public static List<PowerNet> Nets(CelestialBody b)
    {
        var t = Topo(b);
        if (t == null) return none;
        Numbers(b, t);
        return t.nets;
    }

    /// Drop every memo. Every mutation calls this so the grid answers correctly on the SAME frame the
    /// map changed, rather than one frame later.
    public static void Invalidate()
    {
        topo.Clear();
        SurfaceSynergy.Invalidate();
        Industry.Invalidate();
    }

    static Topology Topo(CelestialBody b)
    {
        if (b == null || b.surface == null) return null;
        int frame = Time.frameCount;
        topo.TryGetValue(b, out var t);
        if (t != null && t.checkedFrame == frame) return t;

        var all = SurfaceBuildManager.On(b);
        long sig = Signature(b, all);
        if (t == null || t.sig != sig)
        {
            t = Build(b, all, sig);
            topo[b] = t;
        }
        t.checkedFrame = frame;
        t.lastUsed = frame;
        Prune(frame);
        return t;
    }

    /// Forget worlds nobody has asked about in a while (a replaced galaxy's bodies, mostly).
    static void Prune(int frame)
    {
        if (frame - pruneFrame < 600) return;
        pruneFrame = frame;
        pruneScratch.Clear();
        foreach (var kv in topo) if (frame - kv.Value.lastUsed > 1200) pruneScratch.Add(kv.Key);
        foreach (var k in pruneScratch) topo.Remove(k);
        pruneScratch.Clear();
    }

    static long Mix(long h, long v) { unchecked { return (h ^ v) * 1099511628211L; } }

    /// Everything the topology depends on, hashed without allocating: which records stand here (by
    /// reference), their type, tier, origin, rotation and drawn cells. Not health, not efficiency, not
    /// charge — those move the numbers, which are refreshed every frame anyway.
    static long Signature(CelestialBody b, List<PlacedBuilding> all)
    {
        long h = 1469598103934665603L;
        h = Mix(h, b.surface.width); h = Mix(h, b.surface.height); h = Mix(h, all.Count);
        for (int i = 0; i < all.Count; i++)
        {
            var p = all[i];
            if (p == null) { h = Mix(h, -1); continue; }
            h = Mix(h, System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(p));
            h = Mix(h, p.type); h = Mix(h, p.level); h = Mix(h, p.x); h = Mix(h, p.y); h = Mix(h, p.rotation);
            if (p.HasDrawnShape)
            {
                h = Mix(h, p.cellsX.Count);
                for (int k = 0; k < p.cellsX.Count; k++) h = Mix(h, p.cellsX[k] * 73856093L ^ p.cellsY[k] * 19349663L);
            }
        }
        return h;
    }

    // ---- Coverage ----

    /// Does this class throw light onto ground OUTSIDE its own footprint? Only generators, and the relay.
    public static bool Projects(SurfaceBuildingInfo info)
        => info != null && info.powerRange > 0f
        && (info.energyPerSec > 0f || info.type == SurfaceBuildingType.PowerNode);

    /// The tiles one building lights: a Euclidean disc of powerRange x tier around every footprint cell
    /// for a projector, and exactly its own footprint for everything else. (A pylon's disc is shown even
    /// when it is not connected — this answers "what would it light"; the derivation decides whether it
    /// actually does.)
    public static HashSet<Vector2Int> CoverageOf(CelestialBody b, PlacedBuilding p)
    {
        var set = new HashSet<Vector2Int>();
        if (b?.surface == null || p == null) return set;
        var cells = SurfaceBuildingDatabase.Footprint(p);
        foreach (var c in cells)
            if (c.x >= 0 && c.y >= 0 && c.x < b.surface.width && c.y < b.surface.height) set.Add(c);
        if (Projects(p.Info)) AddDisc(b, cells, p.Info.powerRange * p.LevelMult, set);
        return set;
    }

    static void AddDisc(CelestialBody b, List<Vector2Int> cells, float r, HashSet<Vector2Int> into)
    {
        int ri = Mathf.CeilToInt(r);
        float r2 = r * r;
        int w = b.surface.width, h = b.surface.height;
        foreach (var cell in cells)
            for (int dy = -ri; dy <= ri; dy++)
                for (int dx = -ri; dx <= ri; dx++)
                {
                    if (dx * dx + dy * dy > r2) continue;
                    int x = cell.x + dx, y = cell.y + dy;
                    if (x < 0 || y < 0 || x >= w || y >= h) continue;
                    into.Add(new Vector2Int(x, y));
                }
    }

    // ============================================================================================
    // DERIVATION
    //
    // Rounds of a breadth-first search, because pylons switch on only once they are reached:
    //   1. Sources at hop 0: every plant, every live pylon, and every building with a tile in one of
    //      their discs.
    //   2. BFS through edge contact: a neighbour of a hop-h building is at hop h+1. Measured past the
    //      limit (FarSearch) so too-far buildings can say how far; energised means hop <= the limit.
    //   3. Any pylon now energised goes live and starts projecting — run again. Liveness only grows, so
    //      this converges, in practice in two or three rounds; bounded by the building count anyway.
    // Then a union-find over the energised buildings splits them into grids.
    // ============================================================================================
    static Topology Build(CelestialBody b, List<PlacedBuilding> list, long sig)
    {
        var t = new Topology { sig = sig };
        foreach (var p in list) if (p != null && p.Info != null) t.all.Add(p);
        var all = t.all;
        int n = all.Count;

        var own = new List<Vector2Int>[n];
        for (int i = 0; i < n; i++)
        {
            t.indexOf[all[i]] = i;
            own[i] = SurfaceBuildingDatabase.Footprint(all[i]);
            foreach (var c in own[i]) t.cellOwner[c] = i;
        }

        // Edge neighbours, once. Shared with SurfaceSynergy, which asks the same question.
        var nbr = new List<int>[n];
        t.neighbours = new List<PlacedBuilding>[n];
        var seen = new HashSet<int>();
        for (int i = 0; i < n; i++)
        {
            nbr[i] = new List<int>();
            t.neighbours[i] = new List<PlacedBuilding>();
            seen.Clear();
            foreach (var c in own[i])
            {
                TryNeighbour(t, i, c + Vector2Int.up, seen, nbr[i]);
                TryNeighbour(t, i, c + Vector2Int.down, seen, nbr[i]);
                TryNeighbour(t, i, c + Vector2Int.left, seen, nbr[i]);
                TryNeighbour(t, i, c + Vector2Int.right, seen, nbr[i]);
            }
            foreach (int j in nbr[i]) t.neighbours[i].Add(all[j]);
        }

        var isGen = new bool[n];
        var isNode = new bool[n];
        var live = new bool[n];
        var disc = new HashSet<Vector2Int>[n];
        bool anyGen = false;
        for (int i = 0; i < n; i++)
        {
            var info = all[i].Info;
            isGen[i] = info.energyPerSec > 0f && info.powerRange > 0f;
            isNode[i] = info.type == SurfaceBuildingType.PowerNode;
            if (isGen[i])
            {
                anyGen = true;
                live[i] = true;
                disc[i] = new HashSet<Vector2Int>();
                AddDisc(b, own[i], info.powerRange * all[i].LevelMult, disc[i]);
            }
        }
        if (!anyGen) return t;   // NO PLANT, NO GRID

        var hop = new int[n];
        var via = new int[n];
        var src = new int[n];
        var queue = new int[n];
        int limit = MaxContactHops + FarSearch;

        for (int round = 0; round <= n; round++)
        {
            int head = 0, tail = 0;
            for (int i = 0; i < n; i++) { hop[i] = int.MaxValue; via[i] = -1; src[i] = -1; }

            for (int i = 0; i < n; i++)
                if (live[i]) { hop[i] = 0; queue[tail++] = i; }

            for (int i = 0; i < n; i++)
            {
                if (!live[i]) continue;
                foreach (var c in disc[i])
                    if (t.cellOwner.TryGetValue(c, out int j) && hop[j] != 0)
                    { hop[j] = 0; src[j] = i; queue[tail++] = j; }
            }

            while (head < tail)
            {
                int u = queue[head++];
                if (hop[u] >= limit) continue;
                int next = hop[u] + 1;
                foreach (int v in nbr[u])
                    if (hop[v] > next) { hop[v] = next; via[v] = u; queue[tail++] = v; }
            }

            bool grew = false;
            for (int i = 0; i < n; i++)
                if (isNode[i] && !live[i] && hop[i] <= MaxContactHops)
                {
                    live[i] = true;
                    disc[i] = new HashSet<Vector2Int>();
                    AddDisc(b, own[i], all[i].Info.powerRange * all[i].LevelMult, disc[i]);
                    grew = true;
                }
            if (!grew) break;
        }

        // ---- Components over the energised ----
        var on = new bool[n];
        for (int i = 0; i < n; i++) on[i] = hop[i] <= MaxContactHops;

        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;
        var claimed = new Dictionary<Vector2Int, int>();
        for (int i = 0; i < n; i++)
        {
            if (!live[i] || !on[i]) continue;
            foreach (var c in disc[i])
            {
                if (claimed.TryGetValue(c, out int k)) Union(parent, i, k); else claimed[c] = i;
                if (t.cellOwner.TryGetValue(c, out int j) && on[j]) Union(parent, i, j);
            }
        }
        for (int i = 0; i < n; i++)
        {
            if (!on[i]) continue;
            foreach (int j in nbr[i]) if (on[j]) Union(parent, i, j);
        }

        var byRoot = new Dictionary<int, PowerNet>();
        var netOf = new PowerNet[n];
        for (int i = 0; i < n; i++)
        {
            if (!on[i]) continue;
            int root = Find(parent, i);
            if (!byRoot.TryGetValue(root, out var net)) { net = new PowerNet(); byRoot[root] = net; t.nets.Add(net); }
            netOf[i] = net;
            net.members.Add(all[i]);
            net.coverage.UnionWith(own[i]);
            if (live[i]) { net.projectors.Add(all[i]); net.coverage.UnionWith(disc[i]); }
            if (hop[i] > net.deepestHop) net.deepestHop = hop[i];
            if (hop[i] > 0) net.viaContact++;
        }

        // NUMBERED DETERMINISTICALLY by their topmost-leftmost lit tile, so "Grid 2" stays put on the
        // map, the card and the list while the player builds elsewhere.
        int w = b.surface.width;
        t.nets.Sort((x, y) => Anchor(x, w).CompareTo(Anchor(y, w)));
        for (int i = 0; i < t.nets.Count; i++) t.nets[i].index = i + 1;

        for (int i = 0; i < n; i++)
        {
            if (hop[i] != int.MaxValue)
                t.links[all[i]] = new Link
                {
                    hop = hop[i],
                    via = via[i] >= 0 ? all[via[i]] : null,
                    source = src[i] >= 0 ? all[src[i]] : null
                };

            var net = netOf[i];
            if (net == null) continue;
            var p = all[i];
            t.owner[p] = net;
            var info = p.Info;
            if (info.energyPerSec > 0f) net.generators.Add(p);
            if (info.powerStorage > 0f) net.capacitors.Add(p);
            if (info.powerDraw > 0f) net.consumers.Add(p);
        }

        // Nearest first: the order a shortfall is shared out in. Stable on the building list for ties.
        foreach (var net in t.nets)
        {
            var hops = t.links;
            var idx = t.indexOf;
            net.consumers.Sort((x, y) =>
            {
                int c = hops[x].hop.CompareTo(hops[y].hop);
                return c != 0 ? c : idx[x].CompareTo(idx[y]);
            });
        }
        return t;
    }

    static void TryNeighbour(Topology t, int i, Vector2Int c, HashSet<int> seen, List<int> into)
    {
        if (t.cellOwner.TryGetValue(c, out int j) && j != i && seen.Add(j)) into.Add(j);
    }

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

    // ---- The numbers, once a frame ----
    static void Numbers(CelestialBody b, Topology t)
    {
        int frame = Time.frameCount;
        if (t.numbersFrame == frame) return;
        t.numbersFrame = frame;   // first, so nothing below can re-enter

        foreach (var net in t.nets)
        {
            net.generation = 0f; net.draw = 0f; net.storage = 0f;
            foreach (var p in net.generators)
                // Matches SurfaceOutput.Energy exactly, synergy (the switchyard) included — the number the
                // grid runs on has to be the number the card shows.
                net.generation += p.Info.energyPerSec * p.OutputMult * (1f + SurfaceSynergy.Bonus(b, p));
            foreach (var p in net.capacitors) net.storage += p.Info.powerStorage * p.LevelMult;
            foreach (var p in net.consumers) net.draw += DrawOf(p);
            net.served = net.SteadyServed;
            Allocate(t, net, net.SteadySupply);
        }
    }

    /// Share `supply` (per second) over the grid's consumers, NEAREST FIRST. See the header.
    static void Allocate(Topology t, PowerNet net, float supply)
    {
        net.servedOf.Clear();
        if (net.draw <= 0.0001f || supply >= net.draw - 0.0001f) return;   // everyone at 1

        float left = Mathf.Max(0f, supply);
        int i = 0, count = net.consumers.Count;
        while (i < count)
        {
            int h = HopIn(t, net.consumers[i]);
            int j = i;
            float tier = 0f;
            while (j < count && HopIn(t, net.consumers[j]) == h) { tier += DrawOf(net.consumers[j]); j++; }
            float s = tier > 0.0001f ? Mathf.Clamp01(left / tier) : 1f;
            for (int k = i; k < j; k++) net.servedOf[net.consumers[k]] = s;
            left = Mathf.Max(0f, left - tier);
            i = j;
        }
    }

    static int HopIn(Topology t, PlacedBuilding p) => t.links.TryGetValue(p, out var l) ? l.hop : 0;

    // ---- Queries ----

    /// The grid feeding a building, or null if nothing reaches it (or it is too far along a chain).
    public static PowerNet NetOf(CelestialBody b, PlacedBuilding p)
    {
        if (b == null || p == null) return null;
        var t = Topo(b);
        if (t == null) return null;
        Numbers(b, t);
        return t.owner.TryGetValue(p, out var net) ? net : null;
    }

    /// Where a building sits in the power graph. False if no grid's chain touches it at all.
    public static bool LinkOf(CelestialBody b, PlacedBuilding p, out Link link)
    {
        link = default;
        var t = Topo(b);
        return t != null && p != null && t.links.TryGetValue(p, out link);
    }

    /// The hop a building is powered at (0..MaxContactHops), or -1 if it is not on a grid.
    public static int HopOf(CelestialBody b, PlacedBuilding p)
        => LinkOf(b, p, out var l) && l.Powered ? l.hop : -1;

    /// A building that touches a grid's chain but sits past the hop limit: which grid, and how far.
    public static bool TooFar(CelestialBody b, PlacedBuilding p, out PowerNet near, out int hops)
    {
        near = null; hops = 0;
        var t = Topo(b);
        if (t == null || p == null || t.owner.ContainsKey(p)) return false;
        if (!t.links.TryGetValue(p, out var l) || l.Powered) return false;
        hops = l.hop;
        // Walk the chain back to the first energised building; its grid is the one this is too far from.
        var cur = l.via;
        for (int guard = 0; cur != null && guard < 64; guard++)
        {
            if (t.owner.TryGetValue(cur, out near)) break;
            cur = t.links.TryGetValue(cur, out var cl) ? cl.via : null;
        }
        if (near != null) Numbers(b, t);
        return near != null;
    }

    /// Every building whose footprint touches `p` edge to edge. Cached with the topology; do not modify.
    public static IReadOnlyList<PlacedBuilding> NeighboursOf(CelestialBody b, PlacedBuilding p)
    {
        var t = Topo(b);
        if (t == null || p == null || !t.indexOf.TryGetValue(p, out int i)) return noBuildings;
        return t.neighbours[i];
    }

    /// The building on a cell, from the cached cell index (far cheaper than SurfaceBuildManager.At).
    public static PlacedBuilding OwnerAt(CelestialBody b, Vector2Int c)
    {
        var t = Topo(b);
        return t != null && t.cellOwner.TryGetValue(c, out int i) ? t.all[i] : null;
    }

    /// Is this tile lit by anything?
    public static PowerNet NetAt(CelestialBody b, int x, int y)
    {
        var cell = new Vector2Int(x, y);
        foreach (var net in Nets(b)) if (net.coverage.Contains(cell)) return net;
        return null;
    }

    /// What a consumer draws: base draw x tech level x the cost curve of its drawn size.
    public static float DrawOf(PlacedBuilding p)
    {
        if (p == null || p.Info == null || p.Info.powerDraw <= 0f) return 0f;
        float size = p.HasDrawnShape ? BuildScaling.CostMultiplier(p.TileCount) : 1f;
        return p.Info.powerDraw * p.LevelMult * size;
    }

    /// What a new building of this class and size would draw.
    public static float DrawFor(SurfaceBuildingInfo info, int tiles, bool drawn)
        => info == null ? 0f : info.powerDraw * (drawn ? BuildScaling.CostMultiplier(Mathf.Max(1, tiles)) : 1f);

    /// The fraction of ITS demand a consumer is getting (1 for anything that draws nothing; 0 off-grid).
    public static float ServedOf(CelestialBody b, PlacedBuilding p)
    {
        if (p == null || p.Info == null || p.Info.powerDraw <= 0f) return 1f;
        var net = NetOf(b, p);
        if (net == null) return 0f;
        return net.servedOf.TryGetValue(p, out float s) ? s : 1f;
    }

    // ============================================================================================
    // WHAT A BUILDING WOULD GET IF IT WERE PLACED HERE — the placement preview's question
    //
    // Any cell in a lit disc: hop 0. Otherwise the best energised neighbour's hop + 1, if within the
    // limit. Otherwise the grid it would touch but be too far from. The same rule the derivation uses,
    // asked of cells that hold nothing yet.
    // ============================================================================================
    public struct Prospect
    {
        public PowerNet net;     // the grid it would join, or null
        public int hop;          // its hop on that grid
        public PowerNet near;    // net == null: a grid it would touch but be too far from...
        public int nearHop;      // ...and the hop it would have been at
        public bool Connected => net != null;
    }

    public static Prospect ProspectFor(CelestialBody b, IEnumerable<Vector2Int> cells, PlacedBuilding extending = null)
    {
        var r = new Prospect { hop = -1, nearHop = -1 };
        var t = Topo(b);
        if (t == null || cells == null) return r;
        Numbers(b, t);

        // Extending a standing building: the merged building keeps that building's place in the graph,
        // unless the new tiles reach something nearer.
        if (extending != null && t.owner.TryGetValue(extending, out var en) && t.links.TryGetValue(extending, out var el))
        { r.net = en; r.hop = el.hop; }

        var mine = new HashSet<Vector2Int>(cells);
        foreach (var c in mine)
            foreach (var net in t.nets)
                if (net.coverage.Contains(c) && !t.cellOwner.ContainsKey(c))
                { r.net = net; r.hop = 0; return r; }

        int farHop = int.MaxValue; PowerNet farNet = null;
        foreach (var c in mine)
        {
            Consider(t, c + Vector2Int.up, mine, extending, ref r, ref farHop, ref farNet);
            Consider(t, c + Vector2Int.down, mine, extending, ref r, ref farHop, ref farNet);
            Consider(t, c + Vector2Int.left, mine, extending, ref r, ref farHop, ref farNet);
            Consider(t, c + Vector2Int.right, mine, extending, ref r, ref farHop, ref farNet);
        }
        if (r.net == null && farNet != null) { r.near = farNet; r.nearHop = farHop; }
        return r;
    }

    static void Consider(Topology t, Vector2Int c, HashSet<Vector2Int> mine, PlacedBuilding extending,
                         ref Prospect r, ref int farHop, ref PowerNet farNet)
    {
        if (mine.Contains(c) || !t.cellOwner.TryGetValue(c, out int j)) return;
        var p = t.all[j];
        if (p == extending) return;
        if (!t.links.TryGetValue(p, out var l)) return;
        int h = l.hop + 1;
        if (t.owner.TryGetValue(p, out var net) && h <= MaxContactHops)
        {
            if (r.net == null || h < r.hop) { r.net = net; r.hop = h; }
            return;
        }
        // Too far: find the grid this chain belongs to.
        PowerNet near = net;
        var cur = l.via;
        for (int guard = 0; near == null && cur != null && guard < 64; guard++)
        {
            if (t.owner.TryGetValue(cur, out near)) break;
            cur = t.links.TryGetValue(cur, out var cl) ? cl.via : null;
        }
        if (near != null && h < farHop) { farHop = h; farNet = near; }
    }

    /// The grid a structure WOULD join if it were placed here (any footprint cell, plus contact).
    public static PowerNet NetForFootprint(CelestialBody b, SurfaceBuildingType t, int x, int y, int rotation)
        => ProspectFor(b, SurfaceBuildingDatabase.Footprint(t, x, y, rotation)).net;

    /// As above, for a drawn footprint.
    public static PowerNet NetForCells(CelestialBody b, IEnumerable<Vector2Int> cells)
        => ProspectFor(b, cells).net;

    /// One rich line for the placement readouts: what a structure of this class would feed or take from
    /// the grid at these cells, and by what route. Null when power does not concern it.
    public static string ProspectText(CelestialBody b, SurfaceBuildingInfo info, IEnumerable<Vector2Int> cells,
                                      int tiles, bool drawn, PlacedBuilding extending = null)
    {
        if (b == null || info == null || cells == null) return null;
        bool gen = info.energyPerSec > 0f, node = info.type == SurfaceBuildingType.PowerNode;
        if (!gen && !node && info.powerDraw <= 0f && info.powerStorage <= 0f)
        {
            // Not a consumer, but it still CONDUCTS — say whether it would extend a grid.
            var pc = ProspectFor(b, cells, extending);
            return pc.Connected && pc.hop < MaxContactHops
                ? $"<color=#9FB4C8>carries Grid {pc.net.index} on (hop {pc.hop} of {MaxContactHops})</color>"
                : null;
        }

        var pr = ProspectFor(b, cells, extending);
        if (gen)
            return pr.Connected || pr.near != null
                ? $"<color=#F5F58C>feeds Grid {(pr.net ?? pr.near).index}</color>"
                : "<color=#F5F58C>starts a new grid</color>";

        if (node)
            return pr.Connected
                ? $"<color=#4DC8FF>extends Grid {pr.net.index}</color>"
                : "<color=#FF6659>a relay has to start from power — on lit ground, or touching a powered building</color>";
        string what = info.powerDraw > 0f ? $"draws {DrawFor(info, tiles, drawn):0.0}" : $"banks {info.powerStorage:0}";
        if (pr.Connected)
        {
            if (pr.hop == 0) return $"{what} · <color=#4DC8FF>Grid {pr.net.index}, lit directly</color>";
            int left = MaxContactHops - pr.hop;
            string hex = left == 0 ? "FFBF4D" : "4DC8FF";
            return $"{what} · <color=#{hex}>Grid {pr.net.index} via {pr.hop} adjacent building{(pr.hop == 1 ? "" : "s")}, " +
                   $"{left} hop{(left == 1 ? "" : "s")} left</color>";
        }
        if (pr.near != null)
            return $"<color=#FF6659>{what} — too far: {pr.nearHop} buildings from Grid {pr.near.index} (limit {MaxContactHops}). " +
                   $"Add a pylon or plant nearer</color>";
        return $"<color=#FF6659>{what} — no grid reaches here (runs at {UnpoweredFactor * 100f:0}% backup)</color>";
    }

    // ============================================================================================
    // WHERE A RELAY MAY BE PLANTED — on lit ground, or touching a building that would pass it power
    // WITHIN THE HOP LIMIT. The limit matters: a pylon planted against a building already at the limit
    // would itself be too far, never go live, and stand dark — the rule and the derivation would disagree.
    // ============================================================================================
    public static bool CanPlantNodeAt(CelestialBody b, IEnumerable<Vector2Int> cells, out string why)
    {
        why = null;
        if (b?.surface == null || cells == null) { why = "no ground here"; return false; }
        var pr = ProspectFor(b, cells);
        if (pr.Connected) return true;
        why = pr.near != null
            ? $"too far from power — that chain is already {pr.nearHop - 1} buildings long (limit {MaxContactHops}); start nearer a plant or pylon"
            : "a relay has to start from power — put it on lit ground, or against a building that already has some";
        return false;
    }

    // ============================================================================================
    // THE LINES BETWEEN PYLONS — relay-to-relay spans on the same grid and within reach of each other.
    // ============================================================================================
    public struct NodeLink
    {
        public Vector2Int a, b;
        public int net;
    }

    public static List<NodeLink> NodeLinks(CelestialBody b)
    {
        var links = new List<NodeLink>();
        if (b?.surface == null) return links;

        var relays = new List<PlacedBuilding>();
        foreach (var net in Nets(b))
            foreach (var p in net.projectors)
                if (p.Type == SurfaceBuildingType.PowerNode) relays.Add(p);

        for (int i = 0; i < relays.Count; i++)
            for (int j = i + 1; j < relays.Count; j++)
            {
                var pa = relays[i];
                var pb = relays[j];
                var na = NetOf(b, pa);
                if (na == null || na != NetOf(b, pb)) continue;
                float r = Mathf.Max(pa.Info.powerRange * pa.LevelMult, pb.Info.powerRange * pb.LevelMult);
                var ca = new Vector2Int(pa.x, pa.y);
                var cb = new Vector2Int(pb.x, pb.y);
                float dx = ca.x - cb.x, dy = ca.y - cb.y;
                if (dx * dx + dy * dy > r * r) continue;
                links.Add(new NodeLink { a = ca, b = cb, net = na.index });
            }
        return links;
    }

    /// How much of its rated output a consumer manages: Lerp(backup, 1, its own served fraction).
    public static float PowerFactor(CelestialBody b, PlacedBuilding p)
    {
        if (p == null || p.Info == null || p.Info.powerDraw <= 0f) return 1f;
        var net = NetOf(b, p);
        if (net == null) return UnpoweredFactor;
        return PowerFactorFor(net.servedOf.TryGetValue(p, out float s) ? s : 1f);
    }

    /// The output factor a consumer runs at when this fraction of its demand is met.
    public static float PowerFactorFor(float served) => Mathf.Lerp(UnpoweredFactor, 1f, Mathf.Clamp01(served));

    /// Everything on this world that wants a grid and hasn't got a working one.
    public static List<PlacedBuilding> Unpowered(CelestialBody b)
    {
        var list = new List<PlacedBuilding>();
        foreach (var p in SurfaceBuildManager.On(b))
        {
            var info = p.Info;
            if (info == null) continue;
            bool wantsGrid = info.powerDraw > 0f || info.powerStorage > 0f;
            if (!wantsGrid) continue;
            var net = NetOf(b, p);
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
        var t = Topo(b);
        if (t == null) return;
        Numbers(b, t);
        foreach (var net in t.nets)
        {
            float made = net.generation * dt;
            float need = net.draw * dt;

            if (made >= need)
            {
                net.served = 1f;
                net.servedOf.Clear();
                // Surplus tops the capacitors up first and only then leaves the world.
                float left = Charge(net, made - need);
                if (left > 0f) PlayerEconomy.Add(ResourceType.Energy, left);
            }
            else
            {
                float pulled = Drain(net, need - made);
                net.served = need <= 0.0001f ? 1f : Mathf.Clamp01((made + pulled) / need);
                Allocate(t, net, (made + pulled) / dt);
            }
        }
    }

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
        if (net.Dead) return UITheme.Warn;
        if (net.draw <= 0.0001f) return UITheme.SubText;
        if (net.served >= 0.999f) return net.Sustainable >= 0.999f ? UITheme.Good : UITheme.Warn;
        if (net.served >= 0.6f) return UITheme.Warn;
        return UITheme.Bad;
    }

    /// Each grid's own identifying colour — the tag on its buildings and its row in the grid list. Hue
    /// only identifies; health is always said in words and in SupplyColor beside it.
    static readonly Color[] gridHues =
    {
        new Color(0.30f, 0.78f, 1.00f), new Color(1.00f, 0.62f, 0.25f), new Color(0.50f, 0.92f, 0.45f),
        new Color(0.95f, 0.45f, 0.85f), new Color(1.00f, 0.90f, 0.35f), new Color(0.45f, 1.00f, 0.90f),
        new Color(0.75f, 0.60f, 1.00f), new Color(1.00f, 0.50f, 0.50f)
    };

    public static Color GridColor(int index) => gridHues[((Mathf.Max(1, index) - 1) % gridHues.Length + gridHues.Length) % gridHues.Length];

    /// How a building gets its power, in words — the card's line and the dark list's.
    public static string ReachText(CelestialBody b, PlacedBuilding p)
    {
        if (b == null || p?.Info == null) return "";
        var net = NetOf(b, p);
        if (net != null && LinkOf(b, p, out var l))
        {
            if (l.hop == 0)
            {
                if (Projects(p.Info)) return p.Info.energyPerSec > 0f ? $"a plant on Grid {net.index}" : $"a live relay on Grid {net.index}";
                string by = l.source?.Info != null ? l.source.Info.name : "a plant";
                return $"lit directly by the {by}'s reach (Grid {net.index})";
            }
            int left = MaxContactHops - l.hop;
            string from = l.via?.Info != null ? $" — through the {l.via.Info.name}" : "";
            return $"powered through {l.hop} adjacent building{(l.hop == 1 ? "" : "s")}{from}, " +
                   $"{left} hop{(left == 1 ? "" : "s")} left (Grid {net.index})";
        }
        if (TooFar(b, p, out var near, out int hops))
            return $"too far — {hops} buildings from Grid {near.index}'s reach (limit {MaxContactHops}). " +
                   "Put a pylon or plant nearer";
        return "no grid reaches it";
    }
}
