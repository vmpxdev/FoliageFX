using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Two columns we own: a spacer carrying a HorizontalLayoutGroup with a left and
// a right VerticalLayoutGroup. Elements are created through VaM's stock
// Create* calls (so they are real VaM controls) and then parented into these
// columns. The pane's height is computed from its visible children because a
// spacer has no intrinsic size. Same mechanism as Live Decals' split pads.
sealed class SplitPane
{
    public string Name;
    public UIDynamic Pad;
    public Transform Left;
    public Transform Right;
    public readonly List<GameObject> LeftItems = new List<GameObject>();
    public readonly List<GameObject> RightItems = new List<GameObject>();

    public GameObject Root { get { return Pad != null ? Pad.gameObject : null; } }

    public void SetActive(bool on)
    {
        GameObject go = Root;
        if (go != null && go.activeSelf != on) go.SetActive(on);
    }

    // Parent every item into its column (idempotent).
    public void Layout()
    {
        UiHost.ParentItems(LeftItems, Left);
        UiHost.ParentItems(RightItems, Right);
    }

    // Height = the taller visible column plus the group's vertical padding.
    public void Size()
    {
        if (Pad == null || Pad.gameObject == null) return;
        float extra = 0f;
        float gap = 6f;
        HorizontalLayoutGroup h = Pad.gameObject.GetComponent<HorizontalLayoutGroup>();
        if (h != null) extra = h.padding.top + h.padding.bottom;
        VerticalLayoutGroup v = Left != null ? Left.GetComponent<VerticalLayoutGroup>() : null;
        if (v != null) gap = v.spacing;
        float hgt = UiHost.ColumnHeight(LeftItems, gap);
        float rightH = UiHost.ColumnHeight(RightItems, gap);
        if (rightH > hgt) hgt = rightH;
        if (hgt < 8f) hgt = 8f;
        hgt += extra;
        LayoutElement le = Pad.gameObject.GetComponent<LayoutElement>();
        if (le != null && Mathf.Abs(Pad.height - hgt) < 1f && Mathf.Abs(le.minHeight - hgt) < 1f) return;
        Pad.height = hgt;
        if (le == null) le = Pad.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.minHeight = hgt;
        le.preferredHeight = hgt;
        le.flexibleHeight = 0f;
    }
}

// The full-width layout engine, lifted from Live Decals' AttachFullWidth.
// VaM's plugin panel exposes a full-width area above its two stock columns
// (MVRScriptUI.fullWidthUIContent). We put one stretching vertical host there
// and move every top-level element (the category bar, one split pane per
// category) into it, in creation order. The stock columns end up empty and the
// panel's margins become ours. Attach is idempotent and re-run whenever VaM
// rebuilds the panel.
sealed class UiHost
{
    readonly UiKit kit;
    readonly List<SplitPane> splits = new List<SplitPane>();
    Transform host;

    public UiHost(UiKit kit)
    {
        this.kit = kit;
    }

    public bool Attached { get { return host != null; } }

    public SplitPane NewSplit(string name)
    {
        SplitPane s = new SplitPane();
        s.Name = name;
        s.Pad = kit.Pad(false, 200f);
        if (s.Pad == null) return s;
        kit.TrackHostItem(s.Pad);
        GameObject go = s.Pad.gameObject;
        HorizontalLayoutGroup h = go.GetComponent<HorizontalLayoutGroup>();
        if (h == null) h = go.AddComponent<HorizontalLayoutGroup>();
        h.spacing = FoliageFXConfig.Gutter;
        h.padding = new RectOffset(0, 0, 0, 0);
        h.childAlignment = TextAnchor.UpperLeft;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = true;
        h.childForceExpandHeight = false;
        s.Left = MakeColumn(go.transform, name + "Left");
        s.Right = MakeColumn(go.transform, name + "Right");
        splits.Add(s);
        return s;
    }

