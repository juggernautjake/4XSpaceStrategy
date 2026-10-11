using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// SHOTS IN FLIGHT
//
// Every round fired in the game is one entry in one list, updated by one loop, drawn by pooled quads,
// lines and trails. No Instantiate in the firing path and no allocation per frame once warm.
//
// This file OWNS the flight and the arrival. It does not own the maths — Ballistics does — and the
// split is deliberate: everything that decides where a round goes is in a file with no Unity in it that
// a Node script can mirror (tools/ballistics-check.mjs). Everything here is bookkeeping and drawing.
//
// ---- WHAT EACH WEAPON LOOKS LIKE ------------------------------------------------------------------
//
//   BEAM LASER   a charge-up glint at the muzzle, then a held beam: a white-hot core inside a soft
//                coloured glow, its width flickering, tracking the target for its short life, with a
//                scorch spark where it lands.
//   RAILGUN      a bigger capacitor glint, a needle-thin white flash, and an ion trail that lingers,
//                widens and fades along the slug's path. The heaviest muzzle flash and recoil.
//   PULSE LASER  short bright bolts — a streak with a fading ghost trail behind it.
//   PLASMA       a swollen, breathing ball of gas: a soft halo round a hot streak, and a long trail.
//   AUTOCANNON   rapid thin tracers.
//   MISSILE      a small body with a bright motor and an exhaust trail, weaving slightly; brighter in
//                its boost phase. A puff of smoke at launch.
//   TORPEDO      a big pulsing warhead glow with a thick trail and a slow weave.
//   POINT DEF.   no projectile of its own: when it catches a round, a burst of rapid tracers flicks out
//                from the mount, and the round goes up as a pop (light rounds) or a FLAK burst with a
//                hanging puff of smoke (plasma, torpedoes).
//
// Every round is drawn in RaceVfx.WeaponColour — the class colour leaned toward the shooter's race — and
// leaves from the shooter's real gun hardpoint (CombatManager asks ShipLights.TryMuzzle). Every size has
// a floor in PIXELS (FxKit.Floor) so a fight stays readable at system zoom.
//
// ---- SHIELDS --------------------------------------------------------------------------------------
//
// A round that meets a shield is stopped by it: a beam is drawn to the shell, not to the hull
// (ShipLights.ShieldContact), and CombatManager.ResolveHit puts the splash and the ripple on the
// bubble. Only what the shield could not hold reaches the hull.
//
// ---- INSTANT SHOTS LET GO AFTER THEIR CHARGE ---------------------------------------------------------
//
// Hit or miss is still decided at the trigger, by the same dispersed aim point. The DAMAGE lands when the
// beam does — 0.06 s later for a beam laser, 0.10 s for a railgun — so the impact flash and the beam
// arrive together instead of the hull flashing before anything has reached it. A target that dies
// during the charge is not hit twice.
//
// ---- TWO OLD BUGS STILL FIXED HERE -----------------------------------------------------------------
//
// 1. Instant weapons dealt no damage at all (ResolveHit sat behind an early `continue`).
// 2. Fast rounds tunnelled through their targets with a point test. Every hit test is a SEGMENT test
//    against the path the round actually swept, which cannot tunnel at any frame rate.
//
// ---- FOG OF WAR -----------------------------------------------------------------------------------
//
// A round fired by a ship the player is not shown is simulated and never drawn (Shot.hidden): no bolt,
// beam, trail, light, glint, muzzle flash or launch sound. The damage it does to a visible target is
// still seen, because CombatManager keys impacts on the TARGET.
//
// ---- GALAXY ZOOM ----------------------------------------------------------------------------------
//
// MapTierVisibility switches this object's children off at galaxy zoom and back ON on return, so pooled
// pieces are retired by switching off their RENDERER, never their GameObject — a re-activated pool must
// not bring every retired bolt back for a frame.
// ============================================================================================
public class ProjectileRenderer : MonoBehaviour
{
    public static ProjectileRenderer Instance;

    enum Style { Beam, Rail, Bolt, Plasma, Tracer, Missile, Torpedo }

    /// A round in flight, or the afterimage of an instant one.
    class Shot
    {
        public WeaponInfo weapon;
        public Style style;
        public Unit shooter;                 // for credit on the kill; may die mid-flight
        public Unit target;                  // may die mid-flight, leaving the round to fly on
        public Vector3 pos, vel;
        public Vector3 aimPoint;             // where an unguided round is going; the fallback for a guided one
        public float damage;
        public float age, maxLife;
        public bool instant;
        public bool hasLock;                 // a guided round that has lost its target never gets it back
        public float phaseOffset;            // so a volley pulses and weaves out of step
        public Color colour;                 // RaceVfx.WeaponColour, fixed at Fire
        public bool hidden;                  // fired by a ship the player is not shown

