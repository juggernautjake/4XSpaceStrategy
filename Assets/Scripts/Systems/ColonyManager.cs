using System.Collections.Generic;
using UnityEngine;

// One in-progress project on a colony: a building, the founding of a city, or a shipyard upgrade.
public class Construction
{
    public CelestialBody body;
    public BuildingType type;
    public bool establishCity;     // founding the first city on an owned-but-empty world
    public bool shipyardUpgrade;   // raising this world's shipyard tier
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

    public static int PopTarget(CelestialBody b) => 40 + b.surfaceSize * 6;
    public const int BuildingTarget = 4;   // city + 3 more

    // Shipyard tiers.
    public const int MaxShipyardLevel = 3;

    // Your best shipyard tier across all owned worlds (0 if you have none). Gates which ships you can
    // build and how fast. The home world always provides at least tier 1.
    public static int PlayerMaxShipyardLevel()
    {
        int best = 0;
        if (SystemContext.Galaxy != null)
            foreach (var b in SystemContext.AllBodies())
                if (b.owner == FactionManager.Player && b.shipyardLevel > best) best = b.shipyardLevel;
        return best;
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
        list.Add(new ColonyObjective { label = "Grow the population", done = b.population >= pt, detail = $"{b.population}/{pt}" });
        list.Add(new ColonyObjective { label = "Develop infrastructure", done = b.buildings.Count >= BuildingTarget, detail = $"{b.buildings.Count}/{BuildingTarget} buildings" });
        return list;
    }

    public static float ClaimProgress(CelestialBody b)
    {
        var objs = Objectives(b);
        int done = 0; foreach (var o in objs) if (o.done) done++;
        return objs.Count > 0 ? done / (float)objs.Count : 0f;
    }

    public static bool IsFullyEstablished(CelestialBody b) => ClaimProgress(b) >= 1f;

    // The highest habitability terraforming could reach for the current species (its ceiling).
    public static float TerraformCeiling(CelestialBody b) => Mathf.Max(b.habitability, b.terraformability);

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

