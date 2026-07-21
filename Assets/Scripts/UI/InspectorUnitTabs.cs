using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// The Inspector's tabs for SHIPS, STATIONS and FLEETS.
//
//   Ship:    Overview (task + actions) · Orders (the queue) · Stats · Effects (stations only)
//   Fleet:   Ships (the roster) · Orders (issued to everyone at once)
public partial class InspectorWindow
{
    void CollectUnitTabs()
    {
        tabs.Add(new InspectorTab("Overview", BuildUnitOverview));
        tabs.Add(new InspectorTab("Orders", BuildUnitOrders));
        tabs.Add(new InspectorTab("Stats", BuildUnitStats));
        tabs.Add(new InspectorTab("Effects", BuildUnitEffects,
            () => target.unit != null && (target.unit.Info.isStation || target.unit.Info.isWorker)));
    }

    // ---------------- Ship overview ----------------
    void BuildUnitOverview(Transform p)
    {
        var u = target.unit;

        var card = Card(p);
        Stat(card, "Class", () => u.Info.name);
        Stat(card, "Rank", () => $"<color=#FFD24D>{u.RankName}</color>  <color=#9FB4C8>({u.experience:F0} XP)</color>");
        Stat(card, "Owner", () =>
        {
            string hex = "#" + ColorUtility.ToHtmlStringRGB(FactionManager.OwnerColor(u.owner));
            return $"<color={hex}>{FactionManager.OwnerName(u.owner)}</color>";
        });
        Stat(card, "Control group", () =>
        {
            int g = ControlGroups.GroupOf(u);
            return g > 0 ? $"<color=#5AB4F0>{g}</color>  <color=#9FB4C8>(press {g} to recall)</color>" : "<color=#9FB4C8>none — Ctrl+1..9 to bind</color>";
        });

        // What it's doing right now, with the matching progress bar.
        Header(p, "CURRENT TASK");
        var task = UIFactory.WrapText(p, "", UITheme.SmallSize, UITheme.Text);
        live.Text(task, () => TaskLine(u));
        Bar(p, () =>
        {
            float prog = TaskProgress(u);
            return (prog, prog > 0f ? $"{prog * 100f:F0}%" : "", u.queuePaused ? UITheme.Warn : UITheme.Good);
        });

        // Carried ore samples — easy to forget a scout is holding research you haven't cashed in.
        var samples = UIFactory.WrapText(p, "", UITheme.SmallSize, UITheme.Accent);
        live.Text(samples, () => u.samples != null && u.samples.Count > 0
            ? $"<color=#8FD0FF>Carrying {u.samples.Count} ore sample(s)</color> — take them to a research ship or a world with a research centre."
            : "");

        // Actions. Each explains itself when it can't be used.
        Header(p, "ACTIONS");

        // Same Focus offered on right-click, here for a ship you already have selected.
        var focusBtn = UIFactory.Button(p, "Focus Camera", () =>
        {
            var t = UnitVisuals.TransformOf(u);
            if (t != null) CameraController.Instance?.FocusAndZoom(t, 3f, true);
        }, 26);
        live.Button(focusBtn, () => UnitVisuals.TransformOf(u) != null
            ? (true, $"Focus on {u.name}")
            : (false, "Focus — this ship isn't on screen"));

        var surveyBtn = UIFactory.Button(p, "", () =>
        {
            if (u.location != null) UnitManager.Instance?.IssueAction(new List<Unit> { u }, OrderKind.Survey, u.location, false);
        }, 26);
        live.Button(surveyBtn, () =>
        {
            if (!u.Info.canExplore) return (false, "Survey — this class can't survey");
            if (u.location == null) return (false, "Survey — travel to a world first");
            if (u.location.Surveyed) return (false, $"Survey — {u.location.name} is already surveyed");
            return (true, $"Survey {u.location.name}");
        });

        var researchBtn = UIFactory.Button(p, "", () =>
        {
            if (u.location != null) UnitManager.Instance?.IssueAction(new List<Unit> { u }, OrderKind.Research, u.location, false);
        }, 26);
        live.Button(researchBtn, () =>
        {
            if (!u.Info.canResearch) return (false, "Deep Survey — needs a research ship");
            if (u.location == null) return (false, "Deep Survey — travel to a world first");
            if (!u.location.Surveyed) return (false, $"Deep Survey — survey {u.location.name} first");
            return (true, u.location.deepSurveyed
                ? $"Deep Survey {u.location.name} again"
                : $"Deep Survey {u.location.name}");
        });

        var colonizeBtn = UIFactory.Button(p, "", () =>
        {
            if (u.location != null) UnitManager.Instance?.IssueAction(new List<Unit> { u }, OrderKind.Colonize, u.location, false);
        }, 26);
        live.Button(colonizeBtn, () =>
        {
            if (!u.Info.canColonize) return (false, "Found Colony — colony ships only");
            if (u.location == null) return (false, "Found Colony — travel to a world first");
            if (u.location.owner == FactionManager.Player) return (false, "Found Colony — already yours");
            if (u.location.habitability < UnitManager.ColonizeMinHabitability)
                return (false, $"Found Colony — needs {UnitManager.ColonizeMinHabitability:F0}% hab (this: {u.location.habitability:F0}%)");
            return (true, $"Found Colony on {u.location.name} (consumes ship)");
        });

        if (u.Info.canTerraform)
        {
            var tfBtn = UIFactory.Button(p, "", () =>
            {
                if (u.location != null) UnitManager.Instance?.IssueAction(new List<Unit> { u }, OrderKind.Terraform, u.location, false);
            }, 26);
            live.Button(tfBtn, () =>
            {
                if (u.location == null) return (false, "Terraform — travel to a world first");
                if (u.location.habitability >= Colony.FoundThreshold) return (false, "Terraform — already habitable");
                if (!Colony.CanReachLivable(u.location)) return (false, "Terraform — can't be made livable yet");
                return (true, $"Terraform {u.location.name}");
            });
        }

        var row = UIFactory.NewUI(p, "Row"); UIFactory.AddLayout(row, 28);
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 6; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true;
        UIFactory.Button(row.transform, "Send…", () =>
        {
            var fleet = new List<Unit>();
            foreach (var s in UnitSelection.Selected) if (s.location != null || s.inSpace) fleet.Add(s);
            if (fleet.Count == 0 && (u.location != null || u.inSpace)) fleet.Add(u);
            FleetMovementController.Instance?.Arm(fleet);
        }, 26);
        var homeBtn = UIFactory.Button(row.transform, "Return Home", () =>
        {
            UnitManager.Instance?.SendUnitsHome(new List<Unit> { u });
        }, 26);
        live.Button(homeBtn, () => (u.location != UnitManager.Instance?.HomePlanet, "Return Home"));

        // Where it is, so you can jump to the world it's sitting at.
        var locBtn = UIFactory.Button(p, "", () => { if (u.location != null) PlanetUI.Instance?.Show(u.location); }, 24);
        live.Button(locBtn, () => u.location != null
            ? (true, $"Inspect {u.location.name} »")
            : (false, u.inSpace ? "Holding position in deep space" : "In transit"));

        // Disposal.
        Header(p, "DISPOSE");
        var row2 = UIFactory.NewUI(p, "Row2"); UIFactory.AddLayout(row2, 28);
        var h2 = row2.AddComponent<HorizontalLayoutGroup>();
        h2.spacing = 6; h2.childControlWidth = true; h2.childControlHeight = true; h2.childForceExpandWidth = true;
        var sd = UIFactory.Button(row2.transform, "Self-Destruct", () => { UnitManager.Instance?.DestroyUnit(u, false); Hide(); }, 26);
        var sdc = sd.colors; sdc.normalColor = new Color(0.4f, 0.15f, 0.15f); sdc.highlightedColor = sdc.normalColor; sdc.selectedColor = sdc.normalColor; sd.colors = sdc;
        var scrapBtn = UIFactory.Button(row2.transform, "", () => { UnitManager.Instance?.DestroyUnit(u, true); Hide(); }, 26);
        live.Button(scrapBtn, () =>
        {
            bool can = UnitManager.Instance != null && UnitManager.Instance.CanScrap(u);
            return (can, can ? "Scrap (recover 20-30%)" : "Scrap — only at a world you hold");
        });
    }

