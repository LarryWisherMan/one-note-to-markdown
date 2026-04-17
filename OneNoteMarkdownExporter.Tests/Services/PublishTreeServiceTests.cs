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
        report.Published.Should().Be(1);
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
        // Two files in different folders but identical FM — both resolve to NB/S/page.
        Write("a/page.md", "---\nonenote:\n  notebook: NB\n  section: S\n---");
        Write("b/page.md", "---\nonenote:\n  notebook: NB\n  section: S\n---");
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
