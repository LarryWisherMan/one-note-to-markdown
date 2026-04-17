# Design: Samples corpus + reusable smoke script

Status: draft for review
Milestone: M2 — Authored Markdown as first-class source

## Overview

Reorganize `samples/` from a flat set of feature-demo files into a realistic
docs-repo layout that demonstrates the folder-tree publishing conventions,
and add a reusable PowerShell smoke script that publishes the corpus into a
throwaway OneNote notebook for visual verification. The corpus doubles as a
long-term manual regression harness: anyone contributing a formatting,
section-group, or publish-path change can run the script and eyeball the
result in OneNote.

## Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Layout shape | Realistic docs repo (`getting-started/`, `reference/{cli,formatting}/`, `examples/`) | Reflects how a user would actually organize content for `--publish`; exercises folder-inferred section groups at multiple depths. |
| Dot-notation coverage | One dot-stem file at the root (`reference.api.endpoints.md`) | Exercises the "dots-as-hierarchy-delimiters" rule from the folder-tree mapping spec in the same corpus as folder paths. |
| No-target file | One plain-markdown file with no `onenote:` key | Exercises the "silently skipped" path; makes the corpus realistic (not every file in a real repo targets OneNote). |
| Notebook targeting | `{{Notebook}}` placeholder in front-matter | Keeps samples target-agnostic. Script substitutes at copy-time. |
| Section / section-group routing | Folder-inferred only | No `onenote.section:` / `onenote.section_groups:` overrides in the corpus — the whole point is to exercise inference. |
| Titles | Explicit `title:` on every targeted file | Verifies front-matter titles override the first-H1 default. |
| Script location | `scripts/smoke-samples.ps1` | Sibling of `smoke-pr21.ps1`; name signals "samples corpus," not PR-specific. |
| Script scope | `--publish` only | Samples demonstrate the publish path. `--import` is a separate feature. |
| Script scratch strategy | Copy `samples/` to `$env:TEMP`, substitute placeholder, publish | Samples stay pure in the repo; each run uses an isolated scratch. |
| Cleanup in OneNote | User responsibility | Matches `smoke-pr21.ps1`'s contract. No programmatic undo. |
| Existing `smoke-pr21.ps1` | Retained as-is | Historical PR-specific fixture; not worth removing. |

## In scope

- Moving existing sample content into the new folder shape (rename/relocate
  the 5 current flat files; content preserved).
- One new dot-stem file (`reference.api.endpoints.md`).
- One new no-front-matter file (`examples/pure-markdown.md`).
- Two new content stubs under `reference/cli/` (`import.md`, `publish.md`)
  and two under `getting-started/` (`overview.md`, `installation.md`).
- Adding `{{Notebook}}` placeholder to all targeted files' front-matter.
- New script `scripts/smoke-samples.ps1`.
- Docs: add a "Samples corpus" subsection to `docs/importer.md`; update any
  stale `samples/`-path references in `README.md` and `docs/importer.md`.
- `CHANGELOG.md` `[Unreleased]` entries.

## Out of scope

- Renaming, removing, or touching `samples/output/` (gitignored build artifacts).
- Adding new image or binary assets beyond the existing `assets/image.png`.
- Linux/Mac adaptation of the script — PowerShell on Windows only, matching
  the rest of the project's test tooling.
- Programmatic verification of OneNote state before/after — visual
  inspection is the point.
- Automatic cleanup of created OneNote sections after a run.
- A sibling `smoke-import.ps1` for single-file `--import` testing — can be
  added later if demand arises; tracked informally.
- Replacing `smoke-pr21.ps1`. It stays.
- Any changes to `docs/reference-page/` (golden-test fixtures — separate
  concern).

## Samples corpus layout

```
samples/
├── getting-started/
│   ├── overview.md
│   └── installation.md
├── reference/
│   ├── cli/
│   │   ├── import.md
│   │   └── publish.md
│   └── formatting/
│       ├── basic.md                    ← content from existing basic-formatting.md
│       ├── code-and-quotes.md          ← content from existing code-and-quotes.md
│       ├── lists-and-tables.md         ← content from existing lists-and-tables.md
│       └── collapsible.md              ← content from existing collapsible-sections.md
├── reference.api.endpoints.md          ← dot-style hierarchy
├── examples/
│   ├── with-image.md                   ← content from existing with-image.md
│   └── pure-markdown.md                ← no front-matter (silently skipped)
└── assets/
    └── image.png                       ← pre-existing, retained at current path
```

### Per-file purpose

