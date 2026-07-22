using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// PER-WORLD BUILD QUEUE
//
// Buildings used to appear the instant you placed them. Now they are BUILT: a confirmed placement
// becomes a job that occupies Labor, takes real time, and can be paused, reordered or cancelled — so a
// colony is something you plan rather than something you assemble.
//
// Deliberately the same shape as the shipyard's stocks (UnitManager.BuildQueue): several jobs progress
// at once, each holding its own share of a pool, and the pool is what decides how fast. The player has
// already learned that model from ships; teaching them a second one for buildings would be a cost with
// no return.
//
// WHY A JOB CAN RUN WITHOUT ENOUGH LABOR. Blocking would deadlock a queue the moment a world's depots
// were destroyed mid-build, and would make the first job in line a wall rather than a priority. Instead
// a shortfall stretches the remaining work (BuildScaling.TimeFactorFor), so something always
// progresses, and Labor freed by a completion immediately speeds up whatever is next.
// ============================================================================================
public class SurfaceBuildJob
{
    public SurfaceBuildingType type;
    public List<Vector2Int> cells = new List<Vector2Int>();

    public float elapsed;          // seconds of work done
    public float duration;         // seconds of work needed, at full Labor
    public bool paused;

    /// Exactly what was paid, so a cancellation refunds that rather than a re-derived price — costs
    /// move as Industry technologies land, and refunding today's price for yesterday's purchase is how
    /// a queue becomes an exploit.
    public int metalPaid, energyPaid;

    /// Labor this job occupies while it is running.
    public float labor;

    public int Tiles => cells != null ? cells.Count : 0;
    public float Progress => duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
    public float Remaining => Mathf.Max(0f, duration - elapsed);

    /// A paused job gives its workforce back. That is the whole point of the pause button: it is how
    /// you let something else jump the queue without losing what you have already built.
    public bool HoldsLabor => !paused;
}

public static class SurfaceBuildQueue
{
    // Per world. A dictionary rather than a field on CelestialBody because a queue is transient
    // construction state, not part of what a world IS — and CelestialBody is already the most
    // over-subscribed type in the project.
    static readonly Dictionary<CelestialBody, List<SurfaceBuildJob>> queues =
        new Dictionary<CelestialBody, List<SurfaceBuildJob>>();

    public static event System.Action OnChanged;

    public static List<SurfaceBuildJob> For(CelestialBody b)
    {
        if (b == null) return null;
        if (!queues.TryGetValue(b, out var list)) { list = new List<SurfaceBuildJob>(); queues[b] = list; }
        return list;
    }

    public static int Count(CelestialBody b) => For(b)?.Count ?? 0;

    /// Ground already claimed by jobs that have not finished yet.
    ///
    /// SurfaceBuildManager.Occupied only knows about buildings that are STANDING, which is the right
    /// answer for it and the wrong one for a queue: a tile with a half-built factory coming to it is
    /// not free. The placement preview should read this too, so a player is never shown a green cell
    /// they cannot actually have.
    public static HashSet<Vector2Int> PendingCells(CelestialBody b)
    {
        var set = new HashSet<Vector2Int>();
        var list = For(b);
        if (list == null) return set;
        foreach (var job in list)
            if (job?.cells != null)
                foreach (var c in job.cells) set.Add(c);
        return set;
    }

    /// Give back everything every in-flight job paid, and drop them.
    ///
    /// Called when the galaxy underneath these jobs is replaced — a new game, or a save loaded over the
    /// top. The queue is not yet persisted, so without the refund a player who saves mid-construction
    /// loses both the building and the materials with no message. Refunding is the honest interim: they
    /// keep what they paid until the queue itself round-trips.
    public static void RefundAll()
    {
        foreach (var kv in queues)
            foreach (var job in kv.Value)
            {
                if (job == null) continue;
                PlayerEconomy.Add(ResourceType.Metal, job.metalPaid);
                PlayerEconomy.Add(ResourceType.Energy, job.energyPaid);
            }
    }

    /// Start building a drawn footprint. The cost is taken up front, exactly as the shipyard does.
    public static SurfaceBuildJob Enqueue(CelestialBody b, SurfaceBuildingType t, List<Vector2Int> cells)
    {
        if (b == null || cells == null || cells.Count == 0) return null;

        var info = SurfaceBuildingDatabase.Get(t);
        if (info == null) return null;

        // EVERY GATE CanPlace APPLIES, APPLIES HERE TOO. Drawing changes which ground a building
        // occupies, not whether the empire is allowed to build it — without this a painted footprint
        // dodged tech requirements, uniqueness, ownership and the classes that are grown or upgraded
        // into rather than placed.
        if (!SurfaceBuildManager.CanPlaceType(b, t, out _)) return null;

        // Nor may two queued jobs claim the same ground. `Occupied` only knows about buildings that are
        // already standing, so without this both jobs are charged and whichever finishes second is
        // refunded and thrown away — the player pays twice to build once.
        var pending = PendingCells(b);
        foreach (var c in cells) if (pending.Contains(c)) return null;

        int tiles = cells.Count;
        float mult = BuildScaling.CostMultiplier(tiles);

        // Through DiscCost like every other purchase, so Industry research discounts a drawn building
        // exactly as it discounts a placed one.
        int cm = Mathf.RoundToInt(ColonyManager.DiscCost(info.costMetal) * mult);
        int ce = Mathf.RoundToInt(ColonyManager.DiscCost(info.costEnergy) * mult);
        if (!GameMode.DevMode && !PlayerEconomy.Spend(cm, ce)) return null;

        var job = new SurfaceBuildJob
        {
            type = t,
            cells = new List<Vector2Int>(cells),
            // Build time scales on the SAME curve as cost — a bigger building is more work, not just a
            // bigger bill. This is the real brake on a mega-structure: while it goes up, nothing else
            // on this world does.
            duration = Mathf.Max(0.5f, info.buildTime * mult * TechEffects.BuildTimeMult),
            metalPaid = GameMode.DevMode ? 0 : cm,
            energyPaid = GameMode.DevMode ? 0 : ce,
            labor = BuildScaling.LaborFor(t, tiles)
        };

        For(b).Add(job);
        OnChanged?.Invoke();
        return job;
    }

