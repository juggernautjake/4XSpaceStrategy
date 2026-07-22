using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// DELETION, WITH A WAY BACK
//
// Deleting a world or a whole solar system genuinely REMOVES it: out of galaxy.systems, out of
// sys.bodies, out of body.moons. Nothing iterates it, nothing ticks it, nothing draws it, and the save
// file no longer carries it. That is what makes it deletion rather than a permanent hide, which is a
// different feature and already exists (see Visibility.cs).
//
// WHY A BIN RATHER THAN A CONFIRMATION DIALOG. This is a tool for taking a galaxy apart to look at it,
// which means dozens of deletes in a session — and a modal "are you sure?" on every one of them makes
// the tool unusable while protecting nothing, because the mistake you actually make is deleting the
// wrong row, not clicking the button by accident. Restore is the answer to that; a dialog is not.
//
// A restored system comes back complete: its suns, its worlds, their moons, their orbits, its derelicts,
// and its place in the list. Everything is kept alive in the entry rather than reconstructed, so what
// comes back is the same objects with the same terrain, colonies and ore — not a regenerated lookalike.
// ============================================================================================
public static class GalaxyTrash
{
    /// One deleted thing, holding everything needed to put it back exactly where it was.
    public class Entry
    {
        public string label;
        public string kind;                 // "System" | "Planet" | "Moon" | "Star"

        public StarSystemData system;       // the deleted system, OR the system a deleted body/star came from
        public int systemIndex = -1;        // where in galaxy.systems it sat
        public bool wasHome;

        public CelestialBody body;          // the deleted world (planet or moon)
        public CelestialBody parent;        // its planet, if it was a moon
        public int bodyIndex = -1;          // where in bodies/moons it sat

        public StarData star;               // the deleted sun
        public int starIndex = -1;

        // Derelicts that lived in a deleted system. They index systems by POSITION, so they cannot
        // simply be left behind — see FixDerelicts.
        public List<Derelict> derelicts;

        // Ships that were sitting on a deleted world, and which world each was on, so Restore can put
        // them back where they were rather than leaving them adrift. See Evict.
        public List<Berth> crew;

        public string Describe() => $"{kind} · {label}";
    }

    /// One ship and the world it was parked on before that world was deleted.
    public struct Berth
    {
        public Unit unit;
        public CelestialBody body;
    }

    static readonly List<Entry> bin = new List<Entry>();

    /// Most recently deleted first — the order you want to undo in.
    public static IReadOnlyList<Entry> Items => bin;

    public static event Action OnChanged;

    // ---- Deleting ------------------------------------------------------------------------------

    /// A whole solar system: its star(s), its planets, their moons, every orbit line, and its marker at
    /// galaxy zoom. One action, as asked for.
    public static bool DeleteSystem(StarSystemData sys, out string why)
    {
        why = null;
        var g = SystemContext.Galaxy;
        if (g == null || sys == null) { why = "no galaxy"; return false; }

        int i = g.systems.IndexOf(sys);
        if (i < 0) { why = "not in this galaxy"; return false; }
        if (g.systems.Count <= 1) { why = "the galaxy's last system"; return false; }

        var entry = new Entry
        {
            kind = "System", label = sys.name,
            // From the INDEX, not from sys.isHome. The two are kept in step (MarkHome), but homeIndex is
            // the one Galaxy.Home actually reads, so it is the one to trust when restoring.
            system = sys, systemIndex = i, wasHome = (i == g.homeIndex),
            derelicts = new List<Derelict>()
        };

        // Take the derelicts that belonged to it out with it, and keep them on the entry. Left in the
        // galaxy they would point at whatever system slid into this index.
        if (g.derelicts != null)
            for (int d = g.derelicts.Count - 1; d >= 0; d--)
                if (g.derelicts[d].systemIndex == i)
                {
                    entry.derelicts.Add(g.derelicts[d]);
                    g.derelicts.RemoveAt(d);
                }

        entry.crew = new List<Berth>();
        foreach (var b in sys.AllBodies()) Evict(b, entry.crew);

        var homeRef = g.Home;
        g.systems.RemoveAt(i);
        FixDerelicts(g, i, -1);
        FixHomeIndex(g, homeRef);
        ForgetVisuals(sys);

        Push(entry);
        Rebuild();
        return true;
    }

