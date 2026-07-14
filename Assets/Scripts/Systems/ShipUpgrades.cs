// Global, empire-wide ship modifiers raised by drive/range technologies and by relay networks.
// Base ship stats live in UnitDatabase; these multipliers scale them at runtime so a fresh empire is
// range-limited to (roughly) its home system and expands its reach as it researches better drives or
// builds relays. Reset at the start of every new game.
public static class ShipUpgrades
{
    public static float RangeMult = 1f;   // multiplies every ship's base travel range
    public static float SpeedMult = 1f;   // multiplies every ship's speed (reserved for drive tech)

    public static void Reset()
    {
        RangeMult = 1f;
        SpeedMult = 1f;
    }
}
