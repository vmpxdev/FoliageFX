# FoliageFX compile gate. Lints, then compiles the cslist in order against the
# local VaM managed assemblies with the .NET Framework C# 5 compiler (the same
# language level VaM's sandbox accepts). Produces no artifact VaM uses; the
# .dll goes to TEMP and exists only to surface errors before loading in VaM.
#
#   pwsh tools/compile.ps1
#   pwsh tools/compile.ps1 -VamRoot "E:\Games\VaM"    # if the folder is not inside the install
#   pwsh tools/compile.ps1 -NoLint
#   pwsh tools/compile.ps1 -Root <folder>            # another folder holding a FoliageFX.cslist (the packager's staged copy)
param(
    [string]$VamRoot,
    [string]$Out,
    [string]$Root,
    [switch]$NoLint
)

$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
$root = if ($Root) { (Resolve-Path $Root).Path } else { $repo }

if (-not $NoLint) {
    & (Join-Path $PSScriptRoot "lint.ps1") -Root $root
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if (-not $VamRoot) {
    # <VaM>/Custom/Scripts/Vampix Collections/FoliageFX -> up four
    $VamRoot = (Resolve-Path (Join-Path $repo "..\..\..\..")).Path
}
$managed = Join-Path $VamRoot "VaM_Data\Managed"
if (-not (Test-Path (Join-Path $managed "Assembly-CSharp.dll"))) {
    Write-Error "VaM managed assemblies not found under $managed (pass -VamRoot)"
    exit 2
}

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { Write-Error "csc.exe not found at $csc"; exit 2 }

if (-not $Out) { $Out = Join-Path $env:TEMP "FoliageFX.check.dll" }

$cslist = Join-Path $root "FoliageFX.cslist"
$files = Get-Content $cslist | ForEach-Object { $_.Trim() } | Where-Object { $_ } | ForEach-Object { Join-Path $root $_ }
foreach ($f in $files) {
    if (-not (Test-Path $f)) { Write-Error "cslist entry missing: $f"; exit 2 }
}

$refNames = @(
    "mscorlib", "System", "System.Core",
    "UnityEngine", "UnityEngine.CoreModule", "UnityEngine.UI", "UnityEngine.UIModule",
    "UnityEngine.PhysicsModule", "UnityEngine.IMGUIModule", "UnityEngine.AssetBundleModule",
    "UnityEngine.TextRenderingModule", "UnityEngine.ImageConversionModule",
    "UnityEngine.ParticleSystemModule",
    "Assembly-CSharp"
)
$refs = @()
foreach ($n in $refNames) {
    $p = Join-Path $managed "$n.dll"
    if (Test-Path $p) { $refs += "/r:$p" }
}

& $csc /nologo /noconfig /nostdlib+ /target:library /langversion:5 /warn:3 "/out:$Out" @refs @files
$code = $LASTEXITCODE
if ($code -eq 0) {
    Write-Host ("compile: ok ({0} files) -> {1}" -f $files.Count, $Out)
} else {
    Write-Host "compile: FAILED"
}
exit $code
