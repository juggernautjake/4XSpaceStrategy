using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ============================================================================================
// THE BUILDING'S CARD — click a placed building, see its whole grid's share at a glance (2026-10-10)
//
// "When selecting a placed building, have a popup window on that building displaying the name/type of
// building, how many of that type of building are connected in a single grid, what the total output and
// total upkeep is for that grid of that building type. Have the window close if clicking elsewhere."
//
// A small card that sits just above the selected building on the Surface View map (below it, if the
// building is at the top edge) and follows it as the map pans and zooms. It reads:
//
//   COMBUSTION PLANT                                 x
//   4 on Grid 2                      (or "3 on this world · not on a power grid")
//   Output   +32.0 energy/s
//   Upkeep   none
//
// "The grid" is the power net the building stands on (PowerGrid.NetOf). A structure that is not on one —
// a farm on its own, or anything outside every pylon's reach — is counted with the others of its kind on
// the world that are likewise off-grid, and the card says so, because "1 on this grid" would be a lie.
//
// Every figure uses the formula the economy actually runs (see WorldEconomy), so the card can never quote
// a number the grid is not really making. Refreshed twice a second while open.
//
// CLOSING: any click that does not land on the card closes it (the selection itself stays; clicking the
// same building again brings the card back — PlanetViewWindow.OnGridClick calls Reopen). Bare ground
// clears the selection, which closes it too.
// ============================================================================================
public class BuildingGridPopup : MonoBehaviour
{
    public System.Func<CelestialBody> Body;
    public System.Func<bool> Visible;

    RectTransform viewport, map, card, accent;
    TMP_Text title, body;
    PlacedBuilding shownFor, dismissedFor;
    float nextRefresh;

    const float Width = 236f;

    public static BuildingGridPopup Create(RectTransform mapRT)
    {
        var vp = mapRT.parent as RectTransform;
        var go = UIFactory.NewUI(vp, "BuildingGridPopup");
        var pop = go.AddComponent<BuildingGridPopup>();
        pop.viewport = vp; pop.map = mapRT;
        var rt = go.GetComponent<RectTransform>();
        UIFactory.Stretch(rt);
        pop.BuildCard(rt);
        SurfaceSelection.OnChanged += pop.OnSelectionChanged;
        return pop;
    }

    void OnDestroy() => SurfaceSelection.OnChanged -= OnSelectionChanged;

    void OnSelectionChanged() { dismissedFor = null; nextRefresh = 0f; keepFrame = Time.frameCount; }

    /// Clicking the building that is already selected: bring its card back if it was closed.
    public void Reopen() { dismissedFor = null; nextRefresh = 0f; keepFrame = Time.frameCount; }

    /// Is this screen point on the open card? PlanetViewWindow asks before starting a map pan or a
    /// build/demolish stroke, because those poll Input directly and would otherwise run under the card.
    public bool Covers(Vector2 screen)
        => card != null && card.gameObject.activeInHierarchy
        && RectTransformUtility.RectangleContainsScreenPoint(card, screen, EventCam());

    // The click that SELECTED something (or re-clicked the selection) is not a click-away: OnGridClick
    // runs in the EventSystem's Update, before LateUpdate, and stamps the frame so the release that
    // caused it cannot also close the card it just opened.
    int keepFrame = -1;
    Vector2 pressAt;
    bool pressOnCard;

    /// Pixels the pointer may travel between press and release and still count as a click — the same
    /// threshold PlanetViewWindow's pan uses, so a drag of the map is never also a click-away.
    const float ClickSlop = 5f;

    void BuildCard(RectTransform parent)
    {
        var go = UIFactory.NewUI(parent, "Card");
        card = go.GetComponent<RectTransform>();
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(0.5f, 0f);
        var bg = go.AddComponent<Image>();
        bg.color = new Color(UITheme.PanelBg.r, UITheme.PanelBg.g, UITheme.PanelBg.b, 0.97f);
        bg.raycastTarget = true;                    // the card swallows its own clicks
        var ol = go.AddComponent<Outline>();
        ol.effectColor = new Color(UITheme.AccentDim.r, UITheme.AccentDim.g, UITheme.AccentDim.b, 0.9f);
        ol.effectDistance = new Vector2(1f, -1f);

        var vl = go.AddComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(10, 10, 9, 9);
        vl.spacing = 4f;
        vl.childControlWidth = true; vl.childControlHeight = true;
        vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
        var fit = go.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // The category colour along the top edge, so the card reads as "an Electrical thing" at a glance.
        var ac = UIFactory.NewUI(card, "Accent");
        accent = ac.GetComponent<RectTransform>();
        accent.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        accent.anchorMin = new Vector2(0f, 1f); accent.anchorMax = new Vector2(1f, 1f);
        accent.pivot = new Vector2(0.5f, 1f);
        accent.sizeDelta = new Vector2(0f, 3f); accent.anchoredPosition = Vector2.zero;
        ac.AddComponent<Image>().raycastTarget = false;

        title = UIFactory.Text(card, "", 15, UITheme.Text);
        title.fontStyle = FontStyles.Bold;
        title.margin = new Vector4(0f, 0f, 22f, 0f);  // room for the close button

        body = UIFactory.Text(card, "", 13, UITheme.SubText);
        body.richText = true;
        body.lineSpacing = 6f;

        // Close button, top right.
        var x = UIFactory.NewUI(card, "Close");
        x.AddComponent<LayoutElement>().ignoreLayout = true;
        var xrt = x.GetComponent<RectTransform>();
        xrt.anchorMin = xrt.anchorMax = xrt.pivot = new Vector2(1f, 1f);
        xrt.sizeDelta = new Vector2(22f, 22f); xrt.anchoredPosition = new Vector2(-4f, -5f);
        var ximg = x.AddComponent<Image>(); ximg.color = new Color(1f, 1f, 1f, 0f);
        var btn = x.AddComponent<Button>();
        btn.onClick.AddListener(() => dismissedFor = shownFor);
        var xt = UIFactory.Text(xrt, "x", 15, UITheme.SubText, TextAlignmentOptions.Center);
        xt.raycastTarget = false;
        UIFactory.Stretch(xt.rectTransform);

        card.sizeDelta = new Vector2(Width, 80f);
        card.gameObject.SetActive(false);
    }

