using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// DEV MODE: THE SAME ECONOMY, PLUS A HAND ON THE TAP
//
// Dev Mode does NOT change your resources. The stockpile in Dev Mode is the stockpile in the game,
// exactly — same numbers, same capacity, same costs. What Dev Mode adds is a set of controls for
// giving yourself as much of anything as you want, whenever you want it, and what you give yourself
// is YOURS: it stays when you drop back into normal play.
//
// WHY THIS AND NOT "INFINITE EVERYTHING"
// This used to hold every resource and the research pool at a million, topping them back up every
// three seconds. It made Dev Mode useless for testing anything economic — you could never see what a
// build actually cost, because the number was back at a million before you finished reading it — and
// it made the mode a one-way door: dropping in to look at something destroyed the balances of the
// game you were playing. A manual grant does the same job (get past a cost that is in your way)
// without either problem, and it is honest about it: the resources are real, and they persist.
//
// The cost checks are left in place deliberately: Dev Mode should exercise the same code the real
// game does, or it stops being a test of the real game.
// ============================================================================================
public class DevCheats : MonoBehaviour
{
    /// Grant sizes offered in the dev panel.
    public static readonly float[] GrantSteps = { 1_000f, 10_000f, 100_000f, 1_000_000f };

    public static void Create()
    {
        if (FindFirstObjectByType<DevCheats>() != null) return;
        var go = new GameObject("DevCheats");
        DontDestroyOnLoad(go);
        go.AddComponent<DevCheats>();
    }

    /// Dev Mode hotkeys that are not owned by a window. See DevBattle.
    void Update() => DevBattle.Hotkey();

    /// Hand the player some of one resource. Dev Mode only.
    ///
    /// Bypasses the storage ceiling on purpose — the point of the control is to get past a limit that
    /// is in the way, and a grant silently clipped to 3,000 would read as the button not working. What
    /// is granted is then ordinary stock: it can be spent, and it stays on the way back to normal play.
    public static void Grant(ResourceType t, float amount)
    {
        if (!GameMode.DevMode) return;
        PlayerEconomy.SetStock(t, PlayerEconomy.Get(t) + amount);
    }

    /// The same, for every resource at once — the common case.
    public static void GrantAll(float amount)
    {
        if (!GameMode.DevMode) return;
        foreach (ResourceType t in Enum.GetValues(typeof(ResourceType)))
            PlayerEconomy.SetStock(t, PlayerEconomy.Get(t) + amount);
    }

    public static void GrantResearchPoints(int amount)
    {
        if (!GameMode.DevMode) return;
        ResearchManager.AddPoints(amount);
    }

    /// Empty the stockpile back out — the undo for an over-generous grant, and the way to get back to
    /// testing a real economy without reloading.
    public static void ClearStock()
    {
        if (!GameMode.DevMode) return;
        foreach (ResourceType t in Enum.GetValues(typeof(ResourceType)))
            PlayerEconomy.SetStock(t, 0f);
    }

