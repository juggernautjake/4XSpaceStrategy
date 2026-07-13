using System.Collections.Generic;
using UnityEngine;

public class CelestialBody
{
    public string name;
    public CelestialBodyType type;
    public ResourceDeposit resources;
    public int surfaceSize;
    public PlanetSurface surface;
    public List<CelestialBody> moons = new List<CelestialBody>();
    public float orbitRadius = 10f;
    public float orbitSpeed = 20f;

    [System.NonSerialized]
    public GameObject visualObject;

    // NEW: Store parent for moons (so editor can restore orbit reference)
    [System.NonSerialized]
    public CelestialBody parentBody; // For moons only

    public CelestialBody(CelestialBodyType type)
    {
        this.type = type;
        this.name = type.ToString();
        this.resources = new ResourceDeposit();
        this.orbitRadius = 10f;   // default
        this.orbitSpeed = 20f;
    }
}