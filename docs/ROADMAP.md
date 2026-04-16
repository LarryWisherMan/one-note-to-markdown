# Roadmap

The vision, not the backlog. This file answers "does this idea belong
in the project?" so triage is fast. The granular work lives in
[GitHub Issues](https://github.com/LarryWisherMan/one-note-to-markdown/issues)
— triage every new issue into `P0` / `P1` / `P2` / close within a minute
of creation.

> Status: **draft**. The maintainer is filling this in. Anything
> unanswered here is fair game for a `docs(roadmap):` PR.

## Vision

**Markdown is the source of truth. OneNote is a presenter.** Notes are
authored and version-controlled in Markdown so the knowledge base stays
machine-readable and portable (AI / RAG / static sites / PDF / Word),
but the same content publishes cleanly into OneNote because the primary
audience — teammates who already live in OneNote — shouldn't have to
change tools.

The forked upstream (`segunak/one-note-to-markdown`) is a starting
point, not a constraint. If clean architecture needs a rewrite to
serve the vision, rewrite it.

Spiritual neighbors: Dendron, Obsidian, Docusaurus, and Microsoft's
own documentation publishing pipeline — minimal but purposeful
front-matter, strict authoring conventions, and one Markdown source
fanning out to many rendered formats.

## Primary users

- ✅ The maintainer — authoring notes in Markdown and publishing into
  team OneNote notebooks so collaborators keep using their preferred
  viewer.
- ✅ The maintainer — exporting legacy OneNote content to Markdown to
  bring it under version control and into AI tooling.
- 🟡 Devs in DLP-constrained orgs who hit the same `Publish()`
  wall — welcome, but not the design target.
- ⬜ Obsidian / external-tool users pulling from OneNote — out of
  current focus.
- ⬜ Team members authoring directly in OneNote for bidirectional
  sync — explicitly deferred (see "out of scope" and the parked sync
  idea).

## "Done enough to upstream" — v1.0.0 definition

A concrete bar for when this fork is worth PR-ing back to upstream
(or releasing as a standalone variant). Each item is independently
verifiable.

- [x] Markdown → OneNote importer emits reference-style XML matching
      a hand-authored target.
- [x] Golden-file test against the reference page.
- [x] `docs/importer.md` current and accurate.
- [x] Repo foundations: GitVersion, `.editorconfig`, `CONTRIBUTING.md`,
      `CHANGELOG.md`, Keep a Changelog discipline.
- [ ] Front-matter schema lets a `.md` declare its OneNote target
      (notebook/section/page name, tags, cross-refs).
- [ ] Folder-tree convention documented: directory structure maps to
      notebook/section/page hierarchy in a predictable way.
- [ ] CLI publishes a whole Markdown tree (not just a single file).
- [ ] Imports are idempotent — re-running updates the existing page
      instead of creating a duplicate.
- [ ] Inter-note links (`[x](./other.md)`) resolve to working OneNote
      links on the published page.
- [ ] A single `.md` can publish to multiple notebook targets.
- [ ] Markdown authoring style guide + strict lint config for
      source files.
- [ ] Clean-architecture separation: MD → content model → target
      adapter. Same core reusable for future HTML / PDF / Word
      targets.

## Explicitly out of scope

<!-- Hard "no" list. Match new ideas against this in triage and close
     with a one-liner citing this section. -->

- _To be confirmed during triage. Candidates the maintainer has
  mentioned wanting **parked but not rejected** (these become `P2`
  issues, not `wontfix`):_
  - _Microsoft Graph / HTML variant._
  - _Office add-in packaging._
  - _GUI polish beyond what already exists._
  - _Bidirectional sync (OneNote edits → Markdown)._
  - _Static-site / PDF / Word export._

## Active milestones

<!-- Ordered. The first unchecked item is what "next" means. Keep this
     short — 2-3 milestones max. Anything further out belongs in P2
     issues, not here. -->

### M1 — Importer reference-style parity ✅

- ✅ Emit reference-style XML (QuickStyleDefs, inline heading styling,
  span-based emphasis, per-line code-block OEs, blank-line spacers).
- ✅ Golden-file test against the hand-authored reference page.
- ✅ Documentation (`docs/importer.md`).
- ✅ Repo foundations (GitVersion, `.editorconfig`, `CONTRIBUTING.md`,
  `CHANGELOG.md`).

### M2 — Authored Markdown as first-class source

Make the importer good enough to be driven from a real Markdown
knowledge base, not just hand-curated single files.

- [ ] Front-matter schema (title, tags, notebook, section, id).
- [ ] Folder-tree mapping convention documented.
- [ ] Multi-file tree publish via CLI (walk, parse, publish).
- [ ] Strict lint config for source `.md`.
- [ ] Author-facing style guide.

### M3 — Idempotent sync + link graph

Stop treating each import as a greenfield page creation.

- [ ] Stable MD-file ↔ OneNote-page-id mapping.
- [ ] Re-imports update rather than duplicate.
- [ ] Inter-note `[x](./y.md)` links resolve to OneNote page links.
- [ ] Single MD publishable to multiple notebook targets.

### M4 — Clean-architecture extraction (enables everything else)

Prepare the core for future HTML / Graph / static-site / PDF targets
without further rewrites.

- [ ] MD → target-agnostic content model.
- [ ] COM-OneNote target as one adapter, isolable and testable
      without touching OneNote.

## Open questions

<!-- Decisions you haven't made yet but will need to. Park them here
     so they don't silently block work. -->

- _To be filled in._

## Working agreements

- **WIP = 1.** One `P0` or `P1` active at a time. Finish or re-queue
  before picking up another.
- **30-second triage.** New issues get labeled within a minute or
  closed with a reason.
- **Monthly `P2` review.** Prune anything >6 months old. The backlog
  isn't a graveyard.
- **Out-of-scope = close, don't move.** Cite this file.
