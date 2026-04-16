# Contributing

This is primarily a solo-dev project, but these conventions exist so the
history stays navigable and a future contributor (or fork) can follow along.

## Development workflow

**Trunk-based.** `master` is always green. Short-lived topic branches for
anything bigger than a one-line fix; merge/squash back into `master` when
done. No long-running `develop` branch, no release branches.

```bash
git checkout -b feat/importer-image-sizing
# ...work, commit, test...
git checkout master
git merge --squash feat/importer-image-sizing
git commit
```

Rebase on top of `master` before merging when the branch goes more than
a day or two without sync.

## Commit messages — Conventional Commits

Commit subjects use [Conventional Commits](https://www.conventionalcommits.org/).
GitVersion reads these to compute the next semver.

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

Keep commits focused — one logical change per commit — so reverts stay
clean.

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
with a `gittools/actions/gitversion/execute@v3` call before pushing the
release workflow back into use.

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
