<#
.SYNOPSIS
    Manual smoke tests for PR #21 — auto-create missing sections / section groups.

.DESCRIPTION
    Walks through the five scenarios listed in the PR #21 test plan:

      1. `--publish --dry-run --create-missing` — confirms `Would create …` preview.
      2. `--publish` (live) — confirms sections actually get created under the
         supplied throwaway notebook.
      3. `--publish --no-create-missing` on a tree with a missing target —
         confirms the clean error + `--create-missing` hint.
      4. `--import` with a missing notebook — confirms `NotebookNotFoundException`
         surfaces with the #19 link.
      5. `--create-missing --no-create-missing` together — confirms parse error
         + exit code 1.

    Between each scenario the script pauses so you can inspect OneNote.

    Nothing is destructive against your real notebooks: the only writes target
    the -Notebook you pass in. When you're done, delete the created sections
    from that notebook manually.

.PARAMETER Notebook
    The name of an existing OneNote notebook you consider throwaway for this
    run. Must exist before you start (notebook-level auto-create is tracked
    by issue #19 and is NOT exercised here).

.PARAMETER ScratchRoot
    Optional scratch directory for the markdown tree. Defaults to a timestamped
    folder under $env:TEMP.

.PARAMETER SkipBuild
    Skip `dotnet build`. Use when you've just built and want to re-run scenarios.

.EXAMPLE
    ./scripts/smoke-pr21.ps1 -Notebook "SampleTest"

.NOTES
    OneNote must be running. Runs the Debug build of OneNoteMarkdownExporter.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Notebook,

    [string]$ScratchRoot,

    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$exe = Join-Path $repoRoot 'OneNoteMarkdownExporter/bin/Debug/net10.0-windows/OneNoteMarkdownExporter.exe'

if (-not $ScratchRoot) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $ScratchRoot = Join-Path $env:TEMP "smoke-pr21-$stamp"
}

function Step-Header {
    param([int]$Number, [string]$Title)
    Write-Host ""
    Write-Host ("=" * 72) -ForegroundColor Cyan
    Write-Host "Scenario $Number — $Title" -ForegroundColor Cyan
    Write-Host ("=" * 72) -ForegroundColor Cyan
}

function Invoke-Exe {
    param([string[]]$Arguments, [string]$Description)
    Write-Host "`n> $Description" -ForegroundColor Yellow
    Write-Host "  command: $exe $($Arguments -join ' ')" -ForegroundColor DarkGray
    & $exe @Arguments
    $code = $LASTEXITCODE
    Write-Host "  exit code: $code" -ForegroundColor DarkGray
    return $code
}

function Pause-For-Inspection {
    param([string]$What)
    Write-Host ""
    Write-Host "→ In OneNote, check: $What" -ForegroundColor Green
    Read-Host "Press Enter to continue to the next scenario"
}

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

Write-Host ""
Write-Host "Notebook:    $Notebook (must already exist in OneNote)" -ForegroundColor Cyan
Write-Host "ScratchRoot: $ScratchRoot" -ForegroundColor Cyan
Write-Host "Executable:  $exe" -ForegroundColor Cyan
Write-Host ""
Read-Host "OneNote desktop running? Ctrl+C to abort, Enter to continue"

# --- Build the scratch tree --------------------------------------------------

New-Item -ItemType Directory -Path $ScratchRoot -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $ScratchRoot 'SubA/SubB') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $ScratchRoot 'Shallow') -Force | Out-Null

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$randomTag = "pr21-$timestamp"

# Deep-nested target — exercises missing-intermediate section-group creation.
$deepContent = @"
---
title: Deep Page ($randomTag)
onenote:
  notebook: "$Notebook"
  section_groups: ["SmokeSubA-$randomTag", "SmokeSubB-$randomTag"]
  section: "DeepSection-$randomTag"
---

# Deep Page

This page exercises nested section-group creation under `$Notebook`.
Run timestamp: $timestamp
"@
Set-Content -Path (Join-Path $ScratchRoot 'SubA/SubB/deep.md') -Value $deepContent -NoNewline

# Shallow target — exercises leaf-only creation.
$shallowContent = @"
---
title: Shallow Page ($randomTag)
onenote:
  notebook: "$Notebook"
  section: "SmokeShallow-$randomTag"
---

# Shallow Page

This page exercises leaf-section creation under `$Notebook` with no section groups.
"@
Set-Content -Path (Join-Path $ScratchRoot 'Shallow/shallow.md') -Value $shallowContent -NoNewline

# Missing-target file — exercises the `--no-create-missing` error path.
# Uses a section name that definitely shouldn't exist; if this false-positives
# (because you happen to have a section by this exact name), rerun and pick a
# different -Notebook.
$missingContent = @"
---
title: Missing Target ($randomTag)
onenote:
  notebook: "$Notebook"
  section: "DoesNotExist-$randomTag-$([guid]::NewGuid().ToString('N').Substring(0,8))"
---

# Missing Target

Scenario 3 expects this to error when --no-create-missing is set.
"@
Set-Content -Path (Join-Path $ScratchRoot 'missing.md') -Value $missingContent -NoNewline

# A throwaway markdown file for the --import smoke test.
$importContent = @"
# Import Smoke

Content for the --import missing-notebook scenario.
"@
Set-Content -Path (Join-Path $ScratchRoot 'import.md') -Value $importContent -NoNewline

