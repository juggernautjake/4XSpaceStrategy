using UnityEngine;

// Keeps a body clickable when the camera zooms out: it grows the sphere collider's radius with camera
// height so far-away bodies still present a reasonable click target. The visual size is unchanged —
// only the invisible pick radius grows. Nearest-to-camera still wins, so overlaps resolve sensibly.
public class ClickColliderScaler : MonoBehaviour
{
    public float baseRadius = 1f;   // local radius when the camera is close

    Camera cam;
    SphereCollider sc;

    void Awake()
    {
        cam = Camera.main;
        sc = GetComponent<SphereCollider>();
        if (sc != null && baseRadius <= 0f) baseRadius = sc.radius;
    }

    void LateUpdate()
    {
        if (sc == null) return;
        if (cam == null) { cam = Camera.main; if (cam == null) return; }

        float height = Mathf.Max(0f, cam.transform.position.y);
        float lossy = Mathf.Max(0.0001f, transform.lossyScale.x);
        float extraWorld = Mathf.Clamp(height * 0.015f, 0f, 12f);   // world units added when far out
        sc.radius = baseRadius + extraWorld / lossy;
    }
}
