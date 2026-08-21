using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// SHOOTING AT EACH OTHER
//
// The rule the whole system rests on: SHIPS FIGHT WHEN THEY ARE NEAR SOMETHING THEY HATE. There is no
// attack order, no combat mode, no separate battle screen. A warship that finds itself within weapons
// range of a hostile hull opens fire, and it keeps firing until one of them is gone or out of range.
//
// That choice is load-bearing rather than a shortcut. An explicit attack order would mean combat only
// happens when the player is watching and clicking, which makes escorting pointless (the escort would
// need orders too), makes ancient defences impossible (nobody orders those), and makes a transport
// jumped in deep space a non-event. Proximity means the player's real decision is WHERE THEY SEND
// THINGS AND WITH WHAT — which is the decision the rest of the game is about.
//
// ---- WHAT A TICK DOES -------------------------------------------------------------------------
//
//   1. Group every living unit by locality, so a fight is resolved against a handful of neighbours
//      rather than against every ship in the galaxy. Combat ranges are tens of units; fleets are
//      hundreds apart.
//   2. For each armed unit, pick a target — the most dangerous thing it can reach, because a screen
//      of fighters in front of a dreadnought should not soak the dreadnought's fire.
//   3. Fire every mount that has come off cooldown, CAN AFFORD TO, and has a firing solution — once
//      each, and a mount that fires a salvo keeps sending rounds for the next few frames.
//   4. Let point-defence mounts swat down whatever is inbound.
//
// Damage does not land here. A shot is handed to ProjectileRenderer, which flies it and calls back
// into ResolveHit when it arrives — so a missile crossing a gap really is in the air for that time,
// and a target that dies first eats none of the volley already aimed at it.
//
// ---- AIMING, WHICH USED NOT TO HAPPEN ---------------------------------------------------------
//
// Every shot used to be fired at the target's CURRENT position, which for anything with a travel time
// meant firing at where it had already stopped being. Now each mount solves an intercept (Ballistics
// .LeadAim), declines the shot outright if the target is outrunning the weapon, and then throws its
// own dispersion cone at the answer. Three consequences worth knowing about:
//
//   * unguided weapons genuinely hit things now, which is a substantial buff to every hull carrying
//     one and was never intended as a nerf in the first place;
//   * manoeuvring is a defence, because a solution is computed at the muzzle and a target that
//     changes velocity afterwards invalidates it;
//   * a mount will hold its fire rather than waste a round it cannot land — which matters little for
//     a laser and enormously for a torpedo tube carrying four.
//
// ---- AND SHIPS CAN RUN OUT ---------------------------------------------------------------------
//
// Magazines owns the two supply resources. Combat asks it before every trigger pull and spends
// through it afterwards; nothing else here knows what a magazine is.
//
// ---- TIME -------------------------------------------------------------------------------------
//
// Cooldowns run on SCALED time (Time.deltaTime already carries Time.timeScale), so a paused game
// stops the fight and a 5x game fights five times as fast, exactly like every other simulation here.
// The projectile and explosion renderers deliberately do NOT — see their headers.
// ============================================================================================
public class CombatManager : MonoBehaviour
{
    public static CombatManager Instance;

    /// How often engagements are re-evaluated, in seconds of game time. Targets do not need picking
    /// sixty times a second — they need picking often enough that a ship entering range starts
    /// shooting promptly, and rarely enough that the neighbour search is not the frame's biggest cost.
    const float RetargetInterval = 0.35f;

    /// Ships further apart than the longest weapon in the game never interact, so the neighbour search
    /// can bucket on this and skip everything else outright.
    const float MaxEngagementRange = 48f;

    /// A hard ceiling on rounds in the air. A hundred ships all firing missiles would otherwise fill
    /// the projectile list faster than it drains, and the frame cost is in the DRAWING, not the maths.
    const int ProjectileCeiling = 260;

    /// Per-unit firing state. Kept here rather than on Unit because it is pure runtime bookkeeping —
    /// it must not be saved, and a ship that reloads mid-battle with a hot cooldown is not a thing
    /// anyone needs restored.
    class Mounts
    {
        public WeaponInfo[] loadout;
        public float[] cooldown;      // seconds remaining per mount
        public Unit target;
        public float retargetIn;

