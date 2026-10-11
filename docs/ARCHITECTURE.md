# FoliageFX architecture

Kept current with the code. FoliageFX grew out of a one-file prototype; nothing here depends
on it any more. The repo layout, tooling, UI kit and persistence follow Live Skin. The
panel model is in `docs/UI_DESIGN.md`.

## 1. Layers

```
FoliageFX (MVRScript facade)   VaM lifecycle, scene JSON, registry, shell, the UI tick
  Core      config, log, build identity, What's New, files, params, settings, lifecycle
  Scene     what is in the scene: trees, their materials, the verdicts, the looks, the leaf shader
  Effects   what is done to it: Shadow Fix, Wind, Falling Leaves
  Ui        UiKit, UiShell, FeatureView, Diagnostics, one panel class per category
```

Dependencies point downward: Ui depends on Effects and Scene, Effects on Scene, Scene on
Core, and Scene holds one thing of the Effects: a `LeafMaterial` carries what the effects
own on it (`Copy`, `FallingMaterial`, `Emitter`) as fields those effects set and clear.
Scene calls no effect; the registry's release hook (`ReleaseEffects`) is a delegate
`Lifecycle` sets. `Lifecycle` lives in Core and is the one class that calls Scene and
Effects, in order. Effects never reference each other; what one needs of another,
`Lifecycle` hands it as arguments (Falling Leaves takes Wind's values).

## 2. Core

| Class | Role |
|---|---|
| `FoliageFXConfig` | identity, schema, cadences, string keys, the leaf rules' thresholds, the atlas and particle constants, feature and page ids, layout metrics, palette |
| `BuildInfo` | the release number (`FoliageFXConfig.Version`, "dev" in the repo, stamped by the packager) and the build identity from build.json; `Line()` for the Overview, `Stamp()` for the scene ("3", or "dev 3 main abc1234 modified"); a development build counts toward build.json's `release`, else `SaveHistory.Current()`, else 1 |
| `Changelog` | the What's New text the Overview shows, one `ChangelogEntry` (version, title, body) per release, made with `Entry` |
| `OverlayLog` | status + VaM log; `Run` / `RunOnce` / `RunWarn` / `SafeRun` / `Probe` error boundaries; a short history and the last error for the report (Live Skin's, verbatim) |
| `PluginFiles` | the plugin's folder (`PluginDir`), `IsPackaged`, `EnsureDir`, and `DiagnosticsFolder`, the only place the plugin writes |
| `ParamSet` | the parameter registry: every storable as a `ParamEntry` with its feature id, scope and key; write, read and reset by scope and feature; `NonDefault` for Diagnose |
| `SaveHistory` | what each release adds to saved data, one `SaveLine` (release, kind, key) per key (none for version 1); `Current()` |
| `Settings` | the scene block, its schema stamp and migration |
| `Lifecycle` | the master, the per-frame poll of the effects' values, the scan tick, the effect order, release, timing |

## 3. Scene

| Class | Role |
|---|---|
| `LeafRules` | which materials are shadowless leaves, and the sentence that says why ("Standard shader in Fade mode: never receives shadows."); `IsOurs` for a material another instance made |
| `LeafLook` | one material's settings (mode, tint, hue, saturation, brightness, cutoff, smoothness, translucency, double-sided, falling) and their JSON |
| `TreeLooks` | every tree's looks by tree key and material name, and each tree's switch: the scene's `trees` node |
| `LeafMaterial` | one source material on a tree: its verdict, its look, its kind (original, leaf, tint), the renderer slots it holds, and what the effects own on it (`Copy`, `FallingMaterial`, `Emitter`); `Assign` writes a material into every slot, `Restore` puts the source back where the copy still is |
| `Tree` | one CustomUnityAsset atom: its loader (hooked, so a loaded or cleared asset marks it dirty), its materials gathered from the loader's MeshRenderers and SkinnedMeshRenderers, its ground, kept as an offset from the loader so that falling leaves made after the tree is moved land at the moved height |
| `TreeRegistry` | finds the trees, decides precedence and scope, runs the scan, follows renames |
| `LeafShader` | `FoliageFX.assetbundle` and its one shader, `Vampix Collections/FoliageFX/Leaves` |

## 4. Effects

| Class | Role |
|---|---|
| `ShadowFix` | converts and restores: each material's kind, the copy made or dropped, the look written into it; soft edges; the global switches (All Atoms, Convert Other Shaders) |
| `Wind` | the sway on every converted material, as shader values on the copy |
| `LeafSprites` | leaf shapes out of a texture into an atlas; UnityEngine and `FoliageFXConfig` only, so it compiles outside VaM |
| `LeafEmitter` | the particle systems of one material's falling leaves: falling and resting; UnityEngine and `FoliageFXConfig` only |
| `FallingLeaves` | the feature: one sprite job per source texture, one emitter per dropping material, the leaf material; `Tick`, on the scan, has the trees waiting on a stalled job applied again, which starts it anew |

## 5. Ui

| Class | Role |
|---|---|
| `UiHost` | the two columns inside VaM's full-width area (Live Skin's) |
| `UiKit` | the control factory and palette; `RackRow` with `SetBadge`, live lines, hint boxes, the scroll box, `Wire` error boundaries |
| `UiShell` | categories, racks, feature selection, inspector building, `Attach` after VaM rebuilds the panel, `OnApplied` after each `Apply` |
| `FeatureView` | one rack row, its inspector bags, state, caption and cost delegates, guide sentence, conditional groups |
| `ConfirmBox` | the branded modal; `InformRows` for the Full Changelog |
| `ReportRow` | one row of that modal |
| `SmoothScroll` | eased wheel scrolling (the What's New box, the modal) |
| `Diagnostics` | the Diagnose report: to the Message Log, the clipboard, and a file in `DiagnosticsFolder` |
| `TestingPanel` | Technical Info and Show Hints |
| `OverviewPanel` | identity card, What's New, the Diagnostics card, the dashboard of trees and effects |
| `TreesPanel` | the live tree rack and the tree editor |
| `EffectsPanel` | the three effect inspectors |

## 6. Ownership

One owner for each thing the plugin makes or changes, and that owner gives it back.

- `LeafMaterial` owns the renderer slots (the renderers and slot indices that held its
  source) and the restore: `Restore()` puts the original back in every slot that still
  shows this instance's copy, and leaves a slot another FoliageFX has filled since.
- `ShadowFix` owns the copy: a leaf copy (a new material from the source, the name plus
  ` (FoliageFX)`, the leaf shader, `_NORMALMAP` when the source has a bump map, RenderType
  TransparentCutout, the AlphaTest queue) or a tint copy (the source with only `_Color`
  changed). `Release(LeafMaterial)` puts the source back (`Restore`), then destroys the
  copy.
- `Wind` owns nothing: it writes its values onto ShadowFix's copy.
- `FallingLeaves` owns each material's emitter and leaf material, and the sprite jobs: one
  per source texture, shared by every material using it, its result (`LeafSprites`)
  destroyed on `ReleaseAll` and when the texture is gone.
- `LeafShader` owns the bundle, released when the plugin is destroyed.
- `TreeRegistry` owns the trees and their loader hooks. Before a tree is released it calls
  each effect's `Release(LeafMaterial)` for every material; `Tree.Release` then puts the
  source back wherever a failed hook left a copy.

The exit paths, each of which ends with the originals in place: disable (`Hold`), destroy,
master off (`SetMaster(false)`), Shadow Fix off, a tree switched off, a tree gone (the scan
releases and unhooks it), an asset cleared or replaced on the atom (the loader's callbacks
mark the tree dirty and it is gathered again), a change of precedence (the tree is
released and marked dirty), Rescan (release everything, scan, re-apply), and a caught
exception.

## 7. The frame

`Lifecycle.Update()`:

```
every frame:
  PollSwitches                     the effects' switches and sliders against the values last
                                   applied (a dozen comparisons, no allocation): a change marks
                                   every tree; Convert Other Shaders has every tree read again
  PollShader                       the leaf shader reference; its arrival has every tree read again
every SceneScanSeconds (2 s):
  Trees.Scan(AllAtoms)             new CustomUnityAsset atoms tracked, gone ones released and
                                   unhooked, precedence re-checked; every CustomUnityAsset
                                   unless the plugin sits on one with All Atoms off
  MSAA                             QualitySettings.antiAliasing > 1; a change marks every tree
  Falling.Tick                     a leaf-shape job that has not answered within
                                   SpriteJobTimeoutSeconds (15 s): the trees waiting on it
                                   marked dirty, and their apply drops it and starts it again
for each tree with Dirty (every tree after TouchAll or a change found above):
  Dirty cleared; a tree yielded to another FoliageFX stops here
  Gather                           first, when its renderers are stale (Tree.NeedsGather)
  with the master on:
    ShadowFix.ApplyTree(tree, shader, msaa)
    Wind.Apply(material)           for each material
    Falling.ApplyTree(tree, host, shader, wind, msaa, softEdges)
each step in log.RunOnce; each effect's steps timed into its cost average and peak
```

`Touch(tree)` marks one tree for the next Update (an edit in the tree editor, a row's
switch); `TouchAll` marks every tree and has each material fetch its look again (an
effect's Reset to Defaults, a scene restore). The effects' storables carry no callbacks of
ours: the poll is what sees a slider, Timeline or a trigger move them. Once every tree is
applied a frame does those comparisons and nothing else: the sway is the shader's and the
falling leaves are Unity's particle systems.

The facade: `Init` creates the status storable, the log and `BuildInfo.Load`, then the
`ParamSet`, the shell, the panels' and the effects' storables (`trees.Create` included),
the `Lifecycle` with the three effects wired, and attaches it to the containing atom
(`Lifecycle.Attach`: on a CustomUnityAsset, All Atoms defaults to off; on any other atom, or
as a session plugin, the switch is hidden and ignored, so a trigger setting it changes
nothing, and every CustomUnityAsset is looked after); then `Diagnostics`, the leaf shader
request, `BuildUi`, the master (`SetMaster` from `Enabled`), the rename hook, and
`AttachSoon`. `Update` runs `Lifecycle.Update`, then the UI tick every `UiRefreshSeconds`
(0.5 s), `RefreshUI`: `shell.Attach`, the switches (Technical Info and Show Hints),
`shell.Apply`, whose `OnApplied` runs the Trees page's sync (it creates rows for new trees
and hands them to the dashboard), then the Overview's refresh while it is the shown page.
`OnDisable` holds (everything released), `OnEnable` resumes, `OnDestroy` releases and lets
go of the bundle. VaM's atom rename handler calls `TreeRegistry.OnAtomRenamed`,
`TreeLooks.Rename` and `TreesPanel.Renamed`.

## 8. The effect contract

No interfaces in the sandbox, so this is a documented shape every effect class follows:

```
Create(ParamSet)            register its storables: feature id, scope, key; none with a callback
ApplyTree(Tree, ...)        bring one tree to what the settings say: make, update or drop what it owns
  or Apply(LeafMaterial)    the per-material form, for an effect that owns nothing (Wind)
Release(LeafMaterial)       give back what it owns on one material; safe to call twice
State(...)                  the rack row's word and the inspector's Status line
Describe(StringBuilder)     one clause for Diagnose
```

An effect takes what `Lifecycle` hands it (the leaf shader, MSAA, another effect's values)
and never reads another effect. Each call runs in its own `log.RunOnce`. Its UI is one
`FeatureView` on the Effects rack, built per the checklist in `docs/UI_DESIGN.md`.

## 9. Persistence

- **Registry**: `ParamSet` holds every storable with a feature id and a scope bit (`Look`,
  `Scene`, `Ui`, `Volatile`, plus the `Private` flag). The scene stores `Look`, `Scene`
  and `Ui`; `Volatile` (the tree editor's controls, which mirror the selected material's
  look) is never saved. An effect's Reset to Defaults resets its `Look` values; a `Scene`
  one (Shadow Fix's All Atoms) stays.
- **Keys**: every parameter is registered with a stable key (`<feature>.<param>`, snake
  case) that the scene stores values under; storable names and labels are free to change.
  Unknown keys are skipped, missing keys take the plugin default. Floats are written in
  the invariant culture.
- **Scene**: storables are registered with VaM (Timeline, triggers) but VaM does not store
  them; the facade's `GetJSON` writes one block (`Settings.SceneBlock(set, looks.Write())`,
  with `needsStore`) into the plugin's node, and `RestoreFromJSON` reads it back
  (`Settings.ReadScene`, migrating first), hands the `trees` node to `TreeLooks.Read`,
  touches every tree and re-applies the shell. `LateRestoreFromJSON` is guarded the same.

  ```
  "FoliageFX": {
    "FoliageFXSchema": 1,
    "version": "dev 1 main abc1234",
    "params": { "plugin.enabled": true, "shadowfix.enabled": true, "wind.direction_deg": 0, ... },
    "trees": { "Sakura 1": { "enabled": true, "materials": { "g2Mat 1": { "mode": "auto",
               "tint": "0,0,1", "hue": -70, ... "double_sided": true, "falling": true } } },
               "self": { ... } }
  }
  ```

- **Trees**: `TreeLooks.Write` keeps only customised looks (a look the user changed) and
  trees switched off. A tree's key is its atom's uid, or `self` for the atom the plugin
  sits on, so those looks survive a rename and travel with the atom. A material's name is
  unique within its tree (a duplicate gets " #2"). A look's keys are `mode` (a chooser id:
  `auto`, `convert`, `keep`), `tint` ("h,s,v", invariant), `hue`, `saturation`,
  `brightness`, `cutoff`, `smoothness`, `translucency`, `double_sided`, `falling`. A
  material the node does not name gets a fresh look from its source (Auto, white, hue 0,
  saturation 1, brightness 1, cutoff 0.5, the source's smoothness else 0.2, translucency
  0, double-sided, falling). `TreeLooks.Read` replaces everything.
- **Rename**: VaM's rename handler moves the tree's key in the registry and in the looks,
  so its saved looks follow the new uid. A row's id is fixed, so the Trees page gives the
  renamed tree a new row and hides the old one; an editor open on it reopens on the new
  row, without the material last picked on the old one.
- **Atoms that leave**: when the scan drops the tree of an atom that has left the scene,
  its looks are forgotten with it (`TreeLooks.Forget`), so a session plugin does not carry
  one scene's looks into the next, and a new atom reusing a deleted uid starts clean.
- **Empty restore**: a restore whose node carries nothing (empty, or the storable's id
  alone) is ignored by both `RestoreFromJSON` and `LateRestoreFromJSON`. That is VaM's
  shape for an atom's Reset, which restores every storable from an empty node with
  missing-means-default and would otherwise put every value back to default and drop
  every look.
- **Schema**: `FoliageFXConfig.Schema` (1, saved as `FoliageFXSchema`) bumps only when
  `Settings.MigrateStep` rewrites saved data (a key rename, a value rewrite); one case per
  bump. Adding a parameter never bumps it. Stored values are built so that day stays far
  off: units in the key (`_mm`, `_s`, `_deg`), chooser ids rather than the words a popup
  shows.
- **No presets**: settings and looks save with the scene only, by decision.

## 10. Directory layout

```
FoliageFX/
  FoliageFX.cslist          dependency order; facade last
  FoliageFX.assetbundle     built by tools/bundle.ps1, never by hand
  Types/
    Core/                   BuildInfo Changelog FoliageFXConfig Lifecycle OverlayLog ParamSet PluginFiles SaveHistory Settings
    Scene/                  LeafLook LeafMaterial LeafRules LeafShader Tree TreeLooks TreeRegistry
    Effects/                FallingLeaves LeafEmitter LeafSprites ShadowFix Wind
    Ui/                     ConfirmBox Diagnostics EffectsPanel FeatureView OverviewPanel ReportRow SmoothScroll TestingPanel TreesPanel UiHost UiKit UiShell
    FoliageFX.cs            facade
  shader/                   Leaves.shader, Editor_BuildFoliageFXBundle.cs, bundle.sources (docs/SHADER_BUILD.md)
  tools/                    setup.ps1 lint.ps1 compile.ps1 bundle.ps1 build-info.ps1 pack.ps1, hooks/, Strip/
    render/                 Editor_RenderTrees.cs, render.ps1: tree bundles rendered before and after
                            the conversion, in the bundle's Unity project (docs/SHADER_BUILD.md)
  packaging/                meta.template.json
  .github/workflows/        ci.yml
  docs/                     ARCHITECTURE.md UI_DESIGN.md TESTING.md PLAN.md SHADER_BUILD.md
```

## 11. Patterns

Facade; RAII ownership (one owner per thing made, restored on every exit path); Observer
via dirty flags; Command for Reset and Copy; registries (`ParamSet` for settings,
`TreeRegistry` for trees); Master-detail for the panel. No DI container, event bus, or
reflection wiring.
