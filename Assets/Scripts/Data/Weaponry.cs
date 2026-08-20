using UnityEngine;

// ============================================================================================
// WHAT A SHIP SHOOTS WITH
//
// `UnitInfo.attack` was a single number and stayed one — it is still what decides how hard a hull
// hits, and every balance pass that has ever been made against it still means what it meant. What was
// missing was everything ELSE about a shot: how fast it flies, whether it chases, how far it reaches,
// how often it goes off, what it costs to fire, and what it looks and sounds like. A game where every
// weapon is one number has no reason to draw anything, and a game that draws one bolt for every
// weapon has no reason to have more than one.
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
//
// ---- AND WHY THE FLIGHT MODEL LIVES IN Ballistics ---------------------------------------------
//
// The fields below say WHAT a round is. Ballistics says how one behaves. That split is what lets the
// whole flight model be ported to Node and rendered as a picture — see tools/ballistics-check.mjs —
// which is the only way anything here gets looked at before it ships, because there is no Unity in
// this environment.
//
// ---- AMMUNITION, AND THE ONE DECISION IT EXISTS TO CREATE --------------------------------------
//
// Every mount draws on one of two things (see AmmoKind), and the choice between them is a STRATEGIC
// one dressed as a tactical one:
//
//   ENERGY mounts are free forever and rationed in the moment. A laser fleet can sit in hostile space
//   indefinitely, and cannot alpha-strike — empty the capacitor and the guns droop until it refills.
//
//   ORDNANCE mounts hit far harder for their reload and then run out. A missile fleet wins the first
//   engagement decisively and has to go home afterwards, which is what gives a forward station a job
//   and what makes "how far from supply are we" a question worth asking.
//
// Nothing enforces a doctrine. The loadouts simply make the heavy hitters ordnance-fed and the
// workhorses energy-fed, and the player picks a fleet composition that leans one way or the other.
// ============================================================================================

/// How a weapon delivers its damage. The eight differ in the things a player can actually read off
/// the screen: does it travel, does it chase, can anything stop it, and does it run out.
public enum WeaponClass
{
    /// Short, fast bolts. Travel time is visible but small; no tracking — they fly where they were
    /// aimed, so a fast target crossing at range can be missed outright.
    PulseLaser,

    /// A held beam. Hits instantly and continuously while the trigger is down, which makes it the only
    /// weapon that cannot miss and the reason its damage per second is the lowest of the eight.
    BeamLaser,

    /// Slow, heavy, glowing. Arcs of superheated gas that carry a lot of damage per shot and can be
    /// shot down by point defence on the way in.
    PlasmaCannon,

    /// A solid slug at enormous speed. Effectively instant, enormous single hit, very long reload, and
    /// nothing can intercept it — the trade is that it is the easiest weapon in the game to waste, and
    /// now that it is ordnance-fed, wasting one costs something you had to carry.
    Railgun,

    /// Homing. Slow off the rail, accelerates hard, and steers by proportional navigation — so it
    /// leads its target rather than chasing it, and lands on a fleeing ship that everything else
    /// misses. Interceptable, and the primary thing point defence exists for.
    Missile,

    /// The screen. Very short range, very fast, very weak, and it targets PROJECTILES rather than
    /// ships — the counter to plasma, missiles and torpedoes, and useless against beams and railguns.
    PointDefence,

    // ---- appended below this line; WeaponClass is read by SimpleAudio and must stay stable --------

    /// Rapid kinetic spray. Cheap, short-ranged, poor against armour and excellent against everything
    /// unarmoured — and it eats magazines faster than anything else in the game.
    Autocannon,

    /// A capital-ship killer: enormous, slow, long-burning, and carried four at a time. It corners as
    /// tightly as a missile at half the speed, so it will follow a battleship around a moon — but it
    /// takes so long to arrive that a screen has every chance to shoot it down.
    Torpedo,
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

    /// Cruise speed, world units per second. `0` means instantaneous (beam, railgun) — the renderer
    /// draws a line rather than a moving body, and the damage lands the frame it is fired.
    public float projectileSpeed = 60f;

    /// How much of the shot survives a hull's armour rating. Armour subtracts flat damage, so a weapon
    /// with high penetration is the answer to a heavily armoured target and a low-penetration weapon
    /// is the answer to a swarm of unarmoured ones.
    public float penetration = 1f;

