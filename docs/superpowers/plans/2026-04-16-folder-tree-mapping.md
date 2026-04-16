# Folder-Tree → OneNote Mapping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `--publish <source>` CLI command that walks a directory of Markdown files, resolves each to a OneNote target via front-matter + folder inference per the design spec, and creates pages in OneNote.

**Architecture:** New pipeline composed of four pure services — `FrontMatterParser` (YAML subset), `MarkdownTreeWalker` (filesystem scan), `OneNoteTargetResolver` (the resolution rule), and `PublishTreeReport` (outcome aggregation) — orchestrated by a new `PublishTreeService`. Existing `OneNoteService` gains `FindSectionIdByPath` for nested-section-group navigation. No changes to `MarkdownToOneNoteXmlConverter` or the existing `--import` path.

**Tech Stack:** C# / .NET 10.0 / YamlDotNet / Markdig (existing) / System.CommandLine (existing) / OneNote COM Interop / xUnit + FluentAssertions + Moq

**Spec:** `docs/superpowers/specs/2026-04-16-folder-tree-mapping-design.md`

**Conventions from the repo:**
- File-scoped namespaces (`.editorconfig`).
- 4-space indent for C#, `_camelCase` private fields, `using` outside namespace, System first.
- xUnit + FluentAssertions + Moq for tests (matches existing test project).
- Conventional Commits (`feat:`, `test:`, `chore:`, `docs:`). GitVersion bumps on `feat:` (minor) and `fix:` (patch).
- No `Co-Authored-By: Claude` trailer (suppressed by `.claude/settings.json`).
- `CHANGELOG.md` `[Unreleased]` section gets updated on substantive changes.

---

### Task 1: Add YamlDotNet + base model types

**Files:**
- Modify: `OneNoteMarkdownExporter/OneNoteMarkdownExporter.csproj:21-33`
- Create: `OneNoteMarkdownExporter/Models/FrontMatter.cs`
- Create: `OneNoteMarkdownExporter/Models/ResolvedTarget.cs`
- Create: `OneNoteMarkdownExporter/Models/PublishDiagnostic.cs`

- [ ] **Step 1: Add YamlDotNet package reference**

Edit `OneNoteMarkdownExporter/OneNoteMarkdownExporter.csproj`, add the line inside the first `<ItemGroup>`, between `Markdig` and `ReverseMarkdown`:

```xml
<PackageReference Include="YamlDotNet" Version="16.2.1" />
```

- [ ] **Step 2: Restore and verify build**

Run: `dotnet restore && dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Create `Models/FrontMatter.cs`**

```csharp
using System.Collections.Generic;

namespace OneNoteMarkdownExporter.Models;

/// <summary>
/// Parsed front-matter from a Markdown file. All fields are optional.
/// Represents only the subset needed for OneNote routing; the full
/// schema (issue #3) may extend this type later.
/// </summary>
public class FrontMatter
{
    /// <summary>Target-neutral page title. Falls back to first H1 then slug.</summary>
    public string? Title { get; set; }

    /// <summary>
    /// OneNote routing block. Null when the file has no <c>onenote:</c> key
    /// (publisher skips the file). Populated but empty when <c>onenote: true</c>
    /// or <c>onenote: {}</c>. <see cref="OptOut"/> is set when <c>onenote: false</c>.
    /// </summary>
    public OneNoteFrontMatter? OneNote { get; set; }

    /// <summary>True when front-matter contained the literal <c>onenote: false</c>.</summary>
    public bool OptOut { get; set; }
}

public class OneNoteFrontMatter
{
    public string? Notebook { get; set; }
    public string? Section { get; set; }
    public List<string>? SectionGroups { get; set; }
}
```

- [ ] **Step 4: Create `Models/ResolvedTarget.cs`**

```csharp
using System.Collections.Generic;

namespace OneNoteMarkdownExporter.Models;

/// <summary>
/// The fully resolved OneNote destination for a Markdown file.
/// Produced by <c>OneNoteTargetResolver</c>; consumed by the publisher.
/// </summary>
public class ResolvedTarget
{
    public string Notebook { get; set; } = string.Empty;
    public List<string> SectionGroups { get; set; } = new();
    public string Section { get; set; } = string.Empty;
    public string PageSlug { get; set; } = string.Empty;
    public string PageTitle { get; set; } = string.Empty;
}
```

- [ ] **Step 5: Create `Models/PublishDiagnostic.cs`**

```csharp
namespace OneNoteMarkdownExporter.Models;

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// A single diagnostic produced during tree publishing. Aggregated by
/// <c>PublishTreeReport</c> for the run summary.
/// </summary>
public class PublishDiagnostic
{
    public string FileRelativePath { get; set; } = string.Empty;
    public DiagnosticSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
}
```

- [ ] **Step 6: Commit**

```bash
git add OneNoteMarkdownExporter/OneNoteMarkdownExporter.csproj \
        OneNoteMarkdownExporter/Models/FrontMatter.cs \
        OneNoteMarkdownExporter/Models/ResolvedTarget.cs \
        OneNoteMarkdownExporter/Models/PublishDiagnostic.cs
git commit -m "chore: add YamlDotNet and front-matter / target model types"
```

---

### Task 2: FrontMatterParser (TDD)

**Files:**
- Create: `OneNoteMarkdownExporter/Services/FrontMatterParser.cs`
- Create: `OneNoteMarkdownExporter.Tests/Services/FrontMatterParserTests.cs`

- [ ] **Step 1: Write failing tests for the eight FM shapes**

Create `OneNoteMarkdownExporter.Tests/Services/FrontMatterParserTests.cs`:

```csharp
using FluentAssertions;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Services;

public class FrontMatterParserTests
{
    private readonly FrontMatterParser _parser = new();

    [Fact]
    public void Parse_NoFrontMatter_ReturnsEmptyFrontMatter()
    {
        var content = "# Just a heading\n\nBody.";
        var fm = _parser.Parse(content);

        fm.Title.Should().BeNull();
        fm.OneNote.Should().BeNull();
        fm.OptOut.Should().BeFalse();
    }

    [Fact]
    public void Parse_EmptyFrontMatter_ReturnsEmptyFrontMatter()
    {
        var content = "---\n---\nBody.";
        var fm = _parser.Parse(content);

        fm.Title.Should().BeNull();
        fm.OneNote.Should().BeNull();
    }

    [Fact]
    public void Parse_OneNoteTrue_MarksPublishableWithEmptyBlock()
    {
        var content = "---\nonenote: true\n---\nBody.";
        var fm = _parser.Parse(content);

        fm.OneNote.Should().NotBeNull();
        fm.OneNote!.Notebook.Should().BeNull();
        fm.OptOut.Should().BeFalse();
    }

    [Fact]
    public void Parse_OneNoteFalse_MarksOptOut()
    {
        var content = "---\nonenote: false\n---\nBody.";
        var fm = _parser.Parse(content);

        fm.OptOut.Should().BeTrue();
        fm.OneNote.Should().BeNull();
    }

    [Fact]
    public void Parse_OneNoteNull_TreatedAsPublishable()
    {
        // YAML: `onenote:` with no value parses as null — treat as opt-in.
        var content = "---\nonenote:\n---\nBody.";
        var fm = _parser.Parse(content);

        fm.OneNote.Should().NotBeNull();
        fm.OptOut.Should().BeFalse();
    }

    [Fact]
    public void Parse_FullOneNoteBlock_PopulatesAllFields()
    {
        var content = """
            ---
            title: My Page
            onenote:
              notebook: Work Notes
              section: Architecture
              section_groups:
                - Backend
                - API
            ---
            Body.
            """;
        var fm = _parser.Parse(content);

        fm.Title.Should().Be("My Page");
        fm.OneNote.Should().NotBeNull();
        fm.OneNote!.Notebook.Should().Be("Work Notes");
        fm.OneNote.Section.Should().Be("Architecture");
        fm.OneNote.SectionGroups.Should().BeEquivalentTo(new[] { "Backend", "API" });
    }

