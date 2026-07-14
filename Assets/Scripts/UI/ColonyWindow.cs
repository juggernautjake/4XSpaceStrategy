using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// The colony control panel for the selected world. Shows the "fully established" objectives, lets you
// terraform an uninhabitable world toward livability (with clear criteria), and build/again see the
// colony's structures. Appears for worlds you own, or worlds you have a ship at (for terraforming).
public class ColonyWindow : MonoBehaviour
{
    public static ColonyWindow Instance;

    GameObject root;
    TMP_Text titleText, statusText, objectivesText, terraformStatus, constructionText;
    Toggle terraformToggle;
    RectTransform buildList;
    Image constructionFill;
    CelestialBody body;
    string lastBuildSig = "";
    bool suppress;

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("ColonyWindow");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<ColonyWindow>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        var content = UIFactory.Window(parent, "Colony", new Vector2(440, 600), out root, out titleText);
        var rt = root.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(220, 0);
        UIFactory.VerticalLayout(content, 6);

        statusText = UIFactory.Label(content, "", UITheme.SmallSize, UITheme.Text, 40);
        statusText.alignment = TextAlignmentOptions.TopLeft;

        UIFactory.Label(content, "OBJECTIVES TO FULLY ESTABLISH", UITheme.SmallSize, UITheme.Accent, 16);
        objectivesText = UIFactory.Label(content, "", UITheme.SmallSize, UITheme.Text, 70);
        objectivesText.alignment = TextAlignmentOptions.TopLeft;

        UIFactory.Label(content, "TERRAFORMING", UITheme.SmallSize, UITheme.Accent, 16);
        terraformStatus = UIFactory.Label(content, "", UITheme.SmallSize, UITheme.SubText, 48);
        terraformStatus.alignment = TextAlignmentOptions.TopLeft;
        terraformToggle = UIFactory.Toggle(content, "Terraform this world", false, on =>
        {
            if (suppress || body == null) return;
            if (on) { if (!ColonyManager.Instance.ToggleTerraform(body)) { suppress = true; terraformToggle.isOn = false; suppress = false; } }
            else if (body.terraforming) ColonyManager.Instance.ToggleTerraform(body);
        });

        UIFactory.Label(content, "STRUCTURES", UITheme.SmallSize, UITheme.Accent, 16);

        // Construction progress bar.
        var barHolder = UIFactory.NewUI(content, "Bar"); UIFactory.AddLayout(barHolder, 18);
        var track = UIFactory.Panel(barHolder.transform, "Track", UITheme.TrackBg); UIFactory.Stretch(track.rectTransform);
        constructionFill = UIFactory.Panel(track.transform, "Fill", UITheme.Good);
        var frt = constructionFill.rectTransform;
        frt.anchorMin = new Vector2(0, 0); frt.anchorMax = new Vector2(0, 1); frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
        constructionText = UIFactory.Text(barHolder.transform, "", UITheme.SmallSize, UITheme.Text, TextAlignmentOptions.Center);
        UIFactory.Stretch(constructionText.rectTransform);

        var holder = UIFactory.NewUI(content, "BuildHolder").GetComponent<RectTransform>();
        UIFactory.AddLayout(holder.gameObject, 240);
        UIFactory.ScrollView(holder, out buildList);

