using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// DEV MODE: INFINITE EVERYTHING
//
// While Dev Mode is on, every resource and the research-point pool are topped back up to a million a
// few seconds after anything is spent. Nothing is free — you spend normally, and the stockpile refills.
//
// WHY REFILL INSTEAD OF SKIPPING THE COST
// The alternative is `if (GameMode.DevMode)` at every cost check, and the codebase already tried that:
// there are about twenty such guards across ColonyManager, UnitManager, SurfaceBuildManager and
// TerraformManager. They work — and research points have NONE of them, so Dev Mode could build a
// battleship for free and then be unable to afford a tech. That's the failure mode of the approach:
// every new cost is a new guard somebody has to remember, and the one they forget is invisible until
// someone hits it.
//
// Refilling the pool needs no guards at all. Every existing check asks "can I afford this?" and the
// answer is yes, including in code paths nobody has thought about — because there is genuinely a million
// of everything. One mechanism, no list to keep in sync.
//
// The cost checks are left in place deliberately: Dev Mode should exercise the same code the real game
// does, or it stops being a test of the real game.
// ============================================================================================
public class DevCheats : MonoBehaviour
{
    /// What every resource, and the research pool, is held at.
    public const float Stock = 1_000_000f;

    /// Roughly how long after spending before it refills.
    const float TopUpInterval = 3f;

    float timer;

    public static void Create()
    {
        if (FindFirstObjectByType<DevCheats>() != null) return;
        var go = new GameObject("DevCheats");
        DontDestroyOnLoad(go);
        go.AddComponent<DevCheats>();
    }

    void OnEnable() { GameMode.OnChanged += OnModeChanged; }
    void OnDisable() { GameMode.OnChanged -= OnModeChanged; }

    // Flipping Dev Mode on refills immediately. Waiting up to three seconds for the thing you just asked
    // for would read as it not having worked.
    void OnModeChanged() { if (GameMode.DevMode) TopUp(); }

    void Update()
    {
        if (!GameMode.DevMode) return;

        // Unscaled: Dev Mode is for poking at things, often while paused.
        timer -= Time.unscaledDeltaTime;
        if (timer > 0f) return;
        timer = TopUpInterval;
        TopUp();
    }

    /// Refill anything that's been spent. Idempotent and silent when nothing has: each Add fires
    /// OnChanged and rebuilds UI, so topping up a full stockpile every three seconds would be a
    /// pointless rebuild forever.
    public static void TopUp()
    {
        if (!GameMode.DevMode) return;

        foreach (ResourceType t in Enum.GetValues(typeof(ResourceType)))
        {
            float have = PlayerEconomy.Get(t);
            if (have < Stock - 0.5f) PlayerEconomy.Add(t, Stock - have);
        }

        // AddPoints rather than a new setter: the difference is the same thing, and one way to change a
        // number is better than two.
        int rp = ResearchManager.ResearchPoints;
        if (rp < (int)Stock) ResearchManager.AddPoints((int)Stock - rp);
    }

    // ========================================================================================
    // LEAVING DEV MODE PUTS EVERYTHING BACK
    //
    // Dev Mode is a place to go and look at something, not a one-way door. Before it turns on, the real
    // economy is copied aside; when it turns off, that copy is written back and the million of everything
    // is gone. So you can drop into Dev to check a thing on the far side of the galaxy and return to the
    // game you were actually playing, with the resources you had actually earned.
    //
    // Captured BEFORE GameMode.DevMode flips (see GameMode.SetDev) — the flag is what TopUp and
    // PlayerEconomy.Capacity read, and once either has run the pre-Dev numbers no longer exist to copy.
    // ========================================================================================
    static Dictionary<ResourceType, float> savedStock;
    static int savedPoints;
    static int savedEmpireLevel;
    static List<int> savedOreDiscovered, savedOreResearched;
    static bool haveBaseline;

    /// True while a pre-Dev baseline is being held. The save system asks, so that saving from inside Dev
    /// Mode does not bake a million of everything into the file permanently.
    public static bool HasBaseline => haveBaseline;

    public static float BaselineStock(ResourceType t) =>
        savedStock != null && savedStock.TryGetValue(t, out var v) ? v : 0f;
    public static int BaselinePoints => savedPoints;
    public static int BaselineEmpireLevel => savedEmpireLevel;
    public static List<int> BaselineOreDiscovered => savedOreDiscovered;
    public static List<int> BaselineOreResearched => savedOreResearched;
    public static List<string> BaselineTech => AllTechGranted ? preGrantTech : TechManager.Export();

