using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// WHAT A SHIP HAS LEFT TO SHOOT WITH
//
// Two resources, and the whole point of the file is that they behave nothing alike.
//
//   THE CAPACITOR is reactor charge. It drains as energy mounts fire and refills continuously, so it
//   is a rate limiter and never a supply problem. A ship that opens with everything at once will find
//   its guns drooping thirty seconds in and back to full a minute after that. It cannot be starved,
//   only outpaced — and a fleet of laser hulls can therefore sit in hostile space forever.
//
//   MAGAZINES are physical rounds. They drain and they DO NOT COME BACK on their own. A dreadnought
//   carries a specific number of torpedoes into a battle and when they are gone the tube is silent
//   until somebody hands it more. That is the entire reason forward stations, carriers and the
//   distance between them are worth thinking about.
//
// ---- WHY THIS IS NOT SAVED --------------------------------------------------------------------
//
// Same reasoning as CombatManager's cooldowns, and the same hazard if it were: this is runtime
// bookkeeping keyed on live Unit references. Restoring a fleet mid-reload, or holding dead Units alive
// in a dictionary across a galaxy replacement, are both worse than the thing being restored. A ship
// loaded from a save comes back with full magazines, which is a small mercy and never a surprise.
//
// ---- THE FAILURE MODE THIS IS BUILT TO AVOID --------------------------------------------------
//
// A supply system can very easily produce a death spiral: the player who is already losing is a long
// way from home, so they run out of ammunition, so they lose harder. Three deliberate choices stop
// that from happening here:
//
//   1. POINT DEFENCE IS ENERGY-FED. A fleet out of supply never loses its ability to defend itself.
//      Only its ability to threaten.
//   2. EVERY WARSHIP KEEPS AN ENERGY MOUNT. Look at the loadouts in Weaponry — there is no hull whose
//      guns all fall silent when the magazines are empty. A dry cruiser is a weak cruiser, not a
//      passenger.
//   3. REARMING NEEDS NO ORDER. Park near anything friendly that can supply and it happens. The player
//      manages a supply LINE, never a supply CHORE.
// ============================================================================================
public static class Magazines
{
    /// Everything one hull is carrying. Arrays are parallel to the unit's loadout from Weaponry.For,
    /// so index k here is mount k there — the same convention CombatManager's cooldown array uses.
    class Load
    {
        public WeaponInfo[] loadout;
        public float[] rounds;          // ordnance remaining per mount; meaningless for energy mounts
        public float[] capacity;        // what full looks like for that mount, after the hull multiplier
        public float charge;            // capacitor now
        public float chargeMax;
        public float resupplyGlow;      // seconds left on the "being rearmed" indicator, for the UI
        public bool warnedDry;          // so a fleet running out notifies once, not sixty times a second
    }

    static readonly Dictionary<Unit, Load> loads = new Dictionary<Unit, Load>();

    // ============================================================================================
    // THE NUMBERS
    // ============================================================================================

    /// Seconds to refill an empty capacitor from nothing. Long enough that an alpha strike costs
    /// something, short enough that it is never why a fight was lost.
    const float CapacitorRefillSeconds = 9f;

    /// How much of a magazine is restored per second while in supply. A tenth means a full rearm takes
    /// ten seconds of sitting still, which is long enough to feel like a decision and short enough
    /// that nobody goes to make tea.
    const float RearmFractionPerSecond = 0.1f;

    /// How close a hull has to be to a supply source. A station reaches much further than a carrier
    /// because it is a fixed installation with tenders of its own, and that difference is the reason
    /// to build one somewhere forward.
    const float StationSupplyRange = 26f;
    const float CarrierSupplyRange = 14f;

    /// How long the rearming indicator stays lit after the last tick of resupply, so it reads as a
    /// steady light rather than a flicker.
    const float ResupplyGlowSeconds = 0.6f;

    // ============================================================================================
    // CAPACITY
    // ============================================================================================

    /// How much charge a hull holds. Built from stats it already has, for the same reason ShipPhysics
    /// builds mass out of hull integrity: a table nobody has to keep in step cannot fall out of step.
    /// Attack dominates, because a ship's capacitor is sized for its guns; health contributes a little,
    /// because a bigger hull has room for a bigger bank.
    public static float CapacitorMax(Unit u)
    {
        var i = u?.Info;
        if (i == null) return 40f;
        return 40f + i.attack * 2.2f + i.health * 0.05f;
    }

