using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// CONCEALMENT: WHY AN OBJECT IS NOT ON SCREEN
//
// Hiding is NOT a developer convenience with a boolean. It is a game mechanic that happens to have a
// developer tool sitting on top of it, and the three ways an object can be off screen are genuinely
// different facts about the world even though they look identical today:
//
//   Dev          — somebody tucked it away from the object panel. No in-fiction meaning at all.
//   Cloaked      — concealed by technology. Late-game tech, and it applies to ships as well as worlds.
//   Undiscovered — it is out there and nobody has found it yet. Generation produces a few of these.
//
// They all render the same (nothing is drawn), and that is deliberate: the reason costs nothing to
// carry now, and when a cloaking tech or a discovery event arrives it is a flag change rather than a
// rewrite. What it must NOT become is one bool, because then "reveal everything the player has found"
// and "drop every cloak" and "un-hide what I hid in the editor" are the same operation, and they are
// not.
//
// CONCEALED IS NOT ABSENT. A concealed body keeps orbiting, keeps being ticked by the colony and
// faction code, keeps its units and its build queue. Only its renderers, colliders and lights go. That
// is the whole reason this does not use GameObject.SetActive: a deactivated body stops running, and a
// cloaked planet that stopped orbiting would be visible by the hole it left in the system.
// ============================================================================================
public enum HideReason
{
    None = 0,
    Dev = 1,
    Cloaked = 2,
    Undiscovered = 3,

    // The fourth reason, and it earns its place rather than being a convenience.
    //
    // The genesis sequence hides the whole galaxy while it is being born and gives it back at the end
    // (see GenesisReveal). "Not arrived yet" is genuinely none of the three above, and the distinction is
    // load-bearing rather than cosmetic: the sequence's final reveal must give back exactly what IT hid
    // and nothing else. Folded into Dev, that reveal would also un-hide anything a developer had tucked
    // away; folded into Undiscovered, it would hand the player the rare hidden world on turn one and
    // there would be nothing left to find.
    Sequence = 4
}

public static class HideReasons
{
    public static string Label(HideReason r) => r switch
    {
        HideReason.Dev => "Hidden",
        HideReason.Cloaked => "Cloaked",
        HideReason.Undiscovered => "Undiscovered",
        HideReason.Sequence => "Not yet arrived",
        _ => "Visible"
    };

    /// Colour for the panel, so the three reasons are told apart at a glance rather than by reading.
    public static Color Tint(HideReason r) => r switch
    {
        HideReason.Dev => new Color(1f, 0.75f, 0.30f),
        HideReason.Cloaked => new Color(0.55f, 0.80f, 1f),
        HideReason.Undiscovered => new Color(0.75f, 0.60f, 1f),
        HideReason.Sequence => new Color(0.55f, 0.62f, 0.70f),
        _ => UITheme.Text
    };
}

// The thing that actually stops an object being drawn, and — just as importantly — puts it back
// EXACTLY as it was.
//
// It records the components it switched off rather than switching everything on again at reveal time.
// Half of this galaxy's renderers are legitimately disabled by somebody else: a ring the player turned
// off, a fog silhouette mid-swap, an atmosphere shell an airless world never got. "Enable everything in
// the subtree" would quietly turn all of those on the first time a body was concealed and revealed.
[DisallowMultipleComponent]
public class ConcealBinding : MonoBehaviour
{
    bool concealed;
    readonly List<Renderer> rends = new List<Renderer>();
    readonly List<Collider> cols = new List<Collider>();
    readonly List<Light> lights = new List<Light>();

    public bool Concealed => concealed;

    /// Conceal or reveal a whole object. Safe to call every frame and safe to call on a null object —
    /// a body whose visual has not been built yet is simply not concealed yet, and ApplyAll will catch
    /// it once the visualizer has run.
    public static void Set(GameObject go, bool concealed)
    {
        if (go == null) return;
        var b = go.GetComponent<ConcealBinding>();
        if (b == null)
        {
            // Nothing to restore and nothing to hide: don't litter every visible object in the galaxy
            // with a component it will never use.
            if (!concealed) return;
            b = go.AddComponent<ConcealBinding>();
        }
        b.Apply(concealed);
    }

