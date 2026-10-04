using UnityEngine;

// Computes a 0-100 habitability rating for a body from a SPECIES' perspective. Each species shifts
// and widens the star's Goldilocks zone according to its ideal temperature and tolerance, and weights
// body types by its own biology. "Habitable" (green ring) means the body sits inside that species'
// shifted zone. Hostile worlds still score low but remain valuable for their ores and anomalies.
public static class Habitability
{
    /// Where a world placed FOR this species should orbit: the overlap of the species' preferred band and
    /// the star's own liquid-water band. The map draws the star's band, so a homeworld or a guaranteed
    /// habitable world placed by the species band alone could land outside the green — which is the
    /// first thing a player would notice. Falls back to the star's band if the two do not overlap.
    public static bool PlacementZone(StarData star, Species species, out float inner, out float outer)
    {
        inner = outer = 0f;
        if (star == null || !star.hasHabitableZone) return false;
        if (!GetZone(star, species, out float si, out float so)) { inner = star.hzInner; outer = star.hzOuter; return true; }
        inner = Mathf.Max(si, star.hzInner);
        outer = Mathf.Min(so, star.hzOuter);
        if (inner > outer) { inner = star.hzInner; outer = star.hzOuter; }
        return true;
    }

    // The species' preferred orbital band, derived by shifting/scaling the star's base zone.
    public static bool GetZone(StarData star, Species species, out float inner, out float outer)
    {
        inner = outer = 0f;
        if (star == null || !star.hasHabitableZone || species == null) return false;

        // ============================================================================================
        // A SPECIES SHADES THE STAR'S ZONE. IT DOES NOT INVENT ITS OWN.
        //
        // The old numbers reported, for a default Terran around a G-type whose own band was 10.1-19.6,
        // a species band of 8.9-33.9 — a zone 2.3 times as wide as the star's, whose centre sat 22%
        // further out than the star's, and which was therefore mostly in places the star does not
        // actually warm. "Any way you look at it the current habitable zone band system is broken" is
        // a fair reading of that, and these three lines are where it came from:
        //
        //   tempShift  Lerp(1.95, 0.5)   a mid-temperature species was displaced outward by 22%, and
        //                                the extremes by nearly a factor of two in either direction
        //   half       HzWidth * 1.15    already wider than the star's whole band before tolerance
        //   asymmetry  -0.85 / +1.45     and then stretched to 2.3x by the two ends disagreeing
        //
        // The physics being modelled is real — a cold-adapted species genuinely can live further out —
        // but it is a SHADE on the star's zone, not a relocation of it. A species that prefers Earth's
        // temperature should sit on the star's own Goldilocks band, because that is the definition of
        // the band.
        // ============================================================================================

        // +/-25% at the extremes of idealTemp, and dead on the star's centre in the middle.
        float tempShift = Mathf.Lerp(1.25f, 0.80f, Mathf.Clamp01(species.idealTemp));
        float center = star.HzCenter * tempShift;

        // Half the star's band as the baseline — so a typical species' zone is about the star's own
        // width, not double it — scaled by tolerance within a range that cannot collapse or run away.
        float half = star.HzWidth * 0.5f * Mathf.Clamp(species.tolerance, 0.6f, 1.4f);

        // Still asymmetric, because a real habitable zone does reach further out than in: past the
        // outer edge a thick atmosphere can hold warmth, while inside the inner edge nothing helps.
        // 0.9 / 1.2 rather than 0.85 / 1.45 — a lean, not a different zone.
        inner = Mathf.Max(0.5f, center - half * 0.9f);
        outer = center + half * 1.2f;
        return true;
    }

    public static bool InZone(StarData star, Species species, float distanceFromStar)
    {
        if (!GetZone(star, species, out float inner, out float outer)) return false;
        return distanceFromStar >= inner && distanceFromStar <= outer;
    }

    // Minimum body-type affinity for a world to count as naturally "habitable" (green ring). Below
    // this, the world is the wrong KIND of place for the species even if the starlight is right.
    public const float HabitableAffinity = 0.55f;

    // "Habitable" = in the species' orbital band AND a body type the species can actually live on. This
    // is what the green habitable ring means, so it no longer flags worlds a species couldn't settle.
    ///
    /// Inside the STAR'S band — the green band the map draws — not the species' own, so the "Habitable"
    /// label and the green ring round a world can never disagree.
    public static bool IsHabitable(StarData star, Species species, CelestialBodyType type, float distanceFromStar)
        => StarDatabase.InZone(star, distanceFromStar) && species != null && species.Affinity(type) >= HabitableAffinity;