    /// Charge returned per second.
    public static float RechargeRate(Unit u) => CapacitorMax(u) / CapacitorRefillSeconds;

    /// How many times a mount's base magazine this hull carries.
    ///
    /// A fighter and a dreadnought mounting the same rack should not carry the same number of rounds,
    /// and hull integrity is the only size-like number every class has. Clamped at both ends so a probe
    /// is not carrying negative missiles and a mega-station is not carrying a thousand.
    public static float MagazineScale(Unit u)
    {
        var i = u?.Info;
        if (i == null) return 1f;
        return Mathf.Clamp(0.5f + i.health / 500f, 0.5f, 4f);
    }

    // ============================================================================================
    // ASKING AND SPENDING
    // ============================================================================================

    static Load LoadFor(Unit u)
    {
        if (u == null) return null;
        if (loads.TryGetValue(u, out var l) && l.loadout != null) return l;

        var loadout = Weaponry.For(u.type);
        float scale = MagazineScale(u);
        l = new Load
        {
            loadout = loadout,
            rounds = new float[loadout.Length],
            capacity = new float[loadout.Length],
            chargeMax = CapacitorMax(u),
        };
        l.charge = l.chargeMax;
        for (int k = 0; k < loadout.Length; k++)
        {
            var w = loadout[k];
            if (w == null || w.ammo != AmmoKind.Ordnance) continue;
            l.capacity[k] = Mathf.Max(1f, w.magazine * scale);
            l.rounds[k] = l.capacity[k];
        }
        loads[u] = l;
        return l;
    }

    /// Can mount `k` afford to fire right now?
    ///
    /// A salvo is all-or-nothing on purpose: a rack with one missile left does not fire a half-salvo,
    /// it fires nothing and reports empty. Partial salvos would mean the last round of every magazine
    /// leaves at a different damage figure from all the ones before it, which is invisible to the
    /// player and impossible to reason about.
    public static bool CanFire(Unit u, int k, WeaponInfo w)
    {
        if (w == null) return false;
        var l = LoadFor(u);
        if (l == null || k < 0 || k >= l.loadout.Length) return false;

        if (w.ammo == AmmoKind.Energy)
            return l.charge >= w.energyPerShot * Mathf.Max(1, w.salvo);

        return l.rounds[k] >= Mathf.Max(1, w.salvo);
    }

    /// Spend what one trigger pull costs. Call only after CanFire said yes.
    public static void Consume(Unit u, int k, WeaponInfo w)
    {
        if (w == null) return;
        var l = LoadFor(u);
        if (l == null || k < 0 || k >= l.loadout.Length) return;

        int n = Mathf.Max(1, w.salvo);
        if (w.ammo == AmmoKind.Energy) l.charge = Mathf.Max(0f, l.charge - w.energyPerShot * n);
        else                           l.rounds[k] = Mathf.Max(0f, l.rounds[k] - n);
    }

    // ============================================================================================
    // READOUTS, FOR THE UI
    // ============================================================================================

    /// Capacitor charge, 0 to 1. Always 1 for a hull with no energy mounts, which keeps the UI from
    /// drawing an empty bar on a transport.
    public static float EnergyFraction(Unit u)
    {
        var l = LoadFor(u);
        if (l == null || l.chargeMax <= 0.001f) return 1f;
        return Mathf.Clamp01(l.charge / l.chargeMax);
    }

    /// The FULLEST ordnance mount, 0 to 1. Returns 1 for a hull that carries no ordnance at all.
    ///
    /// Fullest rather than emptiest, and it took getting this the wrong way round once to see why. A
    /// dreadnought carries four torpedoes and four hundred autocannon rounds; on the emptiest reading,
    /// firing the four torpedoes shows an empty ammunition bar on a ship with a nearly untouched
    /// magazine, and the player reads "out of ammunition" when what happened is "out of the scarce
    /// thing". Per-mount detail belongs in the roster panel, not in one summary bar, and the summary
    /// should answer "can this ship still shoot" — which is the fullest mount.
    public static float AmmoFraction(Unit u)
    {
        var l = LoadFor(u);
        if (l == null) return 1f;
        float best = -1f;
        for (int k = 0; k < l.loadout.Length; k++)
        {
            var w = l.loadout[k];
            if (w == null || w.ammo != AmmoKind.Ordnance || l.capacity[k] <= 0.001f) continue;
            best = Mathf.Max(best, l.rounds[k] / l.capacity[k]);
        }
        return best < 0f ? 1f : Mathf.Clamp01(best);
    }

