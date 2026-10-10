using UnityEngine;

// ============================================================================================
// ONE WORLD'S BOOKS — income, upkeep and net, per resource (2026-10-09)
//
// For the production strip along the bottom of the Planet View's Overview. Read straight off the same
// formulas the economy actually runs (SurfaceBuildManager.TickOutput, PowerGrid.Compute, Carrying), so
// the strip can never quote a number the world is not really earning.
//
//   Metal  — mines and refineries: metalPerSec x siting x tech level x power x ore-yield research.
//   Water  — hydro and the like.
//   Energy — production is every generator's output (with the Power Distribution adjacency bonus, as
//            the grid computes it); upkeep is every consumer's draw.
//   Food   — how many people the farms can feed, against how many there are (Carrying). A capacity,
//            not a per-second flow, and labelled that way.
//
// UPKEEP: energy drawn by consumers that are on a grid (as PowerGrid counts it), and — while the world
// is terraforming — the water, energy and metal ColonyManager.TickTerraform burns each second.
// ============================================================================================
public static class WorldEconomy
{
    public struct Books
    {
        public float metal, water;
        public float energyMade, energyUsed;
        public float metalUpkeep, waterUpkeep;
        public float foodMade, foodEaten;
        public float EnergyNet => energyMade - energyUsed;
        public float FoodNet => foodMade - foodEaten;
    }

    public static Books Of(CelestialBody b)
    {
        var k = new Books();
        if (b == null) return k;

        foreach (var p in SurfaceBuildManager.On(b))
        {
            var info = p.Info;
            if (info == null) continue;
            float run = p.OutputMult * PowerGrid.PowerFactor(b, p);
            if (info.metalPerSec > 0f) k.metal += info.metalPerSec * run * TechEffects.OreYieldMult;
            if (info.waterPerSec > 0f) k.water += info.waterPerSec * run;
            if (info.energyPerSec > 0f)
                k.energyMade += info.energyPerSec * p.OutputMult * (1f + SurfaceBuildManager.AdjacencyBonus(b, p));
            // Only consumers on a grid draw — exactly what PowerGrid.Compute adds to a net's load.
            if (info.powerDraw > 0f && PowerGrid.NetOf(b, p) != null) k.energyUsed += info.powerDraw * p.LevelMult;
        }

        // Terraforming's running cost, per second, mirroring ColonyManager.TickTerraform.
        if (b.terraforming)
        {
            float rate = Mathf.Min(1f + ColonyManager.CountTerraformers(b), 6f) + StationEffects.TerraformAuraAt(b);
            float gain = 1.1f * rate * TerraformProjects.SpeedFactor();
            k.waterUpkeep += gain * 4f;
            k.energyUsed += gain * 3f;
            k.metalUpkeep += gain * 2f;
        }

        if (b.settled)
        {
            k.foodMade = Carrying.FoodSupply(b);
            k.foodEaten = Carrying.FoodDemand(b);
        }
        return k;
    }

    /// "+1.2" / "-0.4" / "0", coloured green, red or grey.
    public static string Signed(float v, string fmt = "0.0")
    {
        string hex = v > 0.005f ? "4DFF6E" : v < -0.005f ? "FF6659" : "9FB4C8";
        string s = v > 0.005f ? "+" + v.ToString(fmt) : v.ToString(fmt);
        return $"<color=#{hex}>{s}</color>";
    }
}
