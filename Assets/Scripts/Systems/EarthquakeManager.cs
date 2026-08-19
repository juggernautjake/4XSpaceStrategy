using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// EARTHQUAKES — on a geological clock, and only where the ground is actually moving
//
// Two things were wrong with the old version, and they were the same thing twice: quakes happened on a
// scale of MINUTES, and they damaged anything near a fault whether or not the survey had marked that
// ground as dangerous. A colony sited anywhere in the neighbourhood of a plate margin lost buildings
// within a few minutes of play — what was meant to be a siting trade-off read as a penalty for having
// built at all.
//
// ---- HOW OFTEN ------------------------------------------------------------------------------
//
// Now the game has a calendar (GameCalendar: one second of game time is one in-game day), a quake can
// be scheduled the way a geologist would describe one — by RETURN PERIOD:
//
//   SMALL     about once every 25 years    minor damage
//   MODERATE  about once every 50 years    real damage
//   MAJOR     about once every 100 years   devastating
//
// Those are the request's own numbers. At 1x speed a year is six minutes, so a major quake is a
// once-every-ten-hours event on any given world and a small one lands every couple of hours — rare
// enough to be news, frequent enough that a long game on a fault line is a running cost rather than a
// theoretical one.
//
// The three are INDEPENDENT rolls rather than one roll with three outcomes, which is what makes the
// numbers mean what they say: "once every 25 years" is the rate of small quakes, not the rate of
// quakes-of-which-some-are-small. A world therefore gets roughly seven quakes a century, one of them
// bad.
//
// ---- AND WHERE ------------------------------------------------------------------------------
//
// A quake damages ONLY what is standing on ground the Geothermal Index has highlighted — 70% or above,
// the same band the Survey overlay paints red and refuses to site a geothermal plant below. "Buildings
// placed outside of these Geothermal highlighted areas should not take damage during an Earthquake."
//
// That is a much stronger promise than the old "near a fault" test, and it is a better one, because it
// is a promise the player can VERIFY: turn on the overlay, look at the red, and everything outside it
// is safe forever. A hazard you can survey is a decision. A hazard you can only infer is a tax.
//
// Reads the same GeothermalMap the terrain was folded from and the survey overlay draws, so the ground
// that shakes is exactly the ground the map says is dangerous. Damage persists (PlacedBuilding.health
// round-trips through save/load) and is REPAIRABLE (SurfaceBuildManager.Repair), so a quake presents a
// bill rather than a loss.
// ============================================================================================
public class EarthquakeManager : MonoBehaviour
{
    public static EarthquakeManager Instance;

    /// How often each world is checked, in in-game DAYS. Thirty — one game-month — is fine grain
    /// against return periods measured in decades, and it means a world is examined about twelve times
    /// a year rather than twice a second.
    const float CheckIntervalDays = 30f;

    // ---- The three quakes -----------------------------------------------------------------------

    public enum Severity { Small, Moderate, Major }

    /// Return periods, in in-game YEARS. The request's numbers, named.
    const float SmallPeriodYears = 25f;
    const float ModeratePeriodYears = 50f;
    const float MajorPeriodYears = 100f;

    /// The share of a structure's condition each severity takes at the epicentre, before falloff. A
    /// major quake really is devastating — nearly half a building's condition in one night — while a
    /// small one is a repair bill you might not bother with immediately.
    static void DamageRange(Severity s, out float lo, out float hi)
    {
        switch (s)
        {
            case Severity.Major: lo = 0.26f; hi = 0.46f; break;
            case Severity.Moderate: lo = 0.10f; hi = 0.20f; break;
            default: lo = 0.03f; hi = 0.08f; break;
        }
    }

    /// How far each severity reaches, as a fraction of the map's width, and the absolute bounds on that.
    /// A major quake shakes a whole region; a small one is felt in the next valley.
    static float RadiusFrac(Severity s)
        => s == Severity.Major ? 0.22f : s == Severity.Moderate ? 0.14f : 0.07f;
    const float MinQuakeRadius = 3f, MaxQuakeRadius = 30f;

    /// A structure at or above this is never destroyed outright — a quake that would flatten it leaves it
    /// standing here instead, wrecked and obvious. Below it, the next quake can finish the job.
    ///
    /// Kept from the old system, and it matters MORE now that a major quake hits for nearly half a
    /// building's condition: without it a single roll could delete a capitol, which is the kind of loss
    /// that has no counterplay and reads as the game cheating rather than as a hazard the player accepted
    /// when they sited there. Losing a building stays a two-stage story: the first quake wrecks it and
    /// says so, and only a SECOND one on an already-wrecked structure takes it away.
    const float CondemnedAt = 0.12f;

