using System.Collections.Generic;
using UnityEngine;

// The six core research branches. (A seventh, "Ancients", is reserved for schematic-gated secret tech.)
public enum TechBranch { Foundations, Warfare, Science, Expansion, Exploration, Industry }

// A single node in the tech tree. Researching it spends Research Points and applies its persistent,
// stacking effects to the empire. Nodes require their prerequisite node(s), a minimum empire Tech
// Level, and optionally that a particular ore has been discovered in the field.
public class Tech
{
    public string id;
    public string name;
    public string desc;
    public TechBranch branch;
    public int tier;             // 1-3 (roughly tracks the empire level you research it at)
    public int cost;             // research points
    public int minEmpireLevel;   // empire Tech Level required
    public string[] prereqs;     // ids that must be researched first
    public OreType reqOre = OreType.None;   // an ore that must be discovered first (None = no requirement)

    // Persistent empire effects (all optional; summed across every researched node).
    public float researchRate;   // +fractional research rate (0.15 = +15%)
    public float buildCostCut;   // fractional reduction to build costs (0.10 = -10%)
    public float buildTimeCut;   // fractional reduction to build times
    public float terraCeiling;   // +points added to every world's terraform ceiling
    public float terraSpeed;     // +fractional terraforming speed
    public float rangeMult;      // +fractional travel range (multiplies with the empire level bonus)
    public float oreYield;       // +fractional ore/metal yield
    public string unlockNote;    // human-readable "this also enables …" (hulls/stations land later)

    public Tech(string id, string name, TechBranch branch, int tier, int cost, int minEmpireLevel, string[] prereqs, string desc)
    { this.id = id; this.name = name; this.branch = branch; this.tier = tier; this.cost = cost; this.minEmpireLevel = minEmpireLevel; this.prereqs = prereqs ?? new string[0]; this.desc = desc; }
}

// Aggregated, live empire modifiers derived from everything researched. Systems read these each frame.
public static class TechEffects
{
    public static float ResearchRateMult = 1f;
    public static float BuildCostMult = 1f;      // multiply a base cost by this
    public static float BuildTimeMult = 1f;
    public static float TerraformCeilingBonus = 0f;
    public static float TerraformSpeedMult = 1f;
    public static float OreYieldMult = 1f;

    public static void Reset()
    {
        ResearchRateMult = 1f; BuildCostMult = 1f; BuildTimeMult = 1f;
        TerraformCeilingBonus = 0f; TerraformSpeedMult = 1f; OreYieldMult = 1f;
    }
}

public static class TechDatabase
{
    static List<Tech> _all;
    static Dictionary<string, Tech> _byId;

    public static List<Tech> All { get { if (_all == null) Build(); return _all; } }
    public static Tech Get(string id) { if (_byId == null) Build(); return _byId.TryGetValue(id, out var t) ? t : null; }

    public static IEnumerable<Tech> InBranch(TechBranch b)
    { foreach (var t in All) if (t.branch == b) yield return t; }

    static Tech T(string id, string name, TechBranch br, int tier, int cost, int lvl, string[] pre, string desc)
        => new Tech(id, name, br, tier, cost, lvl, pre, desc);

