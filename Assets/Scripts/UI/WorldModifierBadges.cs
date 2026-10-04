using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// ============================================================================================
// THE WORLD MODIFIER BADGES — a row of small framed icons that says "this world is unusual"
//
// Each badge is a 16x16 icon in a square box whose border is the colour of the index the modifier
// bends (WorldModifiers.BorderColor), so even before the player knows WHAT it is, the colour hints at
// what kind of thing it is: orange is minerals, purple is weather, and so on.
//
//   not surveyed      nothing — the row is empty
//   level-1 survey    a "?" in the coloured frame. Hover: something about this world is unique.
//   level-2 survey    the real icon. Hover: the modifier's name and what it does.
//
// SELF-UPDATING. It watches the survey level and the modifier set itself and rebuilds only when either
// changes, so a host panel can drop it in once and never think about it again — the "?" turns into the
// real icon the moment the level-2 survey finishes, with the panel open.
//
// Used in two places: across the top of the Inspector Overview's planet sphere, and in the Planet View
// window under the bottom-right corner of the surface map, level with its tabs.
// ============================================================================================
public class WorldModifierBadges : MonoBehaviour
{
    CelestialBody body;
    float size = 22f;
    int lastStage = -1, lastMask = -1;
    readonly List<WorldModifier> mods = new List<WorldModifier>();

    /// A badge row under `parent`. The caller positions the returned RectTransform; the row sizes itself
    /// to its badges.
    public static WorldModifierBadges Attach(Transform parent, CelestialBody b, float badgeSize)
    {
        var go = UIFactory.NewUI(parent, "ModifierBadges");
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 3;
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = false;
        h.childAlignment = TextAnchor.MiddleLeft;
        var fit = go.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var badges = go.AddComponent<WorldModifierBadges>();
        badges.size = badgeSize;
        badges.body = b;
        badges.Refresh(true);
        return badges;
    }

    /// Point the row at a different world (the Planet View reuses one row across selections).
    public void SetBody(CelestialBody b)
    {
        if (b == body) return;
        body = b;
        Refresh(true);
    }

    void Update() => Refresh(false);

    void Refresh(bool force)
    {
        int stage = body == null ? 0 : Survey.LevelOf(body);
        // A world the player owns shows every index in full (Survey.RevealOf), so its modifiers are no
        // secret either.
        if (body != null && body.owner == FactionManager.Player && body.Surveyed) stage = 2;
        int mask = 0;
        if (body != null)
            foreach (var m in WorldModifiers.All)
                if (WorldModifiers.Has(body, m)) mask |= 1 << (int)m;

        if (!force && stage == lastStage && mask == lastMask) return;
        lastStage = stage; lastMask = mask;
        Rebuild(stage);
    }

    void Rebuild(int stage)
    {
        // Deactivated as well as destroyed: Destroy is deferred to the end of the frame, and the layout
        // pass in between would still count the old badges and shove the new ones sideways.
        bool hadBadges = transform.childCount > 0;
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }
        // A badge removed from under the cursor never gets its pointer-exit, so its tooltip would stay up.
        if (hadBadges) TooltipManager.Instance.Hide();
        if (body == null || stage < 1) return;

        WorldModifiers.List(body, mods);
        foreach (var m in mods) Badge(m, stage >= 2);
    }

    void Badge(WorldModifier m, bool known)
    {
        // The border IS the box: a panel in the index colour, with the dark plate inset over it. Raycast
        // ON, because the hover tooltip lives on it.
        var frame = UIFactory.Panel(transform, "Badge", WorldModifiers.BorderColor(m));
        var le = frame.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = size; le.preferredHeight = size;
        le.minWidth = size; le.minHeight = size;

        var plate = UIFactory.Panel(frame.transform, "Plate", new Color(0.03f, 0.05f, 0.08f, 0.92f));
        plate.raycastTarget = false;
        UIFactory.Stretch(plate.rectTransform);
        plate.rectTransform.offsetMin = new Vector2(2f, 2f);
        plate.rectTransform.offsetMax = new Vector2(-2f, -2f);

        var iconGo = UIFactory.NewUI(frame.transform, "Icon");
        var icon = iconGo.AddComponent<RawImage>();
        icon.raycastTarget = false;
        icon.texture = known ? WorldModifiers.Icon(m) : WorldModifiers.UnknownIcon();
        // Pixel art: never smoothed.
        if (icon.texture != null) icon.texture.filterMode = FilterMode.Point;
        var irt = icon.rectTransform;
        UIFactory.Stretch(irt);
        float pad = Mathf.Max(3f, (size - 16f) * 0.5f);
        irt.offsetMin = new Vector2(pad, pad);
        irt.offsetMax = new Vector2(-pad, -pad);

        string hex = ColorUtility.ToHtmlStringRGB(WorldModifiers.BorderColor(m));
        string tip = known
            ? $"<color=#{hex}><b>{WorldModifiers.Name(m)}</b></color>\n{WorldModifiers.Describe(m)}\n" +
              $"<color=#8FA3B5>Affects the {SurfaceIndex.Name(WorldModifiers.IndexOf(m))}.</color>"
            : $"<color=#{hex}><b>Something unusual</b></color>\nThere is something unique about this world. " +
              $"<color=#8FA3B5>A level-2 survey will reveal what it is — the frame's colour hints at which " +
              $"index it touches.</color>";
        UIFactory.Tooltip(frame.gameObject, tip);
    }
}
