using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// WHAT THE FLEET IS SHOOTING AT
//
// Concentrating fire is the most consequential order in the game and, without a mark on the map, the
// least visible one. Every shot already flies from A to B — but at fighting zoom, with two dozen
// hulls and a hundred rounds in the air, "are they all shooting the same thing" is not a question the
// tracer fire answers. The player would give the order, see no change, and reasonably conclude it did
// nothing.
//
// So the designated target wears a ring. One per distinct target the current selection is
// concentrating on, which is usually one — but a selection spanning two squadrons under two different
// orders genuinely has two, and drawing one would be picking a winner and lying about the other.
//
// ---- WHY IT IS DRAWN AND NOT LISTED ------------------------------------------------------------
//
// The command bar reports the target by name, which answers "what did I order" and not "which of
// those six ships is it". At the zoom a battle is fought at, a name is a lookup and a ring is an
// answer.
//
// ---- WHY IT COUNTER-ROTATES --------------------------------------------------------------------
//
// The ring is drawn in the plane of play and spun slowly. A static ring at this size reads as part of
// the ship's own art — the game already draws selection rings, orbit rings, range rings and habitable
// zones — and the one thing this must not read as is scenery. Rotation is the cheapest way to say
// "this is a live order", and it is the same trick the lock-on indicator uses for the same reason.
// ============================================================================================
public class FocusTargetMarker : MonoBehaviour
{
    public static FocusTargetMarker Instance;

    const int Segments = 40;

    /// How big the ring is relative to the hull it surrounds, and the floor that keeps it visible on a
    /// fighter. Generous: a ring that hugs the hull is indistinguishable from the selection ring.
    const float RadiusScale = 1.9f;
    const float MinRadius = 0.9f;

    const float SpinDegPerSec = 34f;

    class Ring
    {
        public LineRenderer lr;
        public Unit target;
    }

    readonly List<Ring> rings = new List<Ring>();
    readonly List<Unit> wanted = new List<Unit>();
    Material mat;

    public static void Create()
    {
        if (Instance != null) return;
        var go = new GameObject("FocusTargetMarker");
        Instance = go.AddComponent<FocusTargetMarker>();
        // Vanishes at galaxy zoom with the hulls it marks, like every other combat visual.
        go.AddComponent<MapTierVisibility>();
    }

    void Awake()
    {
        Instance = this;
        mat = new Material(Shader.Find("Sprites/Default"));
    }

    void LateUpdate()
    {
        // AFTER the hulls have moved this frame, so a ring is never a frame behind the ship it is
        // marking — at fighting zoom a ring lagging its target reads as two separate objects.
        Gather();

        for (int i = 0; i < rings.Count; i++)
        {
            var r = rings[i];
            bool live = i < wanted.Count;
            if (r.lr.enabled != live) r.lr.enabled = live;
            if (!live) { r.target = null; continue; }

            r.target = wanted[i];
            Draw(r);
        }
    }

    /// Every distinct target the current selection is concentrating on.
    void Gather()
    {
        wanted.Clear();
        var sel = UnitSelection.Selected;
        if (sel == null) return;

        for (int i = 0; i < sel.Count; i++)
        {
            var t = CombatOrders.FocusFor(sel[i]);
            if (t == null || t.IsDestroyed) continue;
            if (!wanted.Contains(t)) wanted.Add(t);
            // A selection spanning more than four separate orders is not a picture anyone can read,
            // and drawing forty rings would be worse than drawing none.
            if (wanted.Count >= 4) break;
        }

        while (rings.Count < wanted.Count) MakeRing();
    }

    void Draw(Ring r)
    {
        Vector3 c = CombatManager.PosOf(r.target);

        // Sized off the hull so a dreadnought gets a bigger ring than a fighter — which is also what
        // keeps it clear of the hull on the big one and visible at all on the small one.
        float hull = r.target.Info != null ? Mathf.Clamp(0.3f + r.target.Info.health / 1400f, 0.3f, 1.6f) : 0.5f;
        float radius = Mathf.Max(MinRadius, hull * RadiusScale);

        float spin = FleetClock.Beats * SpinDegPerSec * Mathf.Deg2Rad;

        for (int i = 0; i <= Segments; i++)
        {
            float a = spin + (i / (float)Segments) * Mathf.PI * 2f;
            r.lr.SetPosition(i, c + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
        }

        // A hostile mark, and it pulses. Red because every other ring the game draws — selection,
        // orbit, range, habitable zone — is cyan, green or white, so the one that means "this thing is
        // being killed" gets the colour nothing else uses.
        float pulse = 0.6f + 0.4f * Mathf.Sin(FleetClock.Beats * Mathf.PI * 3f);
        var col = new Color(1f, 0.32f, 0.24f, Mathf.Lerp(0.45f, 0.95f, pulse));
        r.lr.startColor = r.lr.endColor = col;
    }

    void MakeRing()
    {
        var go = new GameObject("FocusRing");
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = false;                       // the extra segment closes it; loop double-draws the seam
        lr.positionCount = Segments + 1;
        lr.widthMultiplier = 0.055f;
        lr.material = new Material(mat);
        lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.enabled = false;
        rings.Add(new Ring { lr = lr });
    }
}
