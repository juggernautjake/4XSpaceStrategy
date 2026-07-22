using System;
using System.Collections.Generic;
using UnityEngine;

// Why a queued ship isn't currently being worked on — drives the label on its queue widget.
public enum BuildState { Building, Paused, WaitingForPower, Impossible }

// One ship in the build queue. Several can be Building at once: each occupies its hull's build power
// for as long as it is under construction and releases it the moment it rolls out.
public class BuildOrder
{
    public int id;
    public UnitType type;
    public float elapsed, duration;
    public bool paused;

    // What was actually paid for this hull, so cancelling refunds exactly that (costs shift as Industry
    // technologies land, so re-deriving the price at cancel time would refund the wrong amount).
    public int metalPaid, energyPaid;

    // Set by the scheduler each tick; the UI reads it rather than recomputing the allocation.
    public BuildState state = BuildState.WaitingForPower;
    public bool Active => state == BuildState.Building;

    public int Power => UnitDatabase.Get(type).buildPower;
    public float Progress => duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
    public float Remaining => Mathf.Max(0f, duration - elapsed);
}

// Owns all ships: creation/building (with a timed queue), movement (travel time driven by the
// slowest ship), exploration + limited research by scouts/research ships, and colony-ship claiming
// with objectives. Also awards experience so ships rank up toward Legendary.
public class UnitManager : MonoBehaviour
{
    public static UnitManager Instance;

    readonly List<Unit> units = new List<Unit>();
    readonly List<BuildOrder> buildQueue = new List<BuildOrder>();
    int nextId = 1;
    int nextOrderId = 1;

    public IReadOnlyList<BuildOrder> BuildQueue => buildQueue;
    public event Action OnBuildChanged;

    public CelestialBody HomePlanet { get; private set; }

    public event Action OnUnitsChanged;

    const float SpeedScale = 6f;
    const float HostileStaySeconds = 45f;   // limited stay on <80% worlds before forced return

    // Active hyper-relays / relay stations quicken travel fleet-wide: the effective speed scale is
    // multiplied by ShipUpgrades.SpeedMult (1 = no relays).
    static float EffSpeedScale() => SpeedScale * Mathf.Max(1f, ShipUpgrades.SpeedMult);

    // XP is earned by COMPLETING tasks (not by idling). Travel gives a little; missions give more.
    const float XpTravel = 8f, XpSample = 5f, XpSurvey = 30f, XpResearch = 45f, XpColonize = 60f;

    // A colony ship can found a city on a world at least this habitable for the current species.
    public const float ColonizeMinHabitability = 45f;

    public static void Create()
    {
        if (Instance != null) return;
        new GameObject("UnitManager").AddComponent<UnitManager>();
    }

    void Awake() { Instance = this; }

    public IReadOnlyList<Unit> Units => units;

    /// Something outside this class changed where the fleet is, or what is in it.
    ///
    /// C# events can only be raised from the type that declares them, and the renderers rebuild off this
    /// one — so a caller that re-homes ships (GalaxyTrash, evicting a fleet off a world it is deleting)
    /// has no way to tell them without it.
    public void NotifyUnitsChanged() => OnUnitsChanged?.Invoke();

    // ---- New game setup ----
    public void NewGame(CelestialBody homePlanet)
    {
        units.Clear();
        buildQueue.Clear();
        ControlGroups.Clear();
        nextId = 1;
        nextOrderId = 1;
        HomePlanet = homePlanet;
        ShipUpgrades.Reset();
        StationEffects.Reset();

        // Starting fleet: two scouts + one colony ship.
        CreateUnit(UnitType.Scout, FactionManager.Player, homePlanet);
        CreateUnit(UnitType.Scout, FactionManager.Player, homePlanet);
        CreateUnit(UnitType.ColonyShip, FactionManager.Player, homePlanet);

        OnUnitsChanged?.Invoke();
    }

    public Unit CreateUnit(UnitType type, Faction owner, CelestialBody at)
    {
        var u = new Unit { id = nextId++, type = type, owner = owner, location = at };
        u.name = $"{UnitDatabase.Get(type).name} {u.id}";
        units.Add(u);
        if (at != null) { if (at.units == null) at.units = new List<Unit>(); at.units.Add(u); }
        OnUnitsChanged?.Invoke();
        return u;
    }

    // ---- Building (timed queue) ----
    public void NotifyBuildChanged() => OnBuildChanged?.Invoke();

    // Whether the player's shipyards are advanced enough to build this class right now.
    public bool CanBuildShip(UnitType type, out string reason)
    {
        reason = null;
        var info = UnitDatabase.Get(type);
        int level = GameMode.DevMode ? Colony.MaxShipyardLevel : Colony.PlayerMaxShipyardLevel();
        if (level < info.minShipyardLevel)
        {
            reason = $"needs a level-{info.minShipyardLevel} shipyard (yours: {Colony.PlayerMaxShipyardLevel()})";
            return false;
        }
        if (info.isProbe && !GameMode.DevMode && !EmpireTech.ProbesUnlocked)
        {
            reason = "reach Empire Tech Level 2 to build probes";
            return false;
        }
        if (!GameMode.DevMode && EmpireTech.Level < info.minEmpireLevel)
        {
            string what = info.isStation ? "deploy this station" : "build this class";
            reason = $"reach Empire Tech Level {info.minEmpireLevel} to {what} (yours: {EmpireTech.Level})";
            return false;
        }
        // Build power gates SIZE, not availability: your yards must be able to hold the hull on the
        // stocks at all. Upgrade a shipyard or research the slipway technologies to make room.
        int power = BuildPower.PlayerTotal();
        if (info.buildPower > power)
        { reason = $"needs {info.buildPower} build power (your yards total {power})"; return false; }
        int cm = ColonyManager.DiscCost(info.costMetal), ce = ColonyManager.DiscCost(info.costEnergy);
        if (!GameMode.DevMode && !PlayerEconomy.CanAfford(cm, ce))
        { reason = $"need {cm} metal, {ce} energy"; return false; }
        return true;
    }

