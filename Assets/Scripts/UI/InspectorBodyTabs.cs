using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// The Inspector's tabs for a PLANET or MOON: everything known about a world, paged.
//
//   Overview   — the at-a-glance card, plus Map / Focus / Terraform actions
//   Climate    — the world as a place: starlight, temperature, day length, tilt, air, gravity
//   Ores       — what is in the ground and whether you have researched it
//   Society    — cities (drill in), population, and WHY the population feels how it does
//   Production — the facilities (drill into the shipyard or research centre) and the build queue
//   Objects    — moons, stations and ships associated with this world (all selectable)
//   Terraform  — the fault list and the ceiling, with a link to the full console
public partial class InspectorWindow
{
    void CollectBodyTabs()
    {
        tabs.Add(new InspectorTab("Overview", BuildBodyOverview));
        tabs.Add(new InspectorTab("Climate", BuildBodyClimate));
        tabs.Add(new InspectorTab("Ores", BuildBodyOres, () => target.body != null && target.body.Surveyed));
        tabs.Add(new InspectorTab("Society", BuildBodySociety, () => target.body != null && target.body.owner == FactionManager.Player));
        tabs.Add(new InspectorTab("Production", BuildBodyProduction, () => target.body != null && target.body.owner == FactionManager.Player));
        tabs.Add(new InspectorTab("Objects", BuildBodyObjects));
        tabs.Add(new InspectorTab("Terraform", BuildBodyTerraform, () => target.body != null && target.body.type != CelestialBodyType.GasGiant));
    }

    // ---------------- Overview ----------------
    void BuildBodyOverview(Transform p)
    {
        var b = target.body;

        var card = Card(p);
        Stat(card, "Type", () => TerraformDiagnosis.Pretty(b.type));
        Stat(card, "Owner", () =>
        {
            string hex = "#" + ColorUtility.ToHtmlStringRGB(FactionManager.OwnerColor(b.owner));
            return $"<color={hex}>{FactionManager.OwnerLabel(b.owner)}</color>";
        });
        Stat(card, "Habitability", () =>
        {
            string hex = Habitability.ScoreColorHex(b.habitability);
            return $"<color={hex}><b>{b.habitability:F0}%</b> ({Habitability.Label(b.habitability, b.isHabitable)})</color>" +
                   $" <color=#9FB4C8>for {SpeciesManager.Current.name}</color>";
        });

        if (!b.Surveyed)
        {
            var warn = Card(p, new Color(0.16f, 0.13f, 0.06f, 0.9f));
            UIFactory.WrapText(warn, "<b><color=#FFBF4D>Unexplored world</color></b>", UITheme.SmallSize, UITheme.Warn);
            Note(warn, "Known: name, type, owner, orbit and host star. Send a ship to survey it and its habitability, resources, ores and points of interest are revealed.");
            Bar(warn, () => (b.explorationProgress, $"Survey {b.explorationProgress * 100f:F0}%", UITheme.Accent));
        }
        else
        {
            Stat(card, "Surface", () => $"{b.surfaceSize * 2}×{b.surfaceSize}");
            Stat(card, "Moons", () => b.moons != null ? b.moons.Count.ToString() : "0");
            Stat(card, "Ships here", () => b.units != null ? b.units.Count.ToString() : "0");
            Stat(card, "Points of interest", () => b.pointsOfInterest != null ? b.pointsOfInterest.Count.ToString() : "0");
        }

        // Colonization progress by someone else.
        if (b.claimProgress > 0f && b.owner != FactionManager.Player)
            Bar(p, () => (b.claimProgress, $"Being colonized — {b.claimProgress * 100f:F0}%", UITheme.Warn));

        // Actions.
        Header(p, "ACTIONS");
        var row = UIFactory.NewUI(p, "Actions"); UIFactory.AddLayout(row, 28);
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 6; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true;

        // The Planet View is where you actually develop a world: info, the build grid, and the survey
        // overlays. The old read-only detail map stays available for points of interest.
        var viewBtn = UIFactory.Button(row.transform, "Planet View", () => PlanetViewWindow.Instance?.ShowFor(b), 26);
        live.Button(viewBtn, () => b.Surveyed
            ? (true, "Planet View (build / survey)")
            : (false, "Planet View — survey this world first"));

        var mapBtn = UIFactory.Button(row.transform, "Map", () => DetailedSurfaceWindow.Instance?.Open(b), 26);
        live.Button(mapBtn, () => b.Surveyed
            ? (true, "Points of Interest Map")
            : (false, "Map — survey this world first"));

        UIFactory.Button(row.transform, "Focus Camera", () =>
        {
            if (b.visualObject != null)
                CameraController.Instance?.FocusAndZoom(b.visualObject.transform, b.surfaceSize, true);
        }, 26);
    }

