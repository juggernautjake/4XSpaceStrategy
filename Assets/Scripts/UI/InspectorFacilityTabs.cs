using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// The Inspector's tabs for things ON a world that are subjects in their own right: the city, the
// shipyard and the research centre. You reach these by drilling in from a planet's Production or
// Society tab, and the breadcrumb walks you back.
//
// The shipyard and the research centre are the two that DO something: a shipyard lays down hulls, a
// laboratory takes on technologies. Both show their own tier and what that tier buys — which is
// parallelism (build power / research capacity), not just speed.
public partial class InspectorWindow
{
    // ================= CITY =================
    void CollectCityTabs()
    {
        tabs.Add(new InspectorTab("Overview", BuildCityOverview));
        tabs.Add(new InspectorTab("People", BuildCityPeople));
    }

    void BuildCityOverview(Transform p)
    {
        var b = target.body;

        Header(p, "THE CITY");
        var card = Card(p);
        Stat(card, "World", () => $"{b.name} ({TerraformDiagnosis.Pretty(b.type)})");
        Stat(card, "Population", () => $"{b.population} / {Colony.PopTarget(b)}");
        Stat(card, "Habitability", () => $"<color={Habitability.ScoreColorHex(b.habitability)}>{b.habitability:F0}%</color>");
        Stat(card, "Development", () => $"{Colony.ClaimProgress(b) * 100f:F0}%" +
            (Colony.IsFullyEstablished(b) ? "  <color=#4DFF6E>fully established</color>" : ""));

        Header(p, "OUTPUT");
        var outp = Card(p);
        var t = UIFactory.WrapText(outp, "", UITheme.SmallSize, UITheme.Text);
        live.Text(t, () =>
        {
            // What this colony actually contributes per second, mirroring ColonyManager.TickColony.
            float popMult = 0.5f + b.population / 200f;
            float oreRich = 1f + OreGenerator.OresOnBody(b).Count * 0.15f;
            float metal = 0f, energy = 0f, water = 0f, research = 0f;
            foreach (int id in b.buildings)
            {
                var info = BuildingDatabase.Get((BuildingType)id);
                float mine = id == (int)BuildingType.Mine ? oreRich * TechEffects.OreYieldMult : 1f;
                metal += info.metalPerSec * popMult * mine;
                energy += info.energyPerSec * popMult;
                water += info.waterPerSec * popMult;
                research += info.researchPerSec * popMult * TechEffects.ResearchRateMult;
            }
            return $"<color=#9FB4C8>Per second:</color>  {metal:0.0} metal · {energy:0.0} energy · {water:0.0} water · {research:0.0} research\n" +
                   $"<color=#9FB4C8>Scaled by population ({popMult:0.00}×) — a bigger city produces more of everything.</color>";
        });

        Header(p, "STRUCTURES");
        if (b.buildings.Count == 0) Note(p, "Nothing built here yet.");
        foreach (int id in b.buildings)
        {
            var bt = (BuildingType)id;
            var info = BuildingDatabase.Get(bt);
            string extra = bt == BuildingType.Shipyard ? $" — Lv{b.shipyardLevel}"
                         : bt == BuildingType.ResearchCenter ? $" — Lv{b.researchCenterLevel}" : "";
            UIFactory.WrapText(p, $"<color=#4DFF6E>•</color> <b>{info.name}</b>{extra}", UITheme.SmallSize, UITheme.Text);
        }
    }

