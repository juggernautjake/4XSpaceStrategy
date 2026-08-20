// ============================================================================================
// DOES A MISSILE FLY LIKE A MISSILE?
//
//   node tools/ballistics-check.mjs [--out <png>]
//
// A Node port of Ballistics.cs, run on the engagements the model has to get right, and drawn as a
// picture. There is no Unity in this environment, so the alternative to this is shipping a guidance
// law unseen and finding out in play — and a guidance law is exactly the sort of thing that looks
// perfectly reasonable in source and produces a missile flying in circles on screen.
//
// The weapon stats are PARSED OUT OF Weaponry.cs rather than typed here, so a tuning change shows up
// in this picture without anyone having to remember to mirror it. The guidance maths is ported by
// hand; if either side changes, both change.
//
// ---- WHAT IS BEING CLAIMED --------------------------------------------------------------------
//
// Six things, each with a panel and each with a pass/fail line printed underneath:
//
//   1 & 2. PRONAV BEATS PURSUIT on a hard crossing shot. Both panels get the same missile, the same
//      target and the same clock. Proportional navigation should fly a nearly straight line to a lead
//      point; pure pursuit should curve toward where the target IS and then whip round in a terminal
//      hook. If the two panels look alike, the geometry is too easy to be testing anything — which it
//      was, for several iterations, at which point both laws hit within a hundredth of a second.
//   3. BURNOUT IS REAL. After fuelTime the round must fly DEAD STRAIGHT regardless of what the target
//      does, because in vacuum turning is thrusting.
//   4. A FAST TARGET ESCAPES. Something quicker than the missile, running, is not caught — and the
//      firing solution should have refused the shot in the first place.
//   5. THE SEEKER CONE IS SIZED HONESTLY. The original claim here was that a hard break shakes the
//      seeker, and measuring it proved that false and taught the most useful thing in this file:
//      proportional navigation HOLDS the target at a constant bearing, bounded by asin(Vt/Vm), so
//      against anything slower than the round the target never leaves a narrow cone no matter how it
//      flies. The cone is therefore not the counterplay to a missile — fuel is — and what the panel
//      and its sweep now check is that the cone is wide enough never to break a healthy lock and
//      narrow enough to bite when a target approaches the round's own speed.
//   6. A TORPEDO CORNERS LIKE A MISSILE at half the speed. Same turn radius, four times the flight
//      time, which is the whole design of the hull.
//
// ---- WHY dt IS 1/60 ---------------------------------------------------------------------------
//
// Because that is what the game will run at, and a guidance law that only converges at a small step
// is a guidance law that gets worse when the player's machine gets busy. If a panel here changes
// character when dt changes, that is a finding and not a rendering artefact.
// ============================================================================================

import sharp from 'sharp';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const argv = process.argv.slice(2);
const arg = (n, d) => { const i = argv.indexOf(n); return i >= 0 ? argv[i + 1] : d; };
const OUT = path.resolve(PROJ, arg('--out', 'Art/_review/ballistics.png'));

const DT = 1 / 60;

// ============================================================================================
// THE WEAPON TABLE, READ FROM THE GAME
// ============================================================================================
function parseWeapons() {
  const src = fs.readFileSync(path.join(PROJ, 'Assets/Scripts/Data/Weaponry.cs'), 'utf8');
  const out = {};
  // `new WeaponInfo(WeaponClass.Missile, "Missile Rack") { ...fields... }` — brace-matched rather
  // than regexed to the first `}`, because several initialisers contain `new Color(...)`.
  const re = /new WeaponInfo\(WeaponClass\.(\w+),\s*"([^"]+)"\)\s*\{/g;
  let m;
  while ((m = re.exec(src))) {
    let i = re.lastIndex, depth = 1;
    while (i < src.length && depth > 0) {
      if (src[i] === '{') depth++;
      else if (src[i] === '}') depth--;
      i++;
    }
    const body = src.slice(re.lastIndex, i - 1).replace(/\/\/[^\n]*/g, '');
    const w = { cls: m[1], name: m[2] };
    for (const f of body.matchAll(/(\w+)\s*=\s*(-?[0-9.]+)f?\s*[,}]/g)) w[f[1]] = parseFloat(f[2]);
    for (const f of body.matchAll(/(\w+)\s*=\s*(true|false)\s*[,}]/g)) w[f[1]] = f[2] === 'true';
    for (const f of body.matchAll(/(\w+)\s*=\s*GuidanceLaw\.(\w+)/g)) w[f[1]] = f[2];
    for (const f of body.matchAll(/(\w+)\s*=\s*AmmoKind\.(\w+)/g)) w[f[1]] = f[2];
    // Defaults that live on the field declarations rather than in any initialiser.
    w.guidance ??= 'Unguided';
    w.navConstant ??= 3.6;
    w.lateralAccel ??= 0;
    w.turnRate ??= 0;
    w.seekerConeDeg ??= 180;
    w.seekerArmTime ??= 0.35;
    w.launchSpeed ??= 0;
    w.boostTime ??= 0;
    w.fuelTime ??= 0;
    w.burnoutTaper ??= 0.6;
    w.trackTime ??= 0;
    w.hitRadius ??= 0.55;
    w.salvo ??= 1;
    out[m[1]] = w;
  }
  return out;
}

const W = parseWeapons();

