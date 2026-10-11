using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// RACE VFX — what a civilization's ships GLOW like, as data rather than as constants
//
// Every hull now has per-civilization art, and the art alone stopped carrying the identity the
// moment the effects on top of it were shared: a Pyrothian lava-hull with the same white-blue drive
// plume, the same aviation lamps and the same green pulse bolts as a Terran spyplane reads as a
// reskin. So the glow is per race too — drive colour, how much the hull's own emissive burns, whether
// it pulses, whether the ship carries running lights at all, and a tint on the rounds it fires.
//
// ---- WHICH RACE A UNIT IS --------------------------------------------------------------------
//
// Exactly the rule the MESH uses (UnitModelLibrary.CivFolder): the player's ships are the species the
// player chose, every other faction is mapped by its id. Reused rather than restated, because a
// second rule that disagreed even once would put Aquarii plumes on a Pyrothian hull, and that is a
// worse look than no per-race effects at all.
//
// ---- DEFAULTS IN CODE, OVERRIDES IN A FILE ---------------------------------------------------
//
// The compiled defaults are the design; they are what ships. On top of them, an optional
// Resources/SpaceAssets/race-vfx.json can override any field of any race, and F10 (the key that
// already reloads ship-meshes.txt) re-reads it and rebuilds the fleet — so tuning a plume colour is
// edit, F10, look, and not a recompile. The file is a JSON object keyed by civ folder name, and only
// the fields it names change:
//
//     {
//       "Pyrothian": { "emissiveMultiplier": 3.5, "thrusterTail": { "r": 1, "g": 0.3, "b": 0, "a": 1 } },
//       "Terran":    { "navLights": false }
//     }
//
// A missing file is the normal state. A broken one is reported once and the defaults stand.
// ============================================================================================

/// One civilization's effects. A class rather than a struct so JsonUtility.FromJsonOverwrite can write
/// a partial override INTO the defaults in place — see RaceVfx.Reload.
///
/// The field initializers are a neutral, Terran-like baseline. They are not what any race uses (each
/// race's defaults are set in RaceVfx.Defaults); they are what a field falls back to if the JSON
/// loader ever rebuilds a nested object from scratch instead of merging into it, so the worst case of
/// a partial override is "that field went plain", never "that field went black".
[System.Serializable]
public class RaceVfxProfile
{
    // ---- drive plume ----
    /// The white-hot nozzle colour, at the root of the plume.
    public Color thrusterCore = new Color(0.85f, 0.95f, 1f);
    /// What the plume cools to at its tail.
    public Color thrusterTail = new Color(0.25f, 0.55f, 1f);
    /// 0..1 — how deep the plume's beat-locked flicker dips. 0.1 is a steady drive; 0.25 is a ragged one.
    public float thrusterFlicker = 0.1f;

    // ---- the hull's own emissive (the glow baked into the art) ----
    /// Multiplier on the material's emissive. 1 is the art as imported, and never blooms: bloom's
    /// threshold is 1.05 (PostFxController), and the imported emissive factor is exactly 1.
    public float emissiveMultiplier = 1f;
    /// Pulse period in FLEET BEATS (FleetClock), snapped to the legal periods so the hull swells in
    /// time with the lamps and plumes rather than on a cycle of its own.
    public float emissivePulseBeats = 4f;
    /// 0..1 — how far the pulse dips below the multiplier. 0 is no pulse at all.
    public float emissivePulseDepth = 0f;
    /// Extra emissive at full throttle, as a fraction (0.5 = 50% hotter flat out). A hull whose glow is
    /// its reactor should burn brighter when the reactor is working.
    public float emissiveThrottleBoost = 0f;

    // ---- running lights ----
    public bool navLights = true;
    public Color navPort = new Color(1f, 0.25f, 0.2f);
    public Color navStarboard = new Color(0.3f, 1f, 0.35f);
    public Color navStrobe = new Color(1f, 1f, 0.95f);
    /// The fastest strobe's period, in beats. The tail strobe runs at twice this and the bow beacon at
    /// six times, each snapped to FleetClock.Periods so the whole rig stays in rhythm.
    public float navBlinkBeats = 0.5f;