    // ---------------- Climate ----------------
    // The world as a PLACE to stand: what the sky looks like, how long a day is, what the air is.
    void BuildBodyClimate(Transform p)
    {
        var b = target.body;
        var s = SpeciesManager.Current;

        Header(p, "THE WORLD");
        var card = Card(p);
        UIFactory.WrapText(card, ClimateProse(b, s), UITheme.SmallSize, UITheme.Text);

        Header(p, "STARLIGHT & ORBIT");
        var orbit = Card(p);
        Stat(orbit, "Distance from star", () => $"{b.distanceFromStar:F1} units");
        Stat(orbit, "Orbital radius", () => $"{b.orbitRadius:F1}");
        Stat(orbit, "Year", () => $"{OrbitalMechanics.PeriodSeconds(b.orbitSpeed):F1}s");
        Stat(orbit, "Eccentricity", () => $"{b.eccentricity:F2}");
        Stat(orbit, "Axial tilt", () => $"{b.inclination:F0}°" + (Mathf.Abs(b.inclination) > 28f ? "  <color=#FFBF4D>(severe seasons)</color>" : ""));
        Stat(orbit, "Day length", () =>
        {
            float spin = Mathf.Abs(b.spinSpeed);
            if (spin < 3f) return $"{spin:F1}°/s  <color=#FFBF4D>(near tidally locked)</color>";
            if (spin > 45f) return $"{spin:F1}°/s  <color=#FFBF4D>(violently fast)</color>";
            return $"{spin:F1}°/s";
        });

        // The star it orbits, since that's what decides the whole climate.
        if (b.hostStar != null)
        {
            Header(p, "HOST STAR");
            var star = Card(p);
            Stat(star, "Temperature", () => $"{b.hostStar.temperatureK:F0} K");
            Stat(star, "Luminosity", () => $"{b.hostStar.luminosity:F2}×");
            Stat(star, "Habitable zone", () =>
                Habitability.GetZone(b.hostStar, s, out float inner, out float outer)
                    ? $"{inner:F1} – {outer:F1} for {s.name}" + (b.distanceFromStar >= inner && b.distanceFromStar <= outer
                        ? "  <color=#4DFF6E>(this world is inside it)</color>"
                        : "  <color=#FFBF4D>(this world is outside it)</color>")
                    : "none — this star has no habitable band");
        }

        Header(p, "HOW YOUR SPECIES SEES IT");
        var spec = Card(p);
        Note(spec, $"{s.name}: {s.habitat}");
        Stat(spec, "Affinity for this world type", () =>
        {
            float a = s.Affinity(b.type);
            string hex = Habitability.ScoreColorHex(a * 100f);
            return $"<color={hex}>{a * 100f:F0}%</color>" +
                   (a < Habitability.HabitableAffinity ? "  <color=#FFBF4D>— the wrong kind of world for them</color>" : "");
        });
        Note(spec, $"They would rather be on a {TerraformDiagnosis.Pretty(s.BestType())}. " +
                   (s.PrefersDry ? "They need it dry." : "They need liquid water."));
    }