    /// Ground at or above this on the Geothermal Index is what the Survey overlay highlights, what a
    /// geothermal plant may be built on — and what a quake can damage. One number, three meanings, and
    /// they have to be the same number or the map stops being a promise.
    const float DangerousGround = SurfaceIndex.ShowFloor;

    /// When each world was last checked, in days. Per-body rather than one global timer so a world
    /// discovered late does not inherit a century of accumulated risk on its first tick.
    ///
    /// KEYED ON A BODY REFERENCE, which is the same hazard GameManager clears SurfaceIndex, TectonicsMap
    /// and GeothermalMap for when a galaxy is replaced: without a matching clear this dictionary keeps
    /// every world of every galaxy the session ever generated alive for the rest of the session, and each
    /// world holds its whole surface grid. See ResetAll.
    readonly Dictionary<CelestialBody, double> lastChecked = new Dictionary<CelestialBody, double>();

    /// The next day the whole-galaxy sweep is allowed to run.
    ///
    /// Update() walks EVERY body in the galaxy, and the thing it is looking for moves on a scale of
    /// decades — so doing that once a frame was a few hundred iterations per frame to discover, almost
    /// always, that not a single world was due. One gate at the top costs one comparison instead.
    double nextSweepDay = double.NegativeInfinity;

    public static void Create()
    {
        if (Instance != null) return;
        new GameObject("EarthquakeManager").AddComponent<EarthquakeManager>();
    }

    void Awake() { Instance = this; }

    /// Forget every world. Called when a galaxy is generated or loaded, alongside the other per-body
    /// caches — the bodies in here belong to the galaxy being replaced.
    public static void ResetAll()
    {
        if (Instance == null) return;
        Instance.lastChecked.Clear();
        Instance.nextSweepDay = double.NegativeInfinity;
    }

    void Update()
    {
        if (SystemContext.Galaxy == null) return;

        double now = GameCalendar.TotalDays;
        if (now < nextSweepDay) return;
        nextSweepDay = now + CheckIntervalDays;

        foreach (var b in SystemContext.AllBodies())
        {
            if (b == null || b.surface == null) continue;
            if (b.placedBuildings == null || b.placedBuildings.Count == 0) continue;
            // A world with neither plates nor plumes has nothing to release. Note this is GeothermalMap
            // rather than TectonicsMap: a plate-less world covered in volcanic hotspots absolutely does
            // shake, and its Geothermal Index says where.
            if (!GeothermalMap.Active(b)) continue;

            if (!lastChecked.TryGetValue(b, out double last)) { lastChecked[b] = now; continue; }

            // CAPPED AT ONE INTERVAL, and this is a correctness fix rather than a tuning one.
            //
            // The two `continue`s above deliberately do NOT stamp lastChecked, so a world's clock keeps
            // running while it has nothing to damage or no geothermal ground. That is fine for a few
            // months and wrong for a few centuries: `expected` below is `elapsed / periodDays`, used
            // directly as a probability, and once elapsed passes the return period that probability
            // exceeds 1 and `Random.value < expected` stops being a roll and becomes a certainty.
            //
            // A colony that was levelled and rebuilt a hundred years later therefore ate a guaranteed
            // major quake in its first month back — and so did any world that only BECAME geothermally
            // active later, which a Dev reseed or a remodel to a volcanic type can do at any time.
            //
            // Risk should accrue while there is something to lose, and there was nothing. Taking at most
            // one interval's worth per check says exactly that, and errs downward, which is the safe
            // direction for a mechanic whose whole promise is that siting off the red keeps you safe.
            double elapsed = now - last;
            if (elapsed < CheckIntervalDays) continue;
            lastChecked[b] = now;
            if (elapsed > CheckIntervalDays) elapsed = CheckIntervalDays;

            // MORE ACTIVE GROUND SHAKES MORE OFTEN, but only within half a factor either way: the return
            // periods above are the design, and letting activity swing them by an order of magnitude
            // would make the stated numbers describe no world in particular.
            float activity = Mathf.Clamp01(GeothermalMap.WorldIntensity(b));
            float rate = Mathf.Lerp(0.6f, 1.4f, activity);

            // Three independent rolls, worst first — so a night that produces both a major and a small
            // quake reports the major one, which is the one the player needs to hear about.
            if (Roll(elapsed, MajorPeriodYears, rate)) { Strike(b, Severity.Major); continue; }
            if (Roll(elapsed, ModeratePeriodYears, rate)) { Strike(b, Severity.Moderate); continue; }
            if (Roll(elapsed, SmallPeriodYears, rate)) Strike(b, Severity.Small);
        }
    }

