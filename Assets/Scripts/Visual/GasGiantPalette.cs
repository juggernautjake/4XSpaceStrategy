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

            // VIOLET IS ONE IN A HUNDRED, not one in twenty.
            //
            // "Lets also make purple/pink gas giants much much more rare to spawn." It was already
            // described here as "deliberately rare" at 5%, and 5% is not rare when a galaxy holds a
            // dozen systems with a giant or two each: a violet giant turned up in most games, often
            // more than once, and the thing it was supposed to be — worth a second look — cannot
            // survive that. At 1% it is a genuine find.
            //
            // The 4% that came off Violet goes to Ammonia and Ember rather than being spread evenly:
            // ammonia clouds are the physical default and should dominate, and Ember is the other
            // warm variant, so the palette as a whole stays warm-leaning with the blues as the
            // interesting minority.
            if (r < 0.50f) return Variant.Ammonia;
            if (r < 0.74f) return Variant.Methane;
            if (r < 0.88f) return Variant.Cobalt;
            if (r < 0.99f) return Variant.Ember;
            return Variant.Violet;                    // one in a hundred
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

    // ============================================================================================
    // THE TWO TILES THAT ARE THE GIANT — GasClouds AND Storm, PER VARIANT
    //
    // "I want to have different variants of the grid colors of GasClouds and Storm to represent the
    // grid surface of different color gas giants. Blue Gas Giants for example would have blue variants
    // of GasClouds and Storm Grids, same for Gas Giants with Red coloring, and even purple coloring."
    //
    // The tint alone could not do this, and the header above says why without noticing the consequence.
    // A single multiplier moves the WHOLE surface round the wheel together, so a methane giant was the
    // tan palette dyed blue — its storms were the tan storm colour dyed blue, and therefore the same
    // hue as its clouds. The one relationship that actually distinguishes these worlds — that a storm
    // is a DIFFERENT colour from the deck it sits in, not merely a darker patch of it — was the one
    // thing the multiplier could not express.
    //
    // So the two tiles that ARE the giant get explicit colours per variant, and everything else on a
    // giant (there is nothing else, but the path stays general) keeps the multiplier. Two tables of
    // five rather than one table of five, which is a small price for the storms reading as storms.
    //
    // The pairs are chosen so every variant keeps the same RELATIONSHIP: the storm is warmer and
    // darker than its deck on the warm worlds, cooler and darker on the cold ones — a storm is a hole
    // you see down into, on every one of them.
    // ============================================================================================

    /// The banded cloud deck, per variant.
    public static Color CloudColor(Variant v)
    {
        switch (v)
        {
            case Variant.Methane: return new Color(0.42f, 0.62f, 0.86f);   // pale ice-giant blue
            case Variant.Cobalt:  return new Color(0.28f, 0.42f, 0.74f);   // darker, colder
            case Variant.Ember:   return new Color(0.72f, 0.42f, 0.30f);   // hot, rust-brown
            case Variant.Violet:  return new Color(0.62f, 0.46f, 0.78f);
            default:              return new Color(0.80f, 0.72f, 0.52f);   // ammonia tan, the table's own
        }
    }

    /// The storm bands and the great spots, per variant. Always distinct in HUE from the deck above,
    /// never merely darker — see the header.
    public static Color StormColor(Variant v)
    {
        switch (v)
        {
            case Variant.Methane: return new Color(0.30f, 0.40f, 0.66f);   // deep blue-slate
            case Variant.Cobalt:  return new Color(0.16f, 0.24f, 0.52f);
            case Variant.Ember:   return new Color(0.62f, 0.20f, 0.14f);   // dark blood-red
            case Variant.Violet:  return new Color(0.48f, 0.24f, 0.60f);
            default:              return new Color(0.78f, 0.38f, 0.24f);   // the reddish orange asked for
        }
    }

    /// Apply the tint to one terrain colour, for a body that may or may not be a giant. Callers hand
    /// every tile through this rather than testing the type themselves, so there is one place that
    /// knows a rocky world is never tinted.
    ///
    /// The terrain TYPE is needed now, because the two gas tiles are looked up rather than multiplied.
    /// The old two-argument form is kept below for the callers that genuinely have no type in hand.
    public static Color Apply(CelestialBody b, Color c, TerrainType type)
    {
        if (b == null || b.type != CelestialBodyType.GasGiant) return c;

        var v = Of(b);
        if (type == TerrainType.GasClouds) { var g = CloudColor(v); return new Color(g.r, g.g, g.b, c.a); }
        if (type == TerrainType.Storm)     { var g = StormColor(v); return new Color(g.r, g.g, g.b, c.a); }

        var t = Tint(b);
        return new Color(Mathf.Clamp01(c.r * t.r), Mathf.Clamp01(c.g * t.g), Mathf.Clamp01(c.b * t.b), c.a);
    }

    /// Tint-only, for a caller with no terrain type. Anything a giant is actually made of should use
    /// the overload above — this one cannot know to look a gas tile up.
    public static Color Apply(CelestialBody b, Color c)
    {
        if (b == null || b.type != CelestialBodyType.GasGiant) return c;
        var t = Tint(b);
        return new Color(Mathf.Clamp01(c.r * t.r), Mathf.Clamp01(c.g * t.g), Mathf.Clamp01(c.b * t.b), c.a);
    }
}
