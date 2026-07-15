using System;
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
}
