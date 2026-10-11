# Builds FoliageFX.assetbundle from the files in shader/ with Unity 2018.1.9f1
# (VaM's engine line) in batch mode, copies it next to the cslist, and records
# what it was built from in shader/bundle.sources, so any machine can tell
# whether the bundle is current without Unity. The build needs Windows and the
# editor; the check runs anywhere (the packager and CI run it).
#
#   pwsh tools/bundle.ps1                     # build; the pre-commit hook runs this when a commit touches shader/
#   pwsh tools/bundle.ps1 -Wait               # build now, copy the moment VaM releases the bundle (plugin removed, or VaM closed)
#   pwsh tools/bundle.ps1 -Check              # exit 0 when the bundle matches shader/, 3 when it does not
#   pwsh tools/bundle.ps1 -Record             # accept the current bundle as built from shader/ (after a build in the editor)
#   pwsh tools/bundle.ps1 -Project <folder>   # once: the Unity project to build in, created if missing (kept in tools/unity.local.json)
#   pwsh tools/bundle.ps1 -Unity <Unity.exe>  # once: the editor, when it is not in a standard install folder
param(
    [switch]$Check,
    [switch]$Record,
    [string]$Project,
    [string]$Unity,
    [switch]$Wait,
    [switch]$Quiet
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$shaderDir = Join-Path $root "shader"
$bundle = "FoliageFX.assetbundle"
$bundlePath = Join-Path $root $bundle
$manifestName = "bundle.sources"
$manifestPath = Join-Path $shaderDir $manifestName
$builder = "Editor_BuildFoliageFXBundle.cs"
$configPath = Join-Path $PSScriptRoot "unity.local.json"
$unityVersion = "2018.1.9f1"

function Say($s) { if (-not $Quiet) { Write-Host $s } }
function Complain($s) { [Console]::Error.WriteLine("bundle: $s") }
function Fail($s) { Complain $s; exit 1 }
function Q($s) { '"' + $s + '"' }

# Everything in shader/ but the record itself: the shaders, the compute shader, the builder.
function Sources() {
    Get-ChildItem $shaderDir -File | Where-Object { $_.Name -ne $manifestName } | Sort-Object Name
}
function HashText($path) {
    # Line endings normalised, so a CRLF checkout hashes like an LF one.
    $text = [IO.File]::ReadAllText($path) -replace "`r`n", "`n"
    $stream = [IO.MemoryStream]::new([Text.Encoding]::UTF8.GetBytes($text))
    (Get-FileHash -InputStream $stream -Algorithm SHA256).Hash.ToLowerInvariant()
}
function HashBytes($path) { (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Manifest() {
    $lines = @("# Written by tools/bundle.ps1: what $bundle was built from. Not edited by hand: rebuild, or -Record after a build in the editor.")
    foreach ($f in Sources) { $lines += ((HashText $f.FullName) + "  " + $f.Name) }
    if (Test-Path $bundlePath) { $lines += ((HashBytes $bundlePath) + "  " + $bundle) }
    $lines
}
function WriteManifest() {
    [IO.File]::WriteAllText($manifestPath, ((Manifest) -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
}

# ---- check: no Unity, any platform ----
if ($Check) {
    if (-not (Test-Path $bundlePath)) { Complain "$bundle missing: run pwsh tools/bundle.ps1"; exit 3 }
    if (-not (Test-Path $manifestPath)) { Complain "shader/$manifestName missing: run pwsh tools/bundle.ps1 (or -Record after a build in the editor)"; exit 3 }
    $want = @(Get-Content $manifestPath | Where-Object { $_ -and $_ -notmatch '^#' })
    $have = @((Manifest) | Where-Object { $_ -notmatch '^#' })
    $stale = @(Compare-Object $want $have | ForEach-Object { ($_.InputObject -split '  ', 2)[1] } | Sort-Object -Unique)
    if ($stale.Count -eq 0) { Say "bundle: current ($(@(Sources).Count - 1) shaders)"; exit 0 }
    Complain ("$bundle is out of date with shader/ (" + ($stale -join ", ") + "): run pwsh tools/bundle.ps1")
    exit 3
}

# ---- record: a bundle built in the editor by hand ----
if ($Record) {
    if (-not (Test-Path $bundlePath)) { Fail "$bundle missing" }
    WriteManifest
    Say "bundle: shader/$manifestName records the current $bundle as built from shader/"
    exit 0
}

# ---- build ----
$windows = if ($null -ne $IsWindows) { $IsWindows } else { $env:OS -eq "Windows_NT" }
if (-not $windows) { Fail "the bundle builds on Windows with Unity ${unityVersion}: run pwsh tools/bundle.ps1 there" }

# this machine's editor and project, remembered across runs
$cfg = @{}
if (Test-Path $configPath) {
    (Get-Content $configPath -Raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $cfg[$_.Name] = $_.Value }
}
$save = $false
if ($Project) { $cfg.project = [IO.Path]::GetFullPath($Project); $save = $true }
if ($Unity) { $cfg.unity = [IO.Path]::GetFullPath($Unity); $save = $true }
if ($save) {
    [IO.File]::WriteAllText($configPath, (([pscustomobject]$cfg | ConvertTo-Json) + "`n"), [Text.UTF8Encoding]::new($false))
    Say "bundle: settings kept in tools/unity.local.json"
}

$exe = $cfg.unity
if (-not $exe) {
    foreach ($c in @("C:\Program Files\Unity\Hub\Editor\$unityVersion\Editor\Unity.exe", "C:\Program Files\Unity\Editor\Unity.exe")) {
        if ((Test-Path $c) -and ((Get-Item $c).VersionInfo.ProductVersion -like "2018.1.9.*")) { $exe = $c; break }
    }
}
if (-not $exe -or -not (Test-Path $exe)) { Fail "Unity $unityVersion not found: install it (VaM's engine line), or pass -Unity <path to Unity.exe> once" }
$ver = (Get-Item $exe).VersionInfo.ProductVersion
if ($ver -notlike "2018.1.9.*") { Fail "$exe is Unity $ver; the bundle must come from $unityVersion (VaM's engine line)" }

$proj = $cfg.project
if (-not $proj) { Fail "no Unity project set: pass -Project <folder> once (created there if it does not exist; kept in tools/unity.local.json)" }

$log = Join-Path $root "dist/bundle.log"
New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null
function RunUnity($arguments, $what) {
    Say "unity: $what"
    if (Test-Path $log) { Remove-Item $log -Force }
    $p = Start-Process -FilePath $exe -ArgumentList $arguments -Wait -PassThru
    $p.ExitCode
}

if (-not (Test-Path (Join-Path $proj "ProjectSettings/ProjectVersion.txt"))) {
    $code = RunUnity @("-batchmode", "-quit", "-createProject", (Q $proj), "-logFile", (Q $log)) "creating the project at $proj (first time only)"
    if ($code -ne 0 -or -not (Test-Path (Join-Path $proj "ProjectSettings/ProjectVersion.txt"))) { Fail "Unity could not create the project (exit $code): see dist/bundle.log" }
}
$pv = Get-Content (Join-Path $proj "ProjectSettings/ProjectVersion.txt") -Raw
if ($pv -notmatch "m_EditorVersion:\s*2018\.1\.9f1") { Fail "$proj is not a $unityVersion project (ProjectSettings/ProjectVersion.txt): point -Project at another folder" }

# the project holds exactly the repo's files: shader/ under Assets/FoliageFX, the builder under Assets/Editor
$assets = Join-Path $proj "Assets/FoliageFX"
$editor = Join-Path $proj "Assets/Editor"
New-Item -ItemType Directory -Force $assets, $editor | Out-Null
$names = @()
# Unity 2018 recompiles a shader only when its own text changes ("skipped as no
# actual change"), never for a file it includes, and a touched timestamp does
# not count: a change to VellusCommon.cginc alone built the old bundle byte for
# byte. So each shader's project copy ends with its includes' hashes, and a
# changed include changes every shader's text.
$includeStamp = (Sources | Where-Object { $_.Extension -eq ".cginc" } | ForEach-Object { "// include " + $_.Name + " " + (HashText $_.FullName) }) -join "`n"
foreach ($f in Sources) {
    if ($f.Name -eq $builder) { continue }
    $names += $f.Name
    $dst = Join-Path $assets $f.Name
    $text = [System.IO.File]::ReadAllText($f.FullName)
    if ($includeStamp -and $f.Extension -in @(".shader", ".compute")) { $text = $text.TrimEnd() + "`n" + $includeStamp + "`n" }
    $have = if (Test-Path $dst) { [System.IO.File]::ReadAllText($dst) } else { $null }
    if ($have -ne $text) { [System.IO.File]::WriteAllText($dst, $text, [System.Text.UTF8Encoding]::new($false)) }
}
foreach ($old in Get-ChildItem $assets -File | Where-Object { ($_.Extension -in @(".shader", ".compute")) -and ($names -notcontains $_.Name) }) {
    Remove-Item $old.FullName -Force
    if (Test-Path ($old.FullName + ".meta")) { Remove-Item ($old.FullName + ".meta") -Force }
    Say "unity: removed $($old.Name), no longer in shader/"
}
$src = Join-Path $shaderDir $builder
$dst = Join-Path $editor $builder
if (-not (Test-Path $src)) { Fail "shader/$builder missing" }
if (-not (Test-Path $dst) -or (HashBytes $dst) -ne (HashBytes $src)) { Copy-Item $src $dst -Force }

$before = if (Test-Path $bundlePath) { HashBytes $bundlePath } else { "" }
$code = RunUnity @("-batchmode", "-quit", "-projectPath", (Q $proj), "-executeMethod", "Editor_BuildFoliageFXBundle.Build", "-logFile", (Q $log)) "building $bundle from $($names.Count) shaders"
$text = if (Test-Path $log) { Get-Content $log -Raw } else { "" }
if ($text -match "Multiple Unity instances cannot open the same project|another Unity instance is running") {
    Fail "the project is open in the Unity editor: close it there and run again"
}
$errors = @([regex]::Matches($text, '(?m)^(Shader error in .*|.*error CS\d+.*)$') | ForEach-Object { $_.Value.TrimEnd() })
if ($code -ne 0 -or $text -notmatch "FoliageFX bundle built:" -or $errors.Count -gt 0) {
    foreach ($e in $errors) { Complain $e }
    Fail "Unity did not build the bundle (exit $code): see dist/bundle.log"
}
$built = Get-ChildItem (Join-Path $proj "AssetBundles") -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -ieq $bundle } | Select-Object -First 1
if (-not $built) { Fail "AssetBundles/$bundle not found in the project after the build" }
# VaM holds the bundle open while the loose plugin is loaded, so the copy can
# fail. -Wait polls until the file is released (the plugin removed from the
# scene, or VaM closed) and copies then; without it the failure is reported and
# the built file waits in the project's AssetBundles folder.
$copied = $false
$told = $false
while (-not $copied) {
    try { Copy-Item $built.FullName $bundlePath -Force -ErrorAction Stop; $copied = $true }
    catch {
        if (-not $Wait) { Fail "could not replace ${bundle}: VaM holds it open. Remove the plugin from the scene (which releases the bundle) or close VaM, then run again, or run with -Wait to copy the moment it is released. The build itself succeeded; the new file waits in the project's AssetBundles folder." }
        if (-not $told) { Say "bundle: built; VaM holds $bundle open. Waiting to copy until it is released (remove the plugin from the scene, or close VaM; Ctrl+C gives up, the built file waits in the project's AssetBundles folder)"; $told = $true }
        Start-Sleep -Seconds 2
    }
}
WriteManifest
$after = HashBytes $bundlePath
$state = if ($after -eq $before) { "unchanged" } else { "updated" }
Say "bundle: $bundle $state ($([math]::Round($built.Length / 1024)) KB, $($names.Count) shaders); shader/$manifestName recorded"
