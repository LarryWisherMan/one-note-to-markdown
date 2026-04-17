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
