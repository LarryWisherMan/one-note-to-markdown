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
