using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// ============================================================================================
// THE SCAN LINE ON THE MAP (2026-10-11)
//
// The survey fog already lifts block by block and the host map frames the block being worked. What it
// did not have is the thing that makes it read as a SCAN: a bright line at the leading edge, sweeping
// across the working block in step with the real progress, with a short shimmering trail behind it.
// And two gaps:
//
//   * a MOON pane had no marker at all — its fog lifted with nothing on screen saying where or why;
//   * a DEEP survey had no marker unless that exact index overlay happened to be switched on, so a
//     research ship could spend minutes reading a world with nothing moving on its map.
//
// This component sits on a map's content rect (the planet's map and each moon's) and draws, per frame:
//   level 1 — a scan line inside every block a ship is working (Survey.ActiveBlocks), at x0 + frac*w;
//             on moon panes the block frame too (the host map already draws its own frames).
//   level 2 — a frame around the deep-survey front (Survey.DeepFrontBlocks) in the colour of the index
//             being read, with its own scan line.
// Everything is in MAP-NORMALISED anchors (0..1 of the content rect), so it pans and zooms with the map
// under the pane zoom model for free. Hairlines are pixel offsets, so they stay crisp at every zoom.
//
// POOLED: Images are created on first need and reused; nothing allocates per frame. The brightness steps
// on the fleet beat in four levels, like the running lights, which is the "subtle shimmer".
// ============================================================================================
public class SurveyMapScan : MonoBehaviour
{
    Func<CelestialBody> who;
    bool drawGroundFrames;
    RectTransform layer;

    // One "mark" per block: a frame (4 edges) and a scan line with two trail lines.
    class Mark
    {
        public RectTransform root;
        public Image[] edges = new Image[4];
        public RectTransform line, trail1, trail2;
        public Image lineImg, trail1Img, trail2Img;
    }

    readonly List<Mark> marks = new List<Mark>();
    static readonly Survey.Block[] scratch = new Survey.Block[8 * Survey.MaxHeads];

    public static SurveyMapScan Attach(RectTransform mapContent, Func<CelestialBody> who, bool drawGroundFrames)
    {
        if (mapContent == null) return null;
        var go = UIFactory.NewUI(mapContent, "SurveyScan");
        var s = go.AddComponent<SurveyMapScan>();
        s.who = who;
        s.drawGroundFrames = drawGroundFrames;
        s.layer = go.GetComponent<RectTransform>();
        UIFactory.Stretch(s.layer);
        return s;
    }

