using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// SHOTS IN FLIGHT
//
// Every round fired in the game is one entry in one list, updated by one loop, drawn by one pooled
// renderer. There is no per-projectile GameObject churn and no Instantiate in the firing path: a
// battle that fires two hundred rounds a second would otherwise spend its entire frame budget
// allocating and collecting quads.
//
// This file OWNS the flight and the arrival. It does not own the maths — Ballistics does — and the
// split is deliberate: everything below is bookkeeping, pooling and drawing, and everything that
// decides where a round goes is in a file with no Unity in it that a Node script can mirror and draw
// a picture of. See tools/ballistics-check.mjs.
//
// ---- THREE KINDS OF SHOT ----------------------------------------------------------------------
//
//   INSTANT     A beam or a railgun slug. There is no travel: hit or miss is decided at the muzzle and
//               what we draw is the AFTERIMAGE, a line that fades over a fraction of a second.
//
//   UNGUIDED    A bolt, a plasma ball, an autocannon round. It was aimed at a point — a LEAD point,
//               computed by Ballistics from where the target was going — and it flies there and no
//               further. If the target changed its mind after the trigger was pulled, the round goes
//               through empty space, and that is the mechanic rather than a shortcoming.
//
//   GUIDED      A missile or a torpedo. It has a motor with a boost phase and a finite burn, a seeker
//               with a finite cone, and a turn radius that grows with the square of its speed. It
//               steers by proportional navigation while it has fuel and a lock, coasts blind and
//               straight once either runs out, and then dissipates.
//
// ---- TWO BUGS THIS REWRITE FIXES, BOTH OF WHICH WERE INVISIBLE --------------------------------
//
// 1. INSTANT WEAPONS DEALT NO DAMAGE AT ALL. The single call to ResolveHit lived inside the
//    travelling-round branch, below an `if (instant) { ...fade...; continue; }`. So every beam and
//    every railgun in the game drew a beautiful line and did nothing whatsoever. That is 56% of a
//    Dreadnought's rated attack, 35% of a Cruiser's and 38% of a Fighter Mk II's quietly going
//    nowhere, in a system where the only symptom is "capital ships feel a bit weak".
//
// 2. FAST ROUNDS TUNNELLED THROUGH THEIR TARGETS. Arrival was a point test — is the round within
//    0.55 units of the target THIS frame. A pulse bolt travels 1.03 units per frame at 60fps, so the
//    test was being asked to catch a 1.1-unit-wide window with a 1.03-unit stride, and it missed
//    roughly half of them. At 30fps it missed nearly all of them. Every hit test below is now a
//    SEGMENT test against the path the round actually swept, which cannot tunnel at any frame rate —
//    and that matters more now than it did, because frame rate should never be a weapon stat.
// ============================================================================================
public class ProjectileRenderer : MonoBehaviour
{
    public static ProjectileRenderer Instance;

    /// A round in flight, or the fading afterimage of an instant one.
    class Shot
    {
        public WeaponInfo weapon;
        public Unit shooter;                 // for credit on the kill; may die mid-flight
        public Unit target;                  // may die mid-flight, leaving the round to fly on
        public Vector3 pos, vel;
        public Vector3 aimPoint;             // where an unguided round is going; the fallback for a guided one
        public float damage;
        public float age, maxLife;
        public bool instant;
        public bool hasLock;                 // a guided round that has lost its target never gets it back

        public Transform tr;                 // pooled quad (travelling) — null for instant shots
        public Light light;                  // pooled point light, so the round lights what it passes
        public float phaseOffset;            // per-shot offset so a volley pulses out of step, not as one strobe
        public LineRenderer beam;            // pooled line (instant) — null for travelling shots
    }

    readonly List<Shot> shots = new List<Shot>();
    readonly Stack<Transform> quadPool = new Stack<Transform>();
    readonly Stack<LineRenderer> beamPool = new Stack<LineRenderer>();
    readonly Stack<Light> lightPool = new Stack<Light>();
    readonly Stack<Shot> shotPool = new Stack<Shot>();

    Material boltMat;
    Material beamMat;
    Texture2D boltTex;

