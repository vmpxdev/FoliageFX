# FoliageFX

Virt-A-Mate plugin. Tree and plant CustomUnityAssets often ship their leaves on an
alpha-blended material, and Unity's built-in pipeline never shadows those: the leaves stay
fully lit in shade and look like they glow. FoliageFX gives them a cutout leaf shader that
casts and receives shadows, with soft edges under MSAA, recolouring and a wind sway, and
can drop falling leaves cut from each tree's own texture. The asset's own materials are
never edited.

**Product:** FoliageFX (Vampix Collections)
**Load on:** any atom, or as a session plugin
**Load file:** `Custom/Scripts/Vampix Collections/FoliageFX/FoliageFX.cslist`
**Package:** `VampixCollections.FoliageFX.<N>.var`; the var number is the version

## Status

First release in preparation. The plugin finds every CustomUnityAsset in the scene, judges
each material on it, assigns a copy on its own leaf shader to every leaf material that
never receives shadows, and puts the originals back whenever it lets go. The panel has
three categories (Overview, Trees, Effects): a live rack of the scene's trees with a
material editor, and the three effects, Shadow Fix, Wind and Falling Leaves. Settings save
with the scene under stable keys with a schema stamp.

Out of the box Shadow Fix is on, which is what the plugin is added for; Wind and Falling
Leaves are off.

## Load in VaM

1. Add the plugin to any atom (Add Plugin), or under Session Plugins.
2. Select `FoliageFX.cslist`.
3. A FoliageFX on a CustomUnityAsset looks after that one (Shadow Fix's All Atoms extends
   it to the whole scene); on anything else, or as a session plugin, every
   CustomUnityAsset in the scene, and All Atoms is hidden and ignored (a trigger setting it
   changes nothing). A CustomUnityAsset whose atom carries its own FoliageFX is left to
   that one.
4. Overview shows the status and every tree. With Technical Info on, **Diagnose** writes a
   report to VaM's Message Log, to the clipboard, and to a file under
   `Saves/PluginData/Vampix Collections/FoliageFX/Diagnostics/`, the only file the plugin
   writes.

## The panel