    // ---- gunfire ----
    /// The race's signature shot colour, blended into the WEAPON CLASS colour rather than replacing it.
    public Color weaponTint = new Color(0.75f, 0.88f, 1f);
    /// 0..1 — how much of the shot colour is the race's. Kept under half by default: the class colour
    /// is how a player tells a railgun from a plasma bolt (see Weaponry), and that matters more than
    /// whose railgun it is.
    public float weaponTintAmount = 0.4f;

    // ---- shields ----
    /// Not drawn by anything yet. Here so the shield effect, when it lands, takes its colour from the
    /// same table as everything else instead of growing a sixth per-race switch somewhere.
    public Color shieldColour = new Color(0.45f, 0.7f, 1f);

    /// Does the hull's emissive need touching at all? False for a race that leaves the art exactly as
    /// imported, which lets ShipLights skip the property block entirely — and a renderer with no block
    /// stays eligible for the SRP batcher.
    public bool AffectsHullGlow =>
        Mathf.Abs(emissiveMultiplier - 1f) > 0.001f || !HullGlowIsStatic;

    /// True when the glow never changes after it is set — no pulse and no throttle term — so it can be
    /// written once at build time and left alone.
    public bool HullGlowIsStatic => emissivePulseDepth <= 0.001f && emissiveThrottleBoost <= 0.001f;
}

public static class RaceVfx
{
    /// Where the optional override file lives: Resources/SpaceAssets/race-vfx.json.
    public const string OverridePath = "SpaceAssets/race-vfx";

    /// The JSON's shape. One field per civ folder name, matching UnitModelLibrary's folders exactly —
    /// JsonUtility maps object keys to field names, so these names ARE the file format.
    [System.Serializable]
    class Table
    {
        public RaceVfxProfile Terran, Aquarii, Pyrothian, Cryithn, Sylvan;
    }

    static Dictionary<string, RaceVfxProfile> byCiv;

    /// Used for anything with no civilization to speak of. Never null, so no caller has to check.
    static readonly RaceVfxProfile Neutral = new RaceVfxProfile();

    /// This unit's profile, by the same civ rule its mesh was chosen with.
    public static RaceVfxProfile For(Unit u) => u == null ? Neutral : ForCiv(UnitModelLibrary.CivFolder(u));

    /// A profile by civ folder name ("Terran", "Aquarii", ...). Unknown names get the neutral one.
    public static RaceVfxProfile ForCiv(string civ)
    {
        if (byCiv == null) Reload();
        return civ != null && byCiv.TryGetValue(civ, out var p) && p != null ? p : Neutral;
    }

    /// The colour a round should be drawn in: the weapon class's colour, leaned toward the shooter's race.
    ///
    /// A shooter with NO owner keeps the plain class colour. The mesh rule files an ownerless unit
    /// under the player's species because a mesh has to come from somewhere; a shot does not, and the
    /// ancient things that fly ownerless (CombatManager: "ancient things hate everything that moves")
    /// firing in the player's own race colour would read as friendly fire.
    public static Color WeaponColour(Unit shooter, Color classColour)
    {
        if (shooter == null || shooter.owner == null) return classColour;
        var p = For(shooter);
        var c = Color.Lerp(classColour, p.weaponTint, Mathf.Clamp01(p.weaponTintAmount));
        c.a = classColour.a;
        return c;
    }

    /// Snap a period in beats to the nearest legal FleetClock period — the guarantee that keeps every
    /// light in time, which a hand-edited JSON value has no way of knowing about.
    public static float SnapBeats(float beats)
    {
        var legal = FleetClock.Periods;
        float best = legal[0];
        for (int i = 1; i < legal.Length; i++)
            if (Mathf.Abs(legal[i] - beats) < Mathf.Abs(best - beats)) best = legal[i];
        return best;
    }