    /// One planet or moon. A planet takes its moons with it — a moon orbiting a world that is not there
    /// has nothing to orbit — and they come back with it on restore.
    public static bool DeleteBody(CelestialBody b, out string why)
    {
        why = null;
        if (b == null) { why = "nothing selected"; return false; }

        var sys = b.system;
        if (sys == null) { why = "no system"; return false; }

        var entry = new Entry { body = b, system = sys, label = b.name, crew = new List<Berth>() };

        // REMOVE IT FIRST, EVICT SECOND. The two refusals below leave the galaxy untouched, and moving
        // the fleet before them would turn a rejected delete into a silent side effect: the world stays
        // put and its ships are adrift in space next to it, with nothing in the bin to restore from.
        if (b.parentBody != null)
        {
            entry.kind = "Moon";
            entry.parent = b.parentBody;
            entry.bodyIndex = b.parentBody.moons.IndexOf(b);
            if (entry.bodyIndex < 0) { why = "not attached to its planet"; return false; }
            b.parentBody.moons.RemoveAt(entry.bodyIndex);
        }
        else
        {
            entry.kind = "Planet";
            entry.bodyIndex = sys.bodies.IndexOf(b);
            if (entry.bodyIndex < 0) { why = "not in its system"; return false; }
            sys.bodies.RemoveAt(entry.bodyIndex);
        }

        // Before ForgetVisuals, which nulls the visualObject the park position is read from.
        Evict(b, entry.crew);
        foreach (var m in b.moons) Evict(m, entry.crew);

        ForgetVisuals(b);
        Push(entry);
        Rebuild();
        return true;
    }

    /// One sun out of a cluster.
    ///
    /// Refused when it is the system's last star, deliberately: StarDatabase.Combine on an empty list
    /// hands back a freshly rolled G-type, so a system with its only sun deleted would quietly grow a
    /// NEW one — the opposite of what was asked. Delete the system instead, which is one row up.
    public static bool DeleteStar(StarSystemData sys, StarData s, out string why)
    {
        why = null;
        if (sys == null || s == null) { why = "nothing selected"; return false; }

        int i = sys.stars.IndexOf(s);
        if (i < 0) { why = "not in this system"; return false; }
        if (sys.stars.Count <= 1) { why = "the system's only star — delete the system instead"; return false; }

        var entry = new Entry { kind = "Star", label = s.name, system = sys, star = s, starIndex = i };
        sys.stars.RemoveAt(i);
        Recombine(sys);
        s.visualObject = null;

        Push(entry);
        Rebuild();
        return true;
    }

    // ---- Restoring -----------------------------------------------------------------------------

    public static bool Restore(Entry e, out string why)
    {
        why = null;
        var g = SystemContext.Galaxy;
        if (g == null || e == null || !bin.Contains(e)) { why = "no longer in the bin"; return false; }

        // THE THING IT WENT BACK INTO HAS TO STILL BE THERE.
        //
        // Delete a planet, then delete the system it came out of, then restore the planet: without this
        // it is spliced into a StarSystemData that no galaxy references, the entry leaves the bin, the
        // rebuild draws nothing, and the player is looking at a Restore button that consumed their world
        // and said nothing. Purge the system first and it is gone for good. Restore the container first
        // and everything works — so the refusal says which container.
        if (e.kind != "System")
        {
            if (e.system == null || !g.systems.Contains(e.system))
            {
                why = $"its system ({(e.system != null ? e.system.name : "?")}) isn't in the galaxy — restore that first";
                return false;
            }
            if (e.kind == "Moon" && (e.parent == null || !e.system.bodies.Contains(e.parent)))
            {
                why = $"its planet ({(e.parent != null ? e.parent.name : "?")}) isn't in the galaxy — restore that first";
                return false;
            }
        }

        switch (e.kind)
        {
            case "System":
            {
                var homeRef = g.Home;
                int at = Mathf.Clamp(e.systemIndex, 0, g.systems.Count);
                g.systems.Insert(at, e.system);
                // Everything at or past the insertion point shifted up by one.
                FixDerelicts(g, at, +1);
                if (e.derelicts != null)
                    foreach (var d in e.derelicts) { d.systemIndex = at; g.derelicts.Add(d); }
                // The home system is identified by POSITION, and the position may have moved. Re-find it
                // by reference — unless the thing being restored IS the home, which has no reference to
                // find because it was not in the list a moment ago.
                if (e.wasHome) { g.homeIndex = at; MarkHome(g); }
                else FixHomeIndex(g, homeRef);
                break;
            }

            case "Moon":
                e.parent.moons.Insert(Mathf.Clamp(e.bodyIndex, 0, e.parent.moons.Count), e.body);
                break;

            case "Planet":
                e.system.bodies.Insert(Mathf.Clamp(e.bodyIndex, 0, e.system.bodies.Count), e.body);
                break;

            case "Star":
                e.system.stars.Insert(Mathf.Clamp(e.starIndex, 0, e.system.stars.Count), e.star);
                Recombine(e.system);
                break;

            default:
                why = "unknown kind";
                return false;
        }

        // Ships that were parked here when it was deleted come home. See Evict.
        if (e.crew != null)
        {
            foreach (var c in e.crew)
                if (c.unit != null && c.body != null) { c.unit.location = c.body; c.unit.inSpace = false; }
            e.crew = null;
            UnitManager.Instance?.NotifyUnitsChanged();
        }

        bin.Remove(e);
        Rebuild();
        Changed();
        return true;
    }

