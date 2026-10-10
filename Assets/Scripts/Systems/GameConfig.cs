using UnityEngine;

public enum Difficulty { Easy, Medium, Hard }

// Global game setup chosen in the new-game wizard: difficulty and faction identity, plus the
// difficulty-driven modifiers (resources, research speed, home-world habitability).
public static class GameConfig
{
    public static Difficulty CurrentDifficulty = Difficulty.Medium;
    public static string FactionName = "Your Empire";

    // Do colonies grow settlements of their own as their population rises (see CityGrowth)?
    //
    // A taste thing, so it's a switch rather than a decision baked into the world: with it on, a
    // habitable planet fills in with towns and cities on its own and they compete for the ground you
    // wanted to mine. With it off, a world only ever has exactly what you placed on it. Nothing else
    // depends on it — turning it off mid-game simply stops new settlements appearing; the ones already
    // grown stay, and can be demolished like anything else.
    public static bool OrganicCityGrowth = true;
    public static event System.Action OnOrganicCityGrowthChanged;

    public static void SetOrganicCityGrowth(bool on)
    {
        if (OrganicCityGrowth == on) return;
        OrganicCityGrowth = on;
        OnOrganicCityGrowthChanged?.Invoke();
    }

    // Bulk resource multiplier (easy = more).
    public static float ResourceMult =>
        CurrentDifficulty == Difficulty.Easy ? 1.6f : CurrentDifficulty == Difficulty.Hard ? 0.7f : 1f;

    // Research time multiplier (easy = faster).
    public static float ResearchTimeMult =>
        CurrentDifficulty == Difficulty.Easy ? 0.55f : CurrentDifficulty == Difficulty.Hard ? 1.5f : 1f;

    // Starting research points (easy = more).
    public static int StartingResearchPoints =>
        CurrentDifficulty == Difficulty.Easy ? 220 : CurrentDifficulty == Difficulty.Hard ? 70 : 120;

    // The guaranteed habitability floor of the cradle — the world built for the player's species. Its
    // real rating stands if it is higher (2026-10-09):
    //  Easy = 95-100, Medium = 80-90, Hard = 70-80.
    public static float HomeHabitability()
    {
        switch (CurrentDifficulty)
        {
            case Difficulty.Easy: return Random.Range(95f, 100f);
            case Difficulty.Hard: return Random.Range(70f, 80f);
            default: return Random.Range(80f, 90f);
        }
    }

    // A SECOND starting option in the home system's habitable zone — another planet, or a habitable moon
    // — so the opening is a real choice. Easy usually gets one at 90%+, Medium half the time at 70%+,
    // Hard never: one world, take it.
    public static float SecondOptionChance =>
        CurrentDifficulty == Difficulty.Easy ? 0.85f : CurrentDifficulty == Difficulty.Hard ? 0f : 0.5f;

    public static float SecondOptionMin =>
        CurrentDifficulty == Difficulty.Easy ? 90f : 70f;

    // Extra starting resources on the home world (easy gives a big head start).
    public static float HomeResourceBonus =>
        CurrentDifficulty == Difficulty.Easy ? 3f : CurrentDifficulty == Difficulty.Hard ? 1f : 1.5f;
}
