using UnityEngine;

// Legacy scene component kept for compatibility. TimeControl is now the source of truth for
// simulation speed; this no longer writes Time.timeScale every frame (which used to fight the HUD
// time slider). Space bar toggles pause/resume via TimeControl.
public class TimeController : MonoBehaviour
{
    // Kept so TimeControl.Set can mirror the value here; not applied every frame anymore.
    public static float timeScale = 1f;

    void Update()
    {
        // Space toggles pause, except while the menu is open (there it's the UI submit key).
        bool menuOpen = EscapeMenu.Instance != null && EscapeMenu.Instance.IsOpen;
        if (!menuOpen && Input.GetKeyDown(KeyCode.Space))
            TimeControl.TogglePause();
    }
}
