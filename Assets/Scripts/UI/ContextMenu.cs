using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// A small right-click popup menu. Shows a list of labelled actions at the cursor and closes when an
// option is chosen or the user clicks elsewhere.
public class ContextMenu : MonoBehaviour
{
    public static ContextMenu Instance;

    public class Option
    {
        public string label; public Action action; public bool enabled;
        public Option(string label, Action action, bool enabled = true) { this.label = label; this.action = action; this.enabled = enabled; }
    }

    GameObject root;
    RectTransform panel;
    RectTransform list;

    public static void Create(Transform canvas)
    {
        if (Instance != null) return;
        var go = new GameObject("ContextMenu");
        go.transform.SetParent(canvas, false);
        Instance = go.AddComponent<ContextMenu>();
        Instance.Build(canvas);
    }

    void Build(Transform canvas)
    {
        var img = UIFactory.Panel(canvas, "ContextMenu", new Color(0.08f, 0.12f, 0.18f, 0.98f));
        root = img.gameObject;
        panel = img.rectTransform;
        panel.pivot = new Vector2(0f, 1f);
        panel.sizeDelta = new Vector2(190, 10);
        var outline = img.gameObject.AddComponent<Outline>();
        outline.effectColor = UITheme.Accent;

        var fitter = img.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        list = img.rectTransform;
        var vlg = img.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(4, 4, 4, 4); vlg.spacing = 3;
        vlg.childControlWidth = true; vlg.childControlHeight = true; vlg.childForceExpandWidth = true;

        root.SetActive(false);
    }

    public void Show(Vector2 screenPos, List<Option> options)
    {
        for (int i = list.childCount - 1; i >= 0; i--) Destroy(list.GetChild(i).gameObject);

        foreach (var o in options)
        {
            var captured = o;
            var btn = UIFactory.Button(list, o.label, () => { Hide(); captured.action?.Invoke(); }, 28);
            btn.interactable = o.enabled;
        }

        panel.position = new Vector3(screenPos.x, screenPos.y, 0);
        root.SetActive(true);
        panel.SetAsLastSibling();
    }

    public void Hide() { if (root != null) root.SetActive(false); }

    void Update()
    {
        if (root == null || !root.activeSelf) return;
        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
        {
            if (!RectTransformUtility.RectangleContainsScreenPoint(panel, Input.mousePosition, null))
                Hide();
        }
    }
}
