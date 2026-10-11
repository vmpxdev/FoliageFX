using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Overview: the dashboard. Left: the identity card (name, brand, version,
// the global master, the Patreon credit), the What's New scroll box, and
// the Diagnostics card (a plain status line or, with Technical Info, the
// cost caption; Diagnose to the Message Log, Rescan; the Technical Info and
// Show Hints switches). Right: a Trees card with one live row per tree and
// an Effects card with one per effect (state and switch); a click opens it.
//
// The tree rows are made at runtime, as the Trees page makes its own: the
// Trees page owns the pool, and hands its views here (SyncTrees) each time it
// adds one. A dashboard tree row shows what that tree's rack row shows (its
// state, its badge, and a switch only where the rack has one), so the two
// never disagree about a tree another FoliageFX looks after.
sealed class OverviewPanel
{
    public delegate void BoolFn(bool value);

    public JSONStorableBool Enabled;
    public BoolFn OnEnabled;

    // One dashboard row per tree or effect; synced on the slow tick.
    sealed class Entry
    {
        public FeatureView View;
        public RackRow Row;
        public bool Tree;
        public string LastState;
    }

    readonly List<Entry> entries = new List<Entry>();
    // Dashboard rows live in their own bag, not the category's, so the shell
    // never re-shows what Refresh hid (a tree that left the scene).
    readonly List<GameObject> dashBag = new List<GameObject>();
    // The status caption, the cost hints, and the Trees card's empty line:
    // their own bags, or the shell's category bag would switch them back on
    // every tick.
    readonly List<GameObject> statusBag = new List<GameObject>();
    readonly List<GameObject> techBag = new List<GameObject>();
    readonly List<GameObject> noTreesBag = new List<GameObject>();
    JSONStorableBool technicalSwitch;
    UiKit kit;
    UiShell shell;
    SplitPane pane;                  // the Overview's two columns, for the rows made at runtime
    UiSection treesCard;
    Text status;
    string lastStatus;
    UIDynamic costHint;              // the cost hints, shown with Technical Info
    UIDynamicButton diagnose;        // shown with Technical Info
    UIDynamicButton copyPatreon;     // reads "Copied" for a moment after a click
    ConfirmBox confirm;              // the Full Changelog modal
    float copiedAt;                  // realtime of the last copy, 0 when the label is back

    // The release notes as the modal's rows (ConfirmBox.InformRows), read back
    // from the plain text every entry is written in (Changelog.cs says how):
    // a release's line, then per line of its body a section name (a line with a
    // bullet right under it), a bullet (its first sentence the bold lead when
    // short), or a paragraph. Heads Up and its bullets take the warning colour,
    // Performance and its bullets green; every release after the newest is
    // drawn quieter. Built on the click.
    static List<ReportRow> ChangelogRows()
    {
        List<ReportRow> rows = new List<ReportRow>();
        ChangelogEntry[] list = Changelog.Entries;
        for (int i = 0; i < list.Length; i++)
        {
            ChangelogEntry e = list[i];
            ReportRow release = ReportRow.Make(ReportRow.KindRelease, "Version " + e.Version, e.Title ?? "");
            if (i > 0) release.Note = "older";
            rows.Add(release);
            string[] lines = (e.Body ?? "").Split('\n');
            Color tint = FoliageFXConfig.ReportConverted;
            for (int k = 0; k < lines.Length; k++)
            {
                string line = lines[k].Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("- ", System.StringComparison.Ordinal))
                {
                    string text = line.Substring(2).Trim();
                    string lead = null;
                    int stop = text.IndexOf(". ", System.StringComparison.Ordinal);
                    if (stop > 0 && stop < Changelog.LeadMax)
                    {
                        lead = text.Substring(0, stop + 1);
                        text = text.Substring(stop + 2);
                    }
                    ReportRow bullet = ReportRow.Make(ReportRow.KindBullet, lead, text);
                    bullet.TagColor = tint;
                    rows.Add(bullet);
                }
                else if (k + 1 < lines.Length && lines[k + 1].StartsWith("- ", System.StringComparison.Ordinal))
                {
                    tint = line == Changelog.HeadsUp ? FoliageFXConfig.ReportChanged
                        : line == Changelog.Performance ? FoliageFXConfig.ReportNew : FoliageFXConfig.ReportConverted;
                    ReportRow section = ReportRow.Make(ReportRow.KindSection, null, line);
                    section.TagColor = tint;
                    rows.Add(section);
                }
                else rows.Add(ReportRow.Make(ReportRow.KindPara, null, line));
            }
        }
        return rows;
    }

    // Scene scope: whether this scene's trees are fixed at all is about the
    // scene, and an effect's Reset leaves it alone.
    public void Create(ParamSet set)
    {
        Enabled = set.Bool(FoliageFXConfig.PluginId, ParamSet.Scene, "enabled", new JSONStorableBool("Enabled", true, EnabledChanged));
    }

    void EnabledChanged(bool value)
    {
        if (OnEnabled != null) OnEnabled(value);
    }

    public void Build(UiKit kit, UiShell shell, UiCategory cat, OverlayLog log, Diagnostics diagnostics, Lifecycle life, JSONStorableBool technical, JSONStorableBool hints)
    {
        List<GameObject> bag = cat.Bag;
        technicalSwitch = technical;
        this.kit = kit;
        this.shell = shell;
        pane = cat.Split;

        // Left: identity and master in one card, then status and actions.
        kit.BeginSection(FoliageFXConfig.ProductName, false, bag);
        kit.Caption(FoliageFXConfig.Brand + "  ·  " + BuildInfo.Line(), false, bag);
        kit.MasterToggle(Enabled, false, bag, "Enabled");
        kit.Caption(FoliageFXConfig.CreditTagline, false, bag, 2);
        Transform credit = kit.Row(false, FoliageFXConfig.RowH, bag);
        copyPatreon = kit.Button(credit, FoliageFXConfig.CopyPatreonLabel, FoliageFXConfig.Button, FoliageFXConfig.OnButton);
        kit.Wire(copyPatreon, "copy patreon link", delegate
        {
            GUIUtility.systemCopyBuffer = FoliageFXConfig.PatreonUrl;
            log.Info("Patreon link copied: " + FoliageFXConfig.PatreonUrl);
            copiedAt = Time.realtimeSinceStartup;
            if (copyPatreon != null) copyPatreon.label = FoliageFXConfig.CopiedPatreonLabel;
        });
        kit.EndSection();

        // What's New: the release notes, newest first, in a box that scrolls.
        kit.BeginSection("What's New", false, bag);
        kit.ScrollBox(false, bag, FoliageFXConfig.WhatsNewH, Changelog.Text());
        // The whole of it at reading size: the branded modal's well is as tall as
        // the panel allows and scrolls past that; Close or a click outside ends it.
        if (confirm == null) confirm = new ConfirmBox(kit);
        Transform notes = kit.Row(false, FoliageFXConfig.RowH, bag);
        UIDynamicButton full = kit.Button(notes, "Full Changelog", FoliageFXConfig.Button, FoliageFXConfig.OnButton);
        kit.Wire(full, "open the changelog", delegate
        {
            confirm.InformRows("What's New", ChangelogRows(), "Close");
        });
        kit.EndSection();

        kit.BeginSection("Diagnostics", false, bag);
        status = kit.Caption("", false, statusBag, 2);
        List<string> costHints = new List<string>();
        costHints.Add("Under 10% of the frame goes unnoticed; past 25% costs frames.");
        costHints.Add("A ~ before the frame rate means it was measured: VaM set no target (in VR, or with vsync off).");
        costHints.Add("Shared is finding the CustomUnityAssets and reading their materials, whatever is switched on.");
        costHints.Add("Peak is the slowest frame in the last 2 s; a spike when a tree is converted or rescanned is normal.");
        costHint = kit.HintBox(costHints, false, techBag);
        UiKit.SetBag(techBag, false);
        Transform row = kit.Row(false, FoliageFXConfig.RowH, bag);
        diagnose = kit.Button(row, "Diagnose", FoliageFXConfig.Accent, FoliageFXConfig.OnAccent);
        kit.Wire(diagnose, "diagnose", delegate { diagnostics.Report(); });
        UIDynamicButton rescan = kit.Button(row, "Rescan", FoliageFXConfig.Button, FoliageFXConfig.OnButton);
        kit.Wire(rescan, "rescan the trees", delegate { life.Rescan(); });
        if (technical != null)
        {
            UIDynamicToggle t = kit.Toggle(technical, false, bag, "Adds costs and counts to the captions, and shows the Diagnose button used for debug logs.");
            if (t != null) OverlayLog.Probe(delegate { t.label = "Technical Info"; return null; });
        }
        if (hints != null)
        {
            UIDynamicToggle t = kit.Toggle(hints, false, bag, "Shows short descriptions of what each setting does and other useful information.");
            if (t != null) OverlayLog.Probe(delegate { t.label = "Show Hints"; return null; });
        }
        kit.EndSection();

        // Right: the trees, whose rows arrive with the scan (SyncTrees), then
        // the effects, fixed from here on.
        treesCard = kit.BeginSection("Trees", true, bag);
        kit.Caption("No CustomUnityAsset atoms in the scene. Add a tree or plant asset and it appears here.", true, noTreesBag);
        kit.EndSection();
        UiCategory effects = shell.CategoryOf(FoliageFXConfig.PageEffects);
        kit.BeginSection("Effects", true, bag);
        if (effects != null)
            for (int i = 0; i < effects.Features.Count; i++)
            {
                FeatureView v = effects.Features[i];
                if (v.Master != null) AddRow(v, false);
            }
        kit.EndSection();
    }

    // The Trees page made rows for trees the scan found: each gets its
    // dashboard row once, in the Trees card, and is hidden with its tree.
    public void SyncTrees(List<FeatureView> treeViews)
    {
        if (kit == null || treesCard == null || treeViews == null) return;
        SplitPane split = kit.Split;
        UiSection section = kit.Section;
        kit.Split = pane;
        kit.Section = treesCard;
        for (int i = 0; i < treeViews.Count; i++)
        {
            FeatureView v = treeViews[i];
            if (v != null && Find(v) == null) AddRow(v, true);
        }
        kit.Split = split;
        kit.Section = section;
    }

    Entry Find(FeatureView v)
    {
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].View == v) return entries[i];
        return null;
    }

    void AddRow(FeatureView v, bool tree)
    {
        Entry e = new Entry();
        e.View = v;
        e.Tree = tree;
        e.Row = kit.RackRow("dash_" + v.Id, v.Label, v.Master, dashBag, shell.Select, false);
        kit.PaintRow(e.Row, false, FoliageFXConfig.IsExperimental(v.Id));
        if (e.Row.Select != null)
        {
            // The row id carries a dash_ prefix so the rack rows keep their
            // ids; the click must select the feature itself.
            string featureId = v.Id;
            UiShell target = shell;
            e.Row.Select.button.onClick.RemoveAllListeners();
            kit.Wire(e.Row.Select, "open " + v.Label, delegate { target.Select(featureId); });
        }
        entries.Add(e);
    }

    public void Refresh(Lifecycle life)
    {
        bool technical = technicalSwitch != null && technicalSwitch.val;
        bool changed = false;
        if (confirm != null) confirm.Tick();
        if (copiedAt > 0f && Time.realtimeSinceStartup - copiedAt > FoliageFXConfig.CopiedLabelSeconds)
        {
            copiedAt = 0f;
            if (copyPatreon != null) copyPatreon.label = FoliageFXConfig.CopyPatreonLabel;
        }
        if (status != null)
        {
            // The plugin's own main-thread cost with Technical Info; without it
            // a plain verdict with no numbers. A leaf shader that failed to load
            // is said either way: it is why nothing is converted.
            bool failed = life.Shader != null && life.Shader.Failed;
            string line = technical && !failed ? life.TimingCaption() : life.StatusLine();
            if (line != lastStatus) { lastStatus = line; status.text = line; }
            GameObject statusPad = status.transform.parent != null ? status.transform.parent.gameObject : null;
            bool showStatus = line.Length > 0;
            if (statusPad != null && statusPad.activeSelf != showStatus) { statusPad.SetActive(showStatus); changed = true; }
        }
        // The cost hints are hints: Show Hints off keeps them out of sight too.
        bool showCost = technical && FoliageFXConfig.ShowHints;
        if (costHint != null && costHint.gameObject != null && costHint.gameObject.activeSelf != showCost) { costHint.gameObject.SetActive(showCost); changed = true; }
        if (diagnose != null && diagnose.gameObject != null && diagnose.gameObject.activeSelf != technical) { diagnose.gameObject.SetActive(technical); changed = true; }
        int treesShown = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            Entry e = entries[i];
            if (e.Row == null) continue;
            if (e.Row.Root != null && e.Row.Root.activeSelf == e.View.Hidden) { e.Row.Root.SetActive(!e.View.Hidden); changed = true; }
            if (e.View.Hidden) continue;
            e.Row.Sync();
            string s;
            if (e.Tree)
            {
                treesShown++;
                s = e.View.OnState != null ? e.View.OnState() : "";
                if (MirrorRack(e)) changed = true;
            }
            else if (!e.View.IsOn) s = e.View.OffState != null ? e.View.OffState : "off";
            else
            {
                // An effect's own rack row shows its state; the dashboard shows
                // what it costs. On but idle reads "on".
                string cost = technical ? life.CostText(e.View.Id) : "";
                s = cost.Length > 0 ? cost : "on";
            }
            if (s != e.LastState && e.Row.State != null) { e.LastState = s; e.Row.SetState(s); }
            // SetState clears the badge (it is for "needs" states); a tree's
            // badge is its own, so it goes back on after.
            if (e.Tree) Badge(e);
        }
        bool none = treesShown == 0;
        for (int i = 0; i < noTreesBag.Count; i++)
        {
            GameObject g = noTreesBag[i];
            if (g != null && g.activeSelf != none) { g.SetActive(none); changed = true; }
        }
        if (changed && shell != null) shell.Resize();
    }

    // A tree's switch shows only where its rack row has one: a tree another
    // FoliageFX looks after has none.
    static bool MirrorRack(Entry e)
    {
        RackRow rack = e.View.Row;
        if (rack == null || e.Row.Toggle == null) return false;
        bool show = rack.Toggle == null || rack.Toggle.gameObject.activeSelf;
        GameObject g = e.Row.Toggle.gameObject;
        if (g.activeSelf == show) return false;
        g.SetActive(show);
        return true;
    }

    static void Badge(Entry e)
    {
        RackRow rack = e.View.Row;
        bool on = rack != null && rack.Warn != null && rack.Warn.activeSelf;
        e.Row.SetBadge(on ? rack.WarnText : null);
    }
}
