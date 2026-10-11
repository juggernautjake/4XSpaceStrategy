using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// SHIELDS — a second pool of hit points in front of the hull
//
// THE RULES (deliberately few, and all here):
//
//   * CAPACITY is a share of the hull's own maximum, so it scales with class and rank exactly as
//     health does and no per-class table can drift out of step:
//         warships (armed, not civilian)   30% of max hull
//         stations                         40%
//         civilian / science / colony      15%
//         probes                           none
//   * A hit lands on the shield FIRST. The shield ignores armour — it is an energy screen, not a
//     plate — but weapons are not equally good against it (VsShield): beams and pulse lasers bleed it
//     a little faster (x1.2 / x1.1), kinetic rounds are partly deflected (railgun x0.75, autocannon
//     x0.8), plasma, missiles and point defence are even (x1.0), and a torpedo's warhead is built for
//     hulls (x0.9). Whatever the shield could not hold carries on to the hull, through armour, as
//     before. A round the shield absorbs completely never touches the hull.
//   * REGENERATION: once a ship has gone RegenDelay (6) seconds — days — without being hit AND is out
//     of combat, the shield refills at RegenPerSecond (10% of capacity a second), so a full recharge
//     takes ten days of quiet. There is no regen while a fight is on: a shield is something you spend
//     in an engagement and recover between them.
//   * A shield driven to zero COLLAPSES (the visual shatters) and stays down until the regen rule
//     brings it back, rising from the keel up.
//
// WHY THESE NUMBERS. Shields add 15-40% to a hull's effective life, which lengthens fights without
// changing who wins them: the ratio between two fleets is unchanged when both get the same share, and
// armour still matters because everything that gets past a shield meets it. The weapon multipliers
// are small enough that loadouts keep their roles (railguns still kill dreadnoughts, they just have
// to break the screen first) and big enough that a beam-heavy fighter screen has a job — stripping
// shields — before the heavy guns arrive. Stations get the largest share because they cannot dodge.
//
// STATE is runtime only (Unit.shield / shieldHitAt, NonSerialized): a loaded game starts with every
// screen full, which is what an unsaved recharge would have reached anyway.
// ============================================================================================
public static class Shields
{
    public const float WarshipShare = 0.30f, StationShare = 0.40f, CivilShare = 0.15f;
    public const float RegenDelay = 6f;
    public const float RegenPerSecond = 0.10f;

    /// Game seconds since the session began, on the scaled clock combat runs on.
    public static float Clock { get; private set; }

    public static int MaxFor(Unit u)
    {
        var info = u?.Info;
        if (info == null || info.isProbe) return 0;
        float share = info.isStation ? StationShare
                    : (info.attack > 0 && !info.isWorker && !info.canColonize) ? WarshipShare
                    : CivilShare;
        return Mathf.RoundToInt(u.EffectiveHealth * share);
    }

    /// How well a weapon class drains a shield, as a multiplier on its damage.
    public static float VsShield(WeaponInfo w)
    {
        if (w == null) return 1f;
        switch (w.cls)
        {
            case WeaponClass.BeamLaser: return 1.2f;
            case WeaponClass.PulseLaser: return 1.1f;
            case WeaponClass.Railgun: return 0.75f;
            case WeaponClass.Autocannon: return 0.8f;
            case WeaponClass.Torpedo: return 0.9f;
            default: return 1f;
        }
    }

    /// Put `damage` (the round's raw damage, before armour) against this unit's shield. Returns what is
    /// left over for the hull, in raw damage. `absorbed` is shield points taken; `collapsed` is true if
    /// this hit took the shield from up to down.
    public static float Absorb(Unit t, WeaponInfo w, float damage, out float absorbed, out bool collapsed)
    {
        absorbed = 0f; collapsed = false;
        if (t == null || damage <= 0f) return damage;
        t.shieldHitAt = Clock;
        float have = t.Shield;
        if (have <= 0.01f) return damage;

        float k = VsShield(w);
        float eff = damage * k;
        absorbed = Mathf.Min(have, eff);
        t.Shield = have - absorbed;
        if (t.Shield <= 0.01f) { t.Shield = 0f; collapsed = true; t.shieldDownAt = Clock; }
        return Mathf.Max(0f, (eff - absorbed) / Mathf.Max(0.01f, k));
    }

    /// Advance the clock and refill every quiet shield. Called by CombatManager with the live list.
    public static void Tick(float dt, List<Unit> units)
    {
        Clock += dt;
        if (units == null) return;
        for (int i = 0; i < units.Count; i++)
        {
            var u = units[i];
            if (u == null || u.IsDestroyed) continue;
            int max = u.MaxShield;
            if (max <= 0) continue;
            float s = u.Shield;
            if (s >= max) continue;
            if (Clock - u.shieldHitAt < RegenDelay) continue;
            if (CombatManager.InCombat(u)) continue;
            u.Shield = s + max * RegenPerSecond * dt;
        }
    }

    public static void Reset() { Clock = 0f; }

    /// The shield left across a group, as one 0..1 (points summed, like Fleets.ConditionOf), or -1 if
    /// nothing in the group carries a shield — so a roster can leave the bar off rather than show an
    /// empty one for a probe flight.
    public static float GroupFraction(IReadOnlyList<Unit> ships)
    {
        if (ships == null) return -1f;
        float have = 0f, max = 0f;
        for (int i = 0; i < ships.Count; i++)
        {
            var u = ships[i];
            if (u == null || u.IsDestroyed) continue;
            int m = u.MaxShield;
            if (m <= 0) continue;
            have += u.Shield;
            max += m;
        }
        return max <= 0f ? -1f : Mathf.Clamp01(have / max);
    }

    /// The colour every shield bar is drawn in: a screen blue, dimming toward violet as it fails.
    public static Color BarColour(float f)
        => Color.Lerp(new Color(0.55f, 0.45f, 0.95f), new Color(0.40f, 0.78f, 1.00f), Mathf.Clamp01(f));
}
