using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// THE POWER GRID — electricity as a PLACE rather than a number.
//
// Energy used to be a stockpile and nothing else: a plant added to it, anything anywhere could spend
// it, and where you put the plant never mattered. This makes power LOCAL. A generator lights the ground
// around itself; a Power Node relays that reach seven tiles further; anything standing on lit ground
// runs properly, and anything off it limps along on its own back-up plant.
//
// ---- What a grid IS ----
// A grid is a CONNECTED COMPONENT OF PROJECTORS. A projector is anything that lights ground:
//
//   GENERATOR     — lights its own footprint and the ring immediately around it, and feeds the grid.
//   POWER NODE    — a 1x1 relay with a wide circular reach and no output of its own. The thing you
//                   chain across a continent to join two cities together.
//   SWITCHYARD    — Power Distribution: a modest relay that also boosts the plants it touches.
//   THE CAPITOL   — the colony's founding reactor, but it lights only its own doorstep (one tile),
//                   so reaching anywhere else is what the rest of the Electrical category is for.
//
// Two projectors are in the same grid when the ground they light OVERLAPS. That single rule is the
// whole system, and every behaviour in the spec falls out of it:
//
//   - Chain nodes between two cities and the two grids BECOME one grid. Nothing merges them; they were
//     never separate once their light overlapped.
//   - Blow a node out of the middle of that chain and they are two grids again. Nothing splits them.
//   - Drop a generator inside an existing grid and it contributes to that grid rather than starting its
//     own — again, not because anything checked, but because its light overlaps.
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
    /// The tiles one projector lights: everything within its range of any cell of its own footprint.
    /// Range is Euclidean and measured cell-centre to cell-centre, so a node's reach is a DISC — the
    /// spec's "circularly around it" — rather than the square a Chebyshev range would give.
    public static HashSet<Vector2Int> CoverageOf(CelestialBody b, PlacedBuilding p)
    {
        var set = new HashSet<Vector2Int>();
        float r = p.Info.powerRange;
        if (r <= 0f || b?.surface == null) return set;

        // A node's reach grows with its tech level: a level-3 relay genuinely covers more ground, which
        // is what makes upgrading one worth doing instead of building a second.
        r *= p.LevelMult;
        int ri = Mathf.CeilToInt(r);
        float r2 = r * r;

        foreach (var cell in SurfaceBuildingDatabase.Footprint(p))
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

    // ---- Derivation ----
    static List<PowerNet> Compute(CelestialBody b, out Dictionary<PlacedBuilding, PowerNet> ownerByBuilding)
    {
        var nets = new List<PowerNet>();
        ownerByBuilding = new Dictionary<PlacedBuilding, PowerNet>();

        var projectors = new List<PlacedBuilding>();
        foreach (var p in SurfaceBuildManager.On(b))
            if (p.Info.powerRange > 0f) projectors.Add(p);
        if (projectors.Count == 0) return nets;

        var cov = new List<HashSet<Vector2Int>>(projectors.Count);
        foreach (var p in projectors) cov.Add(CoverageOf(b, p));

        // Union-find over projectors, joined by any tile two of them both light. Walking the cells once
        // and unioning on collision is what makes "their light overlaps" transitive: if A meets B on one
        // tile and B meets C on another, all three end up in one component without anyone comparing A to
        // C. That transitivity IS the node chain.
        var parent = new int[projectors.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        var claimed = new Dictionary<Vector2Int, int>();
        for (int i = 0; i < projectors.Count; i++)
            foreach (var c in cov[i])
            {
                if (claimed.TryGetValue(c, out int j)) Union(parent, i, j);
                else claimed[c] = i;
            }

        // Component root -> net.
        var byRoot = new Dictionary<int, PowerNet>();
        for (int i = 0; i < projectors.Count; i++)
        {
            int root = Find(parent, i);
            if (!byRoot.TryGetValue(root, out var net))
            {
                net = new PowerNet();
                byRoot[root] = net;
                nets.Add(net);
            }
            net.projectors.Add(projectors[i]);
            net.coverage.UnionWith(cov[i]);
        }

        // NUMBER THEM DETERMINISTICALLY, by their topmost-leftmost lit tile. The derivation above walks
        // the building list, so numbering in discovery order would mean demolishing something early in
        // that list silently renumbers every grid after it — and "Grid 2" is a label the player reads on
        // the map, in the status bar and in the panel, all of which must agree and stay put.
        int w = b.surface.width;
        nets.Sort((x, y) => Anchor(x, w).CompareTo(Anchor(y, w)));
        for (int i = 0; i < nets.Count; i++) nets[i].index = i + 1;

        // Which grid owns each lit tile. Unambiguous by construction: two grids can't share a tile, or
        // the union-find above would have made them one grid.
        var ownerOf = new Dictionary<Vector2Int, PowerNet>();
        foreach (var net in nets)
            foreach (var c in net.coverage) ownerOf[c] = net;

        // Hang every building off the grid that reaches it.
        foreach (var p in SurfaceBuildManager.On(b))
        {
            var net = NetCovering(ownerOf, p);
            if (net == null) continue;
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

    static PowerNet NetCovering(Dictionary<Vector2Int, PowerNet> ownerOf, PlacedBuilding p)
    {
        // ANY cell of the footprint is enough. A plant with one corner on the grid is wired in — asking
        // for the whole footprint would make big buildings mysteriously harder to connect than small
        // ones, for no reason the player could see.
        foreach (var c in SurfaceBuildingDatabase.Footprint(p))
            if (ownerOf.TryGetValue(c, out var net)) return net;
        return null;
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
    /// Must agree with NetCovering, which accepts any cell of the footprint. Testing only the origin
    /// cell (the obvious shortcut) would tell a player "no grid here" for a four-tile plant whose origin
    /// happens to sit one tile off the light, and then power it fully the moment they placed it anyway.
    public static PowerNet NetForFootprint(CelestialBody b, SurfaceBuildingType t, int x, int y, int rotation)
    {
        foreach (var c in SurfaceBuildingDatabase.Footprint(t, x, y, rotation))
        {
            var net = NetAt(b, c.x, c.y);
            if (net != null) return net;
        }
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
