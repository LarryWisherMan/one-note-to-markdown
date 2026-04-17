<#
.SYNOPSIS
    Build + publish the samples/ corpus into a throwaway OneNote notebook
    for visual verification.

.DESCRIPTION
    Copies samples/ to a timestamped temp folder, substitutes the
    {{Notebook}} placeholder in every front-matter block, then invokes
    OneNoteMarkdownExporter.exe --publish against the copy.

    Nothing is destructive against your real notebooks: only the -Notebook
    you pass in gets written to. When you're done, delete the created
    sections from that notebook manually (idempotent re-publish is
    tracked by issue #6).

.PARAMETER Notebook
    The name of an existing OneNote notebook you consider throwaway for
    this run. Must exist before you start — notebook-level auto-create
    is tracked by issue #19 and is NOT exercised here.

.PARAMETER DryRun
    Run --publish --dry-run --verbose --create-missing (preview the walk
    without writing to OneNote).

.PARAMETER SkipBuild
    Skip `dotnet build`. Use when you've just built and want to re-run.

.PARAMETER KeepScratch
    Leave the copied samples temp folder around for inspection.

.EXAMPLE
    ./scripts/smoke-samples.ps1 -Notebook "SamplesDemo"

.EXAMPLE
    ./scripts/smoke-samples.ps1 -Notebook "SamplesDemo" -DryRun

.NOTES
    OneNote must be running. Runs the Debug build of OneNoteMarkdownExporter.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Notebook,

    [switch]$DryRun,
    [switch]$SkipBuild,
    [switch]$KeepScratch
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Notebook)) {
    throw "-Notebook cannot be empty or whitespace."
}

if ($Notebook.Contains('"')) {
    # Substitution replaces the placeholder inside a quoted YAML scalar
    # (notebook: "{{Notebook}}"). A literal double-quote in the notebook
    # name would break out of the scalar and produce invalid front-matter.
    throw "-Notebook must not contain a double-quote character."
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$samplesSrc = Join-Path $repoRoot 'samples'
$exe = Join-Path $repoRoot 'OneNoteMarkdownExporter/bin/Debug/net10.0-windows/OneNoteMarkdownExporter.exe'

# --- Preflight ---------------------------------------------------------------

if (-not $SkipBuild) {
    Write-Host "Building Debug..." -ForegroundColor Yellow
    & dotnet build (Join-Path $repoRoot 'OneNoteMarkdownExporter') -c Debug --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed — aborting."
    }
}

if (-not (Test-Path $exe)) {
    throw "Executable not found: $exe. Run without -SkipBuild."
}

if (-not (Test-Path $samplesSrc)) {
    throw "Samples directory not found: $samplesSrc."
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$scratch = Join-Path $env:TEMP "samples-smoke-$stamp"

$mode = if ($DryRun) { 'DRY RUN' } else { 'LIVE' }

Write-Host ""
Write-Host "Notebook:    $Notebook (must already exist in OneNote)" -ForegroundColor Cyan
Write-Host "ScratchRoot: $scratch" -ForegroundColor Cyan
Write-Host "Mode:        $mode" -ForegroundColor Cyan
Write-Host "Executable:  $exe" -ForegroundColor Cyan
Write-Host ""
Read-Host "OneNote desktop running? Ctrl+C to abort, Enter to continue"

# --- Copy + substitute -------------------------------------------------------

Copy-Item -Recurse -Path $samplesSrc -Destination $scratch

# samples/output/ is a gitignored dir of stale converter test artifacts.
# Drop it from the scratch copy so nothing in it gets walked or substituted.
$scratchOutput = Join-Path $scratch 'output'
if (Test-Path $scratchOutput) {
    Remove-Item -Path $scratchOutput -Recurse -Force
}

$mdFiles = Get-ChildItem -Path $scratch -Recurse -Filter '*.md' -File
$substitutedCount = 0
foreach ($file in $mdFiles) {
    $content = Get-Content -Path $file.FullName -Raw
    $replaced = $content.Replace('{{Notebook}}', $Notebook)
    if ($replaced -ne $content) {
        Set-Content -Path $file.FullName -Value $replaced -NoNewline -Encoding UTF8
        $substitutedCount++
    }
}

Write-Host ""
Write-Host "Scratch populated: $($mdFiles.Count) .md file(s), $substitutedCount substituted." -ForegroundColor DarkGray

# --- Run the publish ---------------------------------------------------------

function Invoke-Exe {
    param([string[]]$Arguments, [string]$Description)
    Write-Host "`n> $Description" -ForegroundColor Yellow
    Write-Host "  command: $exe $($Arguments -join ' ')" -ForegroundColor DarkGray

    # OneNoteMarkdownExporter is a WPF WinExe; `& $exe` returns before the
    # real exit code is known. Redirect stdio and use Start-Process -Wait.
    $stdoutFile = [System.IO.Path]::GetTempFileName()
    $stderrFile = [System.IO.Path]::GetTempFileName()
    try {
        $proc = Start-Process -FilePath $exe -ArgumentList $Arguments `
            -NoNewWindow -Wait -PassThru `
            -RedirectStandardOutput $stdoutFile `
            -RedirectStandardError $stderrFile
        $stdout = Get-Content -Path $stdoutFile -Raw
        $stderr = Get-Content -Path $stderrFile -Raw
        if ($stdout) { Write-Host $stdout }
        if ($stderr) { Write-Host $stderr -ForegroundColor Red }
        Write-Host "  exit code: $($proc.ExitCode)" -ForegroundColor DarkGray
        return $proc.ExitCode
    }
    finally {
        Remove-Item -Path $stdoutFile, $stderrFile -ErrorAction SilentlyContinue
    }
}

$publishArgs = if ($DryRun) {
    @('--publish', $scratch, '--dry-run', '--verbose', '--create-missing')
} else {
    @('--publish', $scratch, '--verbose', '--create-missing')
}

$exitCode = Invoke-Exe -Arguments $publishArgs -Description "Publish samples corpus"

# --- Report + cleanup --------------------------------------------------------

Write-Host ""
if ($DryRun) {
    Write-Host "Dry-run complete. Inspect the 'Would create …' lines above." -ForegroundColor Cyan
}
elseif ($exitCode -eq 0) {
    Write-Host "Live publish complete. Open OneNote and inspect '$Notebook'." -ForegroundColor Green
    Write-Host "Expected: section groups 'getting-started', 'reference' (with nested 'api', 'cli', 'formatting'), and 'examples', each holding the published pages. 'examples/pure-markdown.md' should NOT appear (no front-matter = silently skipped)." -ForegroundColor Green
}
else {
    Write-Host "Live publish FAILED (exit $exitCode). Scratch kept for debugging." -ForegroundColor Red
}

$failed = $exitCode -ne 0
$keepAnyway = $KeepScratch -or $DryRun -or $failed
if (-not $keepAnyway) {
    Remove-Item -Path $scratch -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Scratch removed: $scratch" -ForegroundColor DarkGray
}
else {
    Write-Host "Scratch kept at: $scratch" -ForegroundColor DarkGray
}

exit $exitCode
