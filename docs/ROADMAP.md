# Roadmap

The vision, not the backlog. This file answers "does this idea belong
in the project?" so triage is fast. The granular work lives in
[GitHub Issues](https://github.com/LarryWisherMan/one-note-to-markdown/issues)
— triage every new issue into `P0` / `P1` / `P2` / close within a minute
of creation.

> Status: **draft**. The maintainer is filling this in. Anything
> unanswered here is fair game for a `docs(roadmap):` PR.

## Vision

<!-- One paragraph. What is this project FOR beyond the upstream fork?
     Who benefits from its existence? Why not just contribute to
     upstream? If you can't articulate "here's why this fork earns its
     keep," the fork probably shouldn't exist. -->

_To be filled in._

## Primary users

<!-- Who uses this? Check any that apply and expand.
     - [ ] Me, personally, exporting my own notebooks to Markdown for
           RAG / AI tooling.
     - [ ] Me, personally, authoring in Markdown and pushing back to
           OneNote.
     - [ ] Other devs in DLP-constrained orgs who hit the same wall.
     - [ ] Automation / scheduled sync jobs.
     - [ ] Obsidian / external-tool users pulling from OneNote.
-->

_To be filled in._

## "Done enough to upstream" — v1.0.0 definition

<!-- What does "this fork is worth PR-ing to segunak/one-note-to-markdown"
     look like? Concrete list. Each item is independently verifiable.
     Examples:
     - [ ] Markdown importer handles the 10 sample files cleanly.
     - [ ] Golden-file test covers the reference page.
     - [ ] README + docs/importer.md current.
     - [ ] No `Interop.*` COM warnings at build.
     - [ ] GitVersion-stamped release zip signed. -->

_To be filled in._

## Explicitly out of scope

<!-- Hard "no" list. When an idea shows up in triage, match it against
     this list before anything else — close with a one-liner citing
     this section. Examples to consider:
     - Graph API support (reintroduces the admin-consent problem).
     - Non-Windows platforms (COM Interop is Windows-only by design).
     - Rich-content OneNote features that have no Markdown equivalent
       (ink annotations, embedded OneNote tags/checkboxes roundtrip,
       password-protected sections, audio/video). -->

_To be filled in._

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

### M2 — <fill in the next milestone>

_To be filled in._

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
