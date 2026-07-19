using System.Collections.Generic;
using UnityEngine;

// Plate-tectonics events: earthquakes along the fault lines of tectonically-active worlds. A quake
// strikes on a fault and damages the infrastructure standing near it — the request's "Plate Tectonics
// will indicate areas around the fault lines that will damage infrastructure with events like
// Earthquakes ... Players may want to avoid building on or near fault lines for their danger factors".
// A world with no active plates (TectonicsMap.Active false) never quakes, and a quake whose epicentre
// finds nothing built near a fault does no damage — so building AWAY from the faults keeps you safe,
// which is the whole point of the danger/reward trade the request describes.
//
// Reads the SAME TectonicsMap geometry the terrain and the Survey overlay do, so quakes land exactly on
// the red fault lines the player can see. Infrastructure damage persists: PlacedBuilding.health round-
// trips through save/load. Terrain-altering aftermath (spawning a volcano, shifting elevation) is
// deliberately NOT done here — the surface is derived from the terrain seed and regenerates on load, so a
// mutated tile wouldn't survive a reload; that needs a persistent per-tile terrain-override layer that
// doesn't exist yet (recorded as follow-up in the dev-request planning doc).
public class EarthquakeManager : MonoBehaviour
{
    public static EarthquakeManager Instance;

    // Real-time cadence at which each eligible world is checked for a quake. Time.deltaTime is
    // timeScale-scaled, so quakes come faster under fast-forward, like every other timed system here.
    // These three are gameplay-tuning knobs picked by feel — there is no Editor in CI to calibrate them.
    const float CheckInterval = 18f;
    // Chance PER CHECK that an eligible world quakes, before scaling by how active its plates are.
    const float BaseQuakeChance = 0.14f;
    // Quake reach around the epicentre, in cells (clamped from a fraction of map width so a big map
    // doesn't shake end to end).
    const float QuakeRadiusFrac = 0.16f;
    const float MinQuakeRadius = 4f, MaxQuakeRadius = 22f;

    float timer;

    public static void Create()
    {
        if (Instance != null) return;
        new GameObject("EarthquakeManager").AddComponent<EarthquakeManager>();
    }

    void Awake() { Instance = this; }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer < CheckInterval) return;
        timer = 0f;

        if (SystemContext.Galaxy == null) return;
        foreach (var b in SystemContext.AllBodies())
        {
            if (b == null || !TectonicsMap.Active(b) || b.surface == null) continue;
            if (b.placedBuildings == null || b.placedBuildings.Count == 0) continue;

            // More active plates -> more frequent quakes. Scale the per-check chance by the world's
            // strongest plate motion (0..1).
            float activity = PlateActivity(b);
            if (activity > 0f && Random.value < BaseQuakeChance * activity)
                Strike(b);
        }
    }

    static float PlateActivity(CelestialBody b)
    {
        var layout = TectonicsMap.Get(b);
        if (layout?.plates == null || layout.plates.Length == 0) return 0f;
        float max = 0f;
        foreach (var p in layout.plates) max = Mathf.Max(max, p.strength);
        return Mathf.Clamp01(max);
    }

    void Strike(CelestialBody b)
    {
        int w = b.surface.width, h = b.surface.height;

        // The epicentre is on the fault under the most exposed structure: find the placed building sitting
        // closest to an active (ideally convergent) fault — that's where the stress releases. If nothing is
        // near a fault, this is a tremor out under empty crust with nothing to damage, so it fizzles.
        Vector2 epicentre = Vector2.zero;
        float hotProx = 0f;
        foreach (var pb in b.placedBuildings)
        {
            if (pb == null) continue;
            float cu = (pb.x + 0.5f) / w, cv = (pb.y + 0.5f) / h;
            var tec = TectonicsMap.Sample(b, cu, cv);
            float prox = tec.boundary * (0.5f + 0.5f * Mathf.Max(0f, tec.convergence));
            if (prox > hotProx) { hotProx = prox; epicentre = new Vector2(pb.x, pb.y); }
        }
        if (hotProx < 0.25f) return;   // nothing built near an active fault this time — no damage

        // Magnitude scales with how hard the fault there is driving; radius is a localized patch.
        float magnitude = Mathf.Lerp(0.15f, 0.6f, Mathf.Clamp01(hotProx));
        float radius = Mathf.Clamp(QuakeRadiusFrac * w, MinQuakeRadius, MaxQuakeRadius);

        int destroyed = 0, damaged = 0;
        // Copy the list first: destroying a building mutates b.placedBuildings.
        foreach (var pb in new List<PlacedBuilding>(b.placedBuildings))
        {
            if (pb == null) continue;
            float dx = pb.x - epicentre.x, dy = pb.y - epicentre.y;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            if (dist > radius) continue;

            float falloff = 1f - dist / radius;                       // 1 at the epicentre, 0 at the edge
            float dmg = magnitude * falloff * Random.Range(0.6f, 1.2f);
            if (dmg <= 0f) continue;

            pb.health -= dmg;
            if (pb.health <= 0f) { SurfaceBuildManager.Demolish(b, pb, refund: false); destroyed++; }
            else damaged++;
        }

        if (destroyed == 0 && damaged == 0) return;

        // Only bother the player about their own worlds.
        if (b.owner == FactionManager.Player)
        {
            SimpleAudio.Instance?.PlayNotify(NotifKind.Danger);
            string msg = destroyed > 0
                ? $"A quake along a fault line destroyed {destroyed} structure{(destroyed == 1 ? "" : "s")}" +
                  (damaged > 0 ? $" and damaged {damaged} more." : ".")
                : $"A quake along a fault line damaged {damaged} structure{(damaged == 1 ? "" : "s")}.";
            NotificationManager.Instance?.Push($"Earthquake on {b.name}", msg, Fly(b), NotifKind.Danger);
        }
    }

    System.Action Fly(CelestialBody b) => () =>
    {
        if (b != null && b.visualObject != null)
            CameraController.Instance?.FocusAndZoom(b.visualObject.transform, b.surfaceSize, true);
        PlanetUI.Instance?.Show(b);
    };
}