    public bool QueueBuild(UnitType type)
    {
        var info = UnitDatabase.Get(type);
        if (!CanBuildShip(type, out _)) return false;
        int cm = ColonyManager.DiscCost(info.costMetal), ce = ColonyManager.DiscCost(info.costEnergy);
        if (!GameMode.DevMode && !PlayerEconomy.Spend(cm, ce)) return false;  // free in Dev Mode
        // Higher-tier shipyards build faster (~15% per level above 1); Industry tech cuts time further.
        int level = Mathf.Max(1, Colony.PlayerMaxShipyardLevel());
        float speed = 1f + 0.15f * (level - 1);
        buildQueue.Add(new BuildOrder
        {
            id = nextOrderId++,
            type = type,
            duration = info.buildTime * TechEffects.BuildTimeMult / speed,
            metalPaid = GameMode.DevMode ? 0 : cm,
            energyPaid = GameMode.DevMode ? 0 : ce
        });
        Schedule();
        OnBuildChanged?.Invoke();
        return true;
    }

    // ---- Queue editing ----
    // Pausing freezes a hull's progress bar where it is and hands its build power to the next ship in
    // line; resuming picks up exactly where it left off (once power is free again).
    public void SetOrderPaused(BuildOrder o, bool paused)
    {
        if (o == null || o.paused == paused) return;
        o.paused = paused;
        Schedule();
        OnBuildChanged?.Invoke();
    }

    // Cancel a queued or half-built ship. Its resources come straight back — an unfinished hull is
    // stripped for parts, so nothing is lost but the time.
    public void CancelOrder(BuildOrder o)
    {
        if (o == null || !buildQueue.Remove(o)) return;
        if (o.metalPaid > 0) PlayerEconomy.Add(ResourceType.Metal, o.metalPaid);
        if (o.energyPaid > 0) PlayerEconomy.Add(ResourceType.Energy, o.energyPaid);
        Schedule();
        OnBuildChanged?.Invoke();
    }

    // Drag-to-reorder. Moving a ship up the queue can start it immediately (and stall whatever was
    // using its power), which is the whole point of being able to reprioritize.
    public void MoveOrder(int from, int to)
    {
        if (from < 0 || from >= buildQueue.Count) return;
        to = Mathf.Clamp(to, 0, buildQueue.Count - 1);
        if (from == to) return;
        var o = buildQueue[from];
        buildQueue.RemoveAt(from);
        buildQueue.Insert(to, o);
        Schedule();
        OnBuildChanged?.Invoke();
    }

    public int IndexOfOrder(BuildOrder o) => buildQueue.IndexOf(o);

    // ---- Build power ----
    public int TotalBuildPower => BuildPower.PlayerTotal();

    public int UsedBuildPower
    {
        get { int n = 0; foreach (var o in buildQueue) if (o.Active) n += o.Power; return n; }
    }

    public int FreeBuildPower => Mathf.Max(0, TotalBuildPower - UsedBuildPower);

    // Decide which ships are being worked on right now. Walks the queue IN ORDER and gives each ship the
    // power it needs; the first ship that doesn't fit stops the walk, so a big hull waiting on power
    // holds its place rather than being leapfrogged by cheaper ships behind it. Two exceptions keep the
    // queue from deadlocking: paused ships are skipped (that's what pausing is for), and a hull that
    // costs more than your entire empire could ever supply is skipped rather than blocking forever.
    void Schedule()
    {
        int total = TotalBuildPower;
        int free = total;
        bool blocked = false;

        foreach (var o in buildQueue)
        {
            if (o.paused) { o.state = BuildState.Paused; continue; }
            if (o.Power > total) { o.state = BuildState.Impossible; continue; }
            if (blocked) { o.state = BuildState.WaitingForPower; continue; }
            if (o.Power <= free) { o.state = BuildState.Building; free -= o.Power; }
            else { o.state = BuildState.WaitingForPower; blocked = true; }
        }
    }

    // Ships roll out at your most advanced shipyard (falling back to the home world).
    CelestialBody BestShipyardBody()
    {
        CelestialBody best = HomePlanet; int bestLevel = HomePlanet != null ? HomePlanet.shipyardLevel : 0;
        if (SystemContext.Galaxy != null)
            foreach (var b in SystemContext.AllBodies())
                if (b.owner == FactionManager.Player && b.shipyardLevel > bestLevel) { best = b; bestLevel = b.shipyardLevel; }
        return best != null ? best : HomePlanet;
    }

    // The first ship actually under construction (for compact readouts), or null.
    public BuildOrder CurrentBuild
    {
        get { foreach (var o in buildQueue) if (o.Active) return o; return null; }
    }

    void AdvanceBuild(float dt)
    {
        if (buildQueue.Count == 0) return;

        // Re-run the allocation every tick: shipyards can be built, upgraded, lost or gained at any time,
        // and every completion frees power that the ships behind it should pick up immediately.
        Schedule();

        bool completed = false;
        for (int i = buildQueue.Count - 1; i >= 0; i--)
        {
            var o = buildQueue[i];
            if (!o.Active) continue;               // paused, waiting for power, or unbuildable: frozen
            o.elapsed += dt;
            if (o.elapsed < o.duration) continue;

            buildQueue.RemoveAt(i);
            var yard = BestShipyardBody();
            CreateUnit(o.type, FactionManager.Player, yard);
            SimpleAudio.Instance?.PlayNotify(NotifKind.Info);
            NotificationManager.Instance?.Push($"{UnitDatabase.Get(o.type).name} built",
                $"Ready at {(yard != null ? yard.name : "home")}. {o.Power} build power freed.", null, NotifKind.Info);
            completed = true;
        }

        if (completed) { Schedule(); OnBuildChanged?.Invoke(); }   // freed power starts the next ships now
    }

    // ---- Movement ----
    public void SendUnits(List<Unit> group, CelestialBody target)
    {
        if (target == null || group == null || group.Count == 0) return;

        int slow = int.MaxValue;
        foreach (var u in group) slow = Mathf.Min(slow, Mathf.Max(1, u.Speed));

        Vector3 from = UnitPos(group[0]);

        // Intercept trajectory: solve for where a moving target will be when the fleet arrives, so the
        // ships fly a straight line to that predicted point rather than chasing the live position.
        var oc = target.visualObject != null ? target.visualObject.GetComponent<OrbitController>() : null;
        Vector3 to; float duration;
        if (oc != null)
        {
            float t = Mathf.Clamp(Vector3.Distance(from, WorldPos(target)) / (slow * EffSpeedScale()), 3f, 120f);
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = oc.PredictWorldPosition(t);
                t = Mathf.Clamp(Vector3.Distance(from, p) / (slow * EffSpeedScale()), 3f, 120f);
            }
            to = oc.PredictWorldPosition(t);
            duration = t;
        }
        else
        {
            to = WorldPos(target);
            duration = Mathf.Clamp(Vector3.Distance(from, to) / (slow * EffSpeedScale()), 3f, 120f);
        }

