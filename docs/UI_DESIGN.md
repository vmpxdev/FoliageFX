# FoliageFX UI design

The Live Decals look (dark surfaces, violet accent, bold titles, small hints) on Live Skin's
panel model: **fixed categories, a rack, one inspector.** Categories never change. FoliageFX
has two racks: the Effects rack is fixed, and the Trees rack is the scene's
CustomUnityAssets, its rows made and hidden as atoms come and go. Adding an effect adds one
row and one inspector; adding a tree to the scene adds one row; neither adds a tab, a page,
or a column of sliders.

## 1. Principles

1. **Master-detail.** The left column lists effects or trees (the rack); the right column
   shows the selected one (the inspector, or the tree editor). Unity's Inspector, Blender's
   Properties editor, a DAW rack.
2. **Stock controls for every parameter.** Sliders, toggles, and popups are VaM's own,
   bound to registered storables, created once at Init. Timeline and triggers see every
   effect setting regardless of what is visible. The tree editor's controls are the one
   exception: they mirror whichever material is selected, so they are kept off VaM's lists.
3. **Custom widgets only for navigation, grouping, status, and actions**: the category bar,
   rack rows, titles, captions, hint wells, cards, button rows.
4. **Progressive disclosure.** A handful of controls per inspector; anything a user rarely
   needs behind that effect's Advanced toggle (Shadow Fix's Convert Other Shaders).
   Advanced values are still storables.
5. **One accent per feature**: its master toggle. Everything else is neutral.
6. **Visible system status in words.** A live state on every rack row, a live caption in
   every inspector, the status line on Overview. Warnings and errors go to VaM's Message
   Log; plain confirmations show in the relevant caption.
7. **Only the fix is on by default.** Adding the plugin converts the shadowless leaves,
   which is what it is added for; Wind and Falling Leaves change nothing until switched on.
8. **Same skeleton everywhere.** Learn one inspector, know them all.

## 2. Layout

The Trees page:

```
| [Overview] [Trees] [Effects]                                                  |  category bar
|---------------------------------------+---------------------------------------|
| TREES                                 | Sakura 1                              |  editor title: the tree's uid
| +-- Trees -------------------------+  | [x] Enabled                 (accent)  |  the tree's switch
| | [Sakura 1] 3 of 5 converted  [x] |  | Status: 3 of 5 converted              |  live caption (top)
| | [Sakura 2]   nothing to fix  [x] |  | Leaves that never receive shadows     |  guide
| | [Ancient ]  own FoliageFX !      |  | are converted. Pick a material ...    |
| | [Fern    ]          loading  [x] |  | +-- Materials ---------------------+  |  card
| | [Bush    ]              off  [ ] |  | | Material  [g2Mat 1 [converted]]  |  |  filterable popup
| +----------------------------------+  | | Standard, queue 3000. Standard   |  |  live line: shader, queue,
|                                       | | shader in Fade mode: never ...   |  |  the verdict's reason
|                                       | | Falling leaves: off for this ... |  |  falling leaves line
|                                       | | Mode                [Auto     v] |  |
|                                       | +-- Look --------------------------+  |  card
|                                       | | Tint                 [ picker ]  |  |
|                                       | | Hue Shift (deg) ====o-----    0  |  |
|                                       | | Saturation      =====o----    1  |  |
|                                       | | Brightness      =====o----    1  |  |
|                                       | | Smoothness      ==o-------  0.2  |  |
|                                       | +-- Leaves ------------------------+  |  card
|                                       | | Alpha Cutoff    ====o-----  0.5  |  |
|                                       | | [x] Double-sided                 |  |
|                                       | | Translucency    o---------    0  |  |
|                                       | | [x] Drop Falling Leaves          |  |
|                                       | +----------------------------------+  |
|                                       | [ Reset Material ] [ Copy Look ]      |  Reset Material, Copy Look to Tree
|                                       | [ Copy to Same Material Everywhere ]  |  Copy to Same Material Everywhere
|                                       | [ Reset to Defaults ]                 |  footer: the whole tree
|                                       | Every material of this tree back ...  |  its hint
```

The Effects page is Live Skin's skeleton: the fixed rack (Shadow Fix, Wind, Falling Leaves)
on the left, the selected effect's inspector on the right (title, `Enabled`, the Status
line and guide, its cards, Show Advanced where it has an Advanced card, Reset to Defaults).

