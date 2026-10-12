# FoliageFX testing

This file is the in-game checklist. Run it before a release and after a change to the part
a section covers. There is no automated runtime test for a VaM plugin; the automated gates
(lint, compile, packaging) are described in README. Record results in the commit message or
a `docs/` note, with the scene, desktop or VR, and whether MSAA is on.

Test assets: **MR.Sakura_Trees** (leaves on the Standard shader in Fade mode: the case the
plugin is for), **YoshiD.ancient_tree**, and **Damarmau.Realistic_trees**, whose leaves are
already cutout and must be left alone. Light the scene with a directional light that casts
shadows, so shade under a canopy is visible.

## Load, panel, and status checklist

1. Load `FoliageFX.cslist` as a session plugin in a scene with a Sakura tree. While the
   bundle loads the Overview reads "Loading the leaf shader", then "1 tree  ·  N leaf
   materials converted". In an empty scene it reads "No CustomUnityAsset atoms in the
   scene".
2. Load it on the Sakura's CustomUnityAsset atom instead: it looks after that tree only.
   Effects > Shadow Fix shows the Scope card with All Atoms off; switch it on and every
   other CustomUnityAsset is fixed too; off, they get their originals back. Load it on an
   Empty atom: it behaves as the session plugin, the Scope card is not shown, and a
   trigger setting Shadow Fix All Atoms off changes nothing.
3. Category bar: three buttons spanning both columns, Overview ("Status and Trees"), Trees
   ("Fix and Recolour"), Effects ("Shadows, Wind, Leaves"), the active one in violet.
4. Overview: the identity card (FoliageFX, "Vampix Collections · Development Build · main
   abc1234" in a loose install, `Enabled`, the tagline, Copy Patreon Link, which reads
   "Copied to Clipboard" for a moment); What's New scrolls a couple of lines a wheel
   notch, and Full Changelog opens the notes in the modal; the Diagnostics card holds the
   status line, Diagnose, Rescan and Show Hints with its hint. The
   dashboard: a Trees card with one row per tree, an Effects card with Shadow Fix, Wind
   and Falling Leaves, each with its state (as on its rack row) and switch; clicking a
   row opens it.
   The modal, in the headset with VR Comfort's Hover Glow on (Full Changelog): no ring
   on the dimmed backdrop or the card, a ring on Close; a click outside the card closes
   it, a click on the card body does nothing; the wheel or stick over it never scrolls
   the page behind. A confirm (Cancel and its action) and an alert behave the same, and
   a notice still goes on any click, the card included.
5. Effects: each inspector opens with `Enabled`, "Status: <state>" and its guide; Shadow
   Fix's story box has its two lines, Falling Leaves' its two. Show Hints off hides every
   hint line and story box; on brings them back.
6. Close and reopen the plugin UI: it returns to the last category and selection.
7. `Enabled` off: every original material is back (the Sakura's leaves glow in shade
   again), no falling leaf remains, and the status line reads "Off  ·  original materials
   on every tree". On: converted again within a scan (2 s).

## Trees rack checklist

1. With the Trees page open, add a CustomUnityAsset atom with a tree: within a scan a row
   appears with its state ("loading" while the asset loads, then "N of M converted").
   Remove the atom: its row goes, and Diagnose shows no tree left behind.
2. Remove every CustomUnityAsset: the rack shows "No CustomUnityAsset atoms in the scene.
   Add a tree or plant asset and it appears here." Add one back: the row returns.
3. A row's switch off: that tree gets its originals back and reads "off"; the others stay
   converted. On: converted again.
4. Add a non-CustomUnityAsset atom (a Shape): no row.

## Conversion checklist

1. Sakura under the directional light, Shadow Fix on: the leaves darken in the shade of
   the canopy and the trunk, cast shadows on the ground and on each other, and stop
   glowing. The trunk is untouched: its material reads "original". The row and the
   editor's Status read "N of M converted", N the leaf materials.
2. The asset's own materials are never edited: put two atoms of the same Sakura asset in
   the scene and switch one off. The other stays converted; the one switched off looks
   exactly as without the plugin.
3. Damarmau.Realistic_trees: "nothing to fix", every material "original" with a reason
   saying it already receives shadows; nothing changes on screen.
4. YoshiD.ancient_tree: each material's live line gives its shader, its queue and the
   reason it was or was not converted; anything Auto converts takes shadows.
5. Shader missing (rename `FoliageFX.assetbundle` in a loose install, reload the plugin):
   the status line gives the shader error, nothing is converted, nothing else breaks.
   Rename it back.

## Tree editor checklist

1. Select a tree: its row, its Overview row and the editor's title read the atom's uid. A
   uid too long for its row keeps the row's text size and loses its middle
   ("CustomUni…Asset#2"), never its "#N" end; the editor's title shows it whole. A
   long material name in the list is cut the same way. Then `Enabled`, "Status:
   <state>", "Asset: <name>" with the loaded asset exactly as it is named
   ("Sakura_Tree", underscores and case kept), and the guide "Leaves that never
   receive shadows are converted. Pick a material on the left to check or change it."
