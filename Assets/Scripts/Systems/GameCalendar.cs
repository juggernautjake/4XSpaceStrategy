using UnityEngine;

// ============================================================================================
// THE GAME CLOCK — one real second is one in-game DAY.
//
// Everything in this codebase that takes time already measures it in SECONDS of scaled game time
// (`Time.deltaTime`, which Unity has already multiplied by `Time.timeScale`): build jobs, research,
// ship travel, terraforming, colony growth. That was the right unit to *simulate* in and the wrong one
// to *speak* in — a spaceport that reads "45s" is a stopwatch, not a construction project.
//
// So this changes the LANGUAGE, not the simulation. One second of game time is one day, thirty days is
// a month, twelve months is a year, and the game starts on Year 0001, Month 01, Day 01. Every duration
// the player is shown runs through Duration() and comes back as days and months; every date runs through
// Stamp(). Not one tick rate, cost or balance number moves, which is the point: this is a unit
// conversion, and a unit conversion that changed the numbers would be a bug.
//
// WHY 30-DAY MONTHS AND A 360-DAY YEAR. There is no leap-year rule, no month-length table and no
// per-world calendar, deliberately. The calendar exists to make "three months" a sentence a player can
// reason about; a 31st of March buys nothing and costs a table lookup in every conversion. A world's own
// year (its orbital period) is a separate, physical thing and is still reported as one — see
// OrbitalMechanics.PeriodSeconds, which now reads out in days through this class.
//
// DERIVED FROM A SINGLE ACCUMULATOR. `days` is the only state, it is a double (a float loses whole days
// to rounding after a few in-game centuries), and it is driven from one place — GameClock below. Year,
// month and day are computed from it, never stored, so they cannot drift apart from each other.
// ============================================================================================
public static class GameCalendar
{
    /// Seconds of scaled game time in one in-game day. The whole convention, in one number.
    public const float SecondsPerDay = 1f;

    public const int DaysPerMonth = 30;
    public const int MonthsPerYear = 12;
    public const int DaysPerYear = DaysPerMonth * MonthsPerYear;   // 360

    /// The game opens here.
    public const int StartYear = 1;

    static double days;

    /// Days elapsed since Year 0001, Month 01, Day 01. The save's single time field.
    public static double TotalDays => days;

    /// Fires when the whole-day number changes — for anything that wants to react once a day rather
    /// than once a frame (notifications, yearly events). Never fires mid-day.
    public static event System.Action OnDayChanged;

    static int lastWholeDay;

    public static void Reset()
    {
        days = 0.0;
        lastWholeDay = 0;
        OnDayChanged?.Invoke();
    }

    /// Restore from a save. Clamped at zero — a negative date is not a thing.
    public static void SetTotalDays(double d)
    {
        days = d > 0.0 ? d : 0.0;
        lastWholeDay = (int)days;
        OnDayChanged?.Invoke();
    }

    /// Advance by a slice of SCALED game time. `Time.deltaTime` is already multiplied by
    /// `Time.timeScale`, so a paused game advances no days and a 5x game advances five times as fast —
    /// exactly like every other timed system here.
    public static void Advance(float scaledDeltaSeconds)
    {
        if (scaledDeltaSeconds <= 0f) return;
        days += scaledDeltaSeconds / SecondsPerDay;

        int whole = (int)days;
        if (whole != lastWholeDay)
        {
            lastWholeDay = whole;
            OnDayChanged?.Invoke();
        }
    }

    public static int Year => StartYear + (int)(days / DaysPerYear);
    public static int Month => (int)((days % DaysPerYear) / DaysPerMonth) + 1;
    public static int Day => (int)(days % DaysPerMonth) + 1;

    /// The date, spelled out. For headers and log lines.
    public static string Stamp() => $"Year {Year:0000} · Month {Month:00} · Day {Day:00}";

    /// The date, compact. For a status bar that has one line to spend.
    public static string Short() => $"{Year:0000}-{Month:00}-{Day:00}";

    // ---- Durations ------------------------------------------------------------------------------

    /// Seconds of game time -> days. The one conversion; nothing else divides by SecondsPerDay.
    public static float DaysFromSeconds(float seconds) => seconds / SecondsPerDay;

    public static float SecondsFromDays(float d) => d * SecondsPerDay;

    /// How long something takes, in words: "18 days", "4 months", "1 year 3 months".
    ///
    /// COARSENS AS IT GROWS, which is the only way a duration string stays readable across four orders
    /// of magnitude. Under a month you want the day count exactly; over a year nobody cares about the
    /// days, and printing them ("2 years, 4 months, 17 days") turns a glanceable figure into a sentence
    /// that has to be read. The remainder is carried one unit down and no further.
    public static string Duration(float seconds)
    {
        if (seconds <= 0f) return "instant";

        float d = DaysFromSeconds(seconds);
        if (d < 1f) return "under a day";

        int totalDays = Mathf.RoundToInt(d);
        if (totalDays < DaysPerMonth) return totalDays == 1 ? "1 day" : $"{totalDays} days";

        if (totalDays < DaysPerYear)
        {
            int months = totalDays / DaysPerMonth;
            int rem = totalDays % DaysPerMonth;
            string m = months == 1 ? "1 month" : $"{months} months";
            return rem == 0 ? m : $"{m} {rem}d";
        }

        int years = totalDays / DaysPerYear;
        int remMonths = (totalDays % DaysPerYear) / DaysPerMonth;
        string y = years == 1 ? "1 year" : $"{years} years";
        return remMonths == 0 ? y : $"{y} {remMonths}mo";
    }

    /// The same, but always in whole days — for short jobs and travel legs where "23 days" is the
    /// useful figure and "0 months 23d" is not.
    public static string Days(float seconds)
    {
        float d = DaysFromSeconds(seconds);
        if (d < 1f) return "under a day";
        int n = Mathf.RoundToInt(d);
        return n == 1 ? "1 day" : $"{n} days";
    }
}

// ============================================================================================
// The component that actually turns frames into days.
//
// Separate from the static class above for the same reason TimeControl and TimeController are
// separate: the calendar is a pure value that save/load reads and writes, and only ONE object may be
// allowed to advance it. A static that ticked itself from anywhere would double-count the moment two
// callers both decided they were responsible.
// ============================================================================================
public class GameClock : MonoBehaviour
{
    public static GameClock Instance;

    public static void Create()
    {
        if (Instance != null) return;
        Instance = new GameObject("GameClock").AddComponent<GameClock>();
        Object.DontDestroyOnLoad(Instance.gameObject);
    }

    void Awake() { if (Instance == null) Instance = this; }

    void Update() => GameCalendar.Advance(Time.deltaTime);
}