    // 0..100 for the given species.
    public static float Rate(StarData star, Species species, CelestialBodyType type, float distanceFromStar)
    {
        if (!GetZone(star, species, out float inner, out float outer)) return 0f;

        float center = (inner + outer) * 0.5f;
        float half = Mathf.Max(0.001f, (outer - inner) * 0.5f);

        float positional;
        if (distanceFromStar >= inner && distanceFromStar <= outer)
        {
            float t = 1f - Mathf.Abs(distanceFromStar - center) / half; // 1 centre, 0 edge
            positional = 60f + 40f * t;
        }
        else
        {
            float over = (distanceFromStar < inner) ? (inner - distanceFromStar) : (distanceFromStar - outer);
            float k = over / half;
            positional = 60f * Mathf.Exp(-0.9f * k * k);
        }

        return Mathf.Clamp(positional * species.Affinity(type), 0f, 100f);
    }

    // ============================================================================================
    // THE RATING IS THE WORLD AS IT IS, NOT WHERE IT ORBITS
    //
    // "A nearly 500 °C planet with no atmosphere has a higher habitability rating (13%) than a Terran
    // rocky world at around 40 °C with tons of biosphere and a full atmosphere (6%)."
    //
    // That was this function. It scored POSITION — how near the centre of the species' orbital band a
    // world sat — times a per-type preference, and the only physical fact it consulted was air pressure.
    // Temperature, water and life were never read. So a volcanic furnace parked mid-band outscored a
    // living world whose orbit and climate disagreed (the sandbox, an orbit migration, a re-rolled star),
    // and nothing on screen could explain why.
    //
    // It now scores what a colonist would actually stand in:
    //
    //   TEMPERATURE   how close the world's average °C is to this species' ideal, on a smooth falloff
    //                 whose width is the species' tolerance — and wider for species whose ideal is far
    //                 from temperate, since a furnace-dweller's comfort is not measured in single degrees.
    //   AIR           the species' own breathable band (AtmosphereSuitability), as before.
    //   WATER         liquid water, for species that need it (TerraformDiagnosis.NeedsWater).
    //   LIFE          a living biosphere, for species that farm one (TerraformDiagnosis.NeedsBiosphere).
    //   GRAVITY       too light to hold a body comfortably, or crushing.
    //   KIND          the species' preference for this type of world — kept, but as a modest weight
    //                 rather than the multiplier that decided everything.
    //
    // Multiplied, because each is a real veto: a perfect climate under no air is not a home. Floored per
    // term rather than zeroed, so a hard world is a bad world (domes, suits) rather than a nonexistent one
    // — the AI's target list and the colony objectives still see it.
    // ============================================================================================
    public static float Rate(StarData star, Species species, CelestialBody b)
    {
        if (b == null || species == null) return 0f;

        // No surface to stand on: a gas giant is a place for stations, not colonists.
        if (b.type == CelestialBodyType.GasGiant) return GasGiantRating;
        // No starlight. A black hole's worlds carry ordinary-looking climate numbers (the reference
        // distance floors to a dim dwarf's), but nothing there is lit or warmed by anything.
        var host = star != null ? star : b.hostStar;
        if (host != null && host.isBlackHole) return GasGiantRating;

        float c = PlanetTemperature.BodyAverageCelsius(b);
        float ideal = IdealCelsius(species);
        float off = Mathf.Abs(c - ideal) / TemperatureWidth(species, ideal);
        float temp = Mathf.Exp(-0.7f * off * off);

        float air = Mathf.Lerp(0.25f, 1f, species.AtmosphereSuitability(b.atmospheres));

        // WATER AND LIFE ONLY FOR LIQUID-WATER LIFE. A Cryithn's ideal is -54 °C and a Pyrothian's 360 °C;
        // neither world can hold liquid water or a biosphere, so asking those species for them would cap
        // their best possible home at a third of the scale. The test is the species' own cradle: if it is
        // not a living world, they are not that kind of life.
        bool waterLife = GalaxyGenerator.CradleWantsLife(species);
        float water = 1f;
        if (waterLife && TerraformDiagnosis.NeedsWater(species) && !BiosphereRules.HasLiquidWater(b)) water = 0.45f;

        float life = 1f;
        if (waterLife && TerraformDiagnosis.NeedsBiosphere(species) && !b.biosphereActive) life = 0.65f;

        // GRAVITY AGAINST THE SPECIES' OWN HOME. Pyrothians come from a 3.5-Earth world; their cradle
        // must not read as crushing to them because it would crush a Terran.
        float home = Mathf.Max(0.2f, species.cradleMass);
        float gravity = 1f;
        if (b.mass < home * LightFraction)
            gravity = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(b.mass / (home * LightFraction)));
        else if (b.mass > home * HeavyFraction)
            gravity = Mathf.Lerp(1f, 0.7f, Mathf.Clamp01((b.mass - home * HeavyFraction) / (home * 1.5f)));

