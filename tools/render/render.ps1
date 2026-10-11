# Renders tree bundles before and after FoliageFX's leaf conversion with Unity 2018.1.9f1, so a
# change to shader/Leaves.shader can be checked without VaM: a roof shades half of each canopy,
# and after the conversion that half should darken. Developer tool; the images go to dist/render/.
# It uses the editor and project of tools/bundle.ps1 (tools/unity.local.json): build the bundle first.
#   pwsh tools/render/render.ps1 -Trees "<tree.assetbundle>::<prefab path>::<label>|..."
#   pwsh tools/render/render.ps1 -Trees "..." -ShaderBundle <other FoliageFX.assetbundle> -Out <folder>
param(
    [Parameter(Mandatory = $true)][string]$Trees,
    [string]$ShaderBundle,
    [string]$Out
)
$ErrorActionPreference = "Stop"
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$configPath = Join-Path $root "tools/unity.local.json"
$script = "Editor_RenderTrees.cs"
$unityVersion = "2018.1.9f1"

function Complain($s) { [Console]::Error.WriteLine("render: $s") }
function Fail($s) { Complain $s; exit 1 }
function Q($s) { '"' + $s + '"' }
function FullPath($p) { $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($p).TrimEnd('\', '/') }
function HashBytes($path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }

$windows = if ($null -ne $IsWindows) { $IsWindows } else { $env:OS -eq "Windows_NT" }
if (-not $windows) { Fail "the render runs on Windows with Unity $unityVersion" }

# the editor and project tools/bundle.ps1 found and kept
$cfg = @{}
if (Test-Path $configPath) {
    (Get-Content $configPath -Raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $cfg[$_.Name] = $_.Value }
}
$exe = $cfg.unity
if (-not $exe) {
    foreach ($c in @("C:\Program Files\Unity\Hub\Editor\$unityVersion\Editor\Unity.exe", "C:\Program Files\Unity\Editor\Unity.exe")) {
        if ((Test-Path $c) -and ((Get-Item $c).VersionInfo.ProductVersion -like "2018.1.9.*")) { $exe = $c; break }
    }
}
if (-not $exe -or -not (Test-Path $exe)) { Fail "Unity $unityVersion not found: run pwsh tools/bundle.ps1 -Unity <path to Unity.exe> once" }
$proj = $cfg.project
if (-not $proj -or -not (Test-Path (Join-Path $proj "ProjectSettings/ProjectVersion.txt"))) {
    Fail "no Unity project yet: run pwsh tools/bundle.ps1 once (it creates the project and builds the bundle)"
}

if (-not $ShaderBundle) { $ShaderBundle = Join-Path $root "FoliageFX.assetbundle" }
$ShaderBundle = FullPath $ShaderBundle
if (-not (Test-Path -LiteralPath $ShaderBundle)) { Fail "$ShaderBundle missing: run pwsh tools/bundle.ps1" }
$Out = if ($Out) { FullPath $Out } else { Join-Path $root "dist/render" }

# Each tree's bundle as a full path: Unity does not run in this folder.
$specs = @()
foreach ($spec in ($Trees -split '\|' | Where-Object { $_.Trim() })) {
    $parts = @($spec -split '::')
    if ($parts.Count -lt 2) { Fail "a tree is <bundle>::<prefab path>::<label>, got '$spec'" }
    $parts[0] = FullPath $parts[0].Trim()
    if (-not (Test-Path -LiteralPath $parts[0])) { Fail "tree bundle not found: $($parts[0])" }
    $specs += ($parts -join '::')
}

$editor = Join-Path $proj "Assets/Editor"
New-Item -ItemType Directory -Force $editor, $Out | Out-Null
$src = Join-Path $PSScriptRoot $script
$dst = Join-Path $editor $script
if (-not (Test-Path $dst) -or (HashBytes $dst) -ne (HashBytes $src)) { Copy-Item -LiteralPath $src $dst -Force }

$log = Join-Path $root "dist/render.log"
if (Test-Path $log) { Remove-Item $log -Force }
$started = Get-Date
Write-Host "unity: rendering $($specs.Count) trees"
$arguments = @("-batchmode", "-quit", "-projectPath", (Q $proj), "-executeMethod", "Editor_RenderTrees.Run", "-logFile", (Q $log),
    "-shaderBundle", (Q $ShaderBundle), "-outDir", (Q $Out), "-trees", (Q ($specs -join '|')))
$code = (Start-Process -FilePath $exe -ArgumentList $arguments -Wait -PassThru).ExitCode
$text = if (Test-Path $log) { Get-Content $log -Raw } else { "" }
if ($text -match "Multiple Unity instances cannot open the same project|another Unity instance is running") {
    Fail "the project is open in the Unity editor: close it there and run again"
}
foreach ($m in [regex]::Matches($text, '(?m)^RenderTrees: .*$')) { Write-Host $m.Value.TrimEnd() }
if ($code -ne 0) {
    foreach ($m in [regex]::Matches($text, '(?m)^(.*error CS\d+.*|\w*Exception: .*)$')) { Complain $m.Value.TrimEnd() }
    Fail "Unity did not render (exit $code): see dist/render.log"
}
$images = @(Get-ChildItem $Out -Filter *.png | Where-Object { $_.LastWriteTime -ge $started })
Write-Host "render: $($images.Count) images in $Out"