    static void Build()
    {
        _all = new List<Tech>();

        // ---- Foundations (prerequisite spine) ----
        _all.Add(T("F1", "Applied Materials", TechBranch.Foundations, 1, 60, 1, null,
            "Stronger structural alloys. The root of the warfare and industry branches.")
            .With(oreYield: 0.15f));
        _all.Add(T("F2", "Fusion Power", TechBranch.Foundations, 1, 80, 1, new[] { "F1" },
            "Reliable fusion reactors power your colonies and drives.")
            .With(buildCostCut: 0.05f));
        _all.Add(T("F3", "Computing Cores", TechBranch.Foundations, 1, 90, 1, new[] { "F1" },
            "Faster laboratories and control systems.")
            .With(researchRate: 0.15f));
        _all.Add(T("F4", "Orbital Construction", TechBranch.Foundations, 2, 120, 2, new[] { "F1", "F2" },
            "Build in orbit — enables level-2 shipyards and medium hulls.")
            .With(buildTimeCut: 0.10f, unlock: "Enables medium hulls (Frigate, Miner, Hauler) once shipyards allow."));

        // ---- Warfare (unlock-focused; hulls arrive with the ship roster) ----
        _all.Add(T("W1", "Ballistics", TechBranch.Warfare, 1, 70, 1, new[] { "F1" },
            "Mass-driver weaponry.").With(unlock: "Enables the Frigate warship."));
        _all.Add(T("W2", "Armour Plating", TechBranch.Warfare, 1, 90, 1, new[] { "W1" },
            "Ablative hull armour for your warships.").With(unlock: "Enables the Fighter Mk II."));
        _all.Add(T("W3", "Energy Shields", TechBranch.Warfare, 2, 140, 2, new[] { "W2", "F2" },
            "Deflector shields absorb incoming fire.").With(unlock: "Enables the Cruiser."));

        // ---- Science ----
        _all.Add(T("S1", "Deep Sensors", TechBranch.Science, 1, 70, 1, new[] { "F3" },
            "Sharper survey scanners; reveal more anomalies.").With(researchRate: 0.05f));
        _all.Add(T("S2", "Laboratory Networks", TechBranch.Science, 2, 110, 2, new[] { "F3" },
            "Linked research labs across your colonies.").With(researchRate: 0.25f));
        _all.Add(T("S3", "Xenoarchaeology", TechBranch.Science, 2, 150, 2, new[] { "S1" },
            "Study ancient ruins — the door to precursor secrets.").With(researchRate: 0.10f, unlock: "Opens the Ancients research path."));
        var s4 = T("S4", "Exotic Matter Studies", TechBranch.Science, 3, 220, 3, new[] { "S2" },
            "Harness exotic ores — enables level-3 research facilities."); s4.reqOre = OreType.Neutronium;
        _all.Add(s4.With(researchRate: 0.40f));

        // ---- Expansion & Terraforming ----
        _all.Add(T("X1", "Closed-Loop Life Support", TechBranch.Expansion, 1, 80, 1, new[] { "F2" },
            "Self-sustaining colonies; cheaper city founding.").With(terraCeiling: 5f));
        _all.Add(T("X2", "Atmospheric Processing", TechBranch.Expansion, 2, 130, 2, new[] { "X1" },
            "Generate breathable atmospheres on hostile worlds.").With(terraCeiling: 12f));
        var x3 = T("X3", "Hydrosphere Seeding", TechBranch.Expansion, 2, 150, 2, new[] { "X2" },
            "Import comets and ice to give arid worlds oceans."); x3.reqOre = OreType.Cryonite;
        _all.Add(x3.With(terraCeiling: 12f));
        _all.Add(T("X4", "Climate Engineering", TechBranch.Expansion, 2, 190, 2, new[] { "X2" },
            "Orbital mirrors and shades tune a world's temperature.").With(terraCeiling: 12f, terraSpeed: 0.25f));
        _all.Add(T("X5", "Xeno-Adaptation", TechBranch.Expansion, 3, 260, 3, new[] { "X3", "X4" },
            "Adapt your colonists to harsher worlds.").With(terraCeiling: 10f, terraSpeed: 0.35f, unlock: "Enables the Colony Ship Mk II."));

        // ---- Exploration (drives that extend travel range, stacking with empire level) ----
        _all.Add(T("E1", "Ion Drives", TechBranch.Exploration, 1, 60, 1, new[] { "F2" },
            "Efficient ion propulsion — more range on every ship.").With(rangeMult: 0.12f, unlock: "Enables the Scout Mk II."));
        _all.Add(T("E2", "Long-Range Scanners", TechBranch.Exploration, 1, 90, 1, new[] { "E1" },
            "See farther; find more points of interest.").With(rangeMult: 0.05f));
        _all.Add(T("E3", "Warp Coils", TechBranch.Exploration, 2, 160, 2, new[] { "E1" },
            "Warp-assisted travel greatly extends fleet range.").With(rangeMult: 0.35f, unlock: "Enables the Explorer."));
        var e4 = T("E4", "Jump Drives", TechBranch.Exploration, 3, 280, 3, new[] { "E3" },
            "Near-instant jumps across vast distances."); e4.reqOre = OreType.Helium3;
        _all.Add(e4.With(rangeMult: 0.60f));

        // ---- Industry ----
        _all.Add(T("I1", "Automated Mining", TechBranch.Industry, 1, 70, 1, new[] { "F1" },
            "Robotic miners boost every ore and metal yield.").With(oreYield: 0.25f, unlock: "Enables the Miner ship."));
        _all.Add(T("I2", "Refineries", TechBranch.Industry, 1, 110, 1, new[] { "I1" },
            "Efficient refining lowers build costs.").With(buildCostCut: 0.10f, unlock: "Enables the Hauler."));
        _all.Add(T("I3", "Modular Shipyards", TechBranch.Industry, 2, 150, 2, new[] { "I2", "F4" },
            "Prefabricated modules speed construction; enables level-3 shipyards.").With(buildTimeCut: 0.15f));
        var i4 = T("I4", "Nanofabrication", TechBranch.Industry, 3, 240, 3, new[] { "I3" },
            "Atom-precise fabrication makes even capital hulls cheap."); i4.reqOre = OreType.Adamantine;
        _all.Add(i4.With(buildCostCut: 0.15f, buildTimeCut: 0.10f, unlock: "Enables Mk III refits."));

        _byId = new Dictionary<string, Tech>();
        foreach (var t in _all) _byId[t.id] = t;
    }
}