| File | Demonstrates | Notes |
|---|---|---|
| `getting-started/overview.md` | Headings, plain prose, inline link. Simplest shape. | New content. |
| `getting-started/installation.md` | Numbered lists, inline code, small GFM table. | New content. |
| `reference/cli/import.md` | 2-level section-group nesting (`reference / cli`). | New content; mirrors `docs/importer.md --import` section. |
| `reference/cli/publish.md` | Same nesting. | New content; mirrors the Tree publish section of `docs/importer.md`. |
| `reference/formatting/basic.md` | Inline bold/italic/strike, inline code, links. | Ported from existing `basic-formatting.md`. |
| `reference/formatting/code-and-quotes.md` | Fenced code blocks, language tags, blockquotes. | Ported from existing `code-and-quotes.md`. |
| `reference/formatting/lists-and-tables.md` | Bullet + numbered lists, nested lists, GFM tables. | Ported from existing `lists-and-tables.md`. |
| `reference/formatting/collapsible.md` | Headings H2–H6, collapsible section nesting. | Ported from existing `collapsible-sections.md`. |
| `reference.api.endpoints.md` | **Dot-path resolution.** Must resolve to the same target as `reference/api/endpoints.md`. | New content; short stub. |
| `examples/with-image.md` | Local image via relative path (`../assets/image.png`). | Ported from existing `with-image.md`; image path updated. |
| `examples/pure-markdown.md` | No `onenote:` front-matter → publisher skips silently. | New content; two-line file. |

### Front-matter convention

Every targeted file uses this shape:

```yaml
---
title: "..."
onenote:
  notebook: "{{Notebook}}"
---
```

No `onenote.section:` or `onenote.section_groups:` keys. Section and
section-group names come from folder path (or dot-stem splits).

`examples/pure-markdown.md` has no front-matter at all.

### Content ports — what changes during the move

File content is carried over verbatim except:

- Front-matter block prepended to every targeted file (5 existing + 6 new).
- `examples/with-image.md` updates the image path from `![alt](assets/image.png)`
  or wherever it currently points to `![alt](../assets/image.png)` to match
  the new nesting.

## Placeholder substitution

**Syntax:** literal string `{{Notebook}}`.

**Where it appears:** only inside `onenote.notebook:` values. Never in titles,
body content, or image paths.

**How it's substituted:** plain string replace at script copy-time.
No templating engine — one-line `Get-Content | .Replace | Set-Content`.

**Validation in the script:** mandatory `-Notebook` parameter; explicit throw
if `-Notebook` is an empty string. The substitution only runs after both
guards pass, so no copy-in-place writes an empty notebook name.

## Script design: `scripts/smoke-samples.ps1`

### Parameters

```powershell
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Notebook,

    [switch]$DryRun,
    [switch]$SkipBuild,
    [switch]$KeepScratch
)
```

### Flow

1. **Preflight.**
   - `$ErrorActionPreference = 'Stop'`.
   - Resolve repo root from `$PSScriptRoot`.
   - Guard: `if ([string]::IsNullOrWhiteSpace($Notebook)) { throw "..." }`.
   - Unless `-SkipBuild`: `dotnet build OneNoteMarkdownExporter -c Debug --nologo`.
   - Check exe exists at `OneNoteMarkdownExporter/bin/Debug/net10.0-windows/OneNoteMarkdownExporter.exe`.
   - Print banner (Notebook / ScratchRoot / Mode).
   - `Read-Host "OneNote desktop running? Ctrl+C to abort, Enter to continue"`.

2. **Copy + substitute.**
   - `$scratch = Join-Path $env:TEMP "samples-smoke-$(Get-Date -Format 'yyyyMMdd-HHmmss')"`.
   - `Copy-Item -Recurse -Path (Join-Path $repoRoot 'samples') -Destination $scratch`.
   - Walk `Get-ChildItem $scratch -Recurse -Filter '*.md'`; for each:
     - Read raw content.
     - Replace `{{Notebook}}` → `$Notebook`.
     - If changed, write back (`-NoNewline`).
   - Print substituted-file count.

3. **Run the publish.**
   - Reuse an `Invoke-Exe` helper modeled on `smoke-pr21.ps1:71-100`
     (Start-Process -Wait + redirected stdout/stderr — WPF WinExe workaround).
   - Args:
     - Live: `@('--publish', $scratch, '--verbose', '--create-missing')`.
     - Dry-run: `@('--publish', $scratch, '--dry-run', '--verbose', '--create-missing')`.
   - Stream stdout; tint stderr red.
   - Capture exit code.