    [Fact]
    public void Parse_TitleOnly_SetsTitleLeavesOneNoteNull()
    {
        var content = "---\ntitle: Only Title\n---\nBody.";
        var fm = _parser.Parse(content);

        fm.Title.Should().Be("Only Title");
        fm.OneNote.Should().BeNull();
    }

    [Fact]
    public void Parse_MalformedYaml_ThrowsWithFilePointer()
    {
        var content = "---\ntitle: [unclosed\n---\nBody.";
        var act = () => _parser.Parse(content);

        act.Should().Throw<FrontMatterParseException>();
    }
}
```

- [ ] **Step 2: Run tests, verify they fail with "type not found"**

Run: `dotnet test --filter "FullyQualifiedName~FrontMatterParserTests"`
Expected: Compilation errors (FrontMatterParser, FrontMatterParseException not defined).

- [ ] **Step 3: Implement `FrontMatterParser`**

Create `OneNoteMarkdownExporter/Services/FrontMatterParser.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using OneNoteMarkdownExporter.Models;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace OneNoteMarkdownExporter.Services;

/// <summary>
/// Parses the minimal YAML front-matter subset needed for OneNote routing:
/// <c>title</c> plus an optional <c>onenote</c> block (or <c>onenote: true</c> /
/// <c>onenote: false</c> shorthand). Full FM schema is issue #3.
/// </summary>
public class FrontMatterParser
{
    public FrontMatter Parse(string fileContent)
    {
        var yaml = ExtractYamlBlock(fileContent);
        if (yaml is null)
        {
            return new FrontMatter();
        }

        YamlStream stream;
        try
        {
            stream = new YamlStream();
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException ex)
        {
            throw new FrontMatterParseException(ex.Message, ex);
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            return new FrontMatter();
        }

        var fm = new FrontMatter();
        foreach (var (keyNode, valueNode) in root.Children)
        {
            if (keyNode is not YamlScalarNode keyScalar) continue;

            switch (keyScalar.Value)
            {
                case "title":
                    if (valueNode is YamlScalarNode titleScalar)
                    {
                        fm.Title = titleScalar.Value;
                    }
                    break;

                case "onenote":
                    ApplyOneNoteNode(fm, valueNode);
                    break;
            }
        }

        return fm;
    }

    private static void ApplyOneNoteNode(FrontMatter fm, YamlNode node)
    {
        if (node is YamlScalarNode scalar)
        {
            if (scalar.Value is null)
            {
                fm.OneNote = new OneNoteFrontMatter();
                return;
            }

            if (scalar.Value.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                fm.OneNote = new OneNoteFrontMatter();
                return;
            }

            if (scalar.Value.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                fm.OptOut = true;
                return;
            }
        }

        if (node is YamlMappingNode mapping)
        {
            var block = new OneNoteFrontMatter();
            foreach (var (keyNode, valueNode) in mapping.Children)
            {
                if (keyNode is not YamlScalarNode keyScalar) continue;

                switch (keyScalar.Value)
                {
                    case "notebook":
                        block.Notebook = (valueNode as YamlScalarNode)?.Value;
                        break;
                    case "section":
                        block.Section = (valueNode as YamlScalarNode)?.Value;
                        break;
                    case "section_groups":
                        if (valueNode is YamlSequenceNode seq)
                        {
                            var list = new List<string>();
                            foreach (var item in seq.Children)
                            {
                                if (item is YamlScalarNode itemScalar && itemScalar.Value is not null)
                                {
                                    list.Add(itemScalar.Value);
                                }
                            }
                            block.SectionGroups = list;
                        }
                        break;
                }
            }
            fm.OneNote = block;
        }
    }

    private static string? ExtractYamlBlock(string content)
    {
        if (!content.StartsWith("---"))
        {
            return null;
        }

        using var reader = new StringReader(content);
        string? first = reader.ReadLine();
        if (first?.TrimEnd() != "---")
        {
            return null;
        }

        var yamlLines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.TrimEnd() == "---")
            {
                return string.Join('\n', yamlLines);
            }
            yamlLines.Add(line);
        }

        return null; // no closing delimiter — treat as no FM
    }
}

public class FrontMatterParseException : Exception
{
    public FrontMatterParseException(string message, Exception innerException)
        : base(message, innerException) { }
}
```

- [ ] **Step 4: Run tests, verify all pass**

Run: `dotnet test --filter "FullyQualifiedName~FrontMatterParserTests"`
Expected: 8/8 passing.

- [ ] **Step 5: Commit**

```bash
git add OneNoteMarkdownExporter/Services/FrontMatterParser.cs \
        OneNoteMarkdownExporter.Tests/Services/FrontMatterParserTests.cs
git commit -m "feat: parse minimal YAML front-matter for OneNote routing"
```

---

### Task 3: MarkdownTreeWalker (TDD)

**Files:**
- Create: `OneNoteMarkdownExporter/Services/MarkdownTreeWalker.cs`
- Create: `OneNoteMarkdownExporter.Tests/Services/MarkdownTreeWalkerTests.cs`

- [ ] **Step 1: Write failing tests for filesystem walk behavior**

Create `OneNoteMarkdownExporter.Tests/Services/MarkdownTreeWalkerTests.cs`:

```csharp
using System.IO;
using System.Linq;
using FluentAssertions;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Services;

public class MarkdownTreeWalkerTests : IDisposable
{
    private readonly string _root;
    private readonly MarkdownTreeWalker _walker = new();

    public MarkdownTreeWalkerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "mtw-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Touch(string relativePath)
    {
        var full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "");
        return full;
    }

    [Fact]
    public void Walk_EmptyDirectory_ReturnsEmpty()
    {
        _walker.Walk(_root).Should().BeEmpty();
    }

    [Fact]
    public void Walk_FindsMarkdownFiles_ReturnsRelativePaths()
    {
        Touch("a.md");
        Touch("sub/b.md");

        var paths = _walker.Walk(_root).ToList();

        paths.Should().BeEquivalentTo(new[]
        {
            "a.md",
            Path.Combine("sub", "b.md"),
        });
    }

    [Fact]
    public void Walk_SkipsNonMarkdownFiles()
    {
        Touch("a.md");
        Touch("b.txt");
        Touch("c.png");

        _walker.Walk(_root).Should().BeEquivalentTo(new[] { "a.md" });
    }

    [Fact]
    public void Walk_SkipsHiddenDirectories()
    {
        Touch(".git/config.md");
        Touch(".obsidian/workspace.md");
        Touch("kept.md");

        _walker.Walk(_root).Should().BeEquivalentTo(new[] { "kept.md" });
    }

    [Fact]
    public void Walk_ReturnsSortedOrder()
    {
        Touch("c.md");
        Touch("a.md");
        Touch("b.md");

        _walker.Walk(_root).Should().ContainInOrder("a.md", "b.md", "c.md");
    }
}
```

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~MarkdownTreeWalkerTests"`
Expected: Compilation errors (`MarkdownTreeWalker` not defined).

- [ ] **Step 3: Implement `MarkdownTreeWalker`**

Create `OneNoteMarkdownExporter/Services/MarkdownTreeWalker.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OneNoteMarkdownExporter.Services;

/// <summary>
/// Enumerates .md files under a source root, returning stable relative
/// paths. Skips hidden directories (dotfiles) and non-.md files. Does
/// not follow symlinks.
/// </summary>
public class MarkdownTreeWalker
{
    public IEnumerable<string> Walk(string sourceRoot)
    {
        var fullRoot = Path.GetFullPath(sourceRoot);
        var results = new List<string>();
        WalkDirectory(fullRoot, fullRoot, results);
        results.Sort(System.StringComparer.Ordinal);
        return results;
    }

    private static void WalkDirectory(string rootFullPath, string currentDir, List<string> results)
    {
        foreach (var file in Directory.EnumerateFiles(currentDir))
        {
            if (!file.EndsWith(".md", System.StringComparison.OrdinalIgnoreCase)) continue;
            var fileName = Path.GetFileName(file);
            if (fileName.StartsWith('.')) continue;
            results.Add(Path.GetRelativePath(rootFullPath, file));
        }

        foreach (var dir in Directory.EnumerateDirectories(currentDir))
        {
            var dirName = Path.GetFileName(dir);
            if (dirName.StartsWith('.')) continue; // .git, .obsidian, etc.
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) continue; // symlink
            WalkDirectory(rootFullPath, dir, results);
        }
    }
}
```

