using UnityEngine;

// The empire-wide Tech Level (1-10) in the HYBRID progression model: a single galaxy-spanning track
// that gates the big MILESTONES (probes, stations, hyper-relays, terraforming stations, onboard
// hyperdrives, mega-stations), while individual facilities (research centres, shipyards) keep their
// small local levels for rate/build-speed. Advancing a level costs research points and gets steeply
// more expensive, but each level opens far more of the game.
public static class EmpireTech
{
    // Most species top out at level 10; a highly intelligent species (IQ >= 8, e.g. Terrans or Cryithn)
    // can push to an exclusive level 11, reaching technologies others never can.
    public const int BaseMaxLevel = 10;
    public static int MaxLevel => (SpeciesManager.Current != null && SpeciesManager.Current.iq >= 8) ? 11 : BaseMaxLevel;
    public static bool CanReachEleven => SpeciesManager.Current != null && SpeciesManager.Current.iq >= 8;

    public static int Level { get; private set; } = 1;
    public static event System.Action OnChanged;

    // Research-point cost to go from `fromLevel` to the next. Rises ~1.75x per level, so the early
    // levels come quickly and the late ones are a serious investment.
    public static int LevelUpCost(int fromLevel)
        => Mathf.RoundToInt(160f * Mathf.Pow(1.75f, Mathf.Max(0, fromLevel - 1)));

    public static bool AtMax => Level >= MaxLevel;
    public static int NextCost => AtMax ? 0 : LevelUpCost(Level);
    public static bool CanAdvance => !AtMax && ResearchManager.ResearchPoints >= NextCost;

    // ---- Milestone gates (read by ships / stations / travel) ----
    public static bool ProbesUnlocked            => Level >= 2;   // launch probes to find other systems
    public static bool StationsUnlocked          => Level >= 2;   // rudimentary orbital stations
    public static bool HyperRelaysUnlocked       => Level >= 5;   // hyper-speed relays (a big milestone)
    public static bool TerraformStationsUnlocked => Level >= 6;   // stations that accelerate terraforming
    public static bool OnboardHyperdrives        => Level >= 7;   // ships carry their own long-range drive
    public static bool MegaStationsUnlocked      => Level >= 9;   // "little moon" do-everything stations

    // The travel-range multiplier this level grants. Tuned to the intended progression:
    //  L1  in-system only          L2  probes scout other systems (ships still local)
    //  L3  scouts/research reach the nearest system    L4-5  colony ships can cross
    //  L6  intergalactic travel takes off              L7+  onboard hyperdrives reach far systems.
    static readonly float[] RangeByLevel = { 1.0f, 1.0f, 1.05f, 1.30f, 1.50f, 1.75f, 2.40f, 3.20f, 3.90f, 4.70f, 5.60f, 7.0f };

    public static void Advance()
    {
        if (!CanAdvance) return;
        ResearchManager.AddPoints(-NextCost);
        Level++;
        ApplyMilestones();
        OnChanged?.Invoke();
        Announce();
    }

    public static void Reset()
    {
        Level = 1;
        ApplyMilestones();
        OnChanged?.Invoke();
    }

    public static void SetLevel(int lvl)   // load
    {
        Level = Mathf.Clamp(lvl, 1, MaxLevel);
        ApplyMilestones();
        OnChanged?.Invoke();
    }

    // Push this level's passive effects into the systems that read them. Called on every change and on
    // load. (When the branch tech tree lands, drive techs will ADD to this baseline.)
    static void ApplyMilestones()
    {
        int i = Mathf.Clamp(Level, 0, RangeByLevel.Length - 1);
        ShipUpgrades.RangeMult = RangeByLevel[i];
    }

    static void Announce()
    {
        string perk = MilestoneFor(Level);
        SimpleAudio.Instance?.PlayNotify(NotifKind.Victory);
        NotificationManager.Instance?.Push($"Empire Tech Level {Level} reached!",
            $"Your civilization advances. {perk}", null, NotifKind.Victory);
    }

    // A short description of what a given level opens up (for the notification and the UI).
    public static string MilestoneFor(int lvl)
    {
        switch (lvl)
        {
            case 2:  return "Probes and rudimentary orbital stations are now available; longer sensor reach.";
            case 3:  return "Scouts and research ships can now reach the nearest star systems.";
            case 4:  return "Improved stations and extended range — colony ships can begin crossing to nearby systems.";
            case 5:  return "HYPER-SPEED RELAYS unlocked — fast long-distance travel between linked points.";
            case 6:  return "Terraforming stations unlocked; intergalactic travel takes off.";
            case 7:  return "ONBOARD HYPERDRIVES — your ships carry their own long-range drives.";
            case 8:  return "Advanced stations and deeper technology paths open up.";
            case 9:  return "Mega-stations — self-sufficient 'little moons' — become buildable.";
            case 10: return "Peak technology: the full breadth of the tech tree is within reach.";
            case 11: return "Transcendent tier — your species' intellect unlocks technologies no ordinary civilization can attain.";
            default: return "New technologies and expansion options open up.";
        }
    }
}