    /// Gone for good. Only drops the entry — the objects it held are already out of the galaxy, so this
    /// is the moment they become garbage.
    public static void Purge(Entry e)
    {
        if (e == null) return;
        bin.Remove(e);
        Changed();
    }

    /// Empty the bin.
    public static void PurgeAll()
    {
        if (bin.Count == 0) return;
        bin.Clear();
        Changed();
    }

    /// A new galaxy has replaced the one these came out of, so restoring any of them would splice a
    /// system from a dead galaxy into a live one. Called when a game is generated or loaded.
    public static void OnGameReplaced() => PurgeAll();

    // ---- Plumbing ------------------------------------------------------------------------------

    static void Push(Entry e)
    {
        bin.Insert(0, e);   // newest first
        Changed();
    }

    // A system's combined star drives light, the habitable zone and every orbital speed in it, so it has
    // to be re-derived whenever the cluster's membership changes — not left describing the suns that
    // were there before.
    static void Recombine(StarSystemData sys)
    {
        if (sys == null || sys.stars.Count == 0) return;

        string keepName = sys.combinedStar != null ? sys.combinedStar.name : sys.name;
        var combined = StarDatabase.Combine(sys.stars);

        // DO NOT NAME IT WHEN COMBINE HANDED BACK A REAL SUN.
        //
        // StarDatabase.Combine returns `stars[0]` ITSELF for a one-star list rather than a synthesized
        // copy. So after deleting one sun of the binary "Vega Beta", stamping the old combined name onto
        // the result renames the SURVIVING SUN from "Vega Beta B" to "Vega Beta" — permanently, in the
        // panel, the summary window, the galaxy hover card and every save from then on. Restoring the
        // deleted sun does not undo it, because the rename happened to the object that stayed.
        if (!ReferenceEquals(combined, sys.stars.Count == 1 ? sys.stars[0] : null))
            combined.name = keepName;

        sys.combinedStar = combined;
        foreach (var b in sys.AllBodies()) b.hostStar = combined;

        // RE-TIME THE PLANETS. Orbital speed is a STORED field derived from the primary's mass
        // (OrbitalMechanics.PlanetAngularSpeed), so losing half a binary's mass has to slow every world
        // in the system down — otherwise they keep circling at the two-star rate forever. The Dev star
        // editor already does exactly this after an edit (InspectorStarTabs.RecomputePlanetSpeeds); this
        // is the same recompute for the same reason.
        foreach (var b in sys.bodies)
        {
            if (b == null || b.parentBody != null) continue;
            b.orbitSpeed = OrbitalMechanics.PlanetAngularSpeed(combined, b.orbitRadius);
        }
    }

    // GET THE SHIPS OFF IT BEFORE IT GOES.
    //
    // A unit sitting on a deleted world keeps a non-null `location` pointing out of the galaxy, and that
    // is worse than it sounds — it survives a save. ExportUnitDTOs writes `locationId` and only records a
    // world POSITION when `location` is null, so on the next load the id resolves to nothing, `inSpace`
    // is false, and UnitPos falls through to Vector3.zero: the whole fleet reappears stacked on the
    // galactic core. Parking them in space at the world's last position keeps them somewhere real, and
    // the berth list means Restore can put them back aboard.
    static void Evict(CelestialBody b, List<Berth> into)
    {
        var um = UnitManager.Instance;
        if (b == null || um == null) return;

        Vector3 where = um.WorldPos(b);   // read NOW, while the visual still exists
        bool any = false;
        foreach (var u in um.Units)
        {
            if (u == null || u.location != b) continue;
            into.Add(new Berth { unit = u, body = b });
            u.location = null;
            u.inSpace = true;
            u.parkPosition = where;
            any = true;
        }
        if (any) um.NotifyUnitsChanged();
    }

