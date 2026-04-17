# Samples corpus + smoke script — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reorganize `samples/` into a realistic docs-repo layout and add `scripts/smoke-samples.ps1` to publish it into a throwaway OneNote notebook for visual verification.

**Architecture:** Content-and-scripting change, no production-code touches. Existing flat sample files are `git mv`'d into nested folders with `{{Notebook}}`-templated front-matter added; new files fill in the gaps (getting-started, reference/cli, dot-path example, no-target example). One PowerShell script copies `samples/` to `$env:TEMP`, substitutes the placeholder, and invokes the existing `--publish` CLI.

**Tech Stack:** Markdown files + YAML front-matter (parsed by the existing `FrontMatterParser`), PowerShell 5+ for the script, no automated tests — the corpus IS the test.

**Spec:** `docs/superpowers/specs/2026-04-17-samples-corpus-design.md`

**Conventions:**
- Use `git mv` for existing files so history follows.
- **No `Co-Authored-By: Claude …` trailer** on any commit (project convention).
- One commit per task unless noted.
- No automated test changes — running the smoke script against a real OneNote notebook is the verification step.

---

## Task 1: Create the feature branch

**Files:** none (git only).

- [ ] **Step 1: Verify clean tree and up-to-date master**

```bash
git status
git checkout master
git pull
```

Expected: "working tree clean", master at the latest commit (includes `f559d72 docs: add samples-corpus + smoke-script design spec`).

- [ ] **Step 2: Create the branch**

```bash
git checkout -b chore/samples-corpus-smoke
```

---

## Task 2: Port existing 5 sample files into their new homes

**Files (5 moves + content edits):**
- Move: `samples/basic-formatting.md` → `samples/reference/formatting/basic.md`
- Move: `samples/code-and-quotes.md` → `samples/reference/formatting/code-and-quotes.md`
- Move: `samples/lists-and-tables.md` → `samples/reference/formatting/lists-and-tables.md`
- Move: `samples/collapsible-sections.md` → `samples/reference/formatting/collapsible.md`
- Move: `samples/with-image.md` → `samples/examples/with-image.md`

All moves use `git mv` so file history follows. After each move, prepend front-matter. For `with-image.md`, also rewrite the image paths.

- [ ] **Step 1: Create target directories**

```bash
mkdir -p samples/reference/formatting samples/reference/cli samples/examples samples/getting-started
```

- [ ] **Step 2: `git mv` all 5 files**

```bash
git mv samples/basic-formatting.md samples/reference/formatting/basic.md
git mv samples/code-and-quotes.md samples/reference/formatting/code-and-quotes.md
git mv samples/lists-and-tables.md samples/reference/formatting/lists-and-tables.md
git mv samples/collapsible-sections.md samples/reference/formatting/collapsible.md
git mv samples/with-image.md samples/examples/with-image.md
```

- [ ] **Step 3: Prepend front-matter to `samples/reference/formatting/basic.md`**

Insert at the very top of the file (before the existing `# Basic Formatting Test` line):

```
---
title: "Basic Formatting"
onenote:
  notebook: "{{Notebook}}"
---

```

(Note the trailing blank line before the existing `# H1`.)

- [ ] **Step 4: Prepend front-matter to `samples/reference/formatting/code-and-quotes.md`**

```
---
title: "Code Blocks and Quotes"
onenote:
  notebook: "{{Notebook}}"
---

```

- [ ] **Step 5: Prepend front-matter to `samples/reference/formatting/lists-and-tables.md`**

```
---
title: "Lists and Tables"
onenote:
  notebook: "{{Notebook}}"
---

```

- [ ] **Step 6: Prepend front-matter to `samples/reference/formatting/collapsible.md`**

```
---
title: "Collapsible Sections"
onenote:
  notebook: "{{Notebook}}"
---

```

- [ ] **Step 7: Prepend front-matter AND rewrite image paths in `samples/examples/with-image.md`**

