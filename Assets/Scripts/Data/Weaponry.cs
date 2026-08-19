using UnityEngine;

// ============================================================================================
// WHAT A SHIP SHOOTS WITH
//
// `UnitInfo.attack` was a single number and stayed one — it is still what decides how hard a hull
// hits, and every balance pass that has ever been made against it still means what it meant. What was
// missing was everything ELSE about a shot: how fast it flies, whether it chases, how far it reaches,
// how often it goes off, and what it looks and sounds like. A game where every weapon is one number
// has no reason to draw anything, and a game that draws one bolt for every weapon has no reason to
// have more than one.
//
// So a hull's `attack` is its BUDGET and its weapon class is how it spends it. A Fighter's twin pulse
// lasers and a Dreadnought's spinal railgun can both be "attack 40" and play completely differently:
// the fighter lands eight small hits a second that a point-defence screen can partly swat down, the
// dreadnought lands one enormous hit every four seconds that nothing can stop and everything can dodge
// by not being there.
//
// ---- WHY THE VISUALS LIVE HERE AND NOT IN THE RENDERER ----------------------------------------
//
// Colour, width, tracking and travel speed are properties of the WEAPON, not of the thing drawing it.
// Putting them here means the projectile renderer has no table of its own to fall out of sync with,
// and adding a weapon is one entry in one place rather than an entry here plus a case in a switch
// somewhere in Visual/. The renderer asks the weapon what it looks like.
//
// The same goes for sound: each class names its own firing note (see SimpleAudio.PlayWeapon), so a
// railgun cracks, a laser snaps and a missile whooshes without the audio layer knowing what a railgun
// is.
// ============================================================================================

/// How a weapon delivers its damage. The five differ in the three things a player can actually read
/// off the screen: does it travel, does it chase, and can anything stop it.
public enum WeaponClass
{
    /// Short, fast bolts. Travel time is visible but small; no tracking — they fly where they were
    /// aimed, so a fast target crossing at range can be missed outright.
    PulseLaser,

    /// A held beam. Hits instantly and continuously while the trigger is down, which makes it the only
    /// weapon that cannot miss and the reason its damage per second is the lowest of the five.
    BeamLaser,

    /// Slow, heavy, glowing. Arcs of superheated gas that carry a lot of damage per shot and can be
    /// shot down by point defence on the way in.
    PlasmaCannon,

    /// A solid slug at enormous speed. Effectively instant, enormous single hit, very long reload, and
    /// nothing can intercept it — the trade is that it is the easiest weapon in the game to waste.
    Railgun,

    /// Homing. Slow to start, accelerates, and turns to follow its target — so it lands on a fleeing
    /// ship that everything else misses. Interceptable, and the primary thing point defence exists for.
    Missile,

    /// The screen. Very short range, very fast, very weak, and it targets PROJECTILES rather than
    /// ships — the counter to plasma and missiles, and useless against beams and railguns.
    PointDefence,
}

public class WeaponInfo
{
    public WeaponClass cls;
    public string name;

    /// Share of the hull's `attack` this mount is responsible for. A hull's mounts sum to 1, so
    /// rebalancing `attack` rebalances the whole loadout and nothing here has to be touched.
    public float attackShare = 1f;

    /// Seconds between shots. With `attackShare`, this is what separates a chattering autocannon from
    /// a spinal gun: damage per shot is (attack * share * cooldown / dps_reference).
    public float cooldown = 1.2f;

    /// How far the mount reaches, in world units. Compare against the fleet ranges in UnitInfo — these
    /// are ENGAGEMENT distances, an order of magnitude smaller than travel distances.
    public float range = 26f;

    /// World units per second. `0` means instantaneous (beam, railgun) — the renderer draws a line
    /// rather than a moving body, and the damage lands the frame it is fired.
    public float projectileSpeed = 60f;

    /// Degrees per second the projectile may turn to follow its target. 0 is a dumb-fire round.
    public float turnRate = 0f;

    /// Can point defence shoot this down on the way in?
    public bool interceptable = false;

    /// How much of the shot survives a hull's armour rating. Armour subtracts flat damage, so a weapon
    /// with high penetration is the answer to a heavily armoured target and a low-penetration weapon
    /// is the answer to a swarm of unarmoured ones.
    public float penetration = 1f;

    // ---- Appearance. The renderer reads these and holds no table of its own. ----
    public Color colour = new Color(1.00f, 0.35f, 0.30f);
    public float width = 0.14f;          // world units
    public float length = 1.6f;          // bolt length; ignored for beams
    public float glow = 1f;              // emissive multiplier