        // ---- salvos ----------------------------------------------------------------------------
        //
        // A rack that empties two tubes per trigger pull cannot fire both on the same frame, or the
        // two rounds are one round drawn twice: same position, same heading, same everything, and the
        // volley reads as a single fat missile. Spacing them by a tenth of a second is what makes a
        // launch look like a launch.
        //
        // The ammunition for the WHOLE salvo is spent when the trigger is pulled, not as each round
        // leaves — see Magazines.CanFire. So a rack cannot start a two-round salvo with one round
        // left and quietly fire one and a half.
        public int[] salvoLeft;       // rounds still to leave this mount from the current pull
        public float[] salvoTimer;    // seconds until the next one does
    }

    readonly Dictionary<Unit, Mounts> mounts = new Dictionary<Unit, Mounts>();
    readonly List<Unit> scratch = new List<Unit>();

    // ============================================================================================
    // WHO IS IN A FIGHT
    //
    // Asked by the renderer, every frame, for every drawn hull — because a ship under fire flies
    // evasively and a ship at peace does not, and the difference has to be cheap to ask about.
    //
    // Both ENDS of every engagement go in. A colony ship has no guns and picks no target, and it is
    // precisely the hull that should be jinking; marking only shooters would leave the things most
    // worth evading with the least reason to. So a unit is engaged if it has a target OR is one.
    //
    // Two sets swapped rather than one cleared, so a reader mid-frame never sees a half-built set —
    // the renderer runs in LateUpdate and this is filled in Update, which is exactly the ordering
    // where a single set would be observed empty for one frame every frame.
    static HashSet<Unit> engaged = new HashSet<Unit>();
    static HashSet<Unit> engagedNext = new HashSet<Unit>();

    /// Is this ship shooting at something, or being shot at?
    public static bool InCombat(Unit u) => u != null && engaged.Contains(u);

    public static void Create()
    {
        if (Instance != null) return;
        new GameObject("CombatManager").AddComponent<CombatManager>();
    }

    void Awake() { Instance = this; }

    /// Forget every ship's firing state. Called when a galaxy is replaced — the keys are Units that
    /// are about to stop existing, and this dictionary would otherwise hold them alive forever, the
    /// same hazard GameManager clears the other per-object caches for.
    public static void ResetAll()
    {
        if (Instance == null) return;
        Instance.mounts.Clear();
        engaged.Clear();
        engagedNext.Clear();
        Magazines.ResetAll();
        CombatOrders.ResetAll();
        ProjectileRenderer.Instance?.ClearAll();
        ExplosionRenderer.Instance?.ClearAll();
    }

