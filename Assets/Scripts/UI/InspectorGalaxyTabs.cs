using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// THE GALACTIC CORE IS NOT A STAR SYSTEM
//
// Clicking the core used to open the STAR tabs with no system attached — so the Worlds tab iterated a
// system that wasn't there and came back empty, and Zone hid itself because a black hole has no
// habitable band. The result was a panel that looked like a broken system view rather than like the
// thing the player had actually clicked: the object the entire galaxy turns around.
//
// The core is the one place in the game where "what am I looking at" is answered at GALAXY scale, so
// that is what it reports — the galaxy's name, how big it is, what is in it, and who holds what.
//
//   Overview — the galaxy and the core itself
//   Systems  — every system in it, drillable
//
// A rare black-hole SYSTEM is deliberately NOT routed here. Those are real systems with real planets
// orbiting them, and they keep the star tabs; only the galactic centre is galaxy-scale. See
// StarInteraction.isGalacticCore, which is set explicitly rather than inferred from a null system —
// inferring it would eventually catch a black-hole system whose data happened to be missing.
// ============================================================================================
public partial class InspectorWindow
{
    void CollectGalaxyTabs()
    {
        tabs.Add(new InspectorTab("Overview", BuildGalaxyOverview));
        tabs.Add(new InspectorTab("Systems", BuildGalaxySystems));
    }

    Galaxy TargetGalaxy => target.galaxy ?? SystemContext.Galaxy;

    void BuildGalaxyOverview(Transform p)
    {
        var g = TargetGalaxy;
        if (g == null) { Note(p, "No galaxy."); return; }

        // ---- The galaxy ----
        Header(p, "GALAXY");
        var about = Card(p);
        Stat(about, "Name", () => g.name);

        // Shown ONLY when it differs. Generation names the core "<Galaxy> Core", so for most galaxies
        // this line would just say the galaxy's name again with a word on the end — and a readout that
        // repeats itself teaches the player to stop reading it.
        var core = target.star ?? g.center;
        // Compared against the name generation actually gives it ("<Galaxy> Core"), not against the bare
        // galaxy name — which it can never equal, so the suppression never fired and the row always
        // rendered the very repetition it exists to avoid.
        if (core != null && !string.IsNullOrEmpty(core.name) && core.name != $"{g.name} Core")
            Stat(about, "Core", () => core.name);

        Stat(about, "Class", () => "Supermassive black hole");
        Stat(about, "Systems", () => g.systems != null ? g.systems.Count.ToString() : "0");
        Stat(about, "Radius", () => $"{CameraController.GalaxyRadius():F0} units");

        // ---- What is in it ----
        Header(p, "CONTENTS");
        var contents = Card(p);
        Stat(contents, "Worlds", () => CountWorlds(g).ToString());
        Stat(contents, "Moons", () => CountMoons(g).ToString());
        Stat(contents, "Habitable for you", () => CountHabitable(g).ToString());
        Stat(contents, "Black-hole systems", () => CountBlackHoles(g).ToString());
        Stat(contents, "Derelicts adrift", () => g.derelicts != null ? g.derelicts.Count.ToString() : "0");

        // ---- Who holds it ----
        Header(p, "HOLDINGS");
        var holdings = Card(p);
        Stat(holdings, "Yours", () => CountSystems(g, FactionManager.Player).ToString());
        Stat(holdings, "Rival empires", () => CountRivalSystems(g).ToString());
        Stat(holdings, "Unclaimed", () => CountSystems(g, null).ToString());

        // ---- The core itself ----
        if (core != null)
        {
            Header(p, "THE CORE");
            var c = Card(p);
            Stat(c, "Mass", () => $"{core.mass:F1} solar");
            Note(p, "Everything in the galaxy turns around this. Nothing orbits it closely enough to " +
                    "survive doing so — the systems you can reach all sit far outside its reach.");
        }
    }

    void BuildGalaxySystems(Transform p)
    {
        var g = TargetGalaxy;
        if (g == null || g.systems == null || g.systems.Count == 0) { Note(p, "No systems."); return; }

        Header(p, $"{g.systems.Count} SYSTEMS");
        Note(p, "Ordered by distance from the core. Click one to open it.");

        // Sorted by distance out from the centre, which is the one ordering that means something here —
        // the golden-angle layout puts index order and spatial order at odds, so listing them as
        // generated would read as arbitrary.
        // Nulls filtered on the way IN, not skipped on the way out — the comparator dereferences every
        // element, so a null would throw during the sort, before any guard downstream could catch it.
        var ordered = new List<StarSystemData>(g.systems.Count);
        foreach (var s0 in g.systems) if (s0 != null) ordered.Add(s0);
        ordered.Sort((a, b) =>
            (a.galaxyPosition - g.centerPosition).sqrMagnitude
            .CompareTo((b.galaxyPosition - g.centerPosition).sqrMagnitude));

        foreach (var sys in ordered)
        {
            var s = sys;   // captured per row rather than by the loop variable

            string label = s.isHome ? $"{s.name}  <color=#4DFF6E>(home)</color>" : s.name;

            DrillRow(p, label, InspectorTarget.Of(s.combinedStar, s), () =>
            {
                float d = (s.galaxyPosition - g.centerPosition).magnitude;
                int worlds = s.bodies != null ? s.bodies.Count : 0;
                return $"{StarDatabase.SystemClass(s.combinedStar)} · {worlds} worlds · " +
                       $"{FactionManager.OwnerLabel(s.owner)} · {d:F0} out";
            });
        }
    }

    // ---- Counting. All derived on demand: a galaxy is at most twelve systems, and a stored total is
    // one more thing that can disagree with the thing it counts. ----

    static int CountWorlds(Galaxy g)
    {
        int n = 0;
        foreach (var sys in g.systems) n += sys.bodies != null ? sys.bodies.Count : 0;
        return n;
    }

    static int CountMoons(Galaxy g)
    {
        int n = 0;
        foreach (var sys in g.systems)
            foreach (var b in sys.bodies)
                n += b.moons != null ? b.moons.Count : 0;
        return n;
    }

    // For the CURRENT species — the same question the habitable-zone overlay answers, so the two agree.
    static int CountHabitable(Galaxy g)
    {
        int n = 0;
        foreach (var sys in g.systems)
            foreach (var b in sys.AllBodies())
                if (b.isHabitable) n++;
        return n;
    }

    static int CountBlackHoles(Galaxy g)
    {
        int n = 0;
        foreach (var sys in g.systems) if (sys.isBlackHole) n++;
        return n;
    }

    static int CountSystems(Galaxy g, Faction owner)
    {
        int n = 0;
        foreach (var sys in g.systems) if (sys.owner == owner) n++;
        return n;
    }

    static int CountRivalSystems(Galaxy g)
    {
        int n = 0;
        foreach (var sys in g.systems)
            if (sys.owner != null && sys.owner != FactionManager.Player) n++;
        return n;
    }
}