    /// Can point defence shoot this down on the way in?
    public bool interceptable = false;

    // ---- GUIDANCE ------------------------------------------------------------------------------

    /// How the round steers once it is away. See Ballistics for what each law actually does.
    public GuidanceLaw guidance = GuidanceLaw.Unguided;

    /// Proportional navigation constant. Roughly, how many degrees the round turns for each degree the
    /// sight line drifts. Below 2 it under-leads and trails; above 5 it snaps onto the collision course
    /// so hard it looks like it is on rails. Everything real sits between 3 and 4.
    public float navConstant = 3.6f;

    /// Lateral thrust the round can pull, in world units per second squared. THIS, not `turnRate`, is
    /// what decides whether a missile can corner — see Ballistics.TurnRadius. Zero on anything that
    /// does not steer.
    public float lateralAccel = 0f;

    /// A hard ceiling on how fast the round may swing its nose, degrees per second, standing in for
    /// the mechanical limit of a fin or a thruster ring. Ballistics takes the tighter of this and what
    /// `lateralAccel` allows at the round's current speed.
    public float turnRate = 0f;

    /// Half-angle of the seeker's field of view, in degrees. Once the target falls outside it the round
    /// is blind and will not reacquire. 180 is an all-aspect seeker that cannot be shaken.
    ///
    /// ---- WHAT THIS NUMBER ACTUALLY CONTROLS, WHICH IS NOT WHAT IT LOOKS LIKE ----
    ///
    /// It is tempting to read the cone as "how hard you have to turn to shake the missile", and that
    /// is wrong. Proportional navigation holds the target at a CONSTANT bearing off the nose — the
    /// lead angle — and that angle is bounded by asin(Vtarget / Vmissile) no matter how the target
    /// flies. So a cone can only ever bite when the target's speed approaches the round's.
    ///
    /// The figures come from tools/ballistics-check.mjs, which sweeps it: against ordinary hulls up
    /// to 16 u/s the target peaks at FOURTEEN degrees off the nose for the whole flight, and only at
    /// 32 u/s does it reach thirty-five. The cone was 75 degrees, which meant it could never fire
    /// under any circumstances and the seeker was decorative.
    ///
    /// At 45 the arithmetic says a target above about 24 u/s can break the lock — which lands on the
    /// fastest scouts in the game and nothing else. That is the intended shape: a scout can shake a
    /// missile by flying, a warship cannot, and what beats a missile aimed at a warship is outlasting
    /// its fuel.
    public float seekerConeDeg = 180f;

    /// Seconds before the seeker starts checking whether it can still see the target.
    ///
    /// ---- THIS MUST BE AT LEAST `boostTime`, AND THE REASON IS NOT OBVIOUS ----
    ///
    /// The first guess was a third of a second — long enough to clear the cold launch. It broke every
    /// missile in the game, and working out why is the most useful thing in this file.
    ///
    /// Proportional navigation holds the target at a CONSTANT bearing off the nose. That bearing is
    /// the lead angle, and the lead angle is asin(Vtarget / Vmissile) — so it depends on how fast the
    /// ROUND is going, and during boost the round is barely moving. A missile still doing 8 u/s at an
    /// 11 u/s target needs to lead by more than sixty degrees, and it will sit there, correctly, at
    /// forty-five degrees off its own nose for a full second before its speed builds and the angle
    /// collapses. tools/ballistics-check.mjs prints exactly that curve: 40, 43, 45, 46, 44, 38, 24,
    /// 17 degrees, and then a hit.
    ///
    /// A seeker that armed during that window would look at a perfectly healthy intercept, decide the
    /// target had left its field of view, and go ballistic. Which is what happened: the crossing-target
    /// panel went from a clean hit to an eight-unit miss, and pure pursuit — which points straight at
    /// the target and therefore never trips the cone — started beating proportional navigation.
    ///
    /// So the seeker is caged until the motor is up to speed. Guidance still commands from launch;
    /// only the give-up test waits.
    public float seekerArmTime = 0.35f;

    // ---- THE MOTOR -----------------------------------------------------------------------------

    /// Speed the round leaves the tube at, before its motor lights. Well under cruise on anything that
    /// boosts, because a missile drifting clear of the hull and THEN igniting is most of what makes a
    /// launch read as a launch.
    public float launchSpeed = 0f;

    /// Seconds spent accelerating from `launchSpeed` to `projectileSpeed`.
    public float boostTime = 0f;

