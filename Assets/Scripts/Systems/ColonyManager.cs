using System.Collections.Generic;
using UnityEngine;

// One in-progress building on a colony.
public class Construction
{
    public CelestialBody body;
    public BuildingType type;
    public float elapsed, duration;
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

    // The objectives that must ALL be met for a colony to be "fully established" (a full claim).
    public static List<ColonyObjective> Objectives(CelestialBody b)
    {
        var list = new List<ColonyObjective>();
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
        building.Add(new Construction { body = b, type = t, duration = info.buildTime });
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
            if (c.elapsed >= c.duration)
            {
                if (!c.body.buildings.Contains((int)c.type)) c.body.buildings.Add((int)c.type);
                building.RemoveAt(i);
                var info = BuildingDatabase.Get(c.type);
                SimpleAudio.Instance?.PlayNotify(NotifKind.Info);
                NotificationManager.Instance?.Push($"{info.name} built on {c.body.name}", info.description, Fly(c.body), NotifKind.Info);
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

        float gain = 1.1f * dt;                       // habitability points this step
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

    System.Action Fly(CelestialBody b) => () =>
    {
        if (b != null && b.visualObject != null) CameraController.Instance?.FocusAndZoom(b.visualObject.transform, b.surfaceSize, true);
        PlanetUI.Instance?.Show(b);
    };
}
