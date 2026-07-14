using System.Collections.Generic;
using UnityEngine;

// One planetary-engineering project under way on a world.
public class TerraformJob
{
    public CelestialBody body;
    public TerraformProjectType type;
    public float elapsed, duration;
    public bool paused;
    public int metalPaid, energyPaid, waterPaid;   // exact refund if cancelled

    public TerraformProjectInfo Info => TerraformProjectDatabase.Get(type);
    public float Progress => duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
    public float Remaining => Mathf.Max(0f, duration - elapsed);
}

// Runs the terraforming PROJECTS — the one-off engineering feats that raise a world's habitability
// ceiling (see Terraforming.cs). Terraformer ships then grind habitability up toward that ceiling;
// that half lives in ColonyManager.TickTerraform.
//
// A project needs a friendly presence at the world (you can't melt a world's ice caps from across the
// galaxy), the required research, and a large lump of resources paid up front.
public class TerraformManager : MonoBehaviour
{
    public static TerraformManager Instance;

    readonly List<TerraformJob> jobs = new List<TerraformJob>();
    public IReadOnlyList<TerraformJob> Jobs => jobs;
    public event System.Action OnChanged;

    public static void Create()
    {
        if (Instance != null) return;
        new GameObject("TerraformManager").AddComponent<TerraformManager>();
    }

    void Awake() { Instance = this; }

    public List<TerraformJob> JobsFor(CelestialBody b)
    {
        var l = new List<TerraformJob>();
        foreach (var j in jobs) if (j.body == b) l.Add(j);
        return l;
    }

    public bool IsRunning(CelestialBody b, TerraformProjectType t)
    {
        foreach (var j in jobs) if (j.body == b && j.type == t) return true;
        return false;
    }

    public bool CanStart(CelestialBody b, TerraformProjectType t, out string reason)
    {
        reason = null;
        var p = TerraformProjectDatabase.Get(t);
        if (p == null || b == null) { reason = "unknown project"; return false; }
        if (TerraformProjects.IsDone(b, t)) { reason = "already completed here"; return false; }
        if (IsRunning(b, t)) { reason = "already under way"; return false; }

        // You have to be there to do the work.
        if (b.owner != FactionManager.Player && !HasPlayerPresence(b))
        { reason = "send a ship to this world first"; return false; }

        if (!GameMode.DevMode && !string.IsNullOrEmpty(p.requiredTech) && !TechManager.IsResearched(p.requiredTech))
        {
            var tech = TechDatabase.Get(p.requiredTech);
            reason = $"research {(tech != null ? tech.name : p.requiredTech)} first";
            return false;
        }

        var s = SpeciesManager.Current;
        var issues = TerraformDiagnosis.Analyze(b, s);
        if (!TerraformDiagnosis.Has(issues, p.solves))
        { reason = $"this world doesn't have that problem ({TerraformDiagnosis.Describe(p.solves).ToLower()})"; return false; }
        if (p.applies != null && !p.applies(b, s))
        { reason = "not possible on this kind of world"; return false; }

        int m = TerraformProjects.MetalCost(p, b), e = TerraformProjects.EnergyCost(p, b), w = TerraformProjects.WaterCost(p, b);
        if (!GameMode.DevMode &&
            (PlayerEconomy.Get(ResourceType.Metal) < m || PlayerEconomy.Get(ResourceType.Energy) < e ||
             PlayerEconomy.Get(ResourceType.Water) < w))
        { reason = $"need {m} metal, {e} energy" + (w > 0 ? $", {w} water" : ""); return false; }

        return true;
    }

    // NOT named Start: MonoBehaviour reserves Start/Awake/Update/etc. as magic methods, and Unity
    // reflects over them at load — finding one with parameters logs
    // "Script error (TerraformManager): Start() can not take parameters." and the component is broken.
    public bool Begin(CelestialBody b, TerraformProjectType t)
    {
        if (!CanStart(b, t, out _)) return false;
        var p = TerraformProjectDatabase.Get(t);

        int m = TerraformProjects.MetalCost(p, b), e = TerraformProjects.EnergyCost(p, b), w = TerraformProjects.WaterCost(p, b);
        if (!GameMode.DevMode)
        {
            PlayerEconomy.Add(ResourceType.Metal, -m);
            PlayerEconomy.Add(ResourceType.Energy, -e);
            if (w > 0) PlayerEconomy.Add(ResourceType.Water, -w);
        }

        jobs.Add(new TerraformJob
        {
            body = b, type = t,
            duration = TerraformProjects.Duration(p, b),
            metalPaid = GameMode.DevMode ? 0 : m,
            energyPaid = GameMode.DevMode ? 0 : e,
            waterPaid = GameMode.DevMode ? 0 : w
        });

        NotificationManager.Instance?.Push($"{p.name} begun on {b.name}", p.description, Fly(b), NotifKind.Info);
        OnChanged?.Invoke();
        return true;
    }

