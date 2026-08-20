using UnityEngine;
using UnityEngine.UI;

// ============================================================================================
// CHOOSING WHAT YOUR EMPIRE LOOKS LIKE
//
// Two colours and a mark, chosen in the same place the race is chosen, because they are one decision.
// Splitting them across screens lets a player build a crest they never see against the livery it will
// be worn in, and pick a livery they never see on the crest.
//
// A LIVE CREST SITS ABOVE THE SWATCHES and updates as they are pressed. That is the whole design of
// this panel: colour pickers that only show you two squares are asking you to imagine the result, and
// the result is the thing being chosen. Every symbol swatch is drawn in the CURRENT colours too, so
// the grid is not ten grey icons — it is ten previews of what you would actually get.
//
// ---- WHY A FIXED PALETTE RATHER THAN A COLOUR WHEEL ----------------------------------------
//
// Every colour offered is saturated enough to survive being written into a ship texture at the
// original pixel's brightness (see CivLivery). A wheel lets a player pick near-black or near-white,
// at which point the livery vanishes into the hull and the honest report is "this feature is broken".
// A dozen colours far enough apart that two empires never look alike is the better trade.
// ============================================================================================
public static class CivIdentityPanel
{
    const float Swatch = 30f, SymbolCell = 46f;

    /// Build the whole identity section — crest, primary row, secondary row, symbol grid — into
    /// `col`. `onChanged` fires after any change so the caller can refresh its own summary line.
    public static void Build(Transform col, System.Action onChanged = null)
    {
        UIFactory.Label(col, "EMPIRE COLOURS & MARK", UITheme.SmallSize, UITheme.Accent, 16);

        // ---- the live crest -------------------------------------------------------------------
        var crestRow = Row(col, 84f);
        var crestGo = UIFactory.NewUI(crestRow, "Crest");
        var crest = crestGo.AddComponent<RawImage>();
        var crt = crestGo.GetComponent<RectTransform>();
        crt.sizeDelta = new Vector2(76, 76);
        UIFactory.Tooltip(crestGo, "Your empire's mark, in your colours. It flies on your ships and " +
                                   "flags the worlds you hold.");

        var note = UIFactory.WrapText(crestRow, "", UITheme.SmallSize, UITheme.Text);
        var nrt = note.GetComponent<RectTransform>();
        nrt.sizeDelta = new Vector2(300, 76);

        // Declared before the swatch rows so their handlers can call it.
        System.Action refresh = null;

        // ---- primary ---------------------------------------------------------------------------
        UIFactory.Label(col, "Primary — the broad livery panels", UITheme.SmallSize, UITheme.Text, 15);
        var primaryRow = Grid(col, Swatch, 2);
        foreach (var (name, color) in CivLivery.Palette)
        {
            var c = color; var n = name;
            var b = Chip(primaryRow, color, () =>
            {
                CivLivery.Set(c, CivLivery.Secondary);
                CivEmblem.InvalidateColours();
                refresh?.Invoke(); onChanged?.Invoke();
            });
            UIFactory.Tooltip(b, $"{n} — your primary colour. Painted onto the broad livery panels of " +
                                 "every ship you build, and the main field of your mark.");
        }

        // ---- secondary -------------------------------------------------------------------------
        UIFactory.Label(col, "Secondary — trim, seams and running lights", UITheme.SmallSize, UITheme.Text, 15);
        var secondaryRow = Grid(col, Swatch, 2);
        foreach (var (name, color) in CivLivery.Palette)
        {
            var c = color; var n = name;
            var b = Chip(secondaryRow, color, () =>
            {
                CivLivery.Set(CivLivery.Primary, c);
                CivEmblem.InvalidateColours();
                refresh?.Invoke(); onChanged?.Invoke();
            });
            UIFactory.Tooltip(b, $"{n} — your secondary colour. Picks out trim, seams and docking " +
                                 "lights, and the detail inside your mark.");
        }

        // ---- the symbols -----------------------------------------------------------------------
        UIFactory.Label(col, "Mark", UITheme.SmallSize, UITheme.Text, 15);
        var symbolRow = Grid(col, SymbolCell, 5);
        var symbolImages = new RawImage[CivEmblem.Symbols.Length];

        for (int i = 0; i < CivEmblem.Symbols.Length; i++)
        {
            int idx = i;
            string name = CivEmblem.Symbols[i];

            var cell = UIFactory.NewUI(symbolRow, "Symbol_" + name);
            var img = cell.AddComponent<RawImage>();
            symbolImages[i] = img;

            var btn = cell.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                CivEmblem.SetSymbol(idx);
                refresh?.Invoke(); onChanged?.Invoke();
            });

            UIFactory.Tooltip(cell, $"{name} — worn by your ships and shown on the worlds you hold.");
        }

        // ---- keeping it all in step -------------------------------------------------------------
        refresh = () =>
        {
            var current = CivEmblem.Current;
            if (crest != null) crest.texture = current;

            for (int i = 0; i < symbolImages.Length; i++)
            {
                if (symbolImages[i] == null) continue;
                symbolImages[i].texture = CivEmblem.Preview(i, CivLivery.Primary, CivLivery.Secondary);
                // The chosen one at full strength, the rest dimmed — a selection has to be visible
                // without a border, because a border around a transparent mark reads as part of it.
                symbolImages[i].color = i == CivEmblem.SymbolIndex ? Color.white : new Color(1, 1, 1, 0.38f);
            }

            if (note != null)
                note.text = $"<b>{CivEmblem.SymbolName}</b>\n<size=10><color=#9FB4C8>" +
                            "Shown on your ships and on every world you hold. Both colours are painted " +
                            "onto the livery panels your fleet was designed with — the hull keeps its " +
                            "own material and weathering.</color></size>";
        };

        // The player has opened the screen, so from here on they own the choice: seed it from the
        // defaults rather than leaving CivLivery.Chosen false, or the swatches would sit there showing
        // colours that are not actually being applied to anything.
        CivLivery.Set(CivLivery.Primary, CivLivery.Secondary);
        refresh();
    }

    // ---- small builders --------------------------------------------------------------------------

    static Transform Row(Transform parent, float height)
    {
        var go = UIFactory.NewUI(parent, "Row");
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 10; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
        h.childAlignment = TextAnchor.MiddleLeft;
        UIFactory.AddLayout(go, height);
        return go.transform;
    }

    static Transform Grid(Transform parent, float cell, int rows)
    {
        var go = UIFactory.NewUI(parent, "Grid");
        var g = go.AddComponent<GridLayoutGroup>();
        g.cellSize = new Vector2(cell, cell);
        g.spacing = new Vector2(6, 6);
        g.childAlignment = TextAnchor.UpperLeft;
        UIFactory.AddLayout(go, rows * (cell + 6) + 4);
        return go.transform;
    }

    static GameObject Chip(Transform parent, Color color, System.Action onClick)
    {
        var go = UIFactory.NewUI(parent, "Chip");
        var img = go.AddComponent<Image>();
        img.color = color;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick?.Invoke());
        return go;
    }
}
