using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// THE OPENING — choose a world, name it, found it (2026-10-09)
//
// A new game used to hand the player a finished capital: an owned, settled world with a capitol, a
// shipyard, a laboratory and a power station already standing. Now it opens on the home system with
// nothing claimed, and the first minutes are the player's own decisions, in order:
//
//   ChooseWorld   — the home system is framed with the species' habitable zone on, and only the
//                   starting OPTIONS are ringed: the CRADLE (at the difficulty's floor — Easy 95+,
//                   Medium 80+, Hard 70+) plus, on Easy/Medium, sometimes a second world (Options).
//                   The game is paused. "Are you sure you want <name>?", then the naming window.
//   PlaceCapitol  — the world is claimed and fully surveyed (level 2, every index), and its Surface Map
//                   opens with the Planetary Capitol in hand. The capitol is never in the build menu; it
//                   is placed here, free, and placing it is what settles the world.
//   Farm → Combustion → Mine → Housing ×2
//                 — one at a time. A flashing yellow "!" sits on the Build category tab holding the next
//                   structure and on that structure's card. A step counts as soon as the job is on the
//                   build queue, so the player is not left waiting on construction to be told what next.
//
// RUNTIME-ONLY, like ColonyLanding. Saving is refused until the capitol is down (SaveLoadMenu asks
// BlocksSaving): a save of an unclaimed home system is a state the loader has no answer for, since it
// treats the first player world in the home system as the capital. After the capitol the remaining
// steps are only advice, and losing them to a save/load costs nothing.
// ============================================================================================
public enum OnboardingStep { None, ChooseWorld, PlaceCapitol, Farm, Combustion, Mine, Housing, Done }

public static class HomeworldOnboarding
{
    /// How many housing blocks the opening asks for ("at least 2 city buildings").
    public const int HousingWanted = 2;

    /// The world the generator built for the species. Set by GalaxyGenerator.ForceHomeWorld.
    public static CelestialBody Cradle;

    /// Every world the player may start on: the cradle, plus the second option Easy and Medium can roll
    /// (GalaxyGenerator.AddSecondOption). Only these can be chosen, and only these get a green ring
    /// while choosing — a moon of a habitable world is not ringed unless it is one of them.
    public static readonly HashSet<CelestialBody> Options = new HashSet<CelestialBody>();

    public static bool IsOption(CelestialBody b) => b != null && Options.Contains(b);

    /// Drop the last galaxy's cradle and options.
    public static void ForgetWorlds() { Cradle = null; Options.Clear(); }

    public static OnboardingStep Step { get; private set; } = OnboardingStep.None;

    /// The world being founded, once chosen.
    public static CelestialBody World { get; private set; }

    public static System.Action OnChanged;

    public static bool Choosing => Step == OnboardingStep.ChooseWorld;
    public static bool BlocksSaving => Step == OnboardingStep.ChooseWorld || Step == OnboardingStep.PlaceCapitol;
    public static bool AwaitingCapitol(CelestialBody b) => b != null && Step == OnboardingStep.PlaceCapitol && World == b;

    // ---- Phase 1: choosing ----------------------------------------------------------------------

    /// Called once a new galaxy is generated and the intro has handed the camera back.
    public static void BeginNewGame(StarSystemData home)
    {
        World = null;
        Step = OnboardingStep.ChooseWorld;

        // Every world in the home system is surveyed to level 1 — the player is choosing between them,
        // and a choice made blind is not a choice. Ratings for the current species are refreshed too.
        if (home != null)
            foreach (var b in home.AllBodies())
            {
                if (b == null) continue;
                b.visited = true;
                b.surveyRows = null;   // re-seeded from the progress below rather than left at 0
                b.explorationProgress = Mathf.Max(b.explorationProgress, 1f);
                if (!b.habitabilityLocked && b.hostStar != null && SpeciesManager.Current != null)
                    b.habitability = Habitability.Rate(b.hostStar, SpeciesManager.Current, b);
            }

        // The camera and the zone wait for the intro to let go of the camera — see FrameIfPending.
        needsFraming = true;
        OnChanged?.Invoke();
    }

    static bool needsFraming;

    /// Frame the home system and switch the species band on, once the genesis sequence has handed the
    /// camera back. Polled by the onboarding UI; does nothing after the first time.
    public static void FrameIfPending()
    {
        if (!needsFraming || GenesisSequence.Running || GenesisCamera.Active) return;
        needsFraming = false;
        if (Step != OnboardingStep.ChooseWorld) return;
        var zone = SystemContext.Zone;
        if (zone != null) { zone.SetSpeciesMode(true); zone.SetVisible(true); }
        CameraController.Instance?.ViewSystem();
        // Paused while choosing: rivals should not expand, and the starting colony ship should not be
        // flown off to settle a world, before the player has a world at all. Claim resumes.
        TimeControl.Pause();
    }

