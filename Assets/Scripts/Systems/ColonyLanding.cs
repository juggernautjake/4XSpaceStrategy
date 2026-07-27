using UnityEngine;

// ============================================================================================
// WHERE THE COLONY SHIP COMES DOWN — THE PLAYER'S CHOICE, NOT THE GAME'S
//
// Founding a colony used to happen entirely off-screen: the ship reached 100%, the game called
// SurfaceBuildManager.FindSpot, dropped the grounded hull on whatever tile that returned, and told you
// afterwards. The first permanent structure on a world — the one every road, grid and city grows out
// of — was sited by a helper function whose whole rule is "the first place it fits".
//
// Now the landing is a PLACEMENT. Settling opens the world's surface in Build Mode with the colony ship
// held on the cursor, and the world is not settled until you put it down. Where you land is the first
// real decision you make about a planet, and it should be one you make while looking at the ore and the
// fertile ground you surveyed to get here.
//
// WHY A SEPARATE LITTLE CLASS RATHER THAN A FIELD ON THE BODY. The landing spans three systems that do
// not otherwise talk: UnitManager finishes the flight, PlanetViewWindow takes the placement, and the
// Unit itself has to survive in between so it can be consumed at the right moment (and still be there,
// in orbit, if the player changes their mind). A body field would have to be serialized, and a landing
// half-completed across a save is a state with no good answer. This is deliberately RUNTIME-ONLY and
// deliberately at most one at a time — you cannot be landing two colony ships in the same instant,
// because the placement UI is modal on one world.
//
// IF THE PLAYER WALKS AWAY. Closing the window, pressing Escape, or anything else that abandons the
// placement calls Abandon(), which puts the ship back in orbit exactly as it was. It does NOT
// auto-place. Landing somewhere the player did not choose is the behaviour this whole file exists to
// remove, and doing it as a "helpful" fallback would just reintroduce it at the worst moment — when
// they have deliberately declined to pick a spot.
// ============================================================================================
public static class ColonyLanding
{
    /// The world awaiting a landing site, or null. Runtime only — never saved; see the header.
    public static CelestialBody Body { get; private set; }

    /// The colony ship that will be consumed when the site is chosen.
    public static Unit Ship { get; private set; }

    /// Is a landing waiting for a site right now?
    public static bool Active => Body != null;

    /// Is THIS world the one awaiting a site?
    public static bool AwaitingOn(CelestialBody b) => b != null && Body == b;

    /// Fired when a landing starts or ends, so the surface view can pick up or drop the held building.
    public static System.Action OnChanged;

    /// The ship has arrived and the player must now choose where it comes down.
    public static void Begin(CelestialBody b, Unit ship)
    {
        if (b == null) return;
        Body = b;
        Ship = ship;
        OnChanged?.Invoke();
    }

    /// The site was chosen and the base is standing. The world becomes a colony and the ship is consumed.
    ///
    /// Cleared BEFORE the work is done, so anything FinishColonyLanding touches — the surface view, the
    /// notification's callback, the owner ring — sees a world with no landing pending rather than one
    /// still asking for a site it has already been given.
    public static void Complete()
    {
        var ship = Ship;
        var body = Body;
        Body = null; Ship = null;

        // The population, the flag, the first city and the disappearance of the ship all live in
        // UnitManager with the rest of colony founding. Splitting them across two files would mean two
        // places to keep in step over what "settled" means.
        UnitManager.Instance?.FinishColonyLanding(body, ship);

        OnChanged?.Invoke();
    }

    /// The player declined to place it. The ship stays in orbit and the world stays unsettled — they can
    /// press Settle again whenever they like. See the header for why this does not auto-place.
    public static void Abandon()
    {
        Body = null; Ship = null;
        OnChanged?.Invoke();
    }
}