        // ---- instant shots ----
        public float charge;                 // seconds from trigger to discharge
        public bool willHit, discharged;
        public float dischargedAt, showFor;
        public Vector3 end;

        // ---- drawing ----
        public Renderer body, ghost;         // streak quads
        public Renderer halo;                // dot quad
        public LineRenderer glowLine, coreLine;
        public TrailRenderer trail;
        public Light light;
    }

    readonly List<Shot> shots = new List<Shot>();
    readonly Stack<Shot> shotPool = new Stack<Shot>();
    readonly Stack<Renderer> streakPool = new Stack<Renderer>();
    readonly Stack<Renderer> dotPool = new Stack<Renderer>();
    readonly Stack<LineRenderer> linePool = new Stack<LineRenderer>();
    readonly Stack<TrailRenderer> trailPool = new Stack<TrailRenderer>();
    readonly Stack<Light> lightPool = new Stack<Light>();

    struct Fading { public TrailRenderer trail; public float until; }
    readonly List<Fading> fadingTrails = new List<Fading>();

    /// Point-defence tracer dashes.
    class Tracer { public LineRenderer line; public Vector3 a, b; public float life, maxLife; public Color colour; }
    readonly List<Tracer> tracers = new List<Tracer>();
    readonly Stack<Tracer> tracerPool = new Stack<Tracer>();
    int pdCursor;

    public static void Create()
    {
        if (Instance != null) return;
        var go = new GameObject("ProjectileRenderer");
        go.AddComponent<ProjectileRenderer>();
        // Shots vanish at galaxy zoom along with the hulls firing them — see MapTierVisibility.
        go.AddComponent<MapTierVisibility>();
    }

    void Awake() { Instance = this; }

    /// How many rounds are in the air. The combat manager uses this to stop a pathological fight from
    /// filling the list.
    public int LiveCount => shots.Count;

    static Style StyleOf(WeaponInfo w)
    {
        switch (w.cls)
        {
            case WeaponClass.BeamLaser: return Style.Beam;
            case WeaponClass.Railgun: return Style.Rail;
            case WeaponClass.PlasmaCannon: return Style.Plasma;
            case WeaponClass.Autocannon: return Style.Tracer;
            case WeaponClass.Missile: return Style.Missile;
            case WeaponClass.Torpedo: return Style.Torpedo;
            case WeaponClass.PointDefence: return Style.Tracer;
            default: return w.IsInstant ? Style.Beam : w.IsGuided ? Style.Missile : Style.Bolt;
        }
    }

    /// How big the muzzle flash is, 1 = a light gun.
    static float MuzzleSize(Style s)
    {
        switch (s)
        {
            case Style.Rail: return 2.6f;
            case Style.Plasma: return 2.0f;
            case Style.Torpedo: return 1.8f;
            case Style.Missile: return 1.2f;
            case Style.Tracer: return 0.7f;
            default: return 1f;
        }
    }

    // ============================================================================================
    // FIRING
    // ============================================================================================

    /// Fire one round. `aimPoint` is where the shot is GOING (CombatManager has already led the target
    /// and thrown the mount's dispersion at it); `from` is the muzzle hardpoint.
    public void Fire(Unit shooter, Unit target, WeaponInfo w, Vector3 from, Vector3 aimPoint, float damage)
    {
        if (w == null) return;

        // Pooled: a fleet action fires hundreds of rounds a second, and a Gen-0 collection is a hitch.
        // Every field is written, including the ones a fresh object would have at zero.
        var s = shotPool.Count > 0 ? shotPool.Pop() : new Shot();
        s.weapon = w; s.style = StyleOf(w);
        s.shooter = shooter; s.target = target;
        s.pos = from; s.vel = Vector3.zero; s.aimPoint = aimPoint; s.damage = damage;
        s.age = 0f; s.maxLife = 0f;
        s.instant = w.IsInstant;
        s.hasLock = target != null && !target.IsDestroyed;
        s.phaseOffset = Random.Range(0f, Mathf.PI * 2f);
        s.colour = RaceVfx.WeaponColour(shooter, w.colour);
        s.hidden = !VisibilityService.ShownToPlayer(shooter);
        s.charge = 0f; s.willHit = false; s.discharged = false; s.dischargedAt = 0f; s.showFor = 0f;
        s.end = aimPoint;
        s.body = null; s.ghost = null; s.halo = null; s.glowLine = null; s.coreLine = null; s.trail = null; s.light = null;

        if (s.instant) FireInstant(s, from, aimPoint);
        else           FireTravelling(s, w, from, aimPoint);

        // A real light on the round (capped; see RentLight). An instant shot's stays dark until it lets go.
        if (!s.hidden && FxKit.SystemView)
        {
            s.light = RentLight();
            if (s.light != null)
            {
                s.light.color = s.colour;
                s.light.range = Mathf.Clamp(w.width * 26f + w.glow * 1.6f, 0.6f, 6f);
                s.light.intensity = s.instant ? 0f : Mathf.Clamp(w.glow * 2.2f, 0.6f, 7f);
                s.light.transform.position = from;
                s.light.enabled = true;
            }
        }

        shots.Add(s);
        if (s.hidden) return;   // nothing below is anything but appearance

        var audio = SimpleAudio.Instance;
        if (audio != null) audio.PlayWeapon(w.cls, from);

        var er = ExplosionRenderer.Instance;
        if (s.instant)
        {
            // The charge: a glint swelling on the muzzle until the beam lets go.
            if (er != null) er.Glint(from, s.colour, s.style == Style.Rail ? 0.55f : 0.32f, s.charge + 0.05f);
        }
        else
        {
            // The muzzle lights up on the SAME call that creates the round, so they can never drift apart.
            MuzzleFlash(shooter, from, s.colour, aimPoint - from, MuzzleSize(s.style));
            if ((s.style == Style.Missile || s.style == Style.Torpedo) && er != null)
                er.Smoke(from, s.style == Style.Torpedo ? 0.22f : 0.14f, new Color(0.55f, 0.53f, 0.5f), 0.35f, 0.7f);
        }
    }

