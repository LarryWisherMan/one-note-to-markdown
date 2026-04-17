# Design: Publish-time target robustness

Status: draft for review
Issues: [#16](https://github.com/LarryWisherMan/one-note-to-markdown/issues/16) (spell-check), [#17](https://github.com/LarryWisherMan/one-note-to-markdown/issues/17) (auto-create)
Follow-ups: [#19](https://github.com/LarryWisherMan/one-note-to-markdown/issues/19) (notebook auto-create), [#6](https://github.com/LarryWisherMan/one-note-to-markdown/issues/6) (idempotent re-publish)
Milestone: M2 — Authored Markdown as first-class source

## Overview

Two small, related improvements to the publish path (`--import` and
`--publish`) that remove friction from the "author Markdown → see a clean
OneNote page" workflow:

1. **Spell-check suppression (#16):** imported pages no longer flag code,
   CLI flags, and identifiers as misspellings.
2. **Auto-create missing targets (#17):** `--publish` stops failing with
   "Section not found" when a section or section group in the resolved
   target path doesn't yet exist; `--import` gains an opt-in for the same
   behavior.

Both changes are scoped to the publish side (`MarkdownToOneNoteXmlConverter`,
`OneNoteService`, `ImportService`, `OneNoteTreePublisher`, `CliHandler`)
and introduce no changes to the markdown-source convention defined in
the folder-tree mapping spec.

## Decisions

| Decision | Choice | Rationale |
|---|---|---|
| #16 suppression mechanism | `lang="yo"` on `<one:Page>` + `<one:Title>` | The OneNote 2013 schema has no dedicated `noProof` attribute. `lang` is the only knob; setting it to a code OneNote has no dictionary for (Yoruba) is the same technique Onetastic's "No Spell Check" macro uses, and is the value the golden `docs/reference-page/Reference-page.xml` already carries. |
| #16 opt-in | Always-on, no flag | This importer targets technical notes. Spell-check on imported pages has no concrete user; YAGNI applies. If a per-page language override is needed later, it belongs in the front-matter schema (issue #3). |
| #17 `--import` default | Opt-in via `--create-missing` | Single-file import is surgical — a missing section is more likely a typo than a gap in the tree. Fail loudly by default. |
| #17 `--publish` default | On, escape via `--no-create-missing` | Bulk tree publish needs to "just work" — requiring the user to pre-create every section by hand defeats the feature. |
| #17 creation scope | Sections and section groups only | Notebook creation requires a storage-path decision (local vs OneDrive vs SharePoint) that this CLI has no principled answer for today. Deferred to #19. |
| #17 walk semantics | `mkdir -p` — create every missing intermediate | Matches the user mental model and the existing filesystem-shaped publish workflow. |
| #17 failure within one chain | Fail fast, no rollback | Matches OneNote UI behavior when its own operations are interrupted; rollback via COM is incomplete and risky. |
| #17 failure across pages in `--publish` | Continue + aggregate errors | Matches the existing per-file try/catch in `ImportService.ImportAsync`. |
| #17 testability | Extend `OneNoteService` with `EnsureSectionIdByPath`; test at the publisher/CLI boundary using fakes and dry-run fixtures | Respects CONTRIBUTING's "one PR = one logical change." A full COM-port abstraction is worthwhile but belongs in a separate refactor PR. |
| Rollout | Two PRs against `master`, #16 first | Each PR is one Conventional Commit; #16 is trivially isolated from #17; landing #16 first keeps master quiet while #17 iterates. |

## In scope

- Emitting `lang="yo"` on `<one:Page>` and `<one:Title>` from
  `MarkdownToOneNoteXmlConverter`.
- A new `EnsureSectionIdByPath` method on `OneNoteService` that walks a
  notebook / section-group / section chain and creates missing
  intermediates.
- A new `NotebookNotFoundException` type for the "cannot proceed"
  signal.
- A `--create-missing` / `--no-create-missing` pair on both `--import`
  and `--publish`, with different defaults per subcommand.
- A dry-run walk that prints `would create …` lines for missing
  intermediates without calling `OpenHierarchy`.
- CHANGELOG and docs updates for both PRs.

Per-page error aggregation in `--publish` is **already** implemented in
`PublishTreeService.PublishAsync` (`PublishTreeService.cs:158-178`). No
change to that loop's control flow is needed — auto-create errors surface
through the existing try/catch and land in `PublishTreeReport` alongside
any other per-file failure.

## Out of scope

- Notebook-level auto-create — tracked by [#19](https://github.com/LarryWisherMan/one-note-to-markdown/issues/19).
  Missing notebooks continue to error with a message that points at
  that issue.
- Persisting the newly-created page's OneNote ID back into the
  Markdown file's front-matter for idempotent re-publish — tracked by
  [#6](https://github.com/LarryWisherMan/one-note-to-markdown/issues/6).
  This spec composes cleanly with #6 later but does not itself
  implement write-back.
- Extracting a mockable `IOneNoteHierarchy` port around the COM
  surface. Worthwhile refactor, belongs in its own PR.
- Switching from `lang="yo"` to `lang="und"` / `lang="zxx"`. Both
  are semantically cleaner BCP 47 tags, but neither is empirically
  verified against OneNote's proofing pipeline in this repo. A
  comment at the emission site flags them as candidates for a
  future change.
- Any changes to `--export` (read side of the tool).

## #16 — Spell-check suppression

### Emission rule

`MarkdownToOneNoteXmlConverter.Convert` currently builds the page
element at `MarkdownToOneNoteXmlConverter.cs:83` and the title element
at line 94. Two attributes are added:

```csharp
// Top of class
private const string SpellCheckSuppressionLang = "yo";
// Yoruba — the Onetastic "No Spell Check" macro pattern: OneNote has no
// dictionary for this tag, so proofing stays silent. See
// https://getonetastic.com/macro/no-spell-check and
// docs/reference-page/Reference-page.xml (which ships with lang="yo").
// Candidates "und" / "zxx" are semantically cleaner but unverified.

var page = new XElement(OneNs + "Page",
    new XAttribute(XNamespace.Xmlns + "one", OneNs.NamespaceName),
    new XAttribute("name", resolvedTitle),
    new XAttribute("lang", SpellCheckSuppressionLang));

page.Add(new XElement(OneNs + "Title",
        new XAttribute("quickStyleIndex", QuickStylePageTitle),
        new XAttribute("lang", SpellCheckSuppressionLang),
        new XElement(OneNs + "OE",
            new XElement(OneNs + "T",
                new XCData(resolvedTitle)))));
```

No changes to QuickStyleDefs, OE-level styling, or the outline body.
No per-OE `lang` overrides.

### Tests

New unit tests in
`OneNoteMarkdownExporter.Tests/Converters/MarkdownToOneNoteXmlConverterTests.cs`:

- `Convert_EmitsLangYoOnPage` — round-trip a minimal markdown input and
  assert `<one:Page lang="yo">`.
- `Convert_EmitsLangYoOnTitle` — same setup, assert `<one:Title lang="yo">`.

The existing golden test
`Convert_ReferenceMarkdown_MatchesReferenceShape` already compares
against `Reference-page.xml`, which carries `lang="yo"` on both
elements. If the test was passing before this change, the comparison
is either loose or attribute-agnostic; adding the attribute cannot
regress it. If it does break, that surfaces useful signal about the
golden's strictness and we tighten or loosen per what's intentional.

### Downstream impact

`ImportService` and `OneNoteTreePublisher` are unaffected — they consume
the converter's output verbatim and splice in the page ID. The new
attribute rides through `UpdatePageContent` untouched.

## #17 — Auto-create missing sections

### New API surface

```csharp
namespace OneNoteMarkdownExporter.Services;

public class NotebookNotFoundException : Exception
{
    public NotebookNotFoundException(string notebookName)
        : base($"Notebook not found: {notebookName}. " +
               "Notebook-level auto-create is not yet supported — " +
               "see https://github.com/LarryWisherMan/one-note-to-markdown/issues/19. " +
               "Create the notebook in OneNote and retry.")
    {
        NotebookName = notebookName;
    }

    public string NotebookName { get; }
}

public partial class OneNoteService
{
    /// <summary>
    /// Resolves a section by explicit notebook → [section groups…] → section
    /// path, creating any missing section groups and the leaf section when
    /// <paramref name="createMissing"/> is true.
    /// </summary>
    /// <exception cref="NotebookNotFoundException">
    /// Thrown when the named notebook does not exist. Notebook creation is
    /// tracked by issue #19 and is not supported by this method.
    /// </exception>
    public string EnsureSectionIdByPath(
        string notebookName,
        IReadOnlyList<string> sectionGroups,
        string sectionName,
        bool createMissing,
        IProgress<string>? progress = null);
}
```

When `createMissing` is false, `EnsureSectionIdByPath` delegates to the
existing `FindSectionIdByPath`, returning `null` on miss. (The
null-return path preserves today's behavior for callers that choose
not to opt into creation.)

### Algorithm (when `createMissing` is true)

```
1. Call GetHierarchy(hsSections). Parse the XML.

2. Locate the notebook by case-insensitive name match on its
   name attribute. If not found → throw NotebookNotFoundException.

3. cursor = notebook element.
   For each sgName in sectionGroups:
     a. Find a child <SectionGroup> whose name attribute equals
        sgName case-insensitively.
     b. If found: cursor = that element, continue.
     c. If not found:
          - Read cursor's path attribute (the filesystem path
            OneNote tracks for that hierarchy node).
          - child = Path.Combine(cursor.path, sgName).
          - OpenHierarchy(child, cursor.Id, out newId,
                          CreateFileType.cftFolder).
          - progress?.Report($"Created section group: {sgName}").
          - Re-run GetHierarchy and set cursor to the new element
            (by Id).

4. At the leaf cursor, find a child <Section> with a case-insensitive
   name match.
   a. If found: return its Id.
   b. If not found:
        - child = Path.Combine(cursor.path, sectionName + ".one").
        - OpenHierarchy(child, cursor.Id, out newId,
                        CreateFileType.cftSection).
        - progress?.Report($"Created section: {sectionName}").
        - Return newId.
```

Case rule: a case-insensitive match against an existing hierarchy
element wins — no new element is created. When creation is needed,
the name is used verbatim from the resolved path (preserving the
casing expressed in the Markdown tree's folder / front-matter). This
matches the existing case handling in `FindSectionIdByPath`.

### CLI surface

Two new flags, parsed by `CliHandler`:

| Flag | Subcommand | Effect |
|---|---|---|
| `--create-missing` | `--import`, `--publish` | Force auto-create on. |
| `--no-create-missing` | `--import`, `--publish` | Force auto-create off. |

Defaults per subcommand:

| Subcommand | Default | Reason |
|---|---|---|
| `--import` | `false` | Single file; a missing section usually means a typo. |
| `--publish` | `true` | Tree publish; pre-creating every section defeats the feature. |

Passing both flags is a CLI error.

### Dry-run behavior

`--dry-run` continues to skip `UpdatePageContent` and `CreatePage`.
For #17, it must additionally walk the hierarchy without mutating it:

- `GetHierarchy` is still called (read-only).
- The walk from the algorithm above runs, but `OpenHierarchy` is
  replaced by a progress line of the form `would create section group: X`
  / `would create section: Y`.

**Wiring the dry-run walk through the publisher.** `IOneNotePublisher.PublishAsync`
(defined inline in `PublishTreeService.cs:16-26`) gains two new parameters:

```csharp
public interface IOneNotePublisher
{
    Task PublishAsync(
        string notebook,
        IReadOnlyList<string> sectionGroups,
        string section,
        string pageTitle,
        string markdownContent,
        string sourceFileFullPath,
        bool collapsible,
        bool createMissing,   // NEW
        bool dryRun);         // NEW
}
```

`PublishTreeService.PublishAsync` pass 3 (currently
`PublishTreeService.cs:144-179`) no longer short-circuits on dry-run
before calling the publisher. Both branches funnel into the publisher,
which decides what a dry-run means for its own work (read hierarchy,
log `would create …`, skip page creation, return). The existing
`[dry-run] {fileRel} → {target}` log line in `PublishTreeService`
stays, and the publisher's `would create …` lines appear nested
underneath it.

`OneNoteTreePublisher.PublishAsync` becomes:

```
1. sectionId = _oneNoteService.EnsureSectionIdByPath(
       notebook, sectionGroups, section, createMissing, dryRun, progress);
2. If dryRun: return.
3. Convert markdown → XML. CreatePage. UpdatePageContent.
```

**Testability seam.** `EnsureSectionIdByPath` takes the hierarchy XML
from `GetHierarchy` as input; for unit tests, an internal overload
accepts a supplied XML string directly:

```csharp
internal string? EnsureSectionIdByPathFromXml(
    string hierarchyXml,          // already-fetched
    string notebookName,
    IReadOnlyList<string> sectionGroups,
    string sectionName,
    bool createMissing,
    bool dryRun,                  // suppress OpenHierarchy
    IProgress<string>? progress);
```

The public entry point reads `GetHierarchy`, calls the overload with
`dryRun: false`, and returns the Id. Dry-run callers pass
`dryRun: true`. Unit tests construct fixture XML and call the overload
directly — no COM required.

Same pattern on the `--import` side: `ImportService.ImportAsync`
(`ImportService.cs:27-38`) no longer gates the section lookup on
`!options.DryRun`. It always calls `EnsureSectionIdByPath(..., dryRun:
options.DryRun, createMissing: options.CreateMissing)`. In dry-run the
method previews and returns an Id or null (null is fine — we aren't
creating a page); in live mode it actually creates.

### Error handling

Within a single `EnsureSectionIdByPath` call:

- `NotebookNotFoundException` — notebook missing, always fatal.
- `COMException` from `OpenHierarchy` — bubbles up unchanged. Whatever
  got created before the failure stays put (no rollback).
- Any other exception — bubbles up.

Across pages in `--publish`:

- No new code. `PublishTreeService.PublishAsync`
  (`PublishTreeService.cs:158-178`) already wraps each per-file publish
  call in try/catch and records failures in `PublishTreeReport` via
  `RecordError`. Auto-create exceptions propagate from
  `EnsureSectionIdByPath` through `OneNoteTreePublisher.PublishAsync`
  and land in that existing catch unchanged.
- `--publish` already exits non-zero when `PublishTreeReport.HasErrors`,
  per the current CLI behavior.

The shape matches the existing per-file try/catch at
`ImportService.ImportAsync:50-57`.

### Caller wiring

- `ImportService.ImportAsync` — replace the `FindSectionId` call at
  `ImportService.cs:29` with `EnsureSectionIdByPath`. The legacy
  two-argument method walked only the direct children of a notebook
  for a named section; `EnsureSectionIdByPath` with zero section
  groups is an exact behavioral superset and preserves `--import`'s
  current expectations (notebook name + section name, no section
  groups in the CLI). The existing `if (!options.DryRun)` guard at
  `ImportService.cs:27` is removed — `EnsureSectionIdByPath` itself
  is the dry-run-aware entry point. New `CreateMissing` field on
  `ImportOptions`, default `false`, wired from the CLI.
- `OneNoteTreePublisher.PublishAsync` — replace the
  `FindSectionIdByPath` call at `OneNoteTreePublisher.cs:35` with
  `EnsureSectionIdByPath`. New `createMissing` and `dryRun` parameters
  on the signature (see Dry-run section for the full interface). The
  publisher still throws on any error; per-page error aggregation stays
  in `PublishTreeService.PublishAsync` as it already does.
- `PublishTreeService.PublishAsync` — pass 3's dry-run branch
  (`PublishTreeService.cs:151-156`) is removed in favor of a single
  call to `_publisher.PublishAsync(..., createMissing, options.DryRun)`
  that handles both modes. `PublishTreeOptions` gains `CreateMissing`
  (default `true`, matching `--publish`'s subcommand default). The
  `[dry-run] {entry.FileRel} → {target}` log line moves to sit beside
  the publisher call so it still prints on every dry-run file.
- `CliHandler` — parse the new flags; reject passing both.

### Tests

Unit tests at the publisher / CLI boundary:

- `OneNoteMarkdownExporter.Tests/Cli/CliHandlerTests.cs` —
  `--create-missing` and `--no-create-missing` flip the flag correctly
  on both subcommands; correct defaults (`false` for `--import`, `true`
  for `--publish`); passing both at once is a usage error.
- `OneNoteMarkdownExporter.Tests/Services/ImportServiceTests.cs` — the
  flag reaches the `OneNoteService` call site. Since `OneNoteService`
  is not interface-backed today, either: (a) extract a minimal
  interface just for the method(s) `ImportService` uses and inject a
  fake, or (b) keep tests that assert against an intercepted
  `IProgress<string>` stream plus the fixture-XML overload. Option (a)
  is cleaner but adds surface; option (b) is less invasive. Plan
  author picks; both are acceptable.
- `OneNoteMarkdownExporter.Tests/Services/PublishTreeServiceTests.cs`
  — assert that dry-run calls `_publisher.PublishAsync` instead of
  short-circuiting; assert `createMissing` propagates from
  `PublishTreeOptions` to the publisher call.
- Existing `PublishTreeServiceTests` error-aggregation coverage already
  exercises the catch block at `PublishTreeService.cs:170-178`. No
  change needed there.

Dry-run walk tests (the real coverage for the algorithm):

- Fixtures under `OneNoteMarkdownExporter.Tests/Fixtures/hierarchy/`:
  - `existing-section.xml` — path fully present.
  - `missing-leaf-section.xml` — section groups present, leaf missing.
  - `missing-section-group.xml` — one intermediate missing.
  - `missing-all-intermediates.xml` — nothing but the notebook.
  - `missing-notebook.xml` — notebook absent.
- Each fixture fuels a parameterized test that calls
  `OneNoteService.EnsureSectionIdByPathFromXml(fixtureXml, ...,
  dryRun: true)` and asserts the exact sequence of progress messages
  (and, for missing-notebook, that `NotebookNotFoundException` is
  thrown).

Integration / manual smoke tests (documented in the PR description,
not run by CI):

- `--publish ./some-tree --dry-run --verbose --create-missing` against
  a real OneNote — confirm the `would create …` output matches
  expectations.
- `--publish ./some-tree` against a throwaway notebook — confirm
  sections actually get created and pages land where expected.

## Rollout

### PR 1 — `feat(importer): suppress OneNote spell-check on imported pages`

Branch: `feat/importer-spellcheck-suppression`

Files:

- `OneNoteMarkdownExporter/Services/MarkdownToOneNoteXmlConverter.cs`
- `OneNoteMarkdownExporter.Tests/Converters/MarkdownToOneNoteXmlConverterTests.cs`
- `docs/importer.md` — short paragraph under "Markdown → OneNote
  mapping" explaining the `lang="yo"` emission and how to re-enable a
  real language in OneNote.
- `CHANGELOG.md` `[Unreleased]` → `Changed`:
  - "Imported and published pages now suppress OneNote spell-check
    via `lang=\"yo\"` so technical content renders without red
    squiggles."

### PR 2 — `feat(publish): auto-create missing sections and section groups`

Branch: `feat/publish-auto-create-missing`

Files:

- `OneNoteMarkdownExporter/Services/OneNoteService.cs` —
  `EnsureSectionIdByPath`, `NotebookNotFoundException`.
- `OneNoteMarkdownExporter/Services/ImportService.cs` — use
  `EnsureSectionIdByPath`, read `CreateMissing` from options.
- `OneNoteMarkdownExporter/Models/ImportOptions.cs` — add
  `CreateMissing` (default `false`).
- `OneNoteMarkdownExporter/Services/PublishTreeService.cs` — extend
  the inline `IOneNotePublisher` interface with `createMissing` and
  `dryRun` parameters; remove the dry-run short-circuit in pass 3 so
  both modes go through the publisher.
- `OneNoteMarkdownExporter/Services/OneNoteTreePublisher.cs` —
  propagate flags; call `EnsureSectionIdByPath`; return early on
  dry-run after the walk preview.
- `OneNoteMarkdownExporter/Services/PublishTreeOptions.cs` — add
  `CreateMissing` (default `true`).
- `OneNoteMarkdownExporter/Services/CliHandler.cs` — parse
  `--create-missing` / `--no-create-missing` on both subcommands,
  different defaults.
- `OneNoteMarkdownExporter.Tests/...` — unit tests + dry-run walk
  tests + fixtures.
- `docs/importer.md`:
  - Add `--create-missing` row to the `--import` flag table.
  - Update "Tree publish" section to describe default-on auto-create
    and the `--no-create-missing` escape.
- `README.md` — one-line mention if current text claims `--publish`
  errors on missing sections.
- `CHANGELOG.md` `[Unreleased]`:
  - `Added`: "`--publish` auto-creates missing sections and section
    groups by default; opt out with `--no-create-missing`."
  - `Added`: "`--import --create-missing` creates a missing target
    section or section group before publishing (opt-in)."

### Commit discipline

Per `CONTRIBUTING.md`:

- Each PR's title is the squash-merge commit on `master` — it must be
  a single Conventional Commit. Exact subjects are the section headings
  above.
- Intermediate commits on each branch can be messy; only the PR title
  matters once squashed.
- `Squash and merge` in the GitHub UI — never `Create a merge commit`
  or `Rebase and merge`.
- No `Co-Authored-By: Claude …` trailer on any commit.
- Delete the source branch after merge.

PR 2 is branched from `master` **after** PR 1 merges. Smoke tests are
run manually before each merge and their results documented in the
PR description.

## References

- [OneNote 2013 developer reference](https://learn.microsoft.com/en-us/office/client-developer/onenote/onenote-developer-reference)
- [Onetastic — Set Proofing Language macro](https://getonetastic.com/macro/9FBBC135E11C4949AF612385A07551B9)
  (confirms `lang` is the attribute the OneNote proofing pipeline reads)
- [Onetastic — No Spell Check macro](https://getonetastic.com/macro/no-spell-check)
  (confirms the "set `lang` to a dictionary-less tag" pattern)
- `docs/reference-page/Reference-page.xml` — the golden XML this repo
  tunes against; already carries `lang="yo"` on `<one:Page>` and
  `<one:Title>`.
- `docs/superpowers/specs/2026-04-16-folder-tree-mapping-design.md` —
  the mapping spec that established `--publish` and the front-matter
  convention this spec builds on.