4. **Report + cleanup.**
   - Live, exit 0 → green: "Open OneNote and inspect `$Notebook`; sections should match the samples tree."
   - Live, non-zero → red; keep scratch regardless of `-KeepScratch` for debugging.
   - Dry-run → blue: "Dry-run complete; inspect the `Would create …` lines above."
   - Remove scratch unless `-KeepScratch`, `-DryRun`, or a live-run failure.
   - Print final scratch status (kept at `X` / removed).

### Error handling

| Failure | Script behavior |
|---|---|
| `-Notebook` omitted | PowerShell parameter binder (Mandatory) errors. |
| `-Notebook` empty/whitespace | Explicit `throw`. |
| `dotnet build` non-zero | `throw`. |
| Exe not found | `throw` with "run without -SkipBuild" hint. |
| Exe non-zero exit | Log loudly, keep scratch, script exits non-zero. |
| OneNote desktop not running | Surfaced by the exe itself (its error lands in stdout/stderr). |

### What the script does NOT do

- Does not clean up created OneNote sections (user responsibility).
- Does not compare before/after OneNote state (visual only).
- Does not invoke `--import` (separate scope).
- Does not prompt between scenarios (single publish run, not a multi-scenario walkthrough).
- Does not attempt to run on Linux/Mac.

## Re-run semantics

Running the script twice with the same `-Notebook` will find existing
sections/section-groups on the second run and publish new page duplicates
alongside (idempotent re-publish is issue #6). For regression sweeps,
either use a fresh notebook each run or clear the target sections in
OneNote between runs.

## Docs changes

### `docs/importer.md`

Pre-flight check confirms the file currently has no `samples` references,
so this is a pure addition. Add a new subsection right after "Auto-create
missing sections" (added in PR #21):

```markdown
### Samples corpus

`samples/` in the repo is a minimal docs-repo layout demonstrating the
conventions above: folder-inferred section groups, a dot-path file
(`reference.api.endpoints.md`), an image via relative path, and a
plain-markdown file with no `onenote:` key to verify silent skipping.

To publish the corpus into a throwaway OneNote notebook for visual
verification:

​```powershell
./scripts/smoke-samples.ps1 -Notebook "SamplesDemo"
​```

Add `-DryRun` to preview without writing. See the script's comment-help
block for the full parameter list.
```

### `README.md`

Pre-flight check confirms no existing `samples` references, so this is a
pure addition. Pick a sensible location (probably near any existing
"getting started" / "try it" section) and add:

```markdown
See `samples/` for a working docs-repo layout and
`scripts/smoke-samples.ps1` to publish it into a throwaway notebook.
```

If no natural home exists in the current README, skip this change —
`docs/importer.md` carries the load.

### `CHANGELOG.md`

Under `[Unreleased]`:

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

## Rollout

Single PR: `chore(samples): realistic docs-repo layout + smoke script`.

Branch name: `chore/samples-corpus-smoke`.

Files touched:

- `samples/` — restructure: move the 5 existing files into their new
  locations, add 6 new files, add `{{Notebook}}` front-matter to each
  targeted file, update the image path in `examples/with-image.md`.
- `scripts/smoke-samples.ps1` — new.
- `docs/importer.md` — add Samples corpus subsection; update any stale
  paths.
- `README.md` — one-line pointer; update any stale paths.
- `CHANGELOG.md` — two entries under `[Unreleased]`.

### Verification

- Manual: run `./scripts/smoke-samples.ps1 -Notebook "SamplesDemo" -DryRun`
  against a fresh throwaway notebook. Confirm:
  - Every targeted file has a `[dry-run]` line.
  - `examples/pure-markdown.md` does NOT appear (silently skipped).
  - `reference.api.endpoints.md` resolves to
    `SamplesDemo / reference / api / endpoints`.
  - `Would create …` lines preview every missing intermediate section group
    and section.
- Manual: same command without `-DryRun`. Open OneNote, inspect the
  notebook, confirm visual fidelity matches the source markdown.
- No automated tests — this corpus is the test.

### Commit discipline

- No `Co-Authored-By: Claude …` trailer (project convention).
- Keep the PR a single Conventional Commit on `master` after squash-merge.
- Delete the branch after merge.

## References

- `docs/superpowers/specs/2026-04-16-folder-tree-mapping-design.md` —
  establishes the folder/dot mapping rules this corpus exercises.
- `docs/importer.md` — the Tree publish section this new subsection
  extends.
- `scripts/smoke-pr21.ps1` — the sibling script whose `Invoke-Exe`
  pattern and general shape we reuse.