    // ============================================================================================
    void Update()
    {
        var um = UnitManager.Instance;
        if (um == null || SystemContext.Galaxy == null) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // A snapshot: firing can destroy ships, and destroying a ship mutates the manager's list.
        scratch.Clear();
        foreach (var u in um.Units)
            if (u != null && !u.IsDestroyed && u.hideReason == HideReason.None) scratch.Add(u);

        // Capacitors recharge and magazines refill here rather than in a manager of their own. Combat
        // already ticks every frame with the unit list in hand, and a second MonoBehaviour would be a
        // second thing to create, reset, and eventually forget to reset.
        Magazines.Tick(dt, scratch);

        engagedNext.Clear();

        for (int i = 0; i < scratch.Count; i++)
        {
            var u = scratch[i];
            if (u.IsDestroyed) continue;

            var m = MountsFor(u);
            if (m.loadout.Length == 0) continue;

            // ---- cooldowns ----
            for (int k = 0; k < m.cooldown.Length; k++)
                if (m.cooldown[k] > 0f) m.cooldown[k] -= dt;

            // ---- rounds still owed from a trigger already pulled ----
            //
            // Ahead of BOTH retargeting and the hold-fire check, and both placements are load-bearing.
            // A salvo whose ammunition has already been spent must finish leaving the tubes: if a
            // squadron is told to hold fire halfway through one, or the mount's target changes under
            // it, the remaining rounds would otherwise sit in `salvoLeft` forever and that mount would
            // never fire again for the rest of the game. Paid for is paid for.
            RunSalvos(u, m, dt);

            // ---- targeting ----
            m.retargetIn -= dt;
            bool needTarget = m.target == null || m.target.IsDestroyed || m.retargetIn <= 0f ||
                              !InRange(u, m.target, Weaponry.MaxRange(m.loadout));
            if (needTarget)
            {
                m.target = PickTarget(u, scratch, Weaponry.MaxRange(m.loadout));
                m.retargetIn = RetargetInterval;
            }

            // Both ends of the engagement, and BEFORE hold-fire can null the target: a squadron
            // slipping past something it cannot beat is still very much in a fight and should still be
            // flying like it.
            if (m.target != null) { engagedNext.Add(u); engagedNext.Add(m.target); }

            // ---- point defence works whether or not this ship has a target of its own ----
            RunPointDefence(u, m, dt);

            // HOLD FIRE. A squadron under this protocol does not shoot first — it is how a player slips
            // a fleet past something it cannot beat. It suppresses INITIATING only, and deliberately
            // sits AFTER point defence: declining to start a fight and declining to swat a missile
            // already in the air are not the same decision, and nobody choosing "do not provoke them"
            // is choosing to be hit. Ships outside a squadron are unaffected and fight on the ordinary
            // proximity rule, which is what everything did before squadrons existed.
            if (Squadrons.HoldingFire(u)) { m.target = null; continue; }

            if (m.target == null) continue;

            Vector3 from = PosOf(u), to = PosOf(m.target);
            Vector3 targetVel = UnitModelRenderer.VelocityOf(m.target);

            // ---- fire everything that is ready ----
            for (int k = 0; k < m.loadout.Length; k++)
            {
                var w = m.loadout[k];
                if (w == null || w.attackShare <= 0f) continue;      // point defence fires elsewhere
                if (m.cooldown[k] > 0f) continue;
                if (m.salvoLeft[k] > 0) continue;                    // still emptying the last pull
                if (Vector3.Distance(from, to) > w.range) continue;
                if (ProjectileRenderer.Instance != null &&
                    ProjectileRenderer.Instance.LiveCount >= ProjectileCeiling) break;

                // ---- can it afford to? --------------------------------------------------------
                //
                // Ahead of the cooldown being set, so a dry mount keeps trying every frame and opens
                // up the instant it is rearmed, rather than sitting out a reload it never paid for.
                if (!Magazines.CanFire(u, k, w)) continue;

                // ---- is the shot worth taking? ------------------------------------------------
                //
                // A round that provably cannot catch its target is not fired at all. For an energy
                // mount that is a small politeness; for a torpedo tube carrying four rounds it is the
                // difference between a weapon and a waste. Ballistics.CanReach is deliberately
                // optimistic — it declines only the genuinely hopeless.
                if (!Ballistics.CanReach(w, from, to, targetVel)) continue;

                m.cooldown[k] = w.cooldown;
                Magazines.Consume(u, k, w);

                m.salvoLeft[k] = Mathf.Max(1, w.salvo);
                m.salvoTimer[k] = 0f;                                // the first round goes now
                FireOne(u, m, k, w);
            }
        }

        // Swap rather than clear-and-refill, so a LateUpdate reader never catches a half-built set.
        var swap = engaged; engaged = engagedNext; engagedNext = swap;
    }

    /// Send the rounds still owed from trigger pulls already made.
    void RunSalvos(Unit u, Mounts m, float dt)
    {
        for (int k = 0; k < m.loadout.Length; k++)
        {
            if (m.salvoLeft[k] <= 0) continue;
            m.salvoTimer[k] -= dt;
            if (m.salvoTimer[k] > 0f) continue;
            FireOne(u, m, k, m.loadout[k]);
        }
    }

