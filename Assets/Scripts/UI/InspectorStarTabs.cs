using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// The Inspector's tabs for a STAR (or a binary/ternary cluster, or a black hole).
//
//   Overview — what kind of star it is and what that means to live near
//   Zone     — its habitable band, and which of its worlds sit inside it FOR YOUR SPECIES
//   Worlds   — every body orbiting it, drillable
public partial class InspectorWindow
{
    void CollectStarTabs()
    {
        tabs.Add(new InspectorTab("Overview", BuildStarOverview));
        tabs.Add(new InspectorTab("Zone", BuildStarZone, () => target.star != null && !target.star.isBlackHole));
        tabs.Add(new InspectorTab("Worlds", BuildStarWorlds));
    }

    void BuildStarOverview(Transform p)
    {
        var s = target.star;

        Header(p, "THE STAR");
        var card = Card(p);
        UIFactory.WrapText(card, StarProse(s), UITheme.SmallSize, UITheme.Text);

        var stats = Card(p);
        Stat(stats, "Class", () => s.isBlackHole ? "Black hole" : $"{s.type}-type");
        Stat(stats, "Stars in system", () => s.starCount == 1 ? "1 (single)" : s.starCount == 2 ? "2 (binary)" : $"{s.starCount} (ternary)");
        Stat(stats, "Surface temperature", () => $"{s.temperatureK:F0} K");
        Stat(stats, "Luminosity", () => $"{s.luminosity:F2}× our sun");
        Stat(stats, "Mass", () => $"{s.mass:F2} solar masses");
        Stat(stats, "Habitable zone", () => s.hasHabitableZone ? $"{s.hzInner:F1} – {s.hzOuter:F1}" : "<color=#FF7A6E>none</color>");

        if (target.system != null)
        {
            Header(p, "SYSTEM");
            var sys = Card(p);
            Stat(sys, "Name", () => target.system.name);
            Stat(sys, "Owner", () =>
            {
                string hex = "#" + ColorUtility.ToHtmlStringRGB(FactionManager.OwnerColor(target.system.owner));
                return $"<color={hex}>{FactionManager.OwnerLabel(target.system.owner)}</color>";
            });
            Stat(sys, "Bodies", () => target.system.bodies != null ? target.system.bodies.Count.ToString() : "0");
        }

        Header(p, "ACTIONS");
        UIFactory.Button(p, "Toggle Habitable Zone Rings", () => SystemContext.Zone?.Toggle(), 26);
    }

    // Why this star matters, in plain language rather than a table.
    static string StarProse(StarData s)
    {
        if (s.isBlackHole)
            return "A black hole. No light, no habitable band, no world here will ever be settled by anything that needs a sun — but the physics around it is worth studying, and the gravity well is a landmark.";

        var parts = new List<string>();
        switch (s.type)
        {
            case StarType.O: parts.Add("A blue supergiant: monstrously hot, blindingly bright, and short-lived. It burns too fiercely and dies too young for life to get started around it."); break;
            case StarType.B: parts.Add("A blue-white giant — enormously luminous and violent, with a lifespan too short for a biosphere to form."); break;
            case StarType.A: parts.Add("A hot white star. Bright and fast-burning, with a habitable band pushed far out."); break;
            case StarType.F: parts.Add("A yellow-white star, hotter and brighter than our sun, with a comfortably wide habitable band."); break;
            case StarType.G: parts.Add("A yellow main-sequence star — the familiar, forgiving kind. Stable, long-lived, and kind to worlds in its band."); break;
            case StarType.K: parts.Add("An orange dwarf: cooler and dimmer than our sun but astonishingly long-lived. Its habitable band sits close in, and it will stay put for billions of years."); break;
            case StarType.M: parts.Add("A red dwarf — dim, cool, and effectively immortal. Its habitable band hugs the star, so worlds there are usually tidally locked."); break;
        }
        if (!s.hasHabitableZone) parts.Add("It has no stable habitable zone at all — anything here must be terraformed or built from scratch.");
        else if (s.hzInner < 6f) parts.Add("Its habitable band sits very close in, so worlds in it tend to be tidally locked and need their rotation fixed.");
        if (s.starCount > 1) parts.Add($"It is not alone: {s.starCount} stars orbit each other here, and their combined light is what its worlds actually feel.");
        return string.Join(" ", parts);
    }