    /// Did a quake of this return period happen in the days that just elapsed?
    ///
    /// The expected COUNT over the interval, used directly as a probability. That is only exactly right
    /// while the expectation is far below one — which it is, by three orders of magnitude, since the
    /// interval is a month and the periods are decades. Doing it properly (1 - e^-λ) would differ in the
    /// fifth decimal place and would need explaining.
    static bool Roll(double elapsedDays, float periodYears, float rate)
    {
        double periodDays = periodYears * GameCalendar.DaysPerYear;
        double expected = elapsedDays / periodDays * rate;
        return Random.value < (float)expected;
    }

    void Strike(CelestialBody b, Severity severity)
    {
        int w = b.surface.width, h = b.surface.height;

        // ---- THE EPICENTRE: the most dangerous ground someone has built on ----
        //
        // Not the most dangerous ground on the world. A quake out under empty crust damages nothing and
        // would be reported to the player as nothing, so the roll would read as a system that fires and
        // does not do anything. Finding the worst-sited structure and releasing there means every quake
        // that happens is a quake the player can see the consequences of — and a colony that stayed off
        // the red simply never has one, which is the point of the whole mechanic.
        Vector2 epicentre = Vector2.zero;
        float worst = 0f;
        foreach (var pb in b.placedBuildings)
        {
            if (pb == null) continue;
            float geo = SurfaceIndex.Get(b, SurfaceIndexKind.Geothermal, pb.x, pb.y);
            if (geo > worst) { worst = geo; epicentre = new Vector2(pb.x, pb.y); }
        }

        // Nothing standing on dangerous ground this time. The ground still moved; there was nothing on it
        // to notice.
        if (worst < DangerousGround) return;

        DamageRange(severity, out float dmgLo, out float dmgHi);
        float radius = Mathf.Clamp(RadiusFrac(severity) * w, MinQuakeRadius, MaxQuakeRadius);

        int destroyed = 0, damaged = 0;
        // Copy the list first: destroying a building mutates b.placedBuildings.
        foreach (var pb in new List<PlacedBuilding>(b.placedBuildings))
        {
            if (pb == null) continue;

            // ---- THE PROMISE, ENFORCED PER BUILDING ----
            //
            // Outside the highlighted band, nothing happens. Not reduced damage, not a small chance —
            // nothing. Checked on the structure's own tile rather than on its distance from anything, so
            // the answer is exactly what the Survey overlay showed the player when they sited it.
            float geo = SurfaceIndex.Get(b, SurfaceIndexKind.Geothermal, pb.x, pb.y);
            if (geo < DangerousGround) continue;

            float dx = pb.x - epicentre.x, dy = pb.y - epicentre.y;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            if (dist > radius) continue;

            float falloff = 1f - dist / radius;                       // 1 at the epicentre, 0 at the edge
            // Worse ground shakes harder: a structure sitting on a 100% margin takes the full roll, one
            // on the 70% edge of the band takes about two-thirds of it.
            float exposure = Mathf.Lerp(0.65f, 1f, Mathf.InverseLerp(DangerousGround, 1f, geo));
            float dmg = Random.Range(dmgLo, dmgHi) * falloff * exposure;
            if (dmg <= 0f) continue;

            float next = pb.health - dmg;
            if (next <= 0f && pb.health > CondemnedAt)
            {
                pb.health = CondemnedAt;
                damaged++;
                continue;
            }

            pb.health = next;
            if (pb.health <= 0f) { SurfaceBuildManager.Demolish(b, pb, refund: false); destroyed++; }
            else damaged++;
        }

        if (destroyed == 0 && damaged == 0) return;

        // Only bother the player about their own worlds.
        if (b.owner == FactionManager.Player)
        {
            SimpleAudio.Instance?.PlayNotify(NotifKind.Danger);
            string what = severity == Severity.Major ? "A major earthquake"
                        : severity == Severity.Moderate ? "An earthquake"
                        : "A tremor";
            string msg = destroyed > 0
                ? $"{what} on active ground destroyed {destroyed} structure{(destroyed == 1 ? "" : "s")}" +
                  (damaged > 0 ? $" and damaged {damaged} more." : ".")
                : $"{what} on active ground damaged {damaged} structure{(damaged == 1 ? "" : "s")}.";
            NotificationManager.Instance?.Push(
                $"{severity} earthquake on {b.name} — {GameCalendar.Stamp()}", msg, Fly(b), NotifKind.Danger);
        }
    }

    System.Action Fly(CelestialBody b) => () =>
    {
        if (b != null && b.visualObject != null)
            CameraController.Instance?.FocusAndZoom(b.visualObject.transform, b.surfaceSize, true);
        PlanetUI.Instance?.Show(b);
    };
}