    void Apply(bool on)
    {
        if (on == concealed)
        {
            // Already concealed, but the subtree may have GROWN since — PlanetAppearance rebuilds a
            // body's atmosphere shell when it is surveyed, BodyFog adds and removes its silhouette, and
            // either would arrive enabled on top of a concealed world. Re-sweeping is what keeps
            // "hidden" true rather than true-at-the-moment-it-was-set.
            if (on) Sweep();
            return;
        }

        concealed = on;
        if (on) { rends.Clear(); cols.Clear(); lights.Clear(); Sweep(); }
        else Restore();
    }

    // Disables everything currently enabled in the subtree and REMEMBERS it. Appends rather than
    // replaces, because what was captured on the first pass is disabled by now and would not be found
    // again — re-capturing from scratch would drop it from the restore list and strand it hidden.
    void Sweep()
    {
        var r = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < r.Length; i++)
            if (r[i] != null && r[i].enabled) { r[i].enabled = false; rends.Add(r[i]); }

        var c = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < c.Length; i++)
            if (c[i] != null && c[i].enabled) { c[i].enabled = false; cols.Add(c[i]); }

        // Lights too, or hiding a star leaves its system lit by a sun that is not there.
        var l = GetComponentsInChildren<Light>(true);
        for (int i = 0; i < l.Length; i++)
            if (l[i] != null && l[i].enabled) { l[i].enabled = false; lights.Add(l[i]); }
    }

    void Restore()
    {
        for (int i = 0; i < rends.Count; i++) if (rends[i] != null) rends[i].enabled = true;
        for (int i = 0; i < cols.Count; i++) if (cols[i] != null) cols[i].enabled = true;
        for (int i = 0; i < lights.Count; i++) if (lights[i] != null) lights[i].enabled = true;
        rends.Clear(); cols.Clear(); lights.Clear();
    }

    // The render ladder switches SystemParent off wholesale at galaxy zoom (GalaxyLOD.ApplyDetail) and
    // back on when you zoom in. Component enable state survives that, so this is belt and braces — but
    // it is also the cheapest possible place to re-assert concealment after anything reparents or
    // reactivates a body, and it costs one branch on an object that is not concealed.
    void OnEnable() { if (concealed) Sweep(); }
}

/// One concealed thing, for the panel and for any gameplay system that wants to ask what is hidden.
public struct HiddenBody
{
    public string label;     // human name, e.g. "Kepler III"
    public string kind;      // "Planet" | "Moon" | "Star" | "Orbit line" | "System"
    public HideReason reason;
    public CelestialBody body;      // null unless this is a body or its orbit line
    public StarData star;           // null unless this is a star
    public StarSystemData system;   // the system it belongs to (or IS, for a system entry)
}

// ============================================================================================
// THE ONE PLACE CONCEALMENT IS DECIDED
//
// Gameplay drives visibility through this and nothing else. A cloaking tech calls Hide(body, Cloaked);
// an exploration event calls Reveal(body); the loading sequence calls HideSystem/RevealAll. None of
// them touch a Renderer, and none of them need to know that a body's orbit ring lives on a different
// GameObject than the body does.
//
// If this game ever goes multiplayer, this is also the chokepoint that has to become server-authoritative:
// a cloaked object's position must never be sent to a client that merely declines to draw it. Keeping the
// decision in one place now is what makes that a change to one file later.
// ============================================================================================
public static class VisibilityService
{
    /// Fired whenever anything's concealment changes, so the object panel can refresh itself.
    public static event Action OnChanged;

    // ---- Queries -------------------------------------------------------------------------------

    /// Is this body concealed, for any reason — including its whole system being concealed?
    public static bool IsHidden(CelestialBody b) => ReasonFor(b) != HideReason.None;

    /// The EFFECTIVE reason a body is not drawn. A system-wide conceal outranks the body's own flag,
    /// because that is the one the player has to undo first for anything to reappear.
    public static HideReason ReasonFor(CelestialBody b)
    {
        if (b == null) return HideReason.None;
        if (b.system != null && b.system.hideReason != HideReason.None) return b.system.hideReason;

        // A MOON GOES WITH ITS PLANET.
        //
        // Moons are built as their own visuals (SystemVisualizer builds them in a separate pass) and
        // their orbit rings are re-centred on the planet's transform every frame — so hiding a planet
        // and leaving its moons alone gives you one to three worlds circling a hole in space, each
        // inside a visible ring. That is not concealment, it is an arrow pointing at what was hidden.
        // It is also the exact case generation produces: SeedUndiscoveredWorlds picks from sys.bodies,
        // and a planet there routinely carries moons.
        if (b.parentBody != null)
        {
            var p = ReasonFor(b.parentBody);
            if (p != HideReason.None) return p;
        }

        return b.hideReason;
    }