    void BuildCityPeople(Transform p)
    {
        var b = target.body;

        Header(p, "SATISFACTION");
        Bar(p, () =>
        {
            float s = Satisfaction.For(b);
            return (s / 100f, $"{Satisfaction.Label(s)} — {s:F0}%", Satisfaction.Color(s));
        });

        var card = Card(p);
        var t = UIFactory.WrapText(card, "", UITheme.SmallSize, UITheme.Text);
        live.Text(t, () =>
        {
            var sb = new System.Text.StringBuilder();
            foreach (var f in Satisfaction.Breakdown(b))
            {
                string hex = ColorUtility.ToHtmlStringRGB(f.delta >= 0f ? UITheme.Good : UITheme.Bad);
                sb.AppendLine($"<color=#{hex}>{(f.delta >= 0f ? "+" : "")}{f.delta:F0}</color>  <b>{f.label}</b>");
                sb.AppendLine($"     <color=#9FB4C8>{f.detail}</color>");
            }
            return sb.ToString();
        });

        Header(p, "GROWTH");
        var g = Card(p);
        var gt = UIFactory.WrapText(g, "", UITheme.SmallSize, UITheme.Text);
        live.Text(gt, () =>
        {
            float mult = Satisfaction.GrowthMultiplier(b);
            if (mult <= 0f) return "<color=#FF6659>This colony is too miserable to grow. Fix what's dragging it down above — food and habitability matter most.</color>";
            if (b.population >= Colony.PopTarget(b)) return "<color=#4DFF6E>At capacity.</color> The world can't hold more people without more habitable land.";
            return $"Growing at <b>×{mult:0.00}</b> of its base rate.";
        });
    }

    // ================= SHIPYARD =================
    void CollectShipyardTabs()
    {
        tabs.Add(new InspectorTab("Overview", BuildShipyardOverview));
        tabs.Add(new InspectorTab("Build", BuildShipyardBuild));
        tabs.Add(new InspectorTab("Stocks", BuildShipyardStocks));
    }

    void BuildShipyardOverview(Transform p)
    {
        var b = target.body;

        Header(p, "THIS SHIPYARD");
        var card = Card(p);
        Stat(card, "Tier", () => $"Level <b>{b.shipyardLevel}</b> / {Colony.MaxShipyardLevel}  <color=#9FB4C8>({Colony.ShipyardPerk(b.shipyardLevel)})</color>");
        Stat(card, "Build power", () => $"<color=#8FD0FF><b>{BuildPower.ForBody(b)}</b></color>" +
            (TechEffects.ShipyardPowerBonus > 0 ? $"  <color=#9FB4C8>(tier {b.shipyardLevel + 1} + {TechEffects.ShipyardPowerBonus} from research)</color>" : ""));
        Stat(card, "Build speed", () => $"×{1f + 0.15f * (Colony.PlayerMaxShipyardLevel() - 1):0.00} <color=#9FB4C8>(from your best yard's tier)</color>");

        Note(p, "A tier buys PARALLELISM: build power is how many hulls this yard can hold on the stocks at once. Every shipyard you own pools its power, and ships are built from the combined total.");

        Header(p, "EMPIRE POOL");
        Bar(p, () =>
        {
            var um = UnitManager.Instance;
            if (um == null) return (0f, "", UITheme.Accent);
            int total = um.TotalBuildPower, used = um.UsedBuildPower;
            return (total > 0 ? used / (float)total : 0f,
                    $"{used} of {total} build power in use across {BuildPower.PlayerYardCount()} shipyard(s)",
                    used >= total ? UITheme.Warn : UITheme.Good);
        });

        Header(p, "UPGRADE");
        var mgr = ColonyManager.Instance;
        if (b.shipyardLevel >= Colony.MaxShipyardLevel)
            UIFactory.WrapText(p, "<color=#4DFF6E>This yard is at its maximum tier.</color>", UITheme.SmallSize, UITheme.Good);
        else if (mgr != null)
        {
            int next = b.shipyardLevel + 1;
            var btn = UIFactory.Button(p, "", () => { if (mgr.StartShipyardUpgrade(b)) lastSig = null; }, 28);
            live.Button(btn, () =>
            {
                bool can = mgr.CanUpgradeShipyard(b, out string why, out _);
                return (can, can
                    ? $"Upgrade -> Lv{next} ({ColonyManager.ShipyardUpgradeMetal(next)}m {ColonyManager.ShipyardUpgradeEnergy(next)}e, {ColonyManager.ShipyardUpgradeTime(next):F0}s) -> {BuildPower.ForLevel(next)} build power"
                    : $"Upgrade -> Lv{next} — {why}");
            });
            Note(p, $"Level {next}: {Colony.ShipyardPerk(next)}.");
        }

        Header(p, "RESEARCH THAT HELPS");
        Note(p, "The Industry branch's slipway line (Parallel Slipways -> Robotic Assembly Swarms -> Autonomous Drydocks) adds build power to EVERY shipyard you own — a well-researched Lv2 yard beats a neglected Lv4 one.");
    }

