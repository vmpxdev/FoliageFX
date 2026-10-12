using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Trees: the scene's CustomUnityAssets and one shared editor. The rack is
// live: a tree the scan finds gets a row (a FeatureView from the shell, keyed
// by its atom's uid) the first time it is seen, a tree that leaves has its row
// hidden, and one that comes back finds its row again; rows are never
// destroyed. Each row's switch is a proxy storable, never registered, that
// writes the tree's switch into TreeLooks and has the tree applied again.
//
// Under the rack, the selected tree's materials, one row each with what was
// done to it; picking one opens it in the editor. The rows are a pool made as
// a tree needs them and reused for the next tree, so a tree with many
// materials is read at a glance instead of through a popup.
//
// The editor is built once and shows whichever material is picked: its
// controls are Volatile mirrors of that material's look. Picking a tree or a
// material pushes the look into them without callbacks; a change in any of
// them writes all of them back into that look, marks it customised and
// touches the tree. Nothing here converts anything: the next Update does.
//
// The editor lives in this panel's own bags, not in a feature's inspector,
// because one editor serves every tree row; the shell never shows or hides
// it, so Sync does, after every shell Apply (OnApplied) and on the slow tick.
sealed class TreesPanel
{
    const string Guide = "Leaves that never receive shadows are converted. Pick a material on the left to check or change it.";
    const string OwnInstance = "This atom has its own FoliageFX plugin instance, which takes precedence.";
    const string Empty = "No CustomUnityAsset atoms in the scene. Add a tree or plant asset and it appears here.";
    const string TintOnly = "only Tint applies. Convert it for wind, falling leaves and the rest of the look.";

    // One rack row per atom uid, made once and hidden while its atom is gone.
    sealed class Row
    {
        public string Uid;
        public FeatureView View;
        public JSONStorableBool Proxy;
        public Tree Tree;               // null while the atom is not in the scene
        public bool Seen;
        public string LastState;
        public string LastMaterial;     // the material last picked on this tree
    }

    // One row of the materials list; the material it shows changes with the tree.
    sealed class MaterialRow
    {
        public RackRow Row;
        public LeafMaterial Material;
        public string LastState;
    }

    // The dashboard makes its own row for each of these (OverviewPanel.SyncTrees).
    public readonly List<FeatureView> Views = new List<FeatureView>();
    public OverlayLog.Fn OnRowsAdded;

    readonly List<Row> rows = new List<Row>();
    readonly Dictionary<string, Row> byUid = new Dictionary<string, Row>();
    readonly List<MaterialRow> materialRows = new List<MaterialRow>();

    // The editor's controls.
    JSONStorableStringChooser mode;
    JSONStorableColor tint;
    JSONStorableFloat hue;
    JSONStorableFloat saturation;
    JSONStorableFloat brightness;
    JSONStorableFloat gloss;
    JSONStorableFloat cutoff;
    JSONStorableFloat translucency;
    JSONStorableBool doubleSided;
    JSONStorableBool dropLeaves;
    // What the editor's Enabled is bound to until a tree is selected; after
    // that it is bound to the selected tree's own proxy, the same storable as
    // the row's switch, so the two can never disagree.
    JSONStorableBool placeholder;
    JSONStorableBool boundProxy;

    UiKit kit;
    UiShell shell;
    Lifecycle life;
    MVRScript script;
    OverlayLog log;
    UiCategory cat;
    UiSection rack;
    UiSection list;
    UiSection header;
    UiSection materialCard;
    UIDynamicToggle enabledToggle;
    UIDynamicToggle dropToggle;
    ChoiceStrip modeStrip;
    Text caption;
    // Disjoint bags, so each part shows on its own terms: the header and its
    // caption with any tree; the switch and the footer only for a tree this
    // instance looks after; the materials list and the material card when the
    // tree has materials; the mode and the tint when the picked material has
    // a look to edit; the rest of the look and the Leaves card
    // only while the material is meant to be converted, the one thing they
    // work on.
    readonly List<GameObject> editorBag = new List<GameObject>();
    readonly List<GameObject> switchBag = new List<GameObject>();
    readonly List<GameObject> listBag = new List<GameObject>();
    readonly List<GameObject> materialBag = new List<GameObject>();
    readonly List<GameObject> lookBag = new List<GameObject>();
    readonly List<GameObject> leafBag = new List<GameObject>();
    readonly List<GameObject> footBag = new List<GameObject>();
    readonly List<GameObject> emptyBag = new List<GameObject>();
    // The hint lines among them: Show Hints keeps them out of sight.
    readonly HashSet<GameObject> hintRoots = new HashSet<GameObject>();

