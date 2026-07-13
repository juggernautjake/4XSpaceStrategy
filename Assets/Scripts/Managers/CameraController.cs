using UnityEngine;
using UnityEngine.EventSystems;

public class CameraController : MonoBehaviour
{
    [Header("Movement")]
    public float panSpeed = 30f;           // WASD panning speed
    public float heightSpeed = 80f;        // Mouse wheel height change speed

    [Header("Limits")]
    public float minHeight = 8f;           // Closest to the system
    public float maxHeight = 120f;         // Farthest view

    private float targetHeight;            // For smooth movement

    private void Start()
    {
        targetHeight = transform.position.y;
    }

    private void Update()
    {
        // Prevent camera movement when hovering UI
        if (EventSystem.current.IsPointerOverGameObject())
            return;
        HandlePanning();
        HandleHeightChange();
        SmoothHeightMovement();
        KeepCameraAngle();
    }

    private void HandlePanning()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        Vector3 move = new Vector3(h, 0, v) * panSpeed * Time.deltaTime;
        transform.Translate(move, Space.World);
    }

    private void HandleHeightChange()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0)
        {
            targetHeight += scroll * heightSpeed * -25f; // Negative for natural feel
            targetHeight = Mathf.Clamp(targetHeight, minHeight, maxHeight);
        }
    }

    private void SmoothHeightMovement()
    {
        Vector3 pos = transform.position;
        pos.y = Mathf.Lerp(pos.y, targetHeight, 10f * Time.deltaTime); // Smoothness
        transform.position = pos;
    }

    private void KeepCameraAngle()
    {
        // Keep a nice 55 degree top-down angle
        transform.rotation = Quaternion.Euler(55f, transform.eulerAngles.y, 0);
    }
}