    /// Seconds of motor burn in total, including the boost. After this the round is ballistic: it keeps
    /// its speed forever, because vacuum, and CANNOT TURN AT ALL, because in space steering is
    /// thrusting. Zero means no motor is modelled and the round is always in control of itself.
    public float fuelTime = 0f;

    /// How long the taper at the end of the burn lasts. Turn authority bleeds away over this window
    /// instead of falling off a cliff, so a missile going dry visibly loses its grip on the turn.
    public float burnoutTaper = 0.6f;

    /// Total seconds the round exists before it gives up and dissipates. Longer than `fuelTime` on
    /// anything guided, and the gap between the two is the ballistic coast — the stretch where the
    /// player can watch a missile sail past behind them because it has nothing left to turn with.
    public float trackTime = 0f;

    /// Degrees off the firing bore the round is ejected at. A cold launch throws the round clear
    /// sideways before the motor lights and guidance hauls it around, which is where a missile's
    /// signature opening curve comes from — it is not an animation, it is a bad initial condition that
    /// proportional navigation then fixes in front of the player.
    public float launchArcDeg = 0f;

    /// Degrees per second of slow random drift while in flight. Only plasma has any: a magnetically
    /// contained ball of gas does not fly a ruler-straight line, and the wander is what makes a plasma
    /// bolt read as barely-contained rather than as a painted slug on a rail.
    public float wanderDeg = 0f;

    // ---- ACCURACY ------------------------------------------------------------------------------

    /// Base pointing error of the mount, in degrees, at point-blank against a stationary target.
    public float spreadDeg = 0f;

    /// How much that error grows at the mount's maximum reach, as a multiple of the base. 0 means the
    /// weapon is as accurate at the edge of its envelope as it is in the shooter's lap.
    public float spreadRangeFactor = 0f;

    /// Extra error, in degrees, per world-unit-per-second of the target's CROSSING speed. This is what
    /// makes manoeuvring a defence and flying in a straight line a mistake.
    public float spreadCrossFactor = 0f;

    /// How close the round has to get to count as a hit. A torpedo has a proximity fuse and a
    /// point-defence needle has to physically touch.
    public float hitRadius = 0.55f;

    // ---- SUPPLY --------------------------------------------------------------------------------

    /// Reactor charge or physical rounds. See AmmoKind.
    public AmmoKind ammo = AmmoKind.Energy;

    /// Capacitor units drawn per shot. Energy mounts only.
    public float energyPerShot = 0f;

    /// Rounds carried per mount at full load, BEFORE the hull's magazine multiplier — a dreadnought's
    /// missile rack holds several times what a fighter's does off the same figure. Ordnance only.
    public int magazine = 0;

    /// How many rounds one trigger pull sends, and how far apart. A rack that empties two tubes in
    /// quick succession reads completely differently from one that fires a single round, and it costs
    /// two rounds of magazine to do it.
    public int salvo = 1;
    public float salvoSpacing = 0.14f;

    // ---- Appearance. The renderer reads these and holds no table of its own. ----
    public Color colour = new Color(1.00f, 0.35f, 0.30f);
    public float width = 0.14f;          // world units
    public float length = 1.6f;          // bolt length; ignored for beams
    public float glow = 1f;              // emissive multiplier

    public WeaponInfo(WeaponClass cls, string name) { this.cls = cls; this.name = name; }

    /// Does this mount steer after launch?
    public bool IsGuided => guidance != GuidanceLaw.Unguided && lateralAccel > 0.0001f;

    /// Does it arrive the frame it is fired?
    public bool IsInstant => projectileSpeed <= 0.01f;

    /// How long a round from this mount lives, whatever kind it is.
    public float Lifetime => trackTime > 0f ? trackTime : Ballistics.MaxDumbFlight;
}

public static class Weaponry
{
    // ============================================================================================
    // THE EIGHT MOUNTS
    //
    // Colours are chosen so the weapon can be identified from the shot alone at the zoom the player
    // actually fights at, and so they never collide with the two colours the map already reserves:
    // selection cyan and the red of an out-of-range path. Each is well clear of both.
    //
    // The accuracy figures are the ones worth reading together, because they are what decides which
    // gun answers which target rather than which gun is better:
    //
    //   pulse       small base error, grows with range, punished hard by crossing speed
    //   beam        NO error of any kind — the one weapon that literally cannot miss
    //   plasma      slow enough that crossing speed hurts it more than anything else in the game
    //   railgun     the most precise mount there is, and it gets one shot every four seconds
    //   autocannon  sprays, and does not care, because it fires eight times in the time a railgun fires once
    // ============================================================================================