    static string TaskLine(Unit u)
    {
        string task;
        switch (u.status)
        {
            case UnitStatus.Traveling:
                task = $"Traveling to {(u.travelTarget != null ? u.travelTarget.name : "a point in space")} " +
                       $"({Mathf.Max(0f, u.travelDuration - u.travelElapsed):F0}s)"; break;
            case UnitStatus.Exploring: task = $"Surveying {(u.location != null ? u.location.name : "?")}"; break;
            case UnitStatus.Colonizing: task = $"Colonizing {(u.location != null ? u.location.name : "?")}"; break;
            case UnitStatus.Researching: task = $"Researching {(u.location != null ? u.location.name : "?")}"; break;
            case UnitStatus.Returning: task = "Returning home"; break;
            default: task = u.location != null ? $"Idle at {u.location.name}" : (u.inSpace ? "Holding position in deep space" : "Idle"); break;
        }
        if (u.queuePaused) task += "  <color=#FFBF4D>(queue paused)</color>";
        return task;
    }

    static float TaskProgress(Unit u)
    {
        switch (u.status)
        {
            case UnitStatus.Traveling: return u.TravelProgress;
            case UnitStatus.Exploring: return u.location != null ? u.location.explorationProgress : 0f;
            case UnitStatus.Colonizing: return u.location != null ? u.location.claimProgress : 0f;
            case UnitStatus.Researching: return u.location != null ? u.location.researchProgress : 0f;
            default: return 0f;
        }
    }