    /// The flash on the hull's own gun port if the shooter is drawn as a mesh; a glint in space if it is
    /// a token.
    static void MuzzleFlash(Unit shooter, Vector3 at, Color colour, Vector3 dir, float size)
    {
        var umr = UnitModelRenderer.Instance;
        var rig = umr != null ? umr.LightsOf(shooter) : null;
        if (rig != null) { rig.FlashMuzzle(at, colour, dir, size); return; }
        var er = ExplosionRenderer.Instance;
        if (er != null) er.Glint(at, colour, 0.18f * size, 0.08f);
    }

    /// A beam or a slug. Hit or miss is decided here, by the dispersed aim point; the damage lands at
    /// discharge (see the header).
    void FireInstant(Shot s, Vector3 from, Vector3 aimPoint)
    {
        var w = s.weapon;
        bool alive = s.target != null && !s.target.IsDestroyed;
        Vector3 hullPos = alive ? CombatManager.PosOf(s.target) : aimPoint;

        s.willHit = alive && Vector3.Distance(aimPoint, hullPos) <= w.hitRadius;
        // A miss carries past the aim point: a beam that stops dead in empty space level with its target
        // reads as a hit that failed to register.
        s.end = s.willHit ? hullPos : from + (aimPoint - from) * 1.12f;

        bool rail = s.style == Style.Rail;
        s.charge = rail ? 0.10f : 0.06f;
        s.showFor = rail ? 0.42f : 0.20f;
        s.maxLife = s.charge + s.showFor;
    }

    /// Anything with a flight time. A guided round gets a deliberately bad launch (the cold launch).
    void FireTravelling(Shot s, WeaponInfo w, Vector3 from, Vector3 aimPoint)
    {
        Vector3 dir = aimPoint - from;
        dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;

        // ---- the cold launch: ejected sideways WITHIN the plane of play, then hauled round by
        // proportional navigation. A yaw about world up, not a pitch — see tools/ballistics-check.mjs.
        if (w.launchArcDeg > 0.01f)
        {
            float side = Random.value < 0.5f ? -1f : 1f;
            dir = Quaternion.AngleAxis(w.launchArcDeg * side, Vector3.up) * dir;
            Vector3 pitchAxis = Vector3.Cross(dir, Vector3.up);
            if (pitchAxis.sqrMagnitude > 1e-6f)
                dir = Quaternion.AngleAxis(Random.Range(-7f, 7f), pitchAxis.normalized) * dir;
        }

        s.vel = dir * Ballistics.SpeedAt(w, 0f);
        s.maxLife = w.Lifetime;

        if (s.hidden) return;   // flown, never drawn

        switch (s.style)
        {
            case Style.Plasma:
                s.halo = Rent(dotPool, FxKit.Dot());
                s.body = Rent(streakPool, FxKit.StreakTex());
                s.ghost = Rent(streakPool, FxKit.StreakTex());
                break;
            case Style.Missile:
            case Style.Torpedo:
                s.halo = Rent(dotPool, FxKit.Dot());
                s.body = Rent(streakPool, FxKit.StreakTex());
                s.trail = RentTrail(from, s.style == Style.Torpedo ? 0.9f : 0.6f);
                break;
            default: // Bolt, Tracer
                s.body = Rent(streakPool, FxKit.StreakTex());
                s.ghost = Rent(streakPool, FxKit.StreakTex());
                break;
        }
        DrawTravelling(s);
    }

