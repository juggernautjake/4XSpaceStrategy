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
//   3. Fire every mount that has come off cooldown, once each.
//   4. Let point-defence mounts swat down whatever is inbound.
//
// Damage does not land here. A shot is handed to ProjectileRenderer, which flies it and calls back
// into ResolveHit when it arrives — so a missile crossing a gap really is in the air for that time,
// and a target that dies first eats none of the volley already aimed at it.
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
    }

    readonly Dictionary<Unit, Mounts> mounts = new Dictionary<Unit, Mounts>();
    readonly List<Unit> scratch = new List<Unit>();

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

        for (int i = 0; i < scratch.Count; i++)
        {
            var u = scratch[i];
            if (u.IsDestroyed) continue;

            var m = MountsFor(u);
            if (m.loadout.Length == 0) continue;

            // ---- cooldowns ----
            for (int k = 0; k < m.cooldown.Length; k++)
                if (m.cooldown[k] > 0f) m.cooldown[k] -= dt;

            // ---- targeting ----
            m.retargetIn -= dt;
            bool needTarget = m.target == null || m.target.IsDestroyed || m.retargetIn <= 0f ||
                              !InRange(u, m.target, Weaponry.MaxRange(m.loadout));
            if (needTarget)
            {
                m.target = PickTarget(u, scratch, Weaponry.MaxRange(m.loadout));
                m.retargetIn = RetargetInterval;
            }

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

            // ---- fire everything that is ready ----
            for (int k = 0; k < m.loadout.Length; k++)
            {
                var w = m.loadout[k];
                if (w == null || w.attackShare <= 0f) continue;      // point defence fires elsewhere
                if (m.cooldown[k] > 0f) continue;
                if (Vector3.Distance(from, to) > w.range) continue;
                if (ProjectileRenderer.Instance != null &&
                    ProjectileRenderer.Instance.LiveCount >= ProjectileCeiling) break;

                m.cooldown[k] = w.cooldown;

                // Muzzle offset: shots leave the hull rather than its centre, so a ship firing several
                // mounts at once does not look like one gun stuttering.
                Vector3 muzzle = from + Random.insideUnitSphere * 0.35f;

                ProjectileRenderer.Instance?.Fire(u, m.target, w, muzzle, to, Weaponry.ShotDamage(u, w));
            }
        }
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

    /// Sweep inbound rounds out of the air around this ship.
    ///
    /// It protects the SHIP CARRYING IT and nothing else. Area point defence — a destroyer screening
    /// the whole formation — sounds generous and plays badly: it makes one ship in the fleet mandatory
    /// and the rest interchangeable. Per-hull means a screen is something you build INTO a formation
    /// by choosing hulls, which is the decision worth having.
    void RunPointDefence(Unit u, Mounts m, float dt)
    {
        var pr = ProjectileRenderer.Instance;
        if (pr == null) return;

        for (int k = 0; k < m.loadout.Length; k++)
        {
            var w = m.loadout[k];
            if (w == null || w.cls != WeaponClass.PointDefence) continue;
            if (m.cooldown[k] > 0f) continue;

            int killed = pr.InterceptIncoming(u, PosOf(u), w.range, 2);
            if (killed > 0)
            {
                m.cooldown[k] = w.cooldown;
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
        m = new Mounts { loadout = loadout, cooldown = new float[loadout.Length] };
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