    // The compact catalogue: click a hull to lay one down here. Click again to queue another.
    void BuildShipyardBuild(Transform p)
    {
        var um = UnitManager.Instance;
        if (um == null) { Note(p, "No shipyard manager."); return; }

        Header(p, "LAY DOWN A HULL");
        Note(p, "Click to queue one. Click again to queue another. Ships are built in queue order from the pooled build power, so several build at once.");

        var res = UIFactory.WrapText(p, "", UITheme.SmallSize, UITheme.Accent);
        live.Text(res, () => PlayerEconomy.Summary());

        var pool = UIFactory.WrapText(p, "", UITheme.SmallSize, UITheme.Text);
        live.Text(pool, () =>
        {
            int total = um.TotalBuildPower, used = um.UsedBuildPower;
            string hex = ColorUtility.ToHtmlStringRGB(used >= total ? UITheme.Warn : UITheme.Good);
            return $"Build Power: <color=#{hex}><b>{total - used}</b> free</color> of <b>{total}</b>";
        });

        foreach (var info in UnitDatabase.All)
        {
            if (info == null) continue;
            var t = info.type;

            var card = Card(p);
            var group = card.gameObject.AddComponent<CanvasGroup>();

            // Icon + name.
            var row = UIFactory.NewUI(card, "Title"); UIFactory.AddLayout(row, 20);
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6; h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childAlignment = TextAnchor.MiddleLeft;

            var icon = UIFactory.NewUI(row.transform, "Icon");
            var img = icon.AddComponent<Image>();
            img.sprite = UnitIconRenderer.Sprite(t);
            img.preserveAspect = true; img.raycastTarget = false;
            var ile = icon.AddComponent<LayoutElement>();
            ile.preferredWidth = 18; ile.minWidth = 18; ile.preferredHeight = 18; ile.flexibleWidth = 0;

            var nm = UIFactory.Text(row.transform, $"<b>{info.name}</b>", UITheme.SmallSize,
                new Color(info.iconColor.r, info.iconColor.g, info.iconColor.b), TextAlignmentOptions.Left);
            var nle = nm.gameObject.AddComponent<LayoutElement>(); nle.flexibleWidth = 1;

            var cost = UIFactory.WrapText(card, "", UITheme.SmallSize, UITheme.SubText);
            live.Text(cost, () =>
            {
                int m = ColonyManager.DiscCost(info.costMetal), e = ColonyManager.DiscCost(info.costEnergy);
                bool afford = GameMode.DevMode || PlayerEconomy.CanAfford(m, e);
                string hex = ColorUtility.ToHtmlStringRGB(afford ? UITheme.SubText : UITheme.Bad);
                return $"<color=#{hex}>{m} metal · {e} energy</color>   <color=#8FD0FF>{info.buildPower} build power</color>   " +
                       $"<color=#9FB4C8>{info.buildTime * TechEffects.BuildTimeMult:F0}s</color>";
            });

            var btn = UIFactory.Button(card, "", () => { UnitManager.Instance?.QueueBuild(t); lastSig = null; }, 24);
            live.Button(btn, () =>
            {
                bool can = um.CanBuildShip(t, out string why);
                return (can, can ? (info.isStation ? "Construct" : "Build") : why);
            }, group);
        }
    }