    public void SetPaused(TerraformJob j, bool paused)
    {
        if (j == null || j.paused == paused) return;
        j.paused = paused;
        OnChanged?.Invoke();
    }

    // Abandoning an unfinished project returns everything that was poured into it.
    public void Cancel(TerraformJob j)
    {
        if (j == null || !jobs.Remove(j)) return;
        if (j.metalPaid > 0) PlayerEconomy.Add(ResourceType.Metal, j.metalPaid);
        if (j.energyPaid > 0) PlayerEconomy.Add(ResourceType.Energy, j.energyPaid);
        if (j.waterPaid > 0) PlayerEconomy.Add(ResourceType.Water, j.waterPaid);
        OnChanged?.Invoke();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || jobs.Count == 0) return;

        bool changed = false;
        for (int i = jobs.Count - 1; i >= 0; i--)
        {
            var j = jobs[i];
            if (j.paused) continue;
            j.elapsed += dt;
            if (j.elapsed < j.duration) continue;

            jobs.RemoveAt(i);
            Complete(j);
            changed = true;
        }
        if (changed) OnChanged?.Invoke();
    }

    void Complete(TerraformJob j)
    {
        var b = j.body;
        var p = j.Info;
        TerraformProjects.MarkDone(b, j.type);

        // Some projects physically change the world, so the model has to change with them — otherwise
        // the diagnosis would keep reporting the same fault forever.
        ApplyPhysicalEffect(b, j.type);

        float ceiling = Colony.TerraformCeiling(b);
        SimpleAudio.Instance?.PlayNotify(NotifKind.Discovery);
        NotificationManager.Instance?.Push($"{p.name} complete on {b.name}",
            $"{TerraformDiagnosis.Describe(p.solves)} resolved. {b.name}'s habitability ceiling is now {ceiling:F0}% for {SpeciesManager.Current.name}" +
            (b.terraforming ? " — terraforming continues toward it." : " — send a terraformer to raise it."),
            Fly(b), NotifKind.Discovery);
        OnChanged?.Invoke();
    }

    // The world itself changes: melting the caps really does give it water, scrubbing the sky really
    // does clear the poison. Without this the fault would be "fixed" but still diagnosed.
    static void ApplyPhysicalEffect(CelestialBody b, TerraformProjectType t)
    {
        switch (t)
        {
            case TerraformProjectType.HaulWater:
            case TerraformProjectType.MeltIceCaps:
            case TerraformProjectType.TapAquifers:
            case TerraformProjectType.CometBombardment:
                if (b.resources != null) b.resources.Add(ResourceType.Water, 220f);
                break;

            case TerraformProjectType.SpinUp:
                b.spinSpeed = Mathf.Max(b.spinSpeed, 12f);
                RefreshSpin(b);
                break;
            case TerraformProjectType.SpinDown:
                b.spinSpeed = Mathf.Min(b.spinSpeed, 20f);
                RefreshSpin(b);
                break;
            case TerraformProjectType.AxialCorrection:
            case TerraformProjectType.CaptureMoon:
            case TerraformProjectType.RemoveMoon:
                b.inclination = Mathf.Clamp(b.inclination, -12f, 12f);
                RefreshOrbit(b);
                break;

            // The orbital migrations genuinely move the planet, which re-scores its habitability from
            // scratch — that is the entire point of spending this much on one world.
            case TerraformProjectType.OrbitShiftOut:
                b.orbitRadius *= 1.45f;
                b.distanceFromStar *= 1.45f;
                RescoreOrbit(b);
                break;
            case TerraformProjectType.OrbitShiftIn:
                b.orbitRadius *= 0.68f;
                b.distanceFromStar *= 0.68f;
                RescoreOrbit(b);
                break;

            // Stripping a world's oceans really does dry it out — and an ocean world without an ocean
            // is a rocky one. The recovered water is shipped home rather than thrown away.
            case TerraformProjectType.HydrosphereVenting:
                if (b.resources != null)
                {
                    float had = b.resources.Get(ResourceType.Water);
                    float recovered = had * 0.45f;
                    b.resources.Add(ResourceType.Water, -had * 0.9f);
                    PlayerEconomy.Add(ResourceType.Water, recovered);
                }
                if (b.type == CelestialBodyType.OceanPlanet) Reshape(b, CelestialBodyType.RockyPlanet);
                else RescoreType(b);
                break;

            case TerraformProjectType.CrustalSequestration:
                if (b.resources != null) b.resources.Add(ResourceType.Water, -b.resources.Get(ResourceType.Water) * 0.7f);
                if (b.type == CelestialBodyType.OceanPlanet) Reshape(b, CelestialBodyType.RockyPlanet);
                else RescoreType(b);
                break;

            // The big one: rebuild the world into the kind of place this species actually wants. This is
            // the only thing that moves a species' TYPE AFFINITY, which is what a low score really is.
            case TerraformProjectType.WorldRemodelling:
                var want = SpeciesManager.Current != null ? SpeciesManager.Current.BestType() : b.type;
                Reshape(b, want);
                break;
        }
    }

    // Physically convert a world to another type and re-score everything that depends on it. Also
    // regenerates the surface so the map matches the new world, and refreshes its 3D appearance.
    static void Reshape(CelestialBody b, CelestialBodyType to)
    {
        if (b == null || b.type == to) { RescoreType(b); return; }
        b.type = to;

        // The surface is derived from the body type, so it has to be rebuilt — deterministically, from
        // the same terrain seed, so the world keeps its identity (same continents, new climate).
        b.surface = PlanetTerrainGenerator.GenerateSurface(b);
        OreGenerator.Populate(b);

        RescoreType(b);

        if (b.visualObject != null) PlanetAppearance.Apply(b, b.visualObject);
    }

    // Re-score habitability/terraformability after the world itself has changed.
    static void RescoreType(CelestialBody b)
    {
        var star = b != null ? b.hostStar : null;
        var s = SpeciesManager.Current;
        if (b == null || star == null || s == null) return;
        if (!b.habitabilityLocked)
        {
            b.habitability = Habitability.Rate(star, s, b.type, b.distanceFromStar);
            b.isHabitable = Habitability.IsHabitable(star, s, b.type, b.distanceFromStar);
        }
        b.terraformability = Habitability.Terraformability(star, s, b);
    }

    static void RefreshSpin(CelestialBody b)
    {
        var oc = b.visualObject != null ? b.visualObject.GetComponent<OrbitController>() : null;
        oc?.SetSpin(b.spinSpeed);
    }

    static void RefreshOrbit(CelestialBody b)
    {
        var oc = b.visualObject != null ? b.visualObject.GetComponent<OrbitController>() : null;
        if (oc != null) { oc.SetInclination(b.inclination); oc.ForceRingRedraw(); }
    }

    // Moving a world changes how much starlight it gets, so its natural habitability and its
    // terraformability both have to be recomputed for the current species.
    static void RescoreOrbit(CelestialBody b)
    {
        var oc = b.visualObject != null ? b.visualObject.GetComponent<OrbitController>() : null;
        if (oc != null) { oc.SetRadius(b.orbitRadius); oc.ForceRingRedraw(); }

        var star = b.hostStar;
        var s = SpeciesManager.Current;
        if (star == null || s == null) return;
        if (!b.habitabilityLocked)
        {
            b.habitability = Habitability.Rate(star, s, b.type, b.distanceFromStar);
            b.isHabitable = Habitability.IsHabitable(star, s, b.type, b.distanceFromStar);
        }
        b.terraformability = Habitability.Terraformability(star, s, b);
    }

    static bool HasPlayerPresence(CelestialBody b)
    {
        if (b == null || b.units == null) return false;
        foreach (var u in b.units) if (u.owner == FactionManager.Player) return true;
        return false;
    }

    System.Action Fly(CelestialBody b) => () =>
    {
        if (b != null && b.visualObject != null)
            CameraController.Instance?.FocusAndZoom(b.visualObject.transform, b.surfaceSize, true);
        PlanetUI.Instance?.Show(b);
    };

    // ---- Save / load ----
    public List<TerraformJobDTO> Export()
    {
        var l = new List<TerraformJobDTO>();
        foreach (var j in jobs)
            l.Add(new TerraformJobDTO
            {
                bodyId = j.body != null ? j.body.id : -1, type = (int)j.type,
                elapsed = j.elapsed, duration = j.duration, paused = j.paused,
                metalPaid = j.metalPaid, energyPaid = j.energyPaid, waterPaid = j.waterPaid
            });
        return l;
    }

    public void Import(List<TerraformJobDTO> dtos, Dictionary<int, CelestialBody> byId)
    {
        jobs.Clear();
        if (dtos != null)
            foreach (var d in dtos)
            {
                if (byId == null || !byId.TryGetValue(d.bodyId, out var b) || b == null) continue;
                jobs.Add(new TerraformJob
                {
                    body = b, type = (TerraformProjectType)d.type,
                    elapsed = d.elapsed, duration = d.duration, paused = d.paused,
                    metalPaid = d.metalPaid, energyPaid = d.energyPaid, waterPaid = d.waterPaid
                });
            }
        OnChanged?.Invoke();
    }
}
