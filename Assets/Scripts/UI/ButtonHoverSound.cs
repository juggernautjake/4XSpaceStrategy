using UnityEngine;
using UnityEngine.EventSystems;

// Plays a soft hover sound when the pointer enters a UI button. Added automatically by UIFactory.
public class ButtonHoverSound : MonoBehaviour, IPointerEnterHandler
{
    public void OnPointerEnter(PointerEventData e)
    {
        SimpleAudio.Instance?.PlayHover();
    }
}
