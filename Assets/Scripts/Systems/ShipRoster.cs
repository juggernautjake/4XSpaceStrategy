using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// WHO IS WHERE, AND WHAT ARE THEY DOING — one answer for every ship list (2026-10-11)
//
// "Click a planet and see all of the ships that are orbiting/docked there." Three lists needed that
// answer — the Inspector's Orbit tab, the Planet View's Orbit tab and the Fleet list — and each used to
// build its own, slightly differently: body.units plus the player's own parked ships, with no fog check,
// so a rival parked in a planet's orbital field never appeared while a rival docked at a world did.
//
// THE RULE, matching how orbit actually works (UnitManager.Travel, ZoomField.cs):
//
//   * DOCKED     — `u.location == body`: the ship is in body.units and rides along with the world.
//                  A player's "move here" into a planet's orbital field ends exactly like this, so a
//                  ship ordered into orbit is docked by construction.
//   * AT A MOON  — docked at one of the planet's moons. The planet's field holds its moons, so the
//                  planet's list includes them, labelled with the moon they are at.
//   * PARKED     — holding a point in open space inside the planet's orbital field (any owner — AI patrol
//                  legs and standoffs keep exact points and can end inside a field).
//
// And never a ship the player is not shown: VisibilityService.ShownToPlayer is the same test the
// renderers, the shots and the explosions use, so a list can never disagree with the map.
// ============================================================================================
public static class ShipRoster
{
    public struct Entry
    {
        public Unit unit;
        /// Null when docked at the listed world itself; the moon's name when docked at one of its moons.
        public string where;
        public bool parked;
    }

    /// Every ship the player can see in this world's orbit, in display order: the player's own first,
    /// then everyone else by owner, ships before stations, then by name.
    public static void CollectAt(CelestialBody b, List<Entry> into)
    {
        if (b == null || into == null) return;
        int start = into.Count;

        AddDocked(b, null, into);

        if (ZoomFieldRules.IsHost(b))
        {
            if (b.moons != null)
                foreach (var m in b.moons) AddDocked(m, m.name, into);

            var um = UnitManager.Instance;
            if (um != null)
                foreach (var u in um.Units)
                {
                    if (u == null || u.IsDestroyed || u.location != null || !u.inSpace) continue;
                    if (!ZoomFieldRules.Contains(b, u.parkPosition)) continue;
                    if (!VisibilityService.ShownToPlayer(u)) continue;
                    into.Add(new Entry { unit = u, parked = true });
                }
        }

        into.Sort(start, into.Count - start, Order);
    }

    static void AddDocked(CelestialBody at, string where, List<Entry> into)
    {
        if (at == null || at.units == null) return;
        foreach (var u in at.units)
        {
            if (u == null || u.IsDestroyed) continue;
            if (!VisibilityService.ShownToPlayer(u)) continue;
            into.Add(new Entry { unit = u, where = where });
        }
    }

    static readonly EntryOrder Order = new EntryOrder();

    class EntryOrder : IComparer<Entry>
    {
        public int Compare(Entry a, Entry b)
        {
            int oa = OwnerRank(a.unit.owner), ob = OwnerRank(b.unit.owner);
            if (oa != ob) return oa.CompareTo(ob);
            bool sa = a.unit.Info.isStation, sb = b.unit.Info.isStation;
            if (sa != sb) return sa ? 1 : -1;
            return string.CompareOrdinal(a.unit.name, b.unit.name);
        }
    }

    static int OwnerRank(Faction f) => f == FactionManager.Player ? -1 : (f != null ? f.id : 999);

    /// A short string that changes whenever the SET of ships listed at this world changes — for the
    /// windows' structural signatures, so an arrival, a departure or a rival drifting into the field
    /// rebuilds the list, and nothing else does.
    public static string Signature(CelestialBody b)
    {
        scratch.Clear();
        CollectAt(b, scratch);
        var sb = new System.Text.StringBuilder(scratch.Count * 4);
        foreach (var e in scratch) sb.Append(e.unit.id).Append(e.parked ? 'p' : ',');
        return sb.ToString();
    }

    static readonly List<Entry> scratch = new List<Entry>();

    // ---- Status --------------------------------------------------------------------------------

    /// What a ship is doing, as one word for a status column ("Docked", "Moving", "Surveying"...).
    public static string StatusWord(Unit u)
    {
        if (u == null) return "";
        switch (u.status)
        {
            case UnitStatus.Traveling:   return "Moving";
            case UnitStatus.Exploring:   return "Surveying";
            case UnitStatus.Researching: return "Deep survey";
            case UnitStatus.Colonizing:  return "Colonizing";
            case UnitStatus.Returning:   return "Returning";
        }
        if (u.Info.isStation) return "Deployed";
        if (u.location != null) return u.location.owner == u.owner && u.owner != null ? "Docked" : "In orbit";
        if (u.inSpace) return ZoomFieldRules.HostAt(u.parkPosition) != null ? "In orbit" : "Holding";
        return "Idle";
    }

