using UnityEngine;

// ============================================================================================
// IS THE EMPIRE IN THIS SYSTEM?
//
// The one question the system-level fog of war turns on. A body is drawn as itself — real textures,
// real type, real name — as soon as the player has ANYTHING in its system: a ship in orbit, a station
// deployed, a world claimed or settled. Take everything out again and the system does not go dark:
// what you have seen, you have seen.
//
// ---- WHY PRESENCE AND NOT A SURVEY ------------------------------------------------------------
// The fog used to be driven by explorationProgress: a world stayed a black sphere, slowly greyed as a
// ship surveyed it, and only became a planet at 100%. That put two different facts on one dial. Being
// able to SEE that the fourth planet is a frozen world is a matter of having a telescope in the same
// solar system; knowing its habitability, its ores and where to build is a matter of having mapped it.
// Merging them meant a fleet could sit in orbit around an obviously banded gas giant and the game
// would insist it was an unidentified black ball.
//
// So they are separate now, and each answers the question it is actually about:
//
//   nothing in the system   — black spheres; only the star is drawn. You know a system is there.
//   a unit in the system    — every body drawn as itself. Type is readable. Nothing else is.
//   survey level 1          — the surface map and habitability.
//   survey level 2          — the index overlays.
//
// VISITED IS STICKY. `StarSystemData.visited` is set the first time the player has presence and never
// cleared, because the alternative is a system that becomes a mystery again the moment a scout leaves
// — which is not fog of war, it is amnesia.
// ============================================================================================
public static class SystemPresence
{
    /// Does the player have a unit, station or holding anywhere in this system right now?
    ///
    /// MEMOISED FOR A FRACTION OF A SECOND, because this is a scan and it is asked in a loop: every
    /// unidentified world polls it, and a world only stops polling once the answer comes back true. So
    /// on a fresh galaxy — where almost nothing is visited and therefore almost everything is asking —
    /// the uncached version runs the scan hundreds of times a second to produce an answer that changes
    /// when a ship arrives, which is not a per-frame event.
    ///
    /// Short enough that a fleet arriving still lights the system up within a blink.
    const float PresenceMemoSeconds = 0.4f;
    static readonly System.Collections.Generic.Dictionary<StarSystemData, (float at, bool val)> presenceMemo
        = new System.Collections.Generic.Dictionary<StarSystemData, (float, bool)>();

    public static bool HasPresence(StarSystemData sys)
    {
        if (sys == null) return false;

        float now = Time.unscaledTime;
        if (presenceMemo.TryGetValue(sys, out var memo) && now - memo.at < PresenceMemoSeconds)
            return memo.val;

        bool result = Scan(sys);
        presenceMemo[sys] = (now, result);
        return result;
    }

    // ============================================================================================
    // THE DETECTION THRESHOLD — a circle you cross, not a place you land
    //
    // A system gives itself up when a ship gets close enough to look at it properly, which is a
    // DISTANCE, not an arrival. Sitting a scout a hair outside the outermost orbit and learning nothing
    // is the wrong answer; so is having to fly all the way to a planet before the system stops being a
    // row of black spheres.
    //
    // So the radius sits a margin beyond the furthest world's orbit: cross it and everything inside is
    // identified. That also makes the reveal happen while the ship is still MOVING, which is when the
    // player is looking at it.
    // ============================================================================================

    /// A margin past the outermost orbit, so the threshold is comfortably outside the system rather
    /// than sitting on top of its last planet.
    const float DetectionMargin = 1.25f;

    /// A floor, for a system whose worlds all huddle close to their star — and for one with no bodies
    /// at all, where the radius would otherwise be zero and could never be crossed.
    const float DetectionMinRadius = 12f;

    /// How close a ship must come to identify what is in this system.
    public static float DetectionRadius(StarSystemData sys)
    {
        if (sys == null) return 0f;
        if (sys.detectionRadiusOverride > 0f) return sys.detectionRadiusOverride;

        float far = 0f;
        if (sys.bodies != null)
            foreach (var b in sys.bodies)
                if (b != null) far = Mathf.Max(far, b.orbitRadius);

        return Mathf.Max(DetectionMinRadius, far * DetectionMargin);
    }

    /// The default, ignoring any Dev override — what the slider resets to.
    public static float NaturalDetectionRadius(StarSystemData sys)
    {
        if (sys == null) return DetectionMinRadius;
        float far = 0f;
        if (sys.bodies != null)
            foreach (var b in sys.bodies)
                if (b != null) far = Mathf.Max(far, b.orbitRadius);
        return Mathf.Max(DetectionMinRadius, far * DetectionMargin);
    }