- [ ] **Step 4: Run tests, verify all pass**

Run: `dotnet test --filter "FullyQualifiedName~MarkdownTreeWalkerTests"`
Expected: 5/5 passing.

- [ ] **Step 5: Commit**

```bash
git add OneNoteMarkdownExporter/Services/MarkdownTreeWalker.cs \
        OneNoteMarkdownExporter.Tests/Services/MarkdownTreeWalkerTests.cs
git commit -m "feat: walk markdown source trees with stable relative paths"
```

---

### Task 4: OneNoteTargetResolver (TDD)

This is the heart of the feature. Tests mirror the examples table in the spec and each branch of the resolution rule.

**Files:**
- Create: `OneNoteMarkdownExporter/Services/OneNoteTargetResolver.cs`
- Create: `OneNoteMarkdownExporter.Tests/Services/OneNoteTargetResolverTests.cs`

- [ ] **Step 1: Write failing tests for the resolver algorithm**

Create `OneNoteMarkdownExporter.Tests/Services/OneNoteTargetResolverTests.cs`:

```csharp
using System.Collections.Generic;
using FluentAssertions;
using OneNoteMarkdownExporter.Models;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Services;

public class OneNoteTargetResolverTests
{
    private readonly OneNoteTargetResolver _resolver = new();

    private static FrontMatter OneNoteTrue() => new() { OneNote = new OneNoteFrontMatter() };
    private static FrontMatter OneNoteFalse() => new() { OptOut = true };
    private static FrontMatter Empty() => new();

    // --- Skip cases -----------------------------------------------------

    [Fact]
    public void Resolve_NoOneNoteAndNoCli_Skips()
    {
        var outcome = _resolver.Resolve("drafts/tmp.md", Empty(), cliNotebook: null, firstH1: null);

        outcome.Target.Should().BeNull();
        outcome.Diagnostic!.Severity.Should().Be(DiagnosticSeverity.Info);
        outcome.Diagnostic.Message.Should().Contain("skipped");
    }

    [Fact]
    public void Resolve_OneNoteFalse_SkipsEvenWithCliNotebook()
    {
        var outcome = _resolver.Resolve("foo.md", OneNoteFalse(), cliNotebook: "Work Notes", firstH1: null);

        outcome.Target.Should().BeNull();
        outcome.Diagnostic!.Severity.Should().Be(DiagnosticSeverity.Info);
    }

    // --- Happy path: fully-FM routing ----------------------------------

    [Fact]
    public void Resolve_FmFullySpecified_IgnoresFolderPath()
    {
        var fm = new FrontMatter
        {
            OneNote = new OneNoteFrontMatter
            {
                Notebook = "X",
                Section = "Y",
                SectionGroups = new() { "SG1" },
            },
        };

        var outcome = _resolver.Resolve("random/foo.md", fm, cliNotebook: null, firstH1: null);

        outcome.Target.Should().NotBeNull();
        outcome.Target!.Notebook.Should().Be("X");
        outcome.Target.Section.Should().Be("Y");
        outcome.Target.SectionGroups.Should().BeEquivalentTo(new[] { "SG1" });
        outcome.Target.PageSlug.Should().Be("foo");
        outcome.Target.PageTitle.Should().Be("foo");
    }

    // --- Happy path: fully inferred from folders -----------------------

    [Fact]
    public void Resolve_FullFolderInference_OneNoteTrue()
    {
        var outcome = _resolver.Resolve(
            "Work Notes/Architecture/overview.md",
            OneNoteTrue(),
            cliNotebook: null,
            firstH1: null);

        outcome.Target!.Notebook.Should().Be("Work Notes");
        outcome.Target.SectionGroups.Should().BeEmpty();
        outcome.Target.Section.Should().Be("Architecture");
        outcome.Target.PageSlug.Should().Be("overview");
    }

    [Fact]
    public void Resolve_DeepFolderInference_ProducesSectionGroups()
    {
        var outcome = _resolver.Resolve(
            "Work Notes/Backend/API/Architecture/overview.md",
            OneNoteTrue(),
            cliNotebook: null,
            firstH1: null);

        outcome.Target!.Notebook.Should().Be("Work Notes");
        outcome.Target.SectionGroups.Should().BeEquivalentTo(new[] { "Backend", "API" });
        outcome.Target.Section.Should().Be("Architecture");
        outcome.Target.PageSlug.Should().Be("overview");
    }

    // --- CLI notebook overrides ----------------------------------------

    [Fact]
    public void Resolve_CliNotebook_WithoutFm_PublishesEverything()
    {
        var outcome = _resolver.Resolve(
            "architecture/overview.md",
            Empty(),
            cliNotebook: "Work Notes",
            firstH1: null);

        outcome.Target!.Notebook.Should().Be("Work Notes");
        outcome.Target.Section.Should().Be("architecture");
        outcome.Target.PageSlug.Should().Be("overview");
    }

    // --- Dotted filenames ----------------------------------------------

    [Fact]
    public void Resolve_DottedFilename_SplitsAsPathSegments()
    {
        var fm = new FrontMatter
        {
            OneNote = new OneNoteFrontMatter { Notebook = "Work Notes" },
        };
        var outcome = _resolver.Resolve("backend.api.auth.md", fm, cliNotebook: null, firstH1: null);

        outcome.Target!.Notebook.Should().Be("Work Notes");
        outcome.Target.SectionGroups.Should().BeEquivalentTo(new[] { "backend" });
        outcome.Target.Section.Should().Be("api");
        outcome.Target.PageSlug.Should().Be("auth");
    }

    [Fact]
    public void Resolve_FolderPlusDottedFilename_Concatenates()
    {
        var outcome = _resolver.Resolve(
            "work/backend.api.auth.md",
            OneNoteTrue(),
            cliNotebook: null,
            firstH1: null);

        outcome.Target!.Notebook.Should().Be("work");
        outcome.Target.SectionGroups.Should().BeEquivalentTo(new[] { "backend" });
        outcome.Target.Section.Should().Be("api");
        outcome.Target.PageSlug.Should().Be("auth");
    }

    // --- Title fallback chain ------------------------------------------

    [Fact]
    public void Resolve_TitleFromFrontMatter_Wins()
    {
        var fm = new FrontMatter
        {
            Title = "Fancy Title",
            OneNote = new OneNoteFrontMatter { Notebook = "X", Section = "Y" },
        };
        var outcome = _resolver.Resolve("foo.md", fm, cliNotebook: null, firstH1: "# Heading");

        outcome.Target!.PageTitle.Should().Be("Fancy Title");
    }

    [Fact]
    public void Resolve_TitleFromFirstH1_WhenNoFmTitle()
    {
        var fm = new FrontMatter
        {
            OneNote = new OneNoteFrontMatter { Notebook = "X", Section = "Y" },
        };
        var outcome = _resolver.Resolve("foo.md", fm, cliNotebook: null, firstH1: "Heading One");

        outcome.Target!.PageTitle.Should().Be("Heading One");
    }

    [Fact]
    public void Resolve_TitleFallsBackToSlug()
    {
        var fm = new FrontMatter
        {
            OneNote = new OneNoteFrontMatter { Notebook = "X", Section = "Y" },
        };
        var outcome = _resolver.Resolve("foo.md", fm, cliNotebook: null, firstH1: null);

        outcome.Target!.PageTitle.Should().Be("foo");
        outcome.Diagnostic!.Severity.Should().Be(DiagnosticSeverity.Warning);
        outcome.Diagnostic.Message.Should().Contain("no title found");
    }

    // --- Error: single segment -----------------------------------------

    [Fact]
    public void Resolve_SingleSegment_WithOneNoteTrue_Errors()
    {
        var outcome = _resolver.Resolve("overview.md", OneNoteTrue(), cliNotebook: null, firstH1: null);

        outcome.Target.Should().BeNull();
        outcome.Diagnostic!.Severity.Should().Be(DiagnosticSeverity.Error);
        outcome.Diagnostic.Message.Should().Contain("cannot infer OneNote path");
    }

    // --- Error: section specified without notebook ---------------------

    [Fact]
    public void Resolve_SectionSetButNoNotebook_Errors()
    {
        var fm = new FrontMatter
        {
            OneNote = new OneNoteFrontMatter { Section = "Y" },
        };
        var outcome = _resolver.Resolve("foo.md", fm, cliNotebook: null, firstH1: null);

        outcome.Target.Should().BeNull();
        outcome.Diagnostic!.Severity.Should().Be(DiagnosticSeverity.Error);
        outcome.Diagnostic.Message.Should().Contain("no notebook");
    }

    // --- Warning: FM notebook vs folder mismatch -----------------------

    [Fact]
    public void Resolve_FmNotebookDiffersFromFolder_WarnsButUsesFm()
    {
        // Under Option C: the first FOLDER segment is the "notebook slot."
        // FM overrides the notebook name; the folder slot is consumed (not
        // left as a section group). Warning emitted for the mismatch.
        var fm = new FrontMatter
        {
            OneNote = new OneNoteFrontMatter { Notebook = "Personal" },
        };
        var outcome = _resolver.Resolve(
            "Work Notes/arch/overview.md",
            fm,
            cliNotebook: null,
            firstH1: null);

        outcome.Target!.Notebook.Should().Be("Personal");
        outcome.Target.SectionGroups.Should().BeEmpty(); // folder slot consumed
        outcome.Target.Section.Should().Be("arch");
        outcome.Target.PageSlug.Should().Be("overview");
        outcome.Diagnostic.Should().NotBeNull();
        outcome.Diagnostic!.Severity.Should().Be(DiagnosticSeverity.Warning);
        outcome.Diagnostic.Message.Should().Contain("overrides folder-inferred");
    }

    // --- Numeric-only segment warning ----------------------------------

    [Fact]
    public void Resolve_NumericSegment_WarnsAboutAccidentalSplit()
    {
        // Scenario crafted so notebook- and title- warnings do not fire;
        // the numeric-segment warning is the only diagnostic emitted.
        // File: ns/v1.0.md → segments ["ns", "v1", "0"], pageSlug "0".
        var fm = new FrontMatter
        {
            Title = "Version 1.0 notes",
            OneNote = new OneNoteFrontMatter { Notebook = "ns" },
        };
        var outcome = _resolver.Resolve("ns/v1.0.md", fm, cliNotebook: null, firstH1: null);

        outcome.Target.Should().NotBeNull();
        outcome.Target!.PageSlug.Should().Be("0");
        outcome.Diagnostic!.Severity.Should().Be(DiagnosticSeverity.Warning);
        outcome.Diagnostic.Message.Should().Contain("numeric-only");
    }

    // --- Empty segment is an error -------------------------------------

    [Fact]
    public void Resolve_EmptyPathSegment_Errors()
    {
        var outcome = _resolver.Resolve(
            "foo..bar.md",
            OneNoteTrue(),
            cliNotebook: null,
            firstH1: null);

        outcome.Target.Should().BeNull();
        outcome.Diagnostic!.Severity.Should().Be(DiagnosticSeverity.Error);
        outcome.Diagnostic.Message.Should().Contain("empty path segment");
    }
}
```

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~OneNoteTargetResolverTests"`
Expected: Compilation errors (`OneNoteTargetResolver`, `ResolveOutcome` not defined).

