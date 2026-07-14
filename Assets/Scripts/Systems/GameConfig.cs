using UnityEngine;

public enum Difficulty { Easy, Medium, Hard }

// Global game setup chosen in the new-game wizard: difficulty and faction identity, plus the
// difficulty-driven modifiers (resources, research speed, home-world habitability).
public static class GameConfig
{
    public static Difficulty CurrentDifficulty = Difficulty.Medium;
    public static string FactionName = "Your Empire";

    // Bulk resource multiplier (easy = more).
    public static float ResourceMult =>
        CurrentDifficulty == Difficulty.Easy ? 1.6f : CurrentDifficulty == Difficulty.Hard ? 0.7f : 1f;

    // Research time multiplier (easy = faster).
    public static float ResearchTimeMult =>
        CurrentDifficulty == Difficulty.Easy ? 0.55f : CurrentDifficulty == Difficulty.Hard ? 1.5f : 1f;

    // Starting research points (easy = more).
    public static int StartingResearchPoints =>
        CurrentDifficulty == Difficulty.Easy ? 220 : CurrentDifficulty == Difficulty.Hard ? 70 : 120;

    // The forced habitability of the starting home world.
    //  Easy = always 100, Medium = 90-99, Hard = 80-89.
    public static float HomeHabitability()
    {
        switch (CurrentDifficulty)
        {
            case Difficulty.Easy: return 100f;
            case Difficulty.Hard: return Random.Range(80f, 89f);
            default: return Random.Range(90f, 99f);
        }
    }

    // Extra starting resources on the home world (easy gives a big head start).
    public static float HomeResourceBonus =>
        CurrentDifficulty == Difficulty.Easy ? 3f : CurrentDifficulty == Difficulty.Hard ? 1f : 1.5f;
}
