using System.Collections.Generic;

namespace OneNoteMarkdownExporter.Models;

/// <summary>
/// Parsed front-matter from a Markdown file. All fields are optional.
/// Represents only the subset needed for OneNote routing; the full
/// schema (issue #3) may extend this type later.
/// </summary>
public class FrontMatter
{
    /// <summary>Target-neutral page title. Falls back to first H1 then slug.</summary>
    public string? Title { get; set; }

    /// <summary>
    /// OneNote routing block. Null when the file has no <c>onenote:</c> key
    /// (publisher skips the file). Populated but empty when <c>onenote: true</c>
    /// or <c>onenote: {}</c>. <see cref="OptOut"/> is set when <c>onenote: false</c>.
    /// </summary>
    public OneNoteFrontMatter? OneNote { get; set; }

    /// <summary>True when front-matter contained the literal <c>onenote: false</c>.</summary>
    public bool OptOut { get; set; }
}

public class OneNoteFrontMatter
{
    public string? Notebook { get; set; }
    public string? Section { get; set; }
    public List<string>? SectionGroups { get; set; }
}