    /// Rebuild the table from the compiled defaults, then lay the override file (if any) over it.
    /// Called lazily on first use and by the F10 reload in UnitModelRenderer.
    public static void Reload()
    {
        var table = Defaults();

        var asset = Resources.Load<TextAsset>(OverridePath);
        if (asset != null)
        {
            try
            {
                // OVERWRITE, not FromJson: FromJson would build a fresh table in which every field the
                // file does not mention is zero, and a race with a black drive plume and a zero
                // emissive multiplier is what a one-line tweak would cost. Overwrite writes only what
                // the file names, on top of the defaults.
                JsonUtility.FromJsonOverwrite(asset.text, table);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RaceVfx] Could not read Resources/{OverridePath}.json ({e.Message}). " +
                                 "Using the built-in race effects.");
                table = Defaults();
            }
            // Unloaded so the NEXT reload reads the file as it is now, not as Resources cached it.
            Resources.UnloadAsset(asset);
        }

        byCiv = new Dictionary<string, RaceVfxProfile>(System.StringComparer.OrdinalIgnoreCase)
        {
            { "Terran", table.Terran ?? Neutral },
            { "Aquarii", table.Aquarii ?? Neutral },
            { "Pyrothian", table.Pyrothian ?? Neutral },
            { "Cryithn", table.Cryithn ?? Neutral },
            { "Sylvan", table.Sylvan ?? Neutral },
        };
    }

    // ============================================================================================
    // THE DEFAULTS — the design, in one place
    //
    // Emissive multipliers are chosen against bloom's 1.05 threshold: at 1 the art's glow never
    // blooms, so a race whose identity IS its glow has to clear it. Pyrothian lava at ~3 blooms
    // outright; Aquarii bioluminescence at ~1.8 just catches; Terrans are lit by their lamps, not their
    // hull, and stay at 1.
    //
    // Pulse periods are in beats and on the FleetClock grid, so a Pyrothian throb and its own drive
    // flicker are counting the same bar.
    // ============================================================================================
    static Table Defaults() => new Table
    {
        // Clean engineering: blue-white drive, aviation lamps, no hull glow to speak of.
        Terran = new RaceVfxProfile
        {
            thrusterCore = new Color(0.85f, 0.95f, 1f),
            thrusterTail = new Color(0.25f, 0.55f, 1f),
            thrusterFlicker = 0.10f,
            emissiveMultiplier = 1f, emissivePulseDepth = 0f,
            navLights = true,
            navPort = new Color(1f, 0.25f, 0.2f),
            navStarboard = new Color(0.3f, 1f, 0.35f),
            navStrobe = new Color(1f, 1f, 0.95f),
            navBlinkBeats = 0.5f,
            weaponTint = new Color(0.78f, 0.90f, 1f),      // crisp blue-white bolts
            weaponTintAmount = 0.40f,
            shieldColour = new Color(0.45f, 0.70f, 1f),
        },

        // Living hulls: a teal drive, and a slow bioluminescent breath. No lamps — a shrimp with a
        // strobe on its tail reads as a toy.
        Aquarii = new RaceVfxProfile
        {
            thrusterCore = new Color(0.80f, 1f, 0.95f),
            thrusterTail = new Color(0.10f, 0.75f, 0.70f),
            thrusterFlicker = 0.06f,
            emissiveMultiplier = 1.8f, emissivePulseBeats = 4f, emissivePulseDepth = 0.30f,
            emissiveThrottleBoost = 0f,
            navLights = false,
            weaponTint = new Color(0.20f, 0.95f, 0.80f),    // teal pulse
            weaponTintAmount = 0.40f,
            shieldColour = new Color(0.25f, 0.90f, 0.85f),
        },

        // Lava: an orange, ragged drive and a hull that throbs slowly and burns hotter under power.
        Pyrothian = new RaceVfxProfile
        {
            thrusterCore = new Color(1f, 0.90f, 0.60f),
            thrusterTail = new Color(1f, 0.35f, 0.05f),
            thrusterFlicker = 0.22f,
            emissiveMultiplier = 3f, emissivePulseBeats = 2f, emissivePulseDepth = 0.35f,
            emissiveThrottleBoost = 0.5f,
            navLights = false,
            weaponTint = new Color(1f, 0.50f, 0.12f),       // orange plasma
            weaponTintAmount = 0.40f,
            shieldColour = new Color(1f, 0.45f, 0.15f),
        },

        // Crystal: a pale violet-blue drive, a faint shimmer, and lamps tinted to match the hull.
        Cryithn = new RaceVfxProfile
        {
            thrusterCore = new Color(0.92f, 0.90f, 1f),
            thrusterTail = new Color(0.55f, 0.50f, 1f),
            thrusterFlicker = 0.05f,
            emissiveMultiplier = 1.4f, emissivePulseBeats = 3f, emissivePulseDepth = 0.12f,
            navLights = true,
            navPort = new Color(1f, 0.40f, 0.55f),
            navStarboard = new Color(0.45f, 1f, 0.80f),
            navStrobe = new Color(0.85f, 0.85f, 1f),
            navBlinkBeats = 1f,
            weaponTint = new Color(0.72f, 0.70f, 1f),       // pale violet-blue
            weaponTintAmount = 0.40f,
            shieldColour = new Color(0.70f, 0.65f, 1f),
        },

        // Grown: a green drive and a gentle glow, breathing like leaves in light. No lamps.
        Sylvan = new RaceVfxProfile
        {
            thrusterCore = new Color(0.90f, 1f, 0.80f),
            thrusterTail = new Color(0.35f, 0.90f, 0.30f),
            thrusterFlicker = 0.12f,
            emissiveMultiplier = 1.6f, emissivePulseBeats = 4f, emissivePulseDepth = 0.20f,
            navLights = false,
            weaponTint = new Color(0.40f, 1f, 0.35f),       // green
            weaponTintAmount = 0.40f,
            shieldColour = new Color(0.45f, 1f, 0.50f),
        },
    };
}