- **The host** (`UiHost`, lifted from Live Decals through Live Skin): VaM's stock two
  columns are not used. Every element is still created through VaM's `Create*` calls, then
  moved into one stretching vertical host inside VaM's full-width area. Host-level items
  are the category bar and one `SplitPane` per category, sized from its visible children.
  `UiShell.Attach` re-runs from `Init`, after layout, and on the slow tick, because VaM
  rebuilds the panel when it opens.
- **Category bar**: three buttons, label plus hint, the active one in AccentPressed.
- **Rack row**: select button (AccentPressed when selected), live state label, on/off
  toggle. On the Effects rack the toggle is the effect's master. On the Trees rack it is
  the tree's switch: a per-tree proxy storable (`Fix <uid>`, never registered) that writes
  `TreeLooks.SetEnabled` and touches the tree. A tree whose atom has its own FoliageFX
  shows the amber badge in its state box, with "This atom has its own FoliageFX, which
  takes precedence." in its tooltip, and no switch.
- **The live rack**: `TreesPanel` keeps a pool of `FeatureView`s keyed by uid. A new
  CustomUnityAsset gets one through `shell.AddFeature` (id `tree:<uid>`), its row built
  with `kit.RackRow` into the rack card (`kit.Split` set to the Trees pane and
  `kit.Section` to the rack card), then `shell.Attach` and `shell.Apply`; a tree that
  leaves has its row hidden. A row's id is fixed, so a renamed atom gets a new row and its
  old one is hidden; an editor open on it reopens on the new row (`TreesPanel.Renamed`),
  without the material last picked on the old one. With no CustomUnityAsset in the scene
  the rack shows one caption: "No CustomUnityAsset atoms in the scene. Add a tree or plant
  asset and it appears here."