    /// Copy the real economy aside. Called from GameMode.SetDev with the flag still false.
    public static void CaptureBaseline()
    {
        // Guarded: a second capture while already in Dev would overwrite the real numbers with the
        // topped-up ones, and there would be nothing left to go back to.
        if (haveBaseline) return;
        savedStock = PlayerEconomy.Snapshot();
        savedPoints = ResearchManager.ResearchPoints;

        // Empire level and the ore codex are captured too, because both are BOUGHT with research points
        // — and Dev Mode hands out a million of those. Refunding the points while leaving the levels they
        // bought would make Dev Mode a way to skip the research game rather than a way to look at it.
        savedEmpireLevel = EmpireTech.Level;
        savedOreDiscovered = ResearchManager.ExportDiscovered();
        savedOreResearched = ResearchManager.ExportResearched();

        haveBaseline = true;
    }

    /// Put the real economy back. Called from GameMode.SetDev with the flag already false, so nothing
    /// re-fills behind it.
    public static void RestoreBaseline()
    {
        if (!haveBaseline) return;

        // The granted tech tree first: it was a Dev Mode grant and it goes back with the rest of it. Left
        // alone, the player would walk out of Dev Mode still holding every technology in the game.
        SetAllTech(false);

        PlayerEconomy.RestoreSnapshot(savedStock);
        ResearchManager.Import(savedOreDiscovered, savedOreResearched, savedPoints);
        EmpireTech.SetLevel(savedEmpireLevel);

        ForgetBaseline();
    }

    /// Drop the baseline without applying it.
    public static void ForgetBaseline()
    {
        savedStock = null;
        savedOreDiscovered = null;
        savedOreResearched = null;
        haveBaseline = false;
    }

    /// The game underneath has been replaced — a new galaxy, or a save loaded over the top.
    ///
    /// Everything here is STATIC and Dev Mode survives a mid-session load, so without this: enter Dev in
    /// one game, load another, leave Dev, and the first game's economy, research and tech tree are
    /// written silently over the second one's. The old baseline describes a game that no longer exists.
    ///
    /// Re-baselining rather than just forgetting, because if Dev Mode is still on then TopUp is about to
    /// flood this game too, and the state that just loaded is the state the player must get back.
    public static void OnGameReplaced()
    {
        // Drop the grant WITHOUT reverting it: reverting would write the previous game's tech list over
        // the one that just loaded, which is the exact bug this method exists to prevent. The loaded
        // save's own tech set is already in place and is the truth now.
        AllTechGranted = false;
        preGrantTech = null;
        preGrantQueue = null;

        ForgetBaseline();
        if (GameMode.DevMode) CaptureBaseline();
    }

    // ========================================================================================
    // GRANT THE WHOLE TECH TREE (reversible)
    //
    // A real grant, not a display flag: the ids go into TechManager's researched set, so every
    // IsResearched check in the game — buildings, terraforming, ship upgrades — sees them, and
    // Recompute folds the whole set into TechEffects. Turning it off restores exactly the set, queue and
    // pause state from before, so nothing that was genuinely researched is lost and nothing granted is
    // kept.
    // ========================================================================================
    public static bool AllTechGranted { get; private set; }

    static List<string> preGrantTech;
    static List<ResearchOrderDTO> preGrantQueue;
    static bool preGrantPaused;

    public static void SetAllTech(bool on)
    {
        if (on == AllTechGranted) return;
        // Only ever granted from inside Dev Mode — nothing outside it would ever turn the grant back off,
        // so it would become permanent. Turning it OFF is deliberately not guarded: RestoreBaseline
        // reverts the grant after the flag has already gone false.
        if (on && !GameMode.DevMode) return;

        if (on)
        {
            // TechManager.Import wipes the research queue, and ImportQueue then drops any order whose
            // tech is now researched — which after a grant-all is all of them. So the queue has to be
            // copied out here to survive the round trip.
            preGrantTech = TechManager.Export();
            preGrantQueue = TechManager.ExportQueue();
            preGrantPaused = TechManager.Paused;

            var all = new List<string>();
            foreach (var t in TechDatabase.All) all.Add(t.id);
            // Import, not Research: Research re-checks empire level, schematics, ore discovery and cost,
            // so most of the tree would silently refuse. Import is also the silent path — the normal
            // unlock fires a notification and a sound each, and sixty of those is not a grant, it is an
            // avalanche.
            TechManager.Import(all);
        }
        else
        {
            TechManager.Import(preGrantTech);
            TechManager.ImportQueue(preGrantQueue, preGrantPaused);
            preGrantTech = null;
            preGrantQueue = null;
        }

        AllTechGranted = on;
    }
}