2. Materials list (left, under the rack): every material on the asset with its state; a
   name that appears twice on the asset carries " #2". Click one: it is painted
   selected, the card on the right takes its name as title, and its live line reads
   "<shader>, queue N. <reason>". Select a tree with fewer materials: the extra rows go.
3. Mode strip: Convert on a material Auto left alone converts it ("converted, forced")
   and it takes shadows; Hue Shift to Gloss and the Leaves card appear at once, and
   the line under the strip reads "Converted: shadows, Wind, Falling Leaves and the whole
   look below apply to it." Keep Original on a converted one puts its original back
   ("kept original"), hides them, and the line says only Tint applies and that Convert
   brings wind, falling leaves and the rest of the look; Auto returns it to the verdict.
   A bark material on Auto shows Tint only, with the same line.
4. Shadow Fix off (Effects): a leaf material on Auto reads "Converted once Shadow Fix is
   switched on, on the Effects page." Its tree switched off: "Converted once this tree
   is enabled." Nothing on the Trees page opens under a card: it has no popup.
5. Tint on a converted material recolours its leaves. Tint on an original material makes
   a tinted copy ("original, tinted") and nothing else about it changes.
6. Hue Shift, Saturation, Brightness and Gloss change the converted leaves at once.
   Only this tree re-applies (Diagnose's gathers count stays put).
7. Alpha Cutoff: lower keeps thin edges, higher trims them. Double-sided off: the leaves'
   back faces disappear. Translucency up: light shows through the leaves from behind
   (stand under the canopy looking toward the light).
8. Reset to Defaults: every look of the tree reset, the tree switched on.
9. Pick another material in the list while one is mid-edit: the controls show the new
   material's look, and the old one keeps what it was given.

## Shadow Fix checklist

1. Shadow Fix off: every original is back on every tree (the leaves glow in shade again);
   the state reads "off", and each tree with leaves to convert reads "not converted" (its
   own switch is still on). On: converted again.
2. Soft Edges with MSAA on in VaM's settings: smooth leaf outlines. MSAA off: hard edges,
   and the state ends "hard edges (MSAA off)". Change MSAA with the scene open: the plugin
   follows within a scan. Soft Edges off with MSAA on: hard edges.
3. Convert Other Shaders (Show Advanced): off by default. On, a transparent material on a
   shader Auto does not know is converted too; check the Trees page, as the hint says.
   Off: back to its original. Particles, Sprites and UI materials are never converted.
4. Reset to Defaults: Soft Edges on, Convert Other Shaders off, Shadow Fix on; All Atoms
   (on a CustomUnityAsset) stays as it was.

## Wind checklist

1. Wind on: every converted leaf sways; the state reads "0.3 from 0°". Materials left
   original do not move. Off: still.
2. Strength 0: still; 1: the leaves move far. Speed: faster flutter. Direction: the sway
   turns with it, and the state follows ("0.3 from 90°").

## Falling Leaves checklist

1. Falling Leaves on with a Sakura: while the texture is read the state reads "finding
   leaf shapes" and the editor's line "Falling leaves: finding leaf shapes..."; then
   petals cut from the tree's own texture drop from the canopy, the line reads "Falling
   leaves: N leaf shapes from the texture." and the state counts them ("12 falling, 40
   resting").
2. Rate: more leaves per second from each tree. Size: bigger leaves. Fall Speed: a faster
   drop, the flutter on top. Time On Ground: leaves lie where they land that long, then
   fade.
3. Wind on: the falling leaves drift with the wind; turn Direction and the drift follows.
4. Resting: leaves land 3 cm above the height of the tree's atom and lie flat, full size,
   for Time On Ground: on the floor under the Sakura and the ancient tree too (their roots
   reach below it), and on VaM's Cyclorama floor, which is drawn above its collider (in
   1.22's Artistic Showcase scene, a landed leaf stays in sight). They do not land on other
   atoms (PLAN: not in version 1).
5. Cast Shadows off: falling leaves cast no shadow; on: they do.
6. Drop Falling Leaves off for one material: its leaves stop and its line reads "Falling
   leaves: off for this material."; the tree's other dropping materials carry on. A
   material that is not converted reads "Falling leaves: only from converted materials."
7. A falling leaf is always a leaf, petal or bud the texture draws; nothing else is cut,
   and no texture gives rounded pieces. Damarmau.Realistic_trees, a leaf material set to
   Convert: "Falling leaves: 2 leaf shapes from the texture." (its texture is two whole
   leaves). MR.Animated_Tree's leaves: 1 leaf shape (one leaf cropped to its texture's
   edges). MR.Sakura_Trees: blossoms and buds, no sprigs. A texture that gives nothing
   reads "Falling leaves: none; " and the reason, its Drop Falling Leaves is greyed, and
   VaM's Message Log has one line naming the texture: MR.Grass_Patches and MR.Trees_2's
   evergreen, "the texture has no single leaf to cut out: its leaves are joined in
   clusters, or are blades, needles or twigs."; a trunk or Yass.sagano_bamboo_forest's
   atlas set to Convert, "the texture is mostly solid, with no leaves cut out of it."
