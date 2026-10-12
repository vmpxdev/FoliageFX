# FoliageFX roadmap

Where the product stands and what comes after the first release. The in-game checks are
in `docs/TESTING.md`; the automated gates (lint, compile, packaging) are in `README.md`.

## Shipped in version 1

Version 1 is in preparation; this is what it ships. Shadow Fix: every leaf material on
every CustomUnityAsset in the scene that never receives shadows (the Standard shader in
Fade or Transparent mode, legacy Transparent and Nature shaders) gets a copy on the
plugin's cutout leaf shader that casts and receives shadows, with soft edges under MSAA
and an opt-in for transparent materials on other shaders; the asset's own materials are
never edited, and the originals come back whenever the plugin lets go. The Trees page:
every tree live in a rack with its state and switch, and an editor per material: Mode
(Auto, Convert, Keep Original), Tint, Hue Shift, Saturation, Brightness, Gloss, Alpha
Cutoff, Double-sided, Translucency, Drop Falling Leaves, with Reset to Defaults for the
whole tree. Wind: a sway on
every converted leaf, with strength, speed and direction. Falling Leaves: leaf shapes cut
from each tree's own texture (none from a texture whose leaves cannot be cut out), dropping
from the canopy at a rate per tree, drifting with the wind, lying on the ground for a
while, casting shadows. Precedence: a FoliageFX on a CustomUnityAsset owns that tree, a
scene-wide one looks after the rest. Scene persistence under stable keys with a schema
stamp, hints under the controls, Diagnose, Timeline and trigger access
to every effect setting.

## Deliberately not in version 1

- Presets. Settings and looks save with the scene only.
- Tree looks saved by material name, outside a scene, so that any tree with that material
  would take them.
- Leaves landing on real geometry. A falling leaf rests on a flat plane 3 cm above its
  tree's atom, not on whatever lies under it.
- Per-tree wind. One wind for the scene.
- Converting leaves on unlit shaders.

## Next

Candidates the author has discussed, none designed yet:

- Look buttons in the tree editor, taken out before version 1 to be done properly: Reset
  Material (the picked material's look back to its defaults), Copy Look to Tree (the look
  to every converted material of the tree, each keeping its mode) and Copy to Same
  Material Everywhere (the look, mode included, to the material of that name on every
  tree). The first build had them: `TreesPanel.ResetMaterial`, `CopyToTree`,
  `CopyEverywhere` and `LeafLook.CopyFrom` in commit e0c83a9.
- Seasons.
- Canopy depth shading.
- Distance limits.
- A Unity creator kit.

## Decisions that stand

- One integer version, VaM's var number; tags `v1`, `v2`, `v3`.
- Save keys are the schema and never change; storable names and labels are free.
- Settings save with the scene only; no presets.
- Off is identical to no plugin.
- A FoliageFX on the atom itself wins over a scene-wide one.
- Two scene-wide instances are not a supported setup: a FoliageFX on the atom wins,
  otherwise the first to convert a material keeps it.
