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

    static void Rescore(CelestialBody b, StarData star)
    {
        var s = SpeciesManager.Current;
        if (b == null || star == null || s == null) return;
        if (!b.habitabilityLocked)
        {
            b.habitability = Habitability.Rate(star, s, b.type, b.distanceFromStar);
            b.isHabitable = Habitability.IsHabitable(star, s, b.type, b.distanceFromStar);
        }
        b.terraformability = Habitability.Terraformability(star, s, b);
    }
}