    static Transform MakeColumn(Transform parent, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        LayoutElement le = go.AddComponent<LayoutElement>();
        le.minWidth = 0f;
        le.preferredWidth = 0f;
        le.flexibleWidth = 1f;
        VerticalLayoutGroup v = go.AddComponent<VerticalLayoutGroup>();
        v.spacing = 6f;
        v.padding = new RectOffset(0, 0, 0, 0);
        v.childAlignment = TextAnchor.UpperCenter;
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        return go.transform;
    }

    // Move host-level items into the full-width host and every column item
    // into its column. Safe without a UI transform (columns still lay out);
    // the host step waits until VaM has built the panel.
    // The panel's own scroll view (VaM's, around the plugin's content): its
    // vertical scrollbar always shows, so every tab is the same width. Left on
    // VaM's auto-hide, a tab too short to scroll hid the bar and widened the
    // viewport into its place, and every element resettled on each tab switch.
    // Permanent stops the scroll view sizing the viewport, so it is given the
    // width it has with the bar shown, as Unity sizes it then. The window is the
    // plugin's own, destroyed with it, so nothing is put back.
    // Re-applied on every attach (VaM rebuilds the window), and the viewport is
    // sized only once the bar has a width, so an attach before VaM's first
    // layout is caught up by the next.
    bool pinExpand;   // the scroll view was VaM's auto-hide-and-expand: its viewport is ours to size
    void PinScrollbar(Transform content, Transform bound)
    {
        ScrollRect sr = null;
        for (Transform t = content; t != null; t = t.parent)
        {
            sr = t.GetComponent<ScrollRect>();
            if (sr != null || t == bound) break;
        }
        if (sr == null || sr.verticalScrollbar == null) return;
        if (sr.verticalScrollbarVisibility == ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport) pinExpand = true;
        if (sr.verticalScrollbarVisibility != ScrollRect.ScrollbarVisibility.Permanent)
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        RectTransform view = sr.viewport;
        RectTransform bar = sr.verticalScrollbar.transform as RectTransform;
        if (pinExpand && view != null && view != sr.transform && bar != null && bar.rect.width > 0f)
            view.sizeDelta = new Vector2(-(bar.rect.width + sr.verticalScrollbarSpacing), view.sizeDelta.y);
        if (!sr.verticalScrollbar.gameObject.activeSelf) sr.verticalScrollbar.gameObject.SetActive(true);
    }