    void BuildShipyardStocks(Transform p)
    {
        var um = UnitManager.Instance;
        if (um == null) return;

        Header(p, "ON THE STOCKS");
        Note(p, "Ships take build power in queue order. Pausing one hands its power to the next; cancelling refunds in full. Drag-reorder lives in the full Shipyard window.");

        if (um.BuildQueue.Count == 0) { Note(p, "Nothing under construction."); }

        foreach (var o in new List<BuildOrder>(um.BuildQueue))
        {
            var cap = o;
            var card = Card(p);
            var t = UIFactory.WrapText(card, "", UITheme.SmallSize, UITheme.Text);
            live.Text(t, () =>
            {
                var info = UnitDatabase.Get(cap.type);
                string state;
                switch (cap.state)
                {
                    case BuildState.Building: state = $"<color=#4DFF6E>Building</color> — {cap.Progress * 100f:F0}% ({cap.Remaining:F0}s)"; break;
                    case BuildState.Paused: state = $"<color=#FFBF4D>Paused</color> at {cap.Progress * 100f:F0}%"; break;
                    case BuildState.Impossible: state = $"<color=#FF6659>Needs {cap.Power} power — more than your yards supply</color>"; break;
                    default: state = $"<color=#9FB4C8>Queued</color> — waiting for {cap.Power} power"; break;
                }
                return $"<b>{info.name}</b> <size=10><color=#8FD0FF>{cap.Power}p</color> {state}</size>";
            });
            Bar(card, () => (cap.Progress, "", cap.state == BuildState.Building ? UITheme.Accent : UITheme.SubText));

            var row = UIFactory.NewUI(card, "Row"); UIFactory.AddLayout(row, 22);
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true;
            var pause = UIFactory.Button(row.transform, "", () => um.SetOrderPaused(cap, !cap.paused), 20);
            live.Button(pause, () => (true, cap.paused ? "Resume" : "Pause"));
            UIFactory.Button(row.transform, "Cancel (refund)", () => { um.CancelOrder(cap); lastSig = null; }, 20);
        }

        UIFactory.Button(p, "Open full Shipyard window »", () => ShipyardWindow.Instance?.Toggle(), 26);
    }

    // ================= RESEARCH CENTRE =================
    void CollectLabTabs()
    {
        tabs.Add(new InspectorTab("Overview", BuildLabOverview));
        tabs.Add(new InspectorTab("Research", BuildLabResearch));
        tabs.Add(new InspectorTab("Projects", BuildLabProjects));
    }

    void BuildLabOverview(Transform p)
    {
        var b = target.body;

        Header(p, "THIS LABORATORY");
        var card = Card(p);
        Stat(card, "Tier", () => $"Level <b>{b.researchCenterLevel}</b> / {Colony.MaxResearchCenterLevel}");
        Stat(card, "Research capacity", () => $"<color=#8FD0FF><b>{ResearchCapacity.ForBody(b)}</b></color>" +
            (TechEffects.ResearchCapacityBonus > 0 ? $"  <color=#9FB4C8>(tier {b.researchCenterLevel + 1} + {TechEffects.ResearchCapacityBonus} from research)</color>" : ""));

        Note(p, "Capacity is how many technologies your laboratories can study AT ONCE — or how large a single project they can take on. Every research centre you own pools its capacity.");

        Header(p, "EMPIRE POOL");
        Bar(p, () =>
        {
            int total = TechManager.TotalCapacity, used = TechManager.UsedCapacity;
            return (total > 0 ? used / (float)total : 0f,
                    $"{used} of {total} capacity in use across {ResearchCapacity.PlayerLabCount()} centre(s)",
                    used >= total ? UITheme.Warn : UITheme.Good);
        });

        var rp = UIFactory.WrapText(p, "", UITheme.SmallSize, UITheme.Accent);
        live.Text(rp, () => $"Research Points banked: <b>{ResearchManager.ResearchPoints}</b>");
        Note(p, "Capacity decides what you MAY study at once; your research income decides what you can afford to. Each active project draws its own stream of points.");

        Header(p, "UPGRADE");
        var mgr = ColonyManager.Instance;
        if (b.researchCenterLevel >= Colony.MaxResearchCenterLevel)
            UIFactory.WrapText(p, "<color=#4DFF6E>This laboratory is at its maximum tier.</color>", UITheme.SmallSize, UITheme.Good);
        else if (mgr != null)
        {
            int next = b.researchCenterLevel + 1;
            var btn = UIFactory.Button(p, "", () => { if (mgr.StartLabUpgrade(b)) lastSig = null; }, 28);
            live.Button(btn, () =>
            {
                bool can = mgr.CanUpgradeLab(b, out string why, out _);
                return (can, can
                    ? $"Upgrade -> Lv{next} ({ColonyManager.LabUpgradeMetal(next)}m {ColonyManager.LabUpgradeEnergy(next)}e, {ColonyManager.LabUpgradeTime(next):F0}s) -> {ResearchCapacity.ForLevel(next)} capacity"
                    : $"Upgrade -> Lv{next} — {why}");
            });
        }

        Header(p, "RESEARCH THAT HELPS");
        Note(p, "The Science branch's wing line (Parallel Research Wings -> Distributed Cognition -> Quantum Think-Tanks) adds capacity to EVERY research centre you own.");
    }