    // A readable paragraph rather than a stat dump — what it would actually be like to stand here.
    static string ClimateProse(CelestialBody b, Species s)
    {
        var parts = new List<string>();

        switch (b.type)
        {
            case CelestialBodyType.OceanPlanet: parts.Add("A world of open ocean, broken only by island chains and storm fronts."); break;
            case CelestialBodyType.IcePlanet: parts.Add("A frozen world. Its water is all here — locked in glaciers kilometres deep."); break;
            case CelestialBodyType.VolcanicPlanet: parts.Add("A furnace world of magma fields and ash skies, lit from below as much as above."); break;
            case CelestialBodyType.BarrenPlanet: parts.Add("A dead rock. No air, no water, no magnetic field — just dust and hard radiation."); break;
            case CelestialBodyType.RockyPlanet: parts.Add("A rocky world with real ground underfoot and weather worth the name."); break;
            case CelestialBodyType.GasGiant: parts.Add("A gas giant: banded storms the size of continents, and no surface to stand on at all."); break;
            case CelestialBodyType.Moon: parts.Add("A moon, locked to its primary."); break;
            default: parts.Add("A small body."); break;
        }

        if (b.hostStar != null && Habitability.GetZone(b.hostStar, s, out float inner, out float outer))
        {
            if (b.distanceFromStar < inner) parts.Add($"It sits closer to its star than {s.name} can comfortably bear — too much light, too much heat.");
            else if (b.distanceFromStar > outer) parts.Add($"It orbits out beyond the light {s.name} needs; the sun here is a bright star and little more.");
            else parts.Add($"It sits squarely in the band {s.name} can live in.");
        }

        float water = b.resources != null ? b.resources.Get(ResourceType.Water) : 0f;
        if (water < 60f) parts.Add("There is essentially no accessible water.");
        else if (water > 300f) parts.Add("Water is abundant — arguably too abundant.");
        else parts.Add("There is some accessible water.");

        if (b.surfaceSize <= 4) parts.Add("It is small enough that gravity is a suggestion and any atmosphere drifts away.");
        else if (b.surfaceSize >= 14) parts.Add("It is massive, and holds a deep, heavy atmosphere.");

        return string.Join(" ", parts);
    }

    // ---------------- Ores ----------------
    void BuildBodyOres(Transform p)
    {
        var b = target.body;
        var ores = OreGenerator.OresOnBody(b);

        Header(p, "MINERAL SURVEY");
        if (ores.Count == 0) { Note(p, "No ore deposits were found on this world."); return; }

        Note(p, $"{ores.Count} ore type(s) present. Surveying DISCOVERS an ore; researching it in the Codex is what unlocks its uses.");

        foreach (var ore in ores)
        {
            var info = OreDatabase.Get(ore);
            bool known = ResearchManager.IsDiscovered(ore);
            bool done = ResearchManager.IsResearched(ore);

            var card = Card(p);
            var title = UIFactory.WrapText(card, "", UITheme.BodySize,
                known ? new Color(info.color.r, info.color.g, info.color.b) : UITheme.SubText);
            var captured = ore;
            live.Text(title, () =>
            {
                bool k = ResearchManager.IsDiscovered(captured);
                bool r = ResearchManager.IsResearched(captured);
                string state = r ? "<color=#4DFF6E>researched</color>"
                             : k ? "<color=#FFBF4D>discovered — not yet researched</color>"
                                 : "<color=#9FB4C8>undiscovered</color>";
                return $"<b>{(k ? info.displayName : "??? — unidentified")}</b>  <size=10>Tier {info.tier} · {info.baseValue}cr · {state}</size>";
            });

            if (!known) { Note(card, "Click its deposits on the surface map, or survey this world, to identify it."); continue; }
            Note(card, info.description);
            if (done)
            {
                UIFactory.WrapText(card, $"<color=#8FD0FF>Uses:</color> {info.uses}", UITheme.SmallSize, UITheme.Text);
                UIFactory.WrapText(card, $"<color=#FFBF4D>Refining:</color> {info.refining}", UITheme.SmallSize, UITheme.Text);
            }
            else
            {
                var btn = UIFactory.Button(card, "", () => ResearchManager.Research(captured), 24);
                live.Button(btn, () =>
                {
                    bool can = ResearchManager.CanResearch(captured);
                    return (can, can ? $"Research {info.displayName} ({info.researchCost} pts)"
                                     : $"Research — need {info.researchCost} pts (have {ResearchManager.ResearchPoints})");
                });
            }
        }
    }

