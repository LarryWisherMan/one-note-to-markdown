# Contributing

This is primarily a solo-dev project, but these conventions exist so the
history stays navigable and a future contributor (or fork) can follow along.

## Development workflow

**Trunk-based with PR-based squash merges.** `master` is always green
and carries one commit per PR — no long-running `develop` branch, no
release branches, no direct pushes for anything bigger than a
readme-typo fix.

```bash
git checkout -b feat/importer-image-sizing
# ...work, commit freely on the branch...
git push -u origin feat/importer-image-sizing
gh pr create --fill
# Review, update CHANGELOG [Unreleased], then "Squash and merge" in the UI
```

On GitHub, use the **Squash and merge** option — never **Create a merge
commit** or **Rebase and merge**. The PR's squashed commit becomes the
single entry on `master`, so:

- **The PR title becomes the commit message on `master`.** It must
  follow Conventional Commits (see below). GitHub offers to edit the
  squashed message at merge time; keep it aligned with the PR title.
- **Intermediate commits on the branch don't need to follow any
  convention** — commit as messily as you like while working; only
  the PR title matters once squashed.
- **Delete the source branch after merge** (GitHub has a repo setting
  for this). Branches are ephemeral — the history lives on `master`.

Rebase onto `master` if your branch gets more than a day or two behind.

## Commit messages — Conventional Commits

**The PR title** (which becomes the squashed commit on `master`) uses
[Conventional Commits](https://www.conventionalcommits.org/). GitVersion
parses it to compute the next semver.

```
<type>(<scope>): <summary>

<body>

<footer>
```

Common types used in this repo:

| Type | Effect on version | Use for |
|---|---|---|
| `feat` | minor bump | New user-visible behavior or CLI flag. |
| `fix` | patch bump | Bug fix. |
| `docs` | no bump | Changes to README, `docs/`, code comments only. |
| `test` | no bump | Adding/changing tests without production changes. |
| `refactor` | no bump | Internal rearrangement, no behavior change. |
| `chore` | no bump | Tooling, gitignore, fixture moves, dep version bumps. |
| `build` | no bump | `.csproj`, MSBuild, packaging. |
| `perf` | patch bump | Performance fix with observable behavior change. |
| `style` | no bump | Formatting-only changes. |

Breaking changes: add `!` after the type (`feat(importer)!: drop --no-collapse`)
or a `BREAKING CHANGE:` footer. Both trigger a **major** bump.

**One PR = one logical change.** Because every PR squashes to a single
commit on `master`, reverting a change means reverting its PR's
squashed commit. Resist the urge to pile unrelated fixes into one
branch — if you find yourself wanting a compound subject like
`feat(x): add y and fix z`, that's two PRs.

## Versioning — GitVersion

Version numbers are computed from git history by
[GitVersion](https://gitversion.net/) using the rules in `GitVersion.yml`.
The executable is stamped at build time via the `GitVersion.MsBuild`
package referenced from `OneNoteMarkdownExporter.csproj` — no manual
`AssemblyVersion` to maintain.

To inspect the computed version locally:

```bash
dotnet tool install --global GitVersion.Tool   # one-time
dotnet-gitversion                              # print the computed version
dotnet-gitversion /showvariable SemVer         # just the semver string
```

**Known issue:** `.github/workflows/release.yml` currently uses its own
naive patch-increment tagger (`git describe --tags` + `++patch`). That
predates the GitVersion setup and will disagree once CI is re-enabled.
Reconcile by replacing the "Get latest tag and increment version" step
with a `gittools/actions/gitversion/execute@v3` call, and feed the
changelog entry for the cut version into `generate_release_notes:`
(replacing the auto-generated one) before pushing the release workflow
back into use.

## Changelog — Keep a Changelog

`CHANGELOG.md` at the repo root follows the
[Keep a Changelog 1.1](https://keepachangelog.com/en/1.1.0/) format.

**Every PR updates the `[Unreleased]` section before it merges** — the
CHANGELOG is part of the PR, not a later follow-up. Sections in use:

| Section | Use for |
|---|---|
| `Added` | New features, CLI flags, user-visible behaviour. |
| `Changed` | Existing behaviour changed — anything a user might notice. |
| `Deprecated` | Features still present but slated for removal. |
| `Removed` | Deleted features/flags/surfaces. |
| `Fixed` | Bugs fixed. |
| `Security` | Vulnerability fixes. |
| `Internal` | Refactors, tooling, test changes, anything invisible to users. Optional — skip if the PR has nothing else. |

Write entries as they'll read in release notes — imperative, concrete,
no PR numbers. Link to relevant docs if the entry warrants it. If a
PR truly has no user-visible change (pure chore / tooling), it still
belongs under `Internal` so the log stays a faithful record of work.

**Cutting a release:**

1. Rename `## [Unreleased]` to `## [x.y.z] - YYYY-MM-DD` (use the
   version GitVersion would compute — `dotnet-gitversion
   /showvariable SemVer`).
2. Open a fresh empty `## [Unreleased]` section above it.
3. Tag and release as usual.

## Code style

`.editorconfig` at the repo root carries the baseline rules
(4-space indent for `.cs`, 2-space for YAML/JSON/XML, file-scoped
namespaces, `var` preferred when the type is apparent, `_camelCase`
private fields).

Before committing large edits, run:

```bash
dotnet format
```

This will apply the `.editorconfig` rules across the solution.

## Tests

`dotnet test` from the repo root runs the full suite. All tests must
pass on `master`. Two tests touch golden fixtures in
`docs/reference-page/`:

- `Convert_ReferenceMarkdown_MatchesReferenceShape` — asserts the
  converter emits structurally-matching XML for the canonical reference
  markdown. If the reference rendering intent changes, update
  `MarkDow_VisualRef1.md` + `Reference-page.xml` together and adjust the
  test.
- `DumpSampleXml_ForManualInspection` — regenerates
  `samples/output/*.xml` and `docs/reference-page/MarkDow_VisualRef1.converted.xml`
  for eyeball-diffing. Those outputs are gitignored.

## Documentation

User-visible behavior changes belong in the README and/or
`docs/importer.md`. The importer mapping table in `docs/importer.md`
should stay truthful — bump it in the same commit as the converter
change, not a follow-up.