Replace the file's `![Project Logo](assets/image.png)` with `![Project Logo](../assets/image.png)` and `![Missing](assets/nonexistent.png)` with `![Missing](../assets/nonexistent.png)`.

Prepend:

```
---
title: "Page with Image"
onenote:
  notebook: "{{Notebook}}"
---

```

Final file shape:

```markdown
---
title: "Page with Image"
onenote:
  notebook: "{{Notebook}}"
---

# Page With Image

This page tests local image embedding.

## The Logo

Here is our logo:

![Project Logo](../assets/image.png)

And here is text after the image.

## Missing Image

This references a file that doesn't exist:

![Missing](../assets/nonexistent.png)

It should show a placeholder instead of crashing.
```

- [ ] **Step 8: Commit**

```bash
git add samples/
git commit -m "chore(samples): relocate existing demo files into nested docs layout"
```

---

## Task 3: Create `getting-started/` stubs

**Files:**
- Create: `samples/getting-started/overview.md`
- Create: `samples/getting-started/installation.md`

- [ ] **Step 1: Write `samples/getting-started/overview.md`**

```markdown
---
title: "Getting Started Overview"
onenote:
  notebook: "{{Notebook}}"
---

# Getting Started

Welcome to the sample docs repo. This page demonstrates the simplest
shape a publishable Markdown note can take: a `title` in front-matter,
one H1, and plain prose.

Visit the [Microsoft OneNote](https://www.onenote.com) site for more
background on OneNote itself.

## What's next

Start with **Installation** for the step-by-step setup, or jump straight
to the **Reference** section for CLI flags and formatting details.
```

- [ ] **Step 2: Write `samples/getting-started/installation.md`**

```markdown
---
title: "Installation"
onenote:
  notebook: "{{Notebook}}"
---

# Installation

Follow these steps to set up the sample project.

1. Clone the repository
2. Install the .NET SDK
3. Run `dotnet build` from the repo root
4. Run `dotnet test` to verify

## Supported runtimes

| Runtime | Version | Notes |
|---------|---------|-------|
| .NET | 10.0 | Windows only |
| PowerShell | 5.1+ | For smoke scripts |
| OneNote | Desktop | Microsoft 365 or Office 365 |

This page exercises numbered lists, inline code, and a small table.
```

- [ ] **Step 3: Commit**

```bash
git add samples/getting-started/
git commit -m "chore(samples): add getting-started/ stubs"
```

---

## Task 4: Create `reference/cli/` stubs

**Files:**
- Create: `samples/reference/cli/import.md`
- Create: `samples/reference/cli/publish.md`

- [ ] **Step 1: Write `samples/reference/cli/import.md`**

```markdown
---
title: "--import"
onenote:
  notebook: "{{Notebook}}"
---

# --import

Imports one or more Markdown files into an existing OneNote notebook
and section. This page demonstrates a **2-level** section-group
nesting: the folder path `reference/cli/` resolves to a section group
`reference` containing a section group `cli`, with this file landing
as a section named `import`.

## Synopsis

```
OneNoteMarkdownExporter.exe --import "Notebook/Section" --file notes.md
```

## Flags

- `--file <path>...` — one or more Markdown files to import.
- `--no-collapse` — emit headings as siblings rather than nesting.
- `--create-missing` — auto-create the target section if missing.
- `--dry-run` — preview without calling OneNote.

See the full CLI surface in `docs/importer.md`.
```

- [ ] **Step 2: Write `samples/reference/cli/publish.md`**

```markdown
---
title: "--publish"
onenote:
  notebook: "{{Notebook}}"
---

# --publish

Walks a Markdown source tree and publishes every file with `onenote:`
front-matter (or every file when `--notebook` bulk mode is used) to
OneNote.

## Synopsis

```
OneNoteMarkdownExporter.exe --publish ./notes [--dry-run] [--notebook "Target"]
```

## Flags

- `--dry-run` — preview without writing to OneNote.
- `--notebook <name>` — bulk mode: publish every `.md` into this
  notebook regardless of front-matter (front-matter still wins
  per-file if present).
- `--no-create-missing` — disable the default auto-create behavior.

## Resolution rule

The full resolution rule (folder path + front-matter + CLI flag →
target notebook / section / page) is documented in
`docs/superpowers/specs/2026-04-16-folder-tree-mapping-design.md`.
```

