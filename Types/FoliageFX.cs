using System.Collections;
using SimpleJSON;
using UnityEngine;

// FoliageFX facade. Owns the VaM lifecycle, the scene JSON (one keyed block,
// with the trees' looks), the parameter registry, the panel shell and the
// atom-rename hook. Everything else lives in Types/: the effects hold their
// own storables, the panels fill their own pages, and Lifecycle runs the
// scene. Loads on any atom and as a session plugin: on a CustomUnityAsset it
// looks after that tree, anywhere else after every CustomUnityAsset that has
// no FoliageFX of its own.
public class FoliageFX : MVRScript
{
    JSONStorableString statusJSON;
    OverlayLog log;
    Lifecycle life;
    Diagnostics diagnostics;
    ParamSet paramSet;
    UiKit kit;
    UiShell shell;
    readonly OverviewPanel overview = new OverviewPanel();
    readonly TestingPanel testing = new TestingPanel();
    readonly TreesPanel trees = new TreesPanel();
    readonly EffectsPanel effects = new EffectsPanel();
    readonly ShadowFix shadowFix = new ShadowFix();
    readonly Wind wind = new Wind();
    readonly FallingLeaves falling = new FallingLeaves();
    SuperController.OnAtomUIDRename renameHandler;
    bool ready;
    float nextUiRefresh;
    // Built once: a method group passed each frame would be a new delegate
    // every frame.
    OverlayLog.Fn fnUpdate;
    OverlayLog.Fn fnRefresh;

    public override void Init()
    {
        statusJSON = new JSONStorableString("status", "Starting " + FoliageFXConfig.ProductName + "...");
        log = new OverlayLog(statusJSON);
        BuildInfo.Load(this, log);
        log.Run("initialize " + FoliageFXConfig.ProductName, delegate
        {
            // Storables first: registered once, visible to Timeline and triggers
            // regardless of what the panel shows.
            paramSet = new ParamSet(this);
            shell = new UiShell(paramSet);
            overview.Create(paramSet);
            overview.OnEnabled = delegate (bool on)
            {
                log.Run("master toggle", delegate { if (life != null) life.SetMaster(on); });
            };
            testing.Create(paramSet);
            shadowFix.Create(paramSet);
            wind.Create(paramSet);
            falling.Create(paramSet);
            trees.Create(paramSet);

            // The effects are wired before Attach, so the scope default (All
            // Atoms off on a CustomUnityAsset) lands now, before a scene
            // restore reads the value against it.
            life = new Lifecycle(log, this);
            life.ShadowFix = shadowFix;
            life.Wind = wind;
            life.Falling = falling;
            life.Attach(containingAtom);
            diagnostics = new Diagnostics(this, life, log);
            diagnostics.Params = paramSet;
            fnUpdate = life.Update;
            fnRefresh = RefreshUI;

            life.Shader.Request();
            BuildUi();
            life.SetMaster(overview.Enabled.val);
            renameHandler = OnAtomRenamed;
            SuperController.singleton.onAtomUIDRenameHandlers += renameHandler;
            ready = true;
            StartCoroutine(AttachSoon());
        });
    }

