using UnityEngine;

// ============================================================================================
// AN UNIDENTIFIED WORLD: A BLACK SPHERE WHERE A PLANET WILL BE
//
// Something is in that orbit and nobody has been close enough to say what. The body still orbits,
// still casts its shadow, still takes a click — it simply has no face yet.
//
// ---- IT USED TO LIGHTEN AS THE SURVEY RAN, AND THAT WAS THE WRONG DIAL ----------------------
// The silhouette was driven by explorationProgress: dark at 0, grey at 50%, the real textured world at
// 100%. That tied "can I see what kind of world this is" to "have I mapped its surface", and those are
// not the same question. A fleet parked in orbit around a banded gas giant, close enough to read its
// atmosphere from the bridge window, would still be shown an anonymous grey ball because nobody had
// finished charting its coastlines.
//
// So the trigger is PRESENCE now (see SystemPresence): the moment the player has anything in the
// system, every world in it is drawn as itself. What a survey buys is what a survey should buy — the
// surface map, the habitability, and then the indexes — none of which is the planet's face.
//
// And the silhouette no longer greys by degrees, because there are no degrees any more: either you are
// in the system and can see the thing, or you are not and cannot. A dial with two positions should
// look like a switch.
// ============================================================================================
public class BodyFog : MonoBehaviour
{
    CelestialBody body;
    Renderer rend;
    bool revealed;

    /// How often to re-ask whether this body is visible yet. Presence changes when a ship ARRIVES,
    /// which is an event this component does not see, so it polls — but at four times a second rather
    /// than every frame, because the answer walks the system's body list to find out.
    const float PollSeconds = 0.25f;
    float nextPoll;

    public void Init(CelestialBody b)
    {
        body = b;
        rend = GetComponent<Renderer>();
        ApplyFog();
    }

    void ApplyFog()
    {
        if (rend == null) return;
        var m = rend.material;
        m.mainTexture = null;
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
        if (m.IsKeywordEnabled("_EMISSION")) m.DisableKeyword("_EMISSION");

        // Not pure black. A body at 0,0,0 disappears into space entirely and the orbit reads as empty;
        // a hair above it keeps the sphere's silhouette against the background and its terminator against
        // the star, which is what says "there is something there" without saying anything else.
        Color c = new Color(0.055f, 0.06f, 0.075f);
        m.color = c;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);

        var atm = transform.Find("Atmosphere");
        if (atm != null) atm.gameObject.SetActive(false);
    }

    void Update()
    {
        if (revealed || body == null) return;

        nextPoll -= Time.unscaledDeltaTime;
        if (nextPoll > 0f) return;
        nextPoll = PollSeconds;

        if (!SystemPresence.Revealed(body)) return;

        revealed = true;
        PlanetAppearance.Apply(body, gameObject);   // full reveal (texture + atmosphere)
        Destroy(this);
    }
}