    Mark Make()
    {
        var m = new Mark();
        m.root = UIFactory.NewUI(layer, "Scan").GetComponent<RectTransform>();
        m.root.pivot = new Vector2(0.5f, 0.5f);
        for (int i = 0; i < 4; i++)
        {
            m.edges[i] = UIFactory.Panel(m.root, "Edge", Color.white);
            m.edges[i].raycastTarget = false;
        }
        Edge(m.edges[0].rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -2), Vector2.zero);
        Edge(m.edges[1].rectTransform, new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, 2));
        Edge(m.edges[2].rectTransform, new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(2, 0));
        Edge(m.edges[3].rectTransform, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-2, 0), Vector2.zero);

        m.trail2Img = UIFactory.Panel(m.root, "Trail2", Color.white); m.trail2Img.raycastTarget = false;
        m.trail1Img = UIFactory.Panel(m.root, "Trail1", Color.white); m.trail1Img.raycastTarget = false;
        m.lineImg = UIFactory.Panel(m.root, "Line", Color.white); m.lineImg.raycastTarget = false;
        m.trail2 = m.trail2Img.rectTransform; m.trail1 = m.trail1Img.rectTransform; m.line = m.lineImg.rectTransform;
        m.root.gameObject.SetActive(false);
        marks.Add(m);
        return m;
    }

    static void Edge(RectTransform e, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
    {
        e.anchorMin = aMin; e.anchorMax = aMax; e.offsetMin = oMin; e.offsetMax = oMax;
    }

    void LateUpdate()
    {
        var b = who != null ? who() : null;
        int used = 0;
        if (b != null && b.surface != null && !GameMode.DevMode)
        {
            int w = Mathf.Max(1, MapMetrics.SurfW(b)), h = Mathf.Max(1, MapMetrics.SurfH(b));

            // Four brightness steps on the fleet beat: the shimmer.
            float step = Mathf.Floor(Mathf.Repeat(FleetClock.Beats * 2f, 4f)) / 3f;

            // Level 1: every block a ship is working.
            if (!b.Surveyed)
            {
                int n = Survey.ActiveBlocks(b, scratch);
                var lineCol = new Color(1f, 1f, 1f, Mathf.Lerp(0.70f, 1f, step));
                var frameCol = new Color(1f, 1f, 1f, drawGroundFrames ? Mathf.Lerp(0.55f, 0.95f, step) : 0f);
                for (int i = 0; i < n; i++)
                {
                    var m = used < marks.Count ? marks[used] : Make();
                    used++;
                    Place(m, scratch[i], w, h, lineCol, frameCol, scanThisBlock: i == 0 || scratch[i].frac > 0f);
                }
            }

            // Level 2: the deep-survey front, in the colour of the index being read.
            int d = Survey.DeepFrontBlocks(b, scratch);
            if (d > 0)
            {
                var k = Survey.CurrentIndex(b);
                Color ic = k != SurfaceIndexKind.None ? SurfaceIndex.Outline(k) : UITheme.Good;
                var lineCol = new Color(Mathf.Lerp(ic.r, 1f, 0.5f), Mathf.Lerp(ic.g, 1f, 0.5f), Mathf.Lerp(ic.b, 1f, 0.5f), Mathf.Lerp(0.70f, 1f, step));
                var frameCol = new Color(ic.r, ic.g, ic.b, Mathf.Lerp(0.55f, 0.95f, step));
                for (int i = 0; i < d; i++)
                {
                    var m = used < marks.Count ? marks[used] : Make();
                    used++;
                    Place(m, scratch[i], w, h, lineCol, frameCol, scanThisBlock: i == 0);
                }
            }
        }

        for (int i = used; i < marks.Count; i++)
            if (marks[i].root.gameObject.activeSelf) marks[i].root.gameObject.SetActive(false);
    }

    static void Place(Mark m, Survey.Block blk, int w, int h, Color lineCol, Color frameCol, bool scanThisBlock)
    {
        if (!m.root.gameObject.activeSelf) m.root.gameObject.SetActive(true);

        // A block straddling the seam is drawn at its left run only, like the host's own marker; the map's
        // wrap mirrors carry the remainder.
        float x0 = blk.x0 / (float)w;
        float x1 = Mathf.Min(1f, (blk.x0 + blk.w) / (float)w);
        float y0 = blk.y0 / (float)h;
        float y1 = Mathf.Min(1f, (blk.y0 + blk.h) / (float)h);
        m.root.anchorMin = new Vector2(x0, y0);
        m.root.anchorMax = new Vector2(x1, y1);
        m.root.offsetMin = Vector2.zero; m.root.offsetMax = Vector2.zero;

        for (int i = 0; i < 4; i++)
        {
            bool on = frameCol.a > 0f;
            if (m.edges[i].enabled != on) m.edges[i].enabled = on;
            if (on) m.edges[i].color = frameCol;
        }

        bool scan = scanThisBlock;
        if (m.line.gameObject.activeSelf != scan)
        {
            m.line.gameObject.SetActive(scan); m.trail1.gameObject.SetActive(scan); m.trail2.gameObject.SetActive(scan);
        }
        if (!scan) return;

        // The leading edge at the block's real progress; two dimmer lines trail it, so it reads as a
        // sweep moving right (the direction the survey advances) rather than as a static stripe.
        float f = Mathf.Clamp01(blk.frac);
        Line(m.line, m.lineImg, f, 0f, 2f, lineCol, 1f);
        Line(m.trail1, m.trail1Img, f, -3f, 2f, lineCol, 0.45f);
        Line(m.trail2, m.trail2Img, f, -6f, 2f, lineCol, 0.2f);
    }

    static void Line(RectTransform rt, Image img, float f, float px, float thick, Color c, float alphaMul)
    {
        rt.anchorMin = new Vector2(f, 0f); rt.anchorMax = new Vector2(f, 1f);
        rt.offsetMin = new Vector2(px - thick * 0.5f, 0f); rt.offsetMax = new Vector2(px + thick * 0.5f, 0f);
        c.a *= alphaMul;
        img.color = c;
    }
}