    Row selRow;
    LeafMaterial selMaterial;
    LeafLook pushedLook;
    string capState;
    string capAsset;
    Tree capTree;
    bool capYielded;
    LeafMaterial lineMaterial;
    string lineText = "";
    LeafMaterial linesFor;
    int visibleRows;
    bool syncing;
    bool pushing;
    string selectAfter;   // a renamed atom's new row, to select once it exists
    readonly OverlayLog.Fn fnSync;
    readonly OverlayLog.Fn fnPull;

    public TreesPanel()
    {
        fnSync = SyncNow;
        fnPull = Pull;
    }

    // Volatile and Private: they mirror the selected look, which the scene's
    // trees node saves, and they are no setting Timeline should list.
    public void Create(ParamSet set)
    {
        const int scope = ParamSet.Volatile | ParamSet.Private;
        string id = FoliageFXConfig.TreeId;
        mode = set.Chooser(id, scope, "mode", new JSONStorableStringChooser("Tree Mode", ModeIds(), ModeLabels(), FoliageFXConfig.ModeAuto, "Mode", ModeChanged));
        HSVColor white = new HSVColor();
        white.H = 0f; white.S = 0f; white.V = 1f;
        tint = set.Color(id, scope, "tint", new JSONStorableColor("Tree Tint", white, TintChanged));
        // The ranges are the leaf shader's own, the ones LeafLook clamps a
        // saved look to.
        hue = set.Float(id, scope, "hue_shift_deg", new JSONStorableFloat("Tree Hue Shift deg", 0f, FloatChanged, -180f, 180f, true));
        saturation = set.Float(id, scope, "saturation", new JSONStorableFloat("Tree Saturation", 1f, FloatChanged, 0f, 2f, true));
        brightness = set.Float(id, scope, "brightness", new JSONStorableFloat("Tree Brightness", 1f, FloatChanged, 0f, 2f, true));
        // Gloss is the look's smoothness, the Standard shader's _Glossiness.
        gloss = set.Float(id, scope, "gloss", new JSONStorableFloat("Tree Gloss", 0.2f, FloatChanged, 0f, 1f, true));
        cutoff = set.Float(id, scope, "alpha_cutoff", new JSONStorableFloat("Tree Alpha Cutoff", 0.5f, FloatChanged, 0.05f, 0.95f, true));
        translucency = set.Float(id, scope, "translucency", new JSONStorableFloat("Tree Translucency", 0f, FloatChanged, 0f, 2f, true));
        doubleSided = set.Bool(id, scope, "double_sided", new JSONStorableBool("Tree Double-sided", true, BoolChanged));
        dropLeaves = set.Bool(id, scope, "drop_falling_leaves", new JSONStorableBool("Tree Drop Falling Leaves", true, BoolChanged));
    }

    static List<string> ModeIds()
    {
        List<string> ids = new List<string>();
        ids.Add(FoliageFXConfig.ModeAuto);
        ids.Add(FoliageFXConfig.ModeConvert);
        ids.Add(FoliageFXConfig.ModeKeep);
        return ids;
    }

    static List<string> ModeLabels()
    {
        List<string> labels = new List<string>();
        labels.Add("Auto");
        labels.Add("Convert");
        labels.Add("Keep Original");
        return labels;
    }

