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
