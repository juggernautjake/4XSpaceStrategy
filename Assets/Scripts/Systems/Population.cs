using UnityEngine;

// ============================================================================================
// POPULATION
//
// ONE POPULATION UNIT = 100,000 PEOPLE. `CelestialBody.population` is in units, so a homeworld that
// starts at 10 is a city of a million. Everything here converts, scales or grows that number; nothing
// else should invent its own idea of what a "population" is.
//
// Growth is a BIRTH RATE, not a fill bar. It is the product of four things, any of which can stall it:
//   * the housing you've built  — nobody is born in a colony with nowhere to live
//   * satisfaction              — a miserable population stops having children (this is the big one)
//   * habitability              — a barely-livable world is a hard place to raise anyone
//   * crowding                  — growth tails off as a world approaches what it can hold
// Multiplied, not added, so a colony that is desperately unhappy does not grow because it happens to
// have a lot of farms. That's the intent behind "if satisfaction is low, birth rates are low".
// ============================================================================================
public static class Population
{
    /// People represented by one unit of `CelestialBody.population`.
    public const int PerUnit = 100_000;

    // ---- Presentation ----
    /// "1.0 million", "12.4 million", "1.24 billion" — units are an implementation detail; the player
    /// should only ever see people.
    public static string Format(int units)
    {
        double people = (double)units * PerUnit;
        if (people >= 1_000_000_000d) return $"{people / 1_000_000_000d:0.##} billion";
        if (people >= 1_000_000d) return $"{people / 1_000_000d:0.#} million";
        if (people >= 1_000d) return $"{people / 1_000d:0.#} thousand";
        return people <= 0d ? "uninhabited" : $"{people:0}";
    }

    /// Compact form for tight rows: "1.0M", "1.24B".
    public static string Short(int units)
    {
        double people = (double)units * PerUnit;
        if (people >= 1_000_000_000d) return $"{people / 1_000_000_000d:0.##}B";
        if (people >= 1_000_000d) return $"{people / 1_000_000d:0.#}M";
        if (people >= 1_000d) return $"{people / 1_000d:0.#}K";
        return people <= 0d ? "0" : $"{people:0}";
    }

    // ---- Species ----
    // Fertility (1..10) is the species' whole relationship with reproduction, so it drives both how
    // many they arrive with and how fast they add more.

    /// 0.7 .. 1.45, from the sluggish to the explosive.
    public static float FertilityFactor(Species s)
        => s == null ? 1f : Mathf.Lerp(0.7f, 1.45f, Mathf.InverseLerp(1f, 10f, s.fertility));

    /// Long-lived species accumulate people: fewer die, so the same birth rate supports a larger
    /// standing population. 0.85 .. 1.3.
    public static float LongevityFactor(Species s)
        => s == null ? 1f : Mathf.Lerp(0.85f, 1.3f, Mathf.InverseLerp(1f, 10f, s.longevity));

    /// The homeworld's starting population, in units.
    ///
    /// The baseline is 10 units — one million people — for an average species, scaled by fertility and
    /// a little by longevity. So the fast-breeding Aquarii (fertility 9) open with a noticeably bigger
    /// cradle than the slow, silicate Pyrothians (fertility 3), and the Cryithn's extreme longevity
    /// (10) partly compensates for their poor fertility: they breed rarely but almost never die.
    public const int HomeBaseline = 10;   // 1,000,000 people

    public static int HomeStart(Species s)
    {
        float f = HomeBaseline * FertilityFactor(s) * Mathf.Lerp(1f, LongevityFactor(s), 0.5f);
        return Mathf.Max(3, Mathf.RoundToInt(f));
    }

    /// A colony founded by a colony ship: a beachhead, not a city. Roughly a tenth of a homeworld,
    /// scaled the same way, with a nod to how big the world is.
    public static int ColonyStart(CelestialBody b, Species s)
    {
        float f = 2f * FertilityFactor(s) + (b != null ? b.surfaceSize * 0.1f : 0f);
        return Mathf.Max(1, Mathf.RoundToInt(f));
    }