| Category | What lives there |
|---|---|
| Overview | identity card with the global `Enabled`, the credit line and Copy Patreon Link; the What's New scroll box and Full Changelog; Diagnostics (the status line, "4 trees · 7 leaf materials converted"; Rescan, Technical Info, Show Hints; with Technical Info on: the cost caption and its hints, and Diagnose), and a dashboard: a Trees card with every tree's state and switch, an Effects card with every effect's, click to open |
| Trees | a rack of the scene's CustomUnityAssets, rows appearing and going as atoms are added and removed, each with its state and a switch; the tree editor on the right: the tree's `Enabled` and status, Materials (Material, the material's shader and verdict, Mode), Look (Tint, Hue Shift, Saturation, Brightness, Smoothness), Leaves (Alpha Cutoff, Double-sided, Translucency, Drop Falling Leaves), then Reset Material, Copy Look to Tree, Copy to Same Material Everywhere, and Reset to Defaults |
| Effects | a rack of Shadow Fix, Wind and Falling Leaves, each with its inspector: Shadow Fix (Soft Edges; All Atoms when the plugin sits on a CustomUnityAsset; Convert Other Shaders under Advanced), Wind (Strength, Speed, Direction), Falling Leaves (Rate, Size, Fall Speed, Time On Ground, Cast Shadows) |

Select a tree in the rack to open it on the right. Its Material popup lists every material
on the asset with what was done to it ("converted", "original", "kept original"), and the
line under it says which shader and queue the material is on and why it was or was not
converted. Mode overrides the verdict for one material: Auto converts leaves that never
receive shadows, Convert forces it for a material Auto missed, Keep original leaves it
alone. Every change applies to that tree at once. Copy Look to Tree gives the look to every
converted material of the tree (each keeps its mode); Copy to Same Material Everywhere gives
it, mode included, to the material of that name on every tree.

A tree whose atom has its own FoliageFX shows "own FoliageFX" and an amber badge in its row,
and no switch: that instance takes precedence.

Every effect setting is a plain JSONStorable, so Timeline, triggers, and other plugins can
drive it whether or not it is visible; panel state, Technical Info, Show Hints and the tree
editor's controls (which mirror the selected material) are kept off those lists. Storable
names follow `<Feature> <Param> <unit>`; inside its own inspector a control shows the short
label (`Direction (deg)`).

## Scenes

Scenes save everything: the effect settings, the switches, the panel state, and every
tree's switch and customised looks, in one block in the plugin's node. Values are stored
under stable keys, never under the names shown in the panel, and the block carries a schema
number so older saves migrate; a value a scene does not carry takes the plugin default, and
one this version does not know is skipped. A tree's looks are keyed by its atom's uid and
follow a rename; on the atom the plugin sits on they are keyed `self` and travel with it.
There are no presets, by decision: a look belongs to a tree in a scene.

## Docs

- `AGENTS.md`: how to work in this repo
- `CHANGELOG.md`: the developer log (not shipped; the user-facing notes are `Types/Core/Changelog.cs`)
- `docs/ARCHITECTURE.md`: layers, ownership, frame order, effect contract, persistence
- `docs/UI_DESIGN.md`: panel model, categories, copy rules, persistence scopes, palette, the add-an-effect checklist
- `docs/PLAN.md`: what ships, what is out of scope, what comes next
- `docs/TESTING.md`: in-game checks and the release matrix
- `docs/SHADER_BUILD.md`: building `FoliageFX.assetbundle`

## Development and releases

The repository is the whole project: sources, the shader, docs, tools, packaging. What
ships is chosen by the packager, never by git. One-time setup after cloning:

```
pwsh tools/setup.ps1
```

This points git at `tools/hooks` and writes `build.json`. From then on:

- `git commit` lints and compiles against VaM first (pre-commit); a commit that does not
  compile never happens. A commit that touches `shader/` also gets its bundle: when
  `FoliageFX.assetbundle` is out of date, Unity 2018.1.9f1 rebuilds it in batch mode
  (`tools/bundle.ps1`) and the bundle and `shader/bundle.sources` are staged with the commit.
- `git push` builds the var into a temp folder first (pre-push): comments stripped with
  Roslyn (`tools/Strip`), the stripped sources compiled. GitHub cannot compile (no VaM
  assemblies there), so this is where the shipped form is proven.
- Every commit, checkout, and merge rewrites `build.json` (ignored by git), so the loose
  install's Overview reads "Development Build · main abc1234", plus "modified" while the
  tree has uncommitted edits.

Commit and push from a terminal (Cursor's integrated terminal is fine). Cursor's Source
Control button runs none of this: its git extension sets `core.hooksPath` to NUL in the
environment of every git it spawns, which outranks the repository config and has no
setting, so a button commit skips lint, compile, the bundle check, and `build.json`.
CI still refuses a push that does not lint or has a stale bundle, but only after the fact.

Versioning is VaM's: the var number. Tags are `v1`, `v2`, `v3`; the tag number is the var
number is the version the Overview shows ("Version 1"). `FoliageFXConfig.Version` is
`"dev"` in the repo and stamped by the packager, never edited by hand. Scenes record
`BuildInfo.Stamp()`: "3", or "dev 3 main abc1234 modified" from a development build of
release 3, so a scene can date the build that saved it. A development build counts toward
the release after the newest vN tag: `tools/build-info.ps1` writes it into build.json as
"release" beside the branch and commit.

Packaging (`tools/pack.ps1`): stages the cslist and the stripped sources under
`Custom/Scripts/Vampix Collections/FoliageFX/`, adds `FoliageFX.assetbundle` (refused when
it does not match `shader/`), and on a dev build `build.json` (a release carries only its
number), writes `meta.json` from `packaging/meta.template.json` (license PC; the What's New
entry appended to the description), zips `VampixCollections.FoliageFX.<N>.var`
(`VampixCollections` is the filename creator of every Vampix package), then checks it
(every cslist entry present and no source outside it, nothing from the deny list, no
`Saves/` entry, Version stamped, comments stripped, meta.json valid). `-Install` copies it
into AddonPackages. `-Tag v1` is a release build and refuses without `Entry(1, ...)` in
`Types/Core/Changelog.cs`. A dev build is its own package,
`VampixCollections.FoliageFX-dev.<commits>.var`, numbered by the commit count, with Version
"dev", so it never collides with a release.

GitHub Actions (`.github/workflows/ci.yml`, Linux runner): every push lints, packages, and
keeps the var as an artifact; a `v<N>` tag also opens a release draft with the var and the
notes. Releasing: write the What's New entry, push, install and test the artifact, `git tag
v1 && git push --tags`, publish the draft, upload to the Hub. A hotfix for a released
version branches from its tag, tags the next number there, and merges back.

The GitHub repository is `vmpxdev/FoliageFX`, beside Live Skin's `vmpxdev/Live-Skin`, and is
added the same way, once:

```
git remote add origin git@github.com-vmpxdev:vmpxdev/FoliageFX.git
git push -u origin main
```

The bundle is built by `tools/bundle.ps1` (Unity 2018.1.9f1 in batch mode; docs/SHADER_BUILD.md)
and committed with `shader/bundle.sources`, the hashes of what it was built from; the packager
and CI refuse a bundle that does not match `shader/` (`tools/bundle.ps1 -Check`, no Unity needed).
