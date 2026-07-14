using UnityEngine;

// Applies the passive effects of DEPLOYED stations and civilian workers: research/supply/mining auras
// feed the economy, and the relay network (relay / deep-space / mega / hyper-speed stations) extends
// and quickens fleet travel empire-wide. A unit counts as "deployed" whenever it is anchored (not in
// transit) — at a body, or parked in open space (deep-space stations). Ticked every frame by
// ColonyManager; terraforming stations feed ColonyManager.TickTerraform via TerraformAuraAt().
public static class StationEffects
{
    static float researchCarry = 0f;   // fractional research points awaiting a whole-point flush

    // Anchored, player-owned, and not travelling.
    static bool Deployed(Unit u)
        => u != null && u.owner == FactionManager.Player && u.status != UnitStatus.Traveling
           && (u.location != null || u.inSpace);

    public static void Tick(float dt)
    {
        var um = UnitManager.Instance;
        if (um == null) { ShipUpgrades.RelayRange = 1f; ShipUpgrades.SpeedMult = 1f; return; }

        float metal = 0f, energy = 0f, research = 0f, relay = 0f;
        foreach (var u in um.Units)
        {
            if (!Deployed(u)) continue;
            var info = u.Info;
            if (info.mineBonus > 0f && u.location != null) metal += info.mineBonus;   // mining barges work a world
            if (info.supplyBonus > 0f) { metal += info.supplyBonus; energy += info.supplyBonus; }
            if (info.researchAura > 0f) research += info.researchAura;
            if (info.relayBoost > 0f) relay += info.relayBoost;
        }

        if (metal != 0f) PlayerEconomy.Add(ResourceType.Metal, metal * dt);
        if (energy != 0f) PlayerEconomy.Add(ResourceType.Energy, energy * dt);
        if (research > 0f)
        {
            researchCarry += research * TechEffects.ResearchRateMult * dt;
            int whole = Mathf.FloorToInt(researchCarry);
            if (whole > 0) { researchCarry -= whole; ResearchManager.AddPoints(whole); }
        }

        // Relay network: each point of relayBoost lengthens range and quickens travel across the galaxy.
        ShipUpgrades.RelayRange = 1f + relay;
        ShipUpgrades.SpeedMult = 1f + relay * 0.75f;
    }

    // Total terraform-speed contribution from terraforming stations (and mega-stations) anchored at
    // this body — added by ColonyManager on top of terraformer ships.
    public static float TerraformAuraAt(CelestialBody body)
    {
        var um = UnitManager.Instance;
        if (um == null || body == null) return 0f;
        float sum = 0f;
        foreach (var u in um.Units)
            if (Deployed(u) && u.location == body && u.Info.terraformAura > 0f) sum += u.Info.terraformAura;
        return sum;
    }

    // Count of deployed relay-type stations (for UI / status readouts).
    public static int ActiveRelayCount()
    {
        var um = UnitManager.Instance;
        if (um == null) return 0;
        int n = 0;
        foreach (var u in um.Units)
            if (Deployed(u) && u.Info.relayBoost > 0f) n++;
        return n;
    }

    public static void Reset() { researchCarry = 0f; ShipUpgrades.RelayRange = 1f; ShipUpgrades.SpeedMult = 1f; }
}
