# Builds the .var. Stages the cslist and the sources with comments stripped,
# stamps the release number (or build.json on a dev build), adds the bundle,
# writes meta.json, zips, and checks the result. Runs on Windows
# with VaM (then it also compiles the stripped sources) and on a bare Linux
# runner (lint and packaging only).
#
#   pwsh tools/pack.ps1                       # dev build: VampixCollections.FoliageFX-dev.<commits>.var, Version "dev"
#   pwsh tools/pack.ps1 -Tag v3               # release build: Version "3"; needs Entry(3, ...) in Changelog.cs
#   pwsh tools/pack.ps1 -Install              # also copy into <VaM>/AddonPackages
#   pwsh tools/pack.ps1 -NoCompile            # skip the compile of the stripped sources (CI)
param(
    [string]$Tag,
    [string]$Out,
    [string]$VamRoot,
    [switch]$Install,
    [switch]$NoCompile,
    [switch]$Quiet
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if (-not $Out) { $Out = Join-Path $root "dist" }
$Out = [IO.Path]::GetFullPath($Out)
$creator = "VampixCollections"
$package = "FoliageFX"
$innerDir = "Custom/Scripts/Vampix Collections/FoliageFX"
$cslist = "FoliageFX.cslist"
$bundle = "FoliageFX.assetbundle"
$deny = @('\.md$', '\.ps1$', '\.sh$', '\.shader$', '\.cginc$', '\.compute$', '\.csproj$', '\.zip$', '\.dll$', '\.png$', '^tools/', '^docs/', '^packaging/', '^\.github/', '^\.cursor/', '/Editor_')

function Say($s) { if (-not $Quiet) { Write-Host $s } }
function Fail($s) { Write-Error $s; exit 1 }
# The pwsh running this script, by path: a git hook can run it without pwsh on PATH.
$pwshExe = (Get-Process -Id $PID).Path
if (-not $pwshExe) { $pwshExe = Join-Path $PSHOME $(if ($IsWindows) { "pwsh.exe" } else { "pwsh" }) }

# ---- version ----
$release = 0
if ($Tag) {
    if ($Tag -notmatch '^v(\d+)$') { Fail "tag must be v<number>, got '$Tag'" }
    $release = [int]$Matches[1]
}
Push-Location $root
try {
    $branch = (git rev-parse --abbrev-ref HEAD 2>$null)
    $commit = (git rev-parse --short=7 HEAD 2>$null)
    $commits = (git rev-list --count HEAD 2>$null)
} finally { Pop-Location }
# A dev build is its own package, FoliageFX-dev, numbered by the commit count,
# so it can never be mistaken for, or collide with, a release.
if ($release -gt 0) { $number = $release }
else { $package = "$package-dev"; $number = if ($commits) { [int]$commits } else { 0 } }
$varName = "$creator.$package.$number.var"

# ---- release gate: notes must exist ----
$changelog = Get-Content (Join-Path $root "Types/Core/Changelog.cs") -Raw
if ($release -gt 0 -and $changelog -notmatch ("Entry\(\s*" + $release + "\s*,")) {
    Fail "Changelog.cs has no Entry($release, ...): write the What's New for version $release first"
}

# ---- stage ----
$stageRoot = Join-Path $Out "stage"
$stage = Join-Path $stageRoot ($innerDir -replace '/', [IO.Path]::DirectorySeparatorChar)
if (Test-Path $stageRoot) { Remove-Item $stageRoot -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

Say "strip: sources"
$stripProj = Join-Path $root "tools/Strip"
& dotnet build $stripProj -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { Fail "could not build tools/Strip" }
$stripDll = Get-ChildItem (Join-Path $stripProj "bin/Release") -Recurse -Filter "Strip.dll" | Select-Object -First 1
if (-not $stripDll) { Fail "Strip.dll not found after build" }
& dotnet $stripDll.FullName (Join-Path $root $cslist) $stage
if ($LASTEXITCODE -ne 0) { Fail "comment stripping failed" }

# the release number into the staged config
$cfg = Join-Path $stage "Types/Core/FoliageFXConfig.cs"
$cfgText = Get-Content $cfg -Raw
if ($cfgText -notmatch 'public const string Version = "dev";') { Fail "FoliageFXConfig.Version placeholder not found; it must read `"dev`" in the repo" }
if ($release -gt 0) {
    $cfgText = $cfgText -replace 'public const string Version = "dev";', ('public const string Version = "' + $release + '";')
    [IO.File]::WriteAllText($cfg, $cfgText, [Text.UTF8Encoding]::new($false))
}

# build identity: dev builds only; a release carries its number and nothing else
if ($release -eq 0) { & $pwshExe -NoProfile -File (Join-Path $PSScriptRoot "build-info.ps1") -Out (Join-Path $stage "build.json") | Out-Null }

# the bundle: present, and built from the shaders in the tree (shader/bundle.sources)
$bundlePath = Join-Path $root $bundle
if (-not (Test-Path $bundlePath)) { Fail "$bundle missing" }
& $pwshExe -NoProfile -File (Join-Path $PSScriptRoot "bundle.ps1") -Check -Quiet
if ($LASTEXITCODE -ne 0) { Fail "$bundle is out of date with shader/: run pwsh tools/bundle.ps1 (docs/SHADER_BUILD.md)" }
Copy-Item $bundlePath (Join-Path $stage $bundle)
# The bundle's contents are compressed, so only its header can be read here:
# a UnityFS bundle from VaM's Unity line, and not an empty stub. Which
# shaders loaded is reported by the plugin's status well and Diagnose.
$head = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($bundlePath), 0, 64)
if ($head -notmatch 'UnityFS') { Fail "$bundle is not a UnityFS asset bundle" }
if ($head -notmatch '2018\.1\.') { Fail "$bundle was not built with Unity 2018.1.x (VaM's line): see docs/SHADER_BUILD.md" }
if ((Get-Item $bundlePath).Length -lt 10000) { Fail "$bundle is too small to hold the shaders" }

# ---- release gate: SaveHistory names no release past the tag ----
# Types/Core/SaveHistory.cs lists what each release added to or changed in
# saved data. A line for a release past the one being tagged is a slip in the
# history, and a load report would date files wrongly by it.
$historyText = Get-Content (Join-Path $root "Types/Core/SaveHistory.cs") -Raw
$historyLines = [regex]::Matches($historyText, '\b(Look|Scene|Panel|Removed|Changed)\(\s*(\d+)\s*,\s*"([^"]+)"')
$staleProblems = @()
foreach ($h in $historyLines) {
    if ($release -gt 0 -and [int]$h.Groups[2].Value -gt $release) { $staleProblems += "SaveHistory.cs: a $($h.Groups[1].Value) line for release $($h.Groups[2].Value) is past the tag v$release" }
}
if ($staleProblems.Count -gt 0) { Fail ($staleProblems -join "`n") }

# ---- the stripped sources must still lint, and compile where VaM is ----
& $pwshExe -NoProfile -File (Join-Path $PSScriptRoot "lint.ps1") -Root $stage
if ($LASTEXITCODE -ne 0) { Fail "lint failed on the stripped sources" }
if (-not $VamRoot) { $VamRoot = [IO.Path]::GetFullPath((Join-Path $root "../../../..")) }
$haveVam = Test-Path (Join-Path $VamRoot "VaM_Data/Managed/Assembly-CSharp.dll")
if (-not $NoCompile) {
    if ($haveVam -and $IsWindows) {
        & $pwshExe -NoProfile -File (Join-Path $PSScriptRoot "compile.ps1") -Root $stage -VamRoot $VamRoot -NoLint
        if ($LASTEXITCODE -ne 0) { Fail "the stripped sources do not compile" }
    } else { Say "compile: skipped (no VaM assemblies here)" }
}

# ---- meta.json ----
$meta = Get-Content (Join-Path $root "packaging/meta.template.json") -Raw | ConvertFrom-Json
$files = Get-ChildItem $stageRoot -Recurse -File | ForEach-Object { $_.FullName.Substring($stageRoot.Length + 1).Replace("\", "/") } | Sort-Object
$meta.packageName = $package
$meta.contentList = @($files)
$notes = ""
if ($release -gt 0) {
    # the What's New entry, out of the C# string literals of Entry(N, "title", "body")
    $idx = $changelog.IndexOf("Entry($release,")
    $rest = $changelog.Substring($idx)
    $end = [regex]::Match($rest.Substring(6), '\n\s*(Entry\(|\};)')
    $block = if ($end.Success) { $rest.Substring(0, $end.Index + 6) } else { $rest }
    $lits = [regex]::Matches($block, '"((?:[^"\\]|\\.)*)"') | ForEach-Object { $_.Groups[1].Value -replace '\\n', "`n" -replace '\\"', '"' -replace '\\\\', '\' }
    $title = $lits[0]; $body = ($lits | Select-Object -Skip 1) -join ""
    $notes = "Version $release  -  $title`n`n$body"
    $meta.description = if ($meta.description) { $meta.description + "`n`n" + $notes } else { $notes }
} else {
    $devNote = "Development build $branch $commit (not a release)."
    $meta.description = if ($meta.description) { $meta.description + "`n`n" + $devNote } else { $devNote }
}
$metaJson = $meta | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $stageRoot "meta.json"), $metaJson + "`n", [Text.UTF8Encoding]::new($false))
if ($notes) { New-Item -ItemType Directory -Force $Out | Out-Null; [IO.File]::WriteAllText((Join-Path $Out "notes.md"), $notes + "`n", [Text.UTF8Encoding]::new($false)) }

# ---- zip, forward slashes, meta.json first ----
New-Item -ItemType Directory -Force $Out | Out-Null
$varPath = Join-Path $Out $varName
if (Test-Path $varPath) { Remove-Item $varPath -Force }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::Open($varPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $stageRoot "meta.json"), "meta.json", [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    foreach ($f in $files) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $stageRoot ($f -replace '/', [IO.Path]::DirectorySeparatorChar)), $f, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $zip.Dispose() }

# ---- check the var itself ----
$zip = [IO.Compression.ZipFile]::OpenRead($varPath)
try {
    $entries = $zip.Entries | ForEach-Object { $_.FullName }
    foreach ($e in $entries) { foreach ($d in $deny) { if ($e -match $d) { Fail "var contains '$e' (matches $d)" } } }
    $listed = Get-Content (Join-Path $root $cslist) | ForEach-Object { $_.Trim() } | Where-Object { $_ }
    foreach ($l in $listed) { if ($entries -notcontains "$innerDir/$l") { Fail "var lacks cslist entry $l" } }
    foreach ($e in $entries) { if ($e -match '\.cs$' -and $e -notmatch '/Editor_' -and $listed -notcontains $e.Substring($innerDir.Length + 1)) { Fail "var contains a source not in the cslist: $e" } }
    if ($entries -notcontains "$innerDir/$bundle") { Fail "var lacks $bundle" }
    foreach ($e in $entries) { if ($e -match "^Saves/") { Fail "var carries a Saves entry: $e" } }
    if ($release -eq 0 -and $entries -notcontains "$innerDir/build.json") { Fail "dev var lacks build.json" }
    if ($release -gt 0 -and $entries -contains "$innerDir/build.json") { Fail "release var must not carry build.json" }
    $stampedEntry = $zip.GetEntry("$innerDir/Types/Core/FoliageFXConfig.cs")
    $sr = [IO.StreamReader]::new($stampedEntry.Open()); $stamped = $sr.ReadToEnd(); $sr.Dispose()
    $want = if ($release -gt 0) { "$release" } else { "dev" }
    if ($stamped -notmatch ('Version = "' + $want + '";')) { Fail "Version in the var is not `"$want`"" }
    if ([regex]::IsMatch($stamped, '(?m)^\s*//')) { Fail "comments survived stripping in FoliageFXConfig.cs" }
    $null = ([IO.StreamReader]::new($zip.GetEntry("meta.json").Open())).ReadToEnd() | ConvertFrom-Json
} finally { $zip.Dispose() }
Remove-Item $stageRoot -Recurse -Force

$size = [math]::Round((Get-Item $varPath).Length / 1KB)
Say ("pack: {0} ({1} KB, {2} files, Version {3})" -f $varName, $size, $files.Count, $want)

if ($Install) {
    $addon = Join-Path $VamRoot "AddonPackages"
    if (-not (Test-Path $addon)) { Fail "AddonPackages not found under $VamRoot" }
    # A dev install replaces every older dev build (throwaway); a release replaces its own number only.
    $old = if ($release -gt 0) { "$creator.$package.$number.var*" } else { "$creator.$package.*.var*" }
    $kept = @()
    foreach ($f in Get-ChildItem $addon -Filter $old) {
        if ($f.Name -eq $varName) { continue }
        try { Remove-Item $f.FullName -Force -ErrorAction Stop } catch { $kept += $f.Name }   # VaM holds it open
    }
    try { Copy-Item $varPath (Join-Path $addon $varName) -Force -ErrorAction Stop }
    catch { Fail "could not write $varName into AddonPackages ($($_.Exception.Message.Trim())): VaM holds it open; close VaM, or remove the plugin from the scene and rescan packages, then run again" }
    Say ("installed: {0}" -f (Join-Path $addon $varName))
    if ($kept.Count -gt 0) { Say ("kept, VaM holds them open (removed by a later install): " + ($kept -join ", ")) }
}