// Fluent helper so node definitions read cleanly.
public static class TechExtensions
{
    public static Tech With(this Tech t, float researchRate = 0f, float buildCostCut = 0f, float buildTimeCut = 0f,
        float terraCeiling = 0f, float terraSpeed = 0f, float rangeMult = 0f, float oreYield = 0f, string unlock = null)
    {
        t.researchRate = researchRate; t.buildCostCut = buildCostCut; t.buildTimeCut = buildTimeCut;
        t.terraCeiling = terraCeiling; t.terraSpeed = terraSpeed; t.rangeMult = rangeMult; t.oreYield = oreYield;
        t.unlockNote = unlock;
        return t;
    }
}

// Tracks researched technologies, gates what can be researched next, and pushes the aggregated effects
// into TechEffects / ShipUpgrades. Part of the save file.
public static class TechManager
{
    static readonly HashSet<string> researched = new HashSet<string>();
    public static event System.Action OnChanged;

    public static bool IsResearched(string id) => researched.Contains(id);

    // ---- Research queue (timed, pausable, editable — like the shipyard queue). The active tech fills
    // by draining research points from your bank over time; pause it, or remove/reorder entries. ----
    static readonly List<string> queue = new List<string>();
    static float progress;                 // RP accumulated toward the front tech
    static float drainCarry;               // fractional RP waiting to be spent as whole points
    public static bool Paused { get; private set; }
    const float DrainRate = 6f;            // base RP/sec funneled from the bank into active research

    public static IReadOnlyList<string> Queue => queue;
    public static string Active => queue.Count > 0 ? queue[0] : null;
    public static bool IsQueued(string id) => queue.Contains(id);
    public static int QueuePosition(string id) => queue.IndexOf(id);

    public static float ActiveProgress01
    {
        get { var t = TechDatabase.Get(Active); return t != null && t.cost > 0 ? Mathf.Clamp01(progress / t.cost) : 0f; }
    }
    public static float ActiveProgressRP => progress;

    public static bool CanQueue(Tech t, out string reason)
    {
        reason = null;
        if (t == null) { reason = "unknown"; return false; }
        if (researched.Contains(t.id)) { reason = "researched"; return false; }
        if (queue.Contains(t.id)) { reason = "queued"; return false; }
        if (EmpireTech.Level < t.minEmpireLevel) { reason = $"needs Empire Tech Level {t.minEmpireLevel}"; return false; }
        foreach (var p in t.prereqs)                       // prereq must be researched OR queued ahead
            if (!researched.Contains(p) && !queue.Contains(p)) { reason = "needs " + PrereqNames(t); return false; }
        if (t.reqOre != OreType.None && !ResearchManager.IsDiscovered(t.reqOre))
        { reason = $"discover {OreDatabase.Get(t.reqOre).displayName} first"; return false; }
        return true;
    }

    public static bool Enqueue(string id)
    {
        var t = TechDatabase.Get(id);
        if (t == null || !CanQueue(t, out _)) return false;
        queue.Add(id);
        OnChanged?.Invoke();
        return true;
    }

    public static void RemoveFromQueue(int index)
    {
        if (index < 0 || index >= queue.Count) return;
        if (index == 0) progress = 0f;
        queue.RemoveAt(index);
        PruneQueue();
        OnChanged?.Invoke();
    }

    public static void SetPaused(bool p) { Paused = p; OnChanged?.Invoke(); }
    public static void ClearQueue() { queue.Clear(); progress = 0f; OnChanged?.Invoke(); }

