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

    // Both in SECONDS of game time, which is DAYS on the calendar (see GameCalendar) — the readouts
    // convert, the simulation does not.
    public float elapsed;          // work done
    public float duration;         // work needed, at full Labor
    public bool paused;

    /// Exactly what was paid, so a cancellation refunds that rather than a re-derived price — costs
    /// move as Industry technologies land, and refunding today's price for yesterday's purchase is how
    /// a queue becomes an exploit.
    public int metalPaid, energyPaid;

    /// Labor this job occupies while it is running.
    public float labor;

    /// The standing building this job EXTENDS, if the player drew it onto the edge of one rather than
    /// siting a new structure. On completion the painted cells are absorbed into that building instead
    /// of becoming a second one beside it (see SurfaceBuildManager.AbsorbInto).
    ///
    /// A reference rather than an index, because the queue is not persisted and the reference is only
    /// ever followed on the same session's completion. It is re-validated at that moment — a building
    /// demolished while its own extension was under construction is exactly the case that would
    /// otherwise merge into a corpse — so a stale one degrades to "build it as a new structure".
    public PlacedBuilding mergeInto;

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

    /// This world's queue WITHOUT creating one, or null if it has never had a job.
    ///
    /// `For` inserts an empty list for whatever world it is handed, which is right for the enqueue path
    /// and wrong for the read-only ones: the map draws its ghosts and the view window rebuilds its
    /// signature every frame, on whichever world the player is looking at, and most of those worlds will
    /// never build anything. Asking through here keeps the dictionary to worlds that have actually built.
    public static List<SurfaceBuildJob> Peek(CelestialBody b)
        => b != null && queues.TryGetValue(b, out var list) ? list : null;

    public static int Count(CelestialBody b) => Peek(b)?.Count ?? 0;

    /// Ground already claimed by jobs that have not finished yet.
    ///
    /// SurfaceBuildManager.Occupied only knows about buildings that are STANDING, which is the right
    /// answer for it and the wrong one for a queue: a tile with a half-built factory coming to it is
    /// not free. The placement preview should read this too, so a player is never shown a green cell
    /// they cannot actually have.
    public static HashSet<Vector2Int> PendingCells(CelestialBody b)
    {
        var set = new HashSet<Vector2Int>();
        var list = Peek(b);
        if (list == null) return set;
        foreach (var job in list)
            if (job?.cells != null)
                foreach (var c in job.cells) set.Add(c);
        return set;
    }

    /// The job whose footprint covers this cell, or null. The hover readout's half of PendingCells:
    /// that answers "is this ground spoken for", this answers "by what, and how far along".
    public static SurfaceBuildJob JobAt(CelestialBody b, int x, int y)
    {
        var list = Peek(b);
        if (list == null) return null;
        var cell = new Vector2Int(x, y);
        foreach (var job in list)
            if (job?.cells != null && job.cells.Contains(cell)) return job;
        return null;
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
        => Enqueue(b, t, cells, out _);

    /// As above, reporting WHY a footprint was refused so the build UI can say so.
    ///
    /// The reason matters more here than it does for a fixed footprint. A tetromino either fits or it
    /// visibly doesn't; a drawn one can fail for reasons that are invisible on the map — too few tiles
    /// for this class, or a block that looks joined but meets only at a corner. Silently declining to
    /// build reads as the button being broken.
    public static SurfaceBuildJob Enqueue(CelestialBody b, SurfaceBuildingType t, List<Vector2Int> cells,
                                          out string why)
        => Enqueue(b, t, cells, null, out why);

    /// As above, for a job that EXTENDS a standing building rather than founding a new one.
    ///
    /// `mergeInto` changes what "the shape" means here. The cells are only the new tiles; the building
    /// that results is those plus the one being extended, and the shape rules — the tile minimum above
    /// all — are about the building. Without this a one-tile extension of a twenty-tile farm would be
    /// refused for being one tile, which is a refusal about a building that will never exist.
    public static SurfaceBuildJob Enqueue(CelestialBody b, SurfaceBuildingType t, List<Vector2Int> cells,
                                          PlacedBuilding mergeInto, out string why)
    {
        why = null;
        if (b == null || cells == null || cells.Count == 0) { why = "nothing drawn"; return null; }

        var info = SurfaceBuildingDatabase.Get(t);
        if (info == null) { why = "unknown building"; return null; }

        // A merge target that is no longer standing is not a merge target. Dropped rather than refused:
        // the cells are still perfectly buildable on their own, so the honest outcome is a new building
        // where the extension would have been, not a job the player cannot place at all.
        if (mergeInto != null &&
            (!SurfaceBuildManager.CanMerge(t) || !SurfaceBuildManager.On(b).Contains(mergeInto)))
            mergeInto = null;

        // EVERY GATE CanPlace APPLIES, APPLIES HERE TOO. Drawing changes which ground a building
        // occupies, not whether the empire is allowed to build it — without this a painted footprint
        // dodged tech requirements, uniqueness, ownership and the classes that are grown or upgraded
        // into rather than placed.
        if (!SurfaceBuildManager.CanPlaceType(b, t, out why)) return null;

        // THE SHAPE ITSELF: minimum tiles, orthogonal connectivity, and the square/rectangle families.
        // Checked here rather than only in the UI because this is the chokepoint every drawn building
        // passes through — the UI preview should refuse first and usually does, but a rule enforced only
        // in the preview is a rule that a second entry point silently skips.
        //
        // Asked of the FINISHED building: for an extension that is the new cells plus the ones already
        // standing. See the note on `mergeInto` above.
        var shape = cells;
        if (mergeInto != null)
        {
            var have = new HashSet<Vector2Int>(cells);
            shape = new List<Vector2Int>(cells);
            foreach (var c in SurfaceBuildingDatabase.Footprint(mergeInto))
                if (have.Add(c)) shape.Add(c);
        }
        if (!BuildShapeRules.Validate(info, shape, out why)) return null;

        // Nor may two queued jobs claim the same ground. `Occupied` only knows about buildings that are
        // already standing, so without this both jobs are charged and whichever finishes second is
        // refunded and thrown away — the player pays twice to build once.
        var pending = PendingCells(b);
        foreach (var c in cells)
            if (pending.Contains(c)) { why = "another queued build already claims that ground"; return null; }

        int tiles = cells.Count;
        float mult = BuildScaling.CostMultiplier(tiles);

        // Through DiscCost like every other purchase, so Industry research discounts a drawn building
        // exactly as it discounts a placed one.
        int cm = Mathf.RoundToInt(ColonyManager.DiscCost(info.costMetal) * mult);
        int ce = Mathf.RoundToInt(ColonyManager.DiscCost(info.costEnergy) * mult);
        if (!GameMode.DevMode && !PlayerEconomy.Spend(cm, ce))
        {
            why = $"not enough resources — {cm} metal and {ce} energy for {tiles} tiles";
            return null;
        }

        var job = new SurfaceBuildJob
        {
            type = t,
            mergeInto = mergeInto,
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

    // ============================================================================================
    // WHAT A JOB IS ACTUALLY GETTING, RIGHT NOW
    //
    // Tick hands Labor out from the top of the queue, so a job's real rate depends on every job ABOVE
    // it — which is exactly the thing a player staring at a crawling build cannot work out for
    // themselves. These two replay that allocation so the Build tab can say "3 of 5 Labor, ~2m left"
    // instead of showing a bar that mysteriously moves at a different speed for each row.
    //
    // Deliberately a replay of Tick's loop rather than a number cached during it: a cached one would be
    // a frame stale at best, and simply wrong on the frame a job above this one is cancelled or paused —
    // the two moments the player is most likely to be looking.
    // ============================================================================================

    /// Labor this job is receiving this instant, given everything ahead of it in the queue.
    public static float LaborGranted(CelestialBody b, SurfaceBuildJob job)
    {
        var list = Peek(b);
        if (list == null || job == null || job.paused) return 0f;

        float free = SurfaceLabor.Max(b);
        foreach (var other in list)
        {
            if (other == null || other.paused) continue;
            float granted = Mathf.Min(other.labor, Mathf.Max(0f, free));
            free -= granted;
            if (other == job) return granted;
        }
        return 0f;
    }

    /// Seconds of game clock until this job completes at its CURRENT rate. Infinity for a paused job —
    /// it is not slow, it is stopped, and quoting it a finishing time would be a lie.
    public static float Eta(CelestialBody b, SurfaceBuildJob job)
    {
        if (job == null) return 0f;
        if (job.paused) return float.PositiveInfinity;
        return job.Remaining * BuildScaling.TimeFactorFor(job.labor, LaborGranted(b, job));
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
        var list = Peek(b);
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
        string name = SurfaceBuildingDatabase.Get(job.type)?.name ?? "Structure";

        // ---- MERGE FIRST, PLACE SECOND ----
        //
        // If the finished cells touch a standing building of the same type, they join it rather than
        // becoming a second one beside it (see SurfaceBuildManager.AbsorbInto for why that is a rule and
        // not a convenience). Asked HERE, at completion, rather than trusted from queue time: the
        // building the player was extending may have been demolished, or flattened by an earthquake,
        // while its own extension was still going up.
        //
        // Note that this runs whether or not the player DELIBERATELY extended something. Drawing a farm
        // that happens to touch another farm produces one farm either way — the map shows one field, so
        // the data has to say one field. `mergeInto` only records which building they aimed at, which
        // decides the survivor when several are touching.
        if (SurfaceBuildManager.CanMerge(job.type))
        {
            var absorbed = SurfaceBuildManager.AbsorbInto(b, job.type, job.cells, job.mergeInto);
            if (absorbed != null)
            {
                SurfaceLabor.Invalidate();
                NotificationManager.Instance?.Push($"{name} extended",
                    $"{job.Tiles} tiles added on {b.name} — now {absorbed.TileCount} tiles, " +
                    $"{absorbed.efficiency * 100f:F0}% sited.", null, NotifKind.Info);
                return;
            }
        }

        // Placed free: the cost was taken when the job was queued. Paying again at completion would
        // charge twice for one building, which is the kind of thing nobody notices until an economy is
        // mysteriously tight.
        var placed = SurfaceBuildManager.PlaceDrawn(b, job.type, job.cells);
        SurfaceLabor.Invalidate();

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