    public static HideReason ReasonFor(StarData s, StarSystemData sys)
    {
        if (s == null) return HideReason.None;
        if (sys != null && sys.hideReason != HideReason.None) return sys.hideReason;
        return s.hideReason;
    }

    /// The effective reason a body's ORBIT LINE is not drawn. Hiding the body hides its line too — an
    /// orbit ring drawn around nothing is not concealment, it is a label saying "something is here" —
    /// but the line can also be concealed on its own while the world stays visible.
    public static HideReason ReasonForOrbitLine(CelestialBody b)
    {
        if (b == null) return HideReason.None;
        var own = ReasonFor(b);
        return own != HideReason.None ? own : b.ringHideReason;
    }

    // ---- Bodies --------------------------------------------------------------------------------

    public static void Hide(CelestialBody b, HideReason reason = HideReason.Dev)
    {
        if (b == null || reason == HideReason.None) return;
        b.hideReason = reason;
        Apply(b);
        Changed();
    }

    public static void Reveal(CelestialBody b)
    {
        if (b == null) return;
        b.hideReason = HideReason.None;
        Apply(b);
        Changed();
    }

    // ---- Orbit lines ---------------------------------------------------------------------------

    public static void HideOrbitLine(CelestialBody b, HideReason reason = HideReason.Dev)
    {
        if (b == null || reason == HideReason.None) return;
        b.ringHideReason = reason;
        Apply(b);
        Changed();
    }

    public static void RevealOrbitLine(CelestialBody b)
    {
        if (b == null) return;
        b.ringHideReason = HideReason.None;
        Apply(b);
        Changed();
    }

    // ---- Stars (one sun of a cluster, or a black hole) ------------------------------------------

    public static void Hide(StarData s, HideReason reason = HideReason.Dev)
    {
        if (s == null || reason == HideReason.None) return;
        s.hideReason = reason;
        Apply(s, SystemOf(s));
        Changed();
    }

    public static void Reveal(StarData s)
    {
        if (s == null) return;
        s.hideReason = HideReason.None;
        Apply(s, SystemOf(s));
        Changed();
    }

    // ---- Ships ---------------------------------------------------------------------------------
    //
    // The cloaking tech's target. A concealed ship keeps flying, keeps its orders and keeps being ticked
    // — only its model (or its billboard token, depending which renderer owns it) stops being drawn and
    // stops answering the cursor.

    /// A ship is concealed by its own cloak — OR by being parked at a world that is concealed.
    ///
    /// The second half matters as much as the first: hiding a system and leaving the fleet sitting in it
    /// drawn is the same leak the orbit rings and derelict hulls had, and it is a bigger giveaway than
    /// either, because a cluster of ships in empty space is unmistakable.
    public static bool IsHidden(Unit u)
    {
        if (u == null) return false;
        if (u.hideReason != HideReason.None) return true;
        return u.location != null && IsHidden(u.location);
    }

    public static void Hide(Unit u, HideReason reason = HideReason.Cloaked)
    {
        if (u == null || reason == HideReason.None) return;
        u.hideReason = reason;
        Apply(u);
        Changed();
    }

    public static void Reveal(Unit u)
    {
        if (u == null) return;
        u.hideReason = HideReason.None;
        Apply(u);
        Changed();
    }

    /// Push a ship's concealment at whatever is currently drawing it. Called by both unit renderers
    /// whenever they (re)build a visual, because a fresh GameObject knows nothing about the cloak.
    public static void Apply(Unit u)
    {
        if (u == null) return;
        // UnitVisuals is the authority on which of the two renderers owns this ship — asking it rather
        // than either renderer directly is what keeps this correct when a unit graduates from a token to
        // a real mesh.
        var t = UnitVisuals.TransformOf(u);
        if (t != null) ConcealBinding.Set(t.gameObject, IsHidden(u));
    }

    // ---- Whole systems -------------------------------------------------------------------------

    /// Conceal an entire solar system in one action: its sun(s), every planet and moon, every orbit
    /// line, and the enlarged star that stands in for it at galaxy zoom. One flag on the system rather
    /// than a flag written onto each of its parts, so revealing it gives every object back exactly the
    /// concealment it had of its own.
    public static void HideSystem(StarSystemData sys, HideReason reason = HideReason.Dev)
    {
        if (sys == null || reason == HideReason.None) return;
        sys.hideReason = reason;
        ApplySystem(sys);
        Changed();
    }

