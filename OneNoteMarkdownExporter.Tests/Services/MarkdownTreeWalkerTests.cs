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