    // The habitable band, read through the CURRENT SPECIES — the whole point being that it moves.
    void BuildStarZone(Transform p)
    {
        var s = target.star;
        var sp = SpeciesManager.Current;

        Header(p, "THE HABITABLE BAND");
        if (!s.hasHabitableZone)
        {
            UIFactory.WrapText(p, "<color=#FF7A6E>This star has no stable habitable zone.</color>", UITheme.SmallSize, UITheme.Bad);
            Note(p, "Its worlds can still be mined, studied and — with enough engineering — built on. They just won't ever be naturally livable.");
            return;
        }

        var card = Card(p);
        Stat(card, "Star's base band", () => $"{s.hzInner:F1} – {s.hzOuter:F1}");
        Stat(card, $"Band for {sp.name}", () =>
            Habitability.GetZone(s, sp, out float inner, out float outer)
                ? $"<color=#4DFF6E>{inner:F1} – {outer:F1}</color>"
                : "none");
        Note(card, $"{sp.name} shift the band themselves: they prefer {TempWord(sp.idealTemp)} worlds, so their comfortable band sits " +
                   $"{(sp.idealTemp > 0.55f ? "closer in" : sp.idealTemp < 0.45f ? "further out" : "near the middle")} than the star's raw figures suggest. " +
                   $"Their tolerance ({sp.tolerance:0.00}×) sets how wide it is.");

        Header(p, "WHICH WORLDS SIT IN IT");
        var bodies = SystemBodies();
        if (bodies.Count == 0) { Note(p, "No known bodies orbit this star."); return; }

        foreach (var b in bodies)
        {
            if (b.parentBody != null) continue;   // moons follow their planet
            var cap = b;
            var row = Card(p);
            var t = UIFactory.WrapText(row, "", UITheme.SmallSize, UITheme.Text);
            live.Text(t, () =>
            {
                bool inZone = Habitability.InZone(s, SpeciesManager.Current, cap.distanceFromStar);
                string mark = inZone ? "<color=#4DFF6E>• in band</color>" : "<color=#9FB4C8>· outside</color>";
                return $"{mark}  <b>{cap.name}</b>  <size=10><color=#9FB4C8>{TerraformDiagnosis.Pretty(cap.type)} · " +
                       $"distance {cap.distanceFromStar:F1} · hab <color={Habitability.ScoreColorHex(cap.habitability)}>{cap.habitability:F0}%</color></color></size>";
            });
            UIFactory.Button(row, "Inspect »", () => PlanetUI.Instance?.Show(cap), 22);
        }
    }

    static string TempWord(float idealTemp)
        => idealTemp > 0.7f ? "scorching" : idealTemp > 0.55f ? "warm" : idealTemp < 0.3f ? "frigid" : idealTemp < 0.45f ? "cool" : "temperate";

    void BuildStarWorlds(Transform p)
    {
        Header(p, "WORLDS OF THIS SYSTEM");
        var bodies = SystemBodies();
        if (bodies.Count == 0) { Note(p, "No known bodies orbit this star."); return; }

        foreach (var b in bodies)
        {
            if (b.parentBody != null) continue;
            var cap = b;

            var card = Card(p);
            var t = UIFactory.WrapText(card, "", UITheme.SmallSize, UITheme.Text);
            live.Text(t, () =>
            {
                string owner = cap.owner != null
                    ? $"<color=#{ColorUtility.ToHtmlStringRGB(FactionManager.OwnerColor(cap.owner))}>{FactionManager.OwnerLabel(cap.owner)}</color>"
                    : "<color=#9FB4C8>unclaimed</color>";
                string surveyed = cap.Surveyed ? "" : $"  <color=#FFBF4D>(unsurveyed — {cap.explorationProgress * 100f:F0}%)</color>";
                int moons = cap.moons != null ? cap.moons.Count : 0;
                int ships = cap.units != null ? cap.units.Count : 0;
                return $"<b>{cap.name}</b>{surveyed}\n" +
                       $"<size=10><color=#9FB4C8>{TerraformDiagnosis.Pretty(cap.type)} · {owner} · " +
                       $"hab <color={Habitability.ScoreColorHex(cap.habitability)}>{cap.habitability:F0}%</color> · " +
                       $"{moons} moon(s) · {ships} ship(s)</color></size>";
            });
            UIFactory.Button(card, "Inspect »", () => PlanetUI.Instance?.Show(cap), 22);
        }
    }

    // The bodies belonging to this star: the focused system's own list when we have it, otherwise the
    // current system context.
    List<CelestialBody> SystemBodies()
    {
        if (target.system != null && target.system.bodies != null) return target.system.bodies;
        return SystemContext.Bodies ?? new List<CelestialBody>();
    }
}