    public static void RevealSystem(StarSystemData sys)
    {
        if (sys == null) return;
        sys.hideReason = HideReason.None;
        ApplySystem(sys);
        Changed();
    }

    // ---- Bulk ----------------------------------------------------------------------------------

    /// Everything in the galaxy, concealed. What the genesis sequence wants at t=0, before it reveals
    /// the home star.
    public static void HideAll(HideReason reason = HideReason.Dev)
    {
        var g = SystemContext.Galaxy;
        if (g == null || reason == HideReason.None) return;

        // Only what is currently VISIBLE. Overwriting an existing concealment would mean a later
        // RevealAll(thisReason) also revealed whatever was hidden for another one — a cloaked system
        // uncloaked by the end of a loading screen. GenesisReveal's guarantee that it gives back exactly
        // what it took depends on this.
        foreach (var sys in g.systems)
            if (sys.hideReason == HideReason.None) sys.hideReason = reason;
        if (g.center != null && g.center.hideReason == HideReason.None) g.center.hideReason = reason;

        ApplyAll();
        Changed();
    }

    /// Drop every concealment of every kind, everywhere. The object panel's escape hatch.
    public static void RevealAll() => RevealAll(HideReason.None);

    /// Reveal only what was hidden for ONE reason, leaving every other concealment alone.
    /// `HideReason.None` means "everything, whatever the reason".
    ///
    /// This is what makes the reason worth carrying. The genesis sequence hides the galaxy as
    /// `Sequence` and gives it back with `RevealAll(Sequence)` — so the rare world that generated
    /// `Undiscovered` stays hidden through it, and so would anything a developer had tucked away. A
    /// blanket reveal would hand the player the one thing in the galaxy there was to find.
    public static void RevealAll(HideReason only)
    {
        var g = SystemContext.Galaxy;
        if (g == null) return;

        bool Match(HideReason r) => r != HideReason.None && (only == HideReason.None || r == only);

        foreach (var sys in g.systems)
        {
            if (Match(sys.hideReason)) sys.hideReason = HideReason.None;
            foreach (var s in sys.stars) if (s != null && Match(s.hideReason)) s.hideReason = HideReason.None;
            // A single-star system's combinedStar IS stars[0], so this is usually the same object twice —
            // harmless, and it is the only thing that covers a black hole and a synthesized cluster star.
            if (sys.combinedStar != null && Match(sys.combinedStar.hideReason))
                sys.combinedStar.hideReason = HideReason.None;
            foreach (var b in sys.AllBodies())
            {
                if (Match(b.hideReason)) b.hideReason = HideReason.None;
                if (Match(b.ringHideReason)) b.ringHideReason = HideReason.None;
            }
        }
        if (g.center != null && Match(g.center.hideReason)) g.center.hideReason = HideReason.None;

        // Ships too. "Drop every concealment, everywhere" has to mean the fleet as well, or a cloaked
        // ship has no way back from the panel — and the panel is the only escape hatch there is.
        var um = UnitManager.Instance;
        if (um != null)
            foreach (var u in um.Units)
                if (u != null && Match(u.hideReason)) { u.hideReason = HideReason.None; Apply(u); }

        ApplyAll();
        Changed();
    }

