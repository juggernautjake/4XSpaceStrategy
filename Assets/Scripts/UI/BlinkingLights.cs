using UnityEngine;
using UnityEngine.UI;

// Pulses a row of small indicator images in a travelling wave for a bit of sci-fi life.
public class BlinkingLights : MonoBehaviour
{
    public Image[] lights;
    public float speed = 3.5f;

    void Update()
    {
        if (lights == null) return;
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] == null) continue;
            float t = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * speed - i * 0.7f);
            var c = lights[i].color;
            c.a = 0.15f + 0.85f * t;
            lights[i].color = c;
        }
    }
}