    public static readonly WeaponInfo Pulse = new WeaponInfo(WeaponClass.PulseLaser, "Pulse Laser")
    {
        cooldown = 0.42f, range = 24f, projectileSpeed = 62f, penetration = 0.8f,
        trackTime = 0.9f, hitRadius = 0.45f,
        spreadDeg = 1.1f, spreadRangeFactor = 1.4f, spreadCrossFactor = 0.30f,
        ammo = AmmoKind.Energy, energyPerShot = 3.0f,
        colour = new Color(0.45f, 1.00f, 0.55f), width = 0.11f, length = 1.5f, glow = 1.35f
    };

    public static readonly WeaponInfo Beam = new WeaponInfo(WeaponClass.BeamLaser, "Beam Laser")
    {
        // Fires often and lands instantly. The cooldown is really a TICK rate — the beam is drawn as a
        // held line and re-evaluated at this interval, which is what makes "cannot miss" cheap.
        //
        // Every accuracy field is deliberately left at zero. That is the design statement: light does
        // not need leading, so there is no range term, no crossing term and no dispersion. It is also
        // why the beam has the worst damage per second on the list.
        cooldown = 0.25f, range = 20f, projectileSpeed = 0f, penetration = 0.65f,
        ammo = AmmoKind.Energy, energyPerShot = 2.0f,
        colour = new Color(0.55f, 0.80f, 1.00f), width = 0.07f, glow = 1.6f
    };

    public static readonly WeaponInfo Plasma = new WeaponInfo(WeaponClass.PlasmaCannon, "Plasma Cannon")
    {
        cooldown = 2.1f, range = 30f, projectileSpeed = 26f, interceptable = true, penetration = 1.35f,
        trackTime = 2.1f, hitRadius = 0.62f, wanderDeg = 9f,
        spreadDeg = 2.2f, spreadRangeFactor = 1.6f, spreadCrossFactor = 0.50f,
        ammo = AmmoKind.Energy, energyPerShot = 14f,
        colour = new Color(1.00f, 0.55f, 0.15f), width = 0.30f, length = 1.1f, glow = 1.8f
    };

    public static readonly WeaponInfo Railgun = new WeaponInfo(WeaponClass.Railgun, "Railgun")
    {
        // Still instantaneous, and still uninterceptable — that pairing is the whole identity of the
        // mount and nothing about ammunition changes it. What ammunition changes is the COST of
        // missing: a railgun cruiser now carries twenty slugs, so a wasted shot is a slug that is not
        // there for the dreadnought two minutes later.
        cooldown = 4.0f, range = 44f, projectileSpeed = 0f, penetration = 2.2f,
        hitRadius = 0.5f,
        spreadDeg = 0.5f, spreadRangeFactor = 2.2f, spreadCrossFactor = 0.55f,
        ammo = AmmoKind.Ordnance, magazine = 20,
        colour = new Color(0.85f, 0.90f, 1.00f), width = 0.16f, glow = 2.2f
    };

    public static readonly WeaponInfo Missiles = new WeaponInfo(WeaponClass.Missile, "Missile Rack")
    {
        cooldown = 3.2f, range = 40f, penetration = 1.1f, interceptable = true,

        // ---- the motor ----
        // Off the rail at 6 and up to 34 in 1.3 seconds. Powered for 3.8 seconds in total, alive for 6:
        // so a missile that has not found its target within about a hundred and ten units of flying is
        // a missile coasting on momentum with no ability to turn, and the two seconds after that are
        // the ones the player spends watching it fail to correct.
        launchSpeed = 6f, projectileSpeed = 34f, boostTime = 1.3f,
        fuelTime = 3.8f, burnoutTaper = 0.6f, trackTime = 6.0f,

        // ---- guidance ----
        // Lateral thrust of 95 at a cruise of 34 gives a turn radius of about thirteen units, against
        // a fighter's twenty-three. So a missile out-corners everything that flies — but only while it
        // has fuel, and only while the target stays inside a 75-degree seeker cone.
        guidance = GuidanceLaw.Proportional, navConstant = 3.6f,
        lateralAccel = 95f, turnRate = 150f, seekerConeDeg = 75f, seekerArmTime = 1.45f,

        launchArcDeg = 38f, hitRadius = 0.70f,
        spreadDeg = 0f,     // a guided round does not need dispersion; the seeker cone is its limit
        ammo = AmmoKind.Ordnance, magazine = 12, salvo = 2, salvoSpacing = 0.16f,
        colour = new Color(1.00f, 0.85f, 0.45f), width = 0.13f, length = 0.9f, glow = 1.1f
    };