Write-Host "Scratch tree written:" -ForegroundColor DarkGray
Get-ChildItem -Path $ScratchRoot -Recurse -File | ForEach-Object {
    Write-Host "  $($_.FullName.Substring($ScratchRoot.Length + 1))" -ForegroundColor DarkGray
}

# --- Scenario 1 — dry-run preview with create-missing ------------------------

Step-Header 1 "--publish --dry-run --verbose --create-missing"
Write-Host "Expected: 'Would create section group: SmokeSubA-...' and" -ForegroundColor DarkGray
Write-Host "          'Would create section group: SmokeSubB-...' and" -ForegroundColor DarkGray
Write-Host "          'Would create section: DeepSection-...' and" -ForegroundColor DarkGray
Write-Host "          'Would create section: SmokeShallow-...'." -ForegroundColor DarkGray
Write-Host "          Nothing written to OneNote. Exit code 0." -ForegroundColor DarkGray

Invoke-Exe -Arguments @('--publish', $ScratchRoot, '--dry-run', '--verbose', '--create-missing') `
           -Description "Dry-run preview"

Write-Host ""
Write-Host "→ In OneNote: nothing should have changed." -ForegroundColor Green
Read-Host "Confirmed? Press Enter to continue"

# --- Scenario 2 — live publish, default-on auto-create ----------------------

Step-Header 2 "--publish (live, auto-create on by default)"
Write-Host "Expected: 'Created section group: SmokeSubA-...' etc." -ForegroundColor DarkGray
Write-Host "          Pages published. Exit code 0." -ForegroundColor DarkGray

# Remove missing.md for this run so the live publish doesn't fail on it.
# We'll bring it back (via regeneration not needed — the file still exists,
# we just pass a different source root). Actually simplest: move it aside
# temporarily.
$missingFile = Join-Path $ScratchRoot 'missing.md'
$missingBackup = "$missingFile.staged"
Move-Item -Path $missingFile -Destination $missingBackup

try {
    Invoke-Exe -Arguments @('--publish', $ScratchRoot, '--verbose') `
               -Description "Live publish with default --create-missing"
}
finally {
    Move-Item -Path $missingBackup -Destination $missingFile
}

Pause-For-Inspection "under '$Notebook', the section groups 'SmokeSubA-$randomTag' → 'SmokeSubB-$randomTag' → 'DeepSection-$randomTag' page, AND a direct-child section 'SmokeShallow-$randomTag' with its page."

# --- Scenario 3 — --no-create-missing on a missing target -------------------

Step-Header 3 "--publish --no-create-missing on a missing section"
Write-Host "Expected: non-zero exit. Error message mentions 'Section not found'" -ForegroundColor DarkGray
Write-Host "          and 'Pass --create-missing to create it automatically.'" -ForegroundColor DarkGray

# Point at just the missing.md file by publishing a subfolder that contains
# only it.
$missingOnlyRoot = Join-Path $ScratchRoot 'missing-only'
New-Item -ItemType Directory -Path $missingOnlyRoot -Force | Out-Null
Copy-Item -Path $missingFile -Destination (Join-Path $missingOnlyRoot 'missing.md')

Invoke-Exe -Arguments @('--publish', $missingOnlyRoot, '--no-create-missing', '--verbose') `
           -Description "Publish with --no-create-missing"

Write-Host ""
Write-Host "→ Did the output show 'Section not found' + a --create-missing hint?" -ForegroundColor Green
Read-Host "Confirmed? Press Enter to continue"

# --- Scenario 4 — --import with a missing notebook --------------------------

Step-Header 4 "--import with a missing notebook (+ --create-missing)"
Write-Host "Expected: NotebookNotFoundException with 'issues/19' link." -ForegroundColor DarkGray
Write-Host "          Non-zero exit." -ForegroundColor DarkGray

$fakeNotebook = "SmokeNoSuchNotebook-$randomTag"
Invoke-Exe -Arguments @('--import', "$fakeNotebook/AnySection", '--file', (Join-Path $ScratchRoot 'import.md'), '--create-missing', '--verbose') `
           -Description "Import into notebook that doesn't exist"

Write-Host ""
Write-Host "→ Did the output mention 'Notebook not found' and an issues/19 link?" -ForegroundColor Green
Read-Host "Confirmed? Press Enter to continue"

# --- Scenario 5 — mutual exclusion -----------------------------------------

Step-Header 5 "--create-missing + --no-create-missing together"
Write-Host "Expected: parse error, exit code 1, stderr says the flags are" -ForegroundColor DarkGray
Write-Host "          mutually exclusive." -ForegroundColor DarkGray

Invoke-Exe -Arguments @('--publish', $ScratchRoot, '--create-missing', '--no-create-missing') `
           -Description "Pass both flags"

Write-Host ""
Write-Host "→ Exit code 1 and a clear 'mutually exclusive' error?" -ForegroundColor Green
Read-Host "Confirmed? Press Enter to finish"

# --- Done --------------------------------------------------------------------

Write-Host ""
Write-Host "All 5 scenarios walked." -ForegroundColor Cyan
Write-Host ""
Write-Host "Cleanup reminder: remove the sections created under '$Notebook' when done:" -ForegroundColor Yellow
Write-Host "  - Section group 'SmokeSubA-$randomTag'" -ForegroundColor Yellow
Write-Host "  - Section 'SmokeShallow-$randomTag'" -ForegroundColor Yellow
Write-Host ""
Write-Host "Scratch tree left at: $ScratchRoot" -ForegroundColor DarkGray