- [ ] **Step 3: Implement `OneNoteTargetResolver`**

Create `OneNoteMarkdownExporter/Services/OneNoteTargetResolver.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OneNoteMarkdownExporter.Models;

namespace OneNoteMarkdownExporter.Services;

public class OneNoteTargetResolver
{
    public ResolveOutcome Resolve(
        string fileRelativePath,
        FrontMatter fm,
        string? cliNotebook,
        string? firstH1)
    {
        // 1) Opt-out short-circuit.
        if (fm.OptOut)
        {
            return ResolveOutcome.Skipped(fileRelativePath, "skipped (onenote: false).");
        }

        // 2) Split file_rel into folder segments + filename dot-segments.
        var (segments, folderSegmentCount) = Segment(fileRelativePath);
        if (segments.Any(string.IsNullOrEmpty))
        {
            return ResolveOutcome.ErroredResult(
                fileRelativePath,
                $"{fileRelativePath}: empty path segment.");
        }

        // 3) Publish gate: need either `onenote:` key or `--notebook` to proceed.
        var hasOneNoteKey = fm.OneNote is not null;
        if (!hasOneNoteKey && cliNotebook is null)
        {
            return ResolveOutcome.Skipped(
                fileRelativePath,
                $"{fileRelativePath}: skipped (no OneNote target).");
        }

        // 4) Page slug = last segment; title chain = FM > firstH1 > slug.
        if (segments.Count == 0)
        {
            return ResolveOutcome.ErroredResult(
                fileRelativePath,
                $"{fileRelativePath}: empty path.");
        }

        var pageSlug = segments[^1].Trim();
        var remaining = segments.Take(segments.Count - 1).Select(s => s.Trim()).ToList();
        bool hasNotebookSlot = folderSegmentCount > 0;

        string? titleWarning = null;
        var pageTitle = fm.Title ?? firstH1;
        if (string.IsNullOrEmpty(pageTitle))
        {
            pageTitle = pageSlug;
            titleWarning = $"{fileRelativePath}: no title found; using slug \"{pageSlug}\" as page name.";
        }

        // 5) Resolve notebook.
        // First FOLDER segment is the "notebook slot." Dots in bare filenames are
        // never the notebook slot — they're always SG/section hierarchy.
        string? notebook = fm.OneNote?.Notebook;
        string? notebookWarning = null;

        if (notebook is not null)
        {
            // FM sets notebook. If a folder notebook-slot exists, consume it.
            if (hasNotebookSlot && remaining.Count > 0)
            {
                var folderFirst = remaining[0];
                if (!string.Equals(folderFirst, notebook))
                {
                    notebookWarning = $"{fileRelativePath}: FM notebook \"{notebook}\" overrides folder-inferred \"{folderFirst}\".";
                }
                remaining.RemoveAt(0); // Always consume the folder notebook slot.
            }
            // Bare filename + FM notebook: dot-segments stay (become SG/section).
        }
        else if (cliNotebook is not null)
        {
            notebook = cliNotebook;
            // CLI mode: no consumption — source root is the notebook root.
        }
        else if (hasOneNoteKey && remaining.Count > 0)
        {
            // Inferred: consume first segment (folder or dot) as notebook.
            notebook = remaining[0];
            remaining.RemoveAt(0);
        }
        else
        {
            return ResolveOutcome.ErroredResult(
                fileRelativePath,
                fm.OneNote?.Section is not null
                    ? $"{fileRelativePath}: section specified but no notebook — add onenote.notebook or pass --notebook."
                    : $"{fileRelativePath}: cannot infer OneNote path — add onenote.notebook and onenote.section to front-matter, or move the file into a folder.");
        }

        // 6) Resolve section.
        string? section = fm.OneNote?.Section;
        if (section is null)
        {
            if (remaining.Count == 0)
            {
                return ResolveOutcome.ErroredResult(
                    fileRelativePath,
                    $"{fileRelativePath}: cannot infer OneNote path — add onenote.section or deepen the folder structure.");
            }
            section = remaining[^1];
            remaining.RemoveAt(remaining.Count - 1);
        }

        // 7) Resolve section groups.
        List<string> sectionGroups;
        if (fm.OneNote?.SectionGroups is not null)
        {
            sectionGroups = fm.OneNote.SectionGroups;
        }
        else
        {
            // Any remaining middle segments become SGs.
            // If the notebook came from FM/CLI (not consumed from segments) AND we still
            // have extras, they are SGs too.
            sectionGroups = remaining;
        }

        // 8) Numeric-only segment warning.
        var numericWarning = Numeric(notebook, sectionGroups, section, pageSlug, fileRelativePath);

        var target = new ResolvedTarget
        {
            Notebook = notebook,
            SectionGroups = sectionGroups,
            Section = section,
            PageSlug = pageSlug,
            PageTitle = pageTitle!,
        };

        // Pick the most relevant single diagnostic (priority: warn > info).
        var diag =
            notebookWarning is not null ? Warn(fileRelativePath, notebookWarning) :
            titleWarning is not null ? Warn(fileRelativePath, titleWarning) :
            numericWarning is not null ? Warn(fileRelativePath, numericWarning) :
            null;

        return new ResolveOutcome(target, diag);
    }

    private static (List<string> segments, int folderSegmentCount) Segment(string fileRelativePath)
    {
        var pathParts = fileRelativePath
            .Replace('\\', '/')
            .Split('/', System.StringSplitOptions.None)
            .ToList();

        // Last part is the filename; everything before it is a folder segment.
        var filename = pathParts[^1];
        pathParts.RemoveAt(pathParts.Count - 1);
        int folderSegmentCount = pathParts.Count;

        // Split the filename stem on dots. Strip .md first.
        if (filename.EndsWith(".md", System.StringComparison.OrdinalIgnoreCase))
        {
            filename = filename[..^3];
        }
        var stemParts = filename.Split('.', System.StringSplitOptions.None);
        pathParts.AddRange(stemParts);
        return (pathParts, folderSegmentCount);
    }

    private static string? Numeric(
        string notebook,
        List<string> sectionGroups,
        string section,
        string pageSlug,
        string fileRelativePath)
    {
        var all = new List<string> { notebook };
        all.AddRange(sectionGroups);
        all.Add(section);
        all.Add(pageSlug);

        foreach (var seg in all)
        {
            if (seg.Length > 0 && seg.All(char.IsDigit))
            {
                return $"{fileRelativePath}: resolved segment \"{seg}\" is numeric-only; this may be an unintended split. Consider renaming with dashes.";
            }
        }
        return null;
    }

    private static PublishDiagnostic Warn(string file, string message) =>
        new() { FileRelativePath = file, Severity = DiagnosticSeverity.Warning, Message = message };
}

public class ResolveOutcome
{
    public ResolveOutcome(ResolvedTarget? target, PublishDiagnostic? diagnostic)
    {
        Target = target;
        Diagnostic = diagnostic;
    }

    public ResolvedTarget? Target { get; }
    public PublishDiagnostic? Diagnostic { get; }

    public static ResolveOutcome Skipped(string file, string message) =>
        new(null, new PublishDiagnostic
        {
            FileRelativePath = file,
            Severity = DiagnosticSeverity.Info,
            Message = message,
        });

    public static ResolveOutcome ErroredResult(string file, string message) =>
        new(null, new PublishDiagnostic
        {
            FileRelativePath = file,
            Severity = DiagnosticSeverity.Error,
            Message = message,
        });
}
```

