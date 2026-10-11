using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The panel model: a fixed category bar, a rack of features on the left, an
// inspector for the selected feature on the right. Everything is created once
// at Init and shown or hidden by bag; VerticalLayoutGroups skip inactive
// children so hidden content leaves no gaps. Category, selection, and each
// feature's advanced state are Ui-scope storables so the panel reopens where
// it was. See docs/UI_DESIGN.md.
sealed class UiShell
{
    public readonly JSONStorableStringChooser Category;
    public readonly JSONStorableString Selected;

    readonly ParamSet set;
    readonly List<UiCategory> categories = new List<UiCategory>();
    readonly List<string> ids = new List<string>();
    readonly List<string> labels = new List<string>();
    readonly List<string> hints = new List<string>();
    readonly List<FeatureView> features = new List<FeatureView>();
    CategoryBar bar;
    UiKit kit;
    UiHost host;
    bool built;
    UnityEngine.Transform lastUi;
    string shownSig;   // fingerprint of what is shown, so Apply re-fits only on a real change

    // A bag that docks into whichever feature's inspector asks for it (the
    // shared brush block). Visible only while its host feature is selected.
    List<GameObject> floatBag;
    FeatureView floatHost;
    SplitPane floatPane;
    bool floatOn;
    // Paint mode: the brush block takes the left column in place of the rack
    // (the tab bar stays) so it cannot be missed; leaving the painting
    // feature ends it.
    public OverlayLog.Fn OnLeavePaint;
    // Runs at the end of every Apply: a panel whose content follows the
    // selection (the tree editor) refills itself here, on the click, not on
    // the next slow tick.
    public OverlayLog.Fn OnApplied;

    public UiShell(ParamSet set)
    {
        this.set = set;
        Category = set.Chooser("shell", ParamSet.Ui | ParamSet.Private,
            "category", new JSONStorableStringChooser("Category", ids, FoliageFXConfig.PageOverview, "Category", OnCategory));
        Selected = set.String("shell", ParamSet.Ui | ParamSet.Private,
            "selected", new JSONStorableString("Selected Feature", "", OnSelected));
    }

    public List<UiCategory> Categories { get { return categories; } }
    public List<FeatureView> Features { get { return features; } }

    // ---- model ----

    public UiCategory AddCategory(string id, string label, string hint)
    {
        UiCategory c = new UiCategory();
        c.Id = id; c.Label = label; c.Hint = hint;
        categories.Add(c);
        ids.Add(id);
        labels.Add(label);
        hints.Add(hint);
        return c;
    }

    public UiCategory CategoryOf(string id)
    {
        for (int i = 0; i < categories.Count; i++)
            if (categories[i].Id == id) return categories[i];
        return null;
    }

    public FeatureView AddFeature(string categoryId, string id, string label, JSONStorableBool master)
    {
        UiCategory c = CategoryOf(categoryId);
        FeatureView v = new FeatureView();
        v.Id = id; v.Category = categoryId; v.Label = label; v.Master = master;
        features.Add(v);
        if (c != null) c.Features.Add(v);
        return v;
    }

    public FeatureView FeatureOf(string id)
    {
        for (int i = 0; i < features.Count; i++)
            if (features[i].Id == id) return features[i];
        return null;
    }

    // ---- building (order matters: bar first, then per category) ----

    public void BuildBar(UiKit kit)
    {
        this.kit = kit;
        host = new UiHost(kit);
        Category.choices = ids;
        Category.displayChoices = labels;
        kit.Split = null;
        bar = kit.CategoryBar(ids, labels, hints, SelectCategory);
    }

    // Opens a category's split pane: everything the kit creates until the next
    // Open goes into this pane's left or right column.
    public void Open(UiCategory c)
    {
        if (c.Split == null) c.Split = host.NewSplit(c.Id);
        kit.Split = c.Split;
    }