- **The tree editor**: one shared editor in `TreesPanel`'s own bag, shown while the
  selected feature is a tree. Its title is the tree's uid; then the tree's `Enabled` (the
  same proxy as the row's switch), "Status: <state>" and the guide; the Materials, Look
  and Leaves cards; two rows of buttons, Reset Material and Copy Look to Tree, then Copy to
  Same Material Everywhere; and Reset to Defaults with its hint. Its controls are
  `ParamSet` entries (feature `tree`, Volatile and Private, storable names `Tree <Param>
  <unit>`, prefix `Tree`). Selecting a tree or a material pushes the look into them with
  `valNoCallback`; a change writes the look, marks it customised, and calls
  `Lifecycle.Touch(tree)`. The selection is refreshed in `shell.OnApplied`, which runs at
  the end of every Apply, the slow tick's included.
- **Inspector**: title, `Enabled` master toggle, the live caption (at the top, so the
  status is read without scrolling), then **cards**: one dark well per functionality area
  (Look, Scope, Wind, Leaves), an optional Show Advanced toggle with an Advanced card, and
  Reset to Defaults. Built by `UiShell.BeginInspector(view, prefix)`,
  `kit.BeginSection(title)` per card, optional `BeginAdvanced`, then `EndInspector`.
- **Cards** (`UiSection`, `UiKit.BeginSection`/`EndSection`): the same well as Live Decals'
  section cards. Hint wells inside a card use `Surface` so they still read as hints.
- **Conditional groups**: `FeatureView.When(predicate)` returns a bag shown only while the
  predicate holds and the feature is selected: Shadow Fix's Scope card shows only when
  the plugin sits on a CustomUnityAsset. Anywhere else All Atoms is hidden and ignored: a
  trigger setting it changes nothing, and every CustomUnityAsset is looked after.
- **Labels**: storable names keep the `<Feature> <Param> <unit>` form; inside its
  inspector a control shows `UiKit.Pretty(name)`: the feature prefix dropped, units in
  parentheses (`Direction (deg)`, `Size (mm)`, `Fall Speed (m/s)`). Category hints and
  button labels are Title Case.
- **Navigation cues**: rack rows and category tabs lift one shade under the pointer
  (`RowHover`, `Accent` on a selected one).
- Overview has no rack; it owns its category bag directly. Its right column is a
  dashboard: a Trees card with one live row per tree and an Effects card with one per
  effect (`RackRow`, synced on the slow tick), click to open.

Everything is created once and shown or hidden by bag (`UiKit.SetBag`); a category's whole
pane is hidden with it. The tree rows are the one thing made after Init, and they are made
once per uid and hidden, never destroyed. Layout groups skip inactive children, so hidden
content leaves no gaps and nothing is rebuilt on navigation.

## 3. Categories

| Category | Contents | Why |
|---|---|---|
| **Overview** ("Status and Trees") | (no rack) identity card: the FoliageFX title, the caption "Vampix Collections · <build line>", the global `Enabled`, the credit tagline, Copy Patreon Link; What's New scroll box and Full Changelog; Diagnostics card: the status line ("4 trees  ·  7 leaf materials converted", "Off  ·  original materials on every tree" with `Enabled` off, "No CustomUnityAsset atoms in the scene", "Loading the leaf shader", or the shader error), replaced with Technical Info on by the cost caption and its hints; Rescan; Technical Info and Show Hints, each with its hint; Diagnose with Technical Info. Right: the Trees card, one row per tree (its state and switch, click opens it), and the Effects card, one row per effect | whether the leaves are fixed, and on which trees, at a glance |
| **Trees** ("Fix and Recolour") | the title card and the Trees rack (one row per CustomUnityAsset); the tree editor: Materials (Material, the live line, the falling leaves line, Mode), Look (Tint, Hue Shift, Saturation, Brightness, Smoothness), Leaves (Alpha Cutoff, Double-sided, Translucency, Drop Falling Leaves), Reset Material, Copy Look to Tree, Copy to Same Material Everywhere, Reset to Defaults | everything about one tree: what was converted and why, and how its leaves look |
| **Effects** ("Shadows, Wind, Leaves") | the Effects rack: Shadow Fix (Look: Soft Edges; Scope: All Atoms, on a CustomUnityAsset only; Advanced: Convert Other Shaders), Wind (Wind: Strength, Speed, Direction), Falling Leaves (Leaves: Rate, Size, Fall Speed, Time On Ground, Cast Shadows) | what applies to every tree |

The caption under an inspector's `Enabled` has one shape: a "Status:" line carrying the
row's own state, then a static guide sentence (`FeatureView.Guide`):

| Inspector | Guide |
|---|---|
| a tree | Leaves that never receive shadows are converted. Pick a material to check or change it. |
| Shadow Fix | Alpha-blended leaves never receive shadows in VaM. Converted to cutout leaves, they cast and receive shadows like the rest of the scene. |
| Wind | A gentle sway on every converted leaf, and the direction falling leaves drift. |
| Falling Leaves | Leaves cut from each tree's own texture drop from the canopy, drift with the wind and lie on the ground for a while. |

With Technical Info on, every inspector caption also carries the effect's cost
(`OnCost = life.CostText(id)`).

The tree editor's Materials card: the Material popup (filterable) lists the materials by
name and shows each as "<name>  [<state>]"; a live line under it reads "<shader>, queue
N. <reason>" and then the falling leaves line; the Mode popup offers Auto, Convert and
Keep original (ids `auto`, `convert`, `keep`). The buttons, in two rows: Reset Material
puts the selected material's look back to its defaults; Copy Look to Tree gives the look
to every converted material of this tree, each keeping its mode; then Copy to Same
Material Everywhere gives it to the material of this name on every tree, mode included.
Below them, Reset to Defaults resets every look of the tree and switches it on.

## 4. Persistence

Every storable is registered through `ParamSet` with a feature id and a scope:

| Scope | Examples | Saved in the scene |
|---|---|---|
| Look | every effect value and effect master: `Shadow Fix`, `Shadow Fix Soft Edges`, `Shadow Fix Convert Other Shaders`, `Wind`, `Wind Strength`, `Wind Speed`, `Wind Direction deg`, `Falling Leaves`, `Falling Leaves Rate`, `Falling Leaves Size mm`, `Falling Leaves Fall Speed m/s`, `Falling Leaves Time On Ground s`, `Falling Leaves Cast Shadows` | yes |
| Scene | `Enabled` (`plugin.enabled`), `Shadow Fix All Atoms` | yes |
| Ui | `Technical Info`, `Show Hints`, the category and the selection (`shell.category`, `shell.selected`), Show Advanced on Shadow Fix (`shadowfix.advanced`) | yes |
| Volatile | the tree editor's controls (`tree.*`): they mirror the selected material's look, which saves in the `trees` node | no |
| Private (flag) | off VaM's storable lists (Timeline, triggers): every Ui entry and the tree editor's controls | as its scope |

- **Look and Scene** both save with the scene; they differ at a reset: an effect's Reset to
  Defaults (`paramSet.Reset(ParamSet.Look, id)`) leaves its Scene values (All Atoms) as
  they are.
- **Keys**: every parameter has a stable key, `<feature>.<param>` in lowercase snake case
  (`shadowfix.soft_edges`, `wind.direction_deg`, `falling.size_mm`). Keys are the schema:
  the scene stores values under them, never under the storable name (what Timeline shows)
  or the panel label, so either can be renamed without touching saved data. A key never
  changes once shipped; a rename is a `Settings.MigrateStep`.
- **Scene**: every storable is registered with VaM (Timeline, triggers) but VaM does not
  store it; `GetJSON` writes one keyed block `FoliageFX: {FoliageFXSchema, version, params,
  trees}` into the plugin's node and `RestoreFromJSON` reads it back (`Settings.SceneBlock`
  / `ReadScene`). The per-tree switches and looks are not storables: they live in the
  `trees` node (`TreeLooks`).
