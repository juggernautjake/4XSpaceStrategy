using System.Collections.Generic;
using UnityEngine;

// One star system within the galaxy: its star cluster, its bodies, where it sits in the galaxy, and
// who (if anyone) owns it. Systems are STATIC in the galaxy — they don't orbit the centre — but the
// bodies inside each system orbit their own star.
public class StarSystemData
{
    public string name;
    public Vector3 galaxyPosition;

    public List<StarData> stars = new List<StarData>();  // 1-3 suns, or a single black hole
    public bool isBlackHole;
    public StarData combinedStar;                        // combined data for HZ / light / orbits

    public List<CelestialBody> bodies = new List<CelestialBody>();

    public Faction owner;      // null == unclaimed
    public bool isHome;

    [System.NonSerialized] public Transform pivot;       // runtime render root at galaxyPosition

    public IEnumerable<CelestialBody> AllBodies()
    {
        foreach (var b in bodies)
        {
            yield return b;
            foreach (var m in b.moons) yield return m;
        }
    }
}

// The whole galaxy.
public class Galaxy
{
    // The galaxy's own name — the one label the player reads at the widest zoom, where the spiral is the
    // only thing on screen. Systems and stars are named by the generators; this is the level above them.
    public string name = "Unnamed Galaxy";

    // Drives every procedural choice in the deep-view spiral: handedness, arm count, tightness, arm
    // length, wispiness, density, distortion and palette (see GalaxyShape.FromSeed). STORED rather than
    // derived from the name, so a saved galaxy reloads with the exact spiral it was generated with — a
    // hash of the name would work until someone renamed it, and then the sky would silently change.
    public int visualSeed;

    // The home system's star CLUSTER (1-3 suns), DECIDED UP FRONT.
    //
    // ForceHomeWorld used to roll this itself, and ForceHomeWorld runs at the very end of generation —
    // so for the whole time the loading screen is running, "which star(s) does the player's home have"
    // had no answer yet. Rolling it in Begin and having ForceHomeWorld consume it means what the loading
    // screen shows and what the player lands in are the same suns by construction rather than by two
    // rolls happening to agree.
    // The INSTANCEs, not just the classes.
    //
    // StarDatabase.Get is not a lookup — it re-rolls temperature, per-channel colour and visual scale on
    // every call. So storing only the StarType(s) and calling Get again would give the loading screen and
    // the actual home system different-looking stars that merely share a spectral class, which is
    // exactly the coincidence this field exists to eliminate. Roll once, hand the same objects to both.
    public List<StarData> homeStars = new List<StarData>();

    // Convenience: the primary (first) home sun, for callers that only care about "the" home star (the
    // loading screen's single-sphere preview, before the pop-out cluster is shown). Never settable —
    // homeStars is the one source of truth, so there's nowhere for the two to disagree.
    public StarData homeStar => homeStars != null && homeStars.Count > 0 ? homeStars[0] : null;

    public List<StarSystemData> systems = new List<StarSystemData>();
    public int homeIndex;
    public StarData center;                 // central supermassive object (visual)
    public Vector3 centerPosition = Vector3.zero;

    // Ancient derelict stations hidden about the galaxy at odd orbits (see Derelict / DerelictGen). Some
    // hold a Vael fragment; others hold salvageable materials or lost technology.
    public List<Derelict> derelicts = new List<Derelict>();

    public StarSystemData Home =>
        (homeIndex >= 0 && homeIndex < systems.Count) ? systems[homeIndex] : (systems.Count > 0 ? systems[0] : null);
}
