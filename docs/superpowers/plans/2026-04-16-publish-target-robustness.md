# Publish-time target robustness — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship two small, related improvements to the publish path: (1) suppress OneNote spell-check on imported pages by emitting `lang="yo"` on `<one:Page>` and `<one:Title>`, and (2) auto-create missing section groups and sections when publishing a markdown tree.

**Architecture:** PR 1 is a single-attribute change in `MarkdownToOneNoteXmlConverter`. PR 2 introduces a pure `SectionHierarchyWalker` that produces a `SectionResolutionPlan` (list of steps), wrapped by `OneNoteService.EnsureSectionIdByPath` which executes the plan against COM. Dry-run paths share the same walker output. CLI gets `--create-missing` / `--no-create-missing` with per-subcommand defaults.

**Tech Stack:** .NET 8, C# 12, xUnit + FluentAssertions, Markdig, Microsoft.Office.Interop.OneNote (COM), System.CommandLine.

**Spec:** `docs/superpowers/specs/2026-04-16-publish-target-robustness-design.md`

**Related dotnet-skills to consult:**
- `dotnet-skills:modern-csharp-coding-standards` — records, pattern matching, sealed types (used heavily in PR 2).
- `dotnet-skills:api-design` — the new `OneNoteService.EnsureSectionIdByPath` is public surface; keep it stable.
- `dotnet-skills:type-design-performance` — `SectionResolutionPlan` and steps are `sealed record`s.

**Conventions (all PRs):**
- TDD: write failing test → run → implement → run → commit.
- Commit per TDD cycle — keep commits small.
- **No `Co-Authored-By: Claude …` trailer** on any commit (project convention).
- PR titles are Conventional Commits; they become the squash-merge commit on `master`.
- Update `CHANGELOG.md` `[Unreleased]` in the same PR.
- `dotnet test` must pass before every commit.

---

# PR 1 — `feat(importer): suppress OneNote spell-check on imported pages`

Branch: `feat/importer-spellcheck-suppression`

## Task 1: Create the PR 1 branch

**Files:**
- None (git operations only).

- [ ] **Step 1: Verify clean tree on master**

```bash
git status
git checkout master
git pull
```

Expected: "working tree clean" and `master` up to date.

- [ ] **Step 2: Create and switch to the feature branch**

```bash
git checkout -b feat/importer-spellcheck-suppression
```

Expected: "Switched to a new branch …".

---

## Task 2: Emit `lang="yo"` on `<one:Page>`

**Files:**
- Modify: `OneNoteMarkdownExporter/Services/MarkdownToOneNoteXmlConverter.cs:83-85`
- Test: `OneNoteMarkdownExporter.Tests/Converters/MarkdownToOneNoteXmlConverterTests.cs`

- [ ] **Step 1: Add the failing test**

Append to the bottom of `MarkdownToOneNoteXmlConverterTests.cs`, before the closing `}` of the class:

```csharp
    #region Spell-check suppression

    [Fact]
    public void Convert_EmitsLangYoOnPage_ToSuppressSpellCheck()
    {
        var result = _converter.Convert("body", pageTitle: "Test");
        var doc = ParseResult(result);

        doc.Root!.Attribute("lang")?.Value.Should().Be("yo");
    }

    #endregion
```

- [ ] **Step 2: Run the test and see it fail**

```bash
dotnet test --filter "FullyQualifiedName~Convert_EmitsLangYoOnPage"
```

Expected: one test failed with either a null `lang` attribute or missing-attribute assertion failure.

- [ ] **Step 3: Implement — add the `lang` attribute to the page element**

In `MarkdownToOneNoteXmlConverter.cs`, modify the page construction (currently lines 83-85):

```csharp
        var page = new XElement(OneNs + "Page",
            new XAttribute(XNamespace.Xmlns + "one", OneNs.NamespaceName),
            new XAttribute("name", resolvedTitle),
            new XAttribute("lang", "yo"));
```

- [ ] **Step 4: Run the test and see it pass**

```bash
dotnet test --filter "FullyQualifiedName~Convert_EmitsLangYoOnPage"
```

Expected: 1 passed, 0 failed.

- [ ] **Step 5: Run the full converter test suite to confirm no regressions**

```bash
dotnet test --filter "FullyQualifiedName~MarkdownToOneNoteXmlConverterTests"
```

Expected: all passing.

- [ ] **Step 6: Commit**

```bash
git add OneNoteMarkdownExporter/Services/MarkdownToOneNoteXmlConverter.cs OneNoteMarkdownExporter.Tests/Converters/MarkdownToOneNoteXmlConverterTests.cs
git commit -m "test: emit lang=\"yo\" on OneNote page element"
```

---

## Task 3: Emit `lang="yo"` on `<one:Title>`

**Files:**
- Modify: `OneNoteMarkdownExporter/Services/MarkdownToOneNoteXmlConverter.cs:94-98`
- Test: `OneNoteMarkdownExporter.Tests/Converters/MarkdownToOneNoteXmlConverterTests.cs`

- [ ] **Step 1: Add the failing test**

Add below the page test in the `Spell-check suppression` region:

```csharp
    [Fact]
    public void Convert_EmitsLangYoOnTitle_ToSuppressSpellCheck()
    {
        var result = _converter.Convert("body", pageTitle: "Test");
        var doc = ParseResult(result);

        var title = doc.Root!.Element(OneNs + "Title");
        title.Should().NotBeNull();
        title!.Attribute("lang")?.Value.Should().Be("yo");
    }
```

- [ ] **Step 2: Run the test and see it fail**

```bash
dotnet test --filter "FullyQualifiedName~Convert_EmitsLangYoOnTitle"
```

Expected: fail on missing `lang`.

- [ ] **Step 3: Implement — add `lang` attribute to the Title element**

In `MarkdownToOneNoteXmlConverter.cs` lines 94-98, change:

```csharp
        page.Add(new XElement(OneNs + "Title",
                new XAttribute("quickStyleIndex", QuickStylePageTitle),
                new XAttribute("lang", "yo"),
                new XElement(OneNs + "OE",
                    new XElement(OneNs + "T",
                        new XCData(resolvedTitle)))));
```

- [ ] **Step 4: Run the test and see it pass**

```bash
dotnet test --filter "FullyQualifiedName~Convert_EmitsLangYoOnTitle"
```

Expected: pass.

- [ ] **Step 5: Commit**

```bash
git add OneNoteMarkdownExporter/Services/MarkdownToOneNoteXmlConverter.cs OneNoteMarkdownExporter.Tests/Converters/MarkdownToOneNoteXmlConverterTests.cs
git commit -m "test: emit lang=\"yo\" on OneNote title element"
```

---

## Task 4: Extract `SpellCheckSuppressionLang` constant with explanatory comment

**Files:**
- Modify: `OneNoteMarkdownExporter/Services/MarkdownToOneNoteXmlConverter.cs` (constants block around line 41-50)

- [ ] **Step 1: Add the constant**

Near the other constants in the class (after the `SpanClose` line, around line 50), add:

```csharp
    // Yoruba — the Onetastic "No Spell Check" macro pattern:
    // OneNote has no dictionary for this tag, so the proofing pipeline
    // stays silent. See https://getonetastic.com/macro/no-spell-check
    // and docs/reference-page/Reference-page.xml, which also carries
    // lang="yo" on <one:Page> and <one:Title>. Candidates "und" / "zxx"
    // are semantically cleaner BCP 47 values but unverified against
    // the real OneNote proofing pipeline in this repo.
    private const string SpellCheckSuppressionLang = "yo";
```

- [ ] **Step 2: Replace the two string literals with the constant**

In page construction (Task 2 output):

```csharp
            new XAttribute("lang", SpellCheckSuppressionLang));
```

In title construction (Task 3 output):

```csharp
                new XAttribute("lang", SpellCheckSuppressionLang),
```

- [ ] **Step 3: Run the converter test suite**

```bash
dotnet test --filter "FullyQualifiedName~MarkdownToOneNoteXmlConverterTests"
```