    /// Can this body be a starting world? Any planet or moon of the home system with a surface.
    public static bool Eligible(CelestialBody b, out string why)
    {
        why = null;
        var home = SystemContext.Galaxy != null ? SystemContext.Galaxy.Home : null;
        if (b == null) { why = "nothing there"; return false; }
        if (home == null || b.system != home) { why = "choose a world in your home system"; return false; }
        if (b.type == CelestialBodyType.GasGiant) { why = "a gas giant has no surface to build on"; return false; }
        if (b.type == CelestialBodyType.Asteroid) { why = "an asteroid is too small to found a civilisation on"; return false; }
        // Difficulty decides how many worlds are on offer (GameConfig.SecondOptionChance); the rest of
        // the system is for later.
        if (Options.Count > 0 && !Options.Contains(b))
        { why = "your people can't live there — choose a world with a green ring"; return false; }
        // There has to be dry ground the capitol fits on, or the opening could never get past it.
        if (!SurfaceBuildManager.FindSpot(b, SurfaceBuildingType.PlanetCapitol, out _, out _))
        { why = "there is no dry ground big enough for a capitol"; return false; }
        return true;
    }

    /// The player confirmed the world and its name. Claims it, surveys it fully, and opens its surface
    /// map with the capitol in hand.
    public static void Claim(CelestialBody b, string name)
    {
        if (!Eligible(b, out _)) return;
        string oldName = b.name;
        if (!string.IsNullOrWhiteSpace(name)) b.name = name.Trim();
        // Moons named off the world ("Old-a") follow the new name.
        if (b.moons != null && oldName != b.name)
            foreach (var m in b.moons)
                if (m != null && m.name != null && m.name.StartsWith(oldName + "-"))
                    m.name = b.name + m.name.Substring(oldName.Length);

        b.owner = FactionManager.Player;
        b.birthrightClaim = true;
        b.claimingFaction = null;
        // LEVEL 2: "give the player access to level 2 survey status on this world, so they can see all
        // the Index information" — before they choose where the capitol goes.
        b.visited = true;
        b.surveyRows = null;          // re-seeded from the progress below, so nothing reads stale rows
        b.explorationProgress = 1f;
        b.deepProgress = 1f;
        b.researchLevel = CelestialBody.MaxResearchLevel;

        // THE HOME MOONS ARE THE CHOSEN WORLD'S. `cradleMoon` (claimable at tech 1, guaranteed
        // terraformable) was stamped on the cradle's moons at generation; it belongs to whatever the
        // player actually founds on. A moon capital's neighbourhood is its planet's other moons.
        var hood = b.parentBody != null ? b.parentBody : b;
        if (b.system != null)
            foreach (var o in b.system.AllBodies())
            {
                if (o == null) continue;
                o.cradleMoon = o.parentBody == hood && o != b;
                // ...and the guarantee that rides with the flag, re-applied for the new home moons the
                // way GalaxyGenerator.Recompute applied it at generation.
                if (o.cradleMoon)
                    o.terraformability = Mathf.Max(o.terraformability, UnitManager.ColonizeMinHabitability + 20f);
            }

        World = b;
        Step = OnboardingStep.PlaceCapitol;

        // The species band was for choosing; from here the band is the star's again.
        var zone = SystemContext.Zone;
        if (zone != null) zone.SetSpeciesMode(false);

        // The starting stock and the starting fleet belong to the chosen world.
        PlayerEconomy.NewGame(b, SpeciesManager.Current);
        UnitManager.Instance?.SetHomePlanet(b, true);
        UnitManager.Instance?.RefreshOwnerRingOf(b);

        TimeControl.Set(1f);
        OnChanged?.Invoke();
        PlanetViewWindow.Instance?.ShowFor(b, PlanetViewWindow.Tab.Build);
    }

    // ---- Phase 2: the capitol -------------------------------------------------------------------

    /// The capitol is standing. The world becomes the capital.
    public static void CapitolPlaced(CelestialBody b)
    {
        if (!AwaitingCapitol(b)) return;
        b.settled = true;
        b.cities = 1;
        b.population = Population.HomeStart(SpeciesManager.Current);
        if (!b.buildings.Contains((int)BuildingType.City)) b.buildings.Add((int)BuildingType.City);
        b.claimProgress = Colony.ClaimProgress(b);
        Step = OnboardingStep.Farm;

        SimpleAudio.Instance?.PlayNotify(NotifKind.Victory);
        NotificationManager.Instance?.Push($"{b.name} is your capital",
            Instruction(), () => PlanetViewWindow.Instance?.ShowFor(b, PlanetViewWindow.Tab.Build), NotifKind.Victory);
        PlanetViewWindow.Instance?.RefreshIfShowing(b);
        OnChanged?.Invoke();
    }