    // Drop any queued tech whose prerequisites are no longer satisfied (e.g. after a removal).
    static void PruneQueue()
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = 0; i < queue.Count; i++)
            {
                var t = TechDatabase.Get(queue[i]); if (t == null) continue;
                bool ok = true;
                foreach (var p in t.prereqs)
                {
                    bool satisfied = researched.Contains(p);
                    if (!satisfied) for (int j = 0; j < i; j++) if (queue[j] == p) { satisfied = true; break; }
                    if (!satisfied) { ok = false; break; }
                }
                if (!ok) { if (i == 0) progress = 0f; queue.RemoveAt(i); changed = true; break; }
            }
        }
    }

    // Advance the active research each frame (called by ResearchTaskManager).
    public static void Tick(float dt)
    {
        if (Paused || queue.Count == 0 || dt <= 0f) return;
        var t = TechDatabase.Get(queue[0]);
        if (t == null) { queue.RemoveAt(0); progress = 0f; return; }
        foreach (var p in t.prereqs) if (!researched.Contains(p)) { queue.RemoveAt(0); progress = 0f; return; }

        // Accumulate fractional research, then spend whole research points from the bank as they're earned.
        float rate = DrainRate * TechEffects.ResearchRateMult;
        drainCarry += rate * dt;
        int spend = Mathf.Min(ResearchManager.ResearchPoints, Mathf.FloorToInt(drainCarry));
        if (spend <= 0) return;                            // no whole RP available yet -> keep accumulating
        drainCarry -= spend;
        ResearchManager.AddPoints(-spend);
        progress += spend;
        if (progress >= t.cost)
        {
            progress -= t.cost;
            queue.RemoveAt(0);
            MarkResearched(t);
        }
    }

    public static bool PrereqsMet(Tech t)
    {
        foreach (var p in t.prereqs) if (!researched.Contains(p)) return false;
        return true;
    }

    public static bool CanResearch(Tech t, out string reason)
    {
        reason = null;
        if (t == null) { reason = "unknown"; return false; }
        if (researched.Contains(t.id)) { reason = "researched"; return false; }
        if (EmpireTech.Level < t.minEmpireLevel) { reason = $"needs Empire Tech Level {t.minEmpireLevel}"; return false; }
        if (!PrereqsMet(t)) { reason = "needs " + PrereqNames(t); return false; }
        if (t.reqOre != OreType.None && !ResearchManager.IsDiscovered(t.reqOre))
        { reason = $"discover {OreDatabase.Get(t.reqOre).displayName} first"; return false; }
        if (ResearchManager.ResearchPoints < t.cost) { reason = $"need {t.cost} RP"; return false; }
        return true;
    }

    static string PrereqNames(Tech t)
    {
        var missing = new List<string>();
        foreach (var p in t.prereqs) if (!researched.Contains(p)) { var pt = TechDatabase.Get(p); missing.Add(pt != null ? pt.name : p); }
        return string.Join(", ", missing);
    }

    public static bool Research(string id)   // instant-complete (dev / fallback; the UI queues instead)
    {
        var t = TechDatabase.Get(id);
        if (t == null || !CanResearch(t, out _)) return false;
        ResearchManager.AddPoints(-t.cost);
        MarkResearched(t);
        return true;
    }

    static void MarkResearched(Tech t)
    {
        researched.Add(t.id);
        Recompute();
        SimpleAudio.Instance?.PlayNotify(NotifKind.Research);
        NotificationManager.Instance?.Push($"Researched: {t.name}",
            t.desc + (string.IsNullOrEmpty(t.unlockNote) ? "" : "  " + t.unlockNote), null, NotifKind.Research);
        OnChanged?.Invoke();
    }

    // Sum every researched node's effects into the live modifier tables.
    public static void Recompute()
    {
        TechEffects.Reset();
        float rr = 0f, bc = 0f, bt = 0f, tc = 0f, ts = 0f, rm = 0f, oy = 0f;
        foreach (var id in researched)
        {
            var t = TechDatabase.Get(id); if (t == null) continue;
            rr += t.researchRate; bc += t.buildCostCut; bt += t.buildTimeCut;
            tc += t.terraCeiling; ts += t.terraSpeed; rm += t.rangeMult; oy += t.oreYield;
        }
        TechEffects.ResearchRateMult = 1f + rr;
        TechEffects.BuildCostMult = Mathf.Clamp(1f - bc, 0.4f, 1f);
        TechEffects.BuildTimeMult = Mathf.Clamp(1f - bt, 0.4f, 1f);
        TechEffects.TerraformCeilingBonus = tc;
        TechEffects.TerraformSpeedMult = 1f + ts;
        TechEffects.OreYieldMult = 1f + oy;
        ShipUpgrades.TechRange = 1f + rm;   // multiplies the empire-level range bonus
    }

    public static void Reset()
    {
        researched.Clear();
        queue.Clear(); progress = 0f; drainCarry = 0f; Paused = false;
        Recompute();
        OnChanged?.Invoke();
    }

    // ---- Save / load ----
    public static List<string> Export() => new List<string>(researched);
    public static void Import(List<string> ids)
    {
        researched.Clear();
        queue.Clear(); progress = 0f; drainCarry = 0f; Paused = false;
        if (ids != null) foreach (var id in ids) researched.Add(id);
        Recompute();
        OnChanged?.Invoke();
    }
}