    /// The status word's colour: green for ships at rest, cyan for moving, amber for working.
    public static Color StatusColor(Unit u)
    {
        if (u == null) return UITheme.SubText;
        switch (u.status)
        {
            case UnitStatus.Traveling:
            case UnitStatus.Returning: return UITheme.Accent;
            case UnitStatus.Exploring:
            case UnitStatus.Researching:
            case UnitStatus.Colonizing: return UITheme.Warn;
        }
        return UITheme.Good;
    }

    /// The status with its place: "Docked at Mirfak II", "Moving to Tarsis", "Holding in deep space".
    public static string StatusLine(Unit u)
    {
        if (u == null) return "";
        string word = StatusWord(u);
        if (u.status == UnitStatus.Traveling || u.status == UnitStatus.Returning)
        {
            if (u.travelTarget != null) return $"{word} to {u.travelTarget.name}";
            var host = ZoomFieldRules.HostAt(u.travelTo);
            return host != null ? $"{word} to {host.name}'s orbit" : $"{word} (deep space)";
        }
        if (u.location != null) return word == "Docked" ? $"Docked at {u.location.name}"
                                    : word == "In orbit" ? $"In orbit of {u.location.name}"
                                    : word == "Deployed" ? $"Deployed at {u.location.name}"
                                    : $"{word} at {u.location.name}";
        if (u.inSpace)
        {
            var host = ZoomFieldRules.HostAt(u.parkPosition);
            if (host != null) return word == "Deployed" ? $"Deployed in {host.name}'s orbit" : $"In orbit of {host.name}";
            return word == "Deployed" ? "Deployed in deep space" : "Holding in deep space";
        }
        return word;
    }

    /// At rest with nothing to do: the ships the "next idle ship" key walks through.
    public static bool IsIdle(Unit u)
        => u != null && !u.IsDestroyed && u.status == UnitStatus.Idle && (u.orders == null || u.orders.Count == 0);

    // ---- Where, for grouping --------------------------------------------------------------------

    /// The world a ship is at or orbiting, or null in open space / in transit. A ship parked in a
    /// planet's orbital field counts as that planet's; a ship at a moon counts as the moon's.
    public static CelestialBody PlaceOf(Unit u)
    {
        if (u == null) return null;
        if (u.location != null) return u.location;
        if (u.inSpace && u.status != UnitStatus.Traveling) return ZoomFieldRules.HostAt(u.parkPosition);
        return null;
    }

    /// The solar system a ship is in: its world's system, or the system whose detection circle holds it.
    /// Null between the stars.
    public static StarSystemData SystemOf(Unit u)
    {
        if (u == null) return null;
        var place = PlaceOf(u);
        if (place != null)
        {
            if (place.system != null) return place.system;
            if (place.parentBody != null && place.parentBody.system != null) return place.parentBody.system;
            var s = SystemPresence.SystemOf(place);
            if (s != null) return s;
        }
        var um = UnitManager.Instance;
        return um != null ? SystemAt(um.UnitPos(u)) : null;
    }

    /// The system whose detection circle holds a point (deepest inside wins), or null.
    public static StarSystemData SystemAt(Vector3 point)
    {
        var g = SystemContext.Galaxy;
        if (g == null || g.systems == null) return null;
        StarSystemData best = null;
        float bestRatio = float.MaxValue;
        foreach (var sys in g.systems)
        {
            if (sys == null) continue;
            float r = SystemPresence.DetectionRadius(sys);
            if (r <= 0f) continue;
            Vector3 c = sys.pivot != null ? sys.pivot.position : sys.galaxyPosition;
            float dx = point.x - c.x, dz = point.z - c.z;
            float ratio = Mathf.Sqrt(dx * dx + dz * dz) / r;
            if (ratio <= 1f && ratio < bestRatio) { bestRatio = ratio; best = sys; }
        }
        return best;
    }

    /// The player's ships (not stations) in a stable order — build order, which is unit id. What Tab
    /// cycles through.
    public static List<Unit> PlayerShips(bool includeStations = false)
    {
        var list = new List<Unit>();
        var um = UnitManager.Instance;
        if (um == null) return list;
        foreach (var u in um.Units)
        {
            if (u == null || u.IsDestroyed || u.owner != FactionManager.Player) continue;
            if (!includeStations && u.Info.isStation) continue;
            list.Add(u);
        }
        list.Sort((a, b) => a.id.CompareTo(b.id));
        return list;
    }
}