        if (!CanReach(group, to, out string reason))
        {
            // Drop the impossible order so it doesn't retry every frame, and say why once.
            foreach (var u in group) { if (u.orders.Count > 0) u.orders.RemoveAt(0); u.status = UnitStatus.Idle; }
            NotificationManager.Instance?.Push($"Out of range — {target.name}", reason, null, NotifKind.Danger);
            OnUnitsChanged?.Invoke();
            return;
        }

        foreach (var u in group)
        {
            if (u.location != null && u.location.units != null) u.location.units.Remove(u);
            u.location = null;
            u.status = UnitStatus.Traveling;
            u.travelTarget = target;
            u.travelFrom = from;
            u.travelTo = to;
            u.travelElapsed = 0f;
            u.travelDuration = duration;
            u.missionTimer = 0f;
        }
        OnUnitsChanged?.Invoke();
    }

    public void SendUnitsHome(List<Unit> group) => IssueMove(group, HomePlanet, false);

    // ---- Self-destruct / scrap ----
    // Scrapping is only allowed on a world your faction has fully claimed or well established.
    public bool CanScrap(Unit u)
        => u != null && u.location != null &&
           (u.location.owner == FactionManager.Player || u.location.claimProgress >= 0.5f);

    public void DestroyUnit(Unit u, bool scrap)
    {
        if (u == null) return;

        Vector3 pos = u.location != null && u.location.visualObject != null
            ? u.location.visualObject.transform.position + Vector3.up * 1.2f
            : Vector3.Lerp(u.travelFrom, u.travelTo, u.TravelProgress);

        UnitTokenRenderer.Instance?.FlashDestroy(pos);
        SimpleAudio.Instance?.PlayUnitDestroyed();

        if (scrap && CanScrap(u))
        {
            float m = u.Info.costMetal * UnityEngine.Random.Range(0.2f, 0.3f);   // recover 20-30% of build cost
            float e = u.Info.costEnergy * UnityEngine.Random.Range(0.2f, 0.3f);
            PlayerEconomy.Add(ResourceType.Metal, m);
            PlayerEconomy.Add(ResourceType.Energy, e);
            NotificationManager.Instance?.Push($"{u.name} scrapped",
                $"Recovered {m:F0} metal and {e:F0} energy.", null, NotifKind.Info);
        }
        else
        {
            NotificationManager.Instance?.Push($"{u.name} self-destructed", "", null, NotifKind.Danger);
        }

        if (u.location != null && u.location.units != null) u.location.units.Remove(u);
        units.Remove(u);
        if (UnitSelection.IsSelected(u)) UnitSelection.Clear();
        OnUnitsChanged?.Invoke();
    }

    /// Where a body IS in the world right now.
    ///
    /// Public because callers outside this class legitimately need it: the Send-to list measures how far
    /// each destination is, and GalaxyTrash reads a world's last position to park its ships when it is
    /// deleted. Both want the same answer this class already computes, and a second copy of "where is
    /// that body" would drift from this one the first time the fallback chain changed.
    public Vector3 WorldPos(CelestialBody b)
    {
        if (b == null) return Vector3.zero;
        if (b.visualObject != null) return b.visualObject.transform.position;
        if (b.system != null) return b.system.galaxyPosition;
        return Vector3.zero;
    }

    // A unit's current world position (at a body, parked in space, or mid-transit).
    public Vector3 UnitPos(Unit u)
    {
        if (u == null) return Vector3.zero;
        if (u.location != null) return WorldPos(u.location);
        if (u.status == UnitStatus.Traveling) return Vector3.Lerp(u.travelFrom, u.travelTo, u.TravelProgress);
        if (u.inSpace) return u.parkPosition;
        return Vector3.zero;
    }

    // Send a fleet to a point in empty space (not a body).
    public void SendUnitsToPoint(List<Unit> group, Vector3 worldPos)
    {
        if (group == null || group.Count == 0) return;
        int slow = int.MaxValue;
        foreach (var u in group) slow = Mathf.Min(slow, Mathf.Max(1, u.Speed));
        Vector3 from = UnitPos(group[0]);
        float duration = Mathf.Clamp(Vector3.Distance(from, worldPos) / (slow * EffSpeedScale()), 3f, 120f);

        if (!CanReach(group, worldPos, out string reason))
        {
            foreach (var u in group) { if (u.orders.Count > 0) u.orders.RemoveAt(0); u.status = UnitStatus.Idle; }
            NotificationManager.Instance?.Push("Out of range", reason, null, NotifKind.Danger);
            OnUnitsChanged?.Invoke();
            return;
        }

        foreach (var u in group)
        {
            if (u.location != null && u.location.units != null) u.location.units.Remove(u);
            u.location = null;
            u.inSpace = false;
            u.travelTarget = null;   // no body -> pure space move
            u.status = UnitStatus.Traveling;
            u.travelFrom = from;
            u.travelTo = worldPos;
            u.travelElapsed = 0f;
            u.travelDuration = duration;
            u.missionTimer = 0f;
        }
        OnUnitsChanged?.Invoke();
    }

    // ---- Travel range ----
    // A ship's reach in world units, scaled by empire drive/relay upgrades. 0 base range = unlimited
    // (probes). Early game this keeps most ships inside their home system; better drives and relays
    // extend it until colony/research ships — and eventually stations — can cross to other systems.
    public float EffectiveRange(Unit u)
        => (u == null || u.Info.range <= 0) ? float.MaxValue : u.Info.range * Mathf.Max(0.1f, ShipUpgrades.RangeMult);

    // A fleet is limited by its SHORTEST-ranged ship.
    public float GroupRange(List<Unit> group)
    {
        float r = float.MaxValue;
        if (group != null) foreach (var u in group) r = Mathf.Min(r, EffectiveRange(u));
        return r;
    }

    public bool CanReach(List<Unit> group, Vector3 targetPos, out string reason)
    {
        reason = null;
        if (group == null || group.Count == 0) return true;
        float range = GroupRange(group);
        if (range >= float.MaxValue) return true;              // all-unlimited (e.g. probes)
        float dist = Vector3.Distance(UnitPos(group[0]), targetPos);
        if (dist > range)
        {
            reason = $"{dist:F0} units away · fleet range {range:F0}. Upgrade drives or build a relay to reach it.";
            return false;
        }
        return true;
    }

    public bool CanReachBody(List<Unit> group, CelestialBody body, out string reason)
    {
        reason = null;
        return body == null || CanReach(group, WorldPos(body), out reason);
    }

    // ---- Simulation ----
    const float ProbePulsePeriod = 2.5f;   // seconds between probe sensor pulses
    const float ProbeLifetime = 90f;       // a probe eventually loses power / signal

    void Update()
    {
        float dt = Time.deltaTime;   // scaled by game speed
        if (dt <= 0f) return;

        AdvanceBuild(dt);

        bool changed = false;

        for (int i = 0; i < units.Count; i++)
        {
            var u = units[i];
            u.serviceTime += dt;   // record only; XP now comes from completing tasks, not idling

            switch (u.status)
            {
                case UnitStatus.Traveling: changed |= Travel(u, dt); break;
                case UnitStatus.Exploring: Explore(u, dt); break;
                case UnitStatus.Researching: Research(u, dt); break;
                case UnitStatus.Colonizing: changed |= Colonize(u, dt); break;
                case UnitStatus.Idle:
                    if (!u.queuePaused && u.orders.Count > 0) StartNext(u);
                    break;
            }
        }

        if (changed) OnUnitsChanged?.Invoke();
    }

    // ---- Order queue (interruptible + queueable) ----
    // Issue a move; queue=false interrupts and replaces, queue=true appends to the end.
    public void IssueMove(List<Unit> group, CelestialBody target, bool queue)
        => Issue(group, () => new ShipOrder { kind = OrderKind.Move, target = target }, queue);

    public void IssueMovePoint(List<Unit> group, Vector3 pt, bool queue)
        => Issue(group, () => new ShipOrder { kind = OrderKind.Move, isPoint = true, point = pt }, queue);

    public void IssueAction(List<Unit> group, OrderKind kind, CelestialBody target, bool queue)
        => Issue(group, () => new ShipOrder { kind = kind, target = target }, queue);

    void Issue(List<Unit> group, System.Func<ShipOrder> make, bool queue)
    {
        if (group == null) return;
        foreach (var u in group)
        {
            if (u == null) continue;
            if (!queue) { u.orders.Clear(); StopActivity(u); }
            u.orders.Add(make());
            u.queuePaused = false;
            if (u.status == UnitStatus.Idle) StartNext(u);
        }
        OnUnitsChanged?.Invoke();
    }

    // Fully stop a ship: clears the queue and any active travel/action.
    public void StopAll(Unit u)
    {
        if (u == null) return;
        u.orders.Clear();
        StopActivity(u);
        u.queuePaused = false;
        OnUnitsChanged?.Invoke();
    }

    // Pause/resume the queue (an in-progress action is suspended, keeping its progress).
    public void SetPaused(Unit u, bool paused)
    {
        if (u == null) return;
        u.queuePaused = paused;
        if (paused)
        {
            // Suspend an active action (survey/research/colonize) but keep progress; travel continues.
            if (u.status == UnitStatus.Exploring || u.status == UnitStatus.Researching || u.status == UnitStatus.Colonizing)
                u.status = UnitStatus.Idle;
        }
        else if (u.status == UnitStatus.Idle) StartNext(u);
        OnUnitsChanged?.Invoke();
    }

    // Remove a specific order from the queue (index 0 = the active one).
    public void RemoveOrder(Unit u, int index)
    {
        if (u == null || index < 0 || index >= u.orders.Count) return;
        if (index == 0) { SkipCurrent(u); return; }
        u.orders.RemoveAt(index);
        OnUnitsChanged?.Invoke();
    }

    // Halt just the current order (skip to the next queued one).
    public void SkipCurrent(Unit u)
    {
        if (u == null) return;
        StopActivity(u);
        if (u.orders.Count > 0) u.orders.RemoveAt(0);
        if (!u.queuePaused) StartNext(u);
        OnUnitsChanged?.Invoke();
    }

    // Stop travel/action without touching the queue; parks mid-transit ships in space.
    void StopActivity(Unit u)
    {
        if (u.status == UnitStatus.Traveling && u.location == null)
        {
            u.parkPosition = Vector3.Lerp(u.travelFrom, u.travelTo, u.TravelProgress);
            u.inSpace = true;
        }
        u.travelTarget = null;
        if (u.status != UnitStatus.Idle) u.status = UnitStatus.Idle;
    }

    // Begin the front order (travelling first if we're not there yet).
    void StartNext(Unit u)
    {
        if (u.queuePaused || u.orders.Count == 0) { u.status = UnitStatus.Idle; return; }
        var o = u.orders[0];

        if (o.isPoint)
        {
            if (u.status != UnitStatus.Traveling) SendUnitsToPoint(new List<Unit> { u }, o.point);
            return;
        }
        if (o.target == null) { u.orders.RemoveAt(0); StartNext(u); return; }
        if (u.location != o.target)
        {
            if (u.status != UnitStatus.Traveling) SendUnits(new List<Unit> { u }, o.target);
            return;
        }
        BeginAction(u, o);
    }

    void BeginAction(Unit u, ShipOrder o)
    {
        var b = u.location;
        switch (o.kind)
        {
            case OrderKind.Move:
                u.orders.RemoveAt(0);
                if (u.orders.Count > 0) StartNext(u);
                else AutoAct(u, b);   // sensible default on arrival
                break;
            case OrderKind.Survey:
                if (b != null && u.Info.canExplore && !b.Surveyed) u.status = UnitStatus.Exploring;
                else FinishAction(u, OrderKind.Survey, b);
                break;
            case OrderKind.Research:
                if (b != null && u.Info.canResearch && b.Surveyed)
                {
                    // Fresh clock per world. researchTimer is only zeroed on COMPLETION, so a ship
                    // pulled off a half-finished deep survey used to arrive at the next world already
                    // 60% done — and that world's progress bar jumped to 60% on the first frame.
                    u.researchTimer = 0f;
                    b.researchProgress = 0f;
                    u.status = UnitStatus.Researching;
                }
                else
                {
                    if (b != null && !b.Surveyed)
                        NotificationManager.Instance?.Push($"{u.name} can't start Deep Research yet",
                            $"{b.name} must be surveyed first.", null, NotifKind.Info);
                    FinishAction(u, OrderKind.Research, b);
                }
                break;
            case OrderKind.Colonize:
                if (b != null && u.Info.canColonize && b.owner != FactionManager.Player)
                { u.status = UnitStatus.Colonizing; b.claimingFaction = FactionManager.Player; }
                else FinishAction(u, OrderKind.Colonize, b);
                break;
            case OrderKind.Terraform:
                // A terraformer starts the project then idles here — staying present keeps it running
                // and doubles its pace (see ColonyManager.TickTerraform).
                if (b != null && u.Info.canTerraform)
                {
                    if (!b.terraforming && ColonyManager.Instance != null && !ColonyManager.Instance.ToggleTerraform(b))
                    { /* couldn't start (already at ceiling / not terraformable) — message already shown */ }
                    else
                        NotificationManager.Instance?.Push($"{u.name} is terraforming {b.name}",
                            "The terraformer will keep the project running (and twice as fast) while it stays here.", FlyTo(b), NotifKind.Info);
                }
                FinishAction(u, OrderKind.Terraform, b);
                break;
        }
    }

    // Default behaviour when a ship simply arrives (a plain Move): colonize > survey > research.
    void AutoAct(Unit u, CelestialBody b)
    {
        if (b == null) { u.status = UnitStatus.Idle; return; }
        if (u.Info.canColonize && b.owner != FactionManager.Player)
        { u.status = UnitStatus.Colonizing; b.claimingFaction = FactionManager.Player; }
        else if (u.Info.canExplore && !b.Surveyed) u.status = UnitStatus.Exploring;
        else if (u.Info.canResearch && b.Surveyed) u.status = UnitStatus.Researching;
        else u.status = UnitStatus.Idle;
    }

    // Pop the active order if it matches (so auto actions, which aren't a queued order, don't pop).
    void FinishAction(Unit u, OrderKind kind, CelestialBody b)
    {
        if (u.orders.Count > 0 && u.orders[0].kind == kind && u.orders[0].target == b)
            u.orders.RemoveAt(0);
        u.status = UnitStatus.Idle;
    }

    // Fly the camera to a body and lock onto it (used by completion notifications).
    System.Action FlyTo(CelestialBody b) => () =>
    {
        if (b != null && b.visualObject != null)
            CameraController.Instance?.FocusAndZoom(b.visualObject.transform, b.surfaceSize, true);
        PlanetUI.Instance?.Show(b);
    };

    // A launched probe pulses on a fixed cadence: it reveals every object within its sensor range and
    // relays a report home. It runs on starlight (no fuel) but eventually loses power and goes dark.
    void ProbeScan(Unit u, float dt)
    {
        u.missionTimer += dt;
        if (u.serviceTime > ProbeLifetime) { ProbeLoseSignal(u, "drifted beyond contact range"); return; }
        if (u.missionTimer < ProbePulsePeriod) return;
        u.missionTimer = 0f;

        Vector3 pos = UnitPos(u);
        float vis = u.Info.visionRange;
        int found = 0; string firstName = null;
        foreach (var b in SystemContext.AllBodies())
        {
            if (b == null || b.visited) continue;
            if (Vector3.Distance(pos, WorldPos(b)) <= vis)
            {
                b.visited = true; u.worldsExplored++; found++;
                if (firstName == null) firstName = b.name;
            }
        }
        if (found > 0)
        {
            SimpleAudio.Instance?.PlayNotify(NotifKind.Discovery);
            string msg = found == 1 ? $"detected {firstName}" : $"detected {firstName} and {found - 1} more object(s)";
            NotificationManager.Instance?.Push($"Probe pulse — {u.name}", $"Sensors {msg}. Data relayed home.", null, NotifKind.Discovery);
        }
    }

    void ProbeLoseSignal(Unit u, string why)
    {
        int seen = u.worldsExplored;
        u.status = UnitStatus.Idle;   // so Travel() stops processing this now-removed probe
        if (u.location != null && u.location.units != null) u.location.units.Remove(u);
        units.Remove(u);
        if (UnitSelection.IsSelected(u)) UnitSelection.Clear();
        SimpleAudio.Instance?.PlayNotify(NotifKind.Info);
        NotificationManager.Instance?.Push($"Signal lost — {u.name}",
            $"The probe {why} and went dark. It revealed {seen} object(s) before contact was lost.", null, NotifKind.Info);
        OnUnitsChanged?.Invoke();
    }

    bool Travel(Unit u, float dt)
    {
        if (u.Info.isProbe) { ProbeScan(u, dt); if (u.status != UnitStatus.Traveling) return true; }

        u.travelElapsed += dt;
        if (u.travelElapsed < u.travelDuration) return false;

        // A probe that reaches the end of its trajectory goes dark rather than "arriving".
        if (u.Info.isProbe) { ProbeLoseSignal(u, "reached the end of its trajectory"); return true; }

        // Arrive.
        var dest = u.travelTarget;
        u.travelTarget = null;

        if (dest == null)
        {
            // Reached a point in empty space — hold position.
            u.location = null;
            u.inSpace = true;
            u.parkPosition = u.travelTo;
            u.status = UnitStatus.Idle;
            if (u.orders.Count > 0 && u.orders[0].isPoint) u.orders.RemoveAt(0);
            NotificationManager.Instance?.Push($"{u.name} reached its destination", "Holding position in space.", null, NotifKind.Info);
            return true;
        }

        u.location = dest;
        u.inSpace = false;
        if (dest.units == null) dest.units = new List<Unit>();
        dest.units.Add(u);
        u.worldsExplored++;
        u.AddExperience(XpTravel);           // a little XP for completing a journey
        dest.visited = true;                 // arrival reveals the low-res mini map

        // Go idle; the order queue (or the class default via AutoAct) decides what to do here.
        u.status = UnitStatus.Idle;
        if (!u.queuePaused) StartNext(u);

        NotificationManager.Instance?.Push($"{u.name} arrived at {dest.name}",
            $"{u.Info.name} · {u.RankName}", FlyTo(dest), NotifKind.Info);
        return true;
    }

    // Survey: ONE order maps the whole world.
    //
    // It used to stop at a per-hull cap (surveyDepth) — a Scout reached 55% and the order popped itself,
    // so finishing a world meant clicking Survey two or three times. That reads as a broken button, not
    // as a ship limitation. Hull quality now buys SPEED instead (UnitInfo.surveyRate): a scout finishes
    // in a fraction of the time a colony ship takes, and both finish.
    //
    // This is the SURFACE pass — the map, the terrain, the mineral seams you can see from orbit, and the
    // points of interest: ruins, wrecks, anomalies, somebody else's settlements. What it cannot tell you
    // is what any of it MEANS. That takes a research ship on the ground — see DeepSurvey.
    //
    // Surveying only DISCOVERS ores (and collects a sample to carry); it never researches them. That
    // happens at a research ship, station or centre, and it costs research points.
    void Explore(Unit u, float dt)
    {
        var b = u.location;
        if (b == null) { u.status = UnitStatus.Idle; return; }
        if (b.Surveyed) { FinishAction(u, OrderKind.Survey, b); return; }   // already done

        float before = b.explorationProgress;
        float sizeFactor = Mathf.Max(0.5f, b.surfaceSize / 8f);                                   // bigger = slower
        float hostility = Mathf.Lerp(1f, 2.2f, Mathf.Clamp01((100f - b.habitability) / 100f));    // less habitable = slower
        b.explorationProgress = Mathf.Clamp01(
            b.explorationProgress + 0.05f * u.Info.surveyRate / (sizeFactor * hostility) * dt);

        // Collect an ore SAMPLE at each survey milestone (discovered + carried; not researched here).
        if (Crossed(before, b.explorationProgress, 0.25f) || Crossed(before, b.explorationProgress, 0.5f) ||
            Crossed(before, b.explorationProgress, 0.75f) || Crossed(before, b.explorationProgress, 1f))
        {
            foreach (var ore in OreGenerator.OresOnBody(b))
            {
                if (!ResearchManager.IsDiscovered(ore)) { ResearchManager.Discover(ore); u.samples.Add((int)ore); u.AddExperience(XpSample); break; }
            }
        }

        if (b.explorationProgress >= 1f)
        {
            u.AddExperience(XpSurvey);
            SimpleAudio.Instance?.PlayNotify(NotifKind.Research);

            int sites = b.pointsOfInterest != null ? b.pointsOfInterest.Count : 0;
            string found = sites > 0
                ? $" {sites} point(s) of interest charted — see the Survey tab."
                : " Nothing of note on the surface.";
            string deeper = u.Info.canResearch
                ? " This ship can also run DEEP RESEARCH here — the first of three studies, each reading what the surface map can only point at."
                : " A research ship could run Deep Research here for far more than the surface shows.";

            NotificationManager.Instance?.Push($"{b.name} surveyed", "Survey complete — map revealed.",
                FlyTo(b), NotifKind.Research,
                detail: $"{u.name} finished surveying {b.name}. Its detailed map and points of interest are now available.{found}{deeper}" +
                (u.samples.Count > 0 ? $" {u.name} is carrying {u.samples.Count} ore sample(s) — take them to a world with a research centre to have them analysed." : ""));
            RevealBodyVisual(b);
            AncientLore.SurveyBody(b);   // announce any ancient ruins the surface pass turned up
            FinishAction(u, OrderKind.Survey, b);
            return;
        }

        // NO hostile-world recall while a survey is running.
        //
        // There used to be one here: after HostileStaySeconds the ship was force-sent home. It quietly
        // undid the whole point of this rework — a large or very hostile world takes longer than the
        // timer, so the ship was yanked at ~56% and the player had to click Survey again, which is the
        // exact behaviour the per-pass cap was removed to eliminate. Worse, it called StopAll, so a
        // player who had queued five worlds lost all five orders, not just this one.
        //
        // Hostility already has its say: it divides the survey rate above, so a hostile world simply
        // takes longer to map. That is the same statement — "this is a nasty place to work" — expressed
        // as a cost rather than as a failure.
    }

    // THE DEEP SURVEY. Research ships only, and only on a world that has already been surveyed.
    //
    // The surface pass tells you a world HAS ruins. This is landing on them. It runs at roughly a third
    // the speed of a surface survey on purpose — it is the long, expensive, second look — and it pays
    // out accordingly: every ore on the world analysed rather than the handful the orbital pass sampled,
    // every point of interest excavated, precursor schematics recovered from the ruins, any Vael
    // fragment the world was hiding, and the Heat / Fertile / Weather indexes that make siting buildings
    // possible at all.
    //
    // DeepSurveyRate is a fraction of Explore's 0.05f base. A big or hostile world divides it further,
    // exactly as the surface pass does, so the worlds most worth studying are also the slowest.
    //
    // Tuned WITH the quality factor below, not before it. An earlier version multiplied by
    // (EffectiveResearch + 1) — a base research ship's 8 — which made the "slower, deeper" pass finish
    // in a third of the time of the surface survey it is supposed to follow. A veteran Science Vessel
    // did it in under three seconds.
    const float DeepSurveyRate = 0.009f;

    // A better research suite, and a more experienced crew, buy time back — but on a curve that keeps
    // the deep survey a real commitment at every tier. Base research ship ~1.3x, Mk III ~1.8x, a
    // legendary Science Vessel ~2.5x. Never the order-of-magnitude the raw stat gave.
    static float DeepSurveyQuality(Unit u) => 1f + u.EffectiveResearch / 24f;

    void Research(Unit u, float dt)
    {
        var b = u.location;
        if (b == null || !u.Info.canResearch) { u.status = UnitStatus.Idle; return; }
        if (!b.Surveyed) { FinishAction(u, OrderKind.Research, b); return; }

        // Same shape as the surface pass: bigger worlds and hostile ones take longer. A better research
        // suite is what buys the time back.
        float sizeFactor = Mathf.Max(0.5f, b.surfaceSize / 8f);
        float hostility = Mathf.Lerp(1f, 2.2f, Mathf.Clamp01((100f - b.habitability) / 100f));
        u.researchTimer += DeepSurveyRate * DeepSurveyQuality(u) / (sizeFactor * hostility) * dt;
        b.researchProgress = Mathf.Clamp01(u.researchTimer);

        if (u.researchTimer >= 1f)
        {
            u.AddExperience(XpResearch);
            DoDeepResearch(u, b);
            u.researchTimer = 0f;
            b.researchProgress = 1f;
            FinishAction(u, OrderKind.Research, b);
        }
    }

    // A research ship finishing a deep survey of a world.
    //
    // IT RESEARCHES NOTHING AND EXCAVATES NOTHING. What it produces is OPPORTUNITIES: the sites it
    // charts become jobs the player can commission — each with a cost in research points, a duration
    // with a progress bar, and a payout in ore, technology or a precursor schematic (see
    // ResearchTaskManager). The ores it finds become entries in the codex that can then be researched
    // as paid projects.
    //
    // It used to do all of that work itself, for free, the moment it finished. That did not just make
    // exploring trivially rewarding — it made the entire timed-research system unreachable, because
    // resolving a site is exactly what removes the button offering to resolve it. The ship's job is to
    // find things and price them. Paying for them is the player's.
    public void DoDeepResearch(Unit researcher, CelestialBody b)
    {
        int studied = 0;

        // ONE TIER, and only if this world and this empire can take it. Deep Research is a ladder of
        // three steps now rather than a single flag that could be re-run forever — see DeepResearch,
        // which owns which tier unlocks what and refuses in words when it cannot.
        int reached = DeepResearch.Advance(b);
        if (reached > 0)
            NotificationManager.Instance?.Push(
                $"{DeepResearch.Name(reached)} — {b.name}",
                DeepResearch.Describe(reached), null);

        // ORES ARE DISCOVERED, NOT RESEARCHED.
        //
        // The ship's job is to find things and say what they are. Turning a discovered ore into a
        // researched one is a project you commission and pay for — it has a cost, a timer and a payout —
        // and doing it automatically here made every one of those projects resolve itself before the
        // player ever saw it offered.
        foreach (var ore in OreGenerator.OresOnBody(b))
            if (ResearchManager.Discover(ore)) studied++;

        // Every site is CHARTED, which is what turns it into a commissionable job. Nothing is dug.
        int opened = 0, ruins = 0;
        int quoted = 0;
        var reports = new System.Text.StringBuilder();

        if (b.pointsOfInterest != null)
            foreach (var poi in b.pointsOfInterest)
            {
                // Skip on `surveyed`, NOT on `explored`.
                //
                // POIGenerator marks SpecialResource and Settlement sites explored at generation — they
                // are not mysteries, you can see what they are. But a rich seam is still a DIG: its
                // IsResearchable clause asks whether its ore has been researched, not whether the site
                // has been visited. Skipping explored sites here would leave every ore deposit
                // permanently uncharted and so permanently un-diggable, quietly deleting the
                // special-resource excavation from the game.
                if (poi.surveyed) continue;

                poi.surveyed = true;          // now offerable in the Survey tab's site list
                if (!poi.IsResearchable) continue;   // context, not a job (settlements)
                opened++;
                quoted += poi.researchPointCost;
                if (poi.type == POIType.AncientRuins) ruins++;

                // What the ship could tell from the surface — enough to say what the site IS and what
                // studying it would take, not what it holds. Collected into ONE notification: five
                // popups in a frame is noise, not a discovery.
                string blurb = !string.IsNullOrEmpty(poi.description) ? poi.description : poi.title;
                reports.Append($"\n\n<b>{poi.title}</b>\n{blurb}\n" +
                               $"<color=#8FD0FF>Study: {poi.researchPointCost} research points, " +
                               $"~{Mathf.RoundToInt(poi.researchDuration)}s</color>");
            }

        // One closing report, so what the survey produced is legible as a list of things to now DO.
        SimpleAudio.Instance?.PlayNotify(NotifKind.Research);
        var summary = new System.Text.StringBuilder();
        summary.Append(studied > 0
            ? $"{studied} new ore(s) identified — research them from the Ore Codex. "
            : "No new ores here. ");
        if (opened > 0)
        {
            summary.Append($"{opened} site(s) charted and ready to study");
            if (ruins > 0) summary.Append($", {ruins} of them ancient ruins");
            summary.Append($" — {quoted} research points to work through them all. Commission them from " +
                           "the Survey tab's site list. ");
        }
        summary.Append("Heat, Fertile and Weather indexes are now available for this world.");

        NotificationManager.Instance?.Push($"Deep survey complete at {b.name}", summary.ToString(),
                                           FlyTo(b), NotifKind.Research,
                                           detail: summary.ToString() + reports.ToString());
        PlanetViewWindow.Instance?.RefreshIfShowing(b);
    }

    // What resolving a point of interest is worth. Ancient ruins are the prize — they're what open the
    // precursor tech tree — while a settlement is mostly context and a special-resource site pays out
    // in the ore itself rather than in points.
    public static int POIReward(PointOfInterest poi)
    {
        switch (poi.type)
        {
            case POIType.AncientRuins: return 60;
            case POIType.Mystery: return 45;
            case POIType.SpecialResource: return 20;
            case POIType.Settlement: return 30;
            default: return 15;
        }
    }

    void RevealBodyVisual(CelestialBody b)
    {
        if (b == null || b.visualObject == null) return;
        var fog = b.visualObject.GetComponent<BodyFog>();
        if (fog != null) Destroy(fog);
        PlanetAppearance.Apply(b, b.visualObject);
    }

    // A colony ship founds a city on a habitable-enough, unowned world, then is CONSUMED (it becomes
    // the city). If the world isn't habitable enough it says so and stops — terraform it first.
    bool Colonize(Unit u, float dt)
    {
        var b = u.location;
        if (b == null || !u.Info.canColonize || b.owner == FactionManager.Player)
        { FinishAction(u, OrderKind.Colonize, b); return false; }

        if (b.habitability < ColonizeMinHabitability)
        {
            NotificationManager.Instance?.Push($"Can't colonize {b.name}",
                $"Habitability {b.habitability:F0}% is below the {ColonizeMinHabitability:F0}% needed. " +
                (Colony.CanReachLivable(b) ? "Terraform it first." : "This world can't be made livable for your species."),
                FlyTo(b), NotifKind.Danger);
            FinishAction(u, OrderKind.Colonize, b);
            return false;
        }

        // Founding takes a little time (bigger worlds take longer) and shows a loading bar.
        b.claimingFaction = FactionManager.Player;
        b.claimProgress = Mathf.Clamp01(b.claimProgress + (1f / (8f + b.surfaceSize * 1.2f)) * dt);
        if (b.claimProgress >= 1f) { FoundColony(u, b); return true; }
        return false;
    }

    void FoundColony(Unit u, CelestialBody b)
    {
        b.owner = FactionManager.Player;
        b.settled = true;               // people live here now — see Claim.cs
        b.claimingFaction = null;
        b.visited = true;
        b.explorationProgress = Mathf.Max(b.explorationProgress, 1f);   // owning fully reveals it
        b.cities = 1;
        // A beachhead, not a city: the colony ship's complement, scaled by how the species breeds.
        b.population = Population.ColonyStart(b, SpeciesManager.Current);
        if (!b.buildings.Contains((int)BuildingType.City)) b.buildings.Add((int)BuildingType.City);
        b.claimProgress = Colony.ClaimProgress(b);
        u.AddExperience(XpColonize);

        RevealBodyVisual(b);

        // The ship doesn't just evaporate: it lands and BECOMES the colony's first seat of government.
        // The grounded hull is a real structure on the surface grid, and stays the world's capitol
        // until you can afford to build a proper one around it.
        if (SurfaceBuildManager.FindSpot(b, SurfaceBuildingType.ColonyShipBase, out int bx, out int by))
            SurfaceBuildManager.ForcePlace(b, SurfaceBuildingType.ColonyShipBase, bx, by, 0);

        // Consume the colony ship — it becomes the city.
        if (b.units != null) b.units.Remove(u);
        units.Remove(u);
        if (UnitSelection.IsSelected(u)) UnitSelection.Clear();

        SimpleAudio.Instance?.PlayNotify(NotifKind.Victory);
        NotificationManager.Instance?.Push($"City founded on {b.name}!",
            $"{FactionManager.Player.name} settled {b.name}. The colony ship became your first city — build it up and develop the world to fully establish it.",
            FlyTo(b), NotifKind.Victory);
        RefreshOwnerRing(b);
        OnUnitsChanged?.Invoke();
    }

    void RefreshOwnerRing(CelestialBody b)
    {
        if (b.visualObject == null) return;
        var oc = b.visualObject.GetComponent<OrbitController>();
        if (oc != null) oc.SetOwnerHighlight(FactionManager.OwnerColor(b.owner), true);
    }

    static bool Crossed(float before, float after, float t) => before < t && after >= t;

    // ---- Save/load ----
    // Serialize every ship and deployed station to plain DTOs. A unit in transit is stored parked at its
    // current world position; on load it resumes any queued orders.
    public List<UnitDTO> ExportUnitDTOs()
    {
        var list = new List<UnitDTO>();
        foreach (var u in units)
        {
            var d = new UnitDTO
            {
                id = u.id,
                type = (int)u.type,
                isPlayer = u.owner == FactionManager.Player,
                locationId = u.location != null ? u.location.id : -1,
                experience = u.experience,
                worldsExplored = u.worldsExplored,
                serviceTime = u.serviceTime,
                queuePaused = u.queuePaused
            };
            if (u.location == null) { var p = UnitPos(u); d.inSpace = true; d.px = p.x; d.py = p.y; d.pz = p.z; }
            foreach (var s in u.samples) d.samples.Add(s);
            foreach (var o in u.orders)
                d.orders.Add(new OrderDTO { kind = (int)o.kind, targetId = o.target != null ? o.target.id : -1, isPoint = o.isPoint, px = o.point.x, py = o.point.y, pz = o.point.z });
            list.Add(d);
        }
        return list;
    }

    // Serialize the build queue: which hulls are on the stocks, how far along, what they cost and
    // whether they're paused. Order matters — it's the power-allocation order.
    public List<BuildOrderDTO> ExportBuildQueue()
    {
        var list = new List<BuildOrderDTO>();
        foreach (var o in buildQueue)
            list.Add(new BuildOrderDTO
            {
                type = (int)o.type, elapsed = o.elapsed, duration = o.duration,
                paused = o.paused, metalPaid = o.metalPaid, energyPaid = o.energyPaid
            });
        return list;
    }

    public void ImportBuildQueue(List<BuildOrderDTO> dtos)
    {
        buildQueue.Clear();
        nextOrderId = 1;
        if (dtos != null)
            foreach (var d in dtos)
                buildQueue.Add(new BuildOrder
                {
                    id = nextOrderId++, type = (UnitType)d.type, elapsed = d.elapsed, duration = d.duration,
                    paused = d.paused, metalPaid = d.metalPaid, energyPaid = d.energyPaid
                });
        Schedule();
        OnBuildChanged?.Invoke();
    }

    // Rebuild the fleet from DTOs, resolving body references by id. Does NOT touch ShipUpgrades range
    // factors (those are restored by EmpireTech/TechManager before this runs).
    public void ImportUnitDTOs(List<UnitDTO> dtos, Dictionary<int, CelestialBody> byId, CelestialBody homePlanet)
    {
        units.Clear();
        nextId = 1;
        HomePlanet = homePlanet;
        if (homePlanet != null)
        {
            homePlanet.shipyardLevel = Mathf.Max(1, homePlanet.shipyardLevel);
            homePlanet.researchCenterLevel = Mathf.Max(1, homePlanet.researchCenterLevel);   // pre-tier saves
            homePlanet.birthrightClaim = true;
            // The home world is settled by definition. Without this a save written before `settled`
            // existed would load with the capital marked unsettled, and TickColony — which now returns
            // early on unsettled worlds — would quietly stop the empire's entire economy.
            homePlanet.settled = true;
        }

        if (dtos != null)
            foreach (var d in dtos)
            {
                CelestialBody at = null;
                if (d.locationId >= 0 && byId != null) byId.TryGetValue(d.locationId, out at);

                var u = new Unit
                {
                    id = d.id,
                    type = (UnitType)d.type,
                    owner = d.isPlayer ? FactionManager.Player : null,
                    location = at,
                    status = UnitStatus.Idle,
                    experience = d.experience,
                    worldsExplored = d.worldsExplored,
                    serviceTime = d.serviceTime,
                    queuePaused = d.queuePaused
                };
                u.name = $"{UnitDatabase.Get(u.type).name} {u.id}";
                if (at == null && d.inSpace) { u.inSpace = true; u.parkPosition = new Vector3(d.px, d.py, d.pz); }
                if (d.samples != null) foreach (var s in d.samples) u.samples.Add(s);
                if (d.orders != null)
                    foreach (var od in d.orders)
                    {
                        CelestialBody tgt = null;
                        if (od.targetId >= 0 && byId != null) byId.TryGetValue(od.targetId, out tgt);
                        u.orders.Add(new ShipOrder { kind = (OrderKind)od.kind, target = tgt, isPoint = od.isPoint, point = new Vector3(od.px, od.py, od.pz) });
                    }

                units.Add(u);
                nextId = Mathf.Max(nextId, u.id + 1);
                if (at != null) { if (at.units == null) at.units = new List<Unit>(); at.units.Add(u); }
            }

        StationEffects.Reset();   // relay/aura totals recompute next frame from the restored fleet
        OnUnitsChanged?.Invoke();
    }
}
