using System.Collections.Generic;
using System.IO;
using System.Linq;
using OneNoteMarkdownExporter.Models;

namespace OneNoteMarkdownExporter.Services;

public class OneNoteTargetResolver
{
    public ResolveOutcome Resolve(
        string fileRelativePath,
        FrontMatter fm,
        string? cliNotebook,
        string? firstH1)
    {
        // 1) Opt-out short-circuit.
        if (fm.OptOut)
        {
            return ResolveOutcome.Skipped(fileRelativePath, $"{fileRelativePath}: skipped (onenote: false).");
        }

        // 2) Split file_rel into folder segments + filename dot-segments.
        var (segments, folderSegmentCount) = Segment(fileRelativePath);
        if (segments.Any(string.IsNullOrEmpty))
        {
            return ResolveOutcome.ErroredResult(
                fileRelativePath,
                $"{fileRelativePath}: empty path segment.");
        }

        // 3) Publish gate.
        var hasOneNoteKey = fm.OneNote is not null;
        if (!hasOneNoteKey && cliNotebook is null)
        {
            return ResolveOutcome.Skipped(
                fileRelativePath,
                $"{fileRelativePath}: skipped (no OneNote target).");
        }

        if (segments.Count == 0)
        {
            return ResolveOutcome.ErroredResult(
                fileRelativePath,
                $"{fileRelativePath}: empty path.");
        }

        // 4) Page slug + title.
        var pageSlug = segments[^1].Trim();
        var remaining = segments.Take(segments.Count - 1).Select(s => s.Trim()).ToList();
        bool hasNotebookSlot = folderSegmentCount > 0;

        string? titleWarning = null;
        var pageTitle = fm.Title ?? firstH1;
        if (string.IsNullOrEmpty(pageTitle))
        {
            pageTitle = pageSlug;
            titleWarning = $"{fileRelativePath}: no title found; using slug \"{pageSlug}\" as page name.";
        }

        // 5) Resolve notebook (Option C: folder-vs-dot distinction).
        string? notebook = fm.OneNote?.Notebook;
        string? notebookWarning = null;

        if (notebook is not null)
        {
            // FM sets notebook. If a folder notebook-slot exists, consume it.
            if (hasNotebookSlot && remaining.Count > 0)
            {
                var folderFirst = remaining[0];
                if (!string.Equals(folderFirst, notebook))
                {
                    notebookWarning = $"{fileRelativePath}: FM notebook \"{notebook}\" overrides folder-inferred \"{folderFirst}\".";
                }
                remaining.RemoveAt(0); // Always consume the folder notebook slot.
            }
            // Bare filename + FM notebook: dot-segments stay (become SG/section).
        }
        else if (cliNotebook is not null)
        {
            notebook = cliNotebook;
            // CLI mode: no consumption.
        }
        else if (hasOneNoteKey && remaining.Count > 0)
        {
            // Inferred: consume first segment (folder or dot) as notebook.
            notebook = remaining[0];
            remaining.RemoveAt(0);
        }
        else
        {
            return ResolveOutcome.ErroredResult(
                fileRelativePath,
                fm.OneNote?.Section is not null
                    ? $"{fileRelativePath}: section specified but no notebook — add onenote.notebook or pass --notebook."
                    : $"{fileRelativePath}: cannot infer OneNote path — add onenote.notebook and onenote.section to front-matter, or move the file into a folder.");
        }

        // 6) Resolve section.
        string? section = fm.OneNote?.Section;
        if (section is null)
        {
            if (remaining.Count == 0)
            {
                return ResolveOutcome.ErroredResult(
                    fileRelativePath,
                    $"{fileRelativePath}: cannot infer OneNote path — add onenote.section or deepen the folder structure.");
            }
            section = remaining[^1];
            remaining.RemoveAt(remaining.Count - 1);
        }
        else if (remaining.Count > 0 && string.Equals(remaining[^1], section))
        {
            // FM section matches the last remaining folder segment — consume it
            // so it doesn't also appear as a section group (mirrors notebook-slot logic).
            remaining.RemoveAt(remaining.Count - 1);
        }

        // 7) Resolve section groups.
        List<string> sectionGroups;
        if (fm.OneNote?.SectionGroups is not null)
        {
            sectionGroups = fm.OneNote.SectionGroups;
        }
        else
        {
            sectionGroups = remaining;
        }

        // 8) Numeric-only segment warning.
        var numericWarning = Numeric(notebook, sectionGroups, section, pageSlug, fileRelativePath);

        var target = new ResolvedTarget
        {
            Notebook = notebook,
            SectionGroups = sectionGroups,
            Section = section,
            PageSlug = pageSlug,
            PageTitle = pageTitle!,
        };

        var diag =
            notebookWarning is not null ? Warn(fileRelativePath, notebookWarning) :
            titleWarning is not null ? Warn(fileRelativePath, titleWarning) :
            numericWarning is not null ? Warn(fileRelativePath, numericWarning) :
            null;

        return new ResolveOutcome(target, diag);
    }

    private static (List<string> segments, int folderSegmentCount) Segment(string fileRelativePath)
    {
        var pathParts = fileRelativePath
            .Replace('\\', '/')
            .Split('/', System.StringSplitOptions.None)
            .ToList();

        var filename = pathParts[^1];
        pathParts.RemoveAt(pathParts.Count - 1);
        int folderSegmentCount = pathParts.Count;

        if (filename.EndsWith(".md", System.StringComparison.OrdinalIgnoreCase))
        {
            filename = filename[..^3];
        }
        var stemParts = filename.Split('.', System.StringSplitOptions.None);
        pathParts.AddRange(stemParts);
        return (pathParts, folderSegmentCount);
    }

    private static string? Numeric(
        string notebook, List<string> sectionGroups,
        string section, string pageSlug, string fileRelativePath)
    {
        var all = new List<string> { notebook };
        all.AddRange(sectionGroups);
        all.Add(section);
        all.Add(pageSlug);

        foreach (var seg in all)
        {
            if (seg.Length > 0 && seg.All(char.IsDigit))
            {
                return $"{fileRelativePath}: resolved segment \"{seg}\" is numeric-only; this may be an unintended split. Consider renaming with dashes.";
            }
        }
        return null;
    }

    private static PublishDiagnostic Warn(string file, string message) =>
        new() { FileRelativePath = file, Severity = DiagnosticSeverity.Warning, Message = message };
}

public class ResolveOutcome
{
    public ResolveOutcome(ResolvedTarget? target, PublishDiagnostic? diagnostic)
    {
        Target = target;
        Diagnostic = diagnostic;
    }

    public ResolvedTarget? Target { get; }
    public PublishDiagnostic? Diagnostic { get; }

    public static ResolveOutcome Skipped(string file, string message) =>
        new(null, new PublishDiagnostic
        {
            FileRelativePath = file,
            Severity = DiagnosticSeverity.Info,
            Message = message,
        });

    public static ResolveOutcome ErroredResult(string file, string message) =>
        new(null, new PublishDiagnostic
        {
            FileRelativePath = file,
            Severity = DiagnosticSeverity.Error,
            Message = message,
        });
}
