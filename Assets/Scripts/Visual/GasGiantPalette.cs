using UnityEngine;

// ============================================================================================
// WHAT COLOUR A GAS GIANT IS
//
// Every gas giant in the game was the same tan-orange, because its colour came from TerrainColorMap
// — a table keyed on TERRAIN TYPE, which knows a tile is GasClouds and cannot know which world the
// tile belongs to. So a system with three giants had three identical giants, and the only thing
// distinguishing them was their size.
//
// This adds the missing dimension: a per-BODY tint, rolled from the world's own seed, multiplied over
// the terrain colours the generator already produces. The banding, the storms and the great spot all
// survive untouched — they are structure, and structure is what the terrain generator is for. Only
// the hue changes, which is the thing that actually differed between Jupiter and Neptune.
//
// ---- WHY A MULTIPLIER AND NOT A SECOND COLOUR TABLE -------------------------------------------
//
// Because the giant's surface is not one colour. It is GasClouds and Storm, jittered per tile by
// `shade`, and a second table would have to reproduce all of that and then be kept in step with it.
// A tint preserves every relationship the generator built — the storms stay darker than the bands,
// the jitter stays the same depth — and moves the whole thing round the wheel together.
//
// ---- AND WHY RARITY IS UNEVEN ------------------------------------------------------------------
//
// Ammonia clouds really are the common case, so the familiar tan-orange keeps the lion's share.
// Methane blues are next, deep reds after that, and the violet is deliberately rare enough that
// finding one is worth a second look. A uniform roll across five variants would make the strange one
// as common as the ordinary one, and nothing is remarkable if everything is.
// ============================================================================================
public static class GasGiantPalette
{
    public enum Variant
    {
        Ammonia,     // the classic tan-orange: warm, banded, familiar
        Methane,     // deep blue — an ice giant
        Cobalt,      // a darker, colder blue than Methane
        Ember,       // dark red-brown, a hot giant close to its star
        Violet,      // rare
    }

    /// Which variant this world is. Derived from the seed rather than stored, so it survives a save,
    /// a reload and a sandbox regeneration without occupying a field — and so two worlds in one system
    /// cannot roll the same colour by sharing a random stream.
    public static Variant Of(CelestialBody b)
    {
        if (b == null) return Variant.Ammonia;

        // A stable hash of the body's own identity. The multiply-xor-shift is the same shape as
        // Survey.Hash01 — not shared, because that one takes a cell and this one does not, but the same
        // reasoning: cheap, well-mixed, and identical every time it is asked.
        unchecked
        {
            uint n = (uint)((b.id * 73856093) ^ Mathf.RoundToInt(b.terrainSeed * 131f));
            n = (n ^ (n >> 13)) * 1274126177u;
            float r = ((n ^ (n >> 16)) & 0xFFFFFF) / (float)0x1000000;

            if (r < 0.44f) return Variant.Ammonia;
            if (r < 0.68f) return Variant.Methane;
            if (r < 0.84f) return Variant.Cobalt;
            if (r < 0.95f) return Variant.Ember;
            return Variant.Violet;                    // one in twenty
        }
    }

    /// The multiplier applied to this giant's terrain colours.
    ///
    /// Kept near unit brightness on purpose. A tint that also darkened would make the blue giants read
    /// as unlit rather than as blue, and the banding — which is a brightness relationship — would go
    /// with it. The channels trade against each other instead: what one loses another gains.
    public static Color Tint(CelestialBody b)
    {
        switch (Of(b))
        {
            case Variant.Methane: return new Color(0.42f, 0.78f, 1.35f);
            case Variant.Cobalt:  return new Color(0.34f, 0.58f, 1.15f);
            case Variant.Ember:   return new Color(1.30f, 0.52f, 0.38f);
            case Variant.Violet:  return new Color(1.02f, 0.55f, 1.30f);
            default:              return Color.white;      // Ammonia is what the table already draws
        }
    }

    /// The colour of this giant's air — the atmosphere shell in the system view, and the survey veil
    /// over its map. One source, so the shell and the veil can never disagree about what colour the
    /// world is.
    public static Color Atmosphere(CelestialBody b)
    {
        // The base is what PlanetAppearance used for every giant. Tinting it here rather than writing
        // five more literals means a change to the base carries to all five variants.
        var t = Tint(b);
        var c = new Color(0.92f * t.r, 0.78f * t.g, 0.52f * t.b, 0.34f);
        return new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), c.a);
    }

    /// A short name for the readouts, so a player can say which giant they mean.
    public static string Describe(CelestialBody b)
    {
        switch (Of(b))
        {
            case Variant.Methane: return "methane-blue";
            case Variant.Cobalt:  return "deep cobalt";
            case Variant.Ember:   return "ember-red";
            case Variant.Violet:  return "violet";
            default:              return "ammonia-tan";
        }
    }

    /// Apply the tint to one terrain colour, for a body that may or may not be a giant. Callers hand
    /// every tile through this rather than testing the type themselves, so there is one place that
    /// knows a rocky world is never tinted.
    public static Color Apply(CelestialBody b, Color c)
    {
        if (b == null || b.type != CelestialBodyType.GasGiant) return c;
        var t = Tint(b);
        return new Color(Mathf.Clamp01(c.r * t.r), Mathf.Clamp01(c.g * t.g), Mathf.Clamp01(c.b * t.b), c.a);
    }
}
