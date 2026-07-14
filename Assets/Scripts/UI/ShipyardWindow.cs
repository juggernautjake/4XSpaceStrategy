using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Build ships by spending resources. Each class has its own cost and build time; a loading bar shows
// the ship currently under construction.
public class ShipyardWindow : MonoBehaviour
{
    public static ShipyardWindow Instance;

    GameObject root;
    TMP_Text resourceText, queueLabel;
    RectTransform list;
    Image queueFill;

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("ShipyardWindow");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<ShipyardWindow>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        var content = UIFactory.Window(parent, "Shipyard", new Vector2(500, 580), out root, out _);
        root.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;

        resourceText = UIFactory.Text(content, "", UITheme.SmallSize, UITheme.Accent, TextAlignmentOptions.Left);
        var rrt = resourceText.rectTransform;
        rrt.anchorMin = new Vector2(0, 1); rrt.anchorMax = new Vector2(1, 1); rrt.pivot = new Vector2(0.5f, 1); rrt.sizeDelta = new Vector2(0, 20);

        var holder = UIFactory.NewUI(content, "Holder").GetComponent<RectTransform>();
        UIFactory.Stretch(holder, 0, 0, 26, 40);
        UIFactory.ScrollView(holder, out list);

        // Build-queue bar (bottom).
        var barHolder = UIFactory.NewUI(content, "Queue");
        var brt = barHolder.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0, 0); brt.anchorMax = new Vector2(1, 0); brt.pivot = new Vector2(0.5f, 0); brt.sizeDelta = new Vector2(0, 24);
        var track = UIFactory.Panel(barHolder.transform, "Track", UITheme.TrackBg);
        UIFactory.Stretch(track.rectTransform);
        queueFill = UIFactory.Panel(track.transform, "Fill", UITheme.Accent);
        var frt = queueFill.rectTransform;
        frt.anchorMin = new Vector2(0, 0); frt.anchorMax = new Vector2(0, 1); frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
        queueLabel = UIFactory.Text(barHolder.transform, "", UITheme.SmallSize, UITheme.Text, TextAlignmentOptions.Center);
        UIFactory.Stretch(queueLabel.rectTransform);

        PlayerEconomy.OnChanged += Refresh;
        if (UnitManager.Instance != null) UnitManager.Instance.OnBuildChanged += Refresh;

        root.SetActive(false);
    }

    public void Toggle()
    {
        bool show = !root.activeSelf;
        root.SetActive(show);
        if (show) { Refresh(); root.GetComponent<RectTransform>().SetAsLastSibling(); }
    }

    void Refresh()
    {
        if (root == null || !root.activeSelf) return;
        int level = Colony.PlayerMaxShipyardLevel();
        resourceText.text = PlayerEconomy.Summary() + $"    <color=#9FB4C8>Shipyard Lv {level}/{Colony.MaxShipyardLevel}</color>";

        for (int i = list.childCount - 1; i >= 0; i--) Destroy(list.GetChild(i).gameObject);

        var um = UnitManager.Instance;
        foreach (var info in UnitDatabase.All)
        {
            var card = UIFactory.Panel(list, "Card", UITheme.RowBg);
            var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 6, 6); vlg.spacing = 2; vlg.childControlWidth = true; vlg.childControlHeight = true; vlg.childForceExpandWidth = true;
            var fit = card.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            string tier = info.minShipyardLevel > 1 ? $"  <size=11><color=#C9A94D>[Lv{info.minShipyardLevel} shipyard]</color></size>" : "";
            UIFactory.WrapText(card.transform, $"<b>{info.name}</b>{tier}  <size=11><color=#9FB4C8>{info.costMetal} metal · {info.costEnergy} energy · {info.buildTime:F0}s</color></size>", UITheme.BodySize, new Color(info.iconColor.r, info.iconColor.g, info.iconColor.b));
            UIFactory.WrapText(card.transform, $"HP {info.health}  Armor {info.armor}  Speed {info.speed}  Research {info.research}  Attack {info.attack}", UITheme.SmallSize, UITheme.SubText);
            UIFactory.WrapText(card.transform, info.description, UITheme.SmallSize, UITheme.Text);

            var t = info.type;
            string why = "no shipyard";
            bool can = um != null && um.CanBuildShip(t, out why);
            var btn = UIFactory.Button(card.transform, can ? "Build" : why, () => { UnitManager.Instance?.QueueBuild(t); Refresh(); }, 26);
            btn.interactable = can;
        }
    }

    void Update()
    {
        if (root == null || !root.activeSelf) return;
        var order = UnitManager.Instance != null ? UnitManager.Instance.CurrentBuild : null;
        if (order == null)
        {
            queueFill.rectTransform.anchorMax = new Vector2(0, 1);
            queueLabel.text = "No ships building";
        }
        else
        {
            queueFill.rectTransform.anchorMax = new Vector2(order.Progress, 1);
            int queued = UnitManager.Instance.BuildQueue.Count;
            queueLabel.text = $"Building {UnitDatabase.Get(order.type).name}: {order.Progress * 100f:F0}% ({order.Remaining:F0}s)" + (queued > 1 ? $"  +{queued - 1} queued" : "");
        }
    }

    void OnDestroy()
    {
        PlayerEconomy.OnChanged -= Refresh;
        if (UnitManager.Instance != null) UnitManager.Instance.OnBuildChanged -= Refresh;
    }
}
