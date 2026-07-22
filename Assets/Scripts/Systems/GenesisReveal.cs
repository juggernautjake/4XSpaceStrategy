using UnityEngine;

// ============================================================================================
// THE GALAXY ARRIVES
//
// The whole galaxy is generated up front, and almost all of it starts CONCEALED. At the moment the
// loading panel dissolves, the only things that exist as far as the eye is concerned are the home
// star(s), the homeworld and its moons. Then the orbit lines draw in, and then the rest of the galaxy
// appears — which is the beat that says the world is finished and the player has it.
//
// WHY THIS IS WORTH DOING AT ALL, given the loading panel covers the screen anyway. Two reasons, and
// the second is the real one:
//
//   1. The handoff. The panel dissolves onto the real camera at planetary zoom, and at that zoom the
//      render ladder is in SYSTEM mode — every other system in the galaxy is drawn as real geometry,
//      small but present, scattered across the background. Concealing them means the planet arrives
//      into empty space, which is what the sequence has spent thirty seconds promising.
//   2. The reveal has to have something to reveal. "All of the other things become visible" is a beat
//      the sequence was performing without a subject: the rings faded in, and everything else had been
//      sitting there in full view the whole time. Hiding it first is what turns the last beat from a
//      fade into an arrival.
//
// CONCEALED, NOT ABSENT — the same guarantee everything else in Visibility.cs makes. Every hidden
// system keeps orbiting, its economy keeps ticking and its faction AI keeps running from the moment
// generation finishes. Nothing about the sequence pauses the galaxy; it only declines to draw it.
//
// Hidden as `HideReason.Sequence`, and revealed by that reason alone. That is what keeps the rare
// world that generated `Undiscovered` hidden through the arrival instead of being handed to the player
// on turn one along with everything else.
// ============================================================================================
public static class GenesisReveal
{
    /// True between Begin() and Finish() — the galaxy is generated and running, and being withheld.
    public static bool Running { get; private set; }

    /// Conceal the galaxy, leaving only the home system's sun(s) lit.
    ///
    /// Called immediately after the galaxy is visualized, while the loading panel still owns the
    /// screen. Safe to call on a galaxy that already has concealments of its own: this only ever writes
    /// `Sequence`, and Finish() only ever clears `Sequence`, so a world hidden for any other reason
    /// passes through untouched.
    public static void Begin()
    {
        var g = SystemContext.Galaxy;
        if (g == null) return;

        // Everything, including the galactic core.
        VisibilityService.HideAll(HideReason.Sequence);

        var home = g.Home;
        if (home == null) { Running = true; return; }

        // ...then give the home system back, and immediately re-hide its WORLDS individually. The
        // system-level flag is the wrong tool for "show the suns but not the planets" — it is all or
        // nothing by design — so the home system comes out of the blanket conceal and its bodies carry
        // their own.
        // Only if the blanket conceal above is what put it there — nothing else sets a system-level
        // reason today, but if something ever does, the sequence has no business dropping it.
        if (home.hideReason == HideReason.Sequence) home.hideReason = HideReason.None;

        // Conditional for the same reason HideAll is: a home body already hidden for another reason
        // keeps that reason, so Finish() cannot reveal it.
        foreach (var b in home.AllBodies())
        {
            if (b.hideReason == HideReason.None) b.hideReason = HideReason.Sequence;
            if (b.ringHideReason == HideReason.None) b.ringHideReason = HideReason.Sequence;
        }
        VisibilityService.ApplySystem(home);

        Running = true;
    }

    /// The homeworld (and its moons) join the star. Called once the real world exists and the sequence
    /// has moved on to showing it — so that when the panel dissolves, the planet the player has been
    /// watching form is genuinely there underneath.
    public static void RevealHomeworld(CelestialBody home)
    {
        if (!Running || home == null) return;

        Clear(home);
        foreach (var m in home.moons) Clear(m);
        VisibilityService.Apply(home);
        foreach (var m in home.moons) VisibilityService.Apply(m);
    }

    /// Beat 9: the rest of the galaxy arrives. Gives back exactly what Begin() took and nothing else.
    ///
    /// Idempotent, and deliberately so — it is called from the loading finale AND as a backstop once
    /// generation returns, because a galaxy left invisible is the single worst way this could fail and
    /// it must not depend on one coroutine reaching its last line.
    public static void Finish()
    {
        if (!Running) return;
        Running = false;
        VisibilityService.RevealAll(HideReason.Sequence);
    }

    // Only clears the sequence's OWN concealment. A home moon could in principle carry another reason
    // (nothing sets one today, but a cloak on a friendly world is exactly the mechanic this enum exists
    // for) and the sequence has no business dropping it.
    static void Clear(CelestialBody b)
    {
        if (b == null) return;
        if (b.hideReason == HideReason.Sequence) b.hideReason = HideReason.None;
        if (b.ringHideReason == HideReason.Sequence) b.ringHideReason = HideReason.None;
    }
}