// ============================================================================================
// HULL MATERIALS — one property-name lookup for two shader families, and one material per look
//
// ---- TWO NAMING SCHEMES ----------------------------------------------------------------------
//
// The hull code was written against URP/Lit's names (`_BaseColor`, `_BaseMap`, `_EmissionColor`) and
// the legacy FBX hulls really do use URP/Lit. The generated fleet does not: its .glb files come in
// through glTFast, whose URP shader graphs name the same things after the glTF spec — `baseColorFactor`,
// `baseColorTexture`, `emissiveFactor`. A SetColor on a property a shader does not have is silently
// ignored, so the faction tint and the player's livery did nothing at all on the generated ships and
// nothing said so. Every lookup here tries the URP name first (so the FBX hulls behave exactly as they
// did) and falls back to the glTF one.
//
// ---- ONE MATERIAL PER LOOK, NOT PER SHIP -----------------------------------------------------
//
// Tinting used to go through `renderer.materials`, which clones every material on every renderer of
// every ship — forty scouts, forty identical copies, none ever destroyed. But a hull's look depends on
// only three things: which source material, which owner tint, and (for the player) which livery. So
// the copy is made once per combination and SHARED by every ship that matches. Per-ship variation
// that does exist (the emissive pulse) goes through a MaterialPropertyBlock in ShipLights instead,
// which needs no copy at all.
// ============================================================================================
public static class HullMaterials
{
    static readonly int BaseColorUrp = Shader.PropertyToID("_BaseColor");
    static readonly int ColorLegacy = Shader.PropertyToID("_Color");
    static readonly int BaseColorGltf = Shader.PropertyToID("baseColorFactor");

    static readonly int BaseMapUrp = Shader.PropertyToID("_BaseMap");
    static readonly int MainTexLegacy = Shader.PropertyToID("_MainTex");
    static readonly int BaseMapGltf = Shader.PropertyToID("baseColorTexture");

    static readonly int EmissionUrp = Shader.PropertyToID("_EmissionColor");
    static readonly int EmissiveGltf = Shader.PropertyToID("emissiveFactor");

