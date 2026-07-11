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

        // Clear old visuals
        foreach (Transform child in systemParent)
            Destroy(child.gameObject);

        // Spawn Central Star
        GameObject starObj = starPrefab != null
            ? Instantiate(starPrefab, systemParent)
            : GameObject.CreatePrimitive(PrimitiveType.Sphere);

        starObj.name = "Star";
        starObj.transform.localScale = Vector3.one * 3.5f;

        float currentRadius = 8f;   // Start closer to the star

        for (int i = 0; i < bodies.Count; i++)
        {
            var bodyData = bodies[i];

            GameObject visual = Instantiate(planetPrefab, systemParent);
            visual.name = bodyData.type.ToString();

            // IMPORTANT: Assign visual reference FIRST
            bodyData.visualObject = visual;

            float scale = Mathf.Max(0.6f, bodyData.surfaceSize * 0.08f);
            visual.transform.localScale = Vector3.one * scale;

            // Data for clicking
            PlanetClick clickHandler = visual.GetComponent<PlanetClick>();
            if (clickHandler != null)
                clickHandler.data = bodyData;
            else
                Debug.LogWarning($"Planet prefab missing PlanetClick component on {visual.name}");

            // === ORBIT CONTROLLER SETUP ===
            OrbitController orbitController = visual.AddComponent<OrbitController>();
            orbitController.Setup(starObj.transform, currentRadius, 25f / Mathf.Max(1f, currentRadius * 0.35f));
            Debug.Log($"Added OrbitController to {visual.name} with radius {currentRadius}");

            // Assign parent BEFORE any Start() logic runs
            orbitController.parentBody = starObj.transform;

            orbitController.orbitRadius = currentRadius;
            orbitController.orbitSpeed = solarSystemGenerator.GetOrbitSpeed(bodyData.type, currentRadius);

            Debug.Log($"Assigned parentBody = Star for {visual.name}");

            // Color
            Renderer rend = visual.GetComponent<Renderer>();
            if (rend != null)
                rend.material.color = GetColorForType(bodyData.type);

            currentRadius += 5f + (bodyData.surfaceSize * 0.25f);
        }

        // Spawn Moons for each planet
        for (int i = 0; i < bodies.Count; i++)
        {
            var planetData = bodies[i];
            var planetVisual = planetData.visualObject;

            if (planetVisual == null || planetData.moons.Count == 0) continue;

            foreach (var moonData in planetData.moons)
            {
                GameObject moonVisual = Instantiate(planetPrefab, systemParent); // Reuse prefab for now
                moonVisual.name = "Moon of " + planetVisual.name;
                moonVisual.transform.localScale = Vector3.one * (moonData.surfaceSize * 0.05f);

                moonData.visualObject = moonVisual;

                // Moon Orbit Controller (parent = planet)
                OrbitController moonOrbit = moonVisual.AddComponent<OrbitController>();
                moonOrbit.Setup(planetVisual.transform, Random.Range(2.5f, 5f), Random.Range(40f, 80f)); // Faster, closer

                // Click handler
                PlanetClick moonClick = moonVisual.GetComponent<PlanetClick>();
                if (moonClick != null) moonClick.data = moonData;

                Debug.Log($"Spawned moon for {planetVisual.name}");
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