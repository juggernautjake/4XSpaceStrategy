using System.Collections;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

// ============================================================================================
// WHICH PART OF GENERATION ATE THE FRAME
//
// The loading screen reports a stall it cannot explain:
//
//     [Loading] a single frame took 84.08s during 'Forming star system 7 / 7'
//
// Eighty-four seconds in ONE frame means one span between two `yield`s did essentially the whole
// system's work. The caption cannot say which span, because the caption is written by the loop that
// owns the coroutine and every step inside it looks the same from out there.
//
// The generation path is a chain of iterators — GameManager -> AddSystemStepped ->
// GenerateSystemStepped -> BuildStepped — and an unyielded span is, by definition, the time spent
// inside ONE `MoveNext()`. So the measurement is exact and needs no instrumentation inside the
// generators at all: wrap an iterator in `Watch`, and every step it takes is timed.
//
//   var step = GenProfiler.Watch($"system {i + 1}", GalaxyGenerator.AddSystemStepped(...));
//   while (step.MoveNext()) yield return step.Current;
//
// `Section` is the second half, for the work that is NOT an iterator: it brackets a straight-line
// call so a long one names itself rather than being attributed to whichever `yield` happened to
// follow it. Between the two, a stall is located to a line rather than to a phase.
//
// ---- WHY IT IS ALWAYS ON -----------------------------------------------------------------------
//
// It costs one Stopwatch per iterator and one comparison per step, against work measured in
// milliseconds at the very least. A profiler you have to switch on is a profiler that is off on the
// machine where the problem happens — and this problem was reported from a build, not from here.
// The threshold is high enough that a healthy load prints nothing at all.
// ============================================================================================
public static class GenProfiler
{
    /// A step longer than this is worth a line in the log. Well above a frame (16.7 ms) so ordinary
    /// work is silent, well below the point where a player notices a hitch.
    public const double WarnMs = 50.0;

    /// Wrap an iterator so every unyielded span it takes is timed, and any span over `WarnMs` is
    /// reported with the label and the step number.
    ///
    /// The inner iterator is driven exactly as the caller would have driven it and its `Current` is
    /// passed straight through, so a `WaitForSecondsRealtime` yielded from inside still behaves — this
    /// measures the chain without changing it.
    public static IEnumerator Watch(string label, IEnumerator inner)
    {
        if (inner == null) yield break;

        var clock = new Stopwatch();
        int step = 0;
        while (true)
        {
            clock.Restart();
            bool more = inner.MoveNext();
            double ms = clock.Elapsed.TotalMilliseconds;

            if (ms >= WarnMs)
                Debug.LogWarning($"[GenProfile] {label}: step {step} ran {ms:F0} ms without yielding.");

            if (!more) yield break;
            step++;
            yield return inner.Current;
        }
    }

    /// Time one straight-line call. Returns the elapsed milliseconds so a caller can accumulate.
    ///
    /// Takes an Action rather than being a Begin/End pair on purpose: a Begin with no matching End —
    /// down an early return, or through an exception — would silently attribute the rest of the load to
    /// whatever section was last opened, which is worse than not measuring at all.
    public static double Section(string label, System.Action work)
    {
        if (work == null) return 0.0;
        var clock = Stopwatch.StartNew();
        work();
        double ms = clock.Elapsed.TotalMilliseconds;
        if (ms >= WarnMs) Debug.LogWarning($"[GenProfile] {label} took {ms:F0} ms in one frame.");
        return ms;
    }
}