        float kind = Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(species.Affinity(b.type)));

        return Mathf.Clamp(100f * temp * air * water * life * gravity * kind, 0f, 100f);
    }

    /// A gas giant's rating: never zero (it can host orbital habitats), never a colony target.
    const float GasGiantRating = 2f;

    /// Gravity starts to cost a colony below this fraction of its species' home mass, and above this one.
    const float LightFraction = 0.6f, HeavyFraction = 1.8f;

    /// The temperature this species is most at home in, in °C. Life-bearing species share the cradle's
    /// own target (so a terraformed world and a homeworld agree on "ideal"); the furnace- and ice-world
    /// species map their preference across a far wider range, because they are not liquid-water life.
    public static float IdealCelsius(Species species)
    {
        if (species == null) return 20f;
        if (GalaxyGenerator.CradleWantsLife(species)) return GalaxyGenerator.CradleTargetCelsius(species);
        return Mathf.Lerp(-150f, 450f, Mathf.Clamp01(species.idealTemp));
    }

    /// How many °C off its ideal a species can be before the rating falls hard. Tolerance widens it, and
    /// so does an ideal far from temperate.
    static float TemperatureWidth(Species species, float ideal)
        => 15f * Mathf.Clamp(species.tolerance, 0.6f, 1.6f) + Mathf.Abs(ideal - 20f) * 0.25f;

    // How feasible it is to TERRAFORM a body to livability for a species (0..100), separate from its
    // current habitability. A world can be uninhabitable now yet very terraformable: what matters is
    // whether it sits near the species' orbital band (right amount of starlight), whether its body
    // type is amenable, and whether it has the water/volatiles to work with. Because it uses the
    // species' shifted zone and type affinities, the same world is more terraformable for some races
    // than others. This value is the ceiling that terraforming can raise habitability toward.
    public static float Terraformability(StarData star, Species species, CelestialBody b)
    {
        if (star == null || species == null || b == null) return 0f;
        if (!GetZone(star, species, out float inner, out float outer)) return 0f;

        float center = (inner + outer) * 0.5f;
        float half = Mathf.Max(0.001f, (outer - inner) * 0.5f);

        // Starlight potential: terraforming can fix atmosphere/temperature, but not orbital distance.
        // Worlds within ~2.5x the zone half-width can be warmed/cooled into range.
        float posK = Mathf.Clamp01(1f - Mathf.Abs(b.distanceFromStar - center) / (half * 2.5f));

        // Body type amenability (species biology weights this).
        float typeK = Mathf.Clamp01(species.Affinity(b.type) * 0.75f + 0.25f);

        // Available water/volatiles help enormously.
        float waterK = 0f;
        if (b.resources != null && b.resources.resources.TryGetValue(ResourceType.Water, out var w))
            waterK = Mathf.Clamp01(w / 220f);

        // A world already partly livable is of course easy to finish.
        float currentK = Mathf.Clamp01(b.habitability / 100f);

        float score = 0.42f * posK + 0.24f * typeK + 0.16f * waterK + 0.18f * currentK;
        return Mathf.Clamp(score * 100f, 0f, 100f);
    }

    public static string Label(float rating, bool inZone)
    {
        if (inZone) return "Habitable";
        if (rating >= 45f) return "Marginal";
        if (rating >= 20f) return "Hostile";
        return "Uninhabitable";
    }

    // Smooth red -> yellow -> green gradient for the score number.
    public static Color ScoreColor(float rating)
    {
        float t = Mathf.Clamp01(rating / 100f);
        return t < 0.5f
            ? Color.Lerp(new Color(1f, 0.30f, 0.25f), new Color(1f, 0.82f, 0.25f), t * 2f)
            : Color.Lerp(new Color(1f, 0.82f, 0.25f), new Color(0.35f, 1f, 0.4f), (t - 0.5f) * 2f);
    }

    public static string ScoreColorHex(float rating)
        => "#" + ColorUtility.ToHtmlStringRGB(ScoreColor(rating));
}