        PlanetUI.OnBodySelected += ShowFor;
        PlanetUI.OnClosed += Hide;
        if (UnitManager.Instance != null)
        {
            UnitManager.Instance.OnUnitsChanged += RefreshIfShowing;
            UnitManager.Instance.OnBuildChanged += RefreshIfShowing;   // shipyard built/upgraded
        }
        root.SetActive(false);
    }

    bool Eligible(CelestialBody b)
    {
        if (b == null) return false;
        if (b.owner == FactionManager.Player) return true;
        if (b.units != null) foreach (var u in b.units) if (u.owner == FactionManager.Player) return true;   // present -> can terraform
        return false;
    }

    public void ShowFor(CelestialBody b)
    {
        if (!Eligible(b)) { Hide(); return; }
        body = b;
        titleText.text = $"Colony — {b.name}";
        lastBuildSig = "";
        RebuildBuildings();
        UpdateLive();
        root.SetActive(true);
        root.GetComponent<RectTransform>().SetAsLastSibling();
    }

    void RefreshIfShowing() { if (root != null && root.activeSelf && body != null) { RebuildBuildings(); } }

    public void Hide() { if (root != null) root.SetActive(false); }

    void Update()
    {
        if (root == null || !root.activeSelf || body == null) return;
        UpdateLive();
        RebuildBuildings();   // cheap: no-ops unless buildings / shipyard level / construction changed
    }

    void UpdateLive()
    {
        var b = body;
        string owner = b.owner == FactionManager.Player ? "yours" : "unclaimed (ship present)";
        statusText.text =
            $"<b>{b.name}</b>  ({b.type})  ·  {owner}\n" +
            $"Population {b.population}   Cities {b.cities}   Habitability {b.habitability:F0}%\n" +
            $"Development: <b>{Colony.ClaimProgress(b) * 100f:F0}%</b>" + (Colony.IsFullyEstablished(b) ? "  <color=#4DFF6E>FULLY ESTABLISHED</color>" : "");

        var sb = new StringBuilder();
        foreach (var o in Colony.Objectives(b))
            sb.AppendLine($"{(o.done ? "<color=#4DFF6E>[x]</color>" : "<color=#FF7A6E>[ ]</color>")} {o.label}  <color=#9FB4C8>({o.detail})</color>");
        objectivesText.text = sb.ToString();

        // Terraforming criteria + live status.
        float ceiling = Colony.TerraformCeiling(b);
        if (b.isHabitable || b.habitability >= Colony.FoundThreshold)
            terraformStatus.text = $"<color=#4DFF6E>Already habitable</color> for {SpeciesManager.Current.name} ({b.habitability:F0}%).";
        else if (!Colony.CanReachLivable(b))
            terraformStatus.text = $"Ceiling {b.terraformability:F0}% — <color=#FF7A6E>can't be made livable</color> for {SpeciesManager.Current.name}. Needs {Colony.FoundThreshold:F0}%.";
        else
            terraformStatus.text = $"Can reach {ceiling:F0}% (need {Colony.FoundThreshold:F0}% to colonize). Consumes water, energy and metal per % raised." +
                (b.terraforming ? "  <color=#4DFF6E>In progress…</color>" : "");
        suppress = true; terraformToggle.isOn = b.terraforming; terraformToggle.interactable = !(b.habitability >= Colony.FoundThreshold); suppress = false;

        // Construction bar.
        var c = ColonyManager.Instance != null ? ColonyManager.Instance.ConstructionFor(b) : null;
        if (c != null)
        {
            constructionFill.rectTransform.anchorMax = new Vector2(c.Progress, 1f);
            string lbl = string.IsNullOrEmpty(c.Label) ? $"Building {BuildingDatabase.Get(c.type).name}" : c.Label;
            constructionText.text = $"{lbl}: {c.Progress * 100f:F0}%";
        }
        else { constructionFill.rectTransform.anchorMax = new Vector2(0, 1f); constructionText.text = ""; }
    }

    // Rebuild the building buttons only when the set of buildings changes.
    void RebuildBuildings()
    {
        var b = body;
        var mgr = ColonyManager.Instance;
        string qsig = "";
        if (mgr != null) foreach (var c in mgr.QueueFor(b)) qsig += (c.shipyardUpgrade ? "U" : c.labUpgrade ? "L" : c.establishCity ? "C" : c.type.ToString()) + ";";
        string sig = string.Join(",", b.buildings) + $"|yard{b.shipyardLevel}|lab{b.researchCenterLevel}|hab{b.habitability >= Colony.FoundThreshold}|q{qsig}";
        if (sig == lastBuildSig && buildList.childCount > 0) return;
        lastBuildSig = sig;

        for (int i = buildList.childCount - 1; i >= 0; i--) Destroy(buildList.GetChild(i).gameObject);

        // Already-built list.
        if (b.buildings.Count > 0)
        {
            var sb = new StringBuilder("Built: ");
            foreach (var id in b.buildings) sb.Append(BuildingDatabase.Get((BuildingType)id).name + "  ");
            UIFactory.Label(buildList, sb.ToString(), UITheme.SmallSize, UITheme.SubText, 24);
        }

        // Construction queue (built one-by-one): the first entry is building, the rest wait. Each cancels
        // for a refund.
        if (mgr != null)
        {
            var q = mgr.QueueFor(b);
            for (int i = 0; i < q.Count; i++)
            {
                var c = q[i];
                string nm = c.shipyardUpgrade ? "Shipyard upgrade" : c.labUpgrade ? "Research centre upgrade" : c.establishCity ? "City" : BuildingDatabase.Get(c.type).name;
                string state = i == 0 ? "building" : $"queued #{i}";
                string pfx = i == 0 ? "> " : $"{i + 1}. ";
                var rowGo = UIFactory.NewUI(buildList, "QRow"); UIFactory.AddLayout(rowGo, 24);
                var rh = rowGo.AddComponent<HorizontalLayoutGroup>(); rh.spacing = 6; rh.childControlWidth = true; rh.childControlHeight = true; rh.childForceExpandWidth = true;
                UIFactory.Text(rowGo.transform, $"{pfx}{nm}  <color=#9FB4C8>({state})</color>", UITheme.SmallSize, i == 0 ? UITheme.Accent : UITheme.Text, TextAlignmentOptions.Left);
                var cap = c;
                var rm = UIFactory.Button(rowGo.transform, "X", () => { mgr.CancelConstruction(cap); lastBuildSig = ""; RebuildBuildings(); }, 22);
                var le = rm.GetComponent<LayoutElement>(); if (le != null) le.preferredWidth = 30;
            }
        }

        // Owned world without a city yet (e.g. a home moon): let the player found one — once it's
        // habitable enough (terraform first if not). This is how birthright worlds get settled.
        if (mgr != null && b.owner == FactionManager.Player && !b.buildings.Contains((int)BuildingType.City))
        {
            bool canCity = mgr.CanEstablishCity(b, out string cityWhy);
            var cityBtn = UIFactory.Button(buildList,
                canCity ? $"Establish City  ({ColonyManager.CityMetal}m {ColonyManager.CityEnergy}e, {ColonyManager.CityBuildTime:F0}s)"
                        : $"Establish City — {cityWhy}",
                () => { if (mgr.StartEstablishCity(b)) { lastBuildSig = ""; RebuildBuildings(); } }, 30);
            cityBtn.interactable = canCity;
        }

        // Shipyard tier + upgrade. The headline perk of a tier is BUILD POWER: how many hulls this yard
        // can hold on the stocks at once, pooled with every other yard you own.
        if (b.shipyardLevel >= 1)
        {
            UIFactory.Label(buildList, $"Shipyard: <b>Level {b.shipyardLevel}</b>  " +
                $"<color=#8FD0FF>{BuildPower.ForBody(b)} build power</color>  <color=#9FB4C8>({Colony.ShipyardPerk(b.shipyardLevel)})</color>",
                UITheme.SmallSize, UITheme.SubText, 22);
            if (mgr != null && b.shipyardLevel < Colony.MaxShipyardLevel)
            {
                int next = b.shipyardLevel + 1;
                bool canUp = mgr.CanUpgradeShipyard(b, out string upWhy, out _);
                int m = ColonyManager.ShipyardUpgradeMetal(next), e = ColonyManager.ShipyardUpgradeEnergy(next);
                var upBtn = UIFactory.Button(buildList,
                    canUp ? $"Upgrade Shipyard → Lv{next}  ({m}m {e}e, {ColonyManager.ShipyardUpgradeTime(next):F0}s) → {BuildPower.ForLevel(next)} build power"
                          : $"Upgrade Shipyard → Lv{next} — {upWhy}",
                    () => { if (mgr.StartShipyardUpgrade(b)) { lastBuildSig = ""; RebuildBuildings(); } }, 30);
                upBtn.interactable = canUp;
            }
        }

        // Research-centre tier + upgrade — the laboratory twin of the shipyard ladder.
        if (b.researchCenterLevel >= 1)
        {
            UIFactory.Label(buildList, $"Research Centre: <b>Level {b.researchCenterLevel}</b>  " +
                $"<color=#8FD0FF>{ResearchCapacity.ForBody(b)} research capacity</color>",
                UITheme.SmallSize, UITheme.SubText, 22);
            if (mgr != null && b.researchCenterLevel < Colony.MaxResearchCenterLevel)
            {
                int next = b.researchCenterLevel + 1;
                bool canUp = mgr.CanUpgradeLab(b, out string upWhy, out _);
                int m = ColonyManager.LabUpgradeMetal(next), e = ColonyManager.LabUpgradeEnergy(next);
                var upBtn = UIFactory.Button(buildList,
                    canUp ? $"Upgrade Research Centre → Lv{next}  ({m}m {e}e, {ColonyManager.LabUpgradeTime(next):F0}s) → {ResearchCapacity.ForLevel(next)} capacity"
                          : $"Upgrade Research Centre → Lv{next} — {upWhy}",
                    () => { if (mgr.StartLabUpgrade(b)) { lastBuildSig = ""; RebuildBuildings(); } }, 30);
                upBtn.interactable = canUp;
            }
        }

        foreach (BuildingType t in System.Enum.GetValues(typeof(BuildingType)))
        {
            if (t == BuildingType.City) continue;
            var info = BuildingDatabase.Get(t);
            bool can = mgr != null && mgr.CanBuild(b, t, out string reason);
            string why = "";
            if (!can && mgr != null) mgr.CanBuild(b, t, out why);
            string label = can
                ? $"Build {info.name}  ({info.costMetal}m {info.costEnergy}e, {info.buildTime:F0}s)"
                : $"{info.name} — {(b.buildings.Contains((int)t) ? "built" : why)}";
            var btn = UIFactory.Button(buildList, label, () => { if (mgr != null && mgr.StartBuilding(b, t)) { lastBuildSig = ""; RebuildBuildings(); } }, 30);
            btn.interactable = can;
        }
    }

    void OnDestroy()
    {
        PlanetUI.OnBodySelected -= ShowFor;
        PlanetUI.OnClosed -= Hide;
        if (UnitManager.Instance != null) UnitManager.Instance.OnUnitsChanged -= RefreshIfShowing;
    }
}