    // ============================================================================================
    // THE ONE LOOP
    // ============================================================================================
    void Update()
    {
        // Frame time: a shot's flight is an ANIMATION. Pausing stops CombatManager from firing; a round
        // already in the air freezes with the clock.
        float dt = Time.deltaTime;

        TickTrailsAndTracers(dt);
        if (dt <= 0f) return;

        for (int i = shots.Count - 1; i >= 0; i--)
        {
            var s = shots[i];
            s.age += dt;

            bool keep = s.instant ? TickInstant(s) : TickTravelling(s, dt);
            if (!keep)
            {
                // A shot can be removed from inside the tick (a kill clears nothing here, but be safe).
                if (i < shots.Count && shots[i] == s) shots.RemoveAt(i);
                else shots.Remove(s);
                Retire(s);
            }
        }
    }

    // ---- instant ---------------------------------------------------------------------------------
    bool TickInstant(Shot s)
    {
        if (!s.discharged)
        {
            if (s.age < s.charge) return true;   // still charging; the glint is ExplosionRenderer's
            Discharge(s);
        }

        float t = Mathf.Clamp01((s.age - s.dischargedAt) / Mathf.Max(0.01f, s.showFor));
        float k = 1f - t;

        // A held beam stays on its target for its short life.
        if (s.style == Style.Beam && s.willHit && s.target != null && !s.target.IsDestroyed)
            s.end = Contact(s.target, s.pos, CombatManager.PosOf(s.target));

        if (s.glowLine != null && s.coreLine != null)
        {
            var w = s.weapon;
            Vector3 mid = (s.pos + s.end) * 0.5f;
            float wpp = FxKit.WorldPerPixel(mid);
            s.glowLine.SetPosition(0, s.pos); s.glowLine.SetPosition(1, s.end);
            s.coreLine.SetPosition(0, s.pos); s.coreLine.SetPosition(1, s.end);

            if (s.style == Style.Rail)
            {
                // A needle of white for an instant, then an ion trail that widens and fades.
                float flash = Mathf.Clamp01(1f - (s.age - s.dischargedAt) / 0.07f);
                s.coreLine.widthMultiplier = Mathf.Max(w.width * 0.45f, 1.2f * wpp);
                FxKit.SetColour(s.coreLine, new Color(3f, 3f, 3.2f, flash));
                s.glowLine.widthMultiplier = Mathf.Max(w.width * (1.2f + 3.5f * t), 2f * wpp);
                Color g = Color.Lerp(s.colour * (w.glow * 1.4f), s.colour * 0.5f, t);
                g.a = k * k * 0.85f;
                FxKit.SetColour(s.glowLine, g);
            }
            else
            {
                // Width flicker: a beam is a held thing, and a held thing is never perfectly steady.
                float flick = 0.72f + 0.28f * Mathf.PerlinNoise(s.age * 38f, s.phaseOffset);
                float fadeIn = Mathf.Clamp01((s.age - s.dischargedAt) / 0.03f);
                float a = fadeIn * Mathf.Clamp01(k * 1.6f);
                s.glowLine.widthMultiplier = Mathf.Max(w.width * 2.8f * flick, 3f * wpp);
                Color g = s.colour * (w.glow * 1.15f); g.a = a * 0.85f;
                FxKit.SetColour(s.glowLine, g);
                s.coreLine.widthMultiplier = Mathf.Max(w.width * 0.7f * flick, 1.2f * wpp);
                Color c = Color.Lerp(s.colour, Color.white, 0.7f) * 2.6f; c.a = a;
                FxKit.SetColour(s.coreLine, c);
            }
        }

        if (s.light != null)
        {
            s.light.transform.position = Vector3.Lerp(s.pos, s.end, 0.5f);
            s.light.intensity = Mathf.Clamp(s.weapon.glow * 2.2f, 0.6f, 7f) * k;
        }
        return s.age < s.maxLife;
    }

    void Discharge(Shot s)
    {
        s.discharged = true;
        s.dischargedAt = s.age;
        var w = s.weapon;
        bool alive = s.target != null && !s.target.IsDestroyed;
        var cm = CombatManager.Instance;

        if (s.willHit && alive)
        {
            Vector3 hullPos = CombatManager.PosOf(s.target);
            // Where it really lands: the shield shell if one is up, else the side of the hull facing us.
            s.end = Contact(s.target, s.pos, hullPos);
            if (cm != null) cm.ResolveHit(s.shooter, s.target, w, s.damage, hullPos, s.pos);
        }
        else
        {
            s.willHit = false;
            if (cm != null) cm.ReportMiss(s.shooter);
        }

        if (s.hidden || !FxKit.SystemView) return;

        s.glowLine = RentLine();
        s.coreLine = RentLine();
        MuzzleFlash(s.shooter, s.pos, s.colour, s.end - s.pos, MuzzleSize(s.style));

        // The scorch: a hot spark spray and a glint where the beam bites.
        var er = ExplosionRenderer.Instance;
        if (er != null && s.willHit)
        {
            Vector3 back = (s.pos - s.end).normalized;
            int n = s.style == Style.Rail ? 6 : 3;
            for (int i = 0; i < n; i++)
                er.Spark(s.end, (Random.onUnitSphere + back).normalized * Random.Range(2f, 5f),
                         Color.Lerp(s.colour, new Color(1f, 0.85f, 0.5f), 0.5f), Random.Range(0.15f, 0.3f), w.width * 0.6f);
            er.Glint(s.end, s.colour, s.style == Style.Rail ? 0.5f : 0.3f, 0.12f);
        }
    }