    // Left: the page title, the rack card, empty until the scan finds a tree,
    // and the picked tree's materials. Right: the editor. Call with the Trees
    // pane open.
    public void Build(UiKit kit, UiShell shell, UiCategory cat, Lifecycle life, MVRScript script, OverlayLog log)
    {
        this.kit = kit;
        this.shell = shell;
        this.cat = cat;
        this.life = life;
        this.script = script;
        this.log = log;

        kit.BeginSection(cat.Label, false, cat.Bag, 32);
        kit.EndSection();
        // The rack card is one of the category's groups, so the shell hides it
        // while every row in it is hidden, as it does for a fixed rack.
        // Titled for what its rows are, so it does not repeat the page title.
        rack = kit.BeginSection("Atoms", false, cat.Bag);
        kit.EndSection();
        cat.Groups.Add(rack);
        cat.GroupMembers.Add(new List<FeatureView>());
        // The shell shows a category's empty caption only while it has no
        // features at all, and the pool keeps the rows of trees that left, so
        // this one is the panel's own.
        kit.Caption(Empty, false, emptyBag);
        // Its rows arrive with the first tree that has materials (SyncList).
        list = kit.BeginSection("Materials", false, listBag);
        kit.EndSection();

        kit.Prefix = "Tree";
        header = kit.BeginSection("Tree", true, editorBag, 32);
        placeholder = new JSONStorableBool("Fix", true);
        enabledToggle = kit.MasterToggle(placeholder, true, switchBag, "Enabled");
        boundProxy = placeholder;
        caption = kit.Caption("", true, editorBag, 3);
        kit.EndSection();

        // Titled with the picked material's name (SyncEditor).
        materialCard = kit.BeginSection("Material", true, materialBag);
        kit.LiveLine(ShaderLine, true, materialBag, 2);
        kit.SubHeader("Mode", true, lookBag);
        modeStrip = new ChoiceStrip(kit, kit.Row(true, FoliageFXConfig.RowH, lookBag), mode, ModeIds(), ModeLabels());
        modeStrip.OnPick = AfterModePick;
        kit.LiveLine(ModeLine, true, lookBag, 2);
        Hint("Auto converts the leaves that never receive shadows. Convert forces a material Auto missed; Keep Original leaves it as the asset made it.", lookBag);

        kit.BeginSection("Look", true, lookBag);
        kit.ColorPicker(tint, true, lookBag);
        Hint("Colours the leaves: white keeps the asset's own colour.", lookBag);
        kit.Slider(hue, true, leafBag);
        Hint("Turns the leaf colours around the colour wheel: lower turns green to yellow and red.", leafBag);
        kit.Slider(saturation, true, leafBag);
        Hint("How vivid the colours are: 0 is grey, higher is richer.", leafBag);
        kit.Slider(brightness, true, leafBag);
        Hint("Darkens or lightens the leaves; 1 keeps the asset's own.", leafBag);
        kit.Slider(gloss, true, leafBag);
        Hint("How shiny the leaves are: higher gives sharper highlights.", leafBag);

        kit.BeginSection("Leaves", true, leafBag);
        kit.Slider(cutoff, true, leafBag);
        Hint("How much of the leaf texture counts as leaf: lower keeps thin edges, higher trims them.", leafBag);
        kit.Toggle(doubleSided, true, leafBag);
        kit.Slider(translucency, true, leafBag);
        Hint("How much light shows through the leaves from behind.", leafBag);
        dropToggle = kit.Toggle(dropLeaves, true, leafBag);
        Hint("This material's leaves fall when Falling Leaves is on.", leafBag);
        kit.LiveLine(FallingLine, true, leafBag, 1);
        kit.EndSection();

        Transform foot = kit.Row(true, FoliageFXConfig.RowH, footBag);
        UIDynamicButton defaults = kit.Button(foot, "Reset to Defaults", FoliageFXConfig.Button, FoliageFXConfig.OnButton);
        kit.Wire(defaults, "reset the tree", ResetTree);
        Hint("Every material of this tree back to its defaults, and the tree switched back on.", footBag);
        kit.Prefix = null;

        Show(editorBag, false);
        Show(switchBag, false);
        Show(listBag, false);
        Show(materialBag, false);
        Show(lookBag, false);
        Show(leafBag, false);
        Show(footBag, false);
        Show(emptyBag, true);
    }

    // A hint line under the control above it, remembered so Show keeps it
    // hidden while Show Hints is off.
    void Hint(string text, List<GameObject> bag)
    {
        Text tx = kit.Hint(text, true, bag);
        if (tx == null) return;
        hintRoots.Add(tx.transform.parent != null ? tx.transform.parent.gameObject : tx.gameObject);
    }

    // A renamed atom gets a new row (a row's id is fixed); if the old one was
    // open, the new one opens once Sync has made it.
    public void Renamed(string oldUid, string newUid)
    {
        if (shell == null) return;
        if (shell.Selected.val == FoliageFXConfig.TreeRowPrefix + oldUid) selectAfter = FoliageFXConfig.TreeRowPrefix + newUid;
    }

