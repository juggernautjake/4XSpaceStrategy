using UnityEngine;

// ============================================================================================
// ROTATION — and the MAGNETIC FIELD it drives.
//
// A magnetic field used to be a coin flip weighted by mass. It is now a CONSEQUENCE: a world has one if
// it is turning fast enough, and how fast it turns is rolled at generation like every other attribute.
// That is the actual mechanism — a planetary dynamo needs a conducting fluid core AND rotation to stir
// it, and rotation is the half that varies between worlds that are otherwise alike. Venus and Earth are
// nearly the same size and made of nearly the same things; Earth turns once a day and has a
// magnetosphere, Venus takes 243 days and has essentially none.
//
// The mass correlation the old roll had is not lost, it has moved one step back: a bigger world is more
// likely to have SPUN UP and less likely to have been tidally braked, so bigger worlds still usually get
// fields — but now you can see why, on the same panel, next to the rotation figure.
//
// WHAT THIS BUYS. The Dev orbit panel's rotation slider is no longer cosmetic: drag a world's spin below
// the threshold and it loses its magnetosphere, its atmosphere ceiling halves (AtmosphereRules), and its
// air starts to look like Mars'. One number, and the consequences fall out of the rules that were
// already there.
//
// PROGRADE BY DEFAULT, RETROGRADE ALLOWED. Direction is stored apart from speed, because a retrograde
// world is not a world with negative rotation for any purpose except which way it is drawn turning — its
// dynamo, its day length and its magnetosphere all care about the RATE. Keeping them apart is what
// stops "retrograde" from accidentally meaning "no magnetic field".
// ============================================================================================
public static class RotationRules
{
    /// Degrees per second at or above which the dynamo runs and the world has a magnetic field.
    ///
    /// The units are the game's own — `spinSpeed` has always been degrees of axial rotation per second
    /// of game time, which is now one in-game day (see GameCalendar), so a world at 15°/s turns once
    /// every 24 days and one at 36°/s once every 10. The threshold sits at 12 because the roll below
    /// puts every world that was not tidally braked above it and every world that was below it: the
    /// number is where those two populations separate, not a value worlds are scattered across.
    public const float MagneticFieldSpin = 12f;

    /// The slowest and fastest a body may turn. The floor is not zero — a genuinely locked world still
    /// turns once per orbit, and a body with a rotation of exactly zero renders as frozen in place,
    /// which reads as a bug rather than as tidal locking.
    public const float MinSpin = 0.4f, MaxSpin = 40f;

    /// How often a world turns out to be turning backwards. Rare, and rarer for planets than for moons —
    /// a retrograde planet needs a giant impact or a captured origin, while a moon only needs to have
    /// been captured, which happens to moons a great deal.
    public const float RetrogradeChancePlanet = 0.10f;
    public const float RetrogradeChanceMoon = 0.16f;

    /// A world's axial rotation, in degrees per second.
    ///
    /// TWO POPULATIONS, not one spread. A world has either spun up and stayed that way, or it has been
    /// tidally braked by its star (or, for a moon, by its host) and turns slowly enough that the
    /// distinction shows in everything downstream. Rolling one continuous range instead would put a lot
    /// of worlds right at the dynamo threshold, where a tenth of a degree per second decides whether the
    /// world has an atmosphere — and a threshold that most worlds sit exactly on is a threshold that
    /// reads as arbitrary.
    public static float Roll(float mass, bool isMoon)
    {
        float lockChance = isMoon
            ? 0.34f                                                     // moons brake against their host
            : Mathf.Lerp(0.32f, 0.06f, Mathf.InverseLerp(0.3f, 4f, mass));

        if (Random.value < lockChance)
            return Quantize(Random.Range(MinSpin, MagneticFieldSpin - 1.5f));

        // Bigger bodies took more angular momentum out of the disc and have shed less of it since, so
        // the roll is biased toward the fast end as mass climbs — a gas giant is the fastest-turning
        // thing in its system, which is true of ours.
        float bias = Mathf.InverseLerp(0.3f, 12f, mass);
        float t = Mathf.Lerp(Random.value, 0.45f + Random.value * 0.55f, bias);
        return Quantize(Mathf.Lerp(MagneticFieldSpin, MaxSpin, t));
    }

    /// +1 prograde (the default), -1 retrograde.
    public static int RollDirection(bool isMoon)
        => Random.value < (isMoon ? RetrogradeChanceMoon : RetrogradeChancePlanet) ? -1 : 1;

    /// Does a body turning this fast run a dynamo?
    ///
    /// Type still has the final word at the two extremes, and both are physics rather than exceptions: a
    /// gas giant's field is generated by metallic hydrogen under pressures a rocky world never reaches
    /// and does not depend on the surface rate at all, and an asteroid has no molten core to stir
    /// however fast it tumbles.
    public static bool GeneratesField(CelestialBodyType type, float mass, float spinSpeed)
    {
        if (type == CelestialBodyType.GasGiant) return true;
        if (type == CelestialBodyType.Asteroid) return false;
        // Too small to have kept a liquid core, whatever the rotation. Below the terrestrial floor a
        // body froze through long ago.
        if (mass < MassRules.TerrestrialMin) return false;
        return Mathf.Abs(spinSpeed) >= MagneticFieldSpin;
    }

    /// Convenience for callers that already have the finished body — the Dev sandbox and the load path.
    public static bool GeneratesField(CelestialBody b)
        => b != null && GeneratesField(b.type, b.mass, b.spinSpeed);

    /// How long one rotation takes, in in-game DAYS. `spinSpeed` is degrees per second and one second is
    /// one day, so this is just 360 over the rate — but it is the figure a player can actually reason
    /// about, and it belongs in one place.
    public static float RotationPeriodDays(float spinSpeed)
    {
        float s = Mathf.Abs(spinSpeed);
        return s <= 0.001f ? float.PositiveInfinity : 360f / s;
    }

    /// The rotation readout: period, direction, and whether it is enough for a magnetosphere.
    public static string Describe(CelestialBody b)
    {
        if (b == null) return "unknown";
        float period = RotationPeriodDays(b.spinSpeed);
        string dir = b.rotationDirection < 0 ? "retrograde" : "prograde";
        string len = float.IsInfinity(period) ? "not turning" : $"{period:0.#} day{(period >= 1.95f ? "s" : "")}";
        return $"{len}, {dir}";
    }

    /// Quantized to one decimal, like every other headline attribute — "17.4°/s" reads as a measurement
    /// and "17.38291°/s" reads as a leak.
    static float Quantize(float v) => Mathf.Clamp(Mathf.Round(v * 10f) / 10f, MinSpin, MaxSpin);
}