8. Falling Leaves off: no falling or resting leaf remains; the state reads "off", and the
   editor's line "Falling leaves: off on the Effects page."

## Precedence checklist

1. A session FoliageFX and a second FoliageFX on the Sakura's atom: in the session
   instance the Sakura's row has no state text, a greyed switch that does not toggle, and
   the amber badge beside it with "This atom has its own FoliageFX plugin instance, which takes
   precedence." on hover; its name is as wide as every other row's, on the Trees page and
   on the Overview. The
   atom's instance converts the tree; once both have scanned no material in its editor
   reads "other FoliageFX", and nothing is converted twice.
2. Remove the atom's instance: its originals come back, and within a scan the session
   instance takes the tree back: the row's state and switch return, and the tree is
   converted again.
3. Add the atom's instance after the session one has converted the tree: within a scan the
   session instance lets it go and the atom's instance holds it.
4. Two scene-wide instances, which is not a supported setup but must stay safe: a session
   plugin plus one on an Empty atom in the scene, or one on a CustomUnityAsset with All
   Atoms on plus a scene-wide one. The first to convert a material keeps it; the other lists it as
   "other FoliageFX" and leaves it, and neither reads the tree again on every scan
   (Diagnose's gathers count stays put). Remove either: it gives back
   only the slots that still hold its own copies, and the other's work stays in place.

## Persistence checklist

1. Move every effect value off its default, switch one tree off, customise two materials
   on two trees (mode, tint, hue, cutoff, Drop Falling Leaves), open the Trees page with a
   material selected. Save the scene and reload it: every value, every look, the tree's
   switch, the category and the selection are back.
2. The saved scene holds one "FoliageFX" block: `FoliageFXSchema` 1, the version stamp,
   `params` under keys, and `trees` with only the customised looks and the tree switched
   off.
3. Rename a tree's atom: it gets a new row under the new uid and its old row is hidden;
   with the tree open, the editor reopens it on the new row, on its first leaf material
   (the last picked one is not carried over). Its looks follow; save and reload, still
   there.
4. On the plugin's own CustomUnityAsset atom, its looks are keyed `self`; rename the atom
   and they stay.
5. Reset the atom that carries the plugin (VaM's Reset): every setting and look stays.
6. Remove the plugin: every original is back on every tree, no falling leaf remains.
7. Disable the plugin (its checkbox in the atom's Plugins tab): originals back, no falling
   leaves; enable: converted again within a scan.

## Diagnose checklist

1. Diagnose: the report goes to VaM's Message Log, to the clipboard, and to a file under
   `Saves/PluginData/Vampix Collections/FoliageFX/Diagnostics/`; its Runtime section has a
   line each for "trees: ...", "shadow fix: ...", "wind: ...", "falling leaves: ...",
   "shader: ...", and the trees line ends with the copies and falling-leaf materials the
   plugin holds, "copies N, leaf materials M", which is what the checks for nothing held
   read.
2. Rescan: everything released, the scene scanned at once, every tree applied again; the
   picture is the same afterwards.

## Asset switch checklist

1. Switch a tree's asset to another on the same atom: the row reads "loading", then the
   new asset's state, and the editor's Asset line its name; the materials list shows the new asset's materials; nothing
   of the old asset's copies or falling leaves remains (Diagnose's trees line).
2. Switch back: the original asset is converted again, with its customised looks.

## Standing regression tests

**Restoration.** With every effect on: master off; Shadow Fix off; a tree switched off;
plugin disable/enable; remove/reload; asset switch; atom removed; scene save/load. The
original materials return exactly, and no copy, emitter, or falling leaf is left behind.

**Off equals no plugin.** With the master off, every tree looks exactly as without the
plugin, no falling leaf remains, and Diagnose's trees line ends "copies 0, leaf
materials 0".

**The asset's own materials.** Two atoms of one asset, one handled and one switched off:
the second always looks exactly as without the plugin.

## Release matrix

| Event | Expected |
|---|---|
| Master off / Shadow Fix off / a tree off | identical to no plugin for what is off |
| Plugin disable, enable | release; re-applied on the next scan |
| Plugin remove, reload | release in `OnDestroy`; fresh scan |
| CustomUnityAsset added, removed | row added within a scan; released and row hidden |
| Asset switched on an atom | old copies released, new asset gathered and applied |
| Atom renamed | a new row under the new uid, the old one hidden; the looks follow |
| A FoliageFX added to or removed from a tree's atom | the tree changes hands within a scan, never converted twice |
| MSAA switched in VaM's settings | soft edges follow within a scan |
| Scene save / load | every saved value and every customised look round-trip; schema stamped |
| The atom's Reset | settings and looks untouched |
| Exception in a step | that step reported once; the rest of the pass carries on |

Performance targets: once every tree is applied a frame only compares the effects' values
(a dozen comparisons), a scan every 2 s, an edit re-applies one tree, 0 B/frame. Read them
with Unity's profiler, or VaM's own frame timings, against the scene without the plugin.