    /// One round out of one mount, with its firing solution worked out.
    ///
    /// This is where a shot stops being "the ship attacks" and becomes a piece of geometry. Three
    /// things happen in order, and the order is the whole model:
    ///
    ///   1. LEAD. Ballistics solves for where the target will be when the round gets there. An
    ///      unguided round is aimed at that point and nowhere else. A guided round does not need it
    ///      to hit — it works out its own intercept the whole way in — but it is handed the same
    ///      point as the place to coast toward if its seeker loses the target, which is a much better
    ///      guess than the target's present position.
    ///
    ///   2. DECLINE. No solution means the target is simply outrunning this weapon. The mount holds
    ///      its fire, and the round it did not waste is still in the magazine.
    ///
    ///   3. DISPERSE. The mount's own error cone is thrown at the perfect answer — bigger at range,
    ///      bigger against a target crossing fast. This is the only source of misses for a weapon
    ///      whose target is flying straight, and it is what stops perfect gunnery from meaning perfect
    ///      hit rates.
    void FireOne(Unit u, Mounts m, int k, WeaponInfo w)
    {
        m.salvoLeft[k]--;
        m.salvoTimer[k] = w.salvoSpacing;

        // The target died between one round of the salvo and the next. The rest of the salvo is
        // dropped rather than re-aimed: a rack that swung onto a fresh target mid-pull would let a
        // ship spend one reload killing two hulls, which is a free shot nobody authored.
        var target = m.target;
        if (target == null || target.IsDestroyed) { m.salvoLeft[k] = 0; return; }

        Vector3 from = PosOf(u);

        // Muzzle offset: shots leave the hull rather than its centre, so a ship firing several mounts
        // at once does not look like one gun stuttering.
        Vector3 muzzle = from + Random.insideUnitSphere * 0.35f;

        Vector3 tPos = PosOf(target);
        Vector3 tVel = UnitModelRenderer.VelocityOf(target);

        // Motor-aware, not cruise-speed: a missile spends most of a short engagement still building
        // speed, and a solution that pretends otherwise aims well short of where the target will be.
        Vector3 aim = Ballistics.LeadAimFor(w, muzzle, tPos, tVel, out bool solved, out _);
        if (!solved) { m.salvoLeft[k] = 0; return; }

        float spread = Ballistics.DispersionDegrees(w, muzzle, tPos, tVel);
        if (spread > 0.0001f)
        {
            Vector3 dir = aim - muzzle;
            float reach = dir.magnitude;
            if (reach > 0.001f)
                aim = muzzle + Ballistics.ApplyDispersion(dir / reach, spread) * reach;
        }

        ProjectileRenderer.Instance?.Fire(u, target, w, muzzle, aim, Weaponry.ShotDamage(u, w));
    }

    // ============================================================================================
    // TARGETING
    // ============================================================================================

    /// The most dangerous hostile in reach.
    ///
    /// Dangerous rather than nearest, and that is the interesting half. Nearest-target means a screen
    /// of cheap hulls in front of a capital ship absorbs everything aimed at it, which turns every
    /// fight into a contest of who brought more chaff. Threat-weighted means the escort has to actually
    /// KILL what it is screening against — so escorting works by winning the fight, not by standing in
    /// the way. Distance still breaks ties, so a ship does not ignore something shooting it in favour
    /// of a slightly scarier thing across the system.
    Unit PickTarget(Unit self, List<Unit> pool, float reach)
    {
        // ---- A DESIGNATED TARGET BEATS THE HEURISTIC -------------------------------------------
        //
        // But only while it is a target this ship can actually do something about. A focus order that
        // outlives its range is worse than no order: it would have the ship hold its fire at something
        // it cannot reach while a hostile shoots it in the back. So the override is checked, validated
        // against the same reach every other candidate is, and otherwise falls straight through to the
        // automatic behaviour below. See CombatOrders.
        var designated = CombatOrders.FocusFor(self);
        if (designated != null && Vector3.Distance(PosOf(self), PosOf(designated)) <= reach)
            return designated;

        Unit best = null;
        float bestScore = float.NegativeInfinity;
        Vector3 p = PosOf(self);

        for (int i = 0; i < pool.Count; i++)
        {
            var o = pool[i];
            if (o == self || o.IsDestroyed) continue;
            if (!AreHostile(self, o)) continue;

            float d = Vector3.Distance(p, PosOf(o));
            if (d > reach) continue;

            // Threat is what it can do to me, tempered by how hard it is to kill. An unarmed hull still
            // scores above zero so a transport alone in space is not ignored forever.
            float threat = o.EffectiveAttack * 1.0f + o.EffectiveHealth * 0.05f + 1f;
            float score = threat - d * 0.35f;
            if (score > bestScore) { bestScore = score; best = o; }
        }
        return best;
    }