    // LateUpdate, not Update: after the EventSystem has delivered this frame's click (so a re-click's
    // Reopen is already in), and after PlanetViewWindow has moved the map (so the card does not trail
    // one frame behind a pan).
    void LateUpdate()
    {
        var b = Body != null ? Body() : null;
        var sel = SurfaceSelection.Body == b ? SurfaceSelection.Selected : null;
        bool open = sel != null && sel != dismissedFor && (Visible == null || Visible()) && b?.surface != null;

        // Any CLICK off the card closes it — judged on the release, and only when the pointer did not
        // travel: a press that becomes a pan of the map is not a click, and closing on the press used to
        // shut the card the moment the player grabbed the map to look around the building. The release
        // is also when uGUI selects (OnPointerClick), so a click on another building, or on this one
        // again, has already stamped keepFrame by now and wins.
        bool down = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
        if (down)
        {
            pressAt = Input.mousePosition;
            pressOnCard = Covers(Input.mousePosition);
        }
        bool up = Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1);
        if (open && card.gameObject.activeSelf && up && !pressOnCard && keepFrame != Time.frameCount
            && ((Vector2)Input.mousePosition - pressAt).sqrMagnitude < ClickSlop * ClickSlop
            && !Covers(Input.mousePosition))
        {
            dismissedFor = sel;
            open = false;
        }

        if (card.gameObject.activeSelf != open) card.gameObject.SetActive(open);
        if (!open) { shownFor = null; return; }

