using UnityEngine;

// Visual fog-of-war for an unexplored body: shows a dark, featureless silhouette that gradually
// lightens as ships survey it, then fully reveals the real textured world (and its atmosphere) once
// surveyed. Responsive in real time to exploration progress and Dev Mode.
public class BodyFog : MonoBehaviour
{
    CelestialBody body;
    Renderer rend;
    bool revealed;

    public void Init(CelestialBody b)
    {
        body = b;
        rend = GetComponent<Renderer>();
        ApplyFog(0f);
    }

    void ApplyFog(float progress)
    {
        if (rend == null) return;
        var m = rend.material;
        m.mainTexture = null;
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
        if (m.IsKeywordEnabled("_EMISSION")) m.DisableKeyword("_EMISSION");
        // Dark silhouette that lightens as it's surveyed.
        float b = Mathf.Lerp(0.10f, 0.42f, Mathf.Clamp01(progress / 0.5f));
        Color c = new Color(b, b, b + 0.03f);
        m.color = c;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);

        var atm = transform.Find("Atmosphere");
        if (atm != null) atm.gameObject.SetActive(false);
    }

    void Update()
    {
        if (revealed || body == null) return;

        if (body.Surveyed)
        {
            revealed = true;
            PlanetAppearance.Apply(body, gameObject);   // full reveal (texture + atmosphere)
            Destroy(this);
        }
        else
        {
            ApplyFog(body.explorationProgress);
        }
    }
}
