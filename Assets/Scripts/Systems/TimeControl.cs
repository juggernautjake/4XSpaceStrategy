using UnityEngine;

// Central control of simulation speed. Keeps the scene TimeController (which writes Time.timeScale
// every frame) in sync, and remembers the last non-zero speed so Pause/Play can restore it.
public static class TimeControl
{
    public const float Max = 5f;
    static float last = 1f;

    public static event System.Action OnChanged;

    public static float Current => Time.timeScale;
    public static float LastNonZero => last;
    public static bool IsPaused => Time.timeScale <= 0.001f;

    public static void Set(float v)
    {
        v = Mathf.Clamp(v, 0f, Max);
        if (v > 0.001f) last = v;              // remember the speed for Resume
        Time.timeScale = v;
        TimeController.timeScale = v;           // the scene component re-applies this each frame
        OnChanged?.Invoke();
    }

    public static void Pause() => Set(0f);
    public static void Resume() => Set(last <= 0.001f ? 1f : last);
    public static void TogglePause() { if (IsPaused) Resume(); else Pause(); }
}
