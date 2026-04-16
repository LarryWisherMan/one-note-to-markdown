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
