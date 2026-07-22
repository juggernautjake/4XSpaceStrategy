using System.Collections.Generic;
using UnityEngine;

// Dev-Mode "put it back" helpers. The orbit slider and terraforming move a body off the orbit it was
// generated with; these restore it (CelestialBody.naturalOrbitRadius, captured at generation). Dev-Mode
// only — the UI that calls these (OrbitControlPanel's Reset button, the star Inspector's Reset System
// button) is already gated behind Dev Mode.
//
// Deliberately does NOT re-run OrbitSafety.EnforceSystem: resetting the WHOLE system restores the exact
// generated (already collision-free) layout, and resetting a SINGLE planet is a dev's explicit choice that
// shouldn't be second-guessed by shoving its neighbours around — the same "Dev Mode bypasses the
// parameters" freedom the orbit slider itself has.
public static class DevReset
{
    /// Put one body back on the orbit it generated with: radius, solar distance, Kepler speed, the live
    /// visual (planet + ring) and its habitability score. No-op if no natural orbit was ever recorded.
    public static void ResetOrbit(CelestialBody b, StarData star)
    {
        if (b == null || b.naturalOrbitRadius <= 0f) return;
        b.orbitRadius = b.naturalOrbitRadius;

        if (b.parentBody == null)
        {
            // A planet: its orbit radius IS its distance from the star.
            b.distanceFromStar = b.naturalOrbitRadius;
            if (star != null) b.orbitSpeed = OrbitalMechanics.PlanetAngularSpeed(star, b.orbitRadius);
            // Moons ride their planet's solar distance — keep them in step, and rescore them too since
            // their habitability follows that distance (a planet reset from the orbit editor moves them).
            if (b.moons != null)
                foreach (var m in b.moons) if (m != null) { m.distanceFromStar = b.distanceFromStar; Rescore(m, star); }
        }
        else
        {
            // A moon: radius is distance from its planet; its solar distance is the planet's and unchanged.
            b.orbitSpeed = OrbitalMechanics.MoonAngularSpeed(b.parentBody, b.orbitRadius);
        }

        var oc = b.visualObject != null ? b.visualObject.GetComponent<OrbitController>() : null;
        if (oc != null) { oc.SetRadius(b.orbitRadius); oc.SetSpeed(b.orbitSpeed); oc.ForceRingRedraw(); }

        Rescore(b, star);
    }

    /// Reset every body in a system (planets AND their moons) back to its generated orbit.
    public static void ResetSystem(List<CelestialBody> bodies, StarData star)
    {
        if (bodies == null) return;
        foreach (var b in bodies)
        {
            if (b == null) continue;
            ResetOrbit(b, star);
            if (b.moons != null)
                foreach (var m in b.moons) ResetOrbit(m, star);
        }
    }

    /// Re-fit a whole system to the star's CURRENT size and mass — the Dev star editor's "Fit orbits to
    /// star" button, run after size/mass edits. Distinct from ResetSystem: it does NOT restore the generated
    /// radii. It keeps every planet where the dev left it if that still clears the (possibly now-bigger)
    /// star and its neighbours, and only pushes inward planets outward far enough to clear them
    /// (OrbitSafety.EnforceSystem). It then re-times every planet from the star's current MASS — orbital
    /// speed follows sqrt(mass)/radius — since a heavier or lighter star should visibly change how fast its
    /// worlds go round, and re-spaces + re-times each planet's moons. Finally it pushes all of that onto the
    /// live visuals (planet + ring positions and speeds).
    public static void FitOrbitsToStar(List<CelestialBody> bodies, StarData star)
    {
        if (bodies == null) return;

        // Clear the star's current radius (visualScale) and keep every lane collision-free.
        OrbitSafety.EnforceSystem(bodies, star);

        foreach (var b in bodies)
        {
            if (b == null) continue;
            // EnforceSystem only re-timed the planets it actually moved; re-time ALL of them from the star's
            // current mass so an edit that didn't need to move anything still re-speeds the system.
            if (b.parentBody == null && star != null)
                b.orbitSpeed = OrbitalMechanics.PlanetAngularSpeed(star, b.orbitRadius);
            SyncController(b);
            Rescore(b, star);
            if (b.moons != null)
                foreach (var m in b.moons)
                {
                    if (m == null) continue;
                    SyncController(m);   // SpaceMoons (inside EnforceSystem) already re-spaced + re-timed moons
                    Rescore(m, star);
                }
        }
    }

    // Push a body's data-authoritative orbit onto its live OrbitController (radius, speed, ring geometry).
    static void SyncController(CelestialBody b)
    {
        var oc = b != null && b.visualObject != null ? b.visualObject.GetComponent<OrbitController>() : null;
        if (oc != null) { oc.SetRadius(b.orbitRadius); oc.SetSpeed(b.orbitSpeed); oc.ForceRingRedraw(); }
    }

    static void Rescore(CelestialBody b, StarData star)
    {
        var s = SpeciesManager.Current;
        if (b == null || star == null || s == null) return;
        if (!b.habitabilityLocked)
        {
            b.habitability = Habitability.Rate(star, s, b);
            b.isHabitable = Habitability.IsHabitable(star, s, b.type, b.distanceFromStar);
        }
        b.terraformability = Habitability.Terraformability(star, s, b);
    }
}