- [ ] **Step 3: Commit**

```bash
git add samples/reference/cli/
git commit -m "chore(samples): add reference/cli/ stubs"
```

---

## Task 5: Create the dot-path file

**Files:**
- Create: `samples/reference.api.endpoints.md`

- [ ] **Step 1: Write `samples/reference.api.endpoints.md`**

```markdown
---
title: "API Endpoints (dot-path example)"
onenote:
  notebook: "{{Notebook}}"
---

# API Endpoints

This file's name uses **dots as hierarchy delimiters**. The path
`reference.api.endpoints.md` resolves identically to
`reference/api/endpoints.md`, landing as section `endpoints` inside
section group `api` inside section group `reference`.

See the folder-tree mapping spec for the full rule:
`docs/superpowers/specs/2026-04-16-folder-tree-mapping-design.md`.

## Why this matters

Dot-style paths are a Dendron convention; this sample exercises the
publisher's dot-splitting logic in the same corpus as the folder-style
paths (see `reference/cli/` and `reference/formatting/`).
```

- [ ] **Step 2: Commit**

```bash
git add samples/reference.api.endpoints.md
git commit -m "chore(samples): add dot-path example file"
```

---

## Task 6: Create the no-target example

**Files:**
- Create: `samples/examples/pure-markdown.md`

- [ ] **Step 1: Write `samples/examples/pure-markdown.md`**

No front-matter at all — just the H1 and body.

```markdown
# Pure Markdown

This file has no `onenote:` front-matter, so `--publish` skips it
silently. It represents the realistic case where a docs repo contains
notes that only render on a static site or are drafts not ready to
publish.
```

- [ ] **Step 2: Commit**

```bash
git add samples/examples/pure-markdown.md
git commit -m "chore(samples): add no-front-matter example (silently skipped)"
```

---

## Task 7: Write `scripts/smoke-samples.ps1`

**Files:**
- Create: `scripts/smoke-samples.ps1`

- [ ] **Step 1: Write the script**

```powershell
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

$mdFiles = Get-ChildItem -Path $scratch -Recurse -Filter '*.md' -File
$substitutedCount = 0
foreach ($file in $mdFiles) {
    $content = Get-Content -Path $file.FullName -Raw
    $replaced = $content.Replace('{{Notebook}}', $Notebook)
    if ($replaced -ne $content) {
        Set-Content -Path $file.FullName -Value $replaced -NoNewline
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

$keepAnyway = $KeepScratch -or $DryRun -or ($exitCode -ne 0 -and -not $DryRun)
if (-not $keepAnyway) {
    Remove-Item -Path $scratch -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Scratch removed: $scratch" -ForegroundColor DarkGray
}
else {
    Write-Host "Scratch kept at: $scratch" -ForegroundColor DarkGray
}

exit $exitCode
```

- [ ] **Step 2: Smoke-test the script with `-DryRun` against a real notebook**

This step requires OneNote running and a throwaway notebook. From a PowerShell prompt in the repo root:

```powershell
./scripts/smoke-samples.ps1 -Notebook "SamplesDemo" -DryRun
```

Expected: exit code 0. Output includes `[dry-run]` lines for every targeted file, `Would create …` lines for missing intermediates, and NO line mentioning `examples/pure-markdown.md` (silently skipped).

If the script errors or the output shape is wrong, fix before committing.

- [ ] **Step 3: Commit**

```bash
git add scripts/smoke-samples.ps1
git commit -m "feat(scripts): add smoke-samples.ps1 for visual regression verification"
```

---