- [ ] **Step 4: Run tests, verify all pass**

Run: `dotnet test --filter "FullyQualifiedName~OneNoteTargetResolverTests"`
Expected: 15/15 passing.

- [ ] **Step 5: Commit**

```bash
git add OneNoteMarkdownExporter/Services/OneNoteTargetResolver.cs \
        OneNoteMarkdownExporter.Tests/Services/OneNoteTargetResolverTests.cs
git commit -m "feat: resolve markdown files to OneNote targets by folder+fm+cli"
```

---

### Task 5: OneNoteService.FindSectionIdByPath

**Files:**
- Modify: `OneNoteMarkdownExporter/Services/OneNoteService.cs:217-254`

This method walks nested section groups explicitly rather than by first-match recursion, so two different section groups can hold sections of the same name unambiguously. No unit test — COM is awkward to mock and the existing `FindSectionId` has no tests either; coverage comes from integration later.

- [ ] **Step 1: Add `FindSectionIdByPath` below `FindSectionId`**

In `OneNoteMarkdownExporter/Services/OneNoteService.cs`, insert after the closing `}` of `FindSectionRecursive` (around line 254):

```csharp
        /// <summary>
        /// Finds a section by walking the explicit path
        /// <c>notebook → sectionGroups[0] → … → sectionGroups[n-1] → section</c>.
        /// Case-insensitive at each step. Returns null if any segment is missing.
        /// </summary>
        public string? FindSectionIdByPath(
            string notebookName,
            IReadOnlyList<string> sectionGroups,
            string sectionName)
        {
            _oneNoteApp.GetHierarchy(null, HierarchyScope.hsSections, out string xml);
            var doc = XDocument.Parse(xml);
            if (doc.Root == null) return null;
            var ns = doc.Root.Name.Namespace;

            XElement? cursor = doc.Descendants(ns + "Notebook")
                .FirstOrDefault(n => string.Equals(
                    n.Attribute("name")?.Value, notebookName, StringComparison.OrdinalIgnoreCase));
            if (cursor == null) return null;

            foreach (var sgName in sectionGroups)
            {
                cursor = cursor.Elements(ns + "SectionGroup")
                    .FirstOrDefault(sg => string.Equals(
                        sg.Attribute("name")?.Value, sgName, StringComparison.OrdinalIgnoreCase));
                if (cursor == null) return null;
            }

            var section = cursor.Elements(ns + "Section")
                .FirstOrDefault(s => string.Equals(
                    s.Attribute("name")?.Value, sectionName, StringComparison.OrdinalIgnoreCase));
            return section?.Attribute("ID")?.Value;
        }
```

Note: add `using System.Collections.Generic;` at the top of the file if not already imported (required for `IReadOnlyList<string>`).

- [ ] **Step 2: Build the project**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add OneNoteMarkdownExporter/Services/OneNoteService.cs
git commit -m "feat: add FindSectionIdByPath for nested section-group navigation"
```

---

### Task 6: PublishTreeReport (TDD)

**Files:**
- Create: `OneNoteMarkdownExporter/Services/PublishTreeReport.cs`
- Create: `OneNoteMarkdownExporter.Tests/Services/PublishTreeReportTests.cs`

- [ ] **Step 1: Write failing tests for report aggregation**

Create `OneNoteMarkdownExporter.Tests/Services/PublishTreeReportTests.cs`:

```csharp
using FluentAssertions;
using OneNoteMarkdownExporter.Models;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Services;

public class PublishTreeReportTests
{
    [Fact]
    public void Empty_ZeroCounts()
    {
        var report = new PublishTreeReport();

        report.TotalFiles.Should().Be(0);
        report.Published.Should().Be(0);
        report.Skipped.Should().Be(0);
        report.Errored.Should().Be(0);
        report.Success.Should().BeTrue();
    }

    [Fact]
    public void Published_IncrementsOnRecord()
    {
        var report = new PublishTreeReport();
        report.RecordPublished("a.md");
        report.RecordPublished("b.md");

        report.Published.Should().Be(2);
        report.TotalFiles.Should().Be(2);
        report.Success.Should().BeTrue();
    }

    [Fact]
    public void Errored_FlipsSuccessFalse()
    {
        var report = new PublishTreeReport();
        report.RecordError(new PublishDiagnostic
        {
            FileRelativePath = "broken.md",
            Severity = DiagnosticSeverity.Error,
            Message = "broken.md: boom",
        });

        report.Errored.Should().Be(1);
        report.Success.Should().BeFalse();
    }

    [Fact]
    public void Summary_ContainsAllCounts()
    {
        var report = new PublishTreeReport();
        report.RecordPublished("a.md");
        report.RecordSkipped(new PublishDiagnostic
        {
            FileRelativePath = "b.md",
            Severity = DiagnosticSeverity.Info,
            Message = "b.md: skipped",
        });
        report.RecordWarning(new PublishDiagnostic
        {
            FileRelativePath = "c.md",
            Severity = DiagnosticSeverity.Warning,
            Message = "c.md: warned",
        });

        var summary = report.RenderSummary();
        summary.Should().Contain("1 published");
        summary.Should().Contain("1 skipped");
        summary.Should().Contain("1 warning");
    }
}
```

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~PublishTreeReportTests"`
Expected: Compilation errors.