- **Loading rules**: unknown keys are skipped (never fatal, so a newer build's scene loads
  what it can); missing keys take the plugin default (a block is a complete state).
- **No presets**: settings and looks save with the scene only, by decision.
- **Hints under controls**: a slider or toggle whose label does not already say what it
  does carries one muted italic line beneath it (`UiKit.Slider(js, right, bag, hint)`,
  `Toggle(..., hint)`). The line says what the control does and which way is more, in the
  user's words, in about ten words: "How far the leaves move." Never a restatement of the
  label, never a unit or an internal name, no mechanism unless the mechanism is the point.
  Self-explanatory controls (Enabled, Double-sided, Cast Shadows) get none. Show Hints
  (Overview, saved with the panel state) hides every hint line and story box at once.
- **Schema**: adding a parameter never bumps the number; a release that adds a saved key
  gives it a `SaveHistory` line. Renaming a key or rewriting a value bumps
  `FoliageFXConfig.Schema` and adds one `Settings.MigrateStep` case.

## 5. Palette (identical to Live Decals and Live Skin)

| Token | Hex | Use |
|---|---|---|
| Accent | #7C3AED | primary buttons, master toggles |
| AccentPressed | #5B21B6 | active category, selected rack row |
| OnAccent / OnAccentMuted | #FFFFFF / #EDE9FE | text on accent |
| Selected | #5C5280 | reserved for lists |
| Warn / OnWarn | #FAC238 / #291F0A | the rack row badge (a tree with its own FoliageFX) |
| Danger / OnDanger | #C42B3A / #FFFFFF | destructive buttons, error status |
| Button / OnButton | #DFDFDF / #1F1C24 | neutral buttons |
| TabIdle / OnTabIdle | #3A3744 / #EDE8F2 | idle category, idle rack row, rack toggle |
| Surface | #25232C | category bar background, hint wells inside a card |
| PreviewBg / CardBg | #121018 | hint wells, section cards |
| Title | #F2F0F7 | section titles |
| Caption | #C7C2D1 | captions, hints, rack state, normal status |
| Focus | #C4B5FD | warning status |

Typography: title 32 bold; category label 28 bold, hint 15; rack row label 26 (button),
state 20; caption, hints, status 24. Font is VaM's Arial.

## 6. Names and keys

Three names per parameter, each with one job:

| | Example | Who reads it | May change? |
|---|---|---|---|
| Key | `wind.direction_deg` | the scene, migrations | never (migrate instead) |
| Storable name | `Wind Direction deg` | VaM: Timeline, triggers, other plugins | rarely; breaks external references |
| Label | `Direction (deg)` | the panel (`UiKit.Pretty`) | freely |

Storable names follow `<Feature> <Param> <unit>` where `<Feature>` is exactly the master's
label (`Shadow Fix Soft Edges`, `Wind Strength`, `Falling Leaves Size mm`), so Timeline and
trigger lists group an effect's values under its name; masters are the bare effect name
(`Shadow Fix`, `Wind`, `Falling Leaves`). The tree editor's controls are named the same
way under `Tree` (`Tree Hue Shift deg`), though they are kept off the lists. Keys are
registered explicitly with each storable (`set.Float(Id, scope, "direction_deg", ...)`);
the registry rejects a key that is not lowercase snake case or is registered twice.

A look in the `trees` node has keys of its own, schema like the rest: `mode`, `tint`,
`hue`, `saturation`, `brightness`, `cutoff`, `smoothness`, `translucency`, `double_sided`,
`falling`. `mode` stores the chooser id (`auto`, `convert`, `keep`), never the popup's
words; `tint` is "h,s,v" in the invariant culture.

## 7. Live text