    /// Do these two shoot at each other?
    ///
    /// Unowned hulls are the interesting case: a derelict probe or an ancient defence platform has no
    /// faction, and it is hostile to EVERYONE. That is what makes it a hazard of the map rather than a
    /// participant in the politics — nobody can ally with it and nobody has to declare war on it.
    public static bool AreHostile(Unit a, Unit b)
    {
        if (a == null || b == null) return false;
        if (a.owner == b.owner) return false;                 // includes two unowned hulls ignoring each other
        if (a.owner == null || b.owner == null) return true;  // ancient things hate everything that moves

        // Ally and Player are one side. Enemy is the other. Neutral shoots at nobody unprovoked, which
        // is the whole meaning of the word and keeps the Free Traders from being a war.
        bool aPlayerSide = a.owner.relation == FactionRelation.Player || a.owner.relation == FactionRelation.Ally;
        bool bPlayerSide = b.owner.relation == FactionRelation.Player || b.owner.relation == FactionRelation.Ally;
        bool aEnemy = a.owner.relation == FactionRelation.Enemy;
        bool bEnemy = b.owner.relation == FactionRelation.Enemy;

        return (aPlayerSide && bEnemy) || (aEnemy && bPlayerSide);
    }

    bool InRange(Unit a, Unit b, float reach)
        => a != null && b != null && Vector3.Distance(PosOf(a), PosOf(b)) <= reach;

    // ============================================================================================
    // POINT DEFENCE
    // ============================================================================================

    /// Sweep inbound rounds out of the air around this ship, and around anything friendly standing
    /// close enough to it.
    ///
    /// It used to protect the ship carrying it and nothing else, on the reasoning that area point
    /// defence makes one screening destroyer mandatory and every other hull interchangeable. That
    /// reasoning still holds and the screen is still PER HULL — but the flat version left a hole
    /// exactly where the game wanted a mechanic. A colony ship has no guns and no screen, and neither
    /// does a terraformer, a science vessel or a transport, because arming them would make them
    /// warships. So an escort could not actually protect the thing it was escorting: the torpedoes
    /// went straight past it into the hull beside it, and escorting was a formation and a protocol
    /// with no teeth.
    ///
    /// The limits that keep it from becoming a fleet-wide umbrella are in ProjectileRenderer
    /// .InterceptIncoming, which is where the geometry lives.
    void RunPointDefence(Unit u, Mounts m, float dt)
    {
        var pr = ProjectileRenderer.Instance;
        if (pr == null) return;

        for (int k = 0; k < m.loadout.Length; k++)
        {
            var w = m.loadout[k];
            if (w == null || w.cls != WeaponClass.PointDefence) continue;
            if (m.cooldown[k] > 0f) continue;

            // The screen draws on the capacitor like any other energy mount. Small, but not nothing:
            // a ship swatting missiles all engagement has a little less charge for its own guns, which
            // is the honest cost of being the hull everything is shooting at.
            if (!Magazines.CanFire(u, k, w)) continue;

            int killed = pr.InterceptIncoming(u, PosOf(u), w.range, 2);
            if (killed > 0)
            {
                m.cooldown[k] = w.cooldown;
                Magazines.Consume(u, k, w);
                // Deliberately no sound. There can be dozens of these a second in a real engagement,
                // and a battle that clicks like a Geiger counter is a battle nobody can hear.
            }
        }
    }

    // ============================================================================================
    // DAMAGE
    // ============================================================================================