    // ---- sync ----

    // The slow tick, and the end of every shell Apply (a click). Re-entrant
    // calls (the Apply this makes after adding a row) return at once.
    public void Sync(Lifecycle life)
    {
        if (syncing || kit == null || life == null) return;
        this.life = life;
        syncing = true;
        log.RunOnce("refresh the Trees page", fnSync);
        syncing = false;
    }

    void SyncNow()
    {
        SyncRows();
        SyncEditor();
    }

    void SyncRows()
    {
        List<Tree> trees = life.Trees.Trees;
        for (int i = 0; i < rows.Count; i++) rows[i].Seen = false;
        bool added = false;
        for (int i = 0; i < trees.Count; i++)
        {
            Tree t = trees[i];
            if (t == null || string.IsNullOrEmpty(t.Uid)) continue;
            Row r;
            if (!byUid.TryGetValue(t.Uid, out r))
            {
                r = MakeRow(t.Uid);
                added = true;
            }
            r.Seen = true;
            r.Tree = t;
        }
        // Hidden and shown in one pass and applied once: one Apply per row
        // would let the shell's fallback take the selection to the first
        // visible row before the selected one is shown again.
        bool hiddenChanged = false;
        // Row text is built only where a row is seen, the rack and the dashboard.
        bool shown = shell.IsCategory(FoliageFXConfig.PageTrees) || shell.IsCategory(FoliageFXConfig.PageOverview);
        visibleRows = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            Row r = rows[i];
            if (!r.Seen) r.Tree = null;
            bool hide = r.Tree == null;
            if (r.View.Hidden != hide)
            {
                r.View.Hidden = hide;
                hiddenChanged = true;
            }
            if (hide) continue;
            visibleRows++;
            if (shown) SyncRow(r);
        }
        if (added)
        {
            if (OnRowsAdded != null) OnRowsAdded();
            shell.Attach(script.UITransform);
        }
        if (added || hiddenChanged) shell.Apply();
        if (selectAfter != null)
        {
            FeatureView v = shell.FeatureOf(selectAfter);
            if (v != null && !v.Hidden)
            {
                string id = selectAfter;
                selectAfter = null;
                shell.Select(id);
            }
        }
    }

    Row MakeRow(string uid)
    {
        Row r = new Row();
        r.Uid = uid;
        r.Proxy = new JSONStorableBool("Fix " + uid, true, delegate (bool on) { Switch(r, on); });
        r.View = shell.AddFeature(FoliageFXConfig.PageTrees, FoliageFXConfig.TreeRowPrefix + uid, uid, r.Proxy);
        // The shell refreshes a row from its state; without this it would
        // print the switch's "on" or "off" over the tree's state.
        r.View.OnState = delegate { return RowState(r.Tree); };
        SplitPane split = kit.Split;
        UiSection section = kit.Section;
        kit.Split = cat.Split;
        kit.Section = rack;
        r.View.Row = kit.RackRow(r.View.Id, uid, r.Proxy, r.View.RowBag, shell.Select);
        kit.Split = split;
        kit.Section = section;
        if (cat.GroupMembers.Count > 0) cat.GroupMembers[0].Add(r.View);
        rows.Add(r);
        byUid[uid] = r;
        Views.Add(r.View);
        return r;
    }

    // A tree another FoliageFX looks after has no state of its own in a row:
    // its badge's tooltip says why, beside its greyed switch, so the row keeps
    // the layout of every other.
    static string RowState(Tree t)
    {
        return t != null && !t.Yielded ? t.State() : "";
    }

    // The row's state, badge and switch. RackRow.SetState clears the badge
    // (it is meant for "needs" states), so the badge is set after it, here and
    // after every shell refresh.
    void SyncRow(Row r)
    {
        Tree t = r.Tree;
        bool on = life.Looks.IsEnabled(t.Key);
        if (r.Proxy.val != on) r.Proxy.valNoCallback = on;
        RackRow row = r.View.Row;
        if (row == null) return;
        string state = RowState(t);
        if (state != r.LastState)
        {
            r.LastState = state;
            row.SetState(state);
        }
        row.SetBadge(t.Yielded ? OwnInstance : null);
        row.SetSwitchEnabled(!t.Yielded);
    }

    void Switch(Row r, bool on)
    {
        log.Run("switch a tree", delegate
        {
            Tree t = r.Tree;
            if (t == null) return;
            life.Looks.SetEnabled(t.Key, on);
            life.Touch(t);
        });
    }

    // Nothing of the editor is seen on another page, so it is left as it is
    // there and brought up to date when the Trees page shows.
    void SyncEditor()
    {
        if (!shell.IsCategory(FoliageFXConfig.PageTrees)) return;
        Row r = SelectedRow();
        Tree t = r != null ? r.Tree : null;
        bool show = t != null;
        bool yielded = show && t.Yielded;
        if (r != selRow)
        {
            selRow = r;
            selMaterial = null;
            pushedLook = null;
        }
        if (show && !yielded) PickMaterial(r, t);
        else selMaterial = null;
        LeafMaterial m = selMaterial;
        bool hasMaterials = show && !yielded && t.Materials.Count > 0;
        bool editable = hasMaterials && m != null && m.Look != null;

        bool changed = SyncList(hasMaterials ? t : null);
        changed = Show(editorBag, show) | changed;
        changed = Show(switchBag, show && !yielded) | changed;
        changed = Show(listBag, hasMaterials) | changed;
        changed = Show(materialBag, hasMaterials) | changed;
        changed = Show(lookBag, editable) | changed;
        changed = Show(leafBag, editable && m.WantsLeaf(true)) | changed;
        changed = Show(footBag, show && !yielded) | changed;
        changed = Show(emptyBag, visibleRows == 0) | changed;
        if (show)
        {
            Bind(r.Proxy);
            if (header != null && header.TitleText != null && header.TitleText.text != t.Uid) header.TitleText.text = t.Uid;
            // The asset the atom has loaded, by its own name, under the status.
            string state = t.State();
            string asset = t.AssetName;
            if (caption != null && (state != capState || yielded != capYielded || t != capTree || asset != capAsset))
            {
                capState = state;
                capYielded = yielded;
                capTree = t;
                capAsset = asset;
                caption.text = "Status: " + Capitalize(state) + (asset != null ? "\nAsset: " + asset : "") + "\n" + (yielded ? OwnInstance : Guide);
            }
        }
        if (m != null && materialCard != null && materialCard.TitleText != null && materialCard.TitleText.text != m.Name) materialCard.TitleText.text = m.Name;
        if (editable && m.Look != pushedLook) Push(m.Look);
        // A texture that gives no leaf shapes drops nothing whatever the
        // switch says; the falling leaves line under it says why.
        if (editable && life.Falling != null) UiKit.SetEnabled(dropToggle, life.Falling.CanDrop(m));
        if (modeStrip != null) modeStrip.Repaint();
        // Another material's lines now, not a slow tick later.
        if (m != linesFor)
        {
            linesFor = m;
            kit.RefreshLiveLines();
            changed = true;
        }
        if (changed) shell.Resize();
    }

    // The shell's current feature, when it is a tree row that is shown.
    Row SelectedRow()
    {
        FeatureView v = shell.CurrentFeature();
        if (v == null || v.Hidden || v.Id == null || !v.Id.StartsWith(FoliageFXConfig.TreeRowPrefix, System.StringComparison.Ordinal)) return null;
        for (int i = 0; i < rows.Count; i++)
            if (rows[i].View == v) return rows[i];
        return null;
    }

    // The selected material stays while the tree still holds it. A tree read
    // again holds new materials under the same names, so the name is looked
    // for next; a tree opened anew gets the material last picked on it, else
    // its first leaf material, which is what the page is mostly opened for.
    void PickMaterial(Row r, Tree t)
    {
        LeafMaterial m = selMaterial;
        List<LeafMaterial> mats = t.Materials;
        if (m != null && m.Owner == t && mats.Contains(m)) return;
        string want = m != null ? m.Name : r.LastMaterial;
        m = want != null ? FindMaterial(t, want) : null;
        for (int i = 0; m == null && i < mats.Count; i++)
            if (mats[i].Candidate && !mats[i].Foreign) m = mats[i];
        if (m == null && mats.Count > 0) m = mats[0];
        selMaterial = m;
        pushedLook = null;
        if (m != null) r.LastMaterial = m.Name;
    }

    static LeafMaterial FindMaterial(Tree t, string name)
    {
        for (int i = 0; i < t.Materials.Count; i++)
            if (t.Materials[i].Name == name) return t.Materials[i];
        return null;
    }

    // ---- the materials list ----

    // "g2Mat 1   converted": one row per material of the tree, in the order
    // the asset lists them, the picked one painted as selected. Rows are made
    // only when a tree has more materials than any before it; a row's text is
    // written only when its material or state changed. True when a row was
    // made, shown or hidden.
    bool SyncList(Tree t)
    {
        int count = t != null ? t.Materials.Count : 0;
        bool changed = false;
        if (materialRows.Count < count)
        {
            SplitPane split = kit.Split;
            UiSection section = kit.Section;
            kit.Split = cat.Split;
            kit.Section = list;
            while (materialRows.Count < count) MakeMaterialRow(materialRows.Count);
            kit.Split = split;
            kit.Section = section;
            shell.Attach(script.UITransform);
            changed = true;
        }
        for (int i = 0; i < materialRows.Count; i++)
        {
            MaterialRow mr = materialRows[i];
            bool on = i < count;
            GameObject g = mr.Row.Root;
            if (g != null && g.activeSelf != on)
            {
                g.SetActive(on);
                changed = true;
            }
            if (!on)
            {
                mr.Material = null;
                continue;
            }
            LeafMaterial m = t.Materials[i];
            if (mr.Material != m)
            {
                mr.Material = m;
                mr.Row.SetLabel(m.Name);
            }
            string state = m.StateLabel();
            if (state != mr.LastState)
            {
                mr.LastState = state;
                mr.Row.SetState(state);
            }
            bool picked = m == selMaterial;
            if (mr.Row.Selected != picked) kit.PaintRow(mr.Row, picked);
        }
        return changed;
    }

    void MakeMaterialRow(int index)
    {
        MaterialRow mr = new MaterialRow();
        int slot = index;
        mr.Row = kit.RackRow("material_" + index, "", null, null, delegate (string id) { PickRow(slot); });
        materialRows.Add(mr);
    }

    void PickRow(int slot)
    {
        Row r = selRow;
        Tree t = r != null ? r.Tree : null;
        if (t == null || slot >= t.Materials.Count) return;
        LeafMaterial m = t.Materials[slot];
        if (m == selMaterial) return;
        selMaterial = m;
        pushedLook = null;
        r.LastMaterial = m.Name;
        Sync(life);
    }

    // Binds the editor's Enabled to a tree's proxy. VaM moves the toggle's
    // listener with it, so the toggle drives whichever tree is selected.
    void Bind(JSONStorableBool proxy)
    {
        if (proxy == null || proxy == boundProxy || enabledToggle == null || enabledToggle.toggle == null) return;
        if (boundProxy != null) boundProxy.toggle = null;
        boundProxy = proxy;
        proxy.toggle = enabledToggle.toggle;
    }

    // Shows or hides a bag, and says whether anything changed. A hint line
    // stays hidden while Show Hints is off. Compared item by item rather than
    // remembered: VaM switches every control on again when it rebuilds the
    // panel, and this puts them back.
    bool Show(List<GameObject> bag, bool on)
    {
        bool changed = false;
        for (int i = 0; i < bag.Count; i++)
        {
            GameObject g = bag[i];
            if (g == null) continue;
            bool want = on && (FoliageFXConfig.ShowHints || !hintRoots.Contains(g));
            if (g.activeSelf == want) continue;
            g.SetActive(want);
            changed = true;
        }
        return changed;
    }

    // ---- live lines ----

    // "Standard, queue 3000. Standard shader in Fade mode: never receives
    // shadows." Kept per material: its source and verdict change only when
    // the tree is read again, which makes new materials.
    string ShaderLine()
    {
        LeafMaterial m = selMaterial;
        if (m == null || m.Source == null) return "";
        if (m == lineMaterial) return lineText;
        lineMaterial = m;
        string shader = m.Source.shader != null ? m.Source.shader.name : "no shader";
        lineText = shader + ", queue " + m.Source.renderQueue + ". " + (m.Reason ?? "");
        return lineText;
    }

    // What the mode makes of the material, from the mode itself rather than
    // from the copy the next frame makes, so a click on the strip is answered
    // at once. Wind, falling leaves and every part of the look but Tint work
    // on the converted leaf only, which is what this says when the material
    // stays as it is.
    string ModeLine()
    {
        LeafMaterial m = selMaterial;
        Tree t = selRow != null ? selRow.Tree : null;
        if (m == null || m.Look == null || t == null || life == null) return "";
        if (!m.WantsLeaf(true))
        {
            if (m.Look.Mode == FoliageFXConfig.ModeKeep) return "Kept as the asset made it, so " + TintOnly;
            return "Auto leaves it as the asset made it, so " + TintOnly;
        }
        if (!life.Master) return "Converted once FoliageFX is enabled on the Overview.";
        if (!t.Enabled) return "Converted once this tree is enabled.";
        if (life.ShadowFix != null && !life.ShadowFix.Enabled.val) return "Converted once Shadow Fix is switched on, on the Effects page.";
        return "Converted: shadows, Wind, Falling Leaves and the whole look below apply to it.";
    }

    string FallingLine()
    {
        LeafMaterial m = selMaterial;
        if (m == null || life == null || life.Falling == null) return "";
        return life.Falling.MaterialLine(m);
    }

    // ---- the look ----

    void Push(LeafLook look)
    {
        pushedLook = look;
        pushing = true;
        mode.valNoCallback = look.Mode;
        tint.valNoCallback = look.Tint;
        hue.valNoCallback = look.Hue;
        saturation.valNoCallback = look.Saturation;
        brightness.valNoCallback = look.Brightness;
        gloss.valNoCallback = look.Smoothness;
        cutoff.valNoCallback = look.Cutoff;
        translucency.valNoCallback = look.Translucency;
        doubleSided.valNoCallback = look.DoubleSided;
        dropLeaves.valNoCallback = look.Falling;
        pushing = false;
        if (modeStrip != null) modeStrip.Repaint();
    }

    void FloatChanged(float value) { Changed(); }
    void BoolChanged(bool value) { Changed(); }
    void ModeChanged(string value) { Changed(); }
    void TintChanged(float h, float s, float v) { Changed(); }

    // The strip wrote the mode into the look already (ModeChanged): the rest
    // of the look shows or hides, and the line under the strip says what the
    // mode does, before the next frame converts or restores the material.
    void AfterModePick()
    {
        Sync(life);
        kit.RefreshLiveLines();
    }

    // Every control writes the whole look: one path for all of them, and no
    // allocation while a slider is dragged.
    void Changed()
    {
        if (pushing || log == null) return;
        log.Run("change a material's look", fnPull);
    }

    // A look the controls were not filled from (a scene restore replaced it
    // a moment ago) is left alone; the next Sync fills them from the new one.
    void Pull()
    {
        LeafMaterial m = selMaterial;
        Tree t = selRow != null ? selRow.Tree : null;
        if (m == null || t == null || m.Owner != t || m.Look == null || m.Look != pushedLook) return;
        LeafLook look = m.Look;
        look.Mode = mode.val;
        look.Tint = tint.val;
        look.Hue = hue.val;
        look.Saturation = saturation.val;
        look.Brightness = brightness.val;
        look.Smoothness = gloss.val;
        look.Cutoff = cutoff.val;
        look.Translucency = translucency.val;
        look.DoubleSided = doubleSided.val;
        look.Falling = dropLeaves.val;
        look.Customized = true;
        life.Touch(t);
    }

    // ---- buttons ----

    // The whole tree: every look back to its source's defaults, and the tree
    // switched on.
    void ResetTree()
    {
        Row r = selRow;
        Tree t = r != null ? r.Tree : null;
        if (t == null || t.Yielded) return;
        life.Looks.ResetTree(t.Key);
        for (int i = 0; i < t.Materials.Count; i++)
        {
            LeafMaterial x = t.Materials[i];
            if (x.Look != null) x.Look.Reset(x.Source);
        }
        if (!r.Proxy.val) r.Proxy.valNoCallback = true;
        life.Touch(t);
        if (selMaterial != null && selMaterial.Look != null) Push(selMaterial.Look);
        Sync(life);
    }

    static string Capitalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