- [ ] **Step 3: Implement `PublishTreeReport`**

Create `OneNoteMarkdownExporter/Services/PublishTreeReport.cs`:

```csharp
using System.Collections.Generic;
using System.Text;
using OneNoteMarkdownExporter.Models;

namespace OneNoteMarkdownExporter.Services;

/// <summary>
/// Aggregates per-file outcomes for a tree-publish run and renders a
/// one-line summary suitable for stdout plus a diagnostic list.
/// </summary>
public class PublishTreeReport
{
    private readonly List<PublishDiagnostic> _diagnostics = new();
    private int _published;
    private int _skipped;
    private int _warnings;
    private int _errored;

    public int TotalFiles => _published + _skipped + _errored;
    public int Published => _published;
    public int Skipped => _skipped;
    public int Warnings => _warnings;
    public int Errored => _errored;
    public bool Success => _errored == 0;
    public IReadOnlyList<PublishDiagnostic> Diagnostics => _diagnostics;

    public void RecordPublished(string file)
    {
        _published++;
    }

    public void RecordSkipped(PublishDiagnostic diagnostic)
    {
        _skipped++;
        _diagnostics.Add(diagnostic);
    }

    public void RecordWarning(PublishDiagnostic diagnostic)
    {
        _warnings++;
        _diagnostics.Add(diagnostic);
    }

    public void RecordError(PublishDiagnostic diagnostic)
    {
        _errored++;
        _diagnostics.Add(diagnostic);
    }

    public string RenderSummary()
    {
        var sb = new StringBuilder();
        sb.Append($"{_published} published");
        if (_skipped > 0) sb.Append($", {_skipped} skipped");
        if (_warnings > 0) sb.Append($", {_warnings} warning{(_warnings == 1 ? "" : "s")}");
        if (_errored > 0) sb.Append($", {_errored} error{(_errored == 1 ? "" : "s")}");
        return sb.ToString();
    }
}
```

- [ ] **Step 4: Run tests, verify all pass**

Run: `dotnet test --filter "FullyQualifiedName~PublishTreeReportTests"`
Expected: 4/4 passing.

- [ ] **Step 5: Commit**

```bash
git add OneNoteMarkdownExporter/Services/PublishTreeReport.cs \
        OneNoteMarkdownExporter.Tests/Services/PublishTreeReportTests.cs
git commit -m "feat: aggregate tree-publish outcomes into a summarized report"
```

---

### Task 7: PublishTreeService + PublishTreeOptions (TDD)

This service wires the walker + parser + resolver + OneNote service + report. Tests exercise orchestration with fakes for OneNote; the real COM path gets covered manually during the integration pass.

**Files:**
- Create: `OneNoteMarkdownExporter/Services/PublishTreeOptions.cs`
- Create: `OneNoteMarkdownExporter/Services/PublishTreeService.cs`
- Create: `OneNoteMarkdownExporter.Tests/Services/PublishTreeServiceTests.cs`

- [ ] **Step 1: Create `PublishTreeOptions.cs`**

```csharp
namespace OneNoteMarkdownExporter.Services;

public class PublishTreeOptions
{
    /// <summary>Root directory to walk.</summary>
    public string SourceRoot { get; set; } = string.Empty;

    /// <summary>
    /// Bulk notebook. When set, every .md file publishes to this notebook even
    /// without an `onenote:` front-matter key. FM-set notebook still wins per-file.
    /// </summary>
    public string? CliNotebook { get; set; }

    public bool Collapsible { get; set; } = true;
    public bool DryRun { get; set; } = false;
    public bool Verbose { get; set; } = false;
    public bool Quiet { get; set; } = false;
}
```

- [ ] **Step 2: Write failing tests for `PublishTreeService`**

Create `OneNoteMarkdownExporter.Tests/Services/PublishTreeServiceTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Services;

public class PublishTreeServiceTests : IDisposable
{
    private readonly string _root;

    public PublishTreeServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pts-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Write(string relPath, string content)
    {
        var full = Path.Combine(_root, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    private static PublishTreeService NewService(FakeOneNotePublisher publisher) =>
        new(new MarkdownTreeWalker(), new FrontMatterParser(), new OneNoteTargetResolver(), publisher);

    [Fact]
    public async Task PublishAsync_DryRun_DoesNotCallPublisher()
    {
        Write("a.md", "---\nonenote:\n  notebook: NB\n  section: S\n---\nBody.");
        var publisher = new FakeOneNotePublisher();
        var service = NewService(publisher);

        var report = await service.PublishAsync(new PublishTreeOptions
        {
            SourceRoot = _root,
            DryRun = true,
        });

        publisher.CreatedPages.Should().BeEmpty();
        report.Published.Should().Be(1); // dry-run counts as "would publish"
    }

    [Fact]
    public async Task PublishAsync_SkipsFilesWithoutOneNoteFm_WhenNoCliNotebook()
    {
        Write("a.md", "# Just a heading\nBody.");
        Write("b.md", "---\nonenote:\n  notebook: NB\n  section: S\n---\nBody.");
        var publisher = new FakeOneNotePublisher();
        var service = NewService(publisher);

        var report = await service.PublishAsync(new PublishTreeOptions { SourceRoot = _root });

        report.Published.Should().Be(1);
        report.Skipped.Should().Be(1);
    }

    [Fact]
    public async Task PublishAsync_CliNotebook_PublishesEverything()
    {
        Write("a.md", "# A");
        Write("b.md", "# B");
        var publisher = new FakeOneNotePublisher();
        var service = NewService(publisher);

        var report = await service.PublishAsync(new PublishTreeOptions
        {
            SourceRoot = _root,
            CliNotebook = "NB",
        });

        report.Published.Should().Be(0); // can't resolve section — single-segment errors
        report.Errored.Should().Be(2);
    }

    [Fact]
    public async Task PublishAsync_ReportsCollisions()
    {
        Write("sect/page.md", "---\nonenote:\n  notebook: NB\n  section: S\n---");
        Write("other/sect/page.md", "---\nonenote:\n  notebook: NB\n  section: S\n---");
        var publisher = new FakeOneNotePublisher();
        var service = NewService(publisher);

        var report = await service.PublishAsync(new PublishTreeOptions
        {
            SourceRoot = _root,
        });

        report.Errored.Should().Be(2);
        report.Diagnostics.Should().Contain(d => d.Message.Contains("Collision"));
    }

    private class FakeOneNotePublisher : IOneNotePublisher
    {
        public List<(string Notebook, IReadOnlyList<string> SGs, string Section, string PageTitle)> CreatedPages { get; } = new();
        public bool FailNextCall { get; set; }

        public Task PublishAsync(
            string notebook,
            IReadOnlyList<string> sectionGroups,
            string section,
            string pageTitle,
            string markdownContent,
            string sourceFileFullPath,
            bool collapsible)
        {
            if (FailNextCall) throw new System.InvalidOperationException("forced");
            CreatedPages.Add((notebook, sectionGroups, section, pageTitle));
            return Task.CompletedTask;
        }
    }
}
```

- [ ] **Step 3: Run tests, verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~PublishTreeServiceTests"`
Expected: Compilation errors (service + interface not defined).

- [ ] **Step 4: Implement `IOneNotePublisher` + real `PublishTreeService`**