    public WeaponInfo(WeaponClass cls, string name) { this.cls = cls; this.name = name; }
}

public static class Weaponry
{
    // ============================================================================================
    // THE SIX MOUNTS
    //
    // Colours are chosen so the weapon can be identified from the shot alone at the zoom the player
    // actually fights at, and so they never collide with the two colours the map already reserves:
    // selection cyan and the red of an out-of-range path. Each is well clear of both.
    // ============================================================================================

    public static readonly WeaponInfo Pulse = new WeaponInfo(WeaponClass.PulseLaser, "Pulse Laser")
    {
        cooldown = 0.42f, range = 24f, projectileSpeed = 62f, penetration = 0.8f,
        colour = new Color(0.45f, 1.00f, 0.55f), width = 0.11f, length = 1.5f, glow = 1.35f
    };

    public static readonly WeaponInfo Beam = new WeaponInfo(WeaponClass.BeamLaser, "Beam Laser")
    {
        // Fires often and lands instantly. The cooldown is really a TICK rate — the beam is drawn as a
        // held line and re-evaluated at this interval, which is what makes "cannot miss" cheap.
        cooldown = 0.25f, range = 20f, projectileSpeed = 0f, penetration = 0.65f,
        colour = new Color(0.55f, 0.80f, 1.00f), width = 0.07f, glow = 1.6f
    };

    public static readonly WeaponInfo Plasma = new WeaponInfo(WeaponClass.PlasmaCannon, "Plasma Cannon")
    {
        cooldown = 2.1f, range = 30f, projectileSpeed = 22f, interceptable = true, penetration = 1.35f,
        colour = new Color(1.00f, 0.55f, 0.15f), width = 0.30f, length = 1.1f, glow = 1.8f
    };

    public static readonly WeaponInfo Railgun = new WeaponInfo(WeaponClass.Railgun, "Railgun")
    {
        cooldown = 4.0f, range = 44f, projectileSpeed = 0f, penetration = 2.2f,
        colour = new Color(0.85f, 0.90f, 1.00f), width = 0.16f, glow = 2.2f
    };

    public static readonly WeaponInfo Missiles = new WeaponInfo(WeaponClass.Missile, "Missile Rack")
    {
        cooldown = 3.2f, range = 40f, projectileSpeed = 17f, turnRate = 150f, interceptable = true,
        penetration = 1.1f,
        colour = new Color(1.00f, 0.85f, 0.45f), width = 0.13f, length = 0.9f, glow = 1.1f
    };

    public static readonly WeaponInfo PointDefence = new WeaponInfo(WeaponClass.PointDefence, "Point Defence")
    {
        // Not part of the attack budget at all — `attackShare` 0 means it takes nothing from the hull's
        // offensive rating. It exists to delete incoming rounds, and its damage is irrelevant.
        attackShare = 0f, cooldown = 0.20f, range = 9f, projectileSpeed = 90f, penetration = 0f,
        colour = new Color(1.00f, 1.00f, 0.75f), width = 0.06f, length = 0.5f, glow = 1.2f
    };

