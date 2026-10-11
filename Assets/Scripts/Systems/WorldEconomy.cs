using UnityEngine;

// ============================================================================================
// ONE WORLD'S BOOKS — income, upkeep and net, per resource (2026-10-09)
//
// For the production strip along the bottom of the Planet View's Overview. Read straight off the same
// formulas the economy actually runs (SurfaceOutput — which SurfaceBuildManager.TickOutput pays out of —
// PowerGrid and Carrying), so the strip can never quote a number the world is not really earning.
//
//   Metal    — mines and refineries: SurfaceOutput.Metal (siting x size x tech x condition x power x
//              synergy x ore-yield research). Upkeep: the factories' metal while something is building
//              (Industry.MetalDemand), plus terraforming.
//   Water    — farms and the like.
//   Energy   — production is every generator's output (switchyard synergy included, as the grid
//              computes it); upkeep is every on-grid consumer's draw (PowerGrid.DrawOf, size-scaled).
//   Research — labs, before the empire's research-rate research.
//   Industry — factories' points, and the build speed they buy here (Industry).
//   Food     — how many people the farms can feed, against how many there are (Carrying). A capacity,
//              not a per-second flow, and labelled that way.
// ============================================================================================
public static class WorldEconomy
{
    public struct Books
    {
        public float metal, water, research;
        public float energyMade, energyUsed;
        public float metalUpkeep, waterUpkeep;
        public float foodMade, foodEaten;
        public float industry, buildSpeed;
        public int mines, refineries, factories;
        public float EnergyNet => energyMade - energyUsed;
        public float FoodNet => foodMade - foodEaten;
    }

    public static Books Of(CelestialBody b)
    {
        var k = new Books { buildSpeed = 1f };
        if (b == null) return k;

        foreach (var p in SurfaceBuildManager.On(b))
        {
            var info = p.Info;
            if (info == null) continue;
            k.metal += SurfaceOutput.Metal(b, p);
            k.water += SurfaceOutput.Water(b, p);
            k.research += SurfaceOutput.Research(b, p);
            k.energyMade += SurfaceOutput.Energy(b, p);
            // Only consumers on a grid draw — exactly what PowerGrid adds to a net's load.
            if (info.powerDraw > 0f && PowerGrid.NetOf(b, p) != null) k.energyUsed += PowerGrid.DrawOf(p);

            if (p.Type == SurfaceBuildingType.Mine) k.mines++;
            else if (p.Type == SurfaceBuildingType.Refinery) k.refineries++;
            else if (p.Type == SurfaceBuildingType.Factory) k.factories++;
        }

        k.industry = Industry.Points(b);
        k.buildSpeed = Industry.SpeedMult(b);
        k.metalUpkeep += Industry.MetalDemand(b);

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
