using System.Collections.Generic;
using UnityEngine;

// One in-progress project on a colony: a building, the founding of a city, or a shipyard upgrade.
public class Construction
{
    public CelestialBody body;
    public BuildingType type;
    public bool establishCity;     // founding the first city on an owned-but-empty world
    public bool shipyardUpgrade;   // raising this world's shipyard tier
    public bool labUpgrade;        // raising this world's research-centre tier
    public float elapsed, duration;
    public string Label;           // what to show while it builds
    public float Progress => duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
}

// Objective/criteria helpers shared by the economy tick and the colony UI, so the rules for
// "habitable enough", "fully established" and terraforming are defined in exactly one place.
public struct ColonyObjective { public string label; public bool done; public string detail; }

public static class Colony
{
    // A world must be at least this habitable to found a city (below it you must terraform first).
    public static float FoundThreshold => UnitManager.ColonizeMinHabitability;

    // How many population units a world can hold. Delegates to Population, which scales it by
    // habitability and by the housing actually built — it used to be a flat 40 + size*6, which meant a
    // paradise and a barely-livable rock held the same number of people.
    public static int PopTarget(CelestialBody b) => Population.Capacity(b);
    public const int BuildingTarget = 4;   // city + 3 more

    // Facility tiers. Tiers 1-3 gate WHICH ships a yard can build; every tier (including 4 and 5) also
    // grants build power — how many hulls it can work on at once (see BuildPower).
    public const int MaxShipyardLevel = 5;
    public const int MaxResearchCenterLevel = 5;

    // Your best shipyard tier across all owned worlds (0 if you have none). Gates which ships you can
    // build and how fast. The home world always provides at least tier 1.
    //
    // Memoized per frame. This walks EVERY body in the galaxy through a yield iterator, and the build
    // menus ask for it once or twice per hull per frame — dozens of full galaxy walks (and enumerator
    // allocations) every frame, which showed up as game-wide stutter. It can only change on a build or
    // an upgrade, never twice within one frame.
    static int yardLvlCache = -1, yardLvlFrame = -1;
    public static int PlayerMaxShipyardLevel()
    {
        if (yardLvlFrame == Time.frameCount && yardLvlCache >= 0) return yardLvlCache;
        int best = 0;
        if (SystemContext.Galaxy != null)
            foreach (var b in SystemContext.AllBodies())
                if (b.owner == FactionManager.Player && b.shipyardLevel > best) best = b.shipyardLevel;
        yardLvlCache = best; yardLvlFrame = Time.frameCount;
        return best;
    }

    static int labLvlCache = -1, labLvlFrame = -1;
    public static int PlayerMaxResearchCenterLevel()
    {
        if (labLvlFrame == Time.frameCount && labLvlCache >= 0) return labLvlCache;
        int best = 0;
        if (SystemContext.Galaxy != null)
            foreach (var b in SystemContext.AllBodies())
                if (b.owner == FactionManager.Player && b.researchCenterLevel > best) best = b.researchCenterLevel;
        labLvlCache = best; labLvlFrame = Time.frameCount;
        return best;
    }

    // What a shipyard tier buys you, for the upgrade button and the level-up notification.
    public static string ShipyardPerk(int level)
    {
        switch (level)
        {
            case 2:  return "Mk II ships unlocked";
            case 3:  return "Terraformers and capital hulls unlocked";
            case 4:  return "a wider yard — more hulls at once";
            case 5:  return "the largest yard your engineers can build";
            default: return "basic hulls";
        }
    }