    static bool Scan(StarSystemData sys)
    {
        if (sys.owner == FactionManager.Player) return true;

        foreach (var b in sys.AllBodies())
        {
            if (b == null) continue;
            if (b.owner == FactionManager.Player || b.settled) return true;
            if (b.units != null)
                foreach (var u in b.units)
                    if (u != null && u.owner == FactionManager.Player) return true;
        }

        // ...and anything of the player's that has crossed the threshold without having arrived at a
        // body yet: a ship in transit, or one parked in open space inside the system.
        var um = UnitManager.Instance;
        if (um != null && sys.pivot != null)
        {
            float r = DetectionRadius(sys);
            float r2 = r * r;
            Vector3 centre = sys.pivot.position;
            foreach (var u in um.Units)
            {
                if (u == null || u.owner != FactionManager.Player) continue;
                if (u.location != null) continue;              // already counted above
                if ((um.UnitPos(u) - centre).sqrMagnitude <= r2) return true;
            }
        }
        return false;
    }

    /// Has the player ever had presence here? Sticky — see the header.
    ///
    /// Recomputed rather than only event-driven so that a save written before `visited` existed, and a
    /// system the player is standing in right now, both come out true without needing a migration pass.
    public static bool Known(StarSystemData sys)
    {
        if (sys == null) return false;
        if (GameMode.DevMode) return true;

        // THE HOME SYSTEM IS NEVER A MYSTERY. A civilisation does not reach the space age without having
        // pointed a telescope at its own neighbours: every world around its own star has been catalogued
        // for centuries before the first ship leaves. Stated outright rather than left to fall out of
        // "the player owns a world here", because that inference depends on ownership being assigned
        // before the visuals are built, and it silently was not — which is exactly how the home system
        // came up as black spheres with only the homeworld showing.
        if (sys.isHome) { sys.visited = true; return true; }

        if (sys.visited) return true;
        if (!HasPresence(sys)) return false;
        sys.visited = true;
        return true;
    }

    // ---- body -> system, without walking the galaxy every time ---------------------------------
    //
    // This is asked once per body per BodyFog poll — four times a second for every unidentified world
    // on the map — and the obvious implementation is a nested scan of every system's every body, which
    // makes the whole thing quadratic in the galaxy and allocates an iterator per system per call. On a
    // twelve-system galaxy of three hundred bodies that is hundreds of thousands of steps a second to
    // answer a question whose answer never changes.
    //
    // So it is indexed once and rebuilt only when the galaxy object itself is replaced. Keyed on the
    // Galaxy REFERENCE rather than a version counter: a new game or a load builds a new Galaxy, which
    // is exactly when the map is stale and the only time it is.
    static Galaxy indexedFor;
    static readonly System.Collections.Generic.Dictionary<CelestialBody, StarSystemData> systemOf
        = new System.Collections.Generic.Dictionary<CelestialBody, StarSystemData>();

    static void EnsureIndex()
    {
        var g = SystemContext.Galaxy;
        if (g == null || ReferenceEquals(g, indexedFor)) return;

        systemOf.Clear();
        foreach (var sys in g.systems)
            foreach (var member in sys.AllBodies())
                if (member != null) systemOf[member] = sys;
        indexedFor = g;
    }

    /// Drops the index. For anything that adds or removes bodies inside the CURRENT galaxy — the Dev
    /// sandbox does — where the Galaxy reference is unchanged and so the automatic rebuild would not
    /// fire.
    public static void Invalidate()
    {
        indexedFor = null;
        presenceMemo.Clear();
    }

    /// The system a body belongs to, or null. Walks up through a moon's parent first — a moon's units
    /// are its own, but its SYSTEM is its planet's.
    public static StarSystemData SystemOf(CelestialBody b)
    {
        if (b == null || SystemContext.Galaxy == null) return null;
        EnsureIndex();

        if (systemOf.TryGetValue(b, out var direct)) return direct;

        // A moon added after the index was built, or one whose parent is the thing that is listed.
        var top = b;
        int guard = 0;
        while (top.parentBody != null && guard++ < 8)
        {
            top = top.parentBody;
            if (systemOf.TryGetValue(top, out var viaParent)) return viaParent;
        }
        return null;
    }

    /// Should this body be drawn as itself rather than as an unidentified silhouette?
    ///
    /// Owning or standing on the body is enough on its own, so a world the player holds is never a
    /// black ball because the galaxy list happened to be rebuilt.
    public static bool Revealed(CelestialBody b)
    {
        if (b == null) return false;
        if (GameMode.DevMode) return true;
        if (b.owner == FactionManager.Player || b.settled || b.Surveyed) return true;
        if (b.units != null)
            foreach (var u in b.units)
                if (u != null && u.owner == FactionManager.Player) return true;
        return Known(SystemOf(b));
    }
}
