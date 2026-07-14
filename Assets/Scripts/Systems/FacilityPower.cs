using UnityEngine;

// The two "how much can I work on at once" pools.
//
// A facility's LEVEL buys parallelism, not just speed: a level-1 shipyard has 2 build power, and every
// tier adds one (Lv3 = 4, Lv5 = 6). Shipyard technologies add more on top of that, per shipyard — so a
// level-2 yard with the right research can match a level-4 one. Research centres work identically with
// research capacity.
//
// Each ship costs build power while it is under construction and releases it the moment it rolls out;
// each technology costs research capacity while it is being studied. That is what lets you run two
// scouts at once, or save the whole pool for one terraformer.
public static class BuildPower
{
    // Power a single shipyard of this tier provides. Level 1 = 2, and each tier adds one.
    // Researched shipyard technologies add their bonus to EVERY shipyard you own.
    public static int ForLevel(int level)
        => level < 1 ? 0 : level + 1 + TechEffects.ShipyardPowerBonus;

    public static int ForBody(CelestialBody b)
        => b == null || b.shipyardLevel < 1 ? 0 : ForLevel(b.shipyardLevel);

    // Every shipyard in the empire pools its power: ships are built from the combined total.
    //
    // Memoized per frame: the UI asks for this for every hull in the catalogue every frame, and walking
    // every body in the galaxy that many times a frame is pure waste. The pool can only change on a
    // build/upgrade/conquest, none of which happen twice within one frame.
    static int cachedTotal = -1, cachedFrame = -1;

    public static int PlayerTotal()
    {
        if (cachedFrame == Time.frameCount && cachedTotal >= 0) return cachedTotal;

        int total = 0;
        if (SystemContext.Galaxy != null)
            foreach (var b in SystemContext.AllBodies())
                if (b != null && b.owner == FactionManager.Player) total += ForBody(b);
        // The home world always runs at least a level-1 yard, even before the galaxy is registered.
        if (total <= 0)
        {
            var home = UnitManager.Instance != null ? UnitManager.Instance.HomePlanet : null;
            if (home != null) total = ForLevel(Mathf.Max(1, home.shipyardLevel));
        }

        cachedTotal = total; cachedFrame = Time.frameCount;
        return total;
    }

    // How many shipyards are pooling (for the readout).
    public static int PlayerYardCount()
    {
        int n = 0;
        if (SystemContext.Galaxy != null)
            foreach (var b in SystemContext.AllBodies())
                if (b != null && b.owner == FactionManager.Player && b.shipyardLevel >= 1) n++;
        return Mathf.Max(1, n);
    }
}

public static class ResearchCapacity
{
    // Capacity a single research centre of this tier provides — same curve as a shipyard.
    public static int ForLevel(int level)
        => level < 1 ? 0 : level + 1 + TechEffects.ResearchCapacityBonus;

    public static int ForBody(CelestialBody b)
        => b == null || b.researchCenterLevel < 1 ? 0 : ForLevel(b.researchCenterLevel);

    // Every research centre pools its capacity. With none built you still get a single point, so the
    // empire can always study something (slowly) from its home laboratories.
    // Memoized per frame for the same reason as BuildPower.PlayerTotal.
    static int cachedTotal = -1, cachedFrame = -1;

    public static int PlayerTotal()
    {
        if (cachedFrame == Time.frameCount && cachedTotal >= 0) return cachedTotal;

        int total = 0;
        if (SystemContext.Galaxy != null)
            foreach (var b in SystemContext.AllBodies())
                if (b != null && b.owner == FactionManager.Player) total += ForBody(b);

        cachedTotal = Mathf.Max(1, total); cachedFrame = Time.frameCount;
        return cachedTotal;
    }

    public static int PlayerLabCount()
    {
        int n = 0;
        if (SystemContext.Galaxy != null)
            foreach (var b in SystemContext.AllBodies())
                if (b != null && b.owner == FactionManager.Player && b.researchCenterLevel >= 1) n++;
        return n;
    }
}