    // ---------------- Society ----------------
    void BuildBodySociety(Transform p)
    {
        var b = target.body;

        Header(p, "POPULATION");
        var card = Card(p);
        Stat(card, "People", () => $"{Population.Format(b.population)} <color=#9FB4C8>of {Population.Format(Colony.PopTarget(b))} capacity</color>");
        Stat(card, "Cities", () => b.cities.ToString());
        Bar(p, () =>
        {
            int t = Colony.PopTarget(b);
            float f = t > 0 ? b.population / (float)t : 0f;
            return (f, $"Population {b.population}/{t}", UITheme.Accent);
        });

        // Satisfaction, with the full reasoning shown rather than a bare number — an unhappy colony
        // should always tell you exactly what it is unhappy about.
        Header(p, "SATISFACTION");
        Bar(p, () =>
        {
            float s = Satisfaction.For(b);
            return (s / 100f, $"{Satisfaction.Label(s)} — {s:F0}%", Satisfaction.Color(s));
        });

        var breakdown = Card(p);
        var bt = UIFactory.WrapText(breakdown, "", UITheme.SmallSize, UITheme.Text);
        live.Text(bt, () =>
        {
            var sb = new System.Text.StringBuilder();
            foreach (var f in Satisfaction.Breakdown(b))
            {
                string hex = ColorUtility.ToHtmlStringRGB(f.delta >= 0f ? UITheme.Good : UITheme.Bad);
                sb.AppendLine($"<color=#{hex}>{(f.delta >= 0f ? "+" : "")}{f.delta:F0}</color>  <b>{f.label}</b>  <color=#9FB4C8>{f.detail}</color>");
            }
            // Satisfaction's real teeth: it multiplies the birth rate, and can zero it.
            float mult = Satisfaction.GrowthMultiplier(b);
            string stall = Population.StallReason(b, InfrastructureGrowth(b));
            sb.AppendLine(stall != null
                ? $"\n<color=#FF6659>Not growing — {stall}.</color>"
                : $"\n<color=#9FB4C8>Birth rate ×{mult:0.00} from satisfaction · " +
                  $"{Population.Format(Mathf.RoundToInt(Population.BirthRate(b, InfrastructureGrowth(b)) * 60f))} per minute</color>");
            return sb.ToString();
        });

        // The cities themselves, drillable.
        Header(p, "CITIES");
        if (b.buildings.Contains((int)BuildingType.City))
            DrillRow(p, $"{b.name} City", InspectorTarget.CityOf(b),
                () => $"pop {b.population} · {Satisfaction.Label(Satisfaction.For(b))}");
        else
            Note(p, "No city here yet. A colony ship founds one, or an owned world can establish one from the Production tab.");

        // Claim objectives.
        Header(p, "OBJECTIVES TO FULLY ESTABLISH");
        var obj = Card(p);
        var ot = UIFactory.WrapText(obj, "", UITheme.SmallSize, UITheme.Text);
        live.Text(ot, () =>
        {
            var sb = new System.Text.StringBuilder();
            foreach (var o in Colony.Objectives(b))
                sb.AppendLine($"{(o.done ? "<color=#4DFF6E>[x]</color>" : "<color=#FF7A6E>[ ]</color>")} {o.label}  <color=#9FB4C8>({o.detail})</color>");
            sb.Append(Colony.IsFullyEstablished(b) ? "\n<color=#4DFF6E>FULLY ESTABLISHED</color>" : $"\nDevelopment: {Colony.ClaimProgress(b) * 100f:F0}%");
            return sb.ToString();
        });
    }

    // The colony's total capacity to raise people: every building's popGrowthPerSec, scaled by how well
    // each was sited. Mirrors what ColonyManager feeds to Population.BirthRate, so the readout and the
    // simulation can't disagree.
    static float InfrastructureGrowth(CelestialBody b)
    {
        float g = 0f;
        foreach (int id in b.buildings) g += BuildingDatabase.Get((BuildingType)id).popGrowthPerSec;
        g += SurfaceBuildManager.PopGrowthPerSec(b);
        return g;
    }

    // ---------------- Production ----------------
    void BuildBodyProduction(Transform p)
    {
        var b = target.body;
        var mgr = ColonyManager.Instance;

        // The two facilities that are subjects in their own right.
        Header(p, "FACILITIES");
        if (b.shipyardLevel >= 1)
            DrillRow(p, "Shipyard", InspectorTarget.ShipyardOf(b),
                () => $"Level {b.shipyardLevel}/{Colony.MaxShipyardLevel} · {BuildPower.ForBody(b)} build power");
        if (b.researchCenterLevel >= 1)
            DrillRow(p, "Research Centre", InspectorTarget.LabOf(b),
                () => $"Level {b.researchCenterLevel}/{Colony.MaxResearchCenterLevel} · {ResearchCapacity.ForBody(b)} capacity");

        // Everything else that's built here, as plain structures.
        foreach (int id in b.buildings)
        {
            var bt = (BuildingType)id;
            if (bt == BuildingType.Shipyard || bt == BuildingType.ResearchCenter || bt == BuildingType.City) continue;
            var info = BuildingDatabase.Get(bt);
            DrillRow(p, info.name, InspectorTarget.StructureOf(b, bt), () => "operating");
        }

        // ---- Everything on the SURFACE ----
        // The map and this tab are one colony. Structures placed on the surface grid used to be
        // invisible here, which made the two systems feel like different games — and worse, they had no
        // effect on the society readout next door. They're the same infrastructure; they belong here.
        var surface = SurfaceBuildManager.On(b);
        if (surface.Count > 0)
        {
            Header(p, "ON THE SURFACE");
            foreach (var sp in new List<PlacedBuilding>(surface))
            {
                var cap = sp;
                var card = Card(p);
                var t = UIFactory.WrapText(card, "", UITheme.SmallSize, UITheme.Text);
                live.Text(t, () =>
                {
                    var info = cap.Info;
                    string hex = ColorUtility.ToHtmlStringRGB(info.color);
                    string eff = info.index == SurfaceIndexKind.None
                        ? "<color=#9FB4C8>full output</color>"
                        : $"<color=#{ColorUtility.ToHtmlStringRGB(SurfaceBuildManager.EfficiencyColor(cap.efficiency))}>" +
                          $"{cap.efficiency * 100f:F0}% sited</color>";
                    return $"<color=#{hex}>■</color> <b>{info.name}</b> <size=10><color=#9FB4C8>Lv{cap.level} · ({cap.x},{cap.y})</color> · {eff}</size>";
                });
            }
        }

        UIFactory.Button(p, "Open Planet View (build on the surface) ▸", () => PlanetViewWindow.Instance?.ShowFor(b), 26);

        if (ColonyFacilities.TotalStructures(b) == 0 && b.shipyardLevel < 1 && b.researchCenterLevel < 1)
            Note(p, "Nothing built here yet.");

        // ---- What this colony can actually DO ----
        // Reads both systems (ColonyFacilities), which is exactly what Satisfaction next door reads —
        // so a surface farm visibly feeds the people rather than being decoration.
        Header(p, "CAPABILITY");
        var capCard = Card(p);
        var ct = UIFactory.WrapText(capCard, "", UITheme.SmallSize, UITheme.Text);
        live.Text(ct, () =>
        {
            int food = ColonyFacilities.FoodSources(b);
            int power = ColonyFacilities.PowerSources(b);
            int res = ColonyFacilities.ResearchSources(b);
            int ind = ColonyFacilities.IndustrySources(b);
            int hou = ColonyFacilities.HousingSources(b);
            string F(int n, string good, string bad) =>
                n > 0 ? $"<color=#4DFF6E>{good}</color>" : $"<color=#FF7A6E>{bad}</color>";
            return $"{F(food, $"{food} food source(s)", "no food")}  ·  {F(power, $"{power} generator(s)", "no power")}\n" +
                   $"{F(res, $"{res} research tier(s)", "no research")}  ·  {F(ind, $"{ind} industry tier(s)", "no industry")}  ·  " +
                   $"{F(hou, $"{hou} housing", "no housing")}\n" +
                   $"<size=10><color=#9FB4C8>Counts colony facilities AND surface structures together — both feed the Society tab.</color></size>";
        });

        // The construction queue — one project at a time per world, cancellable for a refund.
        Header(p, "CONSTRUCTION");
        if (mgr != null)
        {
            var queue = mgr.QueueFor(b);
            if (queue.Count == 0) Note(p, "Nothing under construction.");
            foreach (var c in queue)
            {
                var cap = c;
                var card = Card(p);
                var lbl = UIFactory.Text(card, "", UITheme.SmallSize, UITheme.Text, TextAlignmentOptions.Left);
                UIFactory.AddLayout(lbl.gameObject, 15);
                live.Text(lbl, () =>
                {
                    string nm = string.IsNullOrEmpty(cap.Label) ? BuildingDatabase.Get(cap.type).name : cap.Label;
                    bool active = mgr.ConstructionFor(b) == cap;
                    return active ? $"<b>{nm}</b> — {cap.Progress * 100f:F0}%" : $"<b>{nm}</b> — <color=#9FB4C8>queued</color>";
                });
                Bar(card, () => (cap.Progress, "", UITheme.Good));
                UIFactory.Button(card, "Cancel (refund)", () => { mgr.CancelConstruction(cap); lastSig = null; }, 22);
            }
        }

        // What can still be built here.
        Header(p, "BUILD");
        if (mgr != null && b.owner == FactionManager.Player && !b.buildings.Contains((int)BuildingType.City))
        {
            var cityBtn = UIFactory.Button(p, "", () => { if (mgr.StartEstablishCity(b)) lastSig = null; }, 26);
            live.Button(cityBtn, () =>
            {
                bool can = mgr.CanEstablishCity(b, out string why);
                return (can, can ? $"Establish City ({ColonyManager.CityMetal}m {ColonyManager.CityEnergy}e, {ColonyManager.CityBuildTime:F0}s)"
                                 : $"Establish City — {why}");
            });
        }

        foreach (BuildingType t in System.Enum.GetValues(typeof(BuildingType)))
        {
            if (t == BuildingType.City) continue;
            var info = BuildingDatabase.Get(t);
            var captured = t;
            // Already present: it's listed under FACILITIES. Uses HasFacility rather than the buildings
            // list so a world's starting shipyard/lab counts — one of each per world, upgrade not stack.
            if (ColonyManager.HasFacility(b, t)) continue;

            var card = Card(p);
            UIFactory.WrapText(card, $"<b>{info.name}</b>", UITheme.SmallSize, UITheme.Text);
            Note(card, info.description);
            var group = card.gameObject.AddComponent<CanvasGroup>();
            var btn = UIFactory.Button(card, "", () => { if (mgr != null && mgr.StartBuilding(b, captured)) lastSig = null; }, 24);
            live.Button(btn, () =>
            {
                if (mgr == null) return (false, "unavailable");
                bool can = mgr.CanBuild(b, captured, out string why);
                return (can, can ? $"Build ({ColonyManager.DiscCost(info.costMetal)}m {ColonyManager.DiscCost(info.costEnergy)}e, {info.buildTime * TechEffects.BuildTimeMult:F0}s)"
                                 : $"Build — {why}");
            }, group);
        }
    }

    // ---------------- Objects ----------------
    // Everything associated with this world: its moons, and every ship or station here — all selectable.
    void BuildBodyObjects(Transform p)
    {
        var b = target.body;

        Header(p, "MOONS");
        if (b.moons == null || b.moons.Count == 0) Note(p, "This world has no moons.");
        else
            foreach (var m in b.moons)
            {
                var captured = m;
                var card = Card(p);
                var t = UIFactory.WrapText(card, "", UITheme.SmallSize, UITheme.Text);
                live.Text(t, () => $"<b>{captured.name}</b>  <size=10><color=#9FB4C8>{TerraformDiagnosis.Pretty(captured.type)} · " +
                                   $"hab {captured.habitability:F0}% · {(captured.units != null ? captured.units.Count : 0)} ship(s)</color></size>");
                UIFactory.Button(card, "Inspect ▸", () => { PlanetUI.Instance?.Show(captured); }, 22);
            }

        // Ships and stations are listed separately — a deployed station is infrastructure, not a fleet.
        var ships = new List<Unit>();
        var stations = new List<Unit>();
        if (b.units != null)
            foreach (var u in b.units) (u.Info.isStation ? stations : ships).Add(u);

        Header(p, "STATIONS");
        if (stations.Count == 0) Note(p, "No stations deployed here.");
        else foreach (var u in stations) UnitRow(p, u);

        Header(p, "SHIPS");
        if (ships.Count == 0) Note(p, "No ships here.");
        else foreach (var u in ships) UnitRow(p, u);

        // Ships on their way here are "associated" with this world too, and it's genuinely useful to
        // know what is inbound before you decide what to do.
        var inbound = new List<Unit>();
        if (UnitManager.Instance != null)
            foreach (var u in UnitManager.Instance.Units)
                if (u.status == UnitStatus.Traveling && u.travelTarget == b) inbound.Add(u);
        if (inbound.Count > 0)
        {
            Header(p, "INBOUND");
            foreach (var u in inbound)
            {
                var cap = u;
                var card = Card(p);
                var t = UIFactory.WrapText(card, "", UITheme.SmallSize, UITheme.SubText);
                live.Text(t, () => $"<b>{cap.name}</b> — arriving in {Mathf.Max(0f, cap.travelDuration - cap.travelElapsed):F0}s");
                UIFactory.Button(card, "Select ▸", () => { UnitSelection.SelectOnly(cap); }, 22);
            }
        }
    }

    // A selectable ship row. Selecting re-targets the Inspector to that ship via UnitSelection.
    void UnitRow(Transform p, Unit u)
    {
        var cap = u;
        var card = Card(p);
        var row = UIFactory.NewUI(card, "Row"); UIFactory.AddLayout(row, 22);
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 6; h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childAlignment = TextAnchor.MiddleLeft;

        var icon = UIFactory.NewUI(row.transform, "Icon");
        var img = icon.AddComponent<Image>();
        img.sprite = UnitIconRenderer.Sprite(u.type);
        img.preserveAspect = true; img.raycastTarget = false;
        var ile = icon.AddComponent<LayoutElement>();
        ile.preferredWidth = 18; ile.minWidth = 18; ile.preferredHeight = 18; ile.flexibleWidth = 0;

        var t = UIFactory.Text(row.transform, "", UITheme.SmallSize, UITheme.Text, TextAlignmentOptions.Left);
        var tle = t.gameObject.AddComponent<LayoutElement>(); tle.flexibleWidth = 1;
        live.Text(t, () =>
        {
            int g = ControlGroups.GroupOf(cap);
            string badge = g > 0 ? $"<color=#5AB4F0>[{g}]</color> " : "";
            return $"{badge}<b>{cap.name}</b>  <size=10><color=#9FB4C8>{cap.Info.name} · {cap.RankName} · {cap.status}</color></size>";
        });

        UIFactory.Button(card, "Select ▸", () => { UnitSelection.SelectOnly(cap); }, 22);
    }

    // ---------------- Terraform ----------------
    void BuildBodyTerraform(Transform p)
    {
        var b = target.body;
        var s = SpeciesManager.Current;

        Header(p, "HABITABILITY CEILING");
        var card = Card(p);
        var t = UIFactory.WrapText(card, "", UITheme.SmallSize, UITheme.Text);
        live.Text(t, () =>
        {
            float now = b.habitability, ceiling = Colony.TerraformCeiling(b);
            float reach = TerraformProjects.ReachableCeiling(b, s), pot = TerraformProjects.PotentialCeiling(b, s);
            return $"Now <color={Habitability.ScoreColorHex(now)}><b>{now:F0}%</b></color>  →  " +
                   $"ceiling today <color={Habitability.ScoreColorHex(ceiling)}><b>{ceiling:F0}%</b></color>  →  " +
                   $"with researched projects <color={Habitability.ScoreColorHex(reach)}><b>{reach:F0}%</b></color>  →  " +
                   $"with all known science <color={Habitability.ScoreColorHex(pot)}><b>{pot:F0}%</b></color>\n" +
                   $"<color=#9FB4C8>Colonizable at {Colony.FoundThreshold:F0}%.</color>";
        });

        Bar(p, () =>
        {
            float now = b.habitability;
            return (now / 100f, $"{now:F0}% habitable", Habitability.ScoreColor(now));
        });

        // The live grind toggle (terraformers raising habitability toward the ceiling).
        var mgr = ColonyManager.Instance;
        if (mgr != null)
        {
            var tf = UIFactory.Button(p, "", () => { mgr.ToggleTerraform(b); lastSig = null; }, 26);
            live.Button(tf, () =>
            {
                if (b.habitability >= Colony.FoundThreshold && !b.terraforming) return (false, "Already habitable");
                if (!Colony.CanReachLivable(b) && !b.terraforming) return (false, $"Can't be made livable for {s.name} — run projects below first");
                return (true, b.terraforming ? "Stop terraforming" : "Start terraforming (consumes water, energy, metal)");
            });
        }

        Header(p, "SURVEY — WHAT IS WRONG WITH THIS WORLD");
        var issues = TerraformDiagnosis.Analyze(b, s);
        if (issues.Count == 0) UIFactory.WrapText(p, $"<color=#4DFF6E>Nothing — this world already suits {s.name}.</color>", UITheme.SmallSize, UITheme.Good);
        foreach (var i in issues)
        {
            var ic = Card(p, new Color(0.14f, 0.10f, 0.10f, 0.85f));
            string hex = ColorUtility.ToHtmlStringRGB(Color.Lerp(UITheme.Warn, UITheme.Bad, i.severity));
            UIFactory.WrapText(ic, $"<b><color=#{hex}>{TerraformDiagnosis.Describe(i.problem)}</color></b>  <size=10><color=#9FB4C8>severity {i.severity * 100f:F0}%</color></size>",
                UITheme.SmallSize, UITheme.Text);
            Note(ic, i.detail);
        }

        Header(p, "PROJECTS");
        Note(p, "Projects raise this world's ceiling permanently. The full console has costs, durations and progress.");
        UIFactory.Button(p, "Open Terraforming Console ▸", () => TerraformWindow.Instance?.ShowFor(b), 26);
    }
}
