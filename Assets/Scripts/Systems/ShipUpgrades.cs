using UnityEngine;

// Global, empire-wide ship modifiers. Travel range is the product of two factors: the empire Tech
// Level milestone bonus (EmpireRange) and the sum of drive technologies researched (TechRange). So
// leveling the empire AND researching better drives both extend how far your ships can go. Reset at
// the start of every new game.
public static class ShipUpgrades
{
    public static float EmpireRange = 1f;   // set by EmpireTech milestones (level-based)
    public static float TechRange = 1f;     // set by drive technologies (Ion/Warp/Jump…)
    public static float SpeedMult = 1f;     // reserved for drive tech

    // What every ship's base range is multiplied by.
    public static float RangeMult => Mathf.Max(0.1f, EmpireRange) * Mathf.Max(0.1f, TechRange);

    public static void Reset()
    {
        EmpireRange = 1f;
        TechRange = 1f;
        SpeedMult = 1f;
    }
}