    // The objectives that must ALL be met for a colony to be "fully established" (a full claim).
    public static List<ColonyObjective> Objectives(CelestialBody b)
    {
        var list = new List<ColonyObjective>();

        // The home world and its moons are claimed by BIRTHRIGHT: being ours from the start IS the
        // whole claim. They don't have to satisfy the survey/habitability/population/building goals
        // other worlds do — though you may still choose to settle and develop them.
        if (b.birthrightClaim)
        {
            list.Add(new ColonyObjective { label = "Homeworld birthright", done = true, detail = "claimed by birthright" });
            return list;
        }

        list.Add(new ColonyObjective { label = "Survey the world", done = b.explorationProgress >= 1f, detail = $"{b.explorationProgress * 100f:F0}%" });
        list.Add(new ColonyObjective { label = $"Habitable (>= {FoundThreshold:F0}%)", done = b.habitability >= FoundThreshold, detail = $"{b.habitability:F0}%" });
        int pt = PopTarget(b);
        list.Add(new ColonyObjective { label = "Grow the population", done = b.population >= pt, detail = $"{Population.Short(b.population)} of {Population.Short(pt)}" });
        // Counts BOTH systems: a world you developed entirely on the surface grid was previously
        // considered to have no infrastructure at all and could never be fully established.
        int structures = ColonyFacilities.TotalStructures(b);
        list.Add(new ColonyObjective { label = "Develop infrastructure", done = structures >= BuildingTarget, detail = $"{structures}/{BuildingTarget} structures" });
        return list;
    }

    public static float ClaimProgress(CelestialBody b)
    {
        var objs = Objectives(b);
        int done = 0; foreach (var o in objs) if (o.done) done++;
        return objs.Count > 0 ? done / (float)objs.Count : 0f;
    }

    public static bool IsFullyEstablished(CelestialBody b) => ClaimProgress(b) >= 1f;

    // The highest habitability terraforming could reach for the current species (its ceiling): the
    // world's natural potential, plus researched Expansion technologies, plus every planetary
    // engineering PROJECT already completed here (melted ice caps, orbital shades, a restarted core).
    // This is why a world's ceiling rises as you work on it — see TerraformProjects.
    public static float TerraformCeiling(CelestialBody b)
        => b == null ? 0f
         : Mathf.Min(100f, Mathf.Max(b.habitability, b.terraformability)
                         + TechEffects.TerraformCeilingBonus
                         + TerraformProjects.CeilingBonus(b));

    // Can terraforming ever make this world colonizable for us?
    public static bool CanReachLivable(CelestialBody b) => TerraformCeiling(b) >= FoundThreshold;
}

// Drives colonies each second: resource income from buildings (scaled by population), population
// growth, research facilities processing samples, terraforming projects, timed construction, and the
// development that fills in the "fully established" objectives.
public class ColonyManager : MonoBehaviour
{
    public static ColonyManager Instance;

    readonly List<Construction> building = new List<Construction>();
    float tick;
    float researchAccum;

    public static void Create()
    {
        if (Instance != null) return;
        new GameObject("ColonyManager").AddComponent<ColonyManager>();
    }