    // Queue technologies straight from the laboratory.
    void BuildLabResearch(Transform p)
    {
        Header(p, "TAKE ON A TECHNOLOGY");
        Note(p, "Queued projects study in order, several at once if capacity allows. A project's capacity cost scales with its size — a 480 RP precursor project needs most of your laboratories to itself.");

        var cap = UIFactory.WrapText(p, "", UITheme.SmallSize, UITheme.Text);
        live.Text(cap, () =>
        {
            int total = TechManager.TotalCapacity, used = TechManager.UsedCapacity;
            string hex = ColorUtility.ToHtmlStringRGB(used >= total ? UITheme.Warn : UITheme.Good);
            return $"Capacity: <color=#{hex}><b>{total - used}</b> free</color> of <b>{total}</b>   ·   " +
                   $"<color=#9FB4C8>{ResearchManager.ResearchPoints} RP banked</color>";
        });

        // Only branches with something actually available, so the list stays navigable.
        bool ancientsUnlocked = AncientLore.SchematicsFound > 0 || TechManager.IsResearched("S3");
        foreach (TechBranch br in System.Enum.GetValues(typeof(TechBranch)))
        {
            if (br == TechBranch.Ancients && !ancientsUnlocked) continue;

            bool headerAdded = false;
            foreach (var tech in TechDatabase.InBranch(br))
            {
                if (TechManager.IsResearched(tech.id)) continue;
                if (!headerAdded) { Header(p, br.ToString().ToUpper()); headerAdded = true; }

                var id = tech.id;
                var card = Card(p);
                var group = card.gameObject.AddComponent<CanvasGroup>();

                UIFactory.WrapText(card, $"<b>{tech.name}</b>  <size=10><color=#9FB4C8>T{tech.tier} · {tech.cost} RP</color> · " +
                                         $"<color=#8FD0FF>{tech.CapacityCost} capacity</color></size>", UITheme.SmallSize, UITheme.Text);
                Note(card, tech.desc);
                if (!string.IsNullOrEmpty(tech.unlockNote))
                    UIFactory.WrapText(card, $"<color=#C9A94D>{tech.unlockNote}</color>", UITheme.SmallSize, UITheme.SubText);

                var btn = UIFactory.Button(card, "", () =>
                {
                    if (TechManager.IsQueued(id)) TechManager.RemoveFromQueue(TechManager.QueuePosition(id));
                    else TechManager.Enqueue(id);
                    lastSig = null;
                }, 24);
                live.Button(btn, () =>
                {
                    var order = TechManager.Find(id);
                    if (order != null)
                    {
                        int pos = TechManager.QueuePosition(id) + 1;
                        return (true, order.Active ? $"Researching now (#{pos}) — abandon" : $"Queued (#{pos}) — remove");
                    }
                    bool can = TechManager.CanQueue(tech, out string why);
                    return (can, can ? $"Research ({tech.cost} RP · {tech.CapacityCost} capacity)" : $"Locked — {why}");
                }, group);
            }
        }
    }

