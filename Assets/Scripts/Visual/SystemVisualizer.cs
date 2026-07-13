using UnityEngine;
using System.Collections.Generic;

public class SystemVisualizer : MonoBehaviour
{
    public SolarSystemGenerator solarSystemGenerator; // Assign in Inspector on the GameManager object

    [Header("Prefabs")]
    public GameObject planetPrefab;
    public GameObject starPrefab;

    [Header("References")]
    public Transform systemParent;

    public void VisualizeSystem(List<CelestialBody> bodies, StarType starType)
    {
        if (planetPrefab == null || systemParent == null)
        {
            Debug.LogError("Missing references in SystemVisualizer!");
            return;
        }

        // Clear old visuals (important when regenerating from sandbox)
        foreach (Transform child in systemParent)
            Destroy(child.gameObject);

        // === Spawn Central Star ===
        GameObject starObj = starPrefab != null
            ? Instantiate(starPrefab, systemParent)
            : GameObject.CreatePrimitive(PrimitiveType.Sphere);

        starObj.name = "Star";
        starObj.transform.localScale = Vector3.one * 3.5f;

        float currentRadius = 8f; // Starting distance from center

        // === Spawn Planets ===
        for (int i = 0; i < bodies.Count; i++)
        {
            var bodyData = bodies[i];
            GameObject visual = Instantiate(planetPrefab, systemParent);
            visual.name = bodyData.type.ToString();

            // Link data to visual
            bodyData.visualObject = visual;

            // Visual scale based on surface size
            float scale = Mathf.Max(0.6f, bodyData.surfaceSize * 0.08f);
            visual.transform.localScale = Vector3.one * scale;

            // Clicking support
            PlanetClick clickHandler = visual.GetComponent<PlanetClick>();
            if (clickHandler != null) clickHandler.data = bodyData;

            // === ORBIT SETUP ===
            OrbitController orbitController = visual.AddComponent<OrbitController>();

            // Get speed from generator (Kepler-like)
            float orbitSpeed = solarSystemGenerator.GetOrbitSpeed(bodyData.type, currentRadius);

            // Setup the orbit (this creates the ring and starts movement)
            orbitController.Setup(starObj.transform, currentRadius, orbitSpeed);

            Debug.Log($"Added OrbitController to {visual.name} | Radius: {currentRadius:F1} | Speed: {orbitSpeed:F1}");

            // Color the planet
            Renderer rend = visual.GetComponent<Renderer>();
            if (rend != null)
                rend.material.color = GetColorForType(bodyData.type);

            // Move outward for next planet
            currentRadius += 5f + (bodyData.surfaceSize * 0.25f);
        }

        // === Spawn Moons ===
        for (int i = 0; i < bodies.Count; i++)
        {
            var planetData = bodies[i];
            var planetVisual = planetData.visualObject;
            if (planetVisual == null || planetData.moons.Count == 0) continue;

            foreach (var moonData in planetData.moons)
            {
                moonData.parentBody = planetData;
                GameObject moonVisual = Instantiate(planetPrefab, systemParent);
                moonVisual.name = "Moon of " + planetVisual.name;

                // Smaller visual scale for moons
                moonVisual.transform.localScale = Vector3.one * (moonData.surfaceSize * 0.05f);
                moonData.visualObject = moonVisual;

                // === MOON ORBIT SETUP ===
                OrbitController moonOrbit = moonVisual.AddComponent<OrbitController>();
                float moonRadius = Random.Range(2.5f, 5f);
                float moonSpeed = Random.Range(40f, 80f);

                // Setup with correct values
                moonOrbit.Setup(planetVisual.transform, moonRadius, moonSpeed);

                // Ensure data is stored
                moonData.orbitRadius = moonRadius;  // Add this line if not present
                moonData.orbitSpeed = moonSpeed;     // Add this line if not present

                // Clicking
                PlanetClick moonClick = moonVisual.GetComponent<PlanetClick>();
                if (moonClick != null) moonClick.data = moonData;

                Debug.Log($"Spawned moon for {planetVisual.name} | Radius: {moonRadius:F1} | Speed: {moonSpeed:F1}");
            }
        }
    }

    private Color GetColorForType(CelestialBodyType type)
    {
        switch (type)
        {
            case CelestialBodyType.GasGiant: return new Color(0.9f, 0.7f, 0.4f);
            case CelestialBodyType.IcePlanet: return Color.cyan;
            case CelestialBodyType.VolcanicPlanet: return new Color(0.8f, 0.2f, 0.1f);
            case CelestialBodyType.OceanPlanet: return new Color(0.2f, 0.5f, 0.9f);
            case CelestialBodyType.BarrenPlanet: return Color.gray;
            default: return Color.green;
        }
    }
}