    void Awake() { Instance = this; }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt > 0f) AdvanceConstruction(dt);

        tick += dt;
        if (tick < 1f) return;
        float step = tick; tick = 0f;

        // Station/worker auras + relay network, on the same 1s cadence as colony income (so the shared
        // economy only fires its change event once per second, not every frame).
        StationEffects.Tick(step);

        if (SystemContext.Galaxy == null) return;
        foreach (var b in SystemContext.AllBodies())
        {
            if (b.owner == FactionManager.Player) TickColony(b, step);
            if (b.terraforming) TickTerraform(b, step);
        }
    }

    // ---- Construction ----
    public bool IsUnique(BuildingType t) => true;   // one of each per world (keeps colonies readable)

    // A world gets ONE shipyard and ONE research centre — you upgrade the one you have rather than
    // stacking more. Both track their tier in a level field rather than the buildings list (the capital
    // starts with a level-1 yard and lab it never "built"), so the level is the real test of existence.
    // Checking only `buildings` would let you build a second yard on your own home world.
    public static bool HasFacility(CelestialBody b, BuildingType t)
    {
        if (b == null) return false;
        if (t == BuildingType.Shipyard) return b.shipyardLevel >= 1 || b.buildings.Contains((int)t);
        if (t == BuildingType.ResearchCenter) return b.researchCenterLevel >= 1 || b.buildings.Contains((int)t);
        return b.buildings.Contains((int)t);
    }

    public bool CanBuild(CelestialBody b, BuildingType t, out string reason)
    {
        reason = null;
        if (b == null || b.owner != FactionManager.Player) { reason = "colony not yours"; return false; }
        if (!b.buildings.Contains((int)BuildingType.City)) { reason = "found a city first"; return false; }
        if (t == BuildingType.City) { reason = "already the city"; return false; }
        if (HasFacility(b, t))
        {
            reason = t == BuildingType.Shipyard ? "this world already has a shipyard — upgrade it instead"
                   : t == BuildingType.ResearchCenter ? "this world already has a research centre — upgrade it instead"
                   : "already built";
            return false;
        }
        if (IsConstructing(b, t)) { reason = "under construction"; return false; }
        var info = BuildingDatabase.Get(t);
        int cm = DiscCost(info.costMetal), ce = DiscCost(info.costEnergy);
        if (!GameMode.DevMode && !PlayerEconomy.CanAfford(cm, ce))
        { reason = $"need {cm} metal, {ce} energy"; return false; }
        return true;
    }

    // Build costs/times are reduced by researched Industry technologies.
    public static int DiscCost(int c) => Mathf.RoundToInt(c * TechEffects.BuildCostMult);

    public bool StartBuilding(CelestialBody b, BuildingType t)
    {
        if (!CanBuild(b, t, out _)) return false;
        var info = BuildingDatabase.Get(t);
        if (!GameMode.DevMode && !PlayerEconomy.Spend(DiscCost(info.costMetal), DiscCost(info.costEnergy))) return false;
        building.Add(new Construction { body = b, type = t, duration = info.buildTime * TechEffects.BuildTimeMult, Label = $"Building {info.name}" });
        return true;
    }

    // ---- Establishing the first city (used to settle an owned-but-empty world, e.g. a home moon) ----
    public const int CityMetal = 80, CityEnergy = 60;
    public const float CityBuildTime = 18f;

    public bool CanEstablishCity(CelestialBody b, out string reason)
    {
        reason = null;
        if (b == null || b.owner != FactionManager.Player) { reason = "world not yours"; return false; }
        if (b.buildings.Contains((int)BuildingType.City)) { reason = "already has a city"; return false; }
        if (b.habitability < Colony.FoundThreshold)
        {
            reason = Colony.CanReachLivable(b)
                ? $"terraform to {Colony.FoundThreshold:F0}% first (this: {b.habitability:F0}%)"
                : "can't be made livable for your species";
            return false;
        }
        if (IsConstructing(b, BuildingType.City)) { reason = "founding under way"; return false; }
        if (!GameMode.DevMode && !PlayerEconomy.CanAfford(CityMetal, CityEnergy))
        { reason = $"need {CityMetal} metal, {CityEnergy} energy"; return false; }
        return true;
    }

    public bool StartEstablishCity(CelestialBody b)
    {
        if (!CanEstablishCity(b, out _)) return false;
        if (!GameMode.DevMode && !PlayerEconomy.Spend(CityMetal, CityEnergy)) return false;
        building.Add(new Construction { body = b, type = BuildingType.City, establishCity = true, duration = CityBuildTime, Label = "Founding city" });
        return true;
    }

    // ---- Shipyard upgrades ----
    // Level 2 unlocks Mk II ships and level 3 the Terraformer; every tier (2-5) also widens the yard,
    // adding a point of build power. The later tiers are a serious industrial investment.
    static readonly int[] YardMetal  = { 0, 0, 200, 350, 550, 850 };
    static readonly int[] YardEnergy = { 0, 0, 150, 280, 450, 700 };
    static readonly float[] YardTime = { 0, 0, 30f, 45f, 65f, 90f };

    public static int ShipyardUpgradeMetal(int toLevel) => YardMetal[Mathf.Clamp(toLevel, 0, Colony.MaxShipyardLevel)];
    public static int ShipyardUpgradeEnergy(int toLevel) => YardEnergy[Mathf.Clamp(toLevel, 0, Colony.MaxShipyardLevel)];
    public static float ShipyardUpgradeTime(int toLevel) => YardTime[Mathf.Clamp(toLevel, 0, Colony.MaxShipyardLevel)];

    public bool CanUpgradeShipyard(CelestialBody b, out string reason, out int nextLevel)
    {
        reason = null; nextLevel = 0;
        if (b == null || b.owner != FactionManager.Player) { reason = "world not yours"; return false; }
        if (b.shipyardLevel < 1) { reason = "build a shipyard first"; return false; }
        if (b.shipyardLevel >= Colony.MaxShipyardLevel) { reason = "already max level"; return false; }
        nextLevel = b.shipyardLevel + 1;
        if (IsShipyardUpgrading(b)) { reason = "upgrade under way"; return false; }
        int m = ShipyardUpgradeMetal(nextLevel), e = ShipyardUpgradeEnergy(nextLevel);
        if (!GameMode.DevMode && !PlayerEconomy.CanAfford(m, e)) { reason = $"need {m} metal, {e} energy"; return false; }
        return true;
    }

    public bool IsShipyardUpgrading(CelestialBody b)
    {
        foreach (var c in building) if (c.body == b && c.shipyardUpgrade) return true;
        return false;
    }

    public bool StartShipyardUpgrade(CelestialBody b)
    {
        if (!CanUpgradeShipyard(b, out _, out int next)) return false;
        int m = ShipyardUpgradeMetal(next), e = ShipyardUpgradeEnergy(next);
        if (!GameMode.DevMode && !PlayerEconomy.Spend(m, e)) return false;
        building.Add(new Construction { body = b, shipyardUpgrade = true, duration = ShipyardUpgradeTime(next), Label = $"Upgrading shipyard to Lv{next}" });
        return true;
    }

    // ---- Research-centre upgrades ----
    // The research-side twin of the shipyard ladder: every tier adds a point of research capacity, so a
    // bigger laboratory can study more technologies at once — or one much larger project.
    static readonly int[] LabMetal  = { 0, 0, 180, 320, 500, 780 };
    static readonly int[] LabEnergy = { 0, 0, 170, 300, 480, 760 };
    static readonly float[] LabTime = { 0, 0, 28f, 42f, 60f, 85f };

    public static int LabUpgradeMetal(int toLevel) => LabMetal[Mathf.Clamp(toLevel, 0, Colony.MaxResearchCenterLevel)];
    public static int LabUpgradeEnergy(int toLevel) => LabEnergy[Mathf.Clamp(toLevel, 0, Colony.MaxResearchCenterLevel)];
    public static float LabUpgradeTime(int toLevel) => LabTime[Mathf.Clamp(toLevel, 0, Colony.MaxResearchCenterLevel)];

    public bool CanUpgradeLab(CelestialBody b, out string reason, out int nextLevel)
    {
        reason = null; nextLevel = 0;
        if (b == null || b.owner != FactionManager.Player) { reason = "world not yours"; return false; }
        if (b.researchCenterLevel < 1) { reason = "build a research centre first"; return false; }
        if (b.researchCenterLevel >= Colony.MaxResearchCenterLevel) { reason = "already max level"; return false; }
        nextLevel = b.researchCenterLevel + 1;
        if (IsLabUpgrading(b)) { reason = "upgrade under way"; return false; }
        int m = LabUpgradeMetal(nextLevel), e = LabUpgradeEnergy(nextLevel);
        if (!GameMode.DevMode && !PlayerEconomy.CanAfford(m, e)) { reason = $"need {m} metal, {e} energy"; return false; }
        return true;
    }

    public bool IsLabUpgrading(CelestialBody b)
    {
        foreach (var c in building) if (c.body == b && c.labUpgrade) return true;
        return false;
    }

    public bool StartLabUpgrade(CelestialBody b)
    {
        if (!CanUpgradeLab(b, out _, out int next)) return false;
        int m = LabUpgradeMetal(next), e = LabUpgradeEnergy(next);
        if (!GameMode.DevMode && !PlayerEconomy.Spend(m, e)) return false;
        building.Add(new Construction { body = b, labUpgrade = true, duration = LabUpgradeTime(next), Label = $"Upgrading research centre to Lv{next}" });
        return true;
    }

    // Only real building projects count. Facility upgrades leave `type` at its default (City), so they
    // must be excluded or they read as "a city is being founded here".
    public bool IsConstructing(CelestialBody b, BuildingType t)
    {
        foreach (var c in building) if (c.body == b && !c.shipyardUpgrade && !c.labUpgrade && c.type == t) return true;
        return false;
    }

    // The ACTIVE construction on a world (first queued), or null.
    public Construction ConstructionFor(CelestialBody b)
    {
        foreach (var c in building) if (c.body == b) return c;
        return null;
    }

    // Everything queued on a world, in build order (the first is the one actually building).
    public List<Construction> QueueFor(CelestialBody b)
    {
        var l = new List<Construction>();
        foreach (var c in building) if (c.body == b) l.Add(c);
        return l;
    }

    // A construction only advances when it's first in line for its world (one build at a time per colony).
    bool IsActive(Construction c)
    {
        foreach (var o in building) { if (o == c) return true; if (o.body == c.body) return false; }
        return true;
    }

    // Cancel a queued/active construction and refund its cost (it wasn't completed).
    public void CancelConstruction(Construction c)
    {
        if (c == null || !building.Remove(c)) return;
        if (!GameMode.DevMode)
        {
            if (c.shipyardUpgrade) { int nl = c.body.shipyardLevel + 1; PlayerEconomy.Add(ResourceType.Metal, ShipyardUpgradeMetal(nl)); PlayerEconomy.Add(ResourceType.Energy, ShipyardUpgradeEnergy(nl)); }
            else if (c.labUpgrade) { int nl = c.body.researchCenterLevel + 1; PlayerEconomy.Add(ResourceType.Metal, LabUpgradeMetal(nl)); PlayerEconomy.Add(ResourceType.Energy, LabUpgradeEnergy(nl)); }
            else if (c.establishCity) { PlayerEconomy.Add(ResourceType.Metal, CityMetal); PlayerEconomy.Add(ResourceType.Energy, CityEnergy); }
            else { var info = BuildingDatabase.Get(c.type); PlayerEconomy.Add(ResourceType.Metal, DiscCost(info.costMetal)); PlayerEconomy.Add(ResourceType.Energy, DiscCost(info.costEnergy)); }
        }
    }

    void AdvanceConstruction(float dt)
    {
        for (int i = building.Count - 1; i >= 0; i--)
        {
            var c = building[i];
            if (!IsActive(c)) continue;   // queued behind another build on this world — wait its turn
            c.elapsed += dt;
            if (c.elapsed < c.duration) continue;
            building.RemoveAt(i);

            if (c.shipyardUpgrade)
            {
                c.body.shipyardLevel = Mathf.Clamp(c.body.shipyardLevel + 1, 1, Colony.MaxShipyardLevel);
                int lvl = c.body.shipyardLevel;
                SimpleAudio.Instance?.PlayNotify(NotifKind.Discovery);
                NotificationManager.Instance?.Push($"Shipyard on {c.body.name} is now Lv{lvl}",
                    $"{Colony.ShipyardPerk(lvl)}. This yard now contributes {BuildPower.ForLevel(lvl)} build power.", Fly(c.body), NotifKind.Discovery);
                UnitManager.Instance?.NotifyBuildChanged();
            }
            else if (c.labUpgrade)
            {
                c.body.researchCenterLevel = Mathf.Clamp(c.body.researchCenterLevel + 1, 1, Colony.MaxResearchCenterLevel);
                int lvl = c.body.researchCenterLevel;
                SimpleAudio.Instance?.PlayNotify(NotifKind.Discovery);
                NotificationManager.Instance?.Push($"Research centre on {c.body.name} is now Lv{lvl}",
                    $"This laboratory now contributes {ResearchCapacity.ForLevel(lvl)} research capacity — more projects at once, or one much larger one.",
                    Fly(c.body), NotifKind.Discovery);
                TechManager.NotifyChanged();
            }
            else if (c.establishCity)
            {
                if (!c.body.buildings.Contains((int)BuildingType.City)) c.body.buildings.Add((int)BuildingType.City);
                c.body.cities = Mathf.Max(1, c.body.cities);
                c.body.population = Mathf.Max(c.body.population, Population.ColonyStart(c.body, SpeciesManager.Current));
                c.body.claimProgress = Colony.ClaimProgress(c.body);
                SimpleAudio.Instance?.PlayNotify(NotifKind.Victory);
                NotificationManager.Instance?.Push($"City founded on {c.body.name}!",
                    "Your settlers established a colony city. Develop the world with mines, farms, shipyards and more.", Fly(c.body), NotifKind.Victory);
            }
            else
            {
                if (!c.body.buildings.Contains((int)c.type)) c.body.buildings.Add((int)c.type);
                if (c.type == BuildingType.Shipyard) c.body.shipyardLevel = Mathf.Max(1, c.body.shipyardLevel);
                if (c.type == BuildingType.ResearchCenter) c.body.researchCenterLevel = Mathf.Max(1, c.body.researchCenterLevel);
                var info = BuildingDatabase.Get(c.type);
                SimpleAudio.Instance?.PlayNotify(NotifKind.Info);
                NotificationManager.Instance?.Push($"{info.name} built on {c.body.name}", info.description, Fly(c.body), NotifKind.Info);
                if (c.type == BuildingType.Shipyard) UnitManager.Instance?.NotifyBuildChanged();
                if (c.type == BuildingType.ResearchCenter) TechManager.NotifyChanged();
            }
        }
    }

    // ---- Per-colony tick ----
    void TickColony(CelestialBody b, float dt)
    {
        if (b.cities < 1) b.cities = 1;
        // Output scales with the workforce. Tuned against the population UNIT scale (1 = 100,000), so a
        // one-million homeworld (10) starts near 0.9x and a large city world climbs toward ~2x.
        float popMult = 0.5f + b.population / 25f;
        popMult = Mathf.Min(popMult, 2.5f);   // diminishing returns; a megacity isn't 50x a town
        float oreRich = 1f + OreGenerator.OresOnBody(b).Count * 0.15f;

        float growth = 0f;
        foreach (int id in b.buildings)
        {
            var info = BuildingDatabase.Get((BuildingType)id);
            // Mines benefit from researched Industry tech (ore-yield bonus).
            float mine = id == (int)BuildingType.Mine ? oreRich * TechEffects.OreYieldMult : 1f;
            if (info.metalPerSec > 0f) PlayerEconomy.Add(ResourceType.Metal, info.metalPerSec * popMult * mine * dt);
            if (info.energyPerSec > 0f) PlayerEconomy.Add(ResourceType.Energy, info.energyPerSec * popMult * dt);
            if (info.waterPerSec > 0f) PlayerEconomy.Add(ResourceType.Water, info.waterPerSec * popMult * dt);
            researchAccum += info.researchPerSec * popMult * TechEffects.ResearchRateMult * dt;   // Science tech speeds research
            growth += info.popGrowthPerSec;
        }
        // Structures physically placed on the surface grid produce on top of the abstract colony
        // buildings, each scaled by how well it was SITED (see SurfaceBuildManager.EfficiencyAt) —
        // a mine on a rich seam earns its keep, one on dead rock never will.
        SurfaceBuildManager.TickOutput(b, dt);
        researchAccum += SurfaceBuildManager.ResearchPerSec(b) * TechEffects.ResearchRateMult * dt;
        growth += SurfaceBuildManager.PopGrowthPerSec(b);

        if (researchAccum >= 1f) { int p = Mathf.FloorToInt(researchAccum); researchAccum -= p; ResearchManager.AddPoints(p); }

        // ---- Population ----
        // A real birth rate (see Population.BirthRate): housing x satisfaction x habitability x room,
        // multiplied, so an unhappy colony doesn't grow just because it has farms.
        //
        // Accumulated FRACTIONALLY. This previously did
        //     b.population += Mathf.Max(1, Mathf.RoundToInt(grown))
        // and `grown` is almost always well under 1 — so RoundToInt gave 0, the Max forced +1 unit
        // every tick, and every multiplier above it was thrown away. One unit is 100,000 people, so
        // that was 100k people a second on every colony regardless of how well it was run. The whole
        // satisfaction system was decorative.
        int capacity = Colony.PopTarget(b);
        float rate = Population.BirthRate(b, growth);
        if (rate > 0f && b.population < capacity)
        {
            b.popAccum += rate * dt;
            if (b.popAccum >= 1f)
            {
                int whole = Mathf.FloorToInt(b.popAccum);
                b.popAccum -= whole;
                b.population = Mathf.Min(capacity, b.population + whole);
            }
        }

        // A research centre analyses ore samples that ships have brought to this world. The analysis
        // COSTS research points, and a bigger laboratory can work through more of them at once — a
        // level-1 centre chews through one sample per tick, a level-5 one clears a backlog quickly.
        // Samples it can't afford stay in the ship's hold rather than being silently consumed.
        if (b.researchCenterLevel >= 1 && b.units != null)
        {
            int budget = b.researchCenterLevel;
            foreach (var u in b.units)
            {
                if (budget <= 0) break;
                if (u.samples.Count == 0) continue;
                var kept = new List<int>();
                foreach (var id in u.samples)
                {
                    if (budget > 0 && ResearchManager.TryResearchSample((OreType)id)) budget--;
                    else kept.Add(id);
                }
                u.samples.Clear();
                u.samples.AddRange(kept);
            }
        }

        b.claimProgress = Colony.ClaimProgress(b);
    }

    // ---- Terraforming ----
    void TickTerraform(CelestialBody b, float dt)
    {
        bool present = b.owner == FactionManager.Player || HasPlayerUnit(b);
        if (!present) { b.terraforming = false; return; }

        float ceiling = Colony.TerraformCeiling(b);
        if (b.habitability >= ceiling - 0.01f)
        {
            b.terraforming = false;
            NotificationManager.Instance?.Push($"Terraforming complete on {b.name}",
                $"Habitability reached its ceiling of {ceiling:F0}% for {SpeciesManager.Current.name}.", Fly(b), NotifKind.Discovery);
            return;
        }

        // Terraforming is slow on its own; each Terraformer ship present speeds it up and they STACK,
        // so parking several on the same world finishes it much faster (they burn resources faster too).
        int tformers = CountTerraformers(b);
        float stationAura = StationEffects.TerraformAuraAt(b);   // terraforming stations add huge speed on top
        float rate = Mathf.Min(1f + tformers, 6f) + stationAura; // 1× baseline, +1× per terraformer (capped ~5), + stations
        // Empire level and Expansion research together decide how fast a world can be reshaped at all —
        // slow and grinding early, near-immediate for a mature empire (see TerraformProjects.SpeedFactor).
        float gain = 1.1f * rate * TerraformProjects.SpeedFactor() * dt;
        float water = gain * 4f, energy = gain * 3f, metal = gain * 2f;   // hauled water + power + materials
        if (PlayerEconomy.Get(ResourceType.Water) >= water &&
            PlayerEconomy.Get(ResourceType.Energy) >= energy &&
            PlayerEconomy.Get(ResourceType.Metal) >= metal)
        {
            PlayerEconomy.Add(ResourceType.Water, -water);
            PlayerEconomy.Add(ResourceType.Energy, -energy);
            PlayerEconomy.Add(ResourceType.Metal, -metal);
            b.habitability = Mathf.Min(ceiling, b.habitability + gain);
            b.isHabitable = b.habitability >= Colony.FoundThreshold;
        }
        else
        {
            b.terraforming = false;
            NotificationManager.Instance?.Push($"Terraforming paused on {b.name}",
                "Out of resources. Terraforming needs water, energy and metal.", Fly(b), NotifKind.Danger);
        }
    }

    public bool ToggleTerraform(CelestialBody b)
    {
        if (b == null) return false;
        if (b.terraforming) { b.terraforming = false; return false; }
        if (!Colony.CanReachLivable(b) && b.terraformability <= b.habitability + 0.5f)
        {
            NotificationManager.Instance?.Push($"{b.name} can't be terraformed",
                $"Its terraformability ceiling ({b.terraformability:F0}%) is too low for {SpeciesManager.Current.name}.", null, NotifKind.Info);
            return false;
        }
        b.terraforming = true;
        return true;
    }

    static bool HasPlayerUnit(CelestialBody b)
    {
        if (b.units == null) return false;
        foreach (var u in b.units) if (u.owner == FactionManager.Player) return true;
        return false;
    }

    public static bool HasTerraformerPresent(CelestialBody b) => CountTerraformers(b) > 0;

    public static int CountTerraformers(CelestialBody b)
    {
        if (b == null || b.units == null) return 0;
        int n = 0;
        foreach (var u in b.units) if (u.owner == FactionManager.Player && u.Info.canTerraform) n++;
        return n;
    }

    System.Action Fly(CelestialBody b) => () =>
    {
        if (b != null && b.visualObject != null) CameraController.Instance?.FocusAndZoom(b.visualObject.transform, b.surfaceSize, true);
        PlanetUI.Instance?.Show(b);
    };
}