Rack states, the dashboard, inspector captions and live lines refresh on the UI tick
(`UiRefreshSeconds`, 0.5 s), only for the visible category, only when the text changed.
Never per-frame string building. The words:

| Where | States |
|---|---|
| a tree's row and Status (`Tree.State`) | "3 of 5 converted", "nothing to fix", "loading", "own FoliageFX", "off" (the tree's own switch), "not converted" (its leaves wait for a fix that is not running: Shadow Fix or the master off, the leaf shader still loading) |
| a material in the popup (`LeafMaterial.StateLabel`) | "converted", "converted, forced", "original", "original, tinted", "kept original", "other FoliageFX" |
| the material's reason (`LeafRules`) | a sentence, such as "Standard shader in Fade mode: never receives shadows.", "Opaque or cutout: already receives shadows." or "Overlay: not foliage."; the live line gives the queue before it, so a reason never repeats it |
| the falling leaves line (`FallingLeaves.MaterialLine`) | "Falling leaves: 15 leaf shapes from the texture.", "Falling leaves: 16 rounded pieces of the texture, which has no separate leaves.", "Falling leaves: no usable leaf shapes in this texture.", "Falling leaves: finding leaf shapes...", "Falling leaves: off on the Effects page.", "Falling leaves: off for this material.", "Falling leaves: only from converted materials." |
| Shadow Fix (`ShadowFix.State`) | "7 materials on 4 trees", "off", "no shader"; "hard edges (MSAA off)" appended when soft edges cannot apply |
| Wind (`Wind.State`) | "off", "0.3 from 90°" |
| Falling Leaves (`FallingLeaves.State`) | "12 falling, 40 resting", "off", "finding leaf shapes" |
| Overview status (`Lifecycle.StatusLine`) | "4 trees  ·  7 leaf materials converted", "Off  ·  original materials on every tree" (`Enabled` off), "No CustomUnityAsset atoms in the scene", "Loading the leaf shader", or the shader error |

## 8. Adding an effect (checklist)

1. A sealed class in `Types/Effects` with the effect contract (`docs/ARCHITECTURE.md`
   section 8) and a feature id in `FoliageFXConfig`.
2. Create its storables through `ParamSet` with the feature id, Look scope, and stable
   keys; storable names `<Feature> <Param> <unit>`.
3. Its step in `Lifecycle`, in the documented order: a cost slot (a constant beside
   `FeatShadowFix`, `FeatCount` raised, its id in `featureIds` and its name in
   `featureNames`), and a delegate for its guarded call, built once in the constructor
   beside `fnShadowFix` and run through `Guard` in `ApplyTree` (inside `log.RunOnce`,
   timed).
4. Its values compared in `PollSwitches` with the ones last applied, in fields beside
   `windStrength`: effect storables carry no callbacks, so this is how a change reaches
   the trees.
5. A line in `ReleaseEffects`, in its own `log.RunWarn`, so the registry has its
   `Release(LeafMaterial)` run before a tree is released.
6. `shell.AddFeature(FoliageFXConfig.PageEffects, id, label, master)` before `BuildRack`
   of Effects.
7. `BeginInspector(view, prefix)`, the guide, one `kit.BeginSection` card per
   functionality area with its controls and their hints, optional `BeginAdvanced`,
   `EndInspector`.
8. Set `OnState`, `OnCost` (`life.CostText(id)`), `OnReset` (`paramSet.Reset(ParamSet.Look,
   id)`, then `life.TouchAll`).
9. The bar, the rack, selection and the scene block need nothing: they already know it.

## Credit and What's New

The Overview's identity card carries the credit the way Live Skin does: the tagline "Sponsor
a coffee. Accidentally fund a feature." and a "Copy Patreon Link" button (VaM plugins
cannot open a browser, so the link goes to the clipboard; the button reads "Copied to
Clipboard" for a moment). Between the identity card and Diagnostics, a "What's New" card
holds the release notes in a scroll box (`UiKit.ScrollBox`). The text is
`Types/Core/Changelog.cs`, curated per release in the user's words, newest first; the
developer CHANGELOG.md is not shipped. Under the box a "Full Changelog" button opens the
same notes in the branded modal (`ConfirmBox.InformRows`, titled What's New, one Close
button), laid out as rows (`OverviewPanel.ChangelogRows`): a release's number in a pill
beside its title, a section's name in colour over a hairline, and bullets with the dot in
its own column and a bold lead.

Every entry is written the same way, as plain text, since `Changelog.Text` is also the
var's description: one sentence on what the release is, then the sections that apply, in
this order, then the sign-off.