// ============================================================================================
// VECTORS — just enough of them
// ============================================================================================
const v = (x = 0, y = 0, z = 0) => ({ x, y, z });
const add = (a, b) => v(a.x + b.x, a.y + b.y, a.z + b.z);
const sub = (a, b) => v(a.x - b.x, a.y - b.y, a.z - b.z);
const mul = (a, s) => v(a.x * s, a.y * s, a.z * s);
const dot = (a, b) => a.x * b.x + a.y * b.y + a.z * b.z;
const cross = (a, b) => v(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
const len = a => Math.sqrt(dot(a, a));
const norm = a => { const l = len(a); return l < 1e-8 ? v(0, 0, 1) : mul(a, 1 / l); };
const clamp = (x, a, b) => Math.min(b, Math.max(a, x));
const angleBetween = (a, b) => {
  const l = len(a) * len(b);
  if (l < 1e-8) return 0;
  return Math.acos(clamp(dot(a, b) / l, -1, 1)) * 180 / Math.PI;
};

/// Unity's Vector3.RotateTowards, ported. Rotates `from` toward `to` by at most `maxRad`, keeping
/// `from`'s magnitude — the magnitude-preserving part matters, because the whole flight model relies
/// on the motor setting the speed and guidance setting only the direction.
function rotateTowards(from, to, maxRad) {
  const lf = len(from);
  if (lf < 1e-8) return to;
  const a = norm(from), b = norm(to);
  const ang = Math.acos(clamp(dot(a, b), -1, 1));
  if (ang < 1e-6) return from;
  const t = Math.min(1, maxRad / ang);
  // Slerp by t.
  const s = Math.sin(ang);
  const k0 = Math.sin((1 - t) * ang) / s, k1 = Math.sin(t * ang) / s;
  return mul(norm(add(mul(a, k0), mul(b, k1))), lf);
}

// ============================================================================================
// BALLISTICS, PORTED
// ============================================================================================
function interceptTime(from, tPos, tVel, speed) {
  if (speed <= 0.001) return 0;
  const d = sub(tPos, from);
  const a = dot(tVel, tVel) - speed * speed;
  const b = 2 * dot(d, tVel);
  const c = dot(d, d);
  if (Math.abs(a) < 0.0001) {
    if (Math.abs(b) < 0.0001) return -1;
    const tl = -c / b;
    return tl > 0 ? tl : -1;
  }
  const disc = b * b - 4 * a * c;
  if (disc < 0) return -1;
  const r = Math.sqrt(disc);
  const t1 = (-b + r) / (2 * a), t2 = (-b - r) / (2 * a);
  let best = -1;
  if (t1 > 0) best = t1;
  if (t2 > 0 && (best < 0 || t2 < best)) best = t2;
  return best;
}

const leadAim = (from, tPos, tVel, speed) => {
  const t = interceptTime(from, tPos, tVel, speed);
  return t < 0 ? null : add(tPos, mul(tVel, t));
};

/// Ballistics.DistanceCovered / EffectiveSpeed / InterceptTimeFor, ported. See the C# for why a
/// cruise-speed solution is badly wrong for anything with a boost phase.
function distanceCovered(w, t) {
  if (t <= 0) return 0;
  if (!w.boostTime) return w.projectileSpeed * t;
  const T = w.boostTime, v0 = w.launchSpeed, v1 = w.projectileSpeed;
  if (t <= T) return v0 * t + (v1 - v0) * (t * t * t) / (3 * T * T);
  return v0 * T + (v1 - v0) * T / 3 + v1 * (t - T);
}

const effectiveSpeed = (w, t) =>
  t <= 0.0001 ? Math.max(0.01, w.launchSpeed || w.projectileSpeed)
              : Math.max(0.01, distanceCovered(w, t) / t);

const reach = (w, d, tVel, t) => distanceCovered(w, t) - len(add(d, mul(tVel, t)));

function interceptTimeFor(w, from, tPos, tVel) {
  if (w.projectileSpeed <= 0.001) return 0;
  if (!w.boostTime) return interceptTime(from, tPos, tVel, w.projectileSpeed);
  const d = sub(tPos, from);
  let hi = w.trackTime || 6;
  if (reach(w, d, tVel, hi) < 0) return -1;
  let lo = 0;
  for (let i = 0; i < 24; i++) {
    const mid = 0.5 * (lo + hi);
    if (reach(w, d, tVel, mid) >= 0) hi = mid; else lo = mid;
  }
  return hi;
}

const leadAimFor = (w, from, tPos, tVel) => {
  const t = interceptTimeFor(w, from, tPos, tVel);
  return t < 0 ? null : add(tPos, mul(tVel, t));
};

const turnRadius = (speed, lat) => (lat <= 0.0001 ? Infinity : (speed * speed) / lat);

function turnRateAt(w, speed) {
  if (!w.lateralAccel) return 0;
  const fromThrust = speed > 0.01 ? (w.lateralAccel / speed) * 180 / Math.PI : w.turnRate;
  return w.turnRate > 0 ? Math.min(fromThrust, w.turnRate) : fromThrust;
}

function guidanceDirection(law, pos, vel, tPos, tVel, N, dt) {
  const r = sub(tPos, pos);
  const range = len(r);
  if (range < 0.0001) return len(vel) > 1e-4 ? norm(vel) : v(0, 0, 1);
  const rHat = mul(r, 1 / range);
  if (law !== 'Proportional') return rHat;

  const vRel = sub(tVel, vel);
  const omega = mul(cross(r, vRel), 1 / (range * range));
  const closing = -dot(r, vRel) / range;
  if (closing <= 0.01) return rHat;

  const accel = mul(cross(omega, rHat), N * closing);
  if (dot(vel, vel) < 0.0001) return rHat;
  const want = add(vel, mul(accel, dt));
  return dot(want, want) > 1e-8 ? norm(want) : rHat;
}

const SEEKER_TERMINAL_SECONDS = 0.35;

/// Ballistics.SeekerHasLock, ported: caged until the motor is up, released again inside the terminal
/// phase where the sight line sweeps faster than any round can turn.
function seekerHasLock(w, age, vel, pos, tPos) {
  if (age < w.seekerArmTime) return true;
  const terminal = w.projectileSpeed * SEEKER_TERMINAL_SECONDS;
  if (len(sub(tPos, pos)) <= terminal) return true;
  return inSeekerCone(vel, pos, tPos, w.seekerConeDeg);
}

function inSeekerCone(vel, pos, tPos, coneDeg) {
  if (coneDeg >= 179.9) return true;
  if (len(vel) < 1e-4) return true;
  const r = sub(tPos, pos);
  if (len(r) < 1e-4) return true;
  return angleBetween(vel, r) <= coneDeg;
}

function thrustFraction(w, age) {
  if (!w.fuelTime) return 1;
  if (age >= w.fuelTime) return 0;
  if (age < w.boostTime) return 1;
  return clamp((w.fuelTime - age) / Math.max(0.15, w.burnoutTaper), 0, 1);
}

function speedAt(w, age) {
  if (!w.boostTime) return w.projectileSpeed;
  if (age >= w.boostTime) return w.projectileSpeed;
  const k = clamp(age / w.boostTime, 0, 1);
  return w.launchSpeed + (w.projectileSpeed - w.launchSpeed) * k * k;
}

function stepGuided(w, pos, vel, tPos, tVel, mid, age, hasLock, dt, lawOverride) {
  const speed = speedAt(w, age);
  let heading = len(vel) > 1e-4 ? norm(vel) : v(0, 0, 1);
  const thrust = thrustFraction(w, age);
  const law = lawOverride || w.guidance;
  if (law === 'Unguided' || thrust <= 0.001) return mul(heading, speed);

  let want;
  if (age < w.seekerArmTime) {
    // Midcourse: fly the firing solution. See Ballistics.StepGuided for why ProNav cannot do this job.
    const leg = sub(mid, pos);
    want = len(leg) > 1e-4 ? norm(leg) : heading;
  } else if (hasLock) {
    want = guidanceDirection(law, pos, vel, tPos, tVel, w.navConstant, dt);
  } else {
    return mul(heading, speed);
  }

  const maxTurn = turnRateAt(w, speed) * thrust * Math.PI / 180 * dt;
  heading = norm(rotateTowards(heading, want, maxTurn));
  return mul(heading, speed);
}

/// Distance from p to the segment ab — the same segment test the renderer uses, so a hit here means
/// a hit there. A point test at these speeds tunnels; see the ProjectileRenderer header.
function segmentDistance(a, b, p) {
  const ab = sub(b, a);
  const l2 = dot(ab, ab);
  if (l2 < 1e-8) return len(sub(p, a));
  const t = clamp(dot(sub(p, a), ab) / l2, 0, 1);
  return len(sub(add(a, mul(ab, t)), p));
}

// ============================================================================================
// THE TARGET
//
// A point that flies at a fixed speed and turns at a fixed rate toward whatever its manoeuvre says.
// Deliberately simpler than a real hull: this file is testing the ROUND, and a target with its own
// full flight model would make every failure ambiguous about which half caused it.
// ============================================================================================
function makeTarget({ pos, dir, speed, turnDeg = 40, manoeuvre = () => null }) {
  return { pos, vel: mul(norm(dir), speed), speed, turnDeg, manoeuvre, track: [pos] };
}

function stepTarget(t, age, dt) {
  const want = t.manoeuvre(age, t);
  if (want) t.vel = rotateTowards(t.vel, mul(norm(want), t.speed), t.turnDeg * Math.PI / 180 * dt);
  t.pos = add(t.pos, mul(t.vel, dt));
  t.track.push(t.pos);
}

// ============================================================================================
// ONE ENGAGEMENT
// ============================================================================================
function engage({ weapon, from, target, lawOverride, coldLaunch = true, maxTime = null }) {
  const w = weapon;
  const life = maxTime ?? (w.trackTime || 6);

  // The firing solution, exactly as CombatManager computes it. `null` means the gun declines.
  const aim = leadAimFor(w, from, target.pos, target.vel);
  const declined = aim === null;
  let dir = norm(sub(aim ?? add(target.pos, v(0, 0, 1)), from));

  // The cold launch, as a bad initial condition rather than an animation. Fixed side and no roll
  // scatter, unlike the game — this is a diagram, and a diagram that changes every run is useless.
  if (coldLaunch && w.launchArcDeg > 0.01) {
    // A yaw about world up, matching ProjectileRenderer: the round fans out sideways within the plane
    // of play. Fixed side and no pitch scatter, unlike the game — this is a diagram, and a diagram
    // that changes every run is useless.
    const a = w.launchArcDeg * Math.PI / 180, ca = Math.cos(a), sa = Math.sin(a);
    dir = norm(v(dir.x * ca + dir.z * sa, dir.y, -dir.x * sa + dir.z * ca));
  }

  let pos = from, vel = mul(dir, speedAt(w, 0));
  let age = 0, hasLock = true, lockLostAt = null, burnoutAt = null;
  const track = [pos];
  let result = 'dissipated', hitAt = null;

  // The largest angle the target ever reaches off the round's nose. This is the number the seeker
  // cone has to be compared against, and it is far more informative than a pass/fail on one cone —
  // see the sweep further down.
  let maxBore = 0;

  // Total heading change over the whole flight, in degrees. This is CONTROL EFFORT, and it is the
  // textbook discriminator between the two guidance laws: proportional navigation commits to a
  // collision course early and then barely steers, while pure pursuit is permanently correcting a
  // course that was wrong the moment it was set. Comparing arrival times alone is far too weak a
  // test — on a short shot the two land within a few hundredths of a second of each other, and the
  // difference the player actually sees is the SHAPE of the track.
  let totalTurn = 0;
  const turnLog = [];   // degrees per second at each step, for the terminal-effort comparison

  while (age < life) {
    const wasPos = pos;
    stepTarget(target, age, DT);

    // Measured from the launch transient onward: for the first fraction of a second a cold-launched
    // round is pointing 38 degrees off by construction, and folding that into the peak would say
    // every missile in the game breaks its own lock at launch.
    if (age > 0.35) maxBore = Math.max(maxBore, angleBetween(vel, sub(target.pos, pos)));

    if (hasLock && !seekerHasLock(w, age, vel, pos, target.pos)) {
      hasLock = false;
      lockLostAt = { age, pos };
    }
    if (burnoutAt === null && w.fuelTime && age >= w.fuelTime) burnoutAt = { age, pos };

    const prevVel = vel;
    vel = stepGuided(w, pos, vel, target.pos, target.vel, aim ?? target.pos, age, hasLock, DT, lawOverride);
    const step = angleBetween(prevVel, vel);
    totalTurn += step;
    turnLog.push(step / DT);
    pos = add(pos, mul(vel, DT));
    track.push(pos);
    age += DT;

    if (segmentDistance(wasPos, pos, target.pos) <= w.hitRadius) {
      result = 'HIT'; hitAt = pos; break;
    }
  }

  // How close it ever got. The number that says whether a miss was near or absurd.
  let closest = Infinity;
  for (let i = 1; i < track.length && i < target.track.length; i++)
    closest = Math.min(closest, len(sub(track[i], target.track[i])));

  // How hard the round was working in its LAST half-second. This is the number that separates the
  // guidance laws, and it separates them by a mile: a pursuit round is chasing a point that is
  // sliding sideways faster the closer it gets, so its demanded turn rate runs away toward the
  // intercept, while a proportional-navigation round has already solved the problem and coasts in
  // almost straight. The textbook version of this is that pure pursuit needs INFINITE lateral
  // acceleration to hit a crossing target, and only survives at all because a hit radius is finite.
  const tail = turnLog.slice(Math.max(0, turnLog.length - Math.round(0.5 / DT)));
  const terminalTurn = tail.length ? Math.max(...tail) : 0;

  return { track, target, result, hitAt, age, closest, declined, lockLostAt, burnoutAt, maxBore,
           totalTurn, terminalTurn, weapon: w };
}

// ============================================================================================
// THE SIX PANELS
// ============================================================================================
const MISSILE = W.Missile, TORPEDO = W.Torpedo, PULSE = W.PulseLaser;

const panels = [];
const checks = [];
const ok = (name, pass, detail) => { checks.push({ name, pass, detail }); return pass; };

// ---- 1 & 2: proportional navigation against pure pursuit, same crossing target -----------------
{
  // The HARD geometry, deliberately. On an easy shot both laws hit within a hundredth of a second of
  // each other and the two panels are indistinguishable, which teaches nobody anything. Pursuit's
  // weakness is geometric: it only shows against a target crossing fast enough that "where it is"
  // and "where it will be" are far apart for the whole flight.
  const mk = () => makeTarget({ pos: v(32, 0, 26), dir: v(0, 0, -1), speed: 24 });
  const pro = engage({ weapon: MISSILE, from: v(0, 0, 0), target: mk() });
  const pur = engage({ weapon: MISSILE, from: v(0, 0, 0), target: mk(), lawOverride: 'Pursuit' });

  panels.push({ title: 'ProNav vs a crossing target', runs: [pro] });
  panels.push({ title: 'Pure pursuit, same shot', runs: [pur] });

  // ---- THE LEAD ANGLE DURING BOOST, WHICH IS WHY seekerArmTime EXISTS -------------------------
  //
  // Printed rather than merely asserted, because the shape of this curve is the single least
  // intuitive thing in the whole flight model and it is worth being able to look at. A missile on a
  // perfect intercept sits at a LARGE constant bearing off its own nose while it is still slow, and
  // that bearing collapses as the motor builds speed — lead angle is asin(Vt/Vm), and Vm is the
  // variable. Anyone who tightens the seeker cone or lengthens the boost needs to see this.
  {
    const uncaged = { ...MISSILE, seekerConeDeg: 180 };
    const t = mk();
    const r = engage({ weapon: uncaged, from: v(0, 0, 0), target: t });
    const rows = [];
    for (let i = 1; i < r.track.length && i < t.track.length && i * DT <= 1.5; i += 12) {
      const vv = sub(r.track[i], r.track[i - 1]);
      rows.push(`${(i * DT).toFixed(1)}s:${angleBetween(vv, sub(t.track[i], r.track[i])).toFixed(0)}`);
    }
    console.log(`lead angle off the nose, uncaged seeker:  ${rows.join('  ')}  (deg)`);
    console.log(`  boost ends at ${MISSILE.boostTime}s, seeker arms at ${MISSILE.seekerArmTime}s, ` +
                `cone is ${MISSILE.seekerConeDeg} deg\n`);
  }
  ok('proportional navigation intercepts a crossing target', pro.result === 'HIT',
     `${pro.result} at ${pro.age.toFixed(2)}s, closest ${pro.closest.toFixed(2)}u`);
  // ---- HOW MUCH BETTER, AND WHEN --------------------------------------------------------------
  //
  // Measured across a range of engagement difficulty rather than asserted on one shot, because on one
  // EASY shot the two laws are nearly indistinguishable and a check that passed on a 0.02-second
  // margin would be a check that tells nobody anything. Pursuit's weakness is geometric and it only
  // shows when the geometry is hard: the faster the target crosses and the further it has to be
  // chased, the more of the flight pursuit spends aimed at a point that has already moved.
  //
  // The theoretical statement is that pure pursuit needs unbounded lateral acceleration to hit a
  // crossing target, and a real round has a cap. So the interesting column is not who hits — it is
  // where pursuit starts SATURATING its turn rate and proportional navigation does not.
  const duel = [
    { label: 'close, slow  ', pos: v(20, 0, 8), dir: v(-0.3, 0, -1), sp: 8 },
    { label: 'medium       ', pos: v(26, 0, 14), dir: v(-0.25, 0, -1), sp: 11 },
    { label: 'long, fast   ', pos: v(30, 0, 24), dir: v(-0.2, 0, -1), sp: 18 },
    { label: 'long, faster ', pos: v(32, 0, 26), dir: v(0, 0, -1), sp: 24 },
  ].map(g => {
    const t = () => makeTarget({ pos: g.pos, dir: g.dir, speed: g.sp });
    const a = engage({ weapon: MISSILE, from: v(0, 0, 0), target: t() });
    const b = engage({ weapon: MISSILE, from: v(0, 0, 0), target: t(), lawOverride: 'Pursuit' });
    return { ...g, pro: a, pur: b };
  });

  console.log('proportional navigation against pure pursuit, same missile and same target');
  for (const d of duel)
    console.log(`  ${d.label} target ${String(d.sp).padStart(2)} u/s   ` +
                `pronav ${d.pro.result === 'HIT' ? 'HIT ' : 'MISS'} ${d.pro.age.toFixed(2)}s eff ${d.pro.totalTurn.toFixed(0).padStart(3)} deg   ` +
                `pursuit ${d.pur.result === 'HIT' ? 'HIT ' : 'MISS'} ${d.pur.age.toFixed(2)}s eff ${d.pur.totalTurn.toFixed(0).padStart(3)} deg`);
  console.log('');

  ok('proportional navigation is never worse than pursuit',
     duel.every(d => (d.pro.result === 'HIT') >= (d.pur.result === 'HIT')),
     duel.map(d => `${d.sp}u/s ${d.pro.result === 'HIT' ? 'hit' : 'miss'}/${d.pur.result === 'HIT' ? 'hit' : 'miss'}`).join('  '));

  // Control effort on the hardest shot in the sweep. This is the real difference between the laws and
  // it is enormous: proportional navigation commits to a collision course and then barely steers,
  // while pure pursuit is correcting a course that was wrong the moment it was set, all the way in.
  {
    const hard = duel[duel.length - 1];
    ok('pursuit works several times as hard as pronav on the hardest shot',
       hard.pur.totalTurn > hard.pro.totalTurn * 2,
       `at ${hard.sp} u/s: pronav ${hard.pro.totalTurn.toFixed(0)} deg over ${hard.pro.age.toFixed(2)}s, ` +
       `pursuit ${hard.pur.totalTurn.toFixed(0)} deg over ${hard.pur.age.toFixed(2)}s — ` +
       `${(hard.pur.totalTurn / hard.pro.totalTurn).toFixed(1)}x the control effort`);
  }
}

// ---- 3: burnout ------------------------------------------------------------------------------
//
// The target runs, so the missile chases it past its own fuel. What has to be true is that the track
// after burnout is a straight line: sample the heading either side of the last few tenths and the
// angle between them must be zero to within a rounding error.
{
  // Launched at a target already most of the way to the edge of the envelope and running at 27 u/s
  // against a 34 u/s missile. Closure is seven units a second, so the round is still chasing when the
  // motor quits — which is the only way to see what a dry missile does.
  // A shot the gun WOULD take — the solution exists at launch — against a target that then turns and
  // runs. The missile follows it past its own burnout, and what the panel is for is the moment the
  // motor quits: the track stops bending, mid-chase, with the target still turning away.
  const tgt = makeTarget({ pos: v(38, 0, 0), dir: v(1, 0, 0.2), speed: 21, turnDeg: 26,
                           manoeuvre: (age) => (age > 0.8 ? v(0.7, 0, 1) : null) });
  const run = engage({ weapon: MISSILE, from: v(0, 0, 0), target: tgt });
  panels.push({ title: 'Burnout: no fuel, no turning', runs: [run] });

  let bend = 0;
  if (run.burnoutAt) {
    const i0 = Math.floor(run.burnoutAt.age / DT) + 4;
    const a = sub(run.track[i0 + 1], run.track[i0]);
    const b = sub(run.track[run.track.length - 1], run.track[run.track.length - 2]);
    if (a && b) bend = angleBetween(a, b);
  }
  ok('a burned-out round flies dead straight', run.burnoutAt !== null && bend < 0.05,
     run.burnoutAt ? `heading drift after burnout: ${bend.toFixed(4)} deg` : 'never burned out');
}

// ---- 4: something faster, running -------------------------------------------------------------
{
  const tgt = makeTarget({ pos: v(20, 0, 0), dir: v(1, 0, 0), speed: 40 });
  const run = engage({ weapon: MISSILE, from: v(0, 0, 0), target: tgt });
  panels.push({ title: 'A faster target, running away', runs: [run] });

  ok('the gun declines a shot it cannot land', run.declined,
     run.declined ? 'no intercept solution — mount holds fire' : 'FIRED at an uncatchable target');
  ok('and the round does not catch it anyway', run.result !== 'HIT',
     `${run.result}, closest ${run.closest.toFixed(1)}u`);
}

// ---- 5: breaking the seeker lock --------------------------------------------------------------
//
// Turning ACROSS the missile rather than away from it. The target holds course until the round has
// committed, then breaks hard through ninety degrees — the classic answer, and the reason a seeker
// cone is a number in the weapon table rather than an assumption.
{
  // Held straight until the round has committed, then broken hard THROUGH the missile's flight path
  // at close range. Late and close is what matters: the line of sight sweeps fastest when the two are
  // nearly on top of each other, and that is the only moment a seeker can be outrun in bearing.
  const tgt = makeTarget({ pos: v(34, 0, 4), dir: v(-1, 0, 0), speed: 15, turnDeg: 110,
                           manoeuvre: (age) => (age > 1.28 ? v(0, 0, 1) : null) });
  const run = engage({ weapon: MISSILE, from: v(0, 0, 0), target: tgt });
  panels.push({ title: 'Hard break across at close range', runs: [run] });

  // ---- HOW WIDE SHOULD THE CONE ACTUALLY BE? ---------------------------------------------------
  //
  // The first version of this check asserted that a hard break shakes the seeker, and it failed —
  // which turned out to be a fact about proportional navigation rather than a bug. ProNav's entire
  // job is to hold the target at a CONSTANT bearing off the nose, and that bearing is the lead angle,
  // which is bounded by asin(Vtarget / Vmissile). Against anything slower than the round, the target
  // therefore sits inside a narrow cone for the whole flight no matter how it flies — 26 degrees for
  // a 15 u/s fighter against a 34 u/s missile — and a 75-degree seeker can never see it leave.
  //
  // So the cone is NOT the counterplay to a missile. Fuel is. The cone only bites when the target is
  // fast enough that the lead angle approaches it, which is exactly when a seeker should struggle,
  // and the sweep below is here to keep that honest: if someone raises missile speed or lowers it,
  // this table says immediately whether the cone still means anything.
  const sweep = [8, 12, 16, 20, 26, 32].map(sp => {
    const t = makeTarget({ pos: v(34, 0, 4), dir: v(-1, 0, 0), speed: sp, turnDeg: 110,
                           manoeuvre: (age) => (age > 1.28 ? v(0, 0, 1) : null) });
    const r = engage({ weapon: MISSILE, from: v(0, 0, 0), target: t });
    return { sp, bore: r.maxBore, result: r.result, lost: r.lockLostAt !== null };
  });
  console.log('seeker geometry — peak bearing off the missile nose, hard late break');
  for (const s of sweep)
    console.log(`  target ${String(s.sp).padStart(2)} u/s   peak off-boresight ` +
                `${s.bore.toFixed(1).padStart(5)} deg   ${s.lost ? 'LOCK LOST' : 'lock held'}   ${s.result}`);
  console.log(`  seeker cone is ${MISSILE.seekerConeDeg} deg; ` +
              `theoretical lead-angle bound is asin(Vt/Vm), ` +
              `${(Math.asin(Math.min(1, 15 / MISSILE.projectileSpeed)) * 180 / Math.PI).toFixed(0)} deg at 15 u/s\n`);

  ok('the seeker cone is wide enough that an ordinary launch never breaks its own lock',
     sweep.filter(s => s.sp <= 16).every(s => !s.lost),
     `peak bearing against ordinary hulls stays under ${Math.max(...sweep.filter(s => s.sp <= 16).map(s => s.bore)).toFixed(0)} deg`);
  ok('and a healthy intercept is never mistaken for a lost target',
     sweep.every(s2 => !s2.lost && s2.result === 'HIT'),
     sweep.every(s2 => !s2.lost) ? 'every hull speed in the sweep holds lock and lands'
                                 : sweep.filter(s2 => s2.lost).map(s2 => `${s2.sp} u/s lost lock`).join(', '));
}

// ---- 6: the torpedo ---------------------------------------------------------------------------
{
  const tgt = makeTarget({ pos: v(44, 0, 10), dir: v(-0.6, 0, -1), speed: 5, turnDeg: 12 });
  const run = engage({ weapon: TORPEDO, from: v(0, 0, 0), target: tgt });
  panels.push({ title: 'Torpedo against a capital hull', runs: [run] });

  ok('a torpedo runs down a slow capital', run.result === 'HIT',
     `${run.result} at ${run.age.toFixed(2)}s over ${(run.age * TORPEDO.projectileSpeed).toFixed(0)}u`);
}

// ============================================================================================
// THE CLAIMS THAT ARE NUMBERS RATHER THAN PICTURES
// ============================================================================================

// The quadratic, against the one case with a closed form: a stationary target is hit at exactly d/v.
{
  const t = interceptTime(v(0, 0, 0), v(30, 0, 0), v(0, 0, 0), 60);
  ok('intercept time on a stationary target is d/v', Math.abs(t - 0.5) < 1e-5, `${t.toFixed(6)}s`);
}

// A head-on target must be hit sooner than a stationary one at the same range, and a fleeing one later.
{
  const head = interceptTime(v(0, 0, 0), v(30, 0, 0), v(-10, 0, 0), 60);
  const still = interceptTime(v(0, 0, 0), v(30, 0, 0), v(0, 0, 0), 60);
  const away = interceptTime(v(0, 0, 0), v(30, 0, 0), v(10, 0, 0), 60);
  ok('closing shortens the solution and fleeing lengthens it', head < still && still < away,
     `head-on ${head.toFixed(3)}s, static ${still.toFixed(3)}s, fleeing ${away.toFixed(3)}s`);
}

// Turn radius is quadratic in speed. Stated because it is the single fact the whole missile design
// rests on, and a linear implementation would still hit things in the panels above.
{
  const r1 = turnRadius(20, MISSILE.lateralAccel);
  const r2 = turnRadius(40, MISSILE.lateralAccel);
  ok('doubling speed quadruples turn radius', Math.abs(r2 / r1 - 4) < 1e-6,
     `${r1.toFixed(1)}u at 20 u/s -> ${r2.toFixed(1)}u at 40 u/s`);
}

// A missile must out-corner the hull it is chasing or it is decoration. A fighter at 12 u/s under the
// ShipPhysics rules holds about a 23-unit circle; the missile has to beat that at its cruise.
{
  const rMissile = turnRadius(MISSILE.projectileSpeed, MISSILE.lateralAccel);
  const rTorpedo = turnRadius(TORPEDO.projectileSpeed, TORPEDO.lateralAccel);
  ok('a missile corners inside a fighter (~23u)', rMissile < 20,
     `missile ${rMissile.toFixed(1)}u at ${MISSILE.projectileSpeed} u/s`);
  ok('a torpedo corners like a missile at half the speed', Math.abs(rTorpedo - rMissile) < 8,
     `torpedo ${rTorpedo.toFixed(1)}u at ${TORPEDO.projectileSpeed} u/s`);
}

// Lead matters. How far behind the target an unled pulse bolt lands, at the ranges it is fired at —
// the number that made this whole rewrite necessary.
{
  const range = 22, cross = 12;
  const tof = range / PULSE.projectileSpeed;
  const behind = cross * tof;
  ok('unled fire misses a crossing target by many hull widths', behind > 1.5,
     `${behind.toFixed(2)}u behind at ${range}u — hulls are ~0.33u across, so ~${Math.round(behind / 0.33)} hull widths`);
}

// A seeker that arms before its motor is up to speed will look at a healthy intercept and decide the
// target has left its field of view — see the seekerArmTime comment in Weaponry. This is the guard
// that stops anyone re-introducing that by lengthening a boost phase and not noticing.
{
  const seekers = Object.values(W).filter(w => w.guidance !== 'Unguided' && w.lateralAccel > 0 && w.seekerConeDeg < 179);
  const bad = seekers.filter(w => w.seekerArmTime < w.fuelTime &&
                                 w.seekerArmTime < w.boostTime);
  ok('every seeker arms only after its motor is up to speed', bad.length === 0,
     bad.length ? bad.map(w => `${w.name}: arms ${w.seekerArmTime}s, boosts until ${w.boostTime}s`).join('; ')
                : seekers
                    .map(w => `${w.name} ${w.seekerArmTime}s > ${w.boostTime}s`).join(', '));
}

// A missile has to be able to outlast its own range or it can never chase anything.
{
  const powered = MISSILE.boostTime * (MISSILE.launchSpeed + MISSILE.projectileSpeed) / 2 +
                  (MISSILE.fuelTime - MISSILE.boostTime) * MISSILE.projectileSpeed;
  ok('powered flight comfortably exceeds the mount\'s range',
     powered > MISSILE.range * 1.8,
     `${powered.toFixed(0)}u of burn against a ${MISSILE.range}u envelope`);
}

// ============================================================================================
// RENDER
// ============================================================================================
const CW = 380, CH = 300, COLS = 3, PAD = 34;
const ROWS = Math.ceil(panels.length / COLS);
const SW = COLS * CW, SH = ROWS * CH + 26;

function drawPanel(p, ox, oy) {
  const pts = [];
  for (const r of p.runs) { pts.push(...r.track, ...r.target.track); }
  let minX = Infinity, maxX = -Infinity, minZ = Infinity, maxZ = -Infinity;
  for (const q of pts) {
    minX = Math.min(minX, q.x); maxX = Math.max(maxX, q.x);
    minZ = Math.min(minZ, q.z); maxZ = Math.max(maxZ, q.z);
  }
  const spanX = Math.max(1, maxX - minX), spanZ = Math.max(1, maxZ - minZ);
  const s = Math.min((CW - PAD * 2) / spanX, (CH - PAD * 2 - 22) / spanZ);
  const X = q => ox + PAD + (q.x - minX) * s;
  const Y = q => oy + PAD + 14 + (q.z - minZ) * s;
  const poly = t => t.map(q => `${X(q).toFixed(1)},${Y(q).toFixed(1)}`).join(' ');

  let g = `<rect x="${ox + 4}" y="${oy + 4}" width="${CW - 8}" height="${CH - 8}" rx="6"
              fill="#12161c" stroke="#2b3540"/>`;
  g += `<text x="${ox + 16}" y="${oy + 24}" fill="#cfe0ee" font-family="monospace" font-size="13">${p.title}</text>`;

  for (const r of p.runs) {
    g += `<polyline points="${poly(r.target.track)}" fill="none" stroke="#5d7a92" stroke-width="1.6" stroke-dasharray="5 4"/>`;
    g += `<polyline points="${poly(r.track)}" fill="none" stroke="${r.result === 'HIT' ? '#ffd23f' : '#e2664f'}" stroke-width="2.1"/>`;
    // launch, and the two events worth marking on the round's own track
    g += `<circle cx="${X(r.track[0])}" cy="${Y(r.track[0])}" r="4" fill="#9fe8b0"/>`;
    g += `<circle cx="${X(r.target.track[0])}" cy="${Y(r.target.track[0])}" r="3.4" fill="none" stroke="#5d7a92" stroke-width="1.6"/>`;
    if (r.burnoutAt) g += `<circle cx="${X(r.burnoutAt.pos)}" cy="${Y(r.burnoutAt.pos)}" r="4.5" fill="none" stroke="#ff9f1c" stroke-width="2"/>`;
    if (r.lockLostAt) g += `<rect x="${X(r.lockLostAt.pos) - 4}" y="${Y(r.lockLostAt.pos) - 4}" width="8" height="8" fill="none" stroke="#c86bff" stroke-width="2"/>`;
    if (r.hitAt) g += `<circle cx="${X(r.hitAt)}" cy="${Y(r.hitAt)}" r="6" fill="none" stroke="#ffd23f" stroke-width="2.4"/>`;

    const label = r.declined ? `${r.result} — no solution, the gun holds fire` : `${r.result}  ${r.age.toFixed(2)}s  miss ${r.closest.toFixed(2)}u`;
    g += `<text x="${ox + 16}" y="${oy + CH - 16}" fill="#8fa3b5" font-family="monospace" font-size="11">${label}</text>`;
  }
  return g;
}

let svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${SW}" height="${SH}">
  <rect width="${SW}" height="${SH}" fill="#0b0e13"/>`;
panels.forEach((p, i) => { svg += drawPanel(p, (i % COLS) * CW, Math.floor(i / COLS) * CH); });
svg += `<text x="12" y="${SH - 8}" fill="#6b7d8e" font-family="monospace" font-size="11">`;
svg += `green dot = launch   dashed = target   orange ring = burnout   violet square = lock lost   `;
svg += `yellow ring = hit   dt=1/60s</text></svg>`;

fs.mkdirSync(path.dirname(OUT), { recursive: true });
await sharp(Buffer.from(svg)).png().toFile(OUT);

// ============================================================================================
// REPORT
// ============================================================================================
console.log(`weapons parsed from Weaponry.cs: ${Object.keys(W).length}\n`);

const guided = Object.values(W).filter(w => w.guidance !== 'Unguided' && w.lateralAccel > 0);
console.log('guided mounts');
for (const w of guided) {
  console.log(`  ${w.name.padEnd(14)} ${String(w.projectileSpeed).padStart(4)} u/s   ` +
              `turn radius ${turnRadius(w.projectileSpeed, w.lateralAccel).toFixed(1).padStart(5)}u   ` +
              `${turnRateAt(w, w.projectileSpeed).toFixed(0).padStart(3)} deg/s   ` +
              `burn ${w.fuelTime}s of ${w.trackTime}s   cone ${w.seekerConeDeg} deg`);
}

console.log('\nunguided mounts — flight time and how far a 12 u/s crosser moves in it');
for (const w of Object.values(W)) {
  if (w.guidance !== 'Unguided' || !w.projectileSpeed) continue;
  const tof = w.range / w.projectileSpeed;
  console.log(`  ${w.name.padEnd(14)} ${String(w.projectileSpeed).padStart(4)} u/s   ` +
              `${tof.toFixed(2)}s at max range   target moves ${(tof * 12).toFixed(2)}u`);
}

console.log('');
let failed = 0;
for (const c of checks) {
  if (!c.pass) failed++;
  console.log(`${c.pass ? 'ok  ' : 'FAIL'} ${c.name}`);
  console.log(`     ${c.detail}`);
}

console.log(`\n${path.relative(PROJ, OUT)}`);
console.log(failed ? `\n${failed} of ${checks.length} checks FAILED.` : `\nAll ${checks.length} checks pass.`);
process.exit(failed ? 1 : 0);
