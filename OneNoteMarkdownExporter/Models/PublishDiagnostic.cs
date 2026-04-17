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