    /// How long an instant shot's afterimage hangs about. Short enough to read as a flash rather than
    /// a rope, long enough to survive a frame drop.
    const float InstantLife = 0.13f;

    public static void Create()
    {
        if (Instance != null) return;
        var go = new GameObject("ProjectileRenderer");
        go.AddComponent<ProjectileRenderer>();
        // Shots vanish at galaxy zoom along with the hulls firing them — see MapTierVisibility.
        go.AddComponent<MapTierVisibility>();
    }

    void Awake()
    {
        Instance = this;
        boltTex = BoltTexture();
        boltMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = boltTex };
        beamMat = new Material(Shader.Find("Sprites/Default"));
    }

    /// How many rounds are in the air. The combat manager uses this to stop a pathological fight from
    /// filling the list.
    public int LiveCount => shots.Count;

    // ============================================================================================
    // FIRING
    // ============================================================================================

    /// Fire one round.
    ///
    /// `aimPoint` is where the shot is GOING, not where the target is. CombatManager has already run
    /// the intercept solution and thrown the mount's dispersion cone at it, so by the time a round
    /// gets here the question of whether it was aimed well has been settled and this only has to fly
    /// it honestly. A guided round keeps the aim point as the place to coast toward if its seeker
    /// loses the target.
    public void Fire(Unit shooter, Unit target, WeaponInfo w, Vector3 from, Vector3 aimPoint, float damage)
    {
        if (w == null) return;

        // ---- POOLED, like the quads and the beams and the lights ----
        //
        // Every other per-round object here is recycled and the Shot itself was not, which is the one
        // that is created most: a fleet action firing two hundred rounds a second allocated two
        // hundred short-lived objects a second, every one of them dead within a few seconds. That is a
        // Gen-0 collection every few seconds, and a Gen-0 collection is a frame hitch — during a
        // battle, which is the exact moment a hitch is least welcome.
        var s = shotPool.Count > 0 ? shotPool.Pop() : new Shot();
        s.weapon = w; s.shooter = shooter; s.target = target;
        s.pos = from; s.vel = Vector3.zero; s.aimPoint = aimPoint; s.damage = damage;
        s.age = 0f; s.maxLife = 0f;
        s.instant = w.IsInstant;
        s.hasLock = target != null && !target.IsDestroyed;
        s.phaseOffset = Random.Range(0f, Mathf.PI * 2f);
        // Every field is written, including the ones a fresh object would have had at zero — a pooled
        // object carrying one stale field from its last flight is the classic way this goes wrong, and
        // it would show up as a round that inherited the previous round's lock or lifetime.
        s.tr = null; s.beam = null; s.light = null;

        if (s.instant) { FireInstant(s, from, aimPoint); }
        else           { FireTravelling(s, w, from, aimPoint); }

        // A real light on the round, so a shot actually illuminates what it passes and what it hits.
        // Range is tied to the weapon's own glow and width, so a heavy plasma bolt throws light and a
        // point-defence needle barely does — one number per weapon, no extra table.
        s.light = RentLight();
        if (s.light != null)
        {
            s.light.color = w.colour;
            s.light.range = Mathf.Clamp(w.width * 26f + w.glow * 1.6f, 0.6f, 6f);
            s.light.intensity = Mathf.Clamp(w.glow * 2.2f, 0.6f, 7f);
            s.light.transform.position = from;
            s.light.enabled = true;
        }

        shots.Add(s);
        SimpleAudio.Instance?.PlayWeapon(w.cls, from);

        // The muzzle lights up on the SAME call that creates the round, so the flash and the shot can
        // never drift apart. Deriving it from the weapon's cooldown instead would re-time it against a
        // different clock and show a flash with no bullet the moment anything hitched.
        UnitModelRenderer.Instance?.LightsOf(shooter)?.FlashMuzzle(from, w.colour);
    }

    /// A beam or a slug: hit or miss is decided here and now.
    ///
    /// The dispersed aim point already carries whatever error the mount has. If it landed inside the
    /// weapon's hit radius of the target, the shot connects and the line is drawn to the HULL, because
    /// that is where it went. If it did not, the line is drawn to the aim point instead — so a missed
    /// railgun shot visibly sails past, which is the only feedback the player will ever get that
    /// crossing speed and range are doing something.
    void FireInstant(Shot s, Vector3 from, Vector3 aimPoint)
    {
        var w = s.weapon;
        s.maxLife = InstantLife;

        Vector3 hullPos = s.target != null && !s.target.IsDestroyed
            ? CombatManager.PosOf(s.target) : aimPoint;

        bool hit = s.target != null && !s.target.IsDestroyed &&
                   Vector3.Distance(aimPoint, hullPos) <= w.hitRadius;

        // Overshoot a miss rather than stopping it dead at the aim point. A beam that stops in empty
        // space exactly level with its target reads as a hit that failed to register; one that carries
        // past reads as a miss.
        Vector3 end = hit ? hullPos : from + (aimPoint - from) * 1.12f;
        s.aimPoint = end;

        s.beam = RentBeam();
        s.beam.startColor = s.beam.endColor = w.colour * w.glow;
        s.beam.widthMultiplier = w.width;
        s.beam.SetPosition(0, from);
        s.beam.SetPosition(1, end);

        if (hit) CombatManager.Instance?.ResolveHit(s.shooter, s.target, w, s.damage, hullPos);
        else     CombatManager.Instance?.ReportMiss(s.shooter);
    }

    /// Anything with a flight time. Sets the launch conditions, which for a guided round are
    /// deliberately BAD ones.
    void FireTravelling(Shot s, WeaponInfo w, Vector3 from, Vector3 aimPoint)
    {
        Vector3 dir = aimPoint - from;
        dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;

        // ---- the cold launch ------------------------------------------------------------------
        //
        // A missile does not leave its tube pointing at the target. It is ejected clear of the hull,
        // the motor lights, and guidance then hauls it round onto the intercept — which is where the
        // characteristic opening curve of a missile launch comes from. Modelling it as a bad initial
        // heading rather than as an animation means proportional navigation fixes it in front of the
        // player, at whatever rate the round's actual thrust allows, and a torpedo's lazier recovery
        // versus a missile's sharp one falls out for free.
        // The ejection is a YAW, about world up, so the round fans out sideways WITHIN the plane the
        // game is played on. The first version rotated about an axis perpendicular to the bore, which
        // threw the round up out of the ecliptic — invisible from a top-down camera, and worse than
        // invisible in the maths: the missile spent its entire boost phase climbing back down, held a
        // huge bearing off its own nose the whole time, and tripped its seeker the instant it armed.
        // Two of the four engagements in tools/ballistics-check.mjs failed on that alone.
        //
        // Sideways is both visible and cheap to undo, and a salvo whose rounds go opposite ways reads
        // as a rack emptying rather than as one missile drawn twice. The small pitch scatter is
        // decoration only — enough that two rounds do not occupy the same pixel, far too little to
        // cost anything.
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

        s.tr = RentQuad();
        var mr = s.tr.GetComponent<MeshRenderer>();
        mr.material.color = w.colour * w.glow;
        s.tr.localScale = new Vector3(w.width * 2f, w.length, 1f);
    }

    // ============================================================================================
    // THE ONE LOOP
    // ============================================================================================
    void Update()
    {
        // Frame time, not game time: a shot's flight is an ANIMATION. Pausing the game should stop the
        // simulation, and it does — CombatManager reads the scaled clock and stops firing — but a round
        // already in the air finishing its arc at 5x speed would just look broken.
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        for (int i = shots.Count - 1; i >= 0; i--)
        {
            var s = shots[i];
            s.age += dt;

            if (s.instant) { if (!TickInstant(s)) { Retire(s); shots.RemoveAt(i); } continue; }
            if (!TickTravelling(s, dt))           { Retire(s); shots.RemoveAt(i); }
        }
    }

    /// Fade an afterimage. Returns false when it is done.
    bool TickInstant(Shot s)
    {
        float k = 1f - Mathf.Clamp01(s.age / s.maxLife);
        if (s.beam != null)
        {
            Color c = s.weapon.colour * s.weapon.glow;
            c.a = k;
            s.beam.startColor = s.beam.endColor = c;
        }
        // The flash lights the gap it crosses. Sat at the midpoint because a beam is lit along its
        // whole length and one point light is a great deal cheaper than a line of them.
        if (s.light != null)
        {
            s.light.transform.position = Vector3.Lerp(s.pos, s.aimPoint, 0.5f);
            s.light.intensity = Mathf.Clamp(s.weapon.glow * 2.2f, 0.6f, 7f) * k;
        }
        return s.age < s.maxLife;
    }

    /// Fly a round one step and see whether it arrived. Returns false when it should leave the list.
    bool TickTravelling(Shot s, float dt)
    {
        var w = s.weapon;
        Vector3 was = s.pos;

        // ---- where the target is, and how it is moving ----
        bool targetAlive = s.target != null && !s.target.IsDestroyed;
        Vector3 tPos = targetAlive ? CombatManager.PosOf(s.target) : s.aimPoint;
        Vector3 tVel = targetAlive ? UnitModelRenderer.VelocityOf(s.target) : Vector3.zero;

        if (!targetAlive) s.hasLock = false;

        if (w.IsGuided)
        {
            // ---- the seeker ----
            //
            // Checked BEFORE the step, against the heading the round currently holds, and caged for
            // the first fraction of a second so a cold-launched round does not break its own lock on
            // the bad heading it was deliberately given. A target that slides outside the cone after
            // that is gone for good: there is no reacquisition.
            if (s.hasLock && !Ballistics.SeekerHasLock(w, s.age, s.vel, s.pos, tPos))
                s.hasLock = false;

            s.vel = Ballistics.StepGuided(w, s.pos, s.vel, tPos, tVel, s.aimPoint, s.age, s.hasLock, dt);
        }
        else if (w.wanderDeg > 0.01f)
        {
            // ---- plasma does not fly straight ----
            //
            // A magnetically bottled ball of gas wanders. Perlin rather than white noise so the drift
            // is a slow curve rather than a jitter, and seeded per shot so a volley does not writhe in
            // unison. This is the only source of curvature on an unguided round, and it is small
            // enough to be flavour rather than a hidden accuracy penalty — a plasma bolt still lands
            // where it was aimed, it just does not get there along a ruler.
            float t = s.age * 0.9f + s.phaseOffset;
            float yaw   = (Mathf.PerlinNoise(t, 0.13f) - 0.5f) * 2f;
            float pitch = (Mathf.PerlinNoise(0.71f, t) - 0.5f) * 2f;
            Vector3 axis = Vector3.Cross(s.vel, Vector3.up);
            if (axis.sqrMagnitude < 1e-6f) axis = Vector3.right;
            s.vel = Quaternion.AngleAxis(yaw * w.wanderDeg * dt, Vector3.up) *
                    (Quaternion.AngleAxis(pitch * w.wanderDeg * dt, axis.normalized) * s.vel);
        }

        s.pos += s.vel * dt;

        // ---- did it touch anything on the way? -------------------------------------------------
        //
        // Against the SEGMENT it just swept, not against where it happens to have stopped. See the
        // header: a point test at these speeds misses about half its hits at 60fps and nearly all of
        // them at 30, which makes frame rate a weapon stat.
        if (targetAlive && SegmentDistance(was, s.pos, tPos) <= w.hitRadius)
        {
            CombatManager.Instance?.ResolveHit(s.shooter, s.target, w, s.damage, tPos);
            return false;
        }

        // An unguided round that reached where it was aimed is spent. That is precisely what "no
        // tracking" means, and it is why a pulse laser misses a ship that changed course.
        if (!w.IsGuided && SegmentDistance(was, s.pos, s.aimPoint) <= w.hitRadius)
        {
            CombatManager.Instance?.ReportMiss(s.shooter);
            return false;
        }

        DrawTravelling(s);
        return s.age < s.maxLife;
    }

    /// Everything about a round in flight that is purely appearance.
    void DrawTravelling(Shot s)
    {
        var w = s.weapon;

        // How much of its life is left, over the final stretch. A round does not blink out — it
        // gutters, which reads as the motor dying instead of as a rendering bug.
        float fade = Mathf.Clamp01((s.maxLife - s.age) / Ballistics.DissipateTime);

        // Boost is BRIGHT. A missile lighting its motor a moment after launch is the single most
        // readable event in a volley, and it costs one multiplier to say so.
        float boost = (w.boostTime > 0.01f && s.age < w.boostTime) ? 1.55f : 1f;

        // Plasma breathes, on the fleet beat like the running lights and the drive plumes — so
        // everything glowing in a battle pulses to one clock rather than each on its own cycle. The
        // per-shot offset keeps a volley from strobing in unison.
        float pulse = w.cls == WeaponClass.PlasmaCannon
            ? 0.82f + 0.18f * Mathf.Sin(FleetClock.Beats * Mathf.PI * 4f + s.phaseOffset)
            : 1f;

        if (s.tr != null)
        {
            s.tr.position = s.pos;
            if (s.vel.sqrMagnitude > 1e-6f)
                s.tr.rotation = Quaternion.LookRotation(Vector3.forward, s.vel.normalized);

            float k = pulse * Mathf.Max(0.15f, fade);
            s.tr.localScale = new Vector3(w.width * 2f * k, w.length * k, 1f);

            var mr = s.tr.GetComponent<MeshRenderer>();
            Color c = w.colour * (w.glow * boost);
            c.a = fade;
            mr.material.color = c;
        }

        // The light rides along, so hulls the round passes are lit as it goes by and the one it is
        // about to hit brightens as it closes.
        if (s.light != null)
        {
            s.light.transform.position = s.pos;
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
    // THE SCREEN
    //
    // Point defence used to protect the hull carrying it and nothing else. The reasoning was sound and
    // the conclusion was half right: AREA point defence, where one destroyer screens a whole fleet,
    // makes that one ship mandatory and every other hull interchangeable, which is a worse game.
    //
    // But it left a hole exactly where the game most wanted a mechanic. A colony ship has no guns and
    // no screen. A terraformer, a science vessel, a transport — none of them carry point defence,
    // because arming them would make them warships. So an escort could not actually protect the thing
    // it was escorting: it could kill the shooter eventually, and in the meantime the torpedoes went
    // past it and into the hull it was standing next to. Escorting was a formation and a protocol with
    // no mechanical teeth.
    //
    // So a mount now reaches for its neighbours as well as itself, with three limits that keep it from
    // becoming the fleet-wide umbrella the original note was right to refuse:
    //
    //   IT REACHES LESS FAR FOR SOMEBODY ELSE. A round crossing your engagement envelope on its way
    //   somewhere else is a harder shot than one coming down your throat, and the geometry says so.
    //
    //   IT LOOKS AFTER ITSELF FIRST. Rounds aimed at the mount's own hull are swept before any aimed
    //   at a neighbour, so a screening ship under fire stops screening — which is the pressure that
    //   makes killing the escort worth doing.
    //
    //   IT IS STILL PER HULL. Every warship has point defence; a fleet's screen is the sum of what its
    //   ships brought, not one specialist everybody has to bring. That was the real point of the
    //   original rule and it survives intact.
    // ============================================================================================

    /// How much of its own range a mount can cover a NEIGHBOUR at, as a fraction. Deliberately well
    /// under one: covering a friend is a favour, not a duty, and a fleet in tight formation should get
    /// a meaningful overlap rather than a blanket.
    const float EscortRangeFraction = 0.62f;

    /// Delete rounds a point-defence mount managed to catch. Returns how many were destroyed.
    ///
    /// `owner` carries the mount. Rounds aimed at it are always eligible; rounds aimed at anything
    /// friendly within the reduced radius are eligible once its own are clear.
    public int InterceptIncoming(Unit owner, Vector3 screenCentre, float radius, int maxKills)
    {
        int killed = Sweep(owner, owner, screenCentre, radius, maxKills);
        if (killed >= maxKills) return killed;

        // ---- and then whatever is going past, on its way to somebody the mount is standing near ----
        float escortRadius = radius * EscortRangeFraction;
        for (int i = shots.Count - 1; i >= 0 && killed < maxKills; i--)
        {
            var s = shots[i];
            if (!Catchable(s)) continue;
            if (s.target == null || s.target == owner || s.target.IsDestroyed) continue;
            // Only for hulls on the same side. "Friendly" is the same test combat uses for hostility,
            // inverted, so a mount can never shoot down a round aimed at somebody it is at war with.
            if (owner == null || s.target.owner != owner.owner) continue;
            if (Vector3.Distance(s.pos, screenCentre) > escortRadius) continue;

            ExplosionRenderer.Instance?.Burst(s.pos, 0.35f, s.weapon.colour);
            Retire(s); shots.RemoveAt(i); killed++;
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

            ExplosionRenderer.Instance?.Burst(s.pos, 0.35f, s.weapon.colour);
            Retire(s); shots.RemoveAt(i); killed++;
        }
        return killed;
    }

    /// Beams and railgun slugs arrive the frame they are fired and cannot be caught by anything.
    static bool Catchable(Shot s) => !s.instant && s.weapon != null && s.weapon.interceptable;

    /// Forget everything in the air. Called when a galaxy is replaced — the shots reference Units that
    /// are about to stop existing.
    public void ClearAll()
    {
        for (int i = shots.Count - 1; i >= 0; i--) Retire(shots[i]);
        shots.Clear();
    }

    // ============================================================================================
    // POOLING
    // ============================================================================================
    void Retire(Shot s)
    {
        if (s.tr != null) { s.tr.gameObject.SetActive(false); quadPool.Push(s.tr); s.tr = null; }
        if (s.beam != null) { s.beam.enabled = false; beamPool.Push(s.beam); s.beam = null; }
        if (s.light != null) { s.light.enabled = false; lightPool.Push(s.light); s.light = null; }

        // The references go too. A retired shot sitting in the pool holding a Unit would keep a dead
        // ship alive until the pool happened to hand that slot out again — the same leak CombatManager
        // clears its dictionaries for, arriving by a quieter route.
        s.shooter = null; s.target = null; s.weapon = null;

        // Bounded, so a pathological battle cannot leave a permanent heap of retired shots behind. The
        // ceiling is the projectile ceiling: past that the pool is already big enough to serve every
        // round the game will allow in the air at once.
        if (shotPool.Count < 300) shotPool.Push(s);
    }

    /// A pooled point light for a round in flight.
    ///
    /// HARD-CAPPED, and it has to be. Real-time lights are the one thing here that is not nearly free:
    /// URP culls per object and a battle putting two hundred rounds in the air would hand the renderer
    /// two hundred lights to sort. Past the cap this returns null and the round simply flies unlit —
    /// which nobody notices in a firefight already lit by the ones that got a light.
    public static bool DynamicLights = true;

    const int MaxLiveLights = 14;
    int lightsMade;

    Light RentLight()
    {
        if (!DynamicLights) return null;
        if (lightPool.Count > 0) return lightPool.Pop();
        if (lightsMade >= MaxLiveLights) return null;
        lightsMade++;
        var go = new GameObject("ShotLight");
        go.transform.SetParent(transform, false);
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.shadows = LightShadows.None;      // a shot casting shadows is invisible and expensive
        l.renderMode = LightRenderMode.ForcePixel;
        l.enabled = false;
        return l;
    }

    Transform RentQuad()
    {
        if (quadPool.Count > 0)
        {
            var t = quadPool.Pop();
            t.gameObject.SetActive(true);
            return t;
        }
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "Bolt";
        var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
        go.transform.SetParent(transform, false);
        var mr = go.GetComponent<MeshRenderer>();
        mr.material = new Material(boltMat);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return go.transform;
    }

    LineRenderer RentBeam()
    {
        if (beamPool.Count > 0)
        {
            var b = beamPool.Pop();
            b.enabled = true;
            return b;
        }
        var go = new GameObject("Beam");
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.numCapVertices = 2;
        lr.material = new Material(beamMat);
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        return lr;
    }

    /// A soft round blob, brightest in the middle. One texture serves every weapon because colour is
    /// applied through the material tint — so a green pulse and an orange plasma ball are the same
    /// four kilobytes.
    static Texture2D BoltTexture()
    {
        const int N = 32;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N - 0.5f, dy = (y + 0.5f) / N - 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;          // 0 centre, 1 edge
                float a = Mathf.Clamp01(1f - d);
                a = a * a;                                              // tighten the core
                byte v = (byte)Mathf.Clamp(a * 255f, 0f, 255f);
                px[y * N + x] = new Color32(255, 255, 255, v);
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }
}