    public static void SetPaused(CelestialBody b, SurfaceBuildJob job, bool paused)
    {
        if (job == null || job.paused == paused) return;
        job.paused = paused;
        SurfaceLabor.Invalidate();
        OnChanged?.Invoke();
    }

    /// Cancel and refund exactly what was paid.
    public static void Cancel(CelestialBody b, SurfaceBuildJob job)
    {
        var list = For(b);
        if (list == null || job == null || !list.Remove(job)) return;
        PlayerEconomy.Add(ResourceType.Metal, job.metalPaid);
        PlayerEconomy.Add(ResourceType.Energy, job.energyPaid);
        SurfaceLabor.Invalidate();
        OnChanged?.Invoke();
    }

    /// Move a job up or down the queue. Order is priority: Labor is handed out from the top.
    public static void Reorder(CelestialBody b, SurfaceBuildJob job, int delta)
    {
        var list = For(b);
        if (list == null || job == null) return;
        int i = list.IndexOf(job);
        if (i < 0) return;
        int to = Mathf.Clamp(i + delta, 0, list.Count - 1);
        if (to == i) return;
        list.RemoveAt(i);
        list.Insert(to, job);
        OnChanged?.Invoke();
    }

    /// Advance every world's queue. Driven from the colony tick, so build time runs on the game clock —
    /// a paused game builds nothing, and a game at 5x builds five times as fast, which is what every
    /// other timed thing in this game already does.
    public static void Tick(CelestialBody b, float dt)
    {
        var list = For(b);
        if (list == null || list.Count == 0) return;

        // Labor is allocated FROM THE TOP. That is what makes queue order mean something: the first job
        // gets the workforce it wants, the next gets what is left, and anything past that crawls.
        float free = SurfaceLabor.Max(b);

        for (int i = 0; i < list.Count; i++)
        {
            var job = list[i];
            if (job == null || job.paused) continue;

            float granted = Mathf.Min(job.labor, Mathf.Max(0f, free));
            free -= granted;

            // Short-handed work is slower, not stopped.
            float factor = BuildScaling.TimeFactorFor(job.labor, granted);
            job.elapsed += dt / Mathf.Max(0.01f, factor);
        }

        // Completions collected FORWARD, then placed in queue order.
        //
        // A reverse loop would be safe for the removal but would place the LOWEST-priority job first —
        // so when two jobs finish on the same tick and overlap, the one the player put first is the one
        // that fails. Order is priority; it has to hold at completion too.
        List<SurfaceBuildJob> finished = null;
        for (int i = 0; i < list.Count; i++)
        {
            var job = list[i];
            if (job == null || job.elapsed < job.duration) continue;
            if (finished == null) finished = new List<SurfaceBuildJob>();
            finished.Add(job);
        }

        if (finished == null) return;

        foreach (var job in finished)
        {
            list.Remove(job);
            Complete(b, job);
        }

        // Fired ONCE, after the loop. Inside Complete it would let a subscriber cancel or reorder while
        // this method is still walking the list.
        OnChanged?.Invoke();
    }

    /// The building actually goes up.
    static void Complete(CelestialBody b, SurfaceBuildJob job)
    {
        // Placed free: the cost was taken when the job was queued. Paying again at completion would
        // charge twice for one building, which is the kind of thing nobody notices until an economy is
        // mysteriously tight.
        var placed = SurfaceBuildManager.PlaceDrawn(b, job.type, job.cells);
        SurfaceLabor.Invalidate();

        string name = SurfaceBuildingDatabase.Get(job.type)?.name ?? "Structure";

        if (placed != null)
        {
            NotificationManager.Instance?.Push($"{name} complete",
                $"{job.Tiles} tiles on {b.name}.", null, NotifKind.Info);
            return;
        }

        // IT FAILED, SO GIVE THE MONEY BACK AND SAY SO.
        //
        // A build takes real time and the ground can change under it — an earthquake, a terraforming
        // project flooding the site, a settlement growing onto it. Dropping the job silently would take
        // the player's metal and energy and hand back nothing, with no message at all: the worst
        // possible combination, because they would never learn it had happened.
        //
        // Refunded rather than re-queued: if the ground is permanently gone, re-queueing loops forever.
        PlayerEconomy.Add(ResourceType.Metal, job.metalPaid);
        PlayerEconomy.Add(ResourceType.Energy, job.energyPaid);
        NotificationManager.Instance?.Push($"{name} could not be completed",
            $"The site on {b.name} is no longer buildable. Materials refunded.", null, NotifKind.Danger);
    }

    /// A new galaxy or a loaded save replaces every world these jobs referred to.
    public static void Clear()
    {
        queues.Clear();
        SurfaceLabor.Invalidate();
        OnChanged?.Invoke();
    }
}