    /// The game underneath has been replaced — a new galaxy, or a save loaded over the top.
    ///
    /// The tech grant is STATIC and Dev Mode survives a mid-session load, so without this: grant the
    /// tree in one game, load another, switch the grant off, and the first game's tech list is written
    /// silently over the second one's. Dropped rather than reverted, because the save that just loaded
    /// brought its own tech set and that set is the truth now.
    public static void OnGameReplaced()
    {
        AllTechGranted = false;
        preGrantTech = null;
        preGrantQueue = null;
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

    /// The technologies the player has actually EARNED — the temporary grant excluded. This is what the
    /// save system writes, so saving with the grant up cannot bake the whole tech tree into the file.
    public static List<string> BaselineTech => AllTechGranted ? preGrantTech : TechManager.Export();

    static List<string> preGrantTech;
    static List<ResearchOrderDTO> preGrantQueue;
    static bool preGrantPaused;

    public static void SetAllTech(bool on)
    {
        if (on == AllTechGranted) return;
        // Only ever granted from inside Dev Mode — nothing outside it would ever turn the grant back off,
        // so it would become permanent. Turning it OFF is deliberately not guarded: GameMode.SetDev
        // switches it off after the flag has already gone false.
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

// ============================================================================================
// DEV BATTLE — Ctrl+Shift+E in Dev Mode: start a fight to watch
//
// Spawns a hostile squadron (the first faction at war with the player) a short way off the player's
// ships, close enough that every weapon class in it reaches: a Dreadnought (railgun, torpedoes, plasma,
// beam, point defence), a Cruiser, a Frigate (missiles, autocannon) and three fighters (pulse, beam,
// missiles). Combat is proximity-driven, so the fight starts by itself.
//
// The anchor is the first of the player's selected ships, else the player's ship nearest the camera.
// If there are no player WARSHIPS within reach of it (or no player ships at all), a matching player
// squadron is spawned too, so there is a two-sided fight to look at rather than an execution.
//
// The spawned hulls are ordinary units: they are saved, they can be selected and destroyed, and they
// stay when Dev Mode is switched off. Press again for another wave.
// ============================================================================================
public static class DevBattle
{
    public const KeyCode Key = KeyCode.E;

    static readonly UnitType[] Hostiles =
        { UnitType.Dreadnought, UnitType.Cruiser, UnitType.Frigate, UnitType.FighterIII, UnitType.FighterII, UnitType.Fighter };
    static readonly UnitType[] Escort =
        { UnitType.Cruiser, UnitType.Frigate, UnitType.Frigate, UnitType.FighterIII, UnitType.FighterII };

    /// Distance from the anchor to the hostile formation's centre: inside every weapon's range bar
    /// point defence, outside each other's hulls.
    const float Standoff = 14f;

    public static void Hotkey()
    {
        if (!GameMode.DevMode) return;
        if (!Input.GetKeyDown(Key)) return;
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (!ctrl || !shift || UIFactory.IsTypingInField()) return;
        Spawn();
    }

    public static void Spawn()
    {
        var um = UnitManager.Instance;
        if (um == null || SystemContext.Galaxy == null) return;

        Faction enemy = null;
        foreach (var f in FactionManager.All)
            if (f != null && f.relation == FactionRelation.Enemy) { enemy = f; break; }
        if (enemy == null)
        {
            Notify("Dev: no hostile faction", "Nobody is at war with you to send a fleet.", null, NotifKind.Info);
            return;
        }

        // ---- the anchor ----
        Unit anchor = null;
        foreach (var u in UnitSelection.Selected)
            if (u != null && !u.IsDestroyed && u.owner == FactionManager.Player) { anchor = u; break; }

        Vector3 look = CameraFocus();
        if (anchor == null)
        {
            float best = float.MaxValue;
            foreach (var u in um.Units)
            {
                if (u == null || u.IsDestroyed || u.owner != FactionManager.Player) continue;
                float d = (CombatManager.PosOf(u) - look).sqrMagnitude;
                if (d < best) { best = d; anchor = u; }
            }
        }

        Vector3 centre = anchor != null ? CombatManager.PosOf(anchor) : look;
        centre.y = 0f;

        // ---- does the player have anything there that can fight? ----
        bool armed = false;
        foreach (var u in um.Units)
        {
            if (u == null || u.IsDestroyed || u.owner != FactionManager.Player || u.Info == null) continue;
            if (u.Info.attack <= 1 || u.Info.isWorker || u.Info.canColonize) continue;
            Vector3 p = CombatManager.PosOf(u); p.y = 0f;
            if ((p - centre).sqrMagnitude <= 30f * 30f) { armed = true; break; }
        }

        float ang = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        Vector3 dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
        Place(um, enemy, Hostiles, centre + dir * Standoff, dir);
        if (!armed) Place(um, FactionManager.Player, Escort, centre - dir * 2f, -dir);

        um.NotifyUnitsChanged();
        Notify("Dev: hostile fleet inbound",
            $"{Hostiles.Length} {enemy.name} warships spawned {Standoff:F0} units off " +
            (anchor != null ? anchor.name : "the camera") + (armed ? "." : ", with a squadron of yours to meet them.") +
            " (Ctrl+Shift+E for another wave.)", null, NotifKind.Danger);
    }

    static void Notify(string title, string body, System.Action onClick, NotifKind kind)
    {
        var n = NotificationManager.Instance;
        if (n != null) n.Push(title, body, onClick, kind);
    }

    /// A loose wedge of parked hulls facing `facing`, centred on `at`.
    static void Place(UnitManager um, Faction owner, UnitType[] types, Vector3 at, Vector3 facing)
    {
        Vector3 side = Vector3.Cross(Vector3.up, facing).normalized;
        for (int i = 0; i < types.Length; i++)
        {
            int row = (i + 1) / 2;
            float s = (i % 2 == 0 ? 1f : -1f) * row;
            Vector3 p = at + side * (s * 1.6f) - facing * (row * 1.2f);
            var u = um.CreateUnit(types[i], owner, null);
            u.inSpace = true;
            u.parkPosition = p;
            u.status = UnitStatus.Idle;
        }
    }

    /// Where the camera is looking, on the plane of play.
    static Vector3 CameraFocus()
    {
        var cam = Camera.main;
        if (cam == null) return Vector3.zero;
        var ray = new Ray(cam.transform.position, cam.transform.forward);
        if (Mathf.Abs(ray.direction.y) > 1e-4f)
        {
            float t = -ray.origin.y / ray.direction.y;
            if (t > 0f) return ray.origin + ray.direction * t;
        }
        return cam.transform.position;
    }
}