    // ---------------- Ship orders ----------------
    void BuildUnitOrders(Transform p)
    {
        var u = target.unit;

        Header(p, "ORDER QUEUE");
        Note(p, "Ctrl+right-click the map to add an order to the end instead of replacing the queue.");

        var row = UIFactory.NewUI(p, "Row"); UIFactory.AddLayout(row, 28);
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 6; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true;
        var pauseBtn = UIFactory.Button(row.transform, "", () => UnitManager.Instance?.SetPaused(u, !u.queuePaused), 26);
        live.Button(pauseBtn, () => (true, u.queuePaused ? "Resume Queue" : "Pause Queue"));
        UIFactory.Button(row.transform, "Stop All", () => { UnitManager.Instance?.StopAll(u); lastSig = null; }, 26);

        int count = u.orders != null ? u.orders.Count : 0;
        if (count == 0) { Note(p, "No orders queued. The ship is following its class default."); return; }

        for (int i = 0; i < count; i++)
        {
            int idx = i;
            var o = u.orders[i];
            var card = Card(p);
            var t = UIFactory.WrapText(card, "", UITheme.SmallSize, UITheme.Text);
            live.Text(t, () =>
            {
                string tag = idx == 0 ? "<color=#4DFF6E>» now</color>" : $"<color=#9FB4C8>{idx}.</color>";
                return $"{tag}  {o.Describe()}";
            });
            UIFactory.Button(card, "Remove", () => { UnitManager.Instance?.RemoveOrder(u, idx); lastSig = null; }, 22);
        }
    }

    // ---------------- Ship stats ----------------
    void BuildUnitStats(Transform p)
    {
        var u = target.unit;
        var info = u.Info;

        Header(p, "COMBAT & MOVEMENT");
        var card = Card(p);
        Stat(card, "Health", () => u.EffectiveHealth.ToString());
        Stat(card, "Armor", () => u.Armor.ToString());
        Stat(card, "Attack", () => u.EffectiveAttack.ToString());
        Stat(card, "Speed", () => u.Speed.ToString());
        Stat(card, "Research", () => u.EffectiveResearch.ToString());
        Stat(card, "Range", () =>
        {
            float er = UnitManager.Instance != null ? UnitManager.Instance.EffectiveRange(u) : info.range;
            return (info.range <= 0 || er >= float.MaxValue) ? "unlimited" : $"{er:F0}  <color=#9FB4C8>(base {info.range})</color>";
        });

        Header(p, "SERVICE RECORD");
        var rec = Card(p);
        Stat(rec, "Rank", () => $"<color=#FFD24D>{u.RankName}</color>");
        Stat(rec, "Experience", () => $"{u.experience:F0} XP");
        Stat(rec, "Worlds visited", () => u.worldsExplored.ToString());
        Stat(rec, "Time in service", () => $"{u.serviceTime:F0}s");

        Header(p, "CLASS");
        var cls = Card(p);
        Stat(cls, "Build cost", () => $"{ColonyManager.DiscCost(info.costMetal)} metal · {ColonyManager.DiscCost(info.costEnergy)} energy");
        Stat(cls, "Build power", () => $"{info.buildPower}");
        Stat(cls, "Build time", () => $"{info.buildTime * TechEffects.BuildTimeMult:F0}s");
        Note(cls, info.description);
    }

