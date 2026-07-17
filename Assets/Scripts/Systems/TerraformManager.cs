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

    // Orbit-migration animation. An orbit-shift project walks the world from orbitStart to the
    // pre-clamped orbitTarget over its duration so you SEE it move. -1 means "not an orbit migration"
    // (or a pre-feature save), which falls back to the old instant jump at completion.
    public float orbitStart = -1f, orbitTarget = -1f;
    public bool IsOrbitShift => orbitTarget >= 0f;

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

    // Progressive morph throttle. Regenerating a surface is ~12k cells; doing it every frame would melt
    // the CPU, but ~once a second reads as motion, and it matches the cadence the habitability grind
    // (ColonyManager.TickTerraform) already regenerates at.
    const float MorphInterval = 1f;
    float morphTimer;

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

        var job = new TerraformJob
        {
            body = b, type = t,
            duration = TerraformProjects.Duration(p, b),
            metalPaid = GameMode.DevMode ? 0 : m,
            energyPaid = GameMode.DevMode ? 0 : e,
            waterPaid = GameMode.DevMode ? 0 : w
        };
        InitOrbitMigration(job);
        jobs.Add(job);

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

        // Abandoning a remodel drops the transition: the world keeps its original type and the half-formed
        // new world melts back off the map on the next regen.
        if (j.type == TerraformProjectType.WorldRemodelling && j.body != null)
        {
            j.body.remodelToType = -1; j.body.remodelT = 0f;
            TerraformVisuals.Advance(j.body, SpeciesManager.Current, force: true);
        }

        OnChanged?.Invoke();
    }

    // The type a Planetary Remodelling turns a world INTO. Undirected today (the species' best-fit type);
    // the directed target chooser will store an explicit choice on the job in a later slice.
    static CelestialBodyType ResolveRemodelTarget(TerraformJob j)
    {
        var s = SpeciesManager.Current;
        if (s != null) return s.BestType();
        return j != null && j.body != null ? j.body.type : CelestialBodyType.RockyPlanet;
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

        // Animate any orbit-migration jobs smoothly every frame — cheap (orbit ring + habitability
        // rescore, no surface regen). This is what makes a migrating world visibly spiral in or out.
        //
        // Collision safety: the radius is re-clamped against the neighbours' CURRENT positions every
        // frame, not just against where they sat when the project began. So even two worlds migrating in
        // the same system at once can never overlap mid-animation — each simply stops at the other's band
        // edge rather than passing through it, and resumes when the other moves on. A lone migrator is
        // unaffected (its lerp already lies inside the allowed band).
        for (int i = 0; i < jobs.Count; i++)
        {
            var j = jobs[i];
            if (j == null || j.paused || !j.IsOrbitShift || j.body == null) continue;
            if (Mathf.Approximately(j.orbitStart, j.orbitTarget)) continue;   // no room to move: nothing to animate

            float r = Mathf.Lerp(j.orbitStart, j.orbitTarget, j.Progress);
            var sys = j.body.system != null ? j.body.system.bodies : null;
            if (sys != null && OrbitSafety.ClampRadius(sys, j.body, j.body.hostStar, r, out float rSafe)) r = rSafe;
            SetOrbitRadiusLive(j.body, r);
        }

        // Keep each directed-remodel transition current every frame so the generator can dither the world
        // from its old type toward the target as the project loads, and Compose can walk the climate to
        // match. Cheap (two field writes per remodel job); completion/cancel clear it.
        for (int i = 0; i < jobs.Count; i++)
        {
            var j = jobs[i];
            if (j == null || j.type != TerraformProjectType.WorldRemodelling || j.body == null) continue;
            j.body.remodelToType = (int)ResolveRemodelTarget(j);
            if (!j.paused) j.body.remodelT = j.Progress;
        }

        // Progressive morph: while projects load, walk each affected world's climate toward the sum of
        // what its projects will do — previewed by their loading bars via TerraformClimate/Compose — so
        // water fills WHILE a Water Convoy runs and the seas recede WHILE Hydrosphere Venting runs.
        // Throttled to MorphInterval across ALL jobs (not per job) to protect the frame budget.
        morphTimer += dt;
        if (morphTimer >= MorphInterval)
        {
            morphTimer = 0f;
            MorphActiveWorlds();
        }

        if (changed) OnChanged?.Invoke();
    }

    // Recompose + regenerate every distinct world that has an unpaused job, once per throttle tick. A
    // paused job contributes nothing to the preview (TerraformClimate.Accumulated skips it), so a world
    // whose only jobs are paused is left alone.
    void MorphActiveWorlds()
    {
        var s = SpeciesManager.Current;
        for (int i = 0; i < jobs.Count; i++)
        {
            var j = jobs[i];
            if (j == null || j.paused || j.body == null) continue;

            // Only the first unpaused job per body triggers the regen — the regen already reflects every
            // running job on that world through Compose, so a second pass would just repaint the same map.
            bool firstForBody = true;
            for (int k = 0; k < i; k++)
                if (jobs[k] != null && !jobs[k].paused && jobs[k].body == j.body) { firstForBody = false; break; }
            if (firstForBody) TerraformVisuals.Advance(j.body, s, force: true);
        }
    }

    void Complete(TerraformJob j)
    {
        var b = j.body;
        var p = j.Info;
        TerraformProjects.MarkDone(b, j.type);

        // Orbit migrations were animated over the project's duration (see Update). Finalize by snapping
        // to the pre-clamped target and re-asserting system spacing. A pre-feature job that never
        // animated (orbitTarget < 0) falls back to the old instant jump so old saves still complete.
        if (j.type == TerraformProjectType.OrbitShiftOut || j.type == TerraformProjectType.OrbitShiftIn)
        {
            float factor = j.type == TerraformProjectType.OrbitShiftOut ? 1.45f : 0.68f;
            MigrateTo(b, j.IsOrbitShift ? j.orbitTarget : b.orbitRadius * factor);
        }

        // Planetary Remodelling: the surface has been dithering toward the target type as the project ran
        // (see Update + PlanetTerrainGenerator). Finalize by clearing the transition (so the generator
        // stops dithering — the world simply IS the new type now) and actually converting the world's
        // TYPE. Its NEW natural baseline becomes the target type's climate, so the ongoing habitability
        // grind blends from there, not from the world's long-gone original, and it doesn't visibly revert
        // the instant the project finishes.
        if (j.type == TerraformProjectType.WorldRemodelling)
        {
            var target = ResolveRemodelTarget(j);
            b.remodelToType = -1; b.remodelT = 0f;
            b.naturalParams = TerraformVisuals.TypeClimate(target);
            Reshape(b, target);
        }

        // Some projects physically change the world, so the model has to change with them — otherwise
        // the diagnosis would keep reporting the same fault forever.
        ApplyPhysicalEffect(b, j.type);

        // Bake the finished project's climate into the world. The running-job preview was pulling the
        // world most of the way there already (delta × progress, progress ≈ 1 at the end); now the job
        // leaves the list and its FULL delta is counted as completed instead, so recomposing here makes
        // the hand-off seamless rather than a snap. (Reshape already regenerated for type changes; this
        // just re-asserts terrainParams from the completed-project set.)
        TerraformVisuals.Advance(b, SpeciesManager.Current, force: true);

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
            // scratch — that is the entire point of spending this much on one world. They are also the
            // only thing in the game that moves an orbit at RUNTIME, so they're the one place a planet
            // could be walked straight into a neighbour: the move is clamped to the room its neighbours
            // leave (see MigrateTo).
            // OrbitShiftOut / OrbitShiftIn are handled in Complete (animated over the project's duration
            // and finalized there), so they intentionally do nothing here.

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

            // WorldRemodelling is handled in Complete (dithered over the project's duration and finalized
            // there — see the remodel block), so it intentionally does nothing here.
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
        // The survey indexes are derived from the terrain field and their per-world distributions are
        // cached. Remodelling a world changes that field, so the cache now describes the planet this
        // used to be — drop it or the overlays lie.
        SurfaceIndex.InvalidateStats(b);
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

    // Walk a planet to a new orbit, but never through one of its neighbours. If the neighbours leave no
    // room at all the planet stays put — the project still counts as done (it raised the ceiling), it
    // just couldn't move as far as it wanted. Better a short migration than two worlds sharing an orbit.
    // Work out where an orbit-shift project will end up, ONCE, when it begins — the desired 45% out / 32%
    // in, clamped into the room the neighbours actually leave (OrbitSafety). Storing start+target on the
    // job lets Update animate a straight, collision-free walk between them, and lets a mid-migration save
    // resume exactly. If there's no room at all, target == the current radius and the world simply doesn't
    // move (the project still completes and still raised the ceiling — same as the old MigrateTo).
    static void InitOrbitMigration(TerraformJob j)
    {
        if (j == null) return;
        if (j.type != TerraformProjectType.OrbitShiftOut && j.type != TerraformProjectType.OrbitShiftIn) return;
        var b = j.body;
        if (b == null) return;

        float desired = j.type == TerraformProjectType.OrbitShiftOut ? b.orbitRadius * 1.45f : b.orbitRadius * 0.68f;
        float target = b.orbitRadius;
        var sys = b.system != null ? b.system.bodies : null;
        if (sys != null) OrbitSafety.ClampRadius(sys, b, b.hostStar, desired, out target);

        j.orbitStart = b.orbitRadius;
        j.orbitTarget = target;
    }

    // Move a world to a radius RIGHT NOW without re-clamping or re-asserting the whole system — the
    // per-frame step of an animated migration, whose endpoints were already made safe. Keeps moons on the
    // planet's solar distance and refreshes the orbit ring + habitability (RescoreOrbit).
    static void SetOrbitRadiusLive(CelestialBody b, float r)
    {
        if (b == null) return;
        b.orbitRadius = r;
        b.distanceFromStar = r;
        if (b.moons != null)
            foreach (var m in b.moons) if (m != null) m.distanceFromStar = r;
        RescoreOrbit(b);
    }

    static void MigrateTo(CelestialBody b, float desiredRadius)
    {
        var sys = b.system;
        var siblings = sys != null ? sys.bodies : null;

        float final = desiredRadius;
        if (siblings != null &&
            !OrbitSafety.ClampRadius(siblings, b, b.hostStar, desiredRadius, out final))
        {
            NotificationManager.Instance?.Push($"{b.name} could not be moved",
                "Its neighbours leave no room to migrate into. The engineering still paid off — the " +
                "world's habitability ceiling rose — but its orbit stays where it is.", null, NotifKind.Info);
            RescoreOrbit(b);
            return;
        }

        b.orbitRadius = final;
        b.distanceFromStar = final;
        if (b.moons != null)
            foreach (var m in b.moons) if (m != null) m.distanceFromStar = final;

        RescoreOrbit(b);

        // Re-assert the whole system afterwards: moving one body can only ever have made it tighter.
        if (siblings != null) OrbitSafety.EnforceSystem(siblings, b.hostStar);
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
                metalPaid = j.metalPaid, energyPaid = j.energyPaid, waterPaid = j.waterPaid,
                orbitStart = j.orbitStart, orbitTarget = j.orbitTarget
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
                    metalPaid = d.metalPaid, energyPaid = d.energyPaid, waterPaid = d.waterPaid,
                    orbitStart = d.orbitStart, orbitTarget = d.orbitTarget
                });
            }
        OnChanged?.Invoke();
    }
}
