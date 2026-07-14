using System;
using System.Collections.Generic;
using UnityEngine;

// Global access point for the currently-loaded solar system. Runtime-built UI (orbit panel,
// save/load, research, habitable zone, star info) reads from here instead of relying on
// Inspector-wired references, so everything works without manual scene setup.
public static class SystemContext
{
    public static List<CelestialBody> Bodies = new List<CelestialBody>();
    public static StarData Star;
    public static Transform StarTransform;
    public static Transform SystemParent;
    public static SystemVisualizer Visualizer;
    public static HabitableZoneVisualizer Zone;

    // Raised whenever a new system is generated or loaded.
    public static event Action OnSystemChanged;

    public static void Set(List<CelestialBody> bodies, StarData star, Transform starT, Transform parent, SystemVisualizer vis)
    {
        Bodies = bodies ?? new List<CelestialBody>();
        Star = star;
        StarTransform = starT;
        SystemParent = parent;
        Visualizer = vis;
        OnSystemChanged?.Invoke();
    }

    // Flattened list including moons — handy for iterating everything.
    public static IEnumerable<CelestialBody> AllBodies()
    {
        foreach (var b in Bodies)
        {
            yield return b;
            foreach (var m in b.moons) yield return m;
        }
    }
}
