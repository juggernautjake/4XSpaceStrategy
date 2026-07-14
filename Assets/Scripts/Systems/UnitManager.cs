using System;
using System.Collections.Generic;
using UnityEngine;

// One in-progress ship being built.
public class BuildOrder
{
    public UnitType type;
    public float elapsed, duration;
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

    public IReadOnlyList<BuildOrder> BuildQueue => buildQueue;
    public event Action OnBuildChanged;

    public CelestialBody HomePlanet { get; private set; }

    public event Action OnUnitsChanged;

    const float SpeedScale = 6f;
    const float HostileStaySeconds = 45f;   // limited stay on <80% worlds before forced return

    // XP is earned by COMPLETING tasks (not by idling). Travel gives a little; missions give more.
    const float XpTravel = 8f, XpSample = 5f, XpSurvey = 30f, XpResearch = 45f, XpColonize = 60f;

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
        nextId = 1;
        HomePlanet = homePlanet;

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
    public bool QueueBuild(UnitType type)
    {
        var info = UnitDatabase.Get(type);
        if (!GameMode.DevMode && !PlayerEconomy.Spend(info.costMetal, info.costEnergy)) return false;  // free in Dev Mode
        buildQueue.Add(new BuildOrder { type = type, duration = info.buildTime });
        OnBuildChanged?.Invoke();
        return true;
    }

    // The order currently under construction (front of the queue), or null.
    public BuildOrder CurrentBuild => buildQueue.Count > 0 ? buildQueue[0] : null;

