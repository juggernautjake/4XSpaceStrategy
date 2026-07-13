using UnityEngine;
using System.Collections;

[RequireComponent(typeof(LineRenderer))]
public class OrbitController : MonoBehaviour
{
    [Header("Orbit Settings")]
    public Transform parentBody;
    public float orbitRadius = 10f;
    public float orbitSpeed = 20f;

    [Header("Visual Ring")]
    public int segments = 120;
    public float lineWidth = 0.1f;
    public Color ringColor = new Color(0.4f, 0.7f, 1f, 0.6f);

    private LineRenderer lineRenderer;
    private float currentAngle = 0f;
    private Transform ringTransform;

    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
    }

    public void Setup(Transform parent, float radius, float speed)
    {
        parentBody = parent;
        orbitRadius = radius;
        orbitSpeed = speed;

        if (parentBody == null)
        {
            Debug.LogWarning($"Setup: null parent on {gameObject.name}");
            return;
        }

        currentAngle = Random.Range(0f, 360f);
        CreateOrbitRing();
        UpdatePosition();

        Debug.Log($"Setup OK for {gameObject.name} | Parent: {parentBody.name} | R:{orbitRadius:F1} S:{orbitSpeed:F1}");
    }

    private void CreateOrbitRing()
    {
        if (ringTransform != null) Destroy(ringTransform.gameObject);

        GameObject ringObj = new GameObject(gameObject.name + "_OrbitRing");
        ringTransform = ringObj.transform;
        ringTransform.SetParent(parentBody); // ← Child of planet (stable!)
        ringTransform.localPosition = Vector3.zero;

        LineRenderer ringLR = ringObj.AddComponent<LineRenderer>();
        ringLR.loop = true;
        ringLR.positionCount = segments;
        ringLR.startWidth = lineWidth;
        ringLR.endWidth = lineWidth;
        ringLR.useWorldSpace = false; // Local space now
        ringLR.material = new Material(Shader.Find("Sprites/Default"));
        ringLR.startColor = ringColor;
        ringLR.endColor = ringColor;

        DrawRing(ringLR);
        Debug.Log($"[CreateOrbitRing] Ring created as child of {parentBody.name} for {gameObject.name}");
    }

    private void DrawRing(LineRenderer ringLR)
    {
        if (ringLR == null) return;

        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 localPos = new Vector3(
                Mathf.Cos(angle) * orbitRadius,
                0f,
                Mathf.Sin(angle) * orbitRadius
            );
            ringLR.SetPosition(i, localPos);
        }
    }

    private void Update()
    {
        if (parentBody == null) return;

        float angularSpeed = orbitSpeed / Mathf.Max(1f, orbitRadius);
        currentAngle += angularSpeed * Time.deltaTime;
        UpdatePosition();
    }

    public void UpdatePosition()
    {
        if (parentBody == null) return;

        float rad = currentAngle * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(
            Mathf.Cos(rad) * orbitRadius,
            0f,
            Mathf.Sin(rad) * orbitRadius
        );
        transform.position = parentBody.position + offset;
    }

    public void SetRadius(float newRadius)
    {
        float old = orbitRadius;
        orbitRadius = Mathf.Max(1f, newRadius);
        Debug.Log($"[SetRadius] {old:F1} → {orbitRadius:F1} on {gameObject.name}");

        if (ringTransform != null)
        {
            LineRenderer lr = ringTransform.GetComponent<LineRenderer>();
            if (lr != null) DrawRing(lr);
        }

        // Force immediate position update
        UpdatePosition();
    }

    public void SetSpeed(float newSpeed)
    {
        orbitSpeed = newSpeed;
        Debug.Log($"[SetSpeed] Changed to {newSpeed:F1} on {gameObject.name}");
    }

    public void RestoreParent(Transform parent)
    {
        parentBody = parent;
        if (ringTransform != null) ringTransform.SetParent(parent);
        Debug.Log($"Restored parent for {gameObject.name}");
    }

    private void OnDestroy()
    {
        if (ringTransform != null) Destroy(ringTransform.gameObject);
    }

    public void ForceRingRedraw()
    {
        if (ringTransform != null)
        {
            LineRenderer lr = ringTransform.GetComponent<LineRenderer>();
            if (lr != null) DrawRing(lr);
        }
    }
}