    // Left column: category title and the rack in cards, one card per group
    // the category registered (a header starts a new card); without headers
    // a single card titled `groupTitle`. Call after every feature of the
    // category has been added.
    public void BuildRack(UiCategory c, string emptyText, string groupTitle)
    {
        kit.BeginSection(c.Label, false, c.Bag, 32);
        kit.EndSection();
        if (c.Features.Count == 0)
        {
            kit.Caption(emptyText, false, c.EmptyBag);
            return;
        }
        bool open = false;
        for (int i = 0; i < c.Features.Count; i++)
        {
            FeatureView v = c.Features[i];
            for (int h = 0; h < c.HeaderBefore.Count; h++)
                if (c.HeaderBefore[h] == v.Id) { OpenGroup(c, c.HeaderText[h]); open = true; }
            if (!open) { OpenGroup(c, groupTitle); open = true; }
            v.Row = kit.RackRow(v.Id, v.Label, v.Master, v.RowBag, Select);
            c.GroupMembers[c.GroupMembers.Count - 1].Add(v);
        }
        kit.EndSection();
        // A card made only of experimental features says so in its title.
        for (int g = 0; g < c.Groups.Count; g++)
        {
            List<FeatureView> members = c.GroupMembers[g];
            bool all = members.Count > 0;
            for (int m = 0; m < members.Count; m++) if (!FoliageFXConfig.IsExperimental(members[m].Id)) { all = false; break; }
            UiSection s = c.Groups[g];
            if (all && s != null && s.TitleText != null) s.TitleText.text = s.Title + "  ·  Experimental";
        }
    }

    public void Resize()
    {
        Resettle();
    }

    // A visibility or size change needs the wrapped text re-fitted at its real
    // width in the same frame: a box shown this frame has no width yet, so a
    // plain Size would leave it at its char-count estimate until the next slow
    // tick measures it. Settle force-lays-out, fits, and sizes at once; before
    // the panel is laid out it reports false and we fall back to a plain size.
    void Resettle()
    {
        if (host == null) return;
        if (!host.Settle()) host.Size();
    }

    void OpenGroup(UiCategory c, string title)
    {
        UiSection s = kit.BeginSection(title, false, c.Bag);
        c.Groups.Add(s);
        c.GroupMembers.Add(new List<FeatureView>());
    }

    // Right column header of a feature's inspector: title, master toggle, and
    // the live caption, so the status is read without scrolling. `prefix` is
    // the storable-name prefix its controls drop from their labels.
    public void BeginInspector(FeatureView v, string prefix)
    {
        kit.EndSection();
        kit.Prefix = prefix;
        kit.BeginSection(v.Label, true, v.InspectorBag, 32);
        if (v.Master != null) kit.MasterToggle(v.Master, true, v.InspectorBag, "Enabled");
        v.Caption = kit.Caption("", true, v.InspectorBag, 3);
        kit.EndSection();
    }

    // Opens the advanced block: a "Show Advanced" toggle whose state persists,
    // then an Advanced card. Controls created until EndInspector go into that
    // card and show only while the toggle is on.
    public void BeginAdvanced(FeatureView v)
    {
        kit.EndSection();
        v.AdvancedOpen = set.Bool(v.Id, ParamSet.Ui | ParamSet.Private, "advanced", new JSONStorableBool(v.Label + " Advanced", false, AdvancedChanged));
        UIDynamicToggle t = kit.Toggle(v.AdvancedOpen, true, v.InspectorBag);
        if (t != null) OverlayLog.Probe(delegate { t.label = "Show Advanced"; return null; });
        kit.BeginSection("Advanced", true, v.AdvancedBag);
    }

    // Right column footer: the reset button.
    public void EndInspector(FeatureView v)
    {
        kit.EndSection();
        if (v.OnReset != null)
        {
            Transform row = kit.Row(true, FoliageFXConfig.RowH, v.InspectorBag);
            UIDynamicButton reset = kit.Button(row, "Reset to Defaults", FoliageFXConfig.Button, FoliageFXConfig.OnButton);
            FeatureView target = v;
            kit.Wire(reset, "reset " + v.Label, delegate { target.OnReset(); });
            List<string> hint = new List<string>();
            hint.Add("Brings this effect's settings back to their defaults. The other effects and every tree's looks are untouched.");
            kit.HintBox(hint, true, v.InspectorBag);
        }
        kit.Prefix = null;
    }