    /// The base-colour property this material actually has, or -1.
    public static int BaseColorId(Material m)
    {
        if (m == null) return -1;
        if (m.HasProperty(BaseColorUrp)) return BaseColorUrp;
        if (m.HasProperty(ColorLegacy)) return ColorLegacy;
        if (m.HasProperty(BaseColorGltf)) return BaseColorGltf;
        return -1;
    }

    /// The base-colour TEXTURE property this material actually has, or -1.
    public static int BaseMapId(Material m)
    {
        if (m == null) return -1;
        if (m.HasProperty(BaseMapUrp)) return BaseMapUrp;
        if (m.HasProperty(MainTexLegacy)) return MainTexLegacy;
        if (m.HasProperty(BaseMapGltf)) return BaseMapGltf;
        return -1;
    }

    /// The emissive colour/factor property this material actually has, or -1.
    public static int EmissiveId(Material m)
    {
        if (m == null) return -1;
        if (m.HasProperty(EmissionUrp)) return EmissionUrp;
        if (m.HasProperty(EmissiveGltf)) return EmissiveGltf;
        return -1;
    }

    /// Is this the URP/Lit emission property, which only draws while the `_EMISSION` keyword is on?
    /// glTFast's graphs have no such keyword; their emissive is always live.
    public static bool NeedsEmissionKeyword(int propertyId) => propertyId == EmissionUrp;

    struct Key : System.IEquatable<Key>
    {
        public int source; public Color tint; public int civ; public Color a, b;
        public bool Equals(Key o) => source == o.source && tint == o.tint && civ == o.civ && a == o.a && b == o.b;
        public override bool Equals(object o) => o is Key k && Equals(k);
        public override int GetHashCode() => (source * 397) ^ tint.GetHashCode() ^ (civ * 31) ^ a.GetHashCode() ^ b.GetHashCode();
    }

    static readonly Dictionary<Key, Material> cache = new Dictionary<Key, Material>();

    /// Give every MeshRenderer under `model` the shared, tinted (and, for the player, liveried) copy
    /// of its materials. `liveryCiv` is the species index to repaint for, or -1 for no livery.
    ///
    /// Call BEFORE ShipLOD.Attach, for the same reason the old per-ship pass ran first: the LOD levels
    /// borrow the base's materials, so recolouring the base once recolours all three.
    public static void Prepare(GameObject model, Color ownerTint, int liveryCiv)
    {
        if (model == null) return;
        // Same 30% lean toward the owner colour the per-ship pass always used, so allegiance reads at a
        // glance without drowning the art.
        Color tint = Color.Lerp(Color.white, ownerTint, 0.3f);
        bool livery = liveryCiv >= 0 && CivLivery.Chosen;

        foreach (var mr in model.GetComponentsInChildren<MeshRenderer>())
        {
            var mats = mr.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null) mats[i] = Shared(mats[i], tint, livery ? liveryCiv : -1);
            mr.sharedMaterials = mats;
        }
    }

    static Material Shared(Material src, Color tint, int liveryCiv)
    {
        var key = new Key
        {
            source = src.GetInstanceID(), tint = tint, civ = liveryCiv,
            // The colour pair is part of the key so a livery change produces new materials rather than
            // handing a re-chosen scheme the old repaint.
            a = liveryCiv >= 0 ? CivLivery.Primary : default,
            b = liveryCiv >= 0 ? CivLivery.Secondary : default,
        };
        if (cache.TryGetValue(key, out var hit) && hit != null) return hit;

        var m = new Material(src) { name = src.name + " (fleet)" };
        if (liveryCiv >= 0) CivLivery.ApplyTo(m, liveryCiv);

        // MULTIPLIED into the authored colour rather than written over it. The URP path used to SET
        // `_BaseColor`, which was harmless while every FBX hull was authored white; a glTF base colour
        // factor is whatever the generator wrote, and overwriting it would discard part of the art.
        int id = BaseColorId(m);
        // Alpha is left alone: a tint is a hue, and an owner colour with any transparency in it would
        // otherwise turn an opaque hull see-through.
        if (id >= 0)
        {
            var c = m.GetColor(id);
            m.SetColor(id, new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, c.a));
        }

        cache[key] = m;
        return m;
    }
}