    public static readonly WeaponInfo Torpedo = new WeaponInfo(WeaponClass.Torpedo, "Torpedo Tube")
    {
        cooldown = 9.0f, range = 52f, penetration = 2.6f, interceptable = true,

        // Half a missile's speed and a fifth of its lateral thrust — which comes out at almost exactly
        // the same turn radius. That is the point of the hull: a torpedo corners like a missile, so
        // nothing shakes it, and it takes eleven seconds to cross its own range, so everything gets a
        // chance to shoot it down. It is a threat you answer with a screen, not with manoeuvre.
        launchSpeed = 4f, projectileSpeed = 18f, boostTime = 2.2f,
        fuelTime = 7.0f, burnoutTaper = 1.0f, trackTime = 11f,

        guidance = GuidanceLaw.Proportional, navConstant = 4.0f,
        lateralAccel = 26f, turnRate = 60f, seekerConeDeg = 75f, seekerArmTime = 2.35f,

        launchArcDeg = 22f, hitRadius = 0.90f,
        ammo = AmmoKind.Ordnance, magazine = 4,
        colour = new Color(0.95f, 0.40f, 0.85f), width = 0.22f, length = 1.9f, glow = 1.45f
    };

    public static readonly WeaponInfo Autocannon = new WeaponInfo(WeaponClass.Autocannon, "Autocannon")
    {
        // The answer to a swarm, and useless against a capital: penetration 0.55 means armour eats most
        // of every round. It fires a two-round burst every 0.28s, so it drains a magazine in under a
        // minute of sustained fire — which is what makes "how long has this fight been going" a real
        // question for a fleet built around it.
        cooldown = 0.28f, range = 18f, projectileSpeed = 78f, penetration = 0.55f,
        trackTime = 0.7f, hitRadius = 0.42f,
        spreadDeg = 2.6f, spreadRangeFactor = 1.8f, spreadCrossFactor = 0.42f,
        ammo = AmmoKind.Ordnance, magazine = 400, salvo = 2, salvoSpacing = 0.07f,
        colour = new Color(1.00f, 0.78f, 0.35f), width = 0.08f, length = 0.7f, glow = 1.0f
    };