    void BuildUi()
    {
        kit = new UiKit(this);
        kit.Log = log;
        kit.EnsureFont(null);

        UiCategory catOverview = shell.AddCategory(FoliageFXConfig.PageOverview, "Overview", "Status and Trees");
        UiCategory catTrees = shell.AddCategory(FoliageFXConfig.PageTrees, "Trees", "Fix and Recolour");
        UiCategory catEffects = shell.AddCategory(FoliageFXConfig.PageEffects, "Effects", "Shadows, Wind, Leaves");
        effects.AddFeatures(shell, shadowFix, wind, falling);

        shell.BuildBar(kit);
        shell.Open(catOverview);
        overview.Build(kit, shell, catOverview, log, diagnostics, life, testing.Hints);
        // Show Hints takes effect on the click, not on the next
        // half-second UI tick (RefreshUI reads it and applies the shell).
        JSONStorableBool.SetBoolCallback onSwitch = delegate (bool v) { log.RunWarn("could not refresh the panel", RefreshUI); };
        if (testing.Hints != null) testing.Hints.setCallbackFunction = onSwitch;
        shell.Open(catTrees);
        trees.Build(kit, shell, catTrees, life, this, log);
        shell.Open(catEffects);
        shell.BuildRack(catEffects, "", "Effects");
        effects.BuildInspectors(kit, shell, life, paramSet, shadowFix, wind, falling);
        shell.Finish();
        // The tree rows are made as the scan finds trees; the dashboard makes
        // its own for each, and the editor follows every selection.
        trees.OnRowsAdded = delegate { overview.SyncTrees(trees.Views); };
        shell.OnApplied = delegate { trees.Sync(life); };
        overview.Refresh(life);
        OverlayLog.SafeRun(log, "could not attach the panel host", delegate { shell.Attach(UITransform); });
    }

    // ---- scene persistence ----

    // The plugin's values live in one keyed block inside its scene node
    // (Settings.SceneBlock), the trees' switches and looks with them; VaM's
    // own by-name storage is off for every storable, so a renamed storable
    // never orphans a saved value.
    public override JSONClass GetJSON(bool includePhysical = true, bool includeAppearance = true, bool forceStore = false)
    {
        JSONClass jc = base.GetJSON(includePhysical, includeAppearance, forceStore);
        OverlayLog.SafeRun(log, "could not write the plugin's scene block", delegate
        {
            if (paramSet == null || life == null) return;
            if (jc.HasKey(Settings.BlockKey)) jc.Remove(Settings.BlockKey);
            jc.Add(Settings.BlockKey, Settings.SceneBlock(paramSet, life.Looks.Write()));
            // VaM writes a plugin into the scene only when needsStore is set,
            // and it sets that only for registered storables that are storable
            // and off their default. Every FoliageFX storable is non-storable
            // (the block above is the save), so with the plugin label empty and
            // the plugin enabled VaM would drop the whole node: the block is
            // the reason to store.
            needsStore = true;
        });
        return jc;
    }

    public override void RestoreFromJSON(JSONClass jc, bool restorePhysical = true, bool restoreAppearance = true,
        JSONArray presetAtoms = null, bool setMissingToDefault = true)
    {
        // A restore that carries nothing changes nothing. An atom's Reset
        // buttons (and anything else that restores from an empty node) call
        // every storable with an empty node and "missing means default": the
        // base class would put every toggle back to its default and the looks
        // would be emptied, so one reset of the atom cost the scene's trees.
        if (CarriesNothing(jc)) return;
        base.RestoreFromJSON(jc, restorePhysical, restoreAppearance, presetAtoms, setMissingToDefault);
        // A node without our block is read only when it holds our keys (under
        // storable names); VaM's own fields alone are not our data, and
        // reading them would put every setting to its default.
        bool block = (jc[Settings.BlockKey] as JSONClass) != null;
        if (!block && (paramSet == null || !paramSet.ClaimsAny(jc))) return;
        OverlayLog.SafeRun(log, "could not restore the plugin's saved values", delegate
        {
            if (paramSet == null || life == null) return;
            JSONClass treesNode;
            Settings.ReadScene(jc, paramSet, setMissingToDefault, out treesNode);
            // A complete restore replaces every look, an absent trees node
            // meaning none; an overlay (missing keys kept) keeps the looks
            // unless the node brings its own.
            if (treesNode != null || setMissingToDefault) life.Looks.Read(treesNode);
            life.TouchAll();
        });
        OverlayLog.SafeRun(log, "could not refresh the panel after a restore", delegate
        {
            if (shell != null) shell.Apply();
        });
    }