Expected: all passing.

- [ ] **Step 4: Commit**

```bash
git add OneNoteMarkdownExporter/Services/MarkdownToOneNoteXmlConverter.cs
git commit -m "refactor: extract SpellCheckSuppressionLang constant"
```

---

## Task 5: Run the full test suite and verify the golden reference test

**Files:**
- None (verification only).

- [ ] **Step 1: Run every test in the solution**

```bash
dotnet test
```

Expected: all passing, including `Convert_ReferenceMarkdown_MatchesReferenceShape`.

**If the golden test fails**: the comparison was attribute-aware and is now *better matching* because we emit what the reference has. Review the failure diff. If the failure is about `lang` mismatch in places other than Page/Title, scope-creep warning — investigate but don't widen Task 4. If it is genuinely a structural regression, stop and diagnose before continuing. Either way, the fix is cheap and local.

- [ ] **Step 2: If all green, move on**

No commit needed — this is a verification step.

---

## Task 6: Update `docs/importer.md` and `CHANGELOG.md`

**Files:**
- Modify: `docs/importer.md` (add short note under "Markdown → OneNote mapping" section)
- Modify: `CHANGELOG.md` (`[Unreleased]` section)

- [ ] **Step 1: Add a `Spell-check suppression` subsection to `docs/importer.md`**

Insert immediately after the "Blank-line spacing" subsection and before "Collapsible headings":

```markdown
### Spell-check suppression

Every imported or published page is emitted with `lang="yo"` on both
`<one:Page>` and `<one:Title>`. OneNote has no proofing dictionary for
Yoruba, so the page renders without red squiggles — useful for
technical content (code snippets, CLI flags, variable names) that
would otherwise be flagged as misspellings.

To re-enable spell-check for a specific language on a published page,
open it in OneNote and use `Review → Language → Set Proofing Language`.
A front-matter-driven per-page override is tracked separately (see
issue #3).
```

- [ ] **Step 2: Update `CHANGELOG.md`**