    public void Finish()
    {
        kit.EndSection();
        kit.Split = null;
        built = true;
        Apply();
    }

    // Move everything into the full-width host and size the panes. Idempotent;
    // called from Init, InitUI, after layout, and on the slow tick.
    public void Attach(Transform uiTransform)
    {
        if (host == null) return;
        if (uiTransform != null) lastUi = uiTransform;
        host.Attach(uiTransform);
        host.Size();
    }

    // Detach a bag from the pane it was built in so it can dock anywhere.
    public void MakeFloating(List<GameObject> bag)
    {
        floatBag = bag;
        for (int i = 0; i < categories.Count; i++)
        {
            SplitPane p = categories[i].Split;
            if (p == null) continue;
            for (int k = 0; k < bag.Count; k++)
            {
                p.LeftItems.Remove(bag[k]);
                p.RightItems.Remove(bag[k]);
            }
        }
        UiKit.SetBag(bag, false);
    }

    // Paint mode: dock the floating brush block at the top of the painting
    // feature's LEFT column, in place of the category title and the rack (both hidden while painting).
    public void Float(string featureId)
    {
        FeatureView v = FeatureOf(featureId);
        if (v == null || floatBag == null) { Unfloat(); return; }
        UiCategory c = CategoryOf(v.Category);
        if (c == null || c.Split == null) { Unfloat(); return; }
        if (floatPane != null)
            for (int k = 0; k < floatBag.Count; k++) { floatPane.LeftItems.Remove(floatBag[k]); floatPane.RightItems.Remove(floatBag[k]); }
        floatPane = c.Split;
        int at = 0;
        for (int k = 0; k < floatBag.Count; k++)
        {
            GameObject g = floatBag[k];
            if (kit.InSection(g) || floatPane.LeftItems.Contains(g)) continue;
            floatPane.LeftItems.Insert(at, g);
            at++;
        }
        floatHost = v;
        floatOn = true;
        if (Selected.val != v.Id) Selected.valNoCallback = v.Id;
        Attach(lastUi);
        Apply();
    }

    public bool Painting { get { return floatOn && floatHost != null; } }

    public void Unfloat()
    {
        floatOn = false;
        if (floatBag != null) UiKit.SetBag(floatBag, false);
        if (host != null) host.Size();
    }

    // ---- state ----

    public string CurrentCategory()
    {
        string v = Category.val;
        for (int i = 0; i < ids.Count; i++)
            if (ids[i] == v) return v;
        return ids.Count > 0 ? ids[0] : FoliageFXConfig.PageOverview;
    }

    public bool IsCategory(string id)
    {
        return CurrentCategory() == id;
    }

    public FeatureView CurrentFeature()
    {
        FeatureView v = FeatureOf(Selected.val);
        if (v != null && v.Category == CurrentCategory()) return v;
        return null;
    }

    public void SelectCategory(string id)
    {
        Category.val = id;
    }

    // Selecting the feature that is already selected still brings it up: a
    // dashboard row on Overview must open its tab even when nothing changed.
    public void Select(string featureId)
    {
        if (Selected.val == featureId)
        {
            if (built) OverlayLog.SafeRun(kit != null ? kit.Log : null, "select feature", delegate { ApplySelected(featureId); });
            return;
        }
        Selected.val = featureId;
    }

    void OnCategory(string value)
    {
        if (!built) return;
        OverlayLog.SafeRun(kit != null ? kit.Log : null, "switch category", delegate { ApplyCategory(); });
    }

    void ApplyCategory()
    {
        // Return to the feature that was open in this category, else its first.
        UiCategory c = CategoryOf(CurrentCategory());
        if (c != null && c.Features.Count > 0)
        {
            FeatureView keep = FeatureOf(c.LastSelected);
            string want = keep != null && keep.Category == c.Id ? keep.Id : c.Features[0].Id;
            if (Selected.val != want) { Selected.val = want; return; } // OnSelected applies
        }
        Apply();
    }

