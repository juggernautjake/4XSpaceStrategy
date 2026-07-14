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

    Vector3 WorldPos(CelestialBody b)
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
                if (b != null && u.Info.canResearch && b.Surveyed) { u.status = UnitStatus.Researching; }
                else
                {
                    if (b != null && !b.Surveyed)
                        NotificationManager.Instance?.Push($"{u.name} can't research yet",
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

    // Survey: reveals the world, one PASS at a time.
    //
    // A ship can only map so much of a world before it has exhausted what its sensors know how to look
    // at (UnitInfo.surveyDepth). A basic Scout maps just over half of a world and then its pass is
    // done — send it round again for the next slice. A Mk III or a Science Vessel maps the whole thing
    // in one go, which is what reveals the points of interest.
    //
    // Surveying only DISCOVERS ores (and collects a sample to carry); it never researches them. That
    // happens at a research ship, station or centre, and it costs research points.
    void Explore(Unit u, float dt)
    {
        var b = u.location;
        if (b == null) { u.status = UnitStatus.Idle; return; }
        if (b.Surveyed) { FinishAction(u, OrderKind.Survey, b); return; }   // already done

        // A pass begins the first frame this ship starts surveying this world.
        if (u.surveyPassBody != b) { u.surveyPassBody = b; u.surveyPassStart = b.explorationProgress; }

        float before = b.explorationProgress;
        float sizeFactor = Mathf.Max(0.5f, b.surfaceSize / 8f);                                   // bigger = slower
        float hostility = Mathf.Lerp(1f, 2.2f, Mathf.Clamp01((100f - b.habitability) / 100f));    // less habitable = slower
        float bonus = u.type == UnitType.Scout ? 1.7f : 1f;                                       // scouts survey faster
        b.explorationProgress = Mathf.Clamp01(b.explorationProgress + 0.05f * bonus / (sizeFactor * hostility) * dt);

        // Collect an ore SAMPLE at each survey milestone (discovered + carried; not researched here).
        if (Crossed(before, b.explorationProgress, 0.25f) || Crossed(before, b.explorationProgress, 0.5f) ||
            Crossed(before, b.explorationProgress, 0.75f) || Crossed(before, b.explorationProgress, 1f))
        {
            foreach (var ore in OreGenerator.OresOnBody(b))
            {
                if (!ResearchManager.IsDiscovered(ore)) { ResearchManager.Discover(ore); u.samples.Add((int)ore); u.AddExperience(XpSample); break; }
            }
        }

        // Pass exhausted before the world is finished: stop here and say so. The ship keeps what it
        // mapped — another pass (or a better hull) picks up exactly where this one left off.
        float mapped = b.explorationProgress - u.surveyPassStart;
        if (b.explorationProgress < 1f && mapped >= u.Info.surveyDepth)
        {
            u.AddExperience(XpSample);
            u.surveyPassBody = null;
            SimpleAudio.Instance?.PlayNotify(NotifKind.Info);
            NotificationManager.Instance?.Push($"{b.name} — survey pass complete",
                $"{u.name} mapped {mapped * 100f:F0}% this pass ({b.explorationProgress * 100f:F0}% total). " +
                $"A {u.Info.name} can only map {u.Info.surveyDepth * 100f:F0}% at a time — send it round again, or bring a better sensor suite, to finish the map and reveal this world's points of interest.",
                FlyTo(b), NotifKind.Info);
            FinishAction(u, OrderKind.Survey, b);
            return;
        }

        if (b.explorationProgress >= 1f)
        {
            u.surveyPassBody = null;
            u.AddExperience(XpSurvey);
            SimpleAudio.Instance?.PlayNotify(NotifKind.Research);
            NotificationManager.Instance?.Push($"{b.name} surveyed", "Survey complete — map revealed.",
                FlyTo(b), NotifKind.Research,
                detail: $"{u.name} finished surveying {b.name}. Its detailed map and points of interest are now available." +
                (u.samples.Count > 0 ? $" {u.name} is carrying {u.samples.Count} ore sample(s) — take them to a research ship or a world with a research centre to research them." : ""));
            RevealBodyVisual(b);
            AncientLore.SurveyBody(b);   // recover any ancient schematics the ruins here hold
            FinishAction(u, OrderKind.Survey, b);
            return;
        }

        // Can't loiter on a hostile world forever (unless colonizing it).
        if (b.habitability < 80f && u.type != UnitType.ColonyShip)
        {
            u.missionTimer += dt;
            if (u.missionTimer >= HostileStaySeconds && HomePlanet != null && b != HomePlanet)
            {
                NotificationManager.Instance?.Push($"{u.name} must return home",
                    $"{b.name} is too hostile ({b.habitability:F0}%) to remain.", null, NotifKind.Danger);
                StopAll(u);
                IssueMove(new List<Unit> { u }, HomePlanet, false);
            }
        }
    }

    // Deep research (research ships / a world with a research centre). Requires the world be surveyed.
    // Researches this world's discovered ores + any samples carried by co-located ships, plus its POIs.
    void Research(Unit u, float dt)
    {
        var b = u.location;
        if (b == null || !u.Info.canResearch) { u.status = UnitStatus.Idle; return; }
        if (!b.Surveyed) { FinishAction(u, OrderKind.Research, b); return; }

        float sizeFactor = Mathf.Max(0.5f, b.surfaceSize / 8f);
        u.researchTimer += (u.EffectiveResearch + 1) * 0.02f / sizeFactor * dt;
        b.researchProgress = Mathf.Clamp01(u.researchTimer);

        if (u.researchTimer >= 1f)
        {
            u.AddExperience(XpResearch);
            DoDeepResearch(u, b);
            u.researchTimer = 0f;
            b.researchProgress = 1f;
            SimpleAudio.Instance?.PlayNotify(NotifKind.Research);
            NotificationManager.Instance?.Push($"Research complete at {b.name}",
                "Ores and anomalies here have been fully researched.", FlyTo(b), NotifKind.Research);
            FinishAction(u, OrderKind.Research, b);
        }
    }

    // Convert discovered ores here (and carried samples of all ships present) into researched tech,
    // award research points, and resolve any points of interest.
    // A research ship finishing a deep study of a world. Every ore it cracks COSTS research points —
    // the ship does the legwork, but the analysis is paid for out of the empire's research bank. Ores
    // it can't afford stay merely discovered (and any samples stay in the hold) until you can pay.
    public void DoDeepResearch(Unit researcher, CelestialBody b)
    {
        int studied = 0, unaffordable = 0;

        // A deep study is what puts people on the ground long enough to chart the geothermal vents, the
        // soil and the weather — it unlocks the Heat, Fertile and Weather survey overlays used when
        // siting surface buildings. Mapping a world from orbit only ever shows you its mineral seams.
        if (!b.deepSurveyed)
        {
            b.deepSurveyed = true;
            NotificationManager.Instance?.Push($"Deep survey complete on {b.name}",
                "Heat, Fertile and Weather index overlays are now available for this world — use them in the Planet View's Survey tab to site geothermal plants, farms and solar arrays where they'll actually pay.",
                FlyTo(b), NotifKind.Research);
        }

        foreach (var ore in OreGenerator.OresOnBody(b))
        {
            ResearchManager.Discover(ore);
            if (ResearchManager.IsResearched(ore)) continue;
            if (ResearchManager.TryResearchSample(ore)) studied++;
            else unaffordable++;
        }

        // Samples carried by any ship parked here get analysed too — that's the whole point of hauling
        // them to a research vessel. Ones you can't afford stay in the hold rather than being lost.
        if (b.units != null)
            foreach (var unit in b.units)
            {
                if (unit.samples.Count == 0) continue;
                var kept = new List<int>();
                foreach (var id in unit.samples)
                {
                    if (ResearchManager.TryResearchSample((OreType)id)) studied++;
                    else { kept.Add(id); unaffordable++; }
                }
                unit.samples.Clear();
                unit.samples.AddRange(kept);
            }

        // Resolving a world's anomalies is the PAYOFF of exploring: it hands points back.
        if (b.pointsOfInterest != null)
            foreach (var poi in b.pointsOfInterest)
            {
                if (poi.explored) continue;
                poi.explored = true;
                ResearchManager.AddPoints(POIReward(poi));
            }

        if (unaffordable > 0)
            NotificationManager.Instance?.Push($"Research stalled at {b.name}",
                $"{studied} sample(s) analysed. {unaffordable} could not be afforded — the samples are being held until you have the research points.",
                null, NotifKind.Info);
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