        if (sel != shownFor || Time.unscaledTime >= nextRefresh)
        {
            shownFor = sel;
            nextRefresh = Time.unscaledTime + 0.5f;
            Fill(b, sel);
            // The ContentSizeFitter only resizes at the canvas's next layout pass, which is after this.
            // Positioning against the stale height would put a just-opened (or just-grown) card a frame
            // in the wrong place — overlapping the building, or hanging off the viewport's edge.
            LayoutRebuilder.ForceRebuildLayoutImmediate(card);
        }
        Position(b, sel);
    }

    Camera EventCam()
    {
        var c = GetComponentInParent<Canvas>();
        c = c != null ? c.rootCanvas : null;
        return c == null || c.renderMode == RenderMode.ScreenSpaceOverlay ? null : c.worldCamera;
    }

    // ---- Placement on the map ----------------------------------------------------------------------

    void Position(CelestialBody b, PlacedBuilding p)
    {
        var cells = SurfaceBuildingDatabase.Footprint(p);
        if (cells == null || cells.Count == 0) return;
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (var c in cells)
        {
            minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x + 1);
            minY = Mathf.Min(minY, c.y); maxY = Mathf.Max(maxY, c.y + 1);
        }
        int w = b.surface.width, h = b.surface.height;
        var r = map.rect;
        Vector2 MapToViewport(float gx, float gy)
        {
            var local = new Vector2(r.xMin + gx / w * r.width, r.yMin + gy / h * r.height);
            return viewport.InverseTransformPoint(map.TransformPoint(local));
        }

        var top = MapToViewport((minX + maxX) * 0.5f, maxY);
        var bottom = MapToViewport((minX + maxX) * 0.5f, minY);
        var vr = viewport.rect;
        float cardH = card.rect.height, cardW = card.rect.width;
        const float gap = 6f, margin = 4f;

        // Above the building if it fits, else below it.
        bool above = top.y + gap + cardH <= vr.yMax - margin;
        card.pivot = new Vector2(0.5f, above ? 0f : 1f);
        var at = above ? top + new Vector2(0f, gap) : bottom - new Vector2(0f, gap);
        at.x = Mathf.Clamp(at.x, vr.xMin + cardW * 0.5f + margin, vr.xMax - cardW * 0.5f - margin);
        if (!above) at.y = Mathf.Max(at.y, vr.yMin + cardH + margin);
        // The card's anchor is the viewport's centre, so convert from the viewport's local frame.
        card.anchoredPosition = at - vr.center;
    }

    // ---- The numbers ------------------------------------------------------------------------------

    void Fill(CelestialBody b, PlacedBuilding sel)
    {
        var info = sel.Info;
        title.text = info != null ? info.name.ToUpperInvariant() : sel.Type.ToString();
        if (info != null)
            accent.GetComponent<Image>().color = SurfaceBuildingCategoryStyle.Of(info.category);

        // The group: every building of this type on the same grid — or, off-grid, every off-grid one.
        var net = PowerGrid.NetOf(b, sel);
        var group = new List<PlacedBuilding>();
        foreach (var p in SurfaceBuildManager.On(b))
        {
            if (p.Type != sel.Type) continue;
            var pn = PowerGrid.NetOf(b, p);
            if (net != null ? pn == net : pn == null) group.Add(p);
        }

        float metal = 0f, water = 0f, energy = 0f, research = 0f, growth = 0f, food = 0f, storage = 0f, labor = 0f, draw = 0f;
        foreach (var p in group)
        {
            var i = p.Info; if (i == null) continue;
            float run = p.OutputMult * PowerGrid.PowerFactor(b, p);
            metal += i.metalPerSec * run * TechEffects.OreYieldMult;
            water += i.waterPerSec * run;
            energy += i.energyPerSec * p.OutputMult * (1f + SurfaceBuildManager.AdjacencyBonus(b, p));
            research += i.researchPerSec * run;
            growth += i.popGrowthPerSec * run;
            if (p.Type == SurfaceBuildingType.Farm) food += Carrying.FoodPerFarm * p.OutputMult * Carrying.FarmTechMult();
            storage += i.storageCapacity * p.LevelMult;
            if (ColonyFacilities.IsHousing(p.Type)) labor += SurfaceLabor.HousingEach;
            if (i.powerDraw > 0f && net != null) draw += i.powerDraw * p.LevelMult;
        }

        var sb = new StringBuilder();
        string noun = group.Count == 1 ? info?.name ?? "building" : Plural(info?.name ?? "building");
        string count = $"<color=#E0F0FF><b>{group.Count}</b></color> {noun}";
        if (net != null)
        {
            string hex = ColorUtility.ToHtmlStringRGB(PowerGrid.SupplyColor(net));
            sb.Append($"{count} on <color=#{hex}>Grid {net.index}</color>");
        }
        else sb.Append($"{count} on this world <color=#7C8CA0>· not on a power grid</color>");

        var outs = new List<string>();
        if (energy > 0.005f) outs.Add(Rate(energy, "energy/s"));
        if (metal > 0.005f) outs.Add(Rate(metal, "metal/s"));
        if (water > 0.005f) outs.Add(Rate(water, "water/s"));
        if (research > 0.005f) outs.Add(Rate(research, "research/s"));
        if (growth > 0.0005f) outs.Add(Rate(growth, "pop/s", "0.00"));
        if (food > 0.005f) outs.Add($"<color=#4DFF6E>feeds {food:0}</color>");
        if (storage > 0.5f) outs.Add($"<color=#4DFF6E>+{storage:0} storage</color>");
        if (labor > 0.005f) outs.Add($"<color=#4DFF6E>+{labor:0.0} labor</color>");
        if (info != null && info.powerRange > 0f && info.energyPerSec <= 0f) outs.Add("<color=#9FB4C8>carries power</color>");

        sb.Append("\n<color=#9FB4C8>Output</color>  ");
        sb.Append(outs.Count > 0 ? string.Join("<color=#5A6A7A>  ·  </color>", outs) : "<color=#7C8CA0>none</color>");

        sb.Append("\n<color=#9FB4C8>Upkeep</color>  ");
        if (draw > 0.005f) sb.Append($"<color=#FF6659>-{draw:0.0} energy/s</color>");
        else if (info != null && info.powerDraw > 0f) sb.Append("<color=#FFBF4D>needs power — no grid reaches it</color>");
        else sb.Append("<color=#7C8CA0>none</color>");

        // A short grid health line when there is one, so "why is my output low" has its answer here.
        if (net != null && net.draw > 0.0001f && PowerGrid.SupplyLabel(net) is string lbl && !string.IsNullOrEmpty(lbl))
            sb.Append($"\n<size=11><color=#7C8CA0>Grid {net.index}: {lbl}</color></size>");

        body.text = sb.ToString();
    }

    static string Rate(float v, string unit, string fmt = "0.0") => $"<color=#4DFF6E>+{v.ToString(fmt)} {unit}</color>";

    static string Plural(string n)
    {
        if (n.EndsWith("y") && !n.EndsWith("ay") && !n.EndsWith("ey") && !n.EndsWith("oy")) return n.Substring(0, n.Length - 1) + "ies";
        if (n.EndsWith("s") || n.EndsWith("x") || n.EndsWith("ch") || n.EndsWith("sh")) return n + "es";
        return n + "s";
    }
}
