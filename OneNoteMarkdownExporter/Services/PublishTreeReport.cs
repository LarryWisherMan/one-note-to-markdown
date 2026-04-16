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