    public static readonly WeaponInfo PointDefence = new WeaponInfo(WeaponClass.PointDefence, "Point Defence")
    {
        // Not part of the attack budget at all — `attackShare` 0 means it takes nothing from the hull's
        // offensive rating. It exists to delete incoming rounds, and its damage is irrelevant.
        //
        // Energy-fed on purpose. A screen that ran out of ammunition would mean a fleet a long way from
        // supply loses its defence at exactly the moment it needs it most, which is a spiral rather
        // than a decision: the player who is already in trouble gets more trouble. The capacitor draw
        // is small but real, so a ship that is swatting missiles all engagement has a little less
        // charge for its own guns, and that IS a decision.
        attackShare = 0f, cooldown = 0.20f, range = 9f, projectileSpeed = 90f, penetration = 0f,
        guidance = GuidanceLaw.Pursuit, lateralAccel = 400f, turnRate = 720f,
        trackTime = 0.8f, hitRadius = 0.35f,
        ammo = AmmoKind.Energy, energyPerShot = 0.5f,
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
    //
    // ---- READ THE SUPPLY COLUMN, NOT THE DAMAGE COLUMN ------------------------------------------
    //
    // The escalation across the warship line is not really about damage. It is about what the hull
    // has to carry:
    //
    //   Fighter      pure energy. Never needs rearming, never runs dry, dies to anything with armour.
    //   Frigate      first ordnance mount. Twelve missiles and then it is a pulse boat.
    //   Cruiser      railgun and autocannon. Genuinely thirsty; a cruiser squadron away from supply
    //                for a long campaign is a squadron losing its teeth one mount at a time.
    //   Dreadnought  four torpedoes. Four. The single most consequential ammunition figure in the
    //                game, because it means the alpha strike is a resource and not a rotation.
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
            //
            // All three stay entirely energy-fed. A fighter wing that had to be rearmed would turn
            // carriers into a chore rather than an option, and "it never runs out" is the compensation
            // for a hull that folds the moment it meets armour.
            case UnitType.Fighter:
                return new[] { Clone(Pulse, 1.00f) };
            case UnitType.FighterII:
                return new[] { Clone(Pulse, 0.62f), Clone(Beam, 0.38f) };
            case UnitType.FighterIII:
                return new[] { Clone(Pulse, 0.50f), Clone(Beam, 0.30f), Clone(Missiles, 0.20f), PointDefence };

            // The first dedicated warship: a missile boat with a screen of its own, and the first hull
            // in the game that can run out of anything.
            case UnitType.Frigate:
                return new[] { Clone(Pulse, 0.20f), Clone(Autocannon, 0.28f), Clone(Missiles, 0.52f),
                               PointDefence };

            case UnitType.Cruiser:
                return new[] { Clone(Plasma, 0.40f), Clone(Railgun, 0.33f), Clone(Autocannon, 0.27f),
                               PointDefence };

            // A carrier's guns are an afterthought — it is armoured, it screens heavily, and its job is
            // to still be there afterwards. It is also the fleet's magazine: see Magazines, which lets
            // a carrier rearm the hulls flying with it.
            case UnitType.Carrier:
                return new[] { Clone(Pulse, 0.40f), Clone(Autocannon, 0.20f), Clone(Missiles, 0.40f),
                               PointDefence, PointDefence };

            case UnitType.Dreadnought:
                return new[] { Clone(Railgun, 0.38f), Clone(Torpedo, 0.24f), Clone(Plasma, 0.20f),
                               Clone(Beam, 0.10f), Clone(Autocannon, 0.08f), PointDefence };

            // A fortress does not manoeuvre, so it is all reach and screen. It is also permanently in
            // supply if it is anywhere useful, which is the quiet reason a station out-shoots a fleet
            // of the same tonnage over a long engagement.
            case UnitType.BattleStation:
                return new[] { Clone(Railgun, 0.34f), Clone(Torpedo, 0.26f), Clone(Missiles, 0.24f),
                               Clone(Autocannon, 0.16f), PointDefence, PointDefence };

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
    ///
    /// A SALVO SPLITS IT. A rack that sends two missiles per pull does not do twice the damage — each
    /// round carries half. Otherwise `salvo` would be a free damage multiplier and every mount in the
    /// file would want one.
    public static float ShotDamage(Unit u, WeaponInfo w)
    {
        if (u == null || w == null || w.attackShare <= 0f) return 0f;
        float perPull = u.EffectiveAttack * w.attackShare * w.cooldown;
        return perPull / Mathf.Max(1, w.salvo);
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
    ///
    /// EVERY FIELD HAS TO BE HERE. A field added above and forgotten here is silently zero on every
    /// mount any warship actually carries, while still reading correctly on the shared originals this
    /// file prints — which is the most confusing possible way for a weapon to be broken.
    static WeaponInfo Clone(WeaponInfo src, float share) => new WeaponInfo(src.cls, src.name)
    {
        attackShare = share,
        cooldown = src.cooldown, range = src.range, projectileSpeed = src.projectileSpeed,
        penetration = src.penetration, interceptable = src.interceptable,

        guidance = src.guidance, navConstant = src.navConstant,
        lateralAccel = src.lateralAccel, turnRate = src.turnRate, seekerConeDeg = src.seekerConeDeg, seekerArmTime = src.seekerArmTime,

        launchSpeed = src.launchSpeed, boostTime = src.boostTime, fuelTime = src.fuelTime,
        burnoutTaper = src.burnoutTaper, trackTime = src.trackTime,
        launchArcDeg = src.launchArcDeg, wanderDeg = src.wanderDeg,

        spreadDeg = src.spreadDeg, spreadRangeFactor = src.spreadRangeFactor,
        spreadCrossFactor = src.spreadCrossFactor, hitRadius = src.hitRadius,

        ammo = src.ammo, energyPerShot = src.energyPerShot, magazine = src.magazine,
        salvo = src.salvo, salvoSpacing = src.salvoSpacing,

        colour = src.colour, width = src.width, length = src.length, glow = src.glow
    };
}