Create `OneNoteMarkdownExporter/Services/PublishTreeService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OneNoteMarkdownExporter.Models;

namespace OneNoteMarkdownExporter.Services;

/// <summary>
/// Abstracts the "push one resolved page to OneNote" step so orchestration
/// can be unit-tested without COM. The real implementation wraps
/// <c>OneNoteService</c> + <c>MarkdownToOneNoteXmlConverter</c>.
/// </summary>
public interface IOneNotePublisher
{
    Task PublishAsync(
        string notebook,
        IReadOnlyList<string> sectionGroups,
        string section,
        string pageTitle,
        string markdownContent,
        string sourceFileFullPath,
        bool collapsible);
}

public class PublishTreeService
{
    private static readonly Regex FirstH1Regex = new(@"^\s*#\s+(?<title>.+?)\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

    private readonly MarkdownTreeWalker _walker;
    private readonly FrontMatterParser _parser;
    private readonly OneNoteTargetResolver _resolver;
    private readonly IOneNotePublisher _publisher;

    public PublishTreeService(
        MarkdownTreeWalker walker,
        FrontMatterParser parser,
        OneNoteTargetResolver resolver,
        IOneNotePublisher publisher)
    {
        _walker = walker;
        _parser = parser;
        _resolver = resolver;
        _publisher = publisher;
    }

    private record ResolvedEntry(
        string FileRel,
        ResolvedTarget Target,
        string Markdown,
        string FullPath,
        PublishDiagnostic? PendingDiagnostic);

    public async Task<PublishTreeReport> PublishAsync(
        PublishTreeOptions options,
        IProgress<string>? progress = null)
    {
        var report = new PublishTreeReport();
        var resolved = new List<ResolvedEntry>();

        // Pass 1 — walk, parse, resolve. Collision detection happens after.
        foreach (var fileRel in _walker.Walk(options.SourceRoot))
        {
            var fullPath = Path.Combine(options.SourceRoot, fileRel);
            string content;
            try
            {
                content = File.ReadAllText(fullPath);
            }
            catch (Exception ex)
            {
                report.RecordError(new PublishDiagnostic
                {
                    FileRelativePath = fileRel,
                    Severity = DiagnosticSeverity.Error,
                    Message = $"{fileRel}: read failed — {ex.Message}",
                });
                continue;
            }

            FrontMatter fm;
            try
            {
                fm = _parser.Parse(content);
            }
            catch (FrontMatterParseException ex)
            {
                report.RecordError(new PublishDiagnostic
                {
                    FileRelativePath = fileRel,
                    Severity = DiagnosticSeverity.Error,
                    Message = $"{fileRel}: invalid front-matter — {ex.Message}",
                });
                continue;
            }

            var firstH1Match = FirstH1Regex.Match(content);
            string? firstH1 = firstH1Match.Success ? firstH1Match.Groups["title"].Value : null;
            if (string.IsNullOrWhiteSpace(firstH1)) firstH1 = null;

            var outcome = _resolver.Resolve(fileRel, fm, options.CliNotebook, firstH1);

            if (outcome.Target is null)
            {
                if (outcome.Diagnostic?.Severity == DiagnosticSeverity.Error)
                {
                    report.RecordError(outcome.Diagnostic);
                }
                else if (outcome.Diagnostic is not null)
                {
                    report.RecordSkipped(outcome.Diagnostic);
                }
                continue;
            }

            resolved.Add(new ResolvedEntry(fileRel, outcome.Target, content, fullPath, outcome.Diagnostic));
        }

        // Pass 2 — detect collisions by grouping on target key.
        var groups = resolved.GroupBy(r => TargetKey(r.Target)).ToList();
        var publishable = new List<ResolvedEntry>();
        foreach (var group in groups)
        {
            var list = group.ToList();
            if (list.Count > 1)
            {
                var files = string.Join(", ", list.Select(r => r.FileRel));
                foreach (var entry in list)
                {
                    report.RecordError(new PublishDiagnostic
                    {
                        FileRelativePath = entry.FileRel,
                        Severity = DiagnosticSeverity.Error,
                        Message = $"Collision: {files} all resolve to {group.Key}.",
                    });
                }
                continue;
            }
            publishable.Add(list[0]);
        }

        // Pass 3 — publish (or dry-run).
        foreach (var entry in publishable)
        {
            if (entry.PendingDiagnostic?.Severity == DiagnosticSeverity.Warning)
            {
                report.RecordWarning(entry.PendingDiagnostic);
            }

            if (options.DryRun)
            {
                report.RecordPublished(entry.FileRel);
                progress?.Report($"  [dry-run] {entry.FileRel} → {TargetKey(entry.Target)}  (title: {entry.Target.PageTitle})");
                continue;
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
                    options.Collapsible);
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

        return report;
    }

    private static string TargetKey(ResolvedTarget t) =>
        string.Join('/', new[] { t.Notebook }.Concat(t.SectionGroups).Concat(new[] { t.Section, t.PageSlug }));
}
```

- [ ] **Step 5: Run tests, verify all pass**

Run: `dotnet test --filter "FullyQualifiedName~PublishTreeServiceTests"`
Expected: 4/4 passing.

- [ ] **Step 6: Commit**

```bash
git add OneNoteMarkdownExporter/Services/PublishTreeOptions.cs \
        OneNoteMarkdownExporter/Services/PublishTreeService.cs \
        OneNoteMarkdownExporter.Tests/Services/PublishTreeServiceTests.cs
git commit -m "feat: orchestrate tree publishing with walker/parser/resolver/report"
```

---

### Task 8: Real OneNote publisher + CLI `--publish` command

**Files:**
- Create: `OneNoteMarkdownExporter/Services/OneNoteTreePublisher.cs` (real `IOneNotePublisher`)
- Modify: `OneNoteMarkdownExporter/Services/CliHandler.cs`
- Modify: `OneNoteMarkdownExporter.Tests/Cli/CliHandlerTests.cs`

- [ ] **Step 1: Create the real `OneNoteTreePublisher`**

`OneNoteMarkdownExporter/Services/OneNoteTreePublisher.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace OneNoteMarkdownExporter.Services;

/// <summary>
/// Real-COM implementation of <see cref="IOneNotePublisher"/>. Resolves the
/// section id via <c>FindSectionIdByPath</c>, creates a page, converts Markdown,
/// and uploads the XML.
/// </summary>
public class OneNoteTreePublisher : IOneNotePublisher
{
    private readonly OneNoteService _oneNoteService;
    private readonly MarkdownToOneNoteXmlConverter _converter;

    public OneNoteTreePublisher(OneNoteService oneNoteService, MarkdownToOneNoteXmlConverter converter)
    {
        _oneNoteService = oneNoteService;
        _converter = converter;
    }

    public Task PublishAsync(
        string notebook,
        IReadOnlyList<string> sectionGroups,
        string section,
        string pageTitle,
        string markdownContent,
        string sourceFileFullPath,
        bool collapsible)
    {
        return Task.Run(() =>
        {
            var sectionId = _oneNoteService.FindSectionIdByPath(notebook, sectionGroups, section)
                ?? throw new InvalidOperationException(
                    $"Section not found: {notebook}/{string.Join('/', sectionGroups)}/{section}".Replace("//", "/"));

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
}
```

- [ ] **Step 2: Add `--publish` to the CLI-flag detection list**

In `OneNoteMarkdownExporter/Services/CliHandler.cs`, edit the `cliFlags` array in `ShouldRunCli` (line 39-46). Add `"--publish"` to the list:

```csharp
var cliFlags = new[]
{
    "--all", "--notebook", "--section", "--page", "--output", "-o",
    "--overwrite", "--no-lint", "--lint-config",
    "--list", "--dry-run", "--verbose", "-v", "--quiet", "-q",
    "--import", "--file", "--no-collapse",
    "--publish",
    "--help", "-h", "-?", "--version"
};
```

- [ ] **Step 3: Declare the `--publish` option**

In `BuildRootCommand`, after the existing `noCollapseOption` declaration (line 129-131), add:

```csharp
var publishOption = new Option<string?>(
    "--publish",
    "Walk a Markdown source tree and publish every opt-in file to OneNote.");
```

Then add it to the root command just after `rootCommand.AddOption(noCollapseOption)` on line 148:

```csharp
rootCommand.AddOption(publishOption);
```

- [ ] **Step 4: Branch into the publish path inside the root handler**

In the `rootCommand.SetHandler` lambda (line 150+), insert a new branch **before** the existing `importTarget` check at line 157:

```csharp
var publishSource = result.GetValueForOption(publishOption);
if (!string.IsNullOrEmpty(publishSource))
{
    // When `--notebook` is also set, use its first value as the bulk notebook.
    // The existing `--notebook` option is `string[]` (used by export for multi-notebook);
    // in publish mode only the first value is meaningful.
    var notebooks = result.GetValueForOption(notebookOption);
    var cliNotebook = notebooks is { Length: > 0 } ? notebooks[0] : null;

    var exitCode = await ExecutePublishTreeAsync(
        publishSource,
        cliNotebook,
        collapsible: !result.GetValueForOption(noCollapseOption),
        dryRun: result.GetValueForOption(dryRunOption),
        verbose: result.GetValueForOption(verboseOption),
        quiet: result.GetValueForOption(quietOption));
    context.ExitCode = exitCode;
    return;
}
```

- [ ] **Step 5: Add the `ExecutePublishTreeAsync` method**

Add this method alongside `ExecuteImportAsync` (around line 282):

```csharp
private static async Task<int> ExecutePublishTreeAsync(
    string sourceDir,
    string? cliNotebook,
    bool collapsible,
    bool dryRun,
    bool verbose,
    bool quiet)
{
    if (!Directory.Exists(sourceDir))
    {
        Console.Error.WriteLine($"Error: source directory not found: {sourceDir}");
        return 1;
    }

    var options = new PublishTreeOptions
    {
        SourceRoot = Path.GetFullPath(sourceDir),
        CliNotebook = cliNotebook,
        Collapsible = collapsible,
        DryRun = dryRun,
        Verbose = verbose,
        Quiet = quiet,
    };

    if (!quiet)
    {
        Console.WriteLine("OneNote Markdown Tree Publisher");
        Console.WriteLine("===============================");
        Console.WriteLine($"Source: {options.SourceRoot}");
        if (cliNotebook is not null) Console.WriteLine($"Notebook (CLI): {cliNotebook}");
        if (dryRun) Console.WriteLine("Mode: DRY RUN");
        Console.WriteLine();
    }

    try
    {
        var oneNoteService = new OneNoteService();
        var converter = new MarkdownToOneNoteXmlConverter();
        var service = new PublishTreeService(
            new MarkdownTreeWalker(),
            new FrontMatterParser(),
            new OneNoteTargetResolver(),
            new OneNoteTreePublisher(oneNoteService, converter));

        var progress = new Progress<string>(msg =>
        {
            if (!quiet) Console.WriteLine(msg);
        });

        var report = await service.PublishAsync(options, progress);

        foreach (var diag in report.Diagnostics)
        {
            var shouldShow =
                diag.Severity == DiagnosticSeverity.Error ||
                diag.Severity == DiagnosticSeverity.Warning ||
                (diag.Severity == DiagnosticSeverity.Info && verbose);
            if (!shouldShow) continue;
            var prefix = diag.Severity switch
            {
                DiagnosticSeverity.Error => "error",
                DiagnosticSeverity.Warning => "warn",
                _ => "info",
            };
            Console.WriteLine($"  [{prefix}] {diag.Message}");
        }

        Console.WriteLine();
        Console.WriteLine(report.RenderSummary());
        return report.Success ? 0 : 1;
    }
    catch (System.Runtime.InteropServices.COMException ex)
    {
        Console.Error.WriteLine($"OneNote COM error: {ex.Message}");
        Console.Error.WriteLine("Make sure OneNote is installed and not running in a protected mode.");
        return 2;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        if (verbose)
        {
            Console.Error.WriteLine(ex.StackTrace);
        }
        return 1;
    }
}
```

- [ ] **Step 6: Add `using OneNoteMarkdownExporter.Models;` if not present**

The method references `DiagnosticSeverity` which is in `OneNoteMarkdownExporter.Models`. The file already has `using OneNoteMarkdownExporter.Models;` at line 9 — no change needed.

- [ ] **Step 7: Add a CLI parsing test for `--publish`**

In `OneNoteMarkdownExporter.Tests/Cli/CliHandlerTests.cs`, add a test that the `--publish` option is recognized and a nonexistent source directory returns exit code 1. Follow the existing test style in that file.

```csharp
[Fact]
public async Task RunAsync_PublishWithMissingSource_ReturnsOne()
{
    var missing = Path.Combine(Path.GetTempPath(), "pts-does-not-exist-" + Path.GetRandomFileName());
    var result = await CliHandler.RunAsync(new[] { "--publish", missing });
    result.Should().Be(1);
}

[Fact]
public void ShouldRunCli_PublishFlag_IsRecognized()
{
    CliHandler.ShouldRunCli(new[] { "--publish", "./notes" }).Should().BeTrue();
}
```

- [ ] **Step 8: Run the full test suite and build**

Run: `dotnet build && dotnet test`
Expected: Build succeeded, all tests passing (existing + new). Note: the COM-interop integration tests that actually talk to OneNote are not exercised here — manual verification happens in the Done criteria.

- [ ] **Step 9: Commit**

```bash
git add OneNoteMarkdownExporter/Services/OneNoteTreePublisher.cs \
        OneNoteMarkdownExporter/Services/CliHandler.cs \
        OneNoteMarkdownExporter.Tests/Cli/CliHandlerTests.cs
git commit -m "feat(cli): add --publish for walking and publishing markdown trees"
```

---

### Task 9: Documentation + CHANGELOG

**Files:**
- Modify: `docs/importer.md`
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Add a new section to `docs/importer.md`**

Append below the existing "Known limitations" section (before "Reference material"):

```markdown
## Tree publish (folder-tree → OneNote)

In addition to `--import` (single file / list), the CLI can walk an entire
Markdown source tree and publish every file that opts in:

```powershell
# Walk ./notes, publish each .md that has an `onenote:` front-matter key.
OneNoteMarkdownExporter.exe --publish ./notes

# Bulk mode: publish every .md under ./notes into "Work Notes".
OneNoteMarkdownExporter.exe --publish ./notes --notebook "Work Notes"

# Preview what would publish — no OneNote calls.
OneNoteMarkdownExporter.exe --publish ./notes --dry-run --verbose
```

The resolution rule (folder path + front-matter + CLI flag → target notebook /
section / page) is documented in detail in
`docs/superpowers/specs/2026-04-16-folder-tree-mapping-design.md`. Short version:

- **Folders** express hierarchy. `Work Notes/Architecture/overview.md` publishes to
  notebook `Work Notes`, section `Architecture`, page `overview`.
- **Dots in filename stems** also count as hierarchy. `backend.api.auth.md`
  resolves the same as `backend/api/auth.md`.
- **Front-matter** overrides folder inference per-field:

  ```yaml
  ---
  title: "My Page"
  onenote:
    notebook: "Work Notes"
    section: "Architecture"
    section_groups: ["Backend", "API"]
  ---
  ```

- **`onenote: true`** opts a file in when you want folder inference to do all the
  work. **`onenote: false`** explicitly excludes a file when using `--notebook`
  bulk mode.

Files without an `onenote:` key and without a `--notebook` flag are silently
skipped.
```

- [ ] **Step 2: Update `CHANGELOG.md`**

Edit the `[Unreleased] → Added` section and add:

```markdown
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
```

- [ ] **Step 3: Commit**

```bash
git add docs/importer.md CHANGELOG.md
git commit -m "docs: document --publish tree command and update changelog"
```

---

## Done criteria

- [ ] All new unit tests pass (`dotnet test`).
- [ ] `dotnet build` succeeds with 0 warnings that weren't already present on master.
- [ ] `--publish ./path --dry-run --verbose` output on a hand-built sample tree matches the resolution rules in the spec for at least three files (happy path, dotted-filename path, skipped-by-default path).
- [ ] `CHANGELOG.md` `[Unreleased]` updated.
- [ ] No `Co-Authored-By: Claude` trailers in any commit (enforced by `.claude/settings.json`).
- [ ] No file under `docs/superpowers/specs/` or `docs/superpowers/plans/` was edited by any task other than 0 (no drift from the frozen design).

## Out-of-plan items (deferred)

- Idempotent re-publish / stable page IDs → issue #6.
- Link resolution (`[x](./y.md)` → OneNote page link rewriting) → issue #7.
- Multi-target fan-out (`publish_to:`) → issue #4.
- Strict authoring lint → issue #8.
- Full front-matter schema (tags, aliases, ids) → issue #3.
- Auto-create missing notebook/section in OneNote.
- Wiki-link (`[[slug]]`) resolution.
