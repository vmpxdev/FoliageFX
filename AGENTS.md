# FoliageFX

Virt-A-Mate plugin: leaves that take shadows. Product **FoliageFX** (Vampix Collections), class
`FoliageFX`, load `FoliageFX.cslist` on any atom or as a session plugin (`Types/` in
dependency order, `Types/FoliageFX.cs` last). First release in preparation; versions are VaM
var numbers (`v1`, `v2`).

Before changing code, read:

1. `docs/ARCHITECTURE.md`: layers, ownership, the frame order, the effect contract, persistence
2. `docs/UI_DESIGN.md`: the design system every page follows, the copy rules, the scope table
3. `docs/TESTING.md`: the in-game checks and the release matrix
4. `docs/PLAN.md`: what ships, what is deliberately out, what comes next
5. `.cursor/rules/`: constraints that always apply
6. `README.md`: the development and release pipeline

## The rules that make this plugin safe

- **The asset's own materials are never edited.** A converted or tinted material is a copy
  (`new Material(source)`, its name ending in `FoliageFXConfig.CopySuffix`) written into the
  renderer slots; the source stays exactly as the asset loaded it.
- **Every copy is restored on every exit path**: disable, destroy, master off, Shadow Fix
  off, a tree switched off, a tree that leaves the scene or loads another asset, a change of
  precedence, Rescan, a caught exception. The slots get their source back, then the copy is
  destroyed; falling leaves' emitters and leaf materials go with it.
- **Off is identical to no plugin.** Master off, Shadow Fix off, a tree switched off: the
  original material in every slot, no copies, no emitters, no leaked GameObjects.
- **A FoliageFX on the atom itself wins.** A CustomUnityAsset whose atom carries its own
  FoliageFX is that instance's tree. A scene-wide instance leaves it alone, lists it as
  handled elsewhere, and takes it back when that instance is removed. Two scene-wide
  instances are not a supported setup: a FoliageFX on the atom wins, otherwise the first
  to convert a material keeps it, and the other lists it as "other FoliageFX".

## How code is organized

- `FoliageFX` is a thin facade: VaM lifecycle, storable registration, scene JSON, page routing.
- `Lifecycle` owns the master, the per-frame poll of the effects' values, the scan tick,
  the effect order and release. New steps are added there, in the documented
  order, never elsewhere.
- The Scene classes model what is in the scene: `TreeRegistry` finds the CustomUnityAsset
  atoms and decides precedence and scope; a `Tree` is one atom and its loader; a
  `LeafMaterial` is one source material on it with its verdict (`LeafRules`), its look
  (`LeafLook`, kept by `TreeLooks`), its renderer slots, and what the effects own on it
  (`Copy`, `FallingMaterial`, `Emitter`, fields those effects set and clear); `LeafShader`
  loads the bundle.
- Every effect is a sealed class with the same shape: `Create(ParamSet)`,
  `ApplyTree(Tree, ...)` or `Apply(LeafMaterial)`, `Release(LeafMaterial)` for what it
  owns, `State` for its row, `Describe(StringBuilder)`. Its storables are registered
  through `ParamSet` with a feature id, a scope, and a stable key; its UI is one rack row
  plus one inspector, built per the checklist in `docs/UI_DESIGN.md`.
- The tree editor's callbacks set dirty flags (`Lifecycle.Touch` for one tree, `TouchAll`
  for every tree); the effects' storables carry none: every frame `Lifecycle` compares
  their values with the ones last applied. The tree pass does the work. Once every tree is
  applied, a frame does those comparisons (a dozen, no allocation) and nothing else.
- Zero allocation in per-frame paths. Every failure has a specific status through
  `OverlayLog`. `RunOnce` on hot paths.

## Sandbox (VaM compiles this in DynamicCSharp)

No `enum`, `interface`, generic method definitions, `static class`, `System.IO` (even in
comments), `WWW`, XML `cref`, `namespace`, `using static`, `foreach` over a `Dictionary`
(its keys, its values or its pairs: no `KeyValuePair<` but
`KeyValuePair<string, JSONNode>`, which is how a `JSONClass` is iterated), and no `public`
type other than `FoliageFX`. C# 5 only. `tools/lint.ps1` enforces this; `tools/compile.ps1` runs it
and then compiles against the local VaM assemblies. The pre-commit hook runs both.

## Persistence and names

- Settings save with the scene and nowhere else: one block in the plugin's node,
  `FoliageFX: {FoliageFXSchema, version, params, trees}`. There are no presets, by decision.
- Save keys (`wind.direction_deg`, a look's `double_sided`) are the schema: never renamed
  once shipped. A key rename needs a schema bump and a migration step in `Settings`.
- A stored value keeps one meaning: units in the key (`falling.size_mm`), chooser ids
  (`auto`, `convert`, `keep`) instead of display words.
- The `trees` node holds each tree's switch and its customised looks, keyed by the atom's
  uid, or `self` for the atom the plugin sits on; a rename moves the key.
- Every saved key a release adds gets one line in `Types/Core/SaveHistory.cs` in the same
  commit (none for version 1, where everything is new; Volatile keys get none). A release
  build refuses a line past its tag.
- Storable names (`Wind Direction deg`) are what Timeline and triggers list: exactly
  `<Feature> <Param> <unit>`. Panel labels are a third thing (`UiKit.Pretty`).
- Scopes: Look (effect values and masters), Scene (`Enabled`, All Atoms), Ui (panel state),
  Volatile (the tree editor's controls, never saved), plus the Private flag for anything
  that must stay off VaM's storable lists.
- `FoliageFXConfig.Version` is `"dev"` in the repo; the packager stamps it. Never edit it.
- The What's New text is `Types/Core/Changelog.cs`, one `Entry(version, ...)` per release;
  a release build refuses without the entry for its number (`Entry(1, ...)` for the first).
  Every entry is a plain list of features, one short line each, in plain text, the same
  list as `CHANGELOG.md`. No history, no internals, no sign-off.
- `CHANGELOG.md` is a plain list of features per release, one short line each. No
  history, no internals: those belong in commit messages and `docs/`.

## Copy rules

- A control whose label does not say what it does carries one hint line, in the user's
  words, saying what it does and which way is more; self-explanatory controls carry none.
- A card's story box is at most three short lines that say what the guide and the hints do
  not. Each fact lives in one place. Long text under a card goes unread.
- Speak in VaM terms: atoms, CustomUnityAssets, session plugins.

## Process

- Original code only. Decompiled VaM source is for local API inspection; never paste it.
- Work on `main`; tags are releases; a hotfix for a released version branches from its tag.
- The user commits and tags. Do not commit, stage, or tag unless asked; a commit message
  covers only what is uncommitted since the last commit.
- Docs are tracked but never shipped; `tools/pack.ps1` chooses what goes in the var.
- The bundle is never edited by hand: after a change in `shader/`, `pwsh tools/bundle.ps1`
  rebuilds it (the pre-commit hook does it on commit) and records `shader/bundle.sources`.
