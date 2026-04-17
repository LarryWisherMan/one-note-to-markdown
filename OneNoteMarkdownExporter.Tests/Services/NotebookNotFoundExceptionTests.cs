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