    // ---------------- Station / worker effects ----------------
    void BuildUnitEffects(Transform p)
    {
        var u = target.unit;
        var info = u.Info;

        Header(p, "ROLE");
        var card = Card(p);
        Stat(card, "Kind", () => info.isStation ? $"Station · {info.stationRole}" : "Civilian worker");
        Stat(card, "Status", () =>
        {
            bool deployed = u.status != UnitStatus.Traveling && (u.location != null || u.inSpace);
            string where = u.location != null ? $"at {u.location.name}" : (u.inSpace ? "in deep space" : "in transit");
            return deployed ? $"<color=#4DFF6E>deployed & working</color> ({where})"
                            : "<color=#FFBF4D>not yet deployed — send it into position</color>";
        });

        Header(p, "PASSIVE EFFECTS");
        var fx = Card(p);
        var parts = new List<string>();
        if (info.researchAura > 0f) parts.Add($"+{info.researchAura:0.#} research per second");
        if (info.supplyBonus > 0f) parts.Add($"+{info.supplyBonus:0.#} metal & energy per second");
        if (info.mineBonus > 0f) parts.Add($"+{info.mineBonus:0.#} metal per second from mining");
        if (info.terraformAura > 0f) parts.Add($"+{info.terraformAura:0.#}× terraform speed at the world it orbits");
        if (info.relayBoost > 0f) parts.Add($"+{info.relayBoost * 100f:0}% fleet travel range & speed while active");
        if (info.deepSpace) parts.Add("Runs on starlight — can be deployed anywhere, no world needed");
        if (parts.Count == 0) Note(fx, "This hull has no passive effects.");
        else foreach (var s in parts) UIFactory.WrapText(fx, $"<color=#8FE9C0>• {s}</color>", UITheme.SmallSize, UITheme.Good);

        Note(p, "Effects only apply while the station is DEPLOYED — parked at a world (or in open space, for deep-space stations) and not travelling.");
    }

    // ---------------- Fleet ----------------
    void CollectFleetTabs()
    {
        tabs.Add(new InspectorTab("Ships", BuildFleetShips));
        tabs.Add(new InspectorTab("Orders", BuildFleetOrders));
    }

    void BuildFleetShips(Transform p)
    {
        Header(p, "SELECTED SHIPS");
        var summary = UIFactory.WrapText(p, "", UITheme.SmallSize, UITheme.Text);
        live.Text(summary, () =>
        {
            var sel = UnitSelection.Selected;
            int slowest = int.MaxValue;
            float range = float.MaxValue;
            foreach (var u in sel)
            {
                slowest = Mathf.Min(slowest, Mathf.Max(1, u.Speed));
                if (UnitManager.Instance != null) range = Mathf.Min(range, UnitManager.Instance.EffectiveRange(u));
            }
            string r = range >= float.MaxValue ? "unlimited" : $"{range:F0}";
            return $"{sel.Count} ships · fleet speed <b>{(slowest == int.MaxValue ? 0 : slowest)}</b> (its slowest ship) · fleet range <b>{r}</b> (its shortest-ranged ship)";
        });

        // Bind this whole selection to a control group without touching the keyboard.
        Header(p, "CONTROL GROUP");
        var row = UIFactory.NewUI(p, "Groups"); UIFactory.AddLayout(row, 24);
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 3; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true;
        for (int g = 1; g <= ControlGroups.Count; g++)
        {
            int captured = g;
            UIFactory.Button(row.transform, g.ToString(), () =>
            {
                ControlGroups.Assign(captured, UnitSelection.Selected);
                lastSig = null;
            }, 22);
        }
        Note(p, "Click a number to bind this selection to that group (same as Ctrl+N). Press the number any time to reselect it and jump the camera there.");

        Header(p, "ROSTER");
        foreach (var u in new List<Unit>(UnitSelection.Selected)) UnitRow(p, u);
    }

    void BuildFleetOrders(Transform p)
    {
        Header(p, "ORDERS FOR THE WHOLE FLEET");
        Note(p, "These apply to every selected ship at once. The fleet travels at its slowest ship's speed and is limited by its shortest-ranged one.");

        UIFactory.Button(p, "Send…", () =>
        {
            var fleet = new List<Unit>();
            foreach (var u in UnitSelection.Selected) if (u.location != null || u.inSpace) fleet.Add(u);
            FleetMovementController.Instance?.Arm(fleet);
        }, 28);

        UIFactory.Button(p, "Return Home", () =>
        {
            UnitManager.Instance?.SendUnitsHome(new List<Unit>(UnitSelection.Selected));
        }, 28);

        UIFactory.Button(p, "Pause All Queues", () =>
        {
            foreach (var u in UnitSelection.Selected) UnitManager.Instance?.SetPaused(u, true);
        }, 28);

        UIFactory.Button(p, "Resume All Queues", () =>
        {
            foreach (var u in UnitSelection.Selected) UnitManager.Instance?.SetPaused(u, false);
        }, 28);

        UIFactory.Button(p, "Stop All", () =>
        {
            foreach (var u in new List<Unit>(UnitSelection.Selected)) UnitManager.Instance?.StopAll(u);
        }, 28);

        Header(p, "WHAT EACH SHIP IS DOING");
        foreach (var u in new List<Unit>(UnitSelection.Selected))
        {
            var cap = u;
            var card = Card(p);
            var t = UIFactory.WrapText(card, "", UITheme.SmallSize, UITheme.Text);
            live.Text(t, () => $"<b>{cap.name}</b>  <size=10><color=#9FB4C8>{TaskLine(cap)}</color></size>");
        }
    }
}