| Section | Holds |
|---|---|
| Heads Up | what updating changes in what the user already has (drawn in the warning amber) |
| New Features | what can be done now that could not before; a rework goes here, as what the user can now do |
| Performance | what costs less than it did (drawn in green). A figure goes in only when it was measured for the build that ships and says what it measured, never a frame rate promise |
| Adjustments | what behaves or looks differently |
| Bugfixes | what was broken |
| Known Issues | what ships broken and is known |

A section is its name on a line, then "- " bullets; a bullet opens with a short lead and a
full stop naming the thing, then one or two sentences. About five bullets to a section,
related items merged; thanks go on the bullet that earned them. The modal reads that shape
back: a line with a bullet right under it is a section, and a bullet's first sentence is its
lead when it ends within `Changelog.LeadMax` characters.

## Slider ranges

A slider's end is a real ceiling or the slider is open. A ceiling that another constant or
another slider imposes is either enforced by the range or named in the hint when it moves
with another control. A value the code takes linearly with nothing allocated by it is an
open slider (`UiKit.OpenSlider`). Never a constrained slider whose top the code cannot
reach.

| Slider | Range | Default |
|---|---|---|
| Wind Strength | 0 to 1 | 0.3 |
| Wind Speed | 0.05 to 4 | 1 |
| Wind Direction deg | 0 to 360 | 0 |
| Falling Leaves Rate (per tree, per second) | 0.1 to 30 | 3 |
| Falling Leaves Size mm | 10 to 300, open (its range buttons go past 300) | 50 |
| Falling Leaves Fall Speed m/s | 0.1 to 3 | 0.7 |
| Falling Leaves Time On Ground s | 0 to 60 | 8 |
| Tree Hue Shift deg | -180 to 180 | 0 |
| Tree Saturation | 0 to 2 | 1 |
| Tree Brightness | 0 to 2 | 1 |
| Tree Smoothness | 0 to 1 | the source's, else 0.2 |
| Tree Alpha Cutoff | 0.05 to 0.95 | 0.5 |
| Tree Translucency | 0 to 2 | 0 |

## Card text

A card's story box holds at most three lines of about twenty-five words, in the user's
words, and says only what the control hints do not: what the feature does as a whole, a
rule of thumb, a thing that surprises. Never how it is implemented, never a Diagnose line,
never a slider's own meaning (that is its hint). Story boxes and control hints both go with
Show Hints. Each fact lives in one place: what a feature is for is the guide under Status,
what a control does is its hint, and the box says neither again.

The hints, exactly:

| Control | Hint |
|---|---|
| Mode | Auto converts leaves that never receive shadows. Convert forces it for a material Auto missed; Keep original leaves it alone. |
| Brightness | Darkens or lightens the leaves; 1 keeps the asset's own. |
| Alpha Cutoff | How much of the leaf texture counts as leaf: lower keeps thin edges, higher trims them. |
| Translucency | How much light shows through the leaves from behind. |
| Drop Falling Leaves | This material's leaves fall when Falling Leaves is on. |
| Reset to Defaults (a tree) | Every material of this tree back to its defaults, and the tree switched back on. |
| Soft Edges | Smooth leaf outlines when MSAA is on in VaM's settings; without MSAA the edges stay hard. |
| All Atoms | Fixes every CustomUnityAsset in the scene, not only this one. |
| Convert Other Shaders | Also converts transparent materials on shaders Auto does not know. Check the Trees page for anything it should not have touched. |
| Strength | How far the leaves move. |
| Speed | How fast they flutter. |
| Direction | Which way the wind blows, in degrees around the scene. |
| Rate | How many leaves fall each second from each tree. |
| Size | How big each falling leaf is. |
| Fall Speed | How fast they drop; wind and flutter are on top. |
| Time On Ground | How long a leaf lies where it lands before it fades. |
| Technical Info | Adds costs and counts to the captions, and shows the Diagnose button used for debug logs. |
| Show Hints | Shows short descriptions of what each setting does and other useful information. |

The Look card's Tint, Hue Shift, Saturation and Smoothness each carry a hint too.

The story boxes:

- Shadow Fix: "Fog and depth of field see converted leaves too." "Nothing in the asset is
  changed: switch the fix off and the original materials are back."
- Falling Leaves: "A texture without separate leaves drops rounded pieces of it instead."
  "Each tree's materials choose with Drop Falling Leaves on the Trees page."

The tree editor and Wind have none: the guide and the hints say it all.
