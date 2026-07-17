using UnityEngine;

// Click a star / cluster / black hole to focus its system, open its info panel, zoom onto it, move
// the habitable-zone rings to it, and float its labels.
[RequireComponent(typeof(Collider))]
public class StarInteraction : MonoBehaviour
{
    public StarData star;
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

    string Name()
    {
        if (star == null) return "Star";
        if (star.isBlackHole) return "Black Hole";
        return $"{star.type}-type Star";
    }

    string Category()
    {
        string sys = system != null ? $"{system.name} · {FactionManager.OwnerLabel(system.owner)}" : "";
        string kind = star == null ? "Star"
            : star.isBlackHole ? "Black Hole"
            : star.starCount >= 3 ? "Ternary system"
            : star.starCount == 2 ? "Binary system"
            : $"{star.type}-type star";
        return string.IsNullOrEmpty(sys) ? kind : $"{kind}  ({sys})";
    }
}
