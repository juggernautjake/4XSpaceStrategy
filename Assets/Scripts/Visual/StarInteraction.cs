using UnityEngine;

// Click a star / cluster / black hole to focus its system, open its info panel, zoom onto it, move
// the habitable-zone rings to it, and float its labels.
[RequireComponent(typeof(Collider))]
public class StarInteraction : MonoBehaviour
{
    public StarData star;          // the COMBINED cluster data (light/heat/HZ) — same on every sun of a system
    public StarData member;        // this specific sun's OWN data (== star for a single-star system)
    public StarSystemData system;

    /// This is the GALACTIC CORE, not a star in a system — so clicking it reports on the galaxy rather
    /// than on a system that does not exist.
    ///
    /// Set explicitly by SystemVisualizer rather than inferred from `system == null`. A rare black-hole
    /// SYSTEM is a genuine system with worlds orbiting it and must keep the star tabs; inferring
    /// "core-ness" from a missing field would eventually catch one of those and hide its planets behind
    /// a galaxy readout.
    public bool isGalacticCore;

    /// The rendered transform for a StarData, or null if it isn't on screen.
    ///
    /// StarData is pure data and holds no back-reference to its visual — unlike CelestialBody, which has
    /// `visualObject`. This component is the only thing that knows the mapping, so the lookup lives here
    /// rather than being re-derived by every caller that wants to point a camera at a star.
    ///
    /// A scan rather than a registry because `star` is assigned AFTER AddComponent (see
    /// SystemVisualizer.CreateStarVisual), so an Awake-time registration would file every star under
    /// null. It only runs on a click, not per frame.
    public static Transform TransformOf(StarData s)
    {
        if (s == null) return null;
        foreach (var si in FindObjectsByType<StarInteraction>(FindObjectsSortMode.None))
            if (si.star == s) return si.transform;
        return null;
    }

    /// EVERY rendered transform mapped to this COMBINED StarData — one for a single star, several for a
    /// binary/ternary cluster (each sun carries the same combined `star`).
    public static System.Collections.Generic.List<Transform> AllOf(StarData s)
    {
        var list = new System.Collections.Generic.List<Transform>();
        if (s == null) return list;
        foreach (var si in FindObjectsByType<StarInteraction>(FindObjectsSortMode.None))
            if (si.star == s) list.Add(si.transform);
        return list;
    }

    /// The rendered transform(s) for one INDIVIDUAL sun (its own `member` data) — so the Dev star editor
    /// can rescale/re-light and re-orbit a single sun of a cluster without touching its sisters.
    public static System.Collections.Generic.List<Transform> MembersOf(StarData member)
    {
        var list = new System.Collections.Generic.List<Transform>();
        if (member == null) return list;
        foreach (var si in FindObjectsByType<StarInteraction>(FindObjectsSortMode.None))
            if (si.member == member) list.Add(si.transform);
        return list;
    }

    void OnMouseDown()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            return;

        // While a fleet is being aimed, this click is the destination confirmation — FleetMovementController
        // owns it. PlanetClick has always guarded this; stars never did, and it started to matter once
        // ClickPriority could forward a click here.
        if (FleetMovementController.Instance != null && FleetMovementController.Instance.IsTargeting) return;

        // A ship parked at this star wins the click — same reasoning as PlanetClick, and a star's pick
        // sphere is the largest of the lot.
        if (ClickPriority.TryClickUnitUnderCursor()) return;

        SimpleAudio.Instance?.PlaySelect();

        // The tabbed Inspector is the ONE readout for anything you click, stars included. The old simpler
        // StarInfoPanel was a duplicate that popped up alongside it; it's retired, so only the fleshed-out
        // tabbed window shows now.
        //
        // No double-click handling here, deliberately. In the detailed system view a SINGLE click already
        // opens the cluster Overview — Inspect() resets to tab 0, and Overview is the first tab the Star
        // target registers — so a double click opens exactly the same window, and special-casing it would
        // only add a branch that does nothing. The galaxy overview is the place that genuinely needed the
        // gesture: out there a single click opens the light summary window, so double-click is what
        // reaches the full per-sun breakdown (see GalaxyStarProxy.OnMouseDown).
        if (isGalacticCore)
            InspectorWindow.Instance?.Inspect(
                InspectorTarget.GalaxyTarget(SystemContext.Galaxy, star), resetTrail: true);
        else
            StarOverview.OpenFromStar(star, system);

        // Clicking a star no longer moves the camera either (it just shows the info + the Inspector, which
        // has its own "Focus" button). Auto-focusing on click was disorienting and was also what let a star
        // "lock" the camera onto itself. Labels still float so you can see what you clicked.
        ObjectLabelManager.Instance?.ShowForStar(transform, transform.lossyScale.x * 0.5f, Name(), Category());
    }

    // The map label shows this sun's NAME (its own, with the A/B/C suffix in a cluster) — never its
    // spectral type, which belongs in the star info panel.
    string Name()
    {
        if (member != null && !string.IsNullOrEmpty(member.name)) return member.name;
        if (star != null && star.isBlackHole) return "Black Hole";
        return system != null && !string.IsNullOrEmpty(system.name) ? system.name : "Star";
    }

    // Classification + owner only — deliberately no spectral type here (it's in the panel).
    string Category()
    {
        if (star != null && star.isBlackHole) return "Black hole";
        string cls = star != null && star.starCount >= 3 ? "Ternary system"
                   : star != null && star.starCount == 2 ? "Binary system"
                   : "Star system";
        string owner = system != null ? FactionManager.OwnerLabel(system.owner) : "";
        return string.IsNullOrEmpty(owner) ? cls : $"{cls} · {owner}";
    }
}
