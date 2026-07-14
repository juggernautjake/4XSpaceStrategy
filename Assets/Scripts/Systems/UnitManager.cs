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

        Vector3 from = WorldPos(group[0].location);
        Vector3 to = WorldPos(target);
        float dist = Vector3.Distance(from, to);
        float duration = Mathf.Clamp(dist / (slow * SpeedScale), 3f, 120f);

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

    public void SendUnitsHome(List<Unit> group) => SendUnits(group, HomePlanet);

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
            u.serviceTime += dt;
            u.AddExperience(dt * 0.2f);   // slow seniority gain just for serving

            switch (u.status)
            {
                case UnitStatus.Traveling: changed |= Travel(u, dt); break;
                case UnitStatus.Exploring: Explore(u, dt); break;
                case UnitStatus.Colonizing: changed |= Colonize(u, dt); break;
            }
        }

        if (changed) OnUnitsChanged?.Invoke();
    }

    bool Travel(Unit u, float dt)
    {
        u.travelElapsed += dt;
        if (u.travelElapsed < u.travelDuration) return false;

        // Arrive.
        var dest = u.travelTarget;
        u.travelTarget = null;
        u.location = dest;
        if (dest != null) { if (dest.units == null) dest.units = new List<Unit>(); dest.units.Add(u); }
        u.worldsExplored++;
        u.AddExperience(15f);

        // Auto-begin the appropriate mission.
        if (u.Info.canColonize && dest != null && dest.owner != FactionManager.Player)
        {
            u.status = UnitStatus.Colonizing;
            dest.claimingFaction = FactionManager.Player;
        }
        else if (u.Info.canExplore && dest != null)
        {
            u.status = UnitStatus.Exploring;
        }
        else u.status = UnitStatus.Idle;

        NotificationManager.Instance?.Push($"{u.name} arrived at {(dest != null ? dest.name : "destination")}",
            $"{u.Info.name} · {u.RankName}", null, NotifKind.Info);
        return true;
    }

    void Explore(Unit u, float dt)
    {
        var b = u.location;
        if (b == null) { u.status = UnitStatus.Idle; return; }

        float before = b.explorationProgress;
        // Bigger worlds take longer; research ships (high research stat) survey far faster than scouts.
        float sizeFactor = Mathf.Max(0.5f, b.surfaceSize / 10f);
        b.explorationProgress = Mathf.Clamp01(b.explorationProgress + (u.Info.research + 1) * 0.012f / sizeFactor * dt);
        u.AddExperience(dt * (u.Info.canResearch ? 1.2f : 0.6f));

        // Limited research: contribute up to a cap, occasionally discovering an ore + points.
        float cap = u.type == UnitType.ResearchShip ? 150f : 50f;
        if (u.researchContributed < cap)
        {
            u.researchContributed += u.EffectiveResearch * 0.05f * dt;
            // Crossing 25%/50%/75%/100% survey milestones surfaces an ore discovery.
            if (Crossed(before, b.explorationProgress, 0.25f) || Crossed(before, b.explorationProgress, 0.5f) ||
                Crossed(before, b.explorationProgress, 0.75f) || Crossed(before, b.explorationProgress, 1f))
            {
                var ores = OreGenerator.OresOnBody(b);
                foreach (var ore in ores) { if (!ResearchManager.IsDiscovered(ore)) { ResearchManager.Discover(ore); break; } }
                ResearchManager.AddPoints(5);
            }
        }

        // Can't stay on a hostile world forever (unless it's habitable enough).
        if (b.habitability < 80f && u.type != UnitType.ColonyShip)
        {
            u.missionTimer += dt;
            if (u.missionTimer >= HostileStaySeconds && HomePlanet != null && b != HomePlanet)
            {
                u.status = UnitStatus.Returning;
                var one = new List<Unit> { u };
                SendUnits(one, HomePlanet);
                NotificationManager.Instance?.Push($"{u.name} must return home",
                    $"{b.name} is too hostile ({b.habitability:F0}%) to remain.", null, NotifKind.Danger);
            }
        }
    }

    bool Colonize(Unit u, float dt)
    {
        var b = u.location;
        if (b == null || b.owner == FactionManager.Player) { u.status = UnitStatus.Idle; return false; }

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

        u.AddExperience(dt * 0.8f);

        if (b.claimProgress >= 1f)
        {
            b.owner = FactionManager.Player;
            b.claimingFaction = null;
            b.claimProgress = 1f;
            foreach (var m in b.moons) m.owner = FactionManager.Player;
            u.status = UnitStatus.Idle;
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