## Task 8: Update `docs/importer.md`

**Files:**
- Modify: `docs/importer.md` — add a new `### Samples corpus` subsection.

- [ ] **Step 1: Locate the insertion point**

Open `docs/importer.md`. Find the `### Auto-create missing sections` subsection (added in PR #21). The new subsection goes immediately after the end of that subsection, **before** the next section starts (likely "Known limitations" or "Reference material").

Use `grep -n "Auto-create missing sections" docs/importer.md` to find the start line, then scan forward to the end of that subsection.

- [ ] **Step 2: Insert the new subsection**

```markdown
### Samples corpus

`samples/` in the repo is a minimal docs-repo layout demonstrating the
conventions above: folder-inferred section groups, a dot-path file
(`reference.api.endpoints.md`), an image via relative path, and a
plain-markdown file with no `onenote:` key to verify silent skipping.

To publish the corpus into a throwaway OneNote notebook for visual
verification:

```powershell
./scripts/smoke-samples.ps1 -Notebook "SamplesDemo"
```

Add `-DryRun` to preview without writing. See the script's
comment-help block for the full parameter list.
```

**Note on nested code fences:** the `powershell` code block inside the
subsection needs to render as a code fence in the final `importer.md`
file. If your editor doesn't handle the nested triple-backticks when
you paste this, adjust by using four backticks on the outer fence when
authoring the final markdown — the `importer.md` file itself only has
the triple-backtick inner fence.

- [ ] **Step 3: Commit**

```bash
git add docs/importer.md
git commit -m "docs: describe samples corpus + smoke-samples.ps1 in importer guide"
```

---

## Task 9: README pointer (optional)

**Files:**
- Modify: `README.md` (only if a natural home exists).

A pre-flight check on the current README found no existing `samples/` references. The README is end-user-focused (download, install, GUI/CLI modes); a contributor-facing samples pointer doesn't fit naturally.

- [ ] **Step 1: Inspect the README**

Skim `README.md` for any section where a sample-corpus pointer would be organic (e.g., a "Contributing" section, a "Features" bullet about authoring). If you find one, add:

```markdown
See `samples/` for a working docs-repo layout and
`scripts/smoke-samples.ps1` to publish it into a throwaway notebook.
```

- [ ] **Step 2: If no natural home, skip this task**

`docs/importer.md` already carries the load. Do not shoehorn a pointer
into an unrelated section.

- [ ] **Step 3: Commit (if Step 1 added anything)**

```bash
git add README.md
git commit -m "docs(readme): pointer to samples corpus + smoke script"
```

---

## Task 10: Update `CHANGELOG.md`

**Files:**
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Add entries to `[Unreleased]`**

Under `[Unreleased]`, add (or extend) a `### Changed` and `### Added` section:

```markdown
### Changed

- Reorganized `samples/` from a flat set of feature-demo files into a
  realistic docs-repo layout (`getting-started/`, `reference/`,
  `examples/`), with `{{Notebook}}`-templated front-matter so the
  corpus is target-agnostic.

### Added

- `scripts/smoke-samples.ps1` — builds + publishes the samples corpus
  into a throwaway OneNote notebook for visual verification. Supports
  `-DryRun`, `-SkipBuild`, `-KeepScratch`.
```

If either subsection already exists under `[Unreleased]`, append to it rather than creating a duplicate.

- [ ] **Step 2: Commit**

```bash
git add CHANGELOG.md
git commit -m "docs: CHANGELOG entry for samples reorg + smoke-samples.ps1"
```

---

## Task 11: Full test suite + build check

**Files:** none.

- [ ] **Step 1: Build the solution**

```bash
dotnet build --nologo
```

Expected: build succeeds, 0 warnings, 0 errors. The existing test `DumpSampleXml_ForManualInspection` may reference old sample paths — check the test output.

- [ ] **Step 2: Run the full test suite**

```bash
dotnet test --nologo
```

Expected: all tests pass. If any test references the old flat sample paths (e.g. `samples/basic-formatting.md`), the test needs updating — but per the spec's grep, only `samples/output/` and the text "samples/ directory" appear in test source, neither of which depends on the flat-file structure.

- [ ] **Step 3: If any test fails, stop and diagnose**

Do NOT blindly update test paths. If a test was exercising a specific flat-file path, investigate WHY before deciding whether to update the path or leave the old file where it was. Report as BLOCKED if unclear.

---

## Task 12: Manual smoke verification

**Files:** none.

- [ ] **Step 1: Run the script in dry-run against a real throwaway OneNote notebook**

(Requires OneNote desktop running and a notebook named e.g. `SamplesDemo` that exists.)

```powershell
./scripts/smoke-samples.ps1 -Notebook "SamplesDemo" -DryRun
```

Expected output checklist:

- `[dry-run]` line for every file EXCEPT `examples/pure-markdown.md`.
- `Would create section group: getting-started` or equivalent for each missing intermediate.
- `Would create section: <slug>` for each leaf section.
- `reference.api.endpoints.md` appears with target path
  `SamplesDemo/reference/api/endpoints/endpoints` — demonstrating the
  dot-stem split resolved to nested section groups.
- No `Error:` lines.
- Exit code 0.

- [ ] **Step 2: (Optional) Run a live publish if you want to eyeball OneNote**

```powershell
./scripts/smoke-samples.ps1 -Notebook "SamplesDemo"
```

Open OneNote. Confirm:
- `SamplesDemo / getting-started` section group exists with two pages.
- `SamplesDemo / reference / cli` and `SamplesDemo / reference / formatting` each contain their respective pages.
- `SamplesDemo / reference / api / endpoints` exists as a section (from the dot-path file).
- `SamplesDemo / examples / with-image` renders the image.
- No section/page corresponding to `pure-markdown.md`.

- [ ] **Step 3: Clean up OneNote sections manually**

Per the script's note, there's no automatic cleanup. Delete what you created in OneNote when you're done eyeballing.

---

## Task 13: Push branch and open PR

**Files:** none.

- [ ] **Step 1: Push**

```bash
git push -u origin chore/samples-corpus-smoke
```

- [ ] **Step 2: Open the PR**

```bash
gh pr create --title "chore(samples): realistic docs-repo layout + smoke-samples.ps1" --body "$(cat <<'EOF'
## Summary

- Reorganize `samples/` from 5 flat feature-demo files into a realistic
  docs-repo layout (`getting-started/`, `reference/{cli,formatting}/`,
  `examples/`) with `{{Notebook}}`-templated front-matter.
- Add `samples/reference.api.endpoints.md` to exercise the dot-path
  hierarchy rule.
- Add `samples/examples/pure-markdown.md` (no front-matter) to verify
  silent-skip behavior.
- New script `scripts/smoke-samples.ps1` copies the corpus to
  `$env:TEMP`, substitutes the placeholder, and invokes
  `--publish --create-missing`. Flags: `-DryRun`, `-SkipBuild`,
  `-KeepScratch`.

Design: `docs/superpowers/specs/2026-04-17-samples-corpus-design.md`.

## Test plan

- [x] `dotnet build` + `dotnet test` green.
- [ ] Manual: `./scripts/smoke-samples.ps1 -Notebook "SamplesDemo" -DryRun` — confirm [dry-run] lines for every targeted file, `Would create …` for missing intermediates, no line for `pure-markdown.md`.
- [ ] Manual: live run against a throwaway notebook — confirm the resulting OneNote tree matches the samples layout.
- [ ] Manual: verify `reference.api.endpoints.md` lands under nested section groups `reference / api / endpoints`.
EOF
)"
```

---

## Post-merge cleanup

After the PR squash-merges:

- [ ] `git branch -D chore/samples-corpus-smoke` locally.
- [ ] Delete the OneNote `SamplesDemo` notebook contents (manual).