    /// Does this hull carry any ordnance at all? The UI uses it to decide whether an ammunition bar
    /// belongs on this ship's row.
    public static bool CarriesOrdnance(Unit u)
    {
        var l = LoadFor(u);
        if (l == null) return false;
        for (int k = 0; k < l.loadout.Length; k++)
            if (l.capacity[k] > 0.001f) return true;
        return false;
    }

    /// Rounds left in one mount, and what full looks like. For the per-mount readout.
    public static void MountAmmo(Unit u, int k, out float have, out float full)
    {
        have = 0f; full = 0f;
        var l = LoadFor(u);
        if (l == null || k < 0 || k >= l.loadout.Length) return;
        have = l.rounds[k]; full = l.capacity[k];
    }

    /// The weakest ordnance mount across a group, or -1 if nothing in it carries any.
    ///
    /// WEAKEST here, where a single hull reports its FULLEST — and the two are opposites on purpose.
    /// Asked about one ship, the useful question is "can it still shoot", which is its best mount.
    /// Asked about a squadron, the useful question is "is anything in here out", which is its worst.
    /// A formation is only as supplied as the ship in it that has run dry.
    public static float GroupSupply(System.Collections.Generic.IReadOnlyList<Unit> group)
    {
        if (group == null) return -1f;
        float worst = -1f;
        for (int i = 0; i < group.Count; i++)
        {
            var u = group[i];
            if (u == null || u.IsDestroyed || !CarriesOrdnance(u)) continue;
            float f = AmmoFraction(u);
            if (worst < 0f || f < worst) worst = f;
        }
        return worst;
    }

    /// A few lines describing what this hull is carrying, for a tooltip. Built here rather than in the
    /// UI so nothing outside this file has to know what a magazine is.
    public static string SupplyReport(Unit u)
    {
        var l = LoadFor(u);
        if (l == null || l.loadout.Length == 0) return "Unarmed.";

        var sb = new System.Text.StringBuilder();
        sb.Append($"Capacitor {EnergyFraction(u) * 100f:F0}%");
        if (Resupplying(u)) sb.Append("   <color=#7FD46A>rearming</color>");

        for (int k = 0; k < l.loadout.Length; k++)
        {
            var w = l.loadout[k];
            if (w == null) continue;
            sb.Append('\n');
            if (w.ammo == AmmoKind.Energy)
                sb.Append($"  {w.name} — reactor-fed");
            else
                sb.Append($"  {w.name} — {Mathf.FloorToInt(l.rounds[k])} / {Mathf.FloorToInt(l.capacity[k])} rounds");
        }
        return sb.ToString();
    }

    /// Is this hull being rearmed right now?
    public static bool Resupplying(Unit u)
    {
        var l = LoadFor(u);
        return l != null && l.resupplyGlow > 0f;
    }

    // ============================================================================================
    // THE TICK
    // ============================================================================================

    static readonly List<Unit> suppliers = new List<Unit>();

    /// Recharge capacitors and rearm anything sitting in supply. Driven from CombatManager.Update
    /// rather than from a MonoBehaviour of its own — combat already ticks every frame with the unit
    /// list in hand, and a second manager object would be a second thing to create, reset and forget
    /// to reset.
    ///
    /// Runs on SCALED time, like cooldowns and unlike the projectile renderer: reloading is part of the
    /// simulation, so a paused game does not reload and a fast-forwarded one reloads fast.
    public static void Tick(float dt, IReadOnlyList<Unit> all)
    {
        if (dt <= 0f || all == null) return;

        // ---- who can hand out ammunition -------------------------------------------------------
        //
        // Gathered once per tick rather than searched per ship. In a fleet of two hundred with four
        // stations, the alternative is eight hundred distance checks a frame against a list that has
        // not changed.
        suppliers.Clear();
        for (int i = 0; i < all.Count; i++)
        {
            var s = all[i];
            if (s == null || s.IsDestroyed || s.owner == null) continue;
            if (IsSupplier(s)) suppliers.Add(s);
        }

        for (int i = 0; i < all.Count; i++)
        {
            var u = all[i];
            if (u == null || u.IsDestroyed) continue;

            var l = LoadFor(u);
            if (l == null || l.loadout.Length == 0) continue;

            // ---- capacitor: always, everywhere, no conditions ----
            if (l.charge < l.chargeMax)
                l.charge = Mathf.Min(l.chargeMax, l.charge + RechargeRate(u) * dt);

            if (l.resupplyGlow > 0f) l.resupplyGlow -= dt;

            // ---- magazines: only in supply ----
            if (!CarriesOrdnance(u)) continue;
            if (!InSupply(u)) { WarnIfDry(u, l); continue; }

            bool tookAny = false;
            for (int k = 0; k < l.loadout.Length; k++)
            {
                if (l.capacity[k] <= 0.001f) continue;
                if (l.rounds[k] >= l.capacity[k]) continue;
                l.rounds[k] = Mathf.Min(l.capacity[k],
                                        l.rounds[k] + l.capacity[k] * RearmFractionPerSecond * dt);
                tookAny = true;
            }
            if (tookAny)
            {
                l.resupplyGlow = ResupplyGlowSeconds;
                l.warnedDry = false;      // rearmed once, so it is allowed to warn again next time
            }
        }
    }