    /// A round arrived. Called by ProjectileRenderer when a shot reaches its target, never by the
    /// firing loop — the delay between the two IS the projectile's flight.
    public void ResolveHit(Unit shooter, Unit target, WeaponInfo w, float damage, Vector3 at)
    {
        if (target == null || target.IsDestroyed || w == null) return;

        // Armour subtracts, penetration divides what it subtracts. So a heavy round shrugs off armour
        // that stops a light one entirely — which is why a swarm of pulse fighters cannot chew through
        // a dreadnought and one railgun cruiser can.
        float armour = target.Armor / Mathf.Max(0.05f, w.penetration);
        float dealt = Mathf.Max(damage * 0.1f, damage - armour);   // never fully absorbed; a hit is a hit

        target.Health -= dealt;

        ExplosionRenderer.Instance?.Impact(at, w.colour);
        // Scaled by what actually got through the armour, not by what was fired — a round that barely
        // scratches a dreadnought should not land with the same weight as one that hurts it.
        SimpleAudio.Instance?.PlayImpact(at, dealt);

        // Experience for landing it. `battles` is incremented on the KILL rather than per hit, in
        // Destroy below — a ship that fires a thousand rounds into one dreadnought fought one battle.
        shooter?.AddExperience(dealt * 0.02f);

        if (target.Health <= 0f) Destroy(target, shooter);
    }

    /// A dumb-fire round reached where it was aimed and the target had moved. Nothing happens — this
    /// exists so the renderer has somewhere to say so, and so a future accuracy statistic has a hook.
    public void ReportMiss(Unit shooter) { }

    /// A ship dies: it explodes, it is heard, it is removed, and whoever killed it is credited.
    void Destroy(Unit victim, Unit killer)
    {
        if (victim == null) return;

        Vector3 at = PosOf(victim);
        float size = HullScale(victim);

        ExplosionRenderer.Instance?.Death(at, size);
        SimpleAudio.Instance?.PlayShipDestroyed(size);

        if (killer != null)
        {
            killer.battles++;
            // A kill is worth a real step of experience, scaled by what was killed — cleaning up
            // probes should not make a Legendary dreadnought.
            killer.AddExperience(12f + victim.EffectiveHealth * 0.05f + victim.EffectiveAttack * 0.4f);
        }

        bool mine = victim.owner == FactionManager.Player;
        string what = victim.Info != null ? victim.Info.name : "vessel";

        // RemoveUnit, not DestroyUnit: the latter is the SCRAP path and announces a self-destruct.
        // Any standing order aimed at it, or given to it, goes with it. Swept here rather than
        // periodically, because a dictionary keyed on a dead Unit is exactly the leak CombatOrders'
        // header warns about.
        CombatOrders.Forget(victim);

        UnitManager.Instance?.RemoveUnit(victim);

        if (mine)
        {
            NotificationManager.Instance?.Push($"{victim.name} destroyed",
                $"Your {what} was lost in action.", null, NotifKind.Danger);
        }
        else if (killer != null && killer.owner == FactionManager.Player)
        {
            NotificationManager.Instance?.Push($"{victim.name} destroyed",
                $"{killer.name} destroyed a hostile {what}.", null, NotifKind.Victory);
        }
    }

    // ============================================================================================
    // HELPERS
    // ============================================================================================

    Mounts MountsFor(Unit u)
    {
        if (mounts.TryGetValue(u, out var m) && m.loadout != null) return m;
        var loadout = Weaponry.For(u.type);
        m = new Mounts
        {
            loadout = loadout,
            cooldown = new float[loadout.Length],
            salvoLeft = new int[loadout.Length],
            salvoTimer = new float[loadout.Length],
        };
        mounts[u] = m;
        return m;
    }

    /// Where a ship is, for aiming. Falls back to its body or park position when it has no drawn
    /// transform — a ship the camera has de-rendered still fights.
    public static Vector3 PosOf(Unit u)
    {
        if (u == null) return Vector3.zero;
        var t = UnitVisuals.TransformOf(u);
        if (t != null) return t.position;
        if (u.location != null && u.location.visualObject != null) return u.location.visualObject.transform.position;
        return u.parkPosition;
    }

    /// Roughly how big this hull is drawn, so its explosion is proportional to it.
    static float HullScale(Unit u)
    {
        if (u == null) return 0.6f;
        var info = u.Info;
        if (info == null) return 0.6f;
        // Health is the only size-like number every hull has, and it tracks the classes' physical
        // scale closely enough for an explosion: a scout is a few hundred, a dreadnought thousands.
        return Mathf.Clamp(0.35f + info.health / 900f, 0.4f, 5f);
    }
}
