using UnityEngine;

// ============================================================================================
// DEEP RESEARCH — three tiers, each run once
//
// A basic survey maps a world from orbit and may be run ONCE; there is nothing to gain from mapping
// the same rock twice. Past that, a research ship can study a world on the ground, and it can do so
// three times over an empire's lifetime — each tier gated behind real progress, each answering a
// question the previous one could not.
//
// This class owns the ladder: what each tier is called, what it needs, and whether a given world can
// take the next step. It exists so those rules live in ONE place — the ship's order button, the survey
// tab's lock message and the overlay gate all have to agree, and three copies of "can this be studied"
// is three chances to disagree.
//
// WHAT EACH TIER REVEALS (the overlays are in SurfaceIndex.RequiredLevel; this is the rest):
//
//   I    atmosphere, biosphere, active tectonics, the terraform diagnosis
//   II   exact per-tile ore richness, what a point of interest actually holds, the terraform CEILING,
//        fault lines and quake risk
//   III  Vael fragments, anomalies, projected climate after each terraform project, subsurface deposits
// ============================================================================================
public static class DeepResearch
{
    /// Empire level required for each tier. Tier I is available from the start — a research ship is
    /// something you can build early and it should have something to do.
    public const int LevelForTierII = 4;
    public const int LevelForTierIII = 7;

    public static string Name(int tier) => tier switch
    {
        1 => "Deep Research I",
        2 => "Deep Research II",
        3 => "Deep Research III",
        _ => "a survey"
    };

    /// What an empire is currently CAPABLE of, regardless of any particular world.
    public static int MaxTierUnlocked
    {
        get
        {
            if (GameMode.DevMode) return CelestialBody.MaxResearchLevel;
            if (EmpireTech.Level >= LevelForTierIII) return 3;
            if (EmpireTech.Level >= LevelForTierII) return 2;
            return 1;
        }
    }

    public static int RequiredEmpireLevel(int tier)
        => tier >= 3 ? LevelForTierIII : tier >= 2 ? LevelForTierII : 1;

    /// Can this world take its next research step right now, and if not, why not?
    ///
    /// Answers in WORDS, because every refusal here is shown to the player on the button that refused.
    public static bool CanAdvance(CelestialBody b, out string reason)
    {
        reason = null;
        if (b == null) { reason = "no world selected"; return false; }

        if (!b.Surveyed)
        {
            reason = "survey this world first";
            return false;
        }

        int next = b.NextResearchLevel;
        if (next == 0)
        {
            reason = "fully studied — there is nothing left to learn here";
            return false;
        }

        if (next > MaxTierUnlocked)
        {
            reason = $"{Name(next)} needs Empire Tech Level {RequiredEmpireLevel(next)} " +
                     $"(yours: {EmpireTech.Level})";
            return false;
        }

        return true;
    }

    /// Take the next step. Returns the tier reached, or 0 if it could not.
    ///
    /// ONE TIER PER CALL, deliberately. The old deep survey could be re-run indefinitely — the ship
    /// panel literally offered "Deep Survey (again)" — which meant the action had no shape: it was
    /// either free to repeat for nothing, or a way to grind. A step you can take once is a decision.
    public static int Advance(CelestialBody b)
    {
        if (!CanAdvance(b, out _)) return 0;

        b.researchLevel = b.NextResearchLevel;

        // Tier III is where a Vael fragment surfaces. It used to appear on the first deep survey, which
        // made collecting all ten a side effect of an early ship order rather than the late-game hunt
        // the Codex is written as.
        if (b.researchLevel >= 3) AncientClues.Reveal(b);

        return b.researchLevel;
    }

    /// What the player gets for taking this step — shown on the button and in the report afterwards.
    public static string Describe(int tier) => tier switch
    {
        1 => "Heat and Fertile surveys, atmosphere, biosphere, tectonics, and what would have to be " +
             "fixed to make this world liveable.",
        2 => "Weather and Solar surveys, exact ore richness, what this world's sites actually hold, how " +
             "good it could ever become, and where its faults run.",
        3 => "The Water survey, anomalies, anything ancient buried here, and what this world would " +
             "look like after each project you could run on it.",
        _ => "A map of the surface and the mineral seams under it."
    };
}
