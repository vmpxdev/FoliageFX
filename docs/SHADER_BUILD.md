# Building FoliageFX.assetbundle

The bundle holds the leaf shader, and the plugin cannot work without it. It is built with
Unity 2018.1.9f1 (VaM's engine line; 2018.1.x bundles load in VaM) from the files in
`shader/`, and committed next to `FoliageFX.cslist`.

| Shader file | Shader name | What the plugin uses it for | Without it |
|---|---|---|---|
| `shader/Leaves.shader` | `Vampix Collections/FoliageFX/Leaves` | every converted leaf material: a copy of the tree's own material on this shader, alpha tested in the AlphaTest queue so it casts and receives shadows, with soft edges, recolouring, translucency and wind; and the material of the falling leaves | the plugin does nothing: no material is converted and no leaves fall, the trees keep their own materials, and the status line, Shadow Fix's state ("no shader") and Diagnose say why |

Only the shader goes in the bundle. The plugin creates its own materials at runtime, so no
material assets and no bundle tagging are needed.

## Building

```
pwsh tools/bundle.ps1
```

The script copies `shader/` into a Unity 2018.1.9f1 project, runs the editor in batch mode
with the builder (`shader/Editor_BuildFoliageFXBundle.cs`, menu **Vampix Collections >
Build FoliageFX Bundle** when the project is open by hand), copies the result next to the
cslist, and writes `shader/bundle.sources`: the hashes of the files the bundle was built
from. About ten seconds once the project exists.

- Windows, with Unity 2018.1.9f1 installed. The script finds it in the standard install
  folders; elsewhere, pass `-Unity <Unity.exe>` once.
- The first run takes `-Project <folder>`: the Unity project to build in, created there if
  it does not exist (a minute or two). Both settings are kept in `tools/unity.local.json`
  (ignored by git).
- Close the project in the editor before building; Unity opens one copy of a project.
- VaM holds `FoliageFX.assetbundle` open while the loose plugin is loaded, so the copy into
  the plugin folder fails then: the build still succeeds and the new file waits in the
  project's AssetBundles folder; remove the plugin from the scene (which releases the
  bundle) or close VaM, then run the script again. `pwsh tools/bundle.ps1 -Wait` builds
  now and keeps polling until the file is released, then copies and records, so it can be
  started before removing the plugin or closing VaM.
- A shader error fails the build and prints in the console; the full editor log is
  `dist/bundle.log`.

`pwsh tools/bundle.ps1 -Check` (no Unity, any platform) compares `shader/` and the bundle
with `bundle.sources`. The pre-commit hook runs it when a commit touches `shader/` and,
when the bundle is out of date, builds it and stages `FoliageFX.assetbundle` and
`shader/bundle.sources` with the commit, so a shader edit and its bundle land together.
The packager and CI run the check and refuse an out-of-date bundle.

To see a change before loading VaM, `pwsh tools/render/render.ps1 -Trees ...` renders tree
bundles before and after the conversion, in the same project, with half of each canopy in a
roof's shadow; the images go to `dist/render/` and the script's header says how to call it.

### By hand

Without the script: open the project in Unity 2018.1.9f1, put `shader/Leaves.shader` under
`Assets/FoliageFX/` and the builder under `Assets/Editor/`, run **Vampix Collections > Build
FoliageFX Bundle**, copy `AssetBundles/FoliageFX.assetbundle` next to the cslist, then
`pwsh tools/bundle.ps1 -Record` so the check accepts it. Remove old shader files from
`Assets/FoliageFX/` first, or the bundle carries them too.

The bundle's name and location never change, so the plugin picks up a new one on the next
reload. Restart VaM when it had the old file open: VaM keeps one copy of a bundle's contents
per session, which is also why the plugin releases its bundle when it is removed (a copy
still held from another folder or package, the loose folder and a var say, makes the next
load fail; the log says so).

## Notes

- VR. VaM renders both eyes in one pass (single pass stereo). A shader compiled without
  the stereo variant draws the left eye's view into the right eye too, so the leaves sit
  right in the left eye and float, offset, in the right. `Leaves` is a surface shader, and
  Unity generates its `UNITY_SINGLE_PASS_STEREO` variants only while the building project
  has VR on, so the builder enables VR with the Single Pass method (Player Settings > XR
  Settings) and reimports the shader before building. Every pass in the bundle carries
  them, the ShadowCaster included. On a monitor all of it is a no-op.
- Colour space. VaM renders in linear space, and Unity compiles a shader for the building
  project's colour space: the Standard lighting's constants, and the shader's trip to gamma
  space and back for its recolouring. The builder sets the project to Linear; a bundle
  built in a Gamma project lights the leaves wrongly in VaM.
- Property names are frozen: `_Color _MainTex _Cutoff _BumpMap _BumpScale _Glossiness
  _Metallic _HueShift _Saturation _Brightness _Translucency _TranslucencyDistortion
  _TranslucencyPower _SoftEdges _MipCoverage _WindStrength _WindSpeed _WindDirection
  _Cull`, and the one keyword, `_NORMALMAP`. The plugin writes them by name on every
  converted copy, and a copy of a Standard material carries its texture, tiling, colour,
  smoothness and metallic over only because the names are the Standard shader's. A rename
  breaks the plugin silently: the value is written to a name the shader no longer has.
  Display names (the Properties block's strings) are free to change. Every look is a
  material value and not a keyword: the bundle carries no materials, so a `shader_feature`
  keyword would lose its variants at build time, and each `multi_compile` one doubles the
  variant count.
- The ShadowCaster pass is written by hand, with `AlphaToMask Off`. The lit passes have
  alpha to coverage on for the soft edges, and it is set for the whole SubShader. The
  ShadowCaster pass, which also draws the camera's depth texture, returns depth instead of
  alpha (zero for most lights), so with alpha to coverage it would drop every sample and
  the leaves would cast no shadow and vanish from the depth texture. It clips at the
  cutoff and applies the same wind as the lit passes (`ApplyWind` and `LeafAlpha` are
  shared in the `CGINCLUDE`), so the shadow moves with the leaves and has their outline;
  a change to either belongs in both.
- The bundle in the repo carries the one shader under its release name
  (`Vampix Collections/FoliageFX/Leaves`, the plugin's `FoliageFXConfig.LeafShaderName`).
  It is rebuilt only after a change in `shader/`.
