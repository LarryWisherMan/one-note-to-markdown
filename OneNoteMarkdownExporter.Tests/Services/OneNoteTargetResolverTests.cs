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

    [Fact]
    public void Resolve_FullFolderInference_OneNoteTrue()
    {
        var outcome = _resolver.Resolve(
            "Work Notes/Architecture/overview.md",
            OneNoteTrue(), cliNotebook: null, firstH1: null);
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
            OneNoteTrue(), cliNotebook: null, firstH1: null);
        outcome.Target!.Notebook.Should().Be("Work Notes");
        outcome.Target.SectionGroups.Should().BeEquivalentTo(new[] { "Backend", "API" });
        outcome.Target.Section.Should().Be("Architecture");
        outcome.Target.PageSlug.Should().Be("overview");
    }

    [Fact]
    public void Resolve_CliNotebook_WithoutFm_PublishesEverything()
    {
        var outcome = _resolver.Resolve(
            "architecture/overview.md", Empty(),
            cliNotebook: "Work Notes", firstH1: null);
        outcome.Target!.Notebook.Should().Be("Work Notes");
        outcome.Target.Section.Should().Be("architecture");
        outcome.Target.PageSlug.Should().Be("overview");
    }

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
            OneNoteTrue(), cliNotebook: null, firstH1: null);
        outcome.Target!.Notebook.Should().Be("work");
        outcome.Target.SectionGroups.Should().BeEquivalentTo(new[] { "backend" });
        outcome.Target.Section.Should().Be("api");
        outcome.Target.PageSlug.Should().Be("auth");
    }

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

    [Fact]
    public void Resolve_SingleSegment_WithOneNoteTrue_Errors()
    {
        var outcome = _resolver.Resolve("overview.md", OneNoteTrue(), cliNotebook: null, firstH1: null);
        outcome.Target.Should().BeNull();
        outcome.Diagnostic!.Severity.Should().Be(DiagnosticSeverity.Error);
        outcome.Diagnostic.Message.Should().Contain("cannot infer OneNote path");
    }

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

    [Fact]
    public void Resolve_FmNotebookDiffersFromFolder_WarnsButUsesFm()
    {
        // Option C: folder slot consumed, mismatch warns, SGs empty
        var fm = new FrontMatter
        {
            OneNote = new OneNoteFrontMatter { Notebook = "Personal" },
        };
        var outcome = _resolver.Resolve(
            "Work Notes/arch/overview.md", fm,
            cliNotebook: null, firstH1: null);
        outcome.Target!.Notebook.Should().Be("Personal");
        outcome.Target.SectionGroups.Should().BeEmpty();
        outcome.Target.Section.Should().Be("arch");
        outcome.Target.PageSlug.Should().Be("overview");
        outcome.Diagnostic.Should().NotBeNull();
        outcome.Diagnostic!.Severity.Should().Be(DiagnosticSeverity.Warning);
        outcome.Diagnostic.Message.Should().Contain("overrides folder-inferred");
    }

    [Fact]
    public void Resolve_NumericSegment_WarnsAboutAccidentalSplit()
    {
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

    [Fact]
    public void Resolve_EmptyPathSegment_Errors()
    {
        var outcome = _resolver.Resolve(
            "foo..bar.md", OneNoteTrue(),
            cliNotebook: null, firstH1: null);
        outcome.Target.Should().BeNull();
        outcome.Diagnostic!.Severity.Should().Be(DiagnosticSeverity.Error);
        outcome.Diagnostic.Message.Should().Contain("empty path segment");
    }
}
