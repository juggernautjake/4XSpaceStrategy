using UnityEngine;

// Click a star / cluster / black hole to focus its system, open its info panel, zoom onto it, move
// the habitable-zone rings to it, and float its labels.
[RequireComponent(typeof(Collider))]
public class StarInteraction : MonoBehaviour
{
    public StarData star;          // the COMBINED cluster data (light/heat/HZ) — same on every sun of a system
    public StarData member;        // this specific sun's OWN data (== star for a single-star system)
    public StarSystemData system;

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

        SimpleAudio.Instance?.PlaySelect();

        if (system != null)
        {
            GameManager.Instance?.SetFocus(system);
            if (SystemContext.Zone != null)
                SystemContext.Zone.Retarget(system.combinedStar, system.pivot, system.bodies);
        }

        // The tabbed Inspector is the ONE readout for anything you click, stars included. The old simpler
        // StarInfoPanel was a duplicate that popped up alongside it; it's retired, so only the fleshed-out
        // tabbed window shows now.
        InspectorWindow.Instance?.Inspect(InspectorTarget.Of(star, system), resetTrail: true);

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