    // Derelicts hold an INDEX into galaxy.systems rather than a reference, so removing or inserting a
    // system silently re-points every derelict past it at the wrong one.
    static void FixDerelicts(Galaxy g, int at, int delta)
    {
        if (g.derelicts == null) return;
        foreach (var d in g.derelicts)
            if (d.systemIndex >= at) d.systemIndex += delta;
    }

    // Galaxy.Home is systems[homeIndex] — a position, not a reference — so it has to be re-found after
    // the list shifts. If the home system itself was what left, index 0 is the honest fallback: the
    // galaxy still has to answer "where is home" for the camera and the economy.
    static void FixHomeIndex(Galaxy g, StarSystemData homeRef)
    {
        int idx = homeRef != null ? g.systems.IndexOf(homeRef) : -1;
        g.homeIndex = idx >= 0 ? idx : 0;
        MarkHome(g);
    }

    // `isHome` and `homeIndex` are two records of the same fact, and they are BOTH read: the galaxy-zoom
    // proxy draws its ring from `isHome` (GalaxyStarProxy.Build), the comet announcer reads it, and the
    // object panel tags it — while DerelictGen and Galaxy.Home read the index. Delete the home system and
    // the index falls back to 0 while `systems[0].isHome` is still false, so the galaxy silently has no
    // home at all in half the code and a home in the other half. Both are persisted separately, so the
    // divergence would survive a save.
    static void MarkHome(Galaxy g)
    {
        var home = g.Home;
        foreach (var sys in g.systems) sys.isHome = (sys == home);
    }

    // Drop the dangling GameObject references before the rebuild destroys them, so nothing holds a
    // pointer to a destroyed object between now and the end of the frame.
    static void ForgetVisuals(StarSystemData sys)
    {
        sys.pivot = null;
        foreach (var s in sys.stars) if (s != null) s.visualObject = null;
        if (sys.combinedStar != null) sys.combinedStar.visualObject = null;
        foreach (var b in sys.AllBodies()) ForgetVisuals(b);
    }

    static void ForgetVisuals(CelestialBody b)
    {
        if (b == null) return;
        b.visualObject = null;
        foreach (var m in b.moons) ForgetVisuals(m);
    }

    // Redraw from the data. Everything under SystemParent is destroyed and rebuilt from what is still in
    // the galaxy, so a deleted object needs no destruction of its own — it simply is not built again.
    // The same path a save load takes, which is why it is safe.
    static void Rebuild()
    {
        // A window pointing at a world that has just left the galaxy would keep showing it, and its
        // buttons would act on it.
        // EVERYTHING THAT HOLDS A SUBJECT HAS TO BE ASKED WHETHER IT STILL EXISTS.
        //
        // A reference to a deleted world or system stays perfectly valid — that is what makes this class
        // of bug quiet. A window keeps showing it and its buttons keep acting on it; a job list keeps
        // ticking it, charging upkeep for a world that is not in the galaxy. Each of these answers the
        // same question about its own subject and closes or drops itself.
        //
        // PlanetUI first, because closing it fires OnClosed and several of the others listen to that.
        var sel = PlanetUI.Selected;
        if (sel != null && !InGalaxy(sel)) PlanetUI.Instance?.CloseAll();
        SystemSummaryWindow.Instance?.HideIfGone();
        InspectorWindow.Instance?.HideIfGone();
        TerraformWindow.Instance?.HideIfGone();
        TerraformManager.Instance?.DropJobsForMissingBodies();
        ResearchTaskManager.Instance?.DropTasksForMissingBodies();

        GameManager.Instance?.RebuildVisuals();
    }

    static bool InGalaxy(CelestialBody b)
    {
        var g = SystemContext.Galaxy;
        if (g == null || b == null) return false;
        foreach (var sys in g.systems)
            foreach (var other in sys.AllBodies())
                if (other == b) return true;
        return false;
    }

    static void Changed() => OnChanged?.Invoke();
}
