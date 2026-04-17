# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and version numbers
adhere to [Semantic Versioning](https://semver.org/spec/v2.0.0.html) as
computed by [GitVersion](https://gitversion.net/) from conventional-commit
messages.

Every PR updates the `[Unreleased]` section before merge. When a version is
cut, `[Unreleased]` is promoted to the new version heading with an ISO date
(`[1.2.0] - 2026-04-16`) and a fresh `[Unreleased]` section is opened above
it.

## [Unreleased]

### Added

- `docs/ROADMAP.md` scaffold capturing vision, primary users, the "done
  enough to upstream" definition, out-of-scope list, and working
  agreements (WIP limit, triage cadence, `P2` pruning). Paired with
  `P0` / `P1` / `P2` / `idea` labels in the issue tracker for backlog
  triage.
- Markdown → OneNote importer now emits reference-style OneNote XML that
  mirrors the shape OneNote produces when content is authored natively:
  only two QuickStyleDefs on the page (`PageTitle`, `p`), headings styled
  inline on `<one:T>` with Segoe UI / `#201F1E` / `<span style='font-weight:bold'>`,
  `<span>`-based inline emphasis (replacing `<b>` / `<i>` / `<del>`),
  fenced code blocks rendered as per-line OEs inside a single-column
  `<Table hasHeaderRow="true">`, and blank-line spacer OEs between
  content blocks so paragraphs, lists, tables, and code blocks render
  with visual breathing room.
- `docs/importer.md`: full Markdown → OneNote mapping table, CLI
  quick-start, blank-line spacing rationale, and a "Known limitations"
  section.
- `docs/reference-page/` fixtures: the OneNote screenshot
  (`OneNote_VisualRef1.png`), the hand-authored reference XML
  (`Reference-page.xml`), the canonical reference markdown
  (`MarkDow_VisualRef1.md`), and before/after spacing-fix screenshots
  (`Results_1.png` / `Results_2.png`).
- Golden-file test
  `Convert_ReferenceMarkdown_MatchesReferenceShape` asserting seven
  structural invariants against `docs/reference-page/Reference-page.xml`.
- Three spacing tests covering paragraph-paragraph, table/code-block,
  and heading-sibling spacer behaviour.
- `.editorconfig` with baseline rules for C# (4-space indent,
  file-scoped namespaces, `_camelCase` private fields, `using`
  directives outside namespace, System first), YAML / JSON / XML
  (2-space), and Markdown.
- `GitVersion.yml` configured for the `GitHubFlow/v1` workflow in
  `ContinuousDelivery` mode with conventional-commit-driven bumps
  (`feat:` → minor, `fix:` → patch, `BREAKING CHANGE` or `type!:` →
  major, other types no-bump).
- `GitVersion.MsBuild` package reference on
  `OneNoteMarkdownExporter.csproj` so the executable is stamped with
  the computed semver at build time.
- `CONTRIBUTING.md` documenting the trunk-based PR + squash-merge
  workflow, conventional-commit rules, GitVersion usage, code style,
  test expectations, and CHANGELOG discipline.
- `CHANGELOG.md` (this file).
- `--publish <source>` CLI command that walks a Markdown source tree and
  creates one OneNote page per publishable file. Opt-in via an
  `onenote:` front-matter block; bulk-publish via `--notebook <name>`.
  Deterministic folder + filename-dot → `Notebook / SectionGroups… / Section /
  Page` resolution rule; collision detection; numeric-segment warnings;
  `--dry-run` supported. See
  `docs/superpowers/specs/2026-04-16-folder-tree-mapping-design.md`.
- `FindSectionIdByPath(notebook, sectionGroups[], section)` on
  `OneNoteService` for nested-section-group navigation.
- YamlDotNet dependency for parsing the minimum front-matter subset.
- `--publish` auto-creates missing section groups and sections by
  default; opt out with `--no-create-missing`. Notebook-level
  auto-create is not yet supported (tracked by #19).
- `--import --create-missing` creates a missing target section (and
  any section groups between it and the notebook) before importing
  (opt-in).
- `scripts/smoke-samples.ps1` — builds + publishes the samples corpus
  into a throwaway OneNote notebook for visual verification. Supports
  `-DryRun`, `-SkipBuild`, `-KeepScratch`.

### Changed

- The first `# H1` in a Markdown document is now consumed as the
  OneNote page `<Title>` and removed from the body so it isn't
  duplicated.
- Inline code size increased from 9pt to 10.0pt to match the reference
  page.
- `samples/with-image.md` points at `samples/assets/image.png` (a real
  PNG) rather than a placeholder, so the smoke-test page renders an
  actual embedded image.
- Reference fixtures moved from `Z_SampleRef/` to `docs/reference-page/`.
- Imported and published pages now suppress OneNote spell-check via
  `lang="yo"` on `<one:Page>` and `<one:Title>`, so technical content
  renders without red squiggles.
- Reorganized `samples/` from a flat set of feature-demo files into a
  realistic docs-repo layout (`getting-started/`, `reference/`,
  `examples/`), with `{{Notebook}}`-templated front-matter so the
  corpus is target-agnostic.

### Removed

- The `h1`–`h6`, `cite`, `quote`, and `code` QuickStyleDefs from the
  emitted page XML. Heading differentiation is now inline-styled
  rather than QSD-referenced.
- `samples/MarkDow_VisualRef1.md`: an accidental duplicate of the
  canonical reference markdown, which now lives only at
  `docs/reference-page/MarkDow_VisualRef1.md`.

### Internal

- `samples/output/*.xml` and `*.converted.xml` are regenerated on every
  test run by `DumpSampleXml_ForManualInspection` and are now
  gitignored.