    // What this empire's laboratories are working on right now.
    void BuildLabProjects(Transform p)
    {
        Header(p, "UNDER STUDY");
        if (TechManager.Queue.Count == 0)
        {
            Note(p, "Nothing queued. Take on a technology from the Research tab.");
        }
        else
        {
            foreach (var o in new List<ResearchOrder>(TechManager.Queue))
            {
                var cap = o;
                var def = cap.Def;
                if (def == null) continue;

                var card = Card(p);
                var t = UIFactory.WrapText(card, "", UITheme.SmallSize, UITheme.Text);
                live.Text(t, () =>
                {
                    string state;
                    switch (cap.state)
                    {
                        case ResearchState.Researching: state = $"<color=#4DFF6E>Researching</color> — {cap.progress:F0}/{def.cost} RP"; break;
                        case ResearchState.Paused: state = $"<color=#FFBF4D>Paused</color> at {cap.Progress01 * 100f:F0}%"; break;
                        case ResearchState.WaitingForPrereq: state = "<color=#9FB4C8>Waiting on its prerequisite</color>"; break;
                        case ResearchState.Impossible: state = $"<color=#FF6659>Needs {cap.Cost} capacity — more than your labs supply</color>"; break;
                        default: state = $"<color=#9FB4C8>Queued</color> — waiting for {cap.Cost} capacity"; break;
                    }
                    return $"<b>{def.name}</b> <size=10><color=#8FD0FF>{cap.Cost}c</color> {state}</size>";
                });
                Bar(card, () => (cap.Progress01, "", cap.state == ResearchState.Researching ? UITheme.Good : UITheme.SubText));

                var row = UIFactory.NewUI(card, "Row"); UIFactory.AddLayout(row, 22);
                var h = row.AddComponent<HorizontalLayoutGroup>();
                h.spacing = 6; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true;
                var pause = UIFactory.Button(row.transform, "", () => TechManager.SetOrderPaused(cap, !cap.paused), 20);
                live.Button(pause, () => (true, cap.paused ? "Resume" : "Pause"));
                UIFactory.Button(row.transform, "Abandon (refunds RP)", () => { TechManager.RemoveOrder(cap); lastSig = null; }, 20);
            }
        }

        Header(p, "EMPIRE TECH LEVEL");
        var ec = Card(p);
        var et = UIFactory.WrapText(ec, "", UITheme.SmallSize, UITheme.Text);
        live.Text(et, () => EmpireTech.AtMax
            ? $"<b>Level {EmpireTech.Level}/{EmpireTech.MaxLevel}</b> — peak for {SpeciesManager.Current.name}."
            : $"<b>Level {EmpireTech.Level}/{EmpireTech.MaxLevel}</b>\n<color=#8FD0FF>Next:</color> {EmpireTech.MilestoneFor(EmpireTech.Level + 1)}");
        if (!EmpireTech.AtMax)
        {
            var adv = UIFactory.Button(ec, "", () => EmpireTech.Advance(), 26);
            live.Button(adv, () =>
            {
                bool can = EmpireTech.CanAdvance;
                return (can, can ? $"Advance to Level {EmpireTech.Level + 1} ({EmpireTech.NextCost} RP)"
                                 : $"Advance to Level {EmpireTech.Level + 1} — need {EmpireTech.NextCost} RP (have {ResearchManager.ResearchPoints})");
            });
        }

        UIFactory.Button(p, "Open full Research window »", () => ResearchWindow.Instance?.Toggle(), 26);
    }

    // ================= PLAIN STRUCTURE (mine, farm, power plant) =================
    void CollectStructureTabs()
    {
        tabs.Add(new InspectorTab("Overview", BuildStructureOverview));
    }

    void BuildStructureOverview(Transform p)
    {
        var b = target.body;
        var info = BuildingDatabase.Get(target.structure);

        Header(p, info.name.ToUpper());
        var card = Card(p);
        Note(card, info.description);
        Stat(card, "On", () => b.name);

        Header(p, "OUTPUT");
        var outp = Card(p);
        var t = UIFactory.WrapText(outp, "", UITheme.SmallSize, UITheme.Text);
        live.Text(t, () =>
        {
            float popMult = 0.5f + b.population / 200f;
            var parts = new List<string>();
            if (info.metalPerSec > 0f)
            {
                float mine = target.structure == BuildingType.Mine
                    ? (1f + OreGenerator.OresOnBody(b).Count * 0.15f) * TechEffects.OreYieldMult : 1f;
                parts.Add($"{info.metalPerSec * popMult * mine:0.00} metal/s");
            }
            if (info.energyPerSec > 0f) parts.Add($"{info.energyPerSec * popMult:0.00} energy/s");
            if (info.waterPerSec > 0f) parts.Add($"{info.waterPerSec * popMult:0.00} water/s");
            if (info.researchPerSec > 0f) parts.Add($"{info.researchPerSec * popMult * TechEffects.ResearchRateMult:0.00} research/s");
            if (info.popGrowthPerSec > 0f) parts.Add($"{info.popGrowthPerSec:0.0} population growth/s");
            if (parts.Count == 0) return "<color=#9FB4C8>This structure has no direct output — it unlocks a capability instead.</color>";
            return string.Join("  ·  ", parts) +
                   $"\n<color=#9FB4C8>Scaled by this colony's population ({popMult:0.00}×).</color>";
        });
    }
}
