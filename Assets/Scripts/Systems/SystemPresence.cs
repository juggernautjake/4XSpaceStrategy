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
    public static bool HasPresence(StarSystemData sys)
    {
        if (sys == null) return false;
        if (sys.owner == FactionManager.Player) return true;

        foreach (var b in sys.AllBodies())
        {
            if (b == null) continue;
            if (b.owner == FactionManager.Player || b.settled) return true;
            if (b.units != null)
                foreach (var u in b.units)
                    if (u != null && u.owner == FactionManager.Player) return true;
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
        if (sys.visited) return true;
        if (!HasPresence(sys)) return false;
        sys.visited = true;
        return true;
    }

    /// The system a body belongs to, or null. Walks up through a moon's parent first — a moon's units
    /// are its own, but its SYSTEM is its planet's.
    public static StarSystemData SystemOf(CelestialBody b)
    {
        if (b == null || SystemContext.Galaxy == null) return null;
        var top = b;
        int guard = 0;
        while (top.parentBody != null && guard++ < 8) top = top.parentBody;

        foreach (var sys in SystemContext.Galaxy.systems)
            foreach (var member in sys.AllBodies())
                if (ReferenceEquals(member, top) || ReferenceEquals(member, b)) return sys;
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