    public void Attach(Transform uiTransform)
    {
        for (int i = 0; i < kit.Sections.Count; i++) kit.Sections[i].Layout();
        for (int i = 0; i < splits.Count; i++) splits[i].Layout();
        if (uiTransform == null) return;
        MVRScriptUI scriptUI = uiTransform.GetComponentInChildren<MVRScriptUI>(true);
        if (scriptUI == null || scriptUI.fullWidthUIContent == null) return;

        kit.TooltipRoot = scriptUI.fullWidthUIContent;
        PinScrollbar(scriptUI.fullWidthUIContent, scriptUI.transform);
        if (host == null)
        {
            GameObject go = new GameObject("FoliageFXHost");
            go.transform.SetParent(scriptUI.fullWidthUIContent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            Stretch(rt);
            VerticalLayoutGroup v = go.AddComponent<VerticalLayoutGroup>();
            v.spacing = FoliageFXConfig.HostSpacing;
            v.padding = new RectOffset(FoliageFXConfig.HostPadX, FoliageFXConfig.HostPadX, FoliageFXConfig.HostPadTop, FoliageFXConfig.HostPadBottom);
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            ContentSizeFitter fit = go.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            LayoutElement hle = go.AddComponent<LayoutElement>();
            hle.flexibleWidth = 1f;
            hle.minWidth = 0f;
            host = go.transform;
        }
        else if (host.parent != scriptUI.fullWidthUIContent)
        {
            host.SetParent(scriptUI.fullWidthUIContent, false);
        }
        Stretch(host as RectTransform);
        host.SetAsFirstSibling();

        List<GameObject> items = kit.HostItems;
        for (int i = 0; i < items.Count; i++)
        {
            GameObject g = items[i];
            if (g == null) continue;
            if (g.transform.parent != host) g.transform.SetParent(host, false);
            LayoutElement le = g.GetComponent<LayoutElement>();
            if (le == null) le = g.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.flexibleHeight = 0f;
            UIDynamic dyn = g.GetComponent<UIDynamic>();
            if (dyn != null && dyn.height > 1f)
            {
                le.minHeight = dyn.height;
                le.preferredHeight = dyn.height;
            }
        }
    }

    // Recompute every card's height from its visible children, then every
    // split's (a card is one item of a column).
    public void Size()
    {
        for (int i = 0; i < kit.Sections.Count; i++) kit.Sections[i].Size();
        for (int i = 0; i < splits.Count; i++) splits[i].Size();
    }

    // Lay out, fit the wrapped text to the width it renders at, size the cards
    // to that text, and lay out again -- all synchronously, so a panel shown
    // or changed this frame is correct at once instead of flashing at its
    // char-count estimate until the next slow tick measures it. Unity only
    // assigns widths at end of frame, so FitTexts on the show tick would
    // otherwise read a zero width and keep the estimate. Returns false before
    // the host is attached. The shell calls this only when what is shown
    // changed, never every tick.
    public bool Settle()
    {
        RectTransform rt = host as RectTransform;
        if (rt == null || !rt.gameObject.activeInHierarchy) return false;
        LayoutRebuilder.ForceRebuildLayoutImmediate(rt);   // assign the real widths
        // Not laid out yet (the panel is still being built, so VaM has given
        // the host no width): the text cannot be measured, so report no settle
        // and leave the caller's change unclaimed. InitUI's end-of-frame
        // retries then settle it once the width exists, before it is seen.
        if (rt.rect.width < 1f) return false;
        if (kit != null) kit.FitTexts();                   // measure the text at those widths
        Size();                                            // size the cards to the fitted text
        LayoutRebuilder.ForceRebuildLayoutImmediate(rt);   // apply the fitted heights now
        return true;
    }

    static void Stretch(RectTransform rt)
    {
        if (rt == null) return;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, rt.sizeDelta.y);
    }

    public static void ParentItems(List<GameObject> items, Transform column)
    {
        if (column == null) return;
        for (int i = 0; i < items.Count; i++)
        {
            GameObject g = items[i];
            if (g == null) continue;
            if (g.transform.parent != column) g.transform.SetParent(column, false);
            // List order is display order: a docked block is inserted mid-list
            // and must land there, not at the end of the children.
            if (g.transform.GetSiblingIndex() != i) g.transform.SetSiblingIndex(i);
            LayoutElement le = g.GetComponent<LayoutElement>();
            if (le == null) le = g.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.minWidth = 0f;
            le.preferredWidth = 0f;
            le.flexibleHeight = 0f;
            float h = WidgetHeight(g);
            if (h > 1f)
            {
                le.minHeight = h;
                le.preferredHeight = h;
            }
        }
    }

    public static float WidgetHeight(GameObject g)
    {
        if (g == null) return 0f;
        float h = 0f;
        UIDynamic dyn = g.GetComponent<UIDynamic>();
        if (dyn != null && dyn.height > 1f) h = dyn.height;
        LayoutElement le = g.GetComponent<LayoutElement>();
        if (le != null)
        {
            if (le.preferredHeight > h) h = le.preferredHeight;
            if (le.minHeight > h) h = le.minHeight;
        }
        return h;
    }

    public static float ColumnHeight(List<GameObject> items, float gap)
    {
        float h = 0f;
        int n = 0;
        for (int i = 0; i < items.Count; i++)
        {
            GameObject g = items[i];
            if (g == null || !g.activeSelf) continue;
            float ch = WidgetHeight(g);
            if (ch <= 1f) continue;
            if (n > 0) h += gap;
            h += ch;
            n++;
        }
        return h;
    }
}
