using System.Collections.Generic;
using UnityEngine;

public class CelestialBody
{
    public int id;                 // stable id for save/load and parent references
    public string name;
    public CelestialBodyType type;
    public ResourceDeposit resources;
    public int surfaceSize;
    public PlanetSurface surface;  // low-res grid (the "general" viewer + gameplay)
    public List<CelestialBody> moons = new List<CelestialBody>();

    // Terrain field identity. Both the low-res grid and the high-res detailed map are sampled from
    // this same seed/frequency, so continents & oceans line up between the two views.
    public float terrainSeed = 0f;
    public float continentFrequency = 4f;

    public List<PointOfInterest> pointsOfInterest = new List<PointOfInterest>();

    // --- Orbit parameters (authoritative data; the OrbitController reads these) ---
    public float orbitRadius = 10f;     // for planets: distance from star; for moons: distance from planet
    public float orbitSpeed = 20f;
    public float orbitPhase = 0f;       // starting angle in degrees
    public int orbitDirection = 1;      // +1 counter-clockwise, -1 clockwise
    public float inclination = 0f;      // orbital tilt in degrees
    public float eccentricity = 0f;     // 0 = circle, up to ~0.6 = ellipse
    public float verticalOffset = 0f;   // lifts the orbit plane up/down
    public float spinSpeed = 0f;        // axial rotation, degrees per second
    public bool showRing = true;

    // --- Habitability (relative to the current star) ---
    public float distanceFromStar = 0f; // absolute distance from the star, in orbit units
    public float habitability = 0f;     // 0..100
    public bool isHabitable = false;    // true if physically inside the Goldilocks zone

    [System.NonSerialized]
    public GameObject visualObject;

    [System.NonSerialized]
    public CelestialBody parentBody;    // For moons only

    public CelestialBody(CelestialBodyType type)
    {
        this.type = type;
        this.name = type.ToString();
        this.resources = new ResourceDeposit();
        this.orbitRadius = 10f;
        this.orbitSpeed = 20f;
    }
}