    // ============================================================================================
    // WHO CARRIES WHAT
    //
    // Keyed on the hull, and deliberately NOT on `attack`, so a civilian hull with a token gun and a
    // warship with the same rating still read as different things. The shares in each loadout sum to
    // 1 (point defence excepted, which takes none), so `UnitInfo.attack` remains the single number
    // that decides how hard the hull hits and every existing balance figure survives untouched.
    //
    // ANYTHING NOT LISTED gets a single pulse laser if it has an attack rating at all, and nothing if
    // it does not — a colony ship is not armed because nobody gave it a loadout, which is the right
    // default and means adding a hull never silently arms it.
    // ============================================================================================
    public static WeaponInfo[] LoadoutFor(UnitType t)
    {
        switch (t)
        {
            // ---- The fighter line: small, fast, close in, and it escalates by adding mounts ----
            //
            // Reading the three together is the clearest statement of what a refit BUYS. The Mk I is one
            // gun. The Mk II splits its budget and gains a beam it cannot miss with. The Mk III keeps
            // both and adds a screen, which is the first time a fighter stops being purely a glass
            // cannon. `attack` climbs across the three as well, so a refit is more damage AND more
            // options rather than a bigger version of the same shot.
            case UnitType.Fighter:
                return new[] { Clone(Pulse, 1.00f) };
            case UnitType.FighterII:
                return new[] { Clone(Pulse, 0.62f), Clone(Beam, 0.38f) };
            case UnitType.FighterIII:
                return new[] { Clone(Pulse, 0.50f), Clone(Beam, 0.30f), Clone(Missiles, 0.20f), PointDefence };

            // The first dedicated warship: a missile boat with a screen of its own.
            case UnitType.Frigate:
                return new[] { Clone(Pulse, 0.45f), Clone(Missiles, 0.55f), PointDefence };

            case UnitType.Cruiser:
                return new[] { Clone(Plasma, 0.45f), Clone(Railgun, 0.35f), Clone(Pulse, 0.20f), PointDefence };

            // A carrier's guns are an afterthought — it is armoured, it screens heavily, and its job is
            // to still be there afterwards.
            case UnitType.Carrier:
                return new[] { Clone(Pulse, 0.55f), Clone(Missiles, 0.45f), PointDefence, PointDefence };

            case UnitType.Dreadnought:
                return new[] { Clone(Railgun, 0.46f), Clone(Plasma, 0.28f), Clone(Missiles, 0.16f),
                               Clone(Beam, 0.10f), PointDefence };

            // A fortress does not manoeuvre, so it is all reach and screen.
            case UnitType.BattleStation:
                return new[] { Clone(Railgun, 0.40f), Clone(Plasma, 0.35f), Clone(Missiles, 0.25f),
                               PointDefence, PointDefence };

            // ---- Civilian hulls, explicitly ----
            //
            // Listed rather than left to the default, because "this ship is unarmed" is a design
            // statement and the default is a fallback. A scout carries a token gun so it can defend
            // itself badly; a colony ship, a terraformer, a miner, a transport and a probe carry
            // nothing at all, and that is the reason escorting them is a mechanic.
            case UnitType.Scout:
            case UnitType.ScoutII:
            case UnitType.ScoutIII:
            case UnitType.Explorer:
                return new[] { Clone(Pulse, 1.00f) };

            case UnitType.ColonyShip:
            case UnitType.Terraformer:
            case UnitType.Miner:
            case UnitType.Transport:
            case UnitType.Probe:
            case UnitType.ResearchShip:
            case UnitType.ResearchShipII:
            case UnitType.ResearchShipIII:
            case UnitType.ScienceVessel:
                return System.Array.Empty<WeaponInfo>();

            default:
                return null;   // filled in by For(), which knows whether the hull is armed at all
        }
    }

    /// The loadout a unit actually flies with. Never null; an unarmed hull returns an empty array.
    public static WeaponInfo[] For(UnitType t)
    {
        var explicitLoadout = LoadoutFor(t);
        if (explicitLoadout != null) return explicitLoadout;

        var info = UnitDatabase.Get(t);
        if (info == null || info.attack <= 0) return System.Array.Empty<WeaponInfo>();

        // An armed hull nobody wrote a loadout for gets one honest gun rather than nothing, so a new
        // warship is never silently a pacifist.
        return new[] { Clone(Pulse, 1.00f) };
    }

    /// Damage one shot from this mount does, before the target's armour.
    ///
    /// Rate-normalised on purpose: `attack` is a DPS-like rating, so a mount that fires every four
    /// seconds has to hit for four seconds' worth or a railgun cruiser would do a quarter the damage of
    /// a pulse fighter with the same rating. This is the line that makes cooldown a feel choice rather
    /// than a balance one.
    public static float ShotDamage(Unit u, WeaponInfo w)
    {
        if (u == null || w == null || w.attackShare <= 0f) return 0f;
        return u.EffectiveAttack * w.attackShare * w.cooldown;
    }

    /// The longest reach in a loadout — how close a ship has to get before anything happens.
    public static float MaxRange(WeaponInfo[] loadout)
    {
        float r = 0f;
        if (loadout != null)
            foreach (var w in loadout)
                if (w != null && w.attackShare > 0f && w.range > r) r = w.range;
        return r;
    }

    /// A copy of a mount carrying a different share of the hull's attack budget.
    ///
    /// Mounts are shared immutable descriptions, so a loadout that wants "a beam, but only 62% of this
    /// hull's damage" must not reach into the shared Beam and change it — every other hull carrying a
    /// beam would change with it. Point defence is handed out unmodified precisely because its share is
    /// always zero and there is nothing to vary.
    static WeaponInfo Clone(WeaponInfo src, float share) => new WeaponInfo(src.cls, src.name)
    {
        attackShare = share,
        cooldown = src.cooldown, range = src.range, projectileSpeed = src.projectileSpeed,
        turnRate = src.turnRate, interceptable = src.interceptable, penetration = src.penetration,
        colour = src.colour, width = src.width, length = src.length, glow = src.glow
    };
}
