# Design: Folder tree → OneNote mapping

Status: draft for review
Issue: [#2](https://github.com/LarryWisherMan/one-note-to-markdown/issues/2)
Milestone: M2 — Authored Markdown as first-class source

## Overview

Define a filesystem convention for authored Markdown repos and a deterministic rule for mapping that convention onto OneNote's `Notebook → [SectionGroup…] → Section → Page` hierarchy when publishing. The repo is the target-neutral source of truth; OneNote is one of several eventual publish targets. This spec covers only the mapping; it does not change the existing XML converter, introduce idempotent re-publish, or define the full front-matter schema.

## Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Primary hierarchy | Folders | Established Markdown-KB convention; portable across GitHub, Obsidian, VSCode, Hugo, static sites. |
| Secondary hierarchy | Dots in filename stems | Dendron-compat for authors who prefer flat layouts. Composes with folder paths. |
| Publish opt-in signal | Presence of `onenote:` front-matter key | Keeps the repo target-neutral: a file explicitly declares its OneNote intent. |
| Bulk-publish escape hatch | `--notebook <name>` CLI flag | Pragmatic path for "publish this whole subtree" without editing every file. |
| Titles | Separate from slug | Slug is filesystem identity; title is display. Title from FM > first `# H1` > slug. |
| Link syntax | Relative markdown paths | `[text](../other.md)` works unchanged in every renderer; publisher rewrites at emit time. |
| Name casing | Verbatim — no auto-translation | Folder `Work Notes/` becomes notebook `Work Notes`; folder `work-notes/` becomes notebook `work-notes`. |
| Field precedence | Independent, FM > CLI > inference | Each of notebook/section/section_groups/title resolves independently. |

## In scope

- Filesystem convention (folders, filenames, slugs, titles).
- Inter-note link syntax (relative paths).
- Minimum front-matter needed for OneNote routing.
- Deterministic resolution rule: `(file, FM, CLI flags) → (notebook, section_groups, section, page)`.
- Errors, warnings, and info classification for publish-time diagnostics.
- `--dry-run` behavior for previewing a publish.

## Out of scope

- Full front-matter schema (tags, aliases, related, IDs) → issue [#3](https://github.com/LarryWisherMan/one-note-to-markdown/issues/3).
- Idempotent re-publish and stable page IDs → issue [#6](https://github.com/LarryWisherMan/one-note-to-markdown/issues/6).
- Link resolution mechanics (how relative paths become OneNote page links at emit) → issue [#7](https://github.com/LarryWisherMan/one-note-to-markdown/issues/7).
- One file → multiple notebook fan-out → issue [#4](https://github.com/LarryWisherMan/one-note-to-markdown/issues/4).
- Strict authoring lint config → issue [#8](https://github.com/LarryWisherMan/one-note-to-markdown/issues/8).
- Wiki-links (`[[...]]`) — a supported future addition, not defined here.
- Auto-create missing notebooks/sections in OneNote — current behavior (error) is preserved.
- OneNote → Markdown bidirectional sync — out of project scope.
- Per-folder display-name overrides (e.g. a `_index.md` with a pretty name for a kebab-case folder).

## Core conventions

### A note is a `.md` file

Its location in the repo and its filename together express its intrinsic hierarchy. A file has four distinct identities:

| Concept | Source | Example |
|---|---|---|
| **Slug** (filename minus `.md`) | On disk | `tiramisu` |
| **Repo path** | Folder + slug | `personal/recipes/tiramisu.md` |
| **Title** (human-readable) | FM `title:` → first `# H1` → slug | `"Grandma's Tiramisu Recipe"` |
| **Target address** | Computed at publish time | OneNote: `Personal / Recipes / Grandma's Tiramisu Recipe` |

### Naming

- **Filenames**: kebab-case recommended (`my-note.md`). Not enforced.
- **Folder names**: use whatever you want to see in the published output. `Work Notes/` renders as notebook `Work Notes`. `work-notes/` renders as `work-notes`.
- **Dots in filename stems are hierarchy delimiters.** `backend.api.auth.md` is equivalent to `backend/api/auth.md` for mapping purposes. Dots inside folder *names* are literal (they don't split).
- **Filenames with literal dots** (`v1.0.md`, `schema-v2.1.md`) will be split into unwanted segments. Use dashes instead: `v1-0.md`, `schema-v2-1.md`. The publisher emits a warning if a resulting segment is numeric-only, since that's a common sign of an accidental split.

### Titles

- **`title:`** in FM if set.
- else the first `# H1` in the body.
- else the slug.

### Assets

Non-`.md` files (images, attachments) live alongside notes and are referenced via relative paths (`![diagram](./assets/arch.png)`). Asset handling is unchanged from current behavior — see `docs/importer.md`.

### Files without a publish target

Silently skipped by the OneNote publisher. A repo may contain notes that only publish to a static site, or notes that never publish anywhere.

## Inter-note links

Use standard relative markdown paths:

```markdown
<!-- in personal/meals.md -->
For dessert try the [tiramisu](./recipes/tiramisu.md) recipe.
```

Properties:

- GitHub, Obsidian, VSCode preview all render this as a working link unchanged.
- When publishing to OneNote, the publisher walks the source tree, builds a map `{ repo_path → onenote_page_id }`, and rewrites the link text's `href` to the OneNote page link at emit time. *(Mechanics: issue #7.)*
- Link display text is whatever the author wrote — no auto-title-lookup magic in this design.

Wiki-links (`[[slug]]`, `[[slug|display]]`) are a natural future addition for Obsidian/Dendron ergonomics but are out of scope here.

## Front-matter for OneNote routing

Minimum schema. Full FM schema is issue #3.

```yaml
---
title: "My Page Title"               # optional, target-neutral
onenote:                             # presence of this key = "publish to OneNote"
  notebook: "Work Notes"             # all fields inside are optional
  section: "Architecture"
  section_groups: ["Backend", "API"] # optional; nested SGs, outer → inner
---
```

### Field meanings

| Field | Role |
|---|---|
| `title:` | Page title used by *all* publish targets. Falls back to first `# H1`, then slug. |
| `onenote:` | Presence of this top-level key = "this file publishes to OneNote". Absence = skipped by the OneNote publisher. |
| `onenote.notebook:` | Target notebook name. If absent, taken from `--notebook` CLI flag; if still absent, inferred from the first path segment. |
| `onenote.section:` | Target section name. If absent, inferred from the next-to-last path segment. |
| `onenote.section_groups:` | List of nested section groups, outer → inner. If absent, inferred from middle path segments. |

### Shorthand forms

- **`onenote: true`** is equivalent to `onenote: {}` — opt in, infer everything from folder path and/or CLI flag.
- **`onenote: false`** — explicit opt-out. Under `--notebook` bulk mode, this file is skipped. Useful for excluding a specific file from a tree publish without restructuring.

### CLI modes

| Invocation | Behavior |
|---|---|
| `publish ./notes/` (no `--notebook`) | Only files with an `onenote:` key publish. `onenote.notebook` must be resolvable (FM or inference). |
| `publish ./notes/ --notebook "Work Notes"` | Every `.md` under `./notes/` publishes to that notebook, whether it has an `onenote:` key or not. Files with their own `onenote.notebook` still win per-file. |

## Publishing: the resolution rule

Each publishable file resolves into `(notebook, [section_groups…], section, page)` via the following deterministic algorithm.

### Inputs

- `source_root`: path passed to `publish`.
- `file_rel`: file's path relative to `source_root`.
- `fm`: the file's front-matter (may include an `onenote:` block).
- `cli_notebook`: value of `--notebook` (may be unset).

### Segmentation

Split `file_rel` on `/` to get folder segments and the filename. Then split the filename stem on `.` to get dot-segments. Track which segments came from folders.

| `file_rel` | Folder segments | Filename dot-segments | Combined |
|---|---|---|---|
| `work-notes/architecture/overview.md` | `["work-notes", "architecture"]` | `["overview"]` | `["work-notes", "architecture", "overview"]` |
| `work-notes/backend.api.auth.md` | `["work-notes"]` | `["backend", "api", "auth"]` | `["work-notes", "backend", "api", "auth"]` |
| `backend/api.auth.md` | `["backend"]` | `["api", "auth"]` | `["backend", "api", "auth"]` |
| `overview.md` | `[]` | `["overview"]` | `["overview"]` |
| `backend.api.auth.md` | `[]` | `["backend", "api", "auth"]` | `["backend", "api", "auth"]` |

The distinction matters for the notebook slot rule below.

### Resolution (fields are independent)

**Opt-out short-circuit**

If `fm.onenote` is the literal `false`, the file is skipped before any further resolution, regardless of `--notebook`.

**Notebook**

The first **folder** segment (if any) is the "notebook slot." Filename dot-segments are never the notebook slot — they are always SG/section hierarchy.

1. `fm.onenote.notebook` if set → use it. If a folder notebook-slot exists, **consume it** (emit a warning if the folder name differs from the FM value).
2. else `cli_notebook` if set → use it. **Do not consume** any segment (source root is the notebook root).
3. else, **only if `onenote:` key is present and not `false`**, consume the first segment (folder or dot) as the notebook.
4. else → file is not a OneNote target; publisher skips it.

Note on step 3: for bare filenames (no folders), the first dot-segment is consumed for inference only — this preserves Dendron-style flat layouts where `work.arch.overview.md` + `onenote: true` infers notebook `work`.

**Page**

- **Slug** = the last segment (always).
- **Title** = `fm.title` → first `# H1` in body → slug.

**Section**

1. `fm.onenote.section` if set →
2. else the segment just before the page slug (after notebook consumed, if applicable).
3. else → error: cannot infer section; add `onenote.section` or deepen the folder.

**Section groups** (outer → inner)

1. `fm.onenote.section_groups` if set →
2. else all segments between the notebook slot and the section slot. May be empty.

### Precedence summary

```
notebook:        fm.onenote.notebook  >  --notebook  >  first path segment (if onenote: present)
section:         fm.onenote.section   >  next-to-last path segment
section_groups:  fm.onenote.section_groups  >  middle path segments
title:           fm.title             >  first # H1  >  slug
```

### Examples

| Source layout | FM | CLI | Result |
|---|---|---|---|
| `Work Notes/Architecture/overview.md` | `onenote: true` | — | NB `Work Notes` → Section `Architecture` → Page `overview` |
| `Work Notes/Backend/API/Architecture/overview.md` | `onenote: true` | — | NB `Work Notes` → SG `Backend`→`API` → Section `Architecture` → Page `overview` |
| `random/foo.md` | `onenote: { notebook: "X", section: "Y" }` | — | NB `X` → Section `Y` → Page `foo`. Folder slot `random` consumed (warning: mismatch). |
| `architecture/overview.md` | (no `onenote:`) | `--notebook "Work Notes"` | NB `Work Notes` → Section `architecture` → Page `overview` |
| `backend.api.auth.md` | `onenote: { notebook: "Work Notes" }` | — | NB `Work Notes` → SG `backend` → Section `api` → Page `auth` |
| `overview.md` | `onenote: true` | — | **Error**: single segment — can't infer section. |
| `drafts/tmp.md` | (no `onenote:`) | (no flag) | **Skipped** silently. |
| `Work Notes/arch/overview.md` | `onenote: { notebook: "Personal" }` | — | NB `Personal` (FM wins) → Section `arch` (folder) → Page `overview`. Warning: notebook mismatch. |

## Validation

### Errors (file does not publish; reported in run summary)

| Case | Message |
|---|---|
| `onenote:` present but file has only one segment and no explicit `section`/`notebook` in FM | `<file>: cannot infer OneNote path — add onenote.notebook and onenote.section to front-matter, or move the file into a folder.` |
| `fm.onenote.section` set but notebook unresolvable | `<file>: section specified but no notebook — add onenote.notebook or pass --notebook.` |
| Target notebook/section doesn't exist in OneNote | Unchanged from current behavior: `Section not found: <notebook>/<section>`. Auto-create is future work. |
| Malformed YAML front-matter | `<file>: invalid front-matter — <parser error>.` |
| Two files resolve to the same `(notebook, [SGs…], section, page)` tuple | `Collision: <fileA> and <fileB> both resolve to <notebook>/<section>/<page>.` Both files are skipped. |
| Empty path segment (e.g. `foo..bar.md` or leading/trailing dots) | `<file>: empty path segment.` |

### Warnings (file publishes; user notified)

| Case | Message |
|---|---|
| FM-set notebook differs from folder-inferred first segment | `<file>: FM notebook "<X>" overrides folder-inferred "<Y>".` |
| No title anywhere (no FM, no first H1) | `<file>: no title found; using slug "<slug>" as page name.` |
| Any resolved segment is numeric-only (likely an accidental dot-split of a versioned filename like `v1.0.md`) | `<file>: resolved segment "<seg>" is numeric-only; this may be an unintended split. Consider renaming with dashes.` |

### Info (shown with `--verbose`)

| Case | Message |
|---|---|
| File skipped because no `onenote:` FM and no `--notebook` flag | `<file>: skipped (no OneNote target).` |

### Normalization rules

- Whitespace in names is preserved.
- Case is preserved. `Work Notes` and `work notes` are treated as distinct.
- Leading/trailing whitespace in names is trimmed.
- Hidden files/dirs (`.git`, `.obsidian`, dotfiles) are skipped.
- Symlinks are not followed.
- Non-`.md` files are ignored by the publisher walker.

### Determinism

- Files publish in sorted order by `file_rel`. Stable across runs.
- No parallel publishing (single-threaded, matches current COM-interop code path).

### `--dry-run`

Existing flag applies. Output includes the resolved `(notebook, SGs, section, page)` tuple for every publishable file, plus all errors and warnings. Does not call OneNote.

## Ergonomic examples

### A simple multi-notebook repo

```
notes/
├── Work Notes/
│   ├── Architecture/
│   │   ├── overview.md              # title: "Architecture Overview"
│   │   └── scalability.md
│   └── Meetings/
│       └── 2026-Q2/planning.md
├── Personal/
│   └── Recipes/tiramisu.md
└── drafts/
    └── half-finished-idea.md        # no onenote: → skipped
```

Each note declares `onenote: true` (or leaves FM empty and relies on `--notebook`). Running `publish ./notes/` walks the tree and publishes `Work Notes`, `Personal`, but not `drafts`.

### A Dendron-style flat repo

```
notes/
├── work.architecture.overview.md
├── work.architecture.scalability.md
├── work.meetings.q2-planning.md
├── personal.recipes.tiramisu.md
└── personal.recipes.carbonara.md
```

Each file has `onenote: true`. Running `publish ./notes/` treats dots as hierarchy delimiters: notebook `work` or `personal`, section `architecture`/`meetings`/`recipes`, pages at the end.

### A team-notebook drop-in

```
team-kb/
├── overview.md
├── onboarding.md
└── guides/
    ├── deploy.md
    └── triage.md
```

No FM anywhere. Run `publish ./team-kb/ --notebook "Engineering Wiki"`. Every file publishes under that notebook; folder structure fills in sections.

## Implementation notes

This design expects the following implementation pieces (details belong in the plan):

- A **walker** component that scans `source_root` for `.md` files, applies filters (hidden files, symlinks, non-`.md`), and produces a stable-ordered list.
- A **resolver** component that, given `(file_rel, fm, cli_notebook)`, emits `(notebook, section_groups, section, page)` or an error per the algorithm above.
- A **front-matter parser** — the minimum required YAML subset (a single `onenote:` block plus `title:`). Full schema lands with issue #3.
- A **publish report** that aggregates per-file outcomes (published / skipped / warned / errored) and prints a summary at the end of the run.

The existing `ImportService`/`ImportOptions` and CliHandler changes needed to wire these together are planning detail, not design.