    /// Where a round from `from` meets `target`: its shield shell if one is up, else its hull's skin.
    static Vector3 Contact(Unit target, Vector3 from, Vector3 hullPos)
    {
        var umr = UnitModelRenderer.Instance;
        var rig = umr != null ? umr.LightsOf(target) : null;
        if (rig == null) return hullPos;
        if (rig.ShieldContact(from, out Vector3 p)) return p;
        return rig.HullContact(from);
    }

    // ---- travelling ------------------------------------------------------------------------------
    bool TickTravelling(Shot s, float dt)
    {
        var w = s.weapon;
        Vector3 was = s.pos;

        bool targetAlive = s.target != null && !s.target.IsDestroyed;
        Vector3 tPos = targetAlive ? CombatManager.PosOf(s.target) : s.aimPoint;
        Vector3 tVel = targetAlive ? UnitModelRenderer.VelocityOf(s.target) : Vector3.zero;
        if (!targetAlive) s.hasLock = false;

        if (w.IsGuided)
        {
            // The seeker is checked BEFORE the step, caged for the first moment so the cold launch does
            // not break its own lock. Once lost, never regained.
            if (s.hasLock && !Ballistics.SeekerHasLock(w, s.age, s.vel, s.pos, tPos))
                s.hasLock = false;
            s.vel = Ballistics.StepGuided(w, s.pos, s.vel, tPos, tVel, s.aimPoint, s.age, s.hasLock, dt);
        }
        else if (w.wanderDeg > 0.01f)
        {
            // A bottled ball of plasma wanders: Perlin, seeded per shot, small enough to be flavour.
            float t = s.age * 0.9f + s.phaseOffset;
            float yaw   = (Mathf.PerlinNoise(t, 0.13f) - 0.5f) * 2f;
            float pitch = (Mathf.PerlinNoise(0.71f, t) - 0.5f) * 2f;
            Vector3 axis = Vector3.Cross(s.vel, Vector3.up);
            if (axis.sqrMagnitude < 1e-6f) axis = Vector3.right;
            s.vel = Quaternion.AngleAxis(yaw * w.wanderDeg * dt, Vector3.up) *
                    (Quaternion.AngleAxis(pitch * w.wanderDeg * dt, axis.normalized) * s.vel);
        }

        s.pos += s.vel * dt;

        // Against the SEGMENT it just swept — a point test makes frame rate a weapon stat.
        if (targetAlive && SegmentDistance(was, s.pos, tPos) <= w.hitRadius)
        {
            var cm = CombatManager.Instance;
            if (cm != null) cm.ResolveHit(s.shooter, s.target, w, s.damage, tPos, was);
            return false;
        }

        // An unguided round that reached where it was aimed is spent: "no tracking".
        if (!w.IsGuided && SegmentDistance(was, s.pos, s.aimPoint) <= w.hitRadius)
        {
            var cm = CombatManager.Instance;
            if (cm != null) cm.ReportMiss(s.shooter);
            return false;
        }

        DrawTravelling(s);
        return s.age < s.maxLife;
    }