    void OnSelected(string value)
    {
        if (!built) return;
        OverlayLog.SafeRun(kit != null ? kit.Log : null, "select feature", delegate { ApplySelected(value); });
    }

    void ApplySelected(string value)
    {
        FeatureView v = FeatureOf(value);
        if (v != null)
        {
            UiCategory c = CategoryOf(v.Category);
            if (c != null) c.LastSelected = v.Id;
            if (v.Category != CurrentCategory()) { Category.val = v.Category; return; } // OnCategory applies
        }
        Apply();
    }

    void AdvancedChanged(bool value)
    {
        if (built) OverlayLog.SafeRun(kit != null ? kit.Log : null, "toggle advanced", Apply);
    }

    // Withhold or reveal a feature's row and inspector. A hidden selection
    // falls back to the category's first visible feature.
    public void SetHidden(string featureId, bool hidden)
    {
        FeatureView v = FeatureOf(featureId);
        if (v == null || v.Hidden == hidden) return;
        v.Hidden = hidden;
        if (built) OverlayLog.SafeRun(kit != null ? kit.Log : null, "hide feature", Apply);
    }

    // Hide or reveal every feature the Experimental switch governs, and
    // switch a hidden one off so nothing hidden keeps running.
    public void SetExperimental(bool on)
    {
        for (int i = 0; i < features.Count; i++)
        {
            FeatureView v = features[i];
            if (!FoliageFXConfig.IsExperimental(v.Id)) continue;
            SetHidden(v.Id, !on);
            if (!on && v.Master != null && v.Master.val) v.Master.val = false;
        }
    }

    static FeatureView FirstVisible(UiCategory c)
    {
        for (int i = 0; i < c.Features.Count; i++) if (!c.Features[i].Hidden) return c.Features[i];
        return null;
    }

    public void Apply()
    {
        if (kit != null) kit.HideTooltipIfStale();   // a tooltip never outlives the row it hovered
        string cat = CurrentCategory();
        FeatureView selected = CurrentFeature();
        if (selected != null && selected.Hidden) selected = null;
        if (selected == null)
        {
            UiCategory c = CategoryOf(cat);
            if (c != null && c.Features.Count > 0)
            {
                FeatureView keep = FeatureOf(c.LastSelected);
                selected = keep != null && keep.Category == c.Id && !keep.Hidden ? keep : FirstVisible(c);
                if (selected != null)
                {
                    Selected.valNoCallback = selected.Id;
                    c.LastSelected = selected.Id;
                }
            }
        }
        // Paint mode holds while the painting feature is the selected one; any
        // other selection or category ends it (the brush goes with it).
        bool painting = floatOn && floatHost != null && selected != null && selected.Id == floatHost.Id && floatHost.Category == cat;
        if (floatOn && floatHost != null && !painting && OnLeavePaint != null)
        {
            floatOn = false;
            OverlayLog.SafeRun(kit != null ? kit.Log : null, "leave paint mode", OnLeavePaint);
        }
        for (int i = 0; i < categories.Count; i++)
        {
            UiCategory c = categories[i];
            bool on = c.Id == cat;
            if (c.Split != null) c.Split.SetActive(on);
            // While painting, the rack (the category title and group cards)
            // gives the column to the brush; the tab bar stays.
            UiKit.SetBag(c.Bag, on && !painting);
            UiKit.SetBag(c.EmptyBag, on && c.Features.Count == 0 && !painting);
        }
        for (int i = 0; i < features.Count; i++)
        {
            FeatureView v = features[i];
            bool inCat = v.Category == cat && !v.Hidden;
            bool isSel = inCat && selected != null && v.Id == selected.Id;
            UiKit.SetBag(v.RowBag, inCat && !painting);
            UiKit.SetBag(v.InspectorBag, isSel);
            UiKit.SetBag(v.AdvancedBag, isSel && v.AdvancedOpen != null && v.AdvancedOpen.val);
            v.ApplyConditions(isSel);
            if (v.Row != null) kit.PaintRow(v.Row, isSel, FoliageFXConfig.IsExperimental(v.Id));
        }
        // A rack card with every row hidden (the Developer group without
        // Experimental) goes with them.
        UiCategory cur = CategoryOf(cat);
        if (cur != null)
            for (int g = 0; g < cur.Groups.Count; g++)
            {
                UiSection s = cur.Groups[g];
                if (s == null || s.Root == null) continue;
                bool any = false;
                List<FeatureView> members = cur.GroupMembers[g];
                for (int m = 0; m < members.Count; m++) if (!members[m].Hidden) { any = true; break; }
                bool show = any && !painting;
                if (s.Root.activeSelf != show) s.Root.SetActive(show);
            }
        if (floatBag != null) UiKit.SetBag(floatBag, painting);
        // The bags re-show every hint line; with Show Hints off they go back
        // out of sight BEFORE the cards are sized, or a card would be sized
        // with the hidden lines in it and keep their empty room.
        if (kit != null) kit.ApplyHints(FoliageFXConfig.ShowHints);
        if (bar != null) bar.Paint(cat);
        if (host != null) host.Size();
        Refresh();
        // When what is shown changes (a new panel, a selection, Advanced,
        // Experimental, or Technical Info), lay out and fit the wrapped text in
        // the same frame so a box never flashes at its estimated height before
        // the slow tick can measure it. Gated on a real change, never per tick.
        string sig = ShownSignature(cat, selected, painting);
        if (host != null && sig != shownSig && host.Settle()) shownSig = sig;
        // A colour picker whose colour was set while its card was hidden (or
        // whose box has changed size since) has its cursor off the colour or
        // outside the box; put it back now that the card is shown and laid out.
        if (kit != null) kit.RefreshColorPickers();
        if (OnApplied != null) OverlayLog.SafeRun(kit != null ? kit.Log : null, "after apply", OnApplied);
    }

