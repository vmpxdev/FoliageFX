# One-time setup after cloning: point git at the repo's hooks and write the
# first build.json so the loose install shows its commit.
#
#   pwsh tools/setup.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    # Absolute, so the path holds whatever directory git is spawned from. Note that
    # Cursor's Source Control button runs no hooks at all: its git extension sets
    # core.hooksPath=NUL in the environment of every git it spawns, above any
    # config. Commit and push from a terminal (README, Development and releases).
    git config core.hooksPath (Join-Path $root "tools/hooks").Replace("\", "/")
    Write-Host "hooks: tools/hooks (pre-commit: lint + compile, bundle rebuild when shader/ changed; pre-push: pack check; post-commit/checkout/merge: build.json)"
    Write-Host "bundle: pwsh tools/bundle.ps1 -Project <folder> once, then the hook keeps FoliageFX.assetbundle current (docs/SHADER_BUILD.md)"
} finally { Pop-Location }
& (Get-Process -Id $PID).Path -NoProfile -File (Join-Path $PSScriptRoot "build-info.ps1")