    /// Everything about a round in flight that is purely appearance.
    void DrawTravelling(Shot s)
    {
        var w = s.weapon;
        float fade = Mathf.Clamp01((s.maxLife - s.age) / Ballistics.DissipateTime);
        // Boost is BRIGHT: a missile lighting its motor is the most readable event in a volley.
        float boost = (w.boostTime > 0.01f && s.age < w.boostTime) ? 1.55f : 1f;
        // Plasma and torpedo warheads breathe on the fleet beat; the per-shot offset de-syncs a volley.
        float pulse = (s.style == Style.Plasma || s.style == Style.Torpedo)
            ? 0.82f + 0.18f * Mathf.Sin(FleetClock.Beats * Mathf.PI * 4f + s.phaseOffset)
            : 1f;

        Vector3 dir = s.vel.sqrMagnitude > 1e-6f ? s.vel.normalized : Vector3.forward;
        Vector3 p = s.pos;

        // ---- the weave, drawing only: a missile's motor is never perfectly steady ----
        if (s.style == Style.Missile || s.style == Style.Torpedo)
        {
            float amp = (s.style == Style.Missile ? 0.09f : 0.05f) * Mathf.Clamp01(s.age / 0.4f);
            float freq = s.style == Style.Missile ? 9f : 4.5f;
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
            p += side.normalized * (Mathf.Sin(s.age * freq + s.phaseOffset) * amp);
        }

        float wpp = FxKit.WorldPerPixel(p);
        float speed = s.vel.magnitude;
        Quaternion along = FxKit.Streak(dir);

        // Per-style proportions: body width/length multipliers, ghost (trail) length, halo size.
        float bodyW = 2f, bodyL = 1f, ghostL = 0f, haloK = 0f, minLenPx = 6f;
        switch (s.style)
        {
            case Style.Bolt:    bodyW = 2.0f; bodyL = 1.0f; ghostL = 2.4f; minLenPx = 7f; break;
            case Style.Tracer:  bodyW = 1.6f; bodyL = 1.0f; ghostL = 1.6f; minLenPx = 5f; break;
            case Style.Plasma:  bodyW = 1.4f; bodyL = 0.8f; ghostL = 3.0f; haloK = 3.2f; minLenPx = 6f; break;
            case Style.Missile: bodyW = 1.4f; bodyL = 0.55f; haloK = 1.8f; minLenPx = 4f; break;
            case Style.Torpedo: bodyW = 1.5f; bodyL = 0.5f; haloK = 3.4f; minLenPx = 4f; break;
        }

        float bright = w.glow * boost * pulse;

        if (s.body != null)
        {
            float len = Mathf.Max(w.length * bodyL, minLenPx * wpp);
            float wid = Mathf.Max(w.width * bodyW * pulse, 1.6f * wpp);
            var t = s.body.transform;
            t.position = p;
            t.rotation = along;
            t.localScale = new Vector3(wid, len, 1f);
            Color c = Color.Lerp(s.colour, Color.white, 0.35f) * (bright * 1.6f);
            c.a = fade;
            FxKit.SetColour(s.body, c);
        }

        if (s.ghost != null)
        {
            // A trail that grows out of the muzzle up to its full length, then follows the round.
            float full = Mathf.Max(w.length * ghostL, minLenPx * 2f * wpp);
            float len = Mathf.Min(full, s.age * speed);
            if (len > 1e-3f)
            {
                var t = s.ghost.transform;
                t.position = p - dir * (len * 0.5f + w.length * bodyL * 0.25f);
                t.rotation = along;
                t.localScale = new Vector3(Mathf.Max(w.width * bodyW * 0.75f, 1.2f * wpp), len, 1f);
                Color c = s.colour * (bright * 0.8f);
                c.a = fade * 0.55f;
                FxKit.SetColour(s.ghost, c);
                if (!s.ghost.enabled) s.ghost.enabled = true;
            }
            else if (s.ghost.enabled) s.ghost.enabled = false;
        }

        if (s.halo != null)
        {
            float size = Mathf.Max(w.width * haloK * pulse * (boost > 1f ? 1.25f : 1f), 4f * wpp);
            var t = s.halo.transform;
            t.position = p;
            t.rotation = FxKit.Billboard(0f);
            t.localScale = new Vector3(size, size, 1f);
            Color c = s.colour * (bright * (s.style == Style.Missile ? 1.8f : 1.3f));
            c.a = fade * 0.9f;
            FxKit.SetColour(s.halo, c);
        }

        if (s.trail != null)
        {
            s.trail.transform.position = p;
            s.trail.widthMultiplier = Mathf.Max(w.width * (s.style == Style.Torpedo ? 0.9f : 0.6f), 1.5f * wpp);
            Color c = Color.Lerp(s.colour, new Color(0.7f, 0.7f, 0.75f), 0.35f) * (boost > 1f ? 1.4f : 0.9f);
            c.a = fade * 0.75f;
            FxKit.SetColour(s.trail, c);
        }

        if (s.light != null)
        {
            s.light.transform.position = p;
            s.light.intensity = Mathf.Clamp(w.glow * 2.2f, 0.6f, 7f) * pulse * boost * fade;
        }
    }