    // The late pass of the same reset: guarded the same way, or it would undo
    // what the early pass kept.
    public override void LateRestoreFromJSON(JSONClass jc, bool restorePhysical = true, bool restoreAppearance = true, bool setMissingToDefault = true)
    {
        if (CarriesNothing(jc)) return;
        base.LateRestoreFromJSON(jc, restorePhysical, restoreAppearance, setMissingToDefault);
    }

    // An empty node, or one holding only the storable's id: VaM's own shape for
    // "restore from nothing". A node with our block, or with our keys under
    // their storable names, carries data and is read.
    static bool CarriesNothing(JSONClass jc)
    {
        if (jc == null || jc.Count == 0) return true;
        return jc.Count == 1 && jc["id"] != null;
    }

    // VaM calls InitUI when the plugin panel is (re)built. Attach the host to
    // the (possibly new) panel, re-apply visibility, and repeat after layout.
    public override void InitUI()
    {
        base.InitUI();
        if (shell == null) return;
        OverlayLog.SafeRun(log, "could not refresh the panel", RefreshUI);
        StartCoroutine(AttachSoon());
    }

    IEnumerator AttachSoon()
    {
        for (int i = 0; i < 3; i++)
        {
            yield return new WaitForEndOfFrame();
            if (shell == null) yield break;
            OverlayLog.SafeRun(log, "could not refresh the panel", RefreshUI);
        }
    }

    // ---- frame ----

    void Update()
    {
        if (!ready) return;
        log.RunOnce("frame update", fnUpdate);
        if (Time.unscaledTime >= nextUiRefresh)
        {
            nextUiRefresh = Time.unscaledTime + FoliageFXConfig.UiRefreshSeconds;
            log.RunOnce("panel tick", fnRefresh);
        }
    }

    // The UI update, factored out of the slow tick so InitUI and its
    // end-of-frame retries run all of it, not only Attach and Apply. Attaching
    // is idempotent and touches only what differs, cheap insurance against VaM
    // rebuilding the panel behind our back. Apply runs the Trees page's sync
    // (OnApplied), which adds rows for new trees and hands them to the
    // dashboard; the Overview's refresh comes after, so it shows them the same
    // tick, and it resizes the panel itself when anything changed.
    void RefreshUI()
    {
        if (shell == null) return;
        shell.Attach(UITransform);
        if (testing != null)
        {
            FoliageFXConfig.ShowHints = testing.Hints == null || testing.Hints.val;
        }
        shell.Apply();
        if (ready && life != null && shell.IsCategory(FoliageFXConfig.PageOverview)) overview.Refresh(life);
    }

    // VaM's rename: the registry renames the tree, the looks move to the new
    // key, and the Trees page opens the renamed tree's new row.
    void OnAtomRenamed(string oldUid, string newUid)
    {
        if (!ready) return;
        log.RunWarn("could not follow a renamed atom", delegate
        {
            life.Trees.OnAtomRenamed(oldUid, newUid);
            life.Looks.Rename(oldUid, newUid);
            trees.Renamed(oldUid, newUid);
        });
    }

    void OnEnable()
    {
        if (!ready) return;
        log.Run("enable", life.Resume);
    }

    void OnDisable()
    {
        if (!ready) return;
        log.Run("disable", life.Hold);
    }

    // Everything made from the leaf shader goes before the shader's bundle is
    // handed back: VaM unloads the bundle when its last holder lets go. The
    // bundle goes back after a failed Init too, since its request may be out.
    void OnDestroy()
    {
        if (renameHandler != null)
        {
            SuperController sc = SuperController.singleton;
            if (sc != null) sc.onAtomUIDRenameHandlers -= renameHandler;
            renameHandler = null;
        }
        if (ready) log.Run("destroy", delegate { life.Release("plugin removed"); });
        ready = false;
        if (life != null && life.Shader != null) OverlayLog.SafeRun(log, "release the leaf shader", life.Shader.Release);
    }
}