    /// Can this hull supply others? Stations because they are installations, carriers because carrying
    /// things is the entire idea of a carrier — and giving it that job is what stops the class from
    /// being a dreadnought with worse guns.
    static bool IsSupplier(Unit s)
    {
        var i = s.Info;
        if (i == null) return false;
        if (i.isStation) return true;
        return s.type == UnitType.Carrier;
    }

    /// Is this hull somewhere it can be rearmed?
    ///
    /// Three ways, cheapest test first:
    ///   * parked at a settled world its owner holds — a colony has magazines;
    ///   * within reach of a friendly station;
    ///   * within reach of a friendly carrier.
    ///
    /// A ship under way is never in supply, whatever it is near. Rearming at speed would make the
    /// whole system decorative, and "stop and take on ordnance" is the decision worth having.
    static bool InSupply(Unit u)
    {
        if (u.status == UnitStatus.Traveling) return false;

        var here = u.location;
        if (here != null && here.settled && here.owner != null && here.owner == u.owner) return true;

        Vector3 p = CombatManager.PosOf(u);
        for (int i = 0; i < suppliers.Count; i++)
        {
            var s = suppliers[i];
            if (s == u || s.owner != u.owner) continue;
            if (s.status == UnitStatus.Traveling) continue;

            float reach = (s.Info != null && s.Info.isStation) ? StationSupplyRange : CarrierSupplyRange;
            if (Vector3.SqrMagnitude(CombatManager.PosOf(s) - p) <= reach * reach) return true;
        }
        return false;
    }

    /// Tell the player once when one of their warships has emptied a magazine with nowhere to fill it.
    ///
    /// Once, and only for hulls that are actually dry — a ship at 40% is not news, and a notification
    /// per frame is how a supply system becomes the reason somebody turns notifications off.
    static void WarnIfDry(Unit u, Load l)
    {
        if (l.warnedDry) return;
        if (u.owner != FactionManager.Player) return;

        bool anyDry = false, anyLeft = false;
        for (int k = 0; k < l.loadout.Length; k++)
        {
            if (l.capacity[k] <= 0.001f) continue;
            if (l.rounds[k] < Mathf.Max(1, l.loadout[k].salvo)) anyDry = true;
            else anyLeft = true;
        }
        if (!anyDry) return;

        l.warnedDry = true;
        string what = anyLeft ? "has emptied a magazine" : "is out of ordnance";
        NotificationManager.Instance?.Push($"{u.name} {what}",
            "Return to a colony, a station or a carrier to rearm. Energy mounts are unaffected.",
            null, NotifKind.Danger);
    }

    /// Forget everything. Called from CombatManager.ResetAll when a galaxy is replaced — the keys are
    /// Units that are about to stop existing, and this dictionary would otherwise hold them alive.
    public static void ResetAll()
    {
        loads.Clear();
        suppliers.Clear();
    }

    /// Refill one hull completely. Used when a ship is built and when the ledger hands one out, so a
    /// new warship never arrives half-loaded.
    public static void FillUp(Unit u)
    {
        var l = LoadFor(u);
        if (l == null) return;
        l.charge = l.chargeMax;
        for (int k = 0; k < l.loadout.Length; k++) l.rounds[k] = l.capacity[k];
        l.warnedDry = false;
    }
}
