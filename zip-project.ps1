# zip-project.ps1
# Zips all project files, honoring .gitignore exclusions.
# Output: MendixProject.zip in the script's directory.

param(
    [string]$OutputPath = "$PSScriptRoot\JevLaya.zip"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot

# ---------------------------------------------------------------------------
# Parse .gitignore into exclude patterns
# ---------------------------------------------------------------------------
$gitignorePath = Join-Path $root '.gitignore'
$rawPatterns = @()
if (Test-Path $gitignorePath) {
    $rawPatterns = Get-Content $gitignorePath |
        Where-Object { $_ -notmatch '^\s*#' -and $_.Trim() -ne '' }
}

# Separate negation (!) patterns from normal exclude patterns.
# Negation patterns re-include files that a broader pattern excluded.
$negationPatterns = [System.Collections.Generic.List[string]]::new()
$excludePatterns  = [System.Collections.Generic.List[string]]::new()

foreach ($p in $rawPatterns) {
    if ($p.StartsWith('!')) {
        $negationPatterns.Add($p.Substring(1).TrimStart('/'))
    } else {
        $excludePatterns.Add($p.TrimStart('/'))
    }
}

# ---------------------------------------------------------------------------
# Helper: test whether a repo-relative path matches a gitignore glob
# ---------------------------------------------------------------------------
function Test-GitignorePattern {
    param([string]$RelPath, [string]$Pattern)

    # Normalise separators
    $rel  = $RelPath  -replace '\\', '/'
    $pat  = $Pattern  -replace '\\', '/'

    # Pattern ends with / -> directory-only match; we handle by checking
    # whether any prefix of the path matches (handled by caller for dirs).
    $dirOnly = $pat.EndsWith('/')
    $pat = $pat.TrimEnd('/')

    # ** matches any number of path segments
    $regexPat = [regex]::Escape($pat) `
        -replace '\\\*\\\*/',  '(.+/)?' `
        -replace '\\\*\\\*',   '.*'     `
        -replace '\\\*',       '[^/]*'  `
        -replace '\\\?',       '[^/]'

    # If pattern contains no slash (after trimming leading /), match against
    # the basename as well as the full path.
    if ($pat -notmatch '/') {
        $basename = ($rel -split '/')[-1]
        if ($basename -match "^$regexPat$") { return $true }
        # Also match any path segment
        if ($rel -match "(^|/)$regexPat(/|$)") { return $true }
    } else {
        if ($rel -match "^$regexPat(/.*)?$") { return $true }
    }
    return $false
}

function Should-Exclude {
    param([string]$RelPath)

    $excluded = $false
    foreach ($pat in $excludePatterns) {
        if (Test-GitignorePattern -RelPath $RelPath -Pattern $pat) {
            $excluded = $true
            break
        }
    }
    if (-not $excluded) { return $false }

    # Check negation patterns
    foreach ($pat in $negationPatterns) {
        if (Test-GitignorePattern -RelPath $RelPath -Pattern $pat) {
            return $false   # re-included
        }
    }
    return $true
}

# ---------------------------------------------------------------------------
# Collect files to include
# ---------------------------------------------------------------------------
Write-Host "Scanning $root ..."

$allFiles = Get-ChildItem -Path $root -Recurse -File -Force

# Also always exclude the output zip itself and this script's own output
$outputName = Split-Path $OutputPath -Leaf

$included = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
$skipped  = 0

foreach ($file in $allFiles) {
    $rel = $file.FullName.Substring($root.Length).TrimStart('\','/')
    $relFwd = $rel -replace '\\', '/'

    # Skip the output zip
    if ($file.Name -eq $outputName) { continue }

    # Skip .git internals
    if ($relFwd -eq '.git' -or $relFwd.StartsWith('.git/')) { $skipped++; continue }

    if (Should-Exclude -RelPath $relFwd) {
        $skipped++
    } else {
        $included.Add($file)
    }
}

Write-Host "  Including : $($included.Count) files"
Write-Host "  Skipping  : $skipped files (matched .gitignore)"

# ---------------------------------------------------------------------------
# Build the zip
# ---------------------------------------------------------------------------
if (Test-Path $OutputPath) {
    Remove-Item $OutputPath -Force
}

Add-Type -Assembly 'System.IO.Compression.FileSystem'
$zip = [System.IO.Compression.ZipFile]::Open($OutputPath, 'Create')

try {
    foreach ($file in $included) {
        $rel = $file.FullName.Substring($root.Length).TrimStart('\','/')
        $entryName = $rel -replace '\\', '/'
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zip, $file.FullName, $entryName,
            [System.IO.Compression.CompressionLevel]::Optimal
        )
    }
} finally {
    $zip.Dispose()
}

$sizeMB = [math]::Round((Get-Item $OutputPath).Length / 1MB, 2)
Write-Host ""
Write-Host "Created: $OutputPath ($sizeMB MB)"