    // ---- Phase 3: the first buildings -----------------------------------------------------------

    /// The structure the current step is asking for, or null.
    public static SurfaceBuildingType? Target
    {
        get
        {
            switch (Step)
            {
                case OnboardingStep.PlaceCapitol: return SurfaceBuildingType.PlanetCapitol;
                case OnboardingStep.Farm: return SurfaceBuildingType.Farm;
                case OnboardingStep.Combustion: return SurfaceBuildingType.CombustionPlant;
                case OnboardingStep.Mine: return SurfaceBuildingType.Mine;
                case OnboardingStep.Housing: return SurfaceBuildingType.Habitat;
                default: return null;
            }
        }
    }

    /// The structure to flag on THIS world's Build tab, if any.
    public static SurfaceBuildingType? TargetOn(CelestialBody b)
        => b != null && b == World && Step >= OnboardingStep.PlaceCapitol && Step < OnboardingStep.Done ? Target : null;

    /// Built or on the build queue.
    static int Have(CelestialBody b, SurfaceBuildingType t)
    {
        int n = SurfaceBuildManager.CountOf(b, t);
        var jobs = SurfaceBuildQueue.Peek(b);
        if (jobs != null) foreach (var j in jobs) if (j != null && j.type == t) n++;
        return n;
    }

    /// Does any ground on this world clear the structure's index floor?
    static bool Possible(CelestialBody b, SurfaceBuildingType t)
    {
        var info = SurfaceBuildingDatabase.Get(t);
        if (info == null) return false;
        if (info.index == SurfaceIndexKind.None) return true;
        return SurfaceIndex.Best(b, info.index) >= SurfaceIndex.Floor(info.index);
    }

    static float nextCheck;

    /// Advance when the current step's structure exists. Polled by the onboarding UI, a few times a second.
    public static void Tick()
    {
        if (World == null || Step < OnboardingStep.Farm || Step >= OnboardingStep.Done) return;
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + 0.3f;

        // A world lost or abandoned mid-tutorial ends the tutorial rather than nagging about it.
        if (World.owner != FactionManager.Player) { Step = OnboardingStep.Done; OnChanged?.Invoke(); return; }

        var before = Step;
        while (Step >= OnboardingStep.Farm && Step < OnboardingStep.Done)
        {
            var t = Target;
            if (!t.HasValue) break;
            int want = Step == OnboardingStep.Housing ? HousingWanted : 1;
            // A step this world cannot satisfy (a farm on a world with no fertile ground, a mine with no
            // mineral ground) is skipped, or the opening would sit on it forever.
            if (Have(World, t.Value) < want && Possible(World, t.Value)) break;
            Step++;
        }
        if (Step == before) return;

        SimpleAudio.Instance?.PlayNotify(NotifKind.Info);
        if (Step == OnboardingStep.Done)
            NotificationManager.Instance?.Push("Your capital is under way",
                "That's the foundation: food, power, metal and housing. The rest of the galaxy is yours to find.",
                null, NotifKind.Victory);
        else
            NotificationManager.Instance?.Push("Next", Instruction(),
                () => PlanetViewWindow.Instance?.ShowFor(World, PlanetViewWindow.Tab.Build), NotifKind.Info);
        PlanetViewWindow.Instance?.RefreshIfShowing(World);
        OnChanged?.Invoke();
    }

    /// One line on what to do now, for the banner and the notifications.
    public static string Instruction()
    {
        string w = World != null ? World.name : "your world";
        switch (Step)
        {
            case OnboardingStep.ChooseWorld:
                return Options.Count > 1
                    ? $"Choose your starting world: click one of the {Options.Count} worlds with a green ring."
                    : "Your starting world has a green ring: click it to begin.";
            case OnboardingStep.PlaceCapitol: return $"Choose where to place your capital on {w}: pick the Planet Capitol from the Civil tab.";
            case OnboardingStep.Farm: return "Place a Farm of at least 3 tiles (Agriculture tab).";
            case OnboardingStep.Combustion: return "Place a Combustion Plant of 2-3 tiles (Electrical tab).";
            case OnboardingStep.Mine: return "Place a Mine of at least 3 tiles (Industry tab).";
            case OnboardingStep.Housing:
                int have = World != null ? Have(World, SurfaceBuildingType.Habitat) : 0;
                return $"Place {HousingWanted} Habitat Blocks for your city (Civil tab) — {have}/{HousingWanted}.";
            default: return "";
        }
    }

    /// A new galaxy or a loaded save ends any opening in progress.
    public static void Reset()
    {
        Step = OnboardingStep.None;
        World = null;
        needsFraming = false;
        var zone = SystemContext.Zone;
        if (zone != null) zone.SetSpeciesMode(false);
        OnChanged?.Invoke();
    }
}
