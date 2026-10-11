# FoliageFX sandbox lint. Fails on constructs VaM's DynamicCSharp compiler
# rejects or crashes on. Run before compile.ps1 (compile.ps1 runs it for you).
#
#   pwsh tools/lint.ps1            # lint Types/**/*.cs
#   pwsh tools/lint.ps1 -Root X    # lint another plugin folder
param(
    [string]$Root = (Split-Path $PSScriptRoot -Parent),
    [string]$Facade = "FoliageFX"
)

$ErrorActionPreference = "Stop"
$typesDir = Join-Path $Root "Types"
if (-not (Test-Path $typesDir)) { Write-Error "No Types folder under $Root"; exit 2 }

# Raw = also check inside comments (VaM scans the source text, not the AST).
$rules = @(
    @{ Name = "enum";                         Pattern = '(^|[\s{;])enum\s+\w' },
    @{ Name = "interface";                    Pattern = '(^|[\s{;])interface\s+\w' },
    @{ Name = "static class";                 Pattern = 'static\s+class\s' },
    @{ Name = "System.IO (even in comments)"; Pattern = 'System\.IO'; Raw = $true },
    @{ Name = "WWW";                          Pattern = '(^|[^\w])WWW\s*\(' },
    @{ Name = "XML see cref";                 Pattern = '<see\s+cref'; Raw = $true },
    @{ Name = "generic method definition";    Pattern = '^\s*(?:(?:public|private|internal|protected|static|override|virtual|sealed)\s+)*[\w\.\[\]<>,]+\s+\w+\s*<\s*\w+(?:\s*,\s*\w+)*\s*>\s*\('; Exclude = '(=|return\s|;)' },
    @{ Name = "foreach over Dictionary Keys/Values"; Pattern = 'foreach\s*\(.*\.(Keys|Values)\b' },
    @{ Name = "foreach over Dictionary pairs";     Pattern = 'foreach\s*\(\s*KeyValuePair<(?!string,\s*JSONNode>)' },
    @{ Name = "namespace";                    Pattern = '^\s*namespace\s' },
    @{ Name = "using static (C# 6)";          Pattern = '^\s*using\s+static\s' },
    @{ Name = "extra public type";            Pattern = ('public\s+(?:sealed\s+|abstract\s+|partial\s+)*(?:class|struct)\s+(?!' + $Facade + '\b)\w') },
    @{ Name = "string interpolation (C# 6)";  Pattern = '\$"' },
    @{ Name = "nameof (C# 6)";                Pattern = '(^|[^\w])nameof\s*\(' },
    @{ Name = "null-conditional (C# 6)";      Pattern = '\?\.\w' }
)

$files = Get-ChildItem -Path $typesDir -Recurse -Filter *.cs
$hits = 0
foreach ($file in $files) {
    $lines = Get-Content -Path $file.FullName
    $inBlock = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $raw = $lines[$i]
        # Strip comments for code-only rules (block comments handled line-wise).
        $code = $raw
        if ($inBlock) {
            $end = $code.IndexOf("*/")
            if ($end -ge 0) { $code = $code.Substring($end + 2); $inBlock = $false } else { $code = "" }
        }
        $code = [regex]::Replace($code, '/\*.*?\*/', '')
        $start = $code.IndexOf("/*")
        if ($start -ge 0) { $code = $code.Substring(0, $start); $inBlock = $true }
        $code = [regex]::Replace($code, '//.*$', '')
        # Drop string literals so words inside quotes do not trip rules.
        $code = [regex]::Replace($code, '"(\\.|[^"\\])*"', '""')

        foreach ($rule in $rules) {
            $subject = if ($rule.Raw) { $raw } else { $code }
            if ($subject -notmatch $rule.Pattern) { continue }
            if ($rule.Exclude -and $subject -match $rule.Exclude) { continue }
            $rel = $file.FullName.Substring($Root.Length).TrimStart('\', '/')
            Write-Host ("{0}:{1}: {2}: {3}" -f $rel, ($i + 1), $rule.Name, $raw.Trim())
            $hits++
        }
    }
}

if ($hits -gt 0) {
    Write-Host ("lint: {0} problem(s) in {1} file(s)" -f $hits, $files.Count)
    exit 1
}
Write-Host ("lint: clean ({0} files)" -f $files.Count)
exit 0