    // ---- Capacity ----
    /// How many units a world can hold. Delegates to Carrying, which is the single authority on the
    /// three ceilings (land, housing, food) and on which of them is binding.
    ///
    /// This used to ADD land and housing together, which had two problems. It meant housing could lift a
    /// world above what its land could support — you could out-build a planet's own habitability — and
    /// it left food out of the ceiling entirely, so a world could grow forever on a single farm. A
    /// ceiling is the smallest of the things that bound you, not the sum of them.
    public static int Capacity(CelestialBody b) => Carrying.Limit(b);

    /// 0..1 from "just barely colonisable" to "paradise". Below the threshold, nothing lives here
    /// unaided. Shared with CityGrowth so the two never disagree about what a good world is.
    public static float Liveability(CelestialBody b)
        => b == null ? 0f : Mathf.Clamp01((b.habitability - Colony.FoundThreshold) / (100f - Colony.FoundThreshold));

    // ---- Growth ----
    /// Population units per second. Zero means the colony is not growing at all, and the UI says why.
    ///
    /// `infrastructure` is the summed popGrowthPerSec of everything built here — farms, habitats,
    /// cities. Without it a colony has no capacity to raise anyone and simply doesn't.
    public static float BirthRate(CelestialBody b, float infrastructure)
    {
        if (b == null || infrastructure <= 0f) return 0f;

        float live = Liveability(b);
        if (live <= 0f) return 0f;                       // below the threshold: nobody is born here

        // Satisfaction is the dominant term, and it can zero the whole thing out. A colony that hates
        // living here does not have children no matter how many farms it has.
        float happy = Satisfaction.GrowthMultiplier(b);
        if (happy <= 0f) return 0f;

        // Habitability, continuous rather than a gate: a 46% world crawls where a 99% one races.
        float habFactor = Mathf.Lerp(0.2f, 1.4f, live);

        // Food, as a brake before it's a crisis. Birth rates fall off as the surplus thins, so a colony
        // slows down BEFORE it starves rather than sailing on and falling off a cliff — which is how
        // real populations behave, and it gives you a chance to notice and plant more farms.
        float ratio = Carrying.FoodRatio(b);
        if (ratio < 1f) return 0f;                       // already short: nobody is planning a family
        float foodFactor = Mathf.Clamp01(Mathf.InverseLerp(1f, 1.35f, ratio));

        // Crowding: growth tails off smoothly as the world fills, rather than slamming into a wall.
        int cap = Capacity(b);
        float room = cap > 0 ? Mathf.Clamp01(1f - (float)b.population / cap) : 0f;
        room = Mathf.Pow(room, 0.65f);                   // stays healthy until genuinely near the top

        return infrastructure * happy * habFactor * foodFactor * room
               * FertilityFactor(SpeciesManager.Current) * GrowthScale;
    }

    // Tuning knob for the whole curve. At 0.02, a content, habitable colony with a couple of farms
    // (infrastructure ~2.5) grows roughly 0.05 units/sec — about 5,000 people a second, so a million
    // people is a few minutes of a well-run world rather than an instant.
    public const float GrowthScale = 0.02f;

    /// Plain-language reason a colony isn't growing, or null if it is.
    public static string StallReason(CelestialBody b, float infrastructure)
    {
        if (b == null) return "no world";
        if (Liveability(b) <= 0f)
            return $"at {b.habitability:F0}% habitability nobody can live here unaided — terraform it past {Colony.FoundThreshold:F0}%";
        if (infrastructure <= 0f)
            return "nowhere to live — build farms or habitats";
        if (Satisfaction.GrowthMultiplier(b) <= 0f)
            return $"the colony is too unhappy to have children ({Satisfaction.For(b):F0}% satisfaction)";
        if (Carrying.FoodRatio(b) < 1f)
            return $"there isn't enough food — {Carrying.FoodLine(b)}";

        int cap = Carrying.Limit(b, out PopLimit bound);
        if (b.population >= cap)
        {
            // Say WHICH ceiling. "At capacity" on its own is a dead end; naming the binding constraint
            // turns it into the next thing to do.
            string advice = Carrying.Advice(bound);
            return advice != null ? $"at capacity — {advice}" : "at capacity";
        }
        return null;
    }
}