In the `[Unreleased]` section, under `### Changed` (create the subsection if it doesn't exist), add:

```markdown
- Imported and published pages now suppress OneNote spell-check via
  `lang="yo"` on `<one:Page>` and `<one:Title>`, so technical content
  renders without red squiggles.
```

- [ ] **Step 3: Commit**

```bash
git add docs/importer.md CHANGELOG.md
git commit -m "docs: note lang=\"yo\" spell-check suppression"
```

---

## Task 7: Push branch and open PR 1

**Files:**
- None (git / gh operations).

- [ ] **Step 1: Push the branch**

```bash
git push -u origin feat/importer-spellcheck-suppression
```

- [ ] **Step 2: Open the PR**

```bash
gh pr create --title "feat(importer): suppress OneNote spell-check on imported pages" --body "$(cat <<'EOF'
## Summary

- Emit `lang="yo"` on `<one:Page>` and `<one:Title>` so imported pages
  render without red squiggles on technical content.
- Mirrors the golden `docs/reference-page/Reference-page.xml`, which
  already uses `lang="yo"`.
- Same mechanism Onetastic's "No Spell Check" macro uses.

Closes #16. Design: `docs/superpowers/specs/2026-04-16-publish-target-robustness-design.md`.

## Test plan

- [x] `Convert_EmitsLangYoOnPage_ToSuppressSpellCheck`
- [x] `Convert_EmitsLangYoOnTitle_ToSuppressSpellCheck`
- [x] Full `dotnet test` green
- [ ] Manual smoke: `OneNoteMarkdownExporter.exe --import "NB/Section" --file sample.md`, open the new page in OneNote, confirm no red squiggles on code blocks.
EOF
)"
```

- [ ] **Step 3: Record the PR URL in the task description**

Track the PR URL so Task 1 of PR 2 can wait for merge.

- [ ] **Step 4: STOP — wait for PR 1 to merge to master before starting PR 2**

After the "Squash and merge" button is clicked and the branch is deleted, continue with PR 2 Task 1.

---

# PR 2 — `feat(publish): auto-create missing sections and section groups`

Branch: `feat/publish-auto-create-missing`, created **after** PR 1 merges.

## Task 8: Create the PR 2 branch from post-PR 1 master

**Files:**
- None (git operations).

- [ ] **Step 1: Sync master**

```bash
git checkout master
git pull
```

Expected: master has the PR 1 squash-merge commit.

- [ ] **Step 2: Create the branch**

```bash
git checkout -b feat/publish-auto-create-missing
```

---

## Task 9: Add `NotebookNotFoundException`

**Files:**
- Create: `OneNoteMarkdownExporter/Services/NotebookNotFoundException.cs`
- Test: `OneNoteMarkdownExporter.Tests/Services/NotebookNotFoundExceptionTests.cs`

- [ ] **Step 1: Write the failing test**

Create `OneNoteMarkdownExporter.Tests/Services/NotebookNotFoundExceptionTests.cs`:

```csharp
using FluentAssertions;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Services;

public class NotebookNotFoundExceptionTests
{
    [Fact]
    public void Constructor_SetsNotebookNameAndIncludesIssue19InMessage()
    {
        var ex = new NotebookNotFoundException("Work Notes");

        ex.NotebookName.Should().Be("Work Notes");
        ex.Message.Should().Contain("Work Notes");
        ex.Message.Should().Contain("19");
    }
}
```

- [ ] **Step 2: Run the test and see it fail**

```bash
dotnet test --filter "FullyQualifiedName~NotebookNotFoundExceptionTests"
```

Expected: compile error — type does not exist.

- [ ] **Step 3: Create the exception class**

Write `OneNoteMarkdownExporter/Services/NotebookNotFoundException.cs`:

```csharp
using System;

namespace OneNoteMarkdownExporter.Services;

/// <summary>
/// Thrown when a resolved publish target references a notebook that does not
/// exist in OneNote. Notebook-level auto-create is tracked by issue #19.
/// </summary>
public class NotebookNotFoundException : Exception
{
    public NotebookNotFoundException(string notebookName)
        : base(
            $"Notebook not found: {notebookName}. " +
            "Notebook-level auto-create is not yet supported — " +
            "see https://github.com/LarryWisherMan/one-note-to-markdown/issues/19. " +
            "Create the notebook in OneNote and retry.")
    {
        NotebookName = notebookName;
    }

    public string NotebookName { get; }
}
```

- [ ] **Step 4: Run the test and see it pass**

```bash
dotnet test --filter "FullyQualifiedName~NotebookNotFoundExceptionTests"
```

Expected: pass.

- [ ] **Step 5: Commit**

```bash
git add OneNoteMarkdownExporter/Services/NotebookNotFoundException.cs OneNoteMarkdownExporter.Tests/Services/NotebookNotFoundExceptionTests.cs
git commit -m "feat: add NotebookNotFoundException for missing-notebook signal"
```

---

## Task 10: Define `SectionResolutionPlan` and `CreationStep` types

**Files:**
- Create: `OneNoteMarkdownExporter/Services/SectionResolutionPlan.cs`
- Test: none yet — these are data types; they get exercised by the walker in Task 12.

- [ ] **Step 1: Write the types**

Create `OneNoteMarkdownExporter/Services/SectionResolutionPlan.cs`:

```csharp
using System.Collections.Generic;

namespace OneNoteMarkdownExporter.Services;

/// <summary>
/// Result of walking an existing OneNote hierarchy for a given
/// notebook / section-groups / section path. Consumers interpret the plan:
/// dry-run callers print the steps; live callers execute them.
/// </summary>
public sealed record SectionResolutionPlan(
    /// <summary>When non-null, the section already exists and has this ID.
    /// <see cref="DeepestExistingAncestorId"/> and <see cref="CreationSteps"/>
    /// are irrelevant when this is set.</summary>
    string? ExistingSectionId,

    /// <summary>ID of the deepest hierarchy node that does exist — either the
    /// notebook or a section group. Serves as the parent for the first
    /// creation step. Empty string when <see cref="ExistingSectionId"/> is set.</summary>
    string DeepestExistingAncestorId,

    /// <summary>Ordered creation steps that would produce the target section
    /// starting from <see cref="DeepestExistingAncestorId"/>. Empty when the
    /// section already exists, or when createMissing was false and any link
    /// was missing.</summary>
    IReadOnlyList<CreationStep> CreationSteps)
{
    /// <summary>True when createMissing was false and the target section
    /// could not be resolved against the existing hierarchy.</summary>
    public bool IsUnresolved =>
        ExistingSectionId is null && CreationSteps.Count == 0;
}

public sealed record CreationStep(
    CreationKind Kind,
    string Name,
    string TargetPath);

public enum CreationKind
{
    SectionGroup,
    Section,
}
```

- [ ] **Step 2: Build to confirm types compile**

```bash
dotnet build
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add OneNoteMarkdownExporter/Services/SectionResolutionPlan.cs
git commit -m "feat: add SectionResolutionPlan and CreationStep types"
```

---

## Task 11: Add hierarchy-walker test fixtures

**Files:**
- Create: `OneNoteMarkdownExporter.Tests/Fixtures/hierarchy/existing-section.xml`
- Create: `OneNoteMarkdownExporter.Tests/Fixtures/hierarchy/missing-leaf-section.xml`
- Create: `OneNoteMarkdownExporter.Tests/Fixtures/hierarchy/missing-intermediate.xml`
- Create: `OneNoteMarkdownExporter.Tests/Fixtures/hierarchy/missing-all-intermediates.xml`
- Create: `OneNoteMarkdownExporter.Tests/Fixtures/hierarchy/missing-notebook.xml`
- Modify: `OneNoteMarkdownExporter.Tests/OneNoteMarkdownExporter.Tests.csproj` (copy fixtures to output)

Each fixture represents the return value of `Application.GetHierarchy(null, hsSections)` for a specific state. The walker will target path `Work Notes / Backend / API / auth-spec`.

- [ ] **Step 1: Write `existing-section.xml` — full path present**

```xml
<?xml version="1.0"?>
<one:Notebooks xmlns:one="http://schemas.microsoft.com/office/onenote/2013/onenote">
  <one:Notebook name="Work Notes" ID="{NB}{1}{B0}" path="C:\Users\Test\Documents\OneNote Notebooks\Work Notes\">
    <one:SectionGroup name="Backend" ID="{SG-B}{1}{B0}" path="C:\Users\Test\Documents\OneNote Notebooks\Work Notes\Backend\">
      <one:SectionGroup name="API" ID="{SG-A}{1}{B0}" path="C:\Users\Test\Documents\OneNote Notebooks\Work Notes\Backend\API\">
        <one:Section name="auth-spec" ID="{SEC}{1}{B0}" path="C:\Users\Test\Documents\OneNote Notebooks\Work Notes\Backend\API\auth-spec.one"/>
      </one:SectionGroup>
    </one:SectionGroup>
  </one:Notebook>
</one:Notebooks>
```

- [ ] **Step 2: Write `missing-leaf-section.xml` — section groups present, leaf section missing**

```xml
<?xml version="1.0"?>
<one:Notebooks xmlns:one="http://schemas.microsoft.com/office/onenote/2013/onenote">
  <one:Notebook name="Work Notes" ID="{NB}{1}{B0}" path="C:\Users\Test\Documents\OneNote Notebooks\Work Notes\">
    <one:SectionGroup name="Backend" ID="{SG-B}{1}{B0}" path="C:\Users\Test\Documents\OneNote Notebooks\Work Notes\Backend\">
      <one:SectionGroup name="API" ID="{SG-A}{1}{B0}" path="C:\Users\Test\Documents\OneNote Notebooks\Work Notes\Backend\API\"/>
    </one:SectionGroup>
  </one:Notebook>
</one:Notebooks>
```

- [ ] **Step 3: Write `missing-intermediate.xml` — `Backend` present, `API` missing**

```xml
<?xml version="1.0"?>
<one:Notebooks xmlns:one="http://schemas.microsoft.com/office/onenote/2013/onenote">
  <one:Notebook name="Work Notes" ID="{NB}{1}{B0}" path="C:\Users\Test\Documents\OneNote Notebooks\Work Notes\">
    <one:SectionGroup name="Backend" ID="{SG-B}{1}{B0}" path="C:\Users\Test\Documents\OneNote Notebooks\Work Notes\Backend\"/>
  </one:Notebook>
</one:Notebooks>
```

- [ ] **Step 4: Write `missing-all-intermediates.xml` — notebook only, no children**

```xml
<?xml version="1.0"?>
<one:Notebooks xmlns:one="http://schemas.microsoft.com/office/onenote/2013/onenote">
  <one:Notebook name="Work Notes" ID="{NB}{1}{B0}" path="C:\Users\Test\Documents\OneNote Notebooks\Work Notes\"/>
</one:Notebooks>
```

- [ ] **Step 5: Write `missing-notebook.xml` — notebook is absent**

```xml
<?xml version="1.0"?>
<one:Notebooks xmlns:one="http://schemas.microsoft.com/office/onenote/2013/onenote">
  <one:Notebook name="Personal" ID="{NB-P}{1}{B0}" path="C:\Users\Test\Documents\OneNote Notebooks\Personal\"/>
</one:Notebooks>
```

- [ ] **Step 6: Ensure fixtures copy to the test-output directory**

Open `OneNoteMarkdownExporter.Tests/OneNoteMarkdownExporter.Tests.csproj`. If there is no `<ItemGroup>` that already copies `Fixtures/**`, add one:

```xml
  <ItemGroup>
    <Content Include="Fixtures\**\*.xml">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
```

If a similar `<Content Include="Fixtures\**" />` already exists, leave it alone.

- [ ] **Step 7: Build to confirm fixtures are picked up**

```bash
dotnet build OneNoteMarkdownExporter.Tests
```

Confirm `OneNoteMarkdownExporter.Tests/bin/Debug/net8.0/Fixtures/hierarchy/existing-section.xml` exists.

- [ ] **Step 8: Commit**

```bash
git add OneNoteMarkdownExporter.Tests/Fixtures/hierarchy OneNoteMarkdownExporter.Tests/OneNoteMarkdownExporter.Tests.csproj
git commit -m "test: add hierarchy fixtures for SectionHierarchyWalker"
```

---

## Task 12: Implement `SectionHierarchyWalker.Plan` — existing-section happy path

**Files:**
- Create: `OneNoteMarkdownExporter/Services/SectionHierarchyWalker.cs`
- Create: `OneNoteMarkdownExporter.Tests/Services/SectionHierarchyWalkerTests.cs`

- [ ] **Step 1: Write the failing test**

Create `OneNoteMarkdownExporter.Tests/Services/SectionHierarchyWalkerTests.cs`:

```csharp
using System.IO;
using FluentAssertions;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Services;

public class SectionHierarchyWalkerTests
{
    private static string LoadFixture(string name) =>
        File.ReadAllText(Path.Combine("Fixtures", "hierarchy", name));

    [Fact]
    public void Plan_ExistingSection_ReturnsExistingSectionId()
    {
        var xml = LoadFixture("existing-section.xml");

        var plan = SectionHierarchyWalker.Plan(
            xml,
            notebookName: "Work Notes",
            sectionGroups: new[] { "Backend", "API" },
            sectionName: "auth-spec",
            createMissing: true);

        plan.ExistingSectionId.Should().Be("{SEC}{1}{B0}");
        plan.CreationSteps.Should().BeEmpty();
        plan.IsUnresolved.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run and see it fail**

```bash
dotnet test --filter "FullyQualifiedName~SectionHierarchyWalkerTests"
```

Expected: compile error — `SectionHierarchyWalker` does not exist.

- [ ] **Step 3: Create the walker with the minimal implementation**

Create `OneNoteMarkdownExporter/Services/SectionHierarchyWalker.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace OneNoteMarkdownExporter.Services;

/// <summary>
/// Pure walker over a OneNote hierarchy XML (from
/// <c>Application.GetHierarchy(hsSections)</c>) that resolves a
/// notebook → section-groups → section path and produces a
/// <see cref="SectionResolutionPlan"/> describing what to do.
/// </summary>
public static class SectionHierarchyWalker
{
    private static readonly XNamespace OneNs =
        "http://schemas.microsoft.com/office/onenote/2013/onenote";

    public static SectionResolutionPlan Plan(
        string hierarchyXml,
        string notebookName,
        IReadOnlyList<string> sectionGroups,
        string sectionName,
        bool createMissing)
    {
        var doc = XDocument.Parse(hierarchyXml);

        var notebook = doc.Descendants(OneNs + "Notebook")
            .FirstOrDefault(n => NameEquals(n, notebookName))
            ?? throw new NotebookNotFoundException(notebookName);

        var cursor = notebook;
        var cursorPath = notebook.Attribute("path")?.Value ?? "";
        var creations = new List<CreationStep>();
        var sawMissing = false;

        foreach (var sgName in sectionGroups)
        {
            if (sawMissing)
            {
                cursorPath = Path.Combine(cursorPath, sgName);
                creations.Add(new CreationStep(
                    CreationKind.SectionGroup, sgName, cursorPath));
                continue;
            }

            var child = cursor.Elements(OneNs + "SectionGroup")
                .FirstOrDefault(sg => NameEquals(sg, sgName));

            if (child is null)
            {
                if (!createMissing)
                    return Unresolved();

                sawMissing = true;
                cursorPath = Path.Combine(cursorPath, sgName);
                creations.Add(new CreationStep(
                    CreationKind.SectionGroup, sgName, cursorPath));
            }
            else
            {
                cursor = child;
                cursorPath = child.Attribute("path")?.Value ?? Path.Combine(cursorPath, sgName);
            }
        }

        if (!sawMissing)
        {
            var existing = cursor.Elements(OneNs + "Section")
                .FirstOrDefault(s => NameEquals(s, sectionName));

            if (existing is not null)
            {
                return new SectionResolutionPlan(
                    ExistingSectionId: existing.Attribute("ID")?.Value ?? "",
                    DeepestExistingAncestorId: "",
                    CreationSteps: Array.Empty<CreationStep>());
            }
        }

        if (!createMissing) return Unresolved();

        var sectionPath = Path.Combine(cursorPath, sectionName + ".one");
        creations.Add(new CreationStep(
            CreationKind.Section, sectionName, sectionPath));

        return new SectionResolutionPlan(
            ExistingSectionId: null,
            DeepestExistingAncestorId: DeepestExistingAncestorId(notebook, cursor, sawMissing),
            CreationSteps: creations);

        static SectionResolutionPlan Unresolved() =>
            new(null, "", Array.Empty<CreationStep>());
    }

    private static string DeepestExistingAncestorId(
        XElement notebook, XElement cursor, bool sawMissing)
    {
        // When we hit a missing link mid-walk, cursor stayed at the last matched
        // ancestor (notebook or last-found SectionGroup). Otherwise cursor is the
        // deepest SectionGroup (or notebook if no section groups).
        return cursor.Attribute("ID")?.Value ?? notebook.Attribute("ID")?.Value ?? "";
    }

    private static bool NameEquals(XElement element, string name) =>
        string.Equals(
            element.Attribute("name")?.Value, name,
            StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 4: Run and see it pass**

```bash
dotnet test --filter "FullyQualifiedName~Plan_ExistingSection"
```

Expected: pass.

- [ ] **Step 5: Commit**

```bash
git add OneNoteMarkdownExporter/Services/SectionHierarchyWalker.cs OneNoteMarkdownExporter.Tests/Services/SectionHierarchyWalkerTests.cs
git commit -m "feat: SectionHierarchyWalker resolves existing section path"
```

---

## Task 13: Walker — missing-leaf-section case

**Files:**
- Modify: `OneNoteMarkdownExporter.Tests/Services/SectionHierarchyWalkerTests.cs`
- Implementation already handles this; this task tightens coverage.

- [ ] **Step 1: Add the test**

```csharp
    [Fact]
    public void Plan_MissingLeafSection_CreateMissing_AddsOneCreationStep()
    {
        var xml = LoadFixture("missing-leaf-section.xml");

        var plan = SectionHierarchyWalker.Plan(
            xml,
            notebookName: "Work Notes",
            sectionGroups: new[] { "Backend", "API" },
            sectionName: "auth-spec",
            createMissing: true);

        plan.ExistingSectionId.Should().BeNull();
        plan.DeepestExistingAncestorId.Should().Be("{SG-A}{1}{B0}");
        plan.CreationSteps.Should().HaveCount(1);
        plan.CreationSteps[0].Kind.Should().Be(CreationKind.Section);
        plan.CreationSteps[0].Name.Should().Be("auth-spec");
        plan.CreationSteps[0].TargetPath.Should().EndWith("API\\auth-spec.one");
    }
```

- [ ] **Step 2: Run and confirm it passes (implementation already handles this)**

```bash
dotnet test --filter "FullyQualifiedName~Plan_MissingLeafSection"
```

Expected: pass.

- [ ] **Step 3: Commit**

```bash
git add OneNoteMarkdownExporter.Tests/Services/SectionHierarchyWalkerTests.cs
git commit -m "test: walker resolves missing leaf section"
```

---

## Task 14: Walker — missing-intermediate section group case

**Files:**
- Modify: `OneNoteMarkdownExporter.Tests/Services/SectionHierarchyWalkerTests.cs`

- [ ] **Step 1: Add the test**

```csharp
    [Fact]
    public void Plan_MissingIntermediateSectionGroup_CreateMissing_AddsTwoCreationSteps()
    {
        var xml = LoadFixture("missing-intermediate.xml");

        var plan = SectionHierarchyWalker.Plan(
            xml,
            notebookName: "Work Notes",
            sectionGroups: new[] { "Backend", "API" },
            sectionName: "auth-spec",
            createMissing: true);

        plan.ExistingSectionId.Should().BeNull();
        plan.DeepestExistingAncestorId.Should().Be("{SG-B}{1}{B0}");
        plan.CreationSteps.Should().HaveCount(2);
        plan.CreationSteps[0].Kind.Should().Be(CreationKind.SectionGroup);
        plan.CreationSteps[0].Name.Should().Be("API");
        plan.CreationSteps[1].Kind.Should().Be(CreationKind.Section);
        plan.CreationSteps[1].Name.Should().Be("auth-spec");
    }
```

- [ ] **Step 2: Run and confirm pass**

```bash
dotnet test --filter "FullyQualifiedName~Plan_MissingIntermediateSectionGroup"
```

- [ ] **Step 3: Commit**

```bash
git add OneNoteMarkdownExporter.Tests/Services/SectionHierarchyWalkerTests.cs
git commit -m "test: walker resolves missing intermediate section group"
```

---

## Task 15: Walker — all-intermediates-missing case

**Files:**
- Modify: `OneNoteMarkdownExporter.Tests/Services/SectionHierarchyWalkerTests.cs`

- [ ] **Step 1: Add the test**

```csharp
    [Fact]
    public void Plan_MissingAllIntermediates_CreateMissing_AddsThreeCreationSteps()
    {
        var xml = LoadFixture("missing-all-intermediates.xml");

        var plan = SectionHierarchyWalker.Plan(
            xml,
            notebookName: "Work Notes",
            sectionGroups: new[] { "Backend", "API" },
            sectionName: "auth-spec",
            createMissing: true);

        plan.ExistingSectionId.Should().BeNull();
        plan.DeepestExistingAncestorId.Should().Be("{NB}{1}{B0}");
        plan.CreationSteps.Should().HaveCount(3);
        plan.CreationSteps[0].Kind.Should().Be(CreationKind.SectionGroup);
        plan.CreationSteps[0].Name.Should().Be("Backend");
        plan.CreationSteps[1].Kind.Should().Be(CreationKind.SectionGroup);
        plan.CreationSteps[1].Name.Should().Be("API");
        plan.CreationSteps[2].Kind.Should().Be(CreationKind.Section);
        plan.CreationSteps[2].Name.Should().Be("auth-spec");
    }
```

- [ ] **Step 2: Run and confirm pass**

```bash
dotnet test --filter "FullyQualifiedName~Plan_MissingAllIntermediates"
```

- [ ] **Step 3: Commit**

```bash
git add OneNoteMarkdownExporter.Tests/Services/SectionHierarchyWalkerTests.cs
git commit -m "test: walker resolves missing full chain from notebook"
```

---

## Task 16: Walker — missing-notebook throws

**Files:**
- Modify: `OneNoteMarkdownExporter.Tests/Services/SectionHierarchyWalkerTests.cs`

- [ ] **Step 1: Add the test**

```csharp
    [Fact]
    public void Plan_MissingNotebook_ThrowsNotebookNotFoundException()
    {
        var xml = LoadFixture("missing-notebook.xml");

        var act = () => SectionHierarchyWalker.Plan(
            xml,
            notebookName: "Work Notes",
            sectionGroups: new[] { "Backend" },
            sectionName: "auth-spec",
            createMissing: true);

        act.Should()
            .Throw<NotebookNotFoundException>()
            .Which.NotebookName.Should().Be("Work Notes");
    }
```

- [ ] **Step 2: Run and confirm pass**

```bash
dotnet test --filter "FullyQualifiedName~Plan_MissingNotebook"
```

- [ ] **Step 3: Commit**

```bash
git add OneNoteMarkdownExporter.Tests/Services/SectionHierarchyWalkerTests.cs
git commit -m "test: walker throws on missing notebook"
```

---

## Task 17: Walker — `createMissing: false` returns unresolved

**Files:**
- Modify: `OneNoteMarkdownExporter.Tests/Services/SectionHierarchyWalkerTests.cs`

- [ ] **Step 1: Add the test**

```csharp
    [Fact]
    public void Plan_MissingLeafSection_CreateMissingFalse_ReturnsUnresolved()
    {
        var xml = LoadFixture("missing-leaf-section.xml");

        var plan = SectionHierarchyWalker.Plan(
            xml,
            notebookName: "Work Notes",
            sectionGroups: new[] { "Backend", "API" },
            sectionName: "auth-spec",
            createMissing: false);

        plan.IsUnresolved.Should().BeTrue();
        plan.ExistingSectionId.Should().BeNull();
        plan.CreationSteps.Should().BeEmpty();
    }
```

- [ ] **Step 2: Run and confirm pass**

```bash
dotnet test --filter "FullyQualifiedName~Plan_MissingLeafSection_CreateMissingFalse"
```

- [ ] **Step 3: Commit**

```bash
git add OneNoteMarkdownExporter.Tests/Services/SectionHierarchyWalkerTests.cs
git commit -m "test: walker returns unresolved when createMissing is false"
```

---

## Task 18: Add `OneNoteService.EnsureSectionIdByPath`

**Files:**
- Modify: `OneNoteMarkdownExporter/Services/OneNoteService.cs` (add new method)

No dedicated unit test — the method is a thin COM-executing wrapper; the pure walker has full coverage (Tasks 12-17), and integration validation happens via manual smoke test in Task 30.

- [ ] **Step 1: Add the public method to `OneNoteService`**

Append inside the `OneNoteService` class (after `FindSectionIdByPath`):

```csharp
    /// <summary>
    /// Resolves a section by explicit notebook → [section groups…] → section
    /// path, creating any missing section groups and the leaf section when
    /// <paramref name="createMissing"/> is true.
    /// </summary>
    /// <param name="dryRun">When true, reports "would create …" via
    /// <paramref name="progress"/> but does not call OpenHierarchy.
    /// Returns the resolved section ID if the section already exists, or
    /// null if it would be created.</param>
    /// <exception cref="NotebookNotFoundException">The named notebook does
    /// not exist. Notebook-level auto-create is tracked by issue #19.</exception>
    public string? EnsureSectionIdByPath(
        string notebookName,
        IReadOnlyList<string> sectionGroups,
        string sectionName,
        bool createMissing,
        bool dryRun,
        IProgress<string>? progress = null)
    {
        _oneNoteApp.GetHierarchy(null, HierarchyScope.hsSections, out string xml);

        var plan = SectionHierarchyWalker.Plan(
            xml, notebookName, sectionGroups, sectionName, createMissing);

        if (plan.ExistingSectionId is { } existing)
        {
            return existing;
        }

        if (plan.IsUnresolved)
        {
            // createMissing=false and the section isn't there — preserve
            // legacy null-return so callers see the same miss they see today.
            return null;
        }

        var parentId = plan.DeepestExistingAncestorId;
        string? leafSectionId = null;

        foreach (var step in plan.CreationSteps)
        {
            var verb = dryRun ? "would create" : "Created";
            var kindLabel = step.Kind == CreationKind.SectionGroup
                ? "section group"
                : "section";
            progress?.Report($"  {verb} {kindLabel}: {step.Name}");

            if (dryRun) continue;

            var fileType = step.Kind == CreationKind.SectionGroup
                ? CreateFileType.cftFolder
                : CreateFileType.cftSection;

            _oneNoteApp.OpenHierarchy(
                step.TargetPath, parentId, out string newId, fileType);

            parentId = newId;
            if (step.Kind == CreationKind.Section)
            {
                leafSectionId = newId;
            }
        }

        return leafSectionId;
    }
```

- [ ] **Step 2: Ensure the `IProgress<string>` using is in place**

Top of `OneNoteService.cs` should already have `using System;` — that covers `IProgress<T>`. Confirm the file builds.

```bash
dotnet build OneNoteMarkdownExporter
```

Expected: build succeeds.

- [ ] **Step 3: Run the whole suite**

```bash
dotnet test
```

Expected: all passing.

- [ ] **Step 4: Commit**

```bash
git add OneNoteMarkdownExporter/Services/OneNoteService.cs
git commit -m "feat: OneNoteService.EnsureSectionIdByPath executes walker plan"
```

---

## Task 19: Add `CreateMissing` to `ImportOptions`

**Files:**
- Modify: `OneNoteMarkdownExporter/Models/ImportOptions.cs`
- Modify: `OneNoteMarkdownExporter.Tests/Services/ImportServiceTests.cs` (`ImportOptionsTests` class, around lines 12-34)

- [ ] **Step 1: Add the failing test**

In `OneNoteMarkdownExporter.Tests/Services/ImportServiceTests.cs`, add inside `ImportOptionsTests`:

```csharp
    [Fact]
    public void CreateMissing_DefaultsToFalse()
    {
        var options = new ImportOptions();
        options.CreateMissing.Should().BeFalse();
    }
```

- [ ] **Step 2: Run and see it fail**

```bash
dotnet test --filter "FullyQualifiedName~CreateMissing_DefaultsToFalse"
```

Expected: compile error — `CreateMissing` does not exist.

- [ ] **Step 3: Add the property**

In `OneNoteMarkdownExporter/Models/ImportOptions.cs`, add:

```csharp
    /// <summary>
    /// When true and the target section does not exist, create missing
    /// section groups and the leaf section before importing. Default false —
    /// <c>--import</c> is surgical; a missing target is usually a typo.
    /// </summary>
    public bool CreateMissing { get; set; } = false;
```

- [ ] **Step 4: Run and see it pass**

```bash
dotnet test --filter "FullyQualifiedName~CreateMissing_DefaultsToFalse"
```

- [ ] **Step 5: Commit**

```bash
git add OneNoteMarkdownExporter/Models/ImportOptions.cs OneNoteMarkdownExporter.Tests/Services/ImportServiceTests.cs
git commit -m "feat: add CreateMissing flag to ImportOptions"
```

---

## Task 20: Rewire `ImportService` to use `EnsureSectionIdByPath`

**Files:**
- Modify: `OneNoteMarkdownExporter/Services/ImportService.cs:26-38`

This replaces the dry-run guard + `FindSectionId` call with a single `EnsureSectionIdByPath` call that is dry-run aware.

- [ ] **Step 1: Rewrite the section-resolution block**

**Important semantic note:** the legacy
`OneNoteService.FindSectionId(notebook, section)` (lines 217-235)
walks the notebook **recursively** — it finds a named section anywhere
in the tree, including nested inside section groups. That preserves the
`--import "NB/Section" --file x.md` UX where users don't specify a
section-group path. We keep that recursive lookup on the "no create"
path. `EnsureSectionIdByPath` (which treats the section name as a
direct child of the last ancestor) is only used when `--create-missing`
is set, where "direct child of notebook" is the only defined creation
target.

In `ImportService.cs`, lines 26-38, change:

```csharp
            string? sectionId = null;
            if (!options.DryRun)
            {
                sectionId = _oneNoteService.FindSectionId(options.NotebookName, options.SectionName);
                if (sectionId == null)
                {
                    var error = $"Section not found: {options.NotebookName}/{options.SectionName}";
                    result.Errors.Add(error);
                    result.FailedPages = result.TotalFiles;
                    progress?.Report($"Error: {error}");
                    return result;
                }
            }
```

to:

```csharp
            // First: recursive lookup (legacy behavior — find the section
            // anywhere under the notebook, regardless of section-group depth).
            string? sectionId = options.DryRun
                ? null
                : _oneNoteService.FindSectionId(options.NotebookName, options.SectionName);

            // Second: if not found and --create-missing, fall through to the
            // path-based ensure (creates the section as a direct child of the
            // notebook).
            if (sectionId is null && options.CreateMissing)
            {
                try
                {
                    sectionId = _oneNoteService.EnsureSectionIdByPath(
                        options.NotebookName,
                        sectionGroups: Array.Empty<string>(),
                        options.SectionName,
                        createMissing: true,
                        dryRun: options.DryRun,
                        progress: progress);
                }
                catch (NotebookNotFoundException ex)
                {
                    result.Errors.Add(ex.Message);
                    result.FailedPages = result.TotalFiles;
                    progress?.Report($"Error: {ex.Message}");
                    return result;
                }
            }

            // Finally: error on miss (unless dry-run, where we still preview).
            if (sectionId is null && !options.DryRun)
            {
                var error = $"Section not found: {options.NotebookName}/{options.SectionName}. " +
                            "Pass --create-missing to create it automatically.";
                result.Errors.Add(error);
                result.FailedPages = result.TotalFiles;
                progress?.Report($"Error: {error}");
                return result;
            }
```

**Dry-run nuance:** in dry-run mode we skip the initial `FindSectionId`
call (no COM access needed for preview) and rely on the
`EnsureSectionIdByPath` path when `--create-missing` is set. Without
`--create-missing`, dry-run doesn't surface a "section not found"
error today either (the legacy code only ran `FindSectionId` in
non-dry-run mode).

- [ ] **Step 2: Add `using System;` at the top of the file if not present**

Verify line 1 is `using System;` — `Array.Empty<string>()` needs it.

- [ ] **Step 3: Build**

```bash
dotnet build
```

Expected: build succeeds.

- [ ] **Step 4: Run the suite**

```bash
dotnet test
```

Expected: all passing.

- [ ] **Step 5: Commit**

```bash
git add OneNoteMarkdownExporter/Services/ImportService.cs
git commit -m "feat: ImportService uses EnsureSectionIdByPath"
```

---

## Task 21: Extend `IOneNotePublisher.PublishAsync` with `createMissing` and `dryRun`

**Files:**
- Modify: `OneNoteMarkdownExporter/Services/PublishTreeService.cs:16-26` (the inline interface)
- Modify: `OneNoteMarkdownExporter/Services/OneNoteTreePublisher.cs` (implementation, lines 24-49)

- [ ] **Step 1: Extend the interface**

In `PublishTreeService.cs` lines 16-26, change:

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
        bool createMissing,
        bool dryRun,
        IProgress<string>? progress = null);
}
```

- [ ] **Step 2: Update `OneNoteTreePublisher` to match**

Rewrite `OneNoteTreePublisher.cs` lines 24-49:

```csharp
    public Task PublishAsync(
        string notebook,
        IReadOnlyList<string> sectionGroups,
        string section,
        string pageTitle,
        string markdownContent,
        string sourceFileFullPath,
        bool collapsible,
        bool createMissing,
        bool dryRun,
        IProgress<string>? progress = null)
    {
        return Task.Run(() =>
        {
            var sectionId = _oneNoteService.EnsureSectionIdByPath(
                notebook, sectionGroups, section,
                createMissing: createMissing,
                dryRun: dryRun,
                progress: progress);

            if (dryRun) return;

            if (sectionId is null)
            {
                throw new InvalidOperationException(
                    $"Section not found: {notebook}/{string.Join('/', sectionGroups)}/{section}. "
                        .Replace("//", "/") +
                    "Pass --create-missing to create it automatically.");
            }

            var pageXml = _converter.Convert(
                markdownContent,
                pageTitle: pageTitle,
                collapsible: collapsible,
                basePath: Path.GetDirectoryName(sourceFileFullPath));

            var pageId = _oneNoteService.CreatePage(sectionId);
            var xmlWithId = pageXml.Replace("<one:Page ", $"<one:Page ID=\"{pageId}\" ");
            _oneNoteService.UpdatePageContent(xmlWithId);
        });
    }
```

- [ ] **Step 3: Update `FakeOneNotePublisher`**

`FakeOneNotePublisher` lives inside `PublishTreeServiceTests.cs` at
line 106-124. Replace the class (keep it as a nested private class):

```csharp
    private class FakeOneNotePublisher : IOneNotePublisher
    {
        public List<(string Notebook, IReadOnlyList<string> SGs, string Section, string PageTitle, bool CreateMissing, bool DryRun)> CreatedPages { get; } = new();
        public bool FailNextCall { get; set; }

        public Task PublishAsync(
            string notebook,
            IReadOnlyList<string> sectionGroups,
            string section,
            string pageTitle,
            string markdownContent,
            string sourceFileFullPath,
            bool collapsible,
            bool createMissing,
            bool dryRun,
            IProgress<string>? progress = null)
        {
            if (FailNextCall)
            {
                FailNextCall = false;
                throw new InvalidOperationException("fake failure");
            }
            CreatedPages.Add((notebook, sectionGroups, section, pageTitle, createMissing, dryRun));
            return Task.CompletedTask;
        }
    }
```

If the existing fake's `PublishAsync` had a different body than the
FailNextCall + Add pattern shown above, preserve that logic — just
extend the tuple and signature.

- [ ] **Step 4: Build to verify the fake compiles**

```bash
dotnet build OneNoteMarkdownExporter.Tests
```

Expected: build succeeds.

- [ ] **Step 5: Commit the interface + implementations**

```bash
git add OneNoteMarkdownExporter/Services/PublishTreeService.cs OneNoteMarkdownExporter/Services/OneNoteTreePublisher.cs OneNoteMarkdownExporter.Tests/Services
git commit -m "refactor: IOneNotePublisher takes createMissing and dryRun"
```

---

## Task 22: Add `CreateMissing` to `PublishTreeOptions`

**Files:**
- Modify: `OneNoteMarkdownExporter/Services/PublishTreeOptions.cs`
- Modify: `OneNoteMarkdownExporter.Tests/Services/PublishTreeServiceTests.cs` (or the matching options test file)

- [ ] **Step 1: Write the failing test**

Add inside `PublishTreeServiceTests` or create a separate `PublishTreeOptionsTests` class in the same file:

```csharp
public class PublishTreeOptionsTests
{
    [Fact]
    public void CreateMissing_DefaultsToTrue()
    {
        var options = new PublishTreeOptions();
        options.CreateMissing.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run and see it fail**

```bash
dotnet test --filter "FullyQualifiedName~PublishTreeOptionsTests.CreateMissing"
```

Expected: compile error.

- [ ] **Step 3: Add the property**

In `PublishTreeOptions.cs`:

```csharp
    /// <summary>
    /// When true, auto-create missing section groups and the leaf section
    /// before publishing each page. Default true — <c>--publish</c> is bulk
    /// and expects the tree to "just work" without manual pre-creation.
    /// </summary>
    public bool CreateMissing { get; set; } = true;
```

- [ ] **Step 4: Run and see pass**

```bash
dotnet test --filter "FullyQualifiedName~PublishTreeOptionsTests"
```

- [ ] **Step 5: Commit**

```bash
git add OneNoteMarkdownExporter/Services/PublishTreeOptions.cs OneNoteMarkdownExporter.Tests/Services/PublishTreeServiceTests.cs
git commit -m "feat: add CreateMissing flag to PublishTreeOptions (default true)"
```

---

## Task 23: Rewire `PublishTreeService` — pass flags, remove dry-run short-circuit

**Files:**
- Modify: `OneNoteMarkdownExporter/Services/PublishTreeService.cs:144-179`

- [ ] **Step 1: Rewrite pass 3 of `PublishAsync`**

Locate the `Pass 3 — publish (or dry-run)` block at `PublishTreeService.cs:143-179`. Replace it with:

```csharp
        // Pass 3 — publish (dry-run goes through the publisher too, so the
        // hierarchy walk and "would create …" progress still happen).
        foreach (var entry in publishable)
        {
            if (entry.PendingDiagnostic?.Severity == DiagnosticSeverity.Warning)
            {
                report.RecordWarning(entry.PendingDiagnostic);
            }

            if (options.DryRun)
            {
                progress?.Report($"  [dry-run] {entry.FileRel} → {TargetKey(entry.Target)}  (title: {entry.Target.PageTitle})");
            }

            try
            {
                await _publisher.PublishAsync(
                    entry.Target.Notebook,
                    entry.Target.SectionGroups,
                    entry.Target.Section,
                    entry.Target.PageTitle,
                    entry.Markdown,
                    entry.FullPath,
                    options.Collapsible,
                    createMissing: options.CreateMissing,
                    dryRun: options.DryRun,
                    progress: progress);
                report.RecordPublished(entry.FileRel);
            }
            catch (Exception ex)
            {
                report.RecordError(new PublishDiagnostic
                {
                    FileRelativePath = entry.FileRel,
                    Severity = DiagnosticSeverity.Error,
                    Message = $"{entry.FileRel}: publish failed — {ex.Message}",
                });
            }
        }
```

- [ ] **Step 2: Build**

```bash
dotnet build
```

Expected: build succeeds. If `progress` is not in scope on `PublishAsync`, inspect the `PublishTreeService.PublishAsync` method signature (around `PublishTreeService.cs:56`) and confirm it accepts `IProgress<string>? progress`. If not, add that parameter — the existing CLI call site already passes a progress sink in `CliHandler.ExecutePublishTreeAsync`.

- [ ] **Step 3: Run tests**

```bash
dotnet test
```

Expected: the existing `PublishAsync_DryRun_DoesNotCallPublisher` test (`PublishTreeServiceTests.cs:40`) **will fail** — dry-run now *does* call the publisher. This is intentional behavior change.

- [ ] **Step 4: Update the failing dry-run test**

In `PublishTreeServiceTests.cs`, locate
`PublishAsync_DryRun_DoesNotCallPublisher` (line 40-54) and replace
with:

```csharp
    [Fact]
    public async Task PublishAsync_DryRun_CallsPublisherWithDryRunFlag()
    {
        Write("a.md", "---\nonenote:\n  notebook: NB\n  section: S\n---\nBody.");
        var publisher = new FakeOneNotePublisher();
        var service = NewService(publisher);

        var report = await service.PublishAsync(new PublishTreeOptions
        {
            SourceRoot = _root,
            DryRun = true,
        });

        publisher.CreatedPages.Should().HaveCount(1);
        publisher.CreatedPages[0].DryRun.Should().BeTrue();
        publisher.CreatedPages[0].CreateMissing.Should().BeTrue();
        report.Published.Should().Be(1);
    }
```

**Rationale for the rename:** the old name ("DoesNotCallPublisher")
encoded behavior that's no longer true — dry-run now *does* call the
publisher so the publisher can walk the hierarchy and preview
creations. Only the actual `CreatePage`/`UpdatePageContent` are
skipped, which is the publisher's responsibility (not the tree
service's).

- [ ] **Step 5: Run and confirm pass**

```bash
dotnet test --filter "FullyQualifiedName~PublishTreeServiceTests"
```

- [ ] **Step 6: Commit**

```bash
git add OneNoteMarkdownExporter/Services/PublishTreeService.cs OneNoteMarkdownExporter.Tests/Services/PublishTreeServiceTests.cs
git commit -m "feat: PublishTreeService routes dry-run through publisher for walk preview"
```

---

## Task 24: Wire `--create-missing` / `--no-create-missing` into `CliHandler`

**Files:**
- Modify: `OneNoteMarkdownExporter/Services/CliHandler.cs`
- Modify: `OneNoteMarkdownExporter.Tests/Cli/CliHandlerTests.cs`

- [ ] **Step 1: Register the options in `BuildRootCommand`**

In `CliHandler.cs` `BuildRootCommand`, after the `publishOption` (around line 134), add:

```csharp
            var createMissingOption = new Option<bool>(
                "--create-missing",
                "Auto-create missing target sections and section groups.");

            var noCreateMissingOption = new Option<bool>(
                "--no-create-missing",
                "Disable auto-create of missing sections/section groups.");
```

Register them:

```csharp
            rootCommand.AddOption(createMissingOption);
            rootCommand.AddOption(noCreateMissingOption);
```

Also extend the `cliFlags` array in `ShouldRunCli` (line 39-46):

```csharp
                "--import", "--file", "--no-collapse", "--publish",
                "--create-missing", "--no-create-missing",
                "--help", "-h", "-?", "--version"
```

- [ ] **Step 2: Resolve the flag in the `SetHandler` delegate**

Inside `SetHandler` at line 155, add a shared helper:

```csharp
                bool ResolveCreateMissing(bool subcommandDefault)
                {
                    var on = result.GetValueForOption(createMissingOption);
                    var off = result.GetValueForOption(noCreateMissingOption);
                    if (on && off)
                    {
                        Console.Error.WriteLine("Error: --create-missing and --no-create-missing are mutually exclusive.");
                        Environment.Exit(2);
                    }
                    if (on) return true;
                    if (off) return false;
                    return subcommandDefault;
                }
```

- [ ] **Step 3: Pass the resolved flag into `ExecutePublishTreeAsync`**

Update the `publishSource` branch (around line 160-174) to call `ResolveCreateMissing(subcommandDefault: true)` and add the parameter to `ExecutePublishTreeAsync`. Modify that method's signature + body to plumb `createMissing` into the `PublishTreeOptions` it constructs.

Search `ExecutePublishTreeAsync` in the same file. Locate where `new PublishTreeOptions { … }` is built and set:

```csharp
            var options = new PublishTreeOptions
            {
                // existing fields…
                CreateMissing = createMissing,
            };
```

- [ ] **Step 4: Pass the resolved flag into `ExecuteImportAsync`**

Same as Step 3 but with `subcommandDefault: false` for `--import`. Plumb into `ImportOptions.CreateMissing`.

- [ ] **Step 5: Add `ShouldRunCli` tests for the new flags**

`CliHandlerTests.cs` currently only exercises `ShouldRunCli` — there
is no established pipeline for parse-and-inspect-options tests.
Scope the new tests to match: verify the new flags activate CLI mode.

Append inside `CliHandlerTests`:

```csharp
    [Fact]
    public void ShouldRunCli_WithCreateMissingFlag_ReturnsTrue()
    {
        var args = new[] { "--publish", "./notes", "--create-missing" };
        CliHandler.ShouldRunCli(args).Should().BeTrue();
    }

    [Fact]
    public void ShouldRunCli_WithNoCreateMissingFlag_ReturnsTrue()
    {
        var args = new[] { "--publish", "./notes", "--no-create-missing" };
        CliHandler.ShouldRunCli(args).Should().BeTrue();
    }
```

**Flag-resolution coverage** (default-on for `--publish`, default-off
for `--import`, mutual exclusion) is exercised by the manual smoke
tests in PR 2's test plan rather than by automated unit tests —
extracting a testable seam for `SetHandler`'s delegate is a non-trivial
refactor that belongs in a dedicated "CLI parse tests" PR.

If that refactor feels warranted during implementation, extract the
`ResolveCreateMissing` helper into a `static internal` method on
`CliHandler` (exposed to tests via `InternalsVisibleTo`) and add:

```csharp
    [Theory]
    [InlineData(false, false, true,  true)]   // no flag, publish default
    [InlineData(true,  false, true,  true)]   // --create-missing wins
    [InlineData(false, true,  true,  false)]  // --no-create-missing wins
    [InlineData(false, false, false, false)]  // no flag, import default
    [InlineData(true,  false, false, true)]   // --create-missing on import
    public void ResolveCreateMissing_Resolves(bool on, bool off, bool defaultOn, bool expected)
    {
        CliHandler.ResolveCreateMissing(on, off, defaultOn).Should().Be(expected);
    }
```

Treat the refactor + Theory as optional. Ship with the two
`ShouldRunCli` tests and the manual smoke plan if time is tight.

- [ ] **Step 6: Run the suite**

```bash
dotnet test
```

Expected: all passing.

- [ ] **Step 7: Commit**

```bash
git add OneNoteMarkdownExporter/Services/CliHandler.cs OneNoteMarkdownExporter.Tests/Cli/CliHandlerTests.cs
git commit -m "feat(cli): --create-missing / --no-create-missing on import and publish"
```

---

## Task 25: Update `docs/importer.md`

**Files:**
- Modify: `docs/importer.md`

- [ ] **Step 1: Add `--create-missing` row to the `--import` flag table**

Find the flag table for `--import` (around lines 32-40). Insert a new row before `--dry-run`:

```markdown
| `--create-missing` | With `--import`: create the target section (and any section groups between it and the notebook) if it doesn't exist. Default: off. |
```

- [ ] **Step 2: Update the "Tree publish" section**

Find the "Tree publish (folder-tree → OneNote)" section (around lines 110-150). Add a subsection after the CLI examples and before the resolution rule:

```markdown
### Auto-create missing sections

By default, `--publish` auto-creates any section group or section in a
resolved target path that doesn't yet exist in OneNote — the tree walks
and the importer does `mkdir -p` on the OneNote side. To require the
full target path to exist (and error if anything is missing), pass
`--no-create-missing`.

`--import` is the opposite — it errors on a missing section unless you
explicitly opt in with `--create-missing`.

Creating a missing **notebook** is not yet supported; the publisher
errors with a link to [issue #19](https://github.com/LarryWisherMan/one-note-to-markdown/issues/19).
Create the notebook manually in OneNote and retry.
```

- [ ] **Step 3: Commit**

```bash
git add docs/importer.md
git commit -m "docs: document --create-missing / --no-create-missing"
```

---

## Task 26: Update `CHANGELOG.md`

**Files:**
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Add entries to `[Unreleased]`**

Under the `[Unreleased]` heading, ensure there's an `### Added` subsection and add:

```markdown
- `--publish` auto-creates missing section groups and sections by
  default; opt out with `--no-create-missing`. Notebook-level
  auto-create is not yet supported (tracked by #19).
- `--import --create-missing` creates a missing target section (and
  any section groups between it and the notebook) before importing
  (opt-in).
```

- [ ] **Step 2: Commit**

```bash
git add CHANGELOG.md
git commit -m "docs: CHANGELOG entry for --create-missing flags"
```

---

## Task 27: Full test run

**Files:**
- None (verification).

- [ ] **Step 1: Run everything**

```bash
dotnet test
```

Expected: all passing, zero skipped, zero errors.

- [ ] **Step 2: If anything is red, stop and fix before proceeding**

Do not move to Task 28 until the full suite is green.

---

## Task 28: Push branch and open PR 2

**Files:**
- None (git / gh).

- [ ] **Step 1: Push**

```bash
git push -u origin feat/publish-auto-create-missing
```

- [ ] **Step 2: Open the PR**

```bash
gh pr create --title "feat(publish): auto-create missing sections and section groups" --body "$(cat <<'EOF'
## Summary

- `--publish` auto-creates missing section groups and sections by default
  (opt-out via `--no-create-missing`).
- `--import` gets `--create-missing` (opt-in).
- Notebook-level auto-create is **not** in this PR (tracked by #19).
- Pure `SectionHierarchyWalker` produces a `SectionResolutionPlan`; the
  COM-executing wrapper lives on `OneNoteService.EnsureSectionIdByPath`.
- Dry-run now funnels through the publisher so `would create …` lines
  appear in preview.

Closes #17. Design: `docs/superpowers/specs/2026-04-16-publish-target-robustness-design.md`.

## Test plan

- [x] `SectionHierarchyWalkerTests` — 5 fixture-driven unit tests covering
  existing-section, missing-leaf, missing-intermediate, missing-all,
  missing-notebook, and `createMissing=false` paths.
- [x] `NotebookNotFoundExceptionTests`
- [x] `ImportOptionsTests.CreateMissing_DefaultsToFalse`
- [x] `PublishTreeOptionsTests.CreateMissing_DefaultsToTrue`
- [x] `PublishTreeServiceTests` — dry-run routes through publisher
- [x] `CliHandlerTests` — flag parsing and mutual exclusion on both
  subcommands
- [x] Full `dotnet test` green
- [ ] Manual smoke 1: `--publish ./notes --dry-run --verbose` against a
  real OneNote; confirm `would create …` lines for missing intermediates.
- [ ] Manual smoke 2: `--publish ./notes` against a throwaway notebook;
  confirm sections actually get created.
- [ ] Manual smoke 3: `--publish ./notes --no-create-missing` on a tree
  with a missing section; confirm it errors with a clear message.
- [ ] Manual smoke 4: `--import "MissingNotebook/Section" --file x.md`;
  confirm `NotebookNotFoundException` message mentions issue #19.
EOF
)"
```

- [ ] **Step 3: Return the PR URL**

End of PR 2.

---

# Post-merge cleanup

After both PRs land on master:

- [ ] Delete both local branches: `git branch -D feat/importer-spellcheck-suppression feat/publish-auto-create-missing`.
- [ ] Delete remote branches if GitHub didn't auto-delete.
- [ ] `gh issue close 16` and `gh issue close 17` if the PR's "Closes #X" trailer didn't auto-close them.

---

# Notes for the executing engineer

- If you encounter `COMException 0x8001010A` (RPC_E_SERVERCALL_RETRYLATER) during manual smoke tests for PR 2, that's OneNote being busy — `OpenHierarchy` calls should be wrapped similarly to the retry loop at `ImportService.cs:110-127`. Consider adding `RetryComCall` around the `OpenHierarchy` calls in `OneNoteService.EnsureSectionIdByPath` if the smoke test hits it; leave the retry out unless actually observed.
- If `SectionHierarchyWalker.Plan` returns `IsUnresolved = true` inside `OneNoteTreePublisher.PublishAsync` in dry-run mode, the publisher still returns without error — that matches the legacy "section not found" dry-run behavior of printing what would happen and moving on. Confirm this is the desired UX during manual smoke test 3; if a dry-run should error on unresolved sections when `--no-create-missing` is set, add that branch.
- `sequentialthinking` is useful for Task 21 when updating the `FakeOneNotePublisher` — the fake's current shape is unknown until you read the file; step through: (1) locate the fake, (2) read its current fields, (3) plan minimal additions, (4) implement.
