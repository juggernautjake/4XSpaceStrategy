using UnityEngine;

// Global, empire-wide ship modifiers. Travel range is the product of three factors: the empire Tech
// Level milestone bonus (EmpireRange), the sum of drive technologies researched (TechRange), and the
// active relay network (RelayRange). So leveling the empire, researching better drives, AND building
// hyper-relays all extend how far your ships can go. Reset at the start of every new game.
public static class ShipUpgrades
{
    public static float EmpireRange = 1f;   // set by EmpireTech milestones (level-based)
    public static float TechRange = 1f;     // set by drive technologies (Ion/Warp/Jump…)
    // Quickened by the RELAY NETWORK ONLY (StationEffects). Drive technologies deliberately buy REACH
    // rather than pace — they raise TechRange — so the two investments do different things: research
    // lets a fleet get further, relays let it get there sooner. Nothing else writes this; if a drive
    // tech is ever meant to add speed it has to multiply in here rather than assign, because
    // StationEffects recomputes this value from the network every frame.
    public static float SpeedMult = 1f;

    // Set by StationEffects from the active relay/deep-space/mega stations. 1 = no relays deployed.
    public static float RelayRange = 1f;

    // What every ship's base range is multiplied by.
    public static float RangeMult => Mathf.Max(0.1f, EmpireRange) * Mathf.Max(0.1f, TechRange) * Mathf.Max(0.1f, RelayRange);

    public static void Reset()
    {
        EmpireRange = 1f;
        TechRange = 1f;
        SpeedMult = 1f;
        RelayRange = 1f;
    }
}