        if (SystemContext.Galaxy == null) return;
        foreach (var b in SystemContext.AllBodies())
        {
            if (b.owner == FactionManager.Player) TickColony(b, step);
            if (b.terraforming) TickTerraform(b, step);
        }
    }

    // ---- Construction ----
    public bool IsUnique(BuildingType t) => true;   // one of each per world (keeps colonies readable)

    public bool CanBuild(CelestialBody b, BuildingType t, out string reason)
    {
        reason = null;
        if (b == null || b.owner != FactionManager.Player) { reason = "colony not yours"; return false; }
        if (!b.buildings.Contains((int)BuildingType.City)) { reason = "found a city first"; return false; }
        if (t == BuildingType.City) { reason = "already the city"; return false; }
        if (b.buildings.Contains((int)t)) { reason = "already built"; return false; }
        if (IsConstructing(b, t)) { reason = "under construction"; return false; }
        var info = BuildingDatabase.Get(t);
        if (!GameMode.DevMode && !PlayerEconomy.CanAfford(info.costMetal, info.costEnergy))
        { reason = $"need {info.costMetal} metal, {info.costEnergy} energy"; return false; }
        return true;
    }

    public bool StartBuilding(CelestialBody b, BuildingType t)
    {
        if (!CanBuild(b, t, out _)) return false;
        var info = BuildingDatabase.Get(t);
        if (!GameMode.DevMode && !PlayerEconomy.Spend(info.costMetal, info.costEnergy)) return false;
        building.Add(new Construction { body = b, type = t, duration = info.buildTime, Label = $"Building {info.name}" });
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

    // ---- Shipyard upgrades (level 2 unlocks Mk II ships, level 3 the Terraformer) ----
    public static int ShipyardUpgradeMetal(int toLevel) => toLevel == 2 ? 200 : 350;
    public static int ShipyardUpgradeEnergy(int toLevel) => toLevel == 2 ? 150 : 280;
    public static float ShipyardUpgradeTime(int toLevel) => toLevel == 2 ? 30f : 45f;

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

    public bool IsConstructing(CelestialBody b, BuildingType t)
    {
        foreach (var c in building) if (c.body == b && c.type == t) return true;
        return false;
    }

    public Construction ConstructionFor(CelestialBody b)
    {
        foreach (var c in building) if (c.body == b) return c;
        return null;
    }

    void AdvanceConstruction(float dt)
    {
        for (int i = building.Count - 1; i >= 0; i--)
        {
            var c = building[i];
            c.elapsed += dt;
            if (c.elapsed < c.duration) continue;
            building.RemoveAt(i);

            if (c.shipyardUpgrade)
            {
                c.body.shipyardLevel = Mathf.Clamp(c.body.shipyardLevel + 1, 1, Colony.MaxShipyardLevel);
                string perk = c.body.shipyardLevel == 2 ? "Mk II ships unlocked." : "Terraformers unlocked.";
                SimpleAudio.Instance?.PlayNotify(NotifKind.Discovery);
                NotificationManager.Instance?.Push($"Shipyard on {c.body.name} is now Lv{c.body.shipyardLevel}", perk, Fly(c.body), NotifKind.Discovery);
                UnitManager.Instance?.NotifyBuildChanged();
            }
            else if (c.establishCity)
            {
                if (!c.body.buildings.Contains((int)BuildingType.City)) c.body.buildings.Add((int)BuildingType.City);
                c.body.cities = Mathf.Max(1, c.body.cities);
                c.body.population = Mathf.Max(c.body.population, Mathf.Max(20, c.body.surfaceSize * 3));
                c.body.claimProgress = Colony.ClaimProgress(c.body);
                SimpleAudio.Instance?.PlayNotify(NotifKind.Victory);
                NotificationManager.Instance?.Push($"City founded on {c.body.name}!",
                    "Your settlers established a colony city. Develop the world with mines, farms, shipyards and more.", Fly(c.body), NotifKind.Victory);
            }
            else
            {
                if (!c.body.buildings.Contains((int)c.type)) c.body.buildings.Add((int)c.type);
                if (c.type == BuildingType.Shipyard) c.body.shipyardLevel = Mathf.Max(1, c.body.shipyardLevel);
                var info = BuildingDatabase.Get(c.type);
                SimpleAudio.Instance?.PlayNotify(NotifKind.Info);
                NotificationManager.Instance?.Push($"{info.name} built on {c.body.name}", info.description, Fly(c.body), NotifKind.Info);
                if (c.type == BuildingType.Shipyard) UnitManager.Instance?.NotifyBuildChanged();
            }
        }
    }

    // ---- Per-colony tick ----
    void TickColony(CelestialBody b, float dt)
    {
        if (b.cities < 1) b.cities = 1;
        float popMult = 0.5f + b.population / 200f;
        float oreRich = 1f + OreGenerator.OresOnBody(b).Count * 0.15f;

        float growth = 0f;
        foreach (int id in b.buildings)
        {
            var info = BuildingDatabase.Get((BuildingType)id);
            float mine = id == (int)BuildingType.Mine ? oreRich : 1f;
            if (info.metalPerSec > 0f) PlayerEconomy.Add(ResourceType.Metal, info.metalPerSec * popMult * mine * dt);
            if (info.energyPerSec > 0f) PlayerEconomy.Add(ResourceType.Energy, info.energyPerSec * popMult * dt);
            if (info.waterPerSec > 0f) PlayerEconomy.Add(ResourceType.Water, info.waterPerSec * popMult * dt);
            researchAccum += info.researchPerSec * popMult * dt;
            growth += info.popGrowthPerSec;
        }
        if (researchAccum >= 1f) { int p = Mathf.FloorToInt(researchAccum); researchAccum -= p; ResearchManager.AddPoints(p); }

        int target = Colony.PopTarget(b);
        if (b.habitability >= Colony.FoundThreshold && b.population < target)
            b.population = Mathf.Min(target, b.population + Mathf.Max(1, Mathf.RoundToInt(growth * dt)));

        // A research centre processes ore samples that ships have brought to this world.
        if (b.buildings.Contains((int)BuildingType.ResearchCenter) && b.units != null)
            foreach (var u in b.units)
            {
                if (u.samples.Count == 0) continue;
                foreach (var id in u.samples) ResearchManager.ForceResearch((OreType)id);
                u.samples.Clear();
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

        // A Terraformer ship present at the world roughly doubles the pace of the project.
        bool terraformer = HasTerraformerPresent(b);
        float gain = (terraformer ? 2.2f : 1.1f) * dt;   // habitability points this step
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

    public static bool HasTerraformerPresent(CelestialBody b)
    {
        if (b == null || b.units == null) return false;
        foreach (var u in b.units) if (u.owner == FactionManager.Player && u.Info.canTerraform) return true;
        return false;
    }

    System.Action Fly(CelestialBody b) => () =>
    {
        if (b != null && b.visualObject != null) CameraController.Instance?.FocusAndZoom(b.visualObject.transform, b.surfaceSize, true);
        PlanetUI.Instance?.Show(b);
    };
}
