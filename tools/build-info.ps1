# Writes build.json: which commit this folder holds, and the release it is
# building (the newest vN tag behind it, plus one: every save after a release
# tag counts toward the next version). The git hooks run it after every
# commit, checkout, and merge into the plugin folder (ignored by git), so the
# loose install shows "Development Build · main abc1234"; the packager runs it
# with -Out into the staged var.
#
#   pwsh tools/build-info.ps1                 # -> <repo>/build.json
#   pwsh tools/build-info.ps1 -Out <path>
param(
    [string]$Out
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if (-not $Out) { $Out = Join-Path $root "build.json" }

Push-Location $root
try {
    $branch = (git rev-parse --abbrev-ref HEAD 2>$null)
    $commit = (git rev-parse --short=7 HEAD 2>$null)
    $date = (git log -1 --format=%cs 2>$null)
    $dirty = [bool]((git status --porcelain 2>$null | Where-Object { $_ -notmatch '^\?\? build\.json$' }) | Select-Object -First 1)
    $tag = (git describe --tags --match "v[0-9]*" --abbrev=0 2>$null)
} finally { Pop-Location }
if (-not $commit) { $branch = ""; $commit = ""; $date = ""; $dirty = $false }
$release = 1
if ("$tag" -match '^v(\d+)$') { $release = [int]$Matches[1] + 1 }

$json = [ordered]@{ branch = "$branch"; commit = "$commit"; date = "$date"; dirty = $dirty; release = $release } | ConvertTo-Json -Compress
[System.IO.File]::WriteAllText($Out, $json + "`n", [System.Text.UTF8Encoding]::new($false))
Write-Host ("build.json: {0} {1}{2}, building v{3}" -f $branch, $commit, $(if ($dirty) { " (modified)" } else { "" }), $release)