    void AdvanceBuild(float dt)
    {
        if (buildQueue.Count == 0) return;
        var order = buildQueue[0];
        order.elapsed += dt;
        if (order.elapsed >= order.duration)
        {
            buildQueue.RemoveAt(0);
            CreateUnit(order.type, FactionManager.Player, HomePlanet);
            SimpleAudio.Instance?.PlayNotify(NotifKind.Info);
            NotificationManager.Instance?.Push($"{UnitDatabase.Get(order.type).name} built",
                $"Ready at {(HomePlanet != null ? HomePlanet.name : "home")}.", null, NotifKind.Info);
            OnBuildChanged?.Invoke();
        }
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
            float t = Mathf.Clamp(Vector3.Distance(from, WorldPos(target)) / (slow * SpeedScale), 3f, 120f);
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = oc.PredictWorldPosition(t);
                t = Mathf.Clamp(Vector3.Distance(from, p) / (slow * SpeedScale), 3f, 120f);
            }
            to = oc.PredictWorldPosition(t);
            duration = t;
        }
        else
        {
            to = WorldPos(target);
            duration = Mathf.Clamp(Vector3.Distance(from, to) / (slow * SpeedScale), 3f, 120f);
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
        float duration = Mathf.Clamp(Vector3.Distance(from, worldPos) / (slow * SpeedScale), 3f, 120f);

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

    // ---- Simulation ----
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

    bool Travel(Unit u, float dt)
    {
        u.travelElapsed += dt;
        if (u.travelElapsed < u.travelDuration) return false;

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

    // Survey: reveals the world. Bigger and LESS habitable worlds take longer; scouts get a survey
    // bonus. Surveying only DISCOVERS ores (collects samples) — it does NOT research them.
    void Explore(Unit u, float dt)
    {
        var b = u.location;
        if (b == null) { u.status = UnitStatus.Idle; return; }
        if (b.Surveyed) { FinishAction(u, OrderKind.Survey, b); return; }   // already done

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

        if (b.explorationProgress >= 1f)
        {
            u.AddExperience(XpSurvey);
            SimpleAudio.Instance?.PlayNotify(NotifKind.Research);
            NotificationManager.Instance?.Push($"{b.name} surveyed",
                $"Detailed map and points of interest revealed." +
                (u.samples.Count > 0 ? $" {u.name} is carrying {u.samples.Count} ore sample(s) — take them to a research ship or centre." : ""),
                FlyTo(b), NotifKind.Research);
            RevealBodyVisual(b);
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
    public void DoDeepResearch(Unit researcher, CelestialBody b)
    {
        foreach (var ore in OreGenerator.OresOnBody(b))
        {
            ResearchManager.Discover(ore);
            ResearchManager.ForceResearch(ore);
        }
        if (b.units != null)
            foreach (var unit in b.units)
            {
                if (unit.samples.Count == 0) continue;
                foreach (var id in unit.samples) ResearchManager.ForceResearch((OreType)id);
                unit.samples.Clear();
            }
        ResearchManager.AddPoints(25);
        if (b.pointsOfInterest != null)
            foreach (var poi in b.pointsOfInterest) poi.explored = true;
    }

    void RevealBodyVisual(CelestialBody b)
    {
        if (b == null || b.visualObject == null) return;
        var fog = b.visualObject.GetComponent<BodyFog>();
        if (fog != null) Destroy(fog);
        PlanetAppearance.Apply(b, b.visualObject);
    }

    bool Colonize(Unit u, float dt)
    {
        var b = u.location;
        if (b == null || b.owner == FactionManager.Player) { FinishAction(u, OrderKind.Colonize, b); return false; }

        bool habMet = b.habitability >= 80f;

        // Explore alongside colonizing.
        b.explorationProgress = Mathf.Clamp01(b.explorationProgress + 0.01f * dt);

        if (habMet)
        {
            int popTarget = 50 + b.surfaceSize * 5;
            int cityTarget = 2 + b.surfaceSize / 6;
            b.population = Mathf.Min(popTarget, b.population + Mathf.RoundToInt(4f * dt) + 1);
            if (b.population > b.cities * (popTarget / Mathf.Max(1, cityTarget))) b.cities = Mathf.Min(cityTarget, b.cities + 1);

            float popF = Mathf.Clamp01(b.population / (float)popTarget);
            float cityF = Mathf.Clamp01(b.cities / (float)cityTarget);
            float exF = Mathf.Clamp01(b.explorationProgress / 0.6f);
            b.claimProgress = 0.25f + 0.25f * popF + 0.25f * cityF + 0.25f * exF;
        }
        else
        {
            // Stalls until the world is habitable enough for the species.
            b.claimProgress = Mathf.Min(b.claimProgress, 0.24f);
        }

        if (b.claimProgress >= 1f)
        {
            b.owner = FactionManager.Player;
            b.claimingFaction = null;
            b.claimProgress = 1f;
            foreach (var m in b.moons) m.owner = FactionManager.Player;
            u.AddExperience(XpColonize);
            FinishAction(u, OrderKind.Colonize, b);
            SimpleAudio.Instance?.PlayNotify(NotifKind.Victory);
            NotificationManager.Instance?.Push($"{b.name} claimed!",
                $"{FactionManager.Player.name} has fully colonized {b.name}.",
                () => { if (b.visualObject != null) CameraController.Focus(b.visualObject.transform.position); PlanetUI.Instance?.Show(b); },
                NotifKind.Victory);
            RefreshOwnerRing(b);
            return true;
        }
        return false;
    }

    void RefreshOwnerRing(CelestialBody b)
    {
        if (b.visualObject == null) return;
        var oc = b.visualObject.GetComponent<OrbitController>();
        if (oc != null) oc.SetOwnerHighlight(FactionManager.OwnerColor(b.owner), true);
    }

    static bool Crossed(float before, float after, float t) => before < t && after >= t;

    // ---- Save/load ----
    public List<Unit> ExportUnits() => new List<Unit>(units);

    public void ImportUnits(List<Unit> loaded, CelestialBody homePlanet)
    {
        units.Clear();
        nextId = 1;
        HomePlanet = homePlanet;
        foreach (var u in loaded)
        {
            units.Add(u);
            nextId = Mathf.Max(nextId, u.id + 1);
            if (u.location != null) { if (u.location.units == null) u.location.units = new List<Unit>(); u.location.units.Add(u); }
        }
        OnUnitsChanged?.Invoke();
    }
}