    /// Everything currently concealed, for a gameplay system that wants to enumerate it (and for the
    /// panel's count). Ordered system by system, the way the panel lists them.
    public static List<HiddenBody> ListHidden()
    {
        var list = new List<HiddenBody>();
        var g = SystemContext.Galaxy;
        if (g == null) return list;

        foreach (var sys in g.systems)
        {
            if (sys.hideReason != HideReason.None)
            {
                list.Add(new HiddenBody { label = sys.name, kind = "System", reason = sys.hideReason, system = sys });
                // A concealed system's contents are concealed BY it; listing every world inside it again
                // would bury the one entry that actually explains why they are gone.
                continue;
            }

            foreach (var s in sys.stars)
                if (s != null && s.hideReason != HideReason.None)
                    list.Add(new HiddenBody { label = s.name, kind = "Star", reason = s.hideReason, star = s, system = sys });

            // A black hole renders from combinedStar — but in a black-hole system combinedStar IS
            // stars[0], and so is the single sun of an ordinary one-star system. Without the Contains
            // guard the loop above and this line report the same object twice, and the panel's "N
            // hidden" count runs one high for every hidden black hole.
            if (sys.combinedStar != null && sys.combinedStar.hideReason != HideReason.None
                && !sys.stars.Contains(sys.combinedStar))
                list.Add(new HiddenBody { label = sys.combinedStar.name, kind = "Star", reason = sys.combinedStar.hideReason, star = sys.combinedStar, system = sys });

            foreach (var b in sys.AllBodies())
            {
                // A moon concealed BY its planet is not its own entry, for the same reason a world
                // inside a concealed system is not: the row that explains it is the one above.
                if (b.parentBody != null && ReasonFor(b.parentBody) != HideReason.None) continue;

                if (b.hideReason != HideReason.None)
                    list.Add(new HiddenBody
                    {
                        label = b.name,
                        kind = b.parentBody != null ? "Moon" : "Planet",
                        reason = b.hideReason, body = b, system = sys
                    });
                else if (b.ringHideReason != HideReason.None)
                    list.Add(new HiddenBody
                    {
                        label = b.name + " orbit", kind = "Orbit line",
                        reason = b.ringHideReason, body = b, system = sys
                    });
            }
        }

        if (g.center != null && g.center.hideReason != HideReason.None)
            list.Add(new HiddenBody { label = g.center.name, kind = "Star", reason = g.center.hideReason, star = g.center });

        return list;
    }

    // ---- Pushing state at the visuals ----------------------------------------------------------

    /// Re-assert every concealment in the galaxy against the visuals as they stand now. Called after
    /// the galaxy is (re)visualized, because a rebuild hands every body a brand new GameObject that
    /// knows nothing about what was hidden.
    public static void ApplyAll()
    {
        var g = SystemContext.Galaxy;
        if (g == null) return;
        foreach (var sys in g.systems) ApplySystem(sys);
        if (g.center != null) ConcealBinding.Set(g.center.visualObject, g.center.hideReason != HideReason.None);
        ApplyUnits();
    }

    /// Re-check the whole fleet.
    ///
    /// A ship inherits the concealment of the world it is parked at (IsHidden(Unit)), so hiding a system
    /// changes what should be drawn for ships that did not themselves change — and nothing else would
    /// ever tell them. Cheap enough to run on every concealment change (see Changed) rather than trying
    /// to work out which ships a given change could possibly have affected.
    public static void ApplyUnits()
    {
        var um = UnitManager.Instance;
        if (um == null) return;
        foreach (var u in um.Units) Apply(u);
    }

    public static void ApplySystem(StarSystemData sys)
    {
        if (sys == null) return;
        foreach (var s in sys.stars) Apply(s, sys);
        // A black-hole system renders from combinedStar rather than from the stars list, so it is not
        // covered by the loop above.
        if (sys.isBlackHole) Apply(sys.combinedStar, sys);
        foreach (var b in sys.AllBodies()) Apply(b);
    }

    public static void Apply(CelestialBody b)
    {
        if (b == null || b.visualObject == null) return;
        ConcealBinding.Set(b.visualObject, ReasonFor(b) != HideReason.None);

        // The orbit ring is NOT a child of the body — it hangs off the unscaled system container so it
        // doesn't inherit the body's scale (see OrbitController) — so the subtree sweep above never
        // reaches it. It has to be concealed through its controller.
        var oc = b.visualObject.GetComponent<OrbitController>();
        if (oc != null)
            oc.SetConcealed(ReasonFor(b) != HideReason.None,
                            ReasonForOrbitLine(b) != HideReason.None);
    }

    public static void Apply(StarData s, StarSystemData sys)
    {
        if (s == null) return;
        ConcealBinding.Set(s.visualObject, ReasonFor(s, sys) != HideReason.None);
    }

    // Which system a star belongs to. Stars carry no back-reference (unlike bodies, which have
    // CelestialBody.system), and adding one would mean threading it through every generation path — so
    // for the handful of calls that need it, a scan of at most twelve systems is the cheaper answer.
    static StarSystemData SystemOf(StarData s)
    {
        var g = SystemContext.Galaxy;
        if (g == null || s == null) return null;
        foreach (var sys in g.systems)
        {
            if (sys.combinedStar == s) return sys;
            foreach (var member in sys.stars) if (member == s) return sys;
        }
        return null;
    }

    // Every public mutator ends here, which makes it the one place the fleet can be re-checked without
    // each of them having to remember to.
    static void Changed()
    {
        ApplyUnits();
        OnChanged?.Invoke();
    }
}
