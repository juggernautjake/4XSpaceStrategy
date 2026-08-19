using UnityEngine;

// Whether a body has active plate tectonics, rolled once at generation from its Mass and Type.
//
// THE ROLL, AND NOTHING ELSE. This file used to carry a caveat saying the fault-line overlay, mountain
// and volcano placement along faults, and earthquake events were all unbuilt because there was no real
// fault GEOMETRY to hang them on. There is now, and every one of those exists — so the caveat had
// inverted from a warning into a false statement about the codebase, which is worse than no comment.
//
// Where the rest of it lives:
//   TectonicsMap            — the geometry. Which plates, where their margins run, which way each is
//                             pushing, and how far any point is from the nearest one.
//   GeothermalMap           — the Geothermal Index those margins radiate, and the hotspots on worlds
//                             that have no plates at all.
//   PlanetTerrainGenerator  — elevation. Plates make continents, collisions fold mountains, rifts open
//                             troughs; `ridge` is derived from that lift rather than rolled beside it.
//   EarthquakeManager       — quakes, on a 25/50/100-year clock, damaging only highlighted ground.
//
// So this file answers one question — does this world have plates — and everything else asks the map.
public static class TectonicsRules
{
    // Same "large moon" line AtmosphereRules uses — a moon needs real mass to have working plates too.
    public const float LargeMoonSurfaceSize = AtmosphereRules.LargeMoonSurfaceSize;

    /// The smallest moon that can have working plates, in MASS. Derived from the surface-size line the
    /// atmosphere rules already draw, so the two agree by construction rather than by two constants
    /// that happen to match today: LargeMoonSurfaceSize is 9, and size is six per unit of mass.
    public const float LargeMoonMass = LargeMoonSurfaceSize / 6f;   // 1.5 Earths

    /// COULD a body of this type and mass have plates at all — as opposed to whether it rolled them?
    ///
    /// The two questions are different and only one of them is a matter of chance. A gas giant has no
    /// crust to fracture and an asteroid froze through long ago: those are facts about what the body IS,
    /// and no roll enters into it. Everything above those lines is a coin weighted by mass.
    ///
    /// Split out because REMODELLING needs the distinction. Turning a world into another type must clear
    /// its plates when the new type cannot have any, and must roll for them when the old type could not
    /// — but must NOT re-roll in between, because a world's plates are the deepest thing about it
    /// (TectonicsMap keys its whole layout on this) and re-rolling reshapes its continents. See
    /// TerraformManager.Reshape.
    public static bool Possible(CelestialBodyType type, float mass)
    {
        switch (type)
        {
            // No solid crust to fracture (gas giant), or too small to hold onto internal heat (asteroid).
            case CelestialBodyType.GasGiant:
            case CelestialBodyType.Asteroid:
                return false;
            case CelestialBodyType.Moon:
                return mass >= LargeMoonMass;
            default:
                return true;
        }
    }

    /// TAKES MASS, not surfaceSize.
    ///
    /// It used to take the size class and gate on it, and that was fine while size and mass were the
    /// same statement scaled by three. They are not the same statement any more: size saturates at 32,
    /// so every gas giant from 10 to 40 reports the identical class, and the terrestrial range now spans
    /// 0.6 to 4 where it used to span 1 to 7. Reading mass directly means the odds are stated in the
    /// same units the request states them in, and the thresholds below say what they mean.
    public static bool Roll(CelestialBodyType type, float mass)
    {
        if (!Possible(type, mass)) return false;

        switch (type)
        {
            case CelestialBodyType.Moon:
                // Large moons get a modest, mass-scaled chance rather than terrestrial planets' full odds.
                return Random.value < Mathf.Lerp(0.10f, 0.30f, Mathf.InverseLerp(LargeMoonMass, 4f, mass));

            default:
                // Spec §2: tectonics on terrestrial worlds ~1/5 of the time, "more likely for the larger
                // planets". Recalibrated to the Earth-relative scale: ~0.20 at an Earth mass — which IS
                // the spec's one-in-five, now stated at the world it was meant to describe — climbing
                // toward 0.55 at the 4-Earth ceiling, so bigger worlds really are more likely without
                // small ones ever being impossible.
                float sizeFactor = Mathf.InverseLerp(MassRules.TerrestrialDefault, MassRules.TerrestrialMax, mass);
                float chance = Mathf.Lerp(0.20f, 0.55f, sizeFactor);
                return Random.value < chance;
        }
    }

    // `BoostRidge` USED TO LIVE HERE and is deleted rather than left dead.
    //
    // It multiplied an active world's ridge amplitude by 1.4, which was the best available stand-in
    // while `ridge` was an independent noise field and there was no fault geometry to place ranges
    // along: a tectonic world folded up more mountains, everywhere, because "somewhere in particular"
    // was not yet expressible. It is now — ridge is derived from the ground the plates actually raised
    // (PlanetTerrainGenerator.RidgeFromRelief) — and its three former call sites have carried a note
    // saying so for a while.
    //
    // Deleted because it is not merely unused, it is ACTIVELY WRONG to call. Running it today would add
    // a world-wide roughness bonus on top of margin-placed relief, which is precisely the artefact the
    // rework exists to remove — and a public method sitting here with a plausible name is an invitation
    // to do exactly that.
}