    /// Distance from a point to the segment ab. The whole reason hits are reliable at any frame rate.
    static float SegmentDistance(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 1e-8f) return Vector3.Distance(a, p);
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
        return Vector3.Distance(a + ab * t, p);
    }

    // ============================================================================================
    // THE SCREEN — point defence
    //
    // A mount reaches for its neighbours as well as itself, with three limits that keep it from being
    // a fleet-wide umbrella: it reaches less far for somebody else (EscortRangeFraction), it looks after
    // itself first (rounds aimed at its own hull are swept first), and it is still per hull.
    // ============================================================================================

    const float EscortRangeFraction = 0.62f;

    /// Delete rounds a point-defence mount managed to catch. Returns how many were destroyed.
    public int InterceptIncoming(Unit owner, Vector3 screenCentre, float radius, int maxKills)
    {
        int killed = Sweep(owner, owner, screenCentre, radius, maxKills);
        if (killed >= maxKills) return killed;

        float escortRadius = radius * EscortRangeFraction;
        for (int i = shots.Count - 1; i >= 0 && killed < maxKills; i--)
        {
            var s = shots[i];
            if (!Catchable(s)) continue;
            if (s.target == null || s.target == owner || s.target.IsDestroyed) continue;
            // Only for hulls on the same side, so a mount can never shoot down a round aimed at an enemy.
            if (owner == null || s.target.owner != owner.owner) continue;
            if (Vector3.Distance(s.pos, screenCentre) > escortRadius) continue;

            Intercepted(owner, s);
            shots.RemoveAt(i); Retire(s); killed++;
        }
        return killed;
    }

    int Sweep(Unit owner, Unit protectedUnit, Vector3 centre, float radius, int maxKills)
    {
        int killed = 0;
        for (int i = shots.Count - 1; i >= 0 && killed < maxKills; i--)
        {
            var s = shots[i];
            if (!Catchable(s)) continue;
            if (s.target != protectedUnit) continue;
            if (Vector3.Distance(s.pos, centre) > radius) continue;

            Intercepted(owner, s);
            shots.RemoveAt(i); Retire(s); killed++;
        }
        return killed;
    }

    /// A round caught: tracers from the mount, and a pop or a flak burst where it died.
    void Intercepted(Unit owner, Shot s)
    {
        if (!FxKit.SystemView) return;
        bool ownerShown = owner != null && VisibilityService.ShownToPlayer(owner);
        if (s.hidden && !ownerShown) return;

        if (ownerShown) PdTracers(owner, s.pos);

        var er = ExplosionRenderer.Instance;
        if (er == null) return;
        bool heavy = s.weapon != null && (s.weapon.cls == WeaponClass.PlasmaCannon || s.weapon.cls == WeaponClass.Torpedo);
        if (heavy) er.Flak(s.pos, Color.Lerp(s.colour, new Color(1f, 0.85f, 0.6f), 0.4f));
        else er.Burst(s.pos, 0.35f, s.colour);
    }

    /// A short burst of rapid tracers from the point-defence mount to the round it caught.
    void PdTracers(Unit owner, Vector3 at)
    {
        var umr = UnitModelRenderer.Instance;
        var rig = umr != null ? umr.LightsOf(owner) : null;
        Vector3 from;
        if (rig == null || !rig.TryMuzzle(at, pdCursor++, out from)) from = CombatManager.PosOf(owner);
        Color c = RaceVfx.WeaponColour(owner, Weaponry.PointDefence.colour);
        float spread = Mathf.Max(0.05f, Vector3.Distance(from, at) * 0.03f);
        for (int i = 0; i < 3; i++)
        {
            var tr = tracerPool.Count > 0 ? tracerPool.Pop() : new Tracer();
            if (tr.line == null) tr.line = FxKit.MakeLine(transform, "PdTracer", FxKit.BeamTex());
            tr.a = from;
            tr.b = at + Random.insideUnitSphere * spread * (i == 0 ? 0f : 1f);
            tr.maxLife = 0.07f + 0.025f * i;
            tr.life = -0.02f * i;       // staggered: a burst, not one line
            tr.colour = c;
            tr.line.enabled = false;
            tracers.Add(tr);
        }
        if (rig != null) rig.FlashMuzzle(from, c, at - from, 0.6f);
    }

    /// Beams and railgun slugs arrive the frame they are fired and cannot be caught by anything.
    static bool Catchable(Shot s) => !s.instant && s.weapon != null && s.weapon.interceptable;

    // ---- tracers and fading trails -----------------------------------------------------------------
    void TickTrailsAndTracers(float dt)
    {
        float now = Time.time;
        for (int i = fadingTrails.Count - 1; i >= 0; i--)
        {
            var f = fadingTrails[i];
            if (f.trail == null) { fadingTrails.RemoveAt(i); continue; }
            if (now < f.until) continue;
            f.trail.enabled = false;
            trailPool.Push(f.trail);
            fadingTrails.RemoveAt(i);
        }

        float step = dt > 0f ? dt : Time.unscaledDeltaTime;
        for (int i = tracers.Count - 1; i >= 0; i--)
        {
            var t = tracers[i];
            t.life += step;
            if (t.life >= t.maxLife || t.line == null)
            {
                if (t.line != null) t.line.enabled = false;
                tracers.RemoveAt(i);
                tracerPool.Push(t);
                continue;
            }
            if (t.life < 0f) continue;   // not fired yet
            // A dash running from the mount to the round: the head leads, the tail follows.
            float k = t.life / t.maxLife;
            Vector3 head = Vector3.Lerp(t.a, t.b, Mathf.Clamp01(k * 1.3f));
            Vector3 tail = Vector3.Lerp(t.a, t.b, Mathf.Clamp01(k * 1.3f - 0.35f));
            float wpp = FxKit.WorldPerPixel(head);
            t.line.SetPosition(0, tail);
            t.line.SetPosition(1, head);
            t.line.widthMultiplier = Mathf.Max(0.035f, 1.4f * wpp);
            Color c = t.colour * 2.2f; c.a = 1f - k * 0.5f;
            FxKit.SetColour(t.line, c);
            if (!t.line.enabled) t.line.enabled = true;
        }
    }

    /// Forget everything in the air. Called when a galaxy is replaced.
    public void ClearAll()
    {
        for (int i = shots.Count - 1; i >= 0; i--) Retire(shots[i]);
        shots.Clear();
        for (int i = 0; i < tracers.Count; i++) { if (tracers[i].line != null) tracers[i].line.enabled = false; tracerPool.Push(tracers[i]); }
        tracers.Clear();
        for (int i = 0; i < fadingTrails.Count; i++)
            if (fadingTrails[i].trail != null) { fadingTrails[i].trail.enabled = false; trailPool.Push(fadingTrails[i].trail); }
        fadingTrails.Clear();
    }

    // ============================================================================================
    // POOLING
    // ============================================================================================
    void Retire(Shot s)
    {
        Release(streakPool, s.body); s.body = null;
        Release(streakPool, s.ghost); s.ghost = null;
        Release(dotPool, s.halo); s.halo = null;
        if (s.glowLine != null) { s.glowLine.enabled = false; linePool.Push(s.glowLine); s.glowLine = null; }
        if (s.coreLine != null) { s.coreLine.enabled = false; linePool.Push(s.coreLine); s.coreLine = null; }
        if (s.trail != null)
        {
            // The exhaust trail is left to fade out where it is rather than vanishing with the round.
            s.trail.emitting = false;
            fadingTrails.Add(new Fading { trail = s.trail, until = Time.time + s.trail.time });
            s.trail = null;
        }
        if (s.light != null) { s.light.enabled = false; lightPool.Push(s.light); s.light = null; }

        // References go too: a pooled shot holding a Unit would keep a dead ship alive.
        s.shooter = null; s.target = null; s.weapon = null;
        if (shotPool.Count < 300) shotPool.Push(s);
    }

    static void Release(Stack<Renderer> pool, Renderer r)
    {
        if (r == null) return;
        r.enabled = false;
        pool.Push(r);
    }

    Renderer Rent(Stack<Renderer> pool, Texture tex)
    {
        Renderer r = null;
        while (pool.Count > 0 && r == null) r = pool.Pop();
        if (r == null) r = FxKit.MakeQuad(transform, pool == dotPool ? "ShotGlow" : "ShotStreak", FxKit.SharedAdditive(tex));
        r.enabled = true;
        return r;
    }

    LineRenderer RentLine()
    {
        LineRenderer lr = null;
        while (linePool.Count > 0 && lr == null) lr = linePool.Pop();
        if (lr == null) lr = FxKit.MakeLine(transform, "Beam", FxKit.BeamTex());
        lr.startColor = lr.endColor = Color.white;   // brightness goes through the property block (HDR)
        lr.enabled = true;
        return lr;
    }

    TrailRenderer RentTrail(Vector3 at, float time)
    {
        TrailRenderer tr = null;
        while (trailPool.Count > 0 && tr == null) tr = trailPool.Pop();
        if (tr == null)
        {
            var go = new GameObject("ShotTrail");
            go.transform.SetParent(transform, false);
            tr = go.AddComponent<TrailRenderer>();
            tr.sharedMaterial = FxKit.SharedAdditive(FxKit.BeamTex());
            tr.textureMode = LineTextureMode.Stretch;
            tr.alignment = LineAlignment.View;
            tr.numCapVertices = 2;
            tr.minVertexDistance = 0.05f;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;
            tr.autodestruct = false;
            // Fades along its length: the colour itself goes through the property block.
            tr.startColor = Color.white;
            tr.endColor = new Color(1f, 1f, 1f, 0f);
            tr.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.25f);
        }
        tr.time = time;
        tr.transform.position = at;
        tr.Clear();
        tr.emitting = true;
        tr.enabled = true;
        return tr;
    }

    /// A pooled point light for a round. HARD-CAPPED: past the cap a round simply flies unlit, which
    /// nobody notices in a firefight already lit by the ones that got a light.
    public static bool DynamicLights = true;

    const int MaxLiveLights = 14;
    int lightsMade;

    Light RentLight()
    {
        if (!DynamicLights) return null;
        while (lightPool.Count > 0)
        {
            var l = lightPool.Pop();
            if (l != null) return l;
        }
        if (lightsMade >= MaxLiveLights) return null;
        lightsMade++;
        var go = new GameObject("ShotLight");
        go.transform.SetParent(transform, false);
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.shadows = LightShadows.None;
        light.renderMode = LightRenderMode.ForcePixel;
        light.enabled = false;
        return light;
    }
}
