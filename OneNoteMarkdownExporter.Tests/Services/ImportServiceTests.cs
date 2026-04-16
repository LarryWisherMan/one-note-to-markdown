using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Services;

public class ImportOptionsTests
{
    [Fact]
    public void NotebookName_DefaultsToEmptyString()
    {
        var options = new ImportOptions();
        options.NotebookName.Should().Be(string.Empty);
    }

    [Fact]
    public void Collapsible_DefaultsToTrue()
    {
        var options = new ImportOptions();
        options.Collapsible.Should().BeTrue();
    }

    [Fact]
    public void DryRun_DefaultsToFalse()
    {
        var options = new ImportOptions();
        options.DryRun.Should().BeFalse();
    }
}

public class ImportResultTests
{
    [Fact]
    public void Success_ReturnsTrueWhenNoFailures()
    {
        var result = new ImportResult { TotalFiles = 2, ImportedPages = 2, FailedPages = 0 };
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Success_ReturnsFalseWhenFailuresExist()
    {
        var result = new ImportResult { TotalFiles = 2, ImportedPages = 1, FailedPages = 1 };
        result.Success.Should().BeFalse();
    }

    [Fact]
    public void Errors_InitializesAsEmptyList()
    {
        var result = new ImportResult();
        result.Errors.Should().NotBeNull();
        result.Errors.Should().BeEmpty();
    }
}