    // A cheap fingerprint of what the panel shows, so Apply tells a real
    // change (which needs a synchronous re-fit) from an idempotent re-assert:
    // the category, the open feature and its Advanced state, Technical Info
    // (it lengthens captions), paint mode, and which features are hidden
    // (Experimental).
    string ShownSignature(string cat, FeatureView selected, bool painting)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append(cat).Append('|');
        sb.Append(selected != null ? selected.Id : "-").Append('|');
        sb.Append(selected != null && selected.AdvancedOpen != null && selected.AdvancedOpen.val ? '1' : '0');
        sb.Append(FoliageFXConfig.TechnicalInfo ? 'T' : 't');
        sb.Append(FoliageFXConfig.ShowHints ? 'H' : 'h');
        sb.Append(painting ? 'P' : 'p').Append('|');
        for (int i = 0; i < features.Count; i++) sb.Append(features[i].Hidden ? '0' : '1');
        return sb.ToString();
    }

    // Slow tick: live text for visible rows and the selected caption only,
    // and the conditional groups of the selected inspector.
    public void Refresh()
    {
        string cat = CurrentCategory();
        FeatureView selected = CurrentFeature();
        bool resize = false;
        for (int i = 0; i < features.Count; i++)
        {
            FeatureView v = features[i];
            if (v.Category != cat || v.Hidden) continue;
            v.RefreshRow();
            if (selected != null && v.Id == selected.Id)
            {
                v.RefreshCaption();
                if (v.ApplyConditions(true)) resize = true;
            }
        }
        // A condition that came on re-showed every line in its bag, the hint
        // lines among them; with Show Hints off they go back out of sight.
        if (kit != null) kit.ApplyHints(FoliageFXConfig.ShowHints);
        // Influence chips follow their features' state and the Experimental switch.
        if (kit != null)
        {
            kit.RefreshInfluences(delegate (string id) { FeatureView f = FeatureOf(id); return f != null && f.Hidden; });
            kit.RefreshLiveLines();
            kit.RefreshColorPickers();   // every shown picker's cursor back on its colour
        }
        // Live text may have wrapped further than its pad; fit every visible
        // label to what it renders at, then re-take the card and column sizes.
        if (kit != null && kit.FitTexts()) resize = true;
        if (resize) Resettle();
    }
}
