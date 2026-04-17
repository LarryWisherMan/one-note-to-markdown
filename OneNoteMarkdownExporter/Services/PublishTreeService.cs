using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OneNoteMarkdownExporter.Models;

namespace OneNoteMarkdownExporter.Services;

/// <summary>
/// Abstracts the "push one resolved page to OneNote" step so orchestration
/// can be unit-tested without COM. The real implementation wraps
/// <c>OneNoteService</c> + <c>MarkdownToOneNoteXmlConverter</c>.
/// </summary>
public interface IOneNotePublisher
{
    Task PublishAsync(
        string notebook,
        IReadOnlyList<string> sectionGroups,
        string section,
        string pageTitle,
        string markdownContent,
        string sourceFileFullPath,
        bool collapsible);
}

public class PublishTreeService
{
    private static readonly Regex FirstH1Regex = new(@"^\s*#\s+(?<title>.+?)\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

    private readonly MarkdownTreeWalker _walker;
    private readonly FrontMatterParser _parser;
    private readonly OneNoteTargetResolver _resolver;
    private readonly IOneNotePublisher _publisher;

    public PublishTreeService(
        MarkdownTreeWalker walker,
        FrontMatterParser parser,
        OneNoteTargetResolver resolver,
        IOneNotePublisher publisher)
    {
        _walker = walker;
        _parser = parser;
        _resolver = resolver;
        _publisher = publisher;
    }

    private record ResolvedEntry(
        string FileRel,
        ResolvedTarget Target,
        string Markdown,
        string FullPath,
        PublishDiagnostic? PendingDiagnostic);

    public async Task<PublishTreeReport> PublishAsync(
        PublishTreeOptions options,
        IProgress<string>? progress = null)
    {
        var report = new PublishTreeReport();
        var resolved = new List<ResolvedEntry>();

        // Pass 1 — walk, parse, resolve.
        foreach (var fileRel in _walker.Walk(options.SourceRoot))
        {
            var fullPath = Path.Combine(options.SourceRoot, fileRel);
            string content;
            try
            {
                content = File.ReadAllText(fullPath);
            }
            catch (Exception ex)
            {
                report.RecordError(new PublishDiagnostic
                {
                    FileRelativePath = fileRel,
                    Severity = DiagnosticSeverity.Error,
                    Message = $"{fileRel}: read failed — {ex.Message}",
                });
                continue;
            }

            FrontMatter fm;
            try
            {
                fm = _parser.Parse(content);
            }
            catch (FrontMatterParseException ex)
            {
                report.RecordError(new PublishDiagnostic
                {
                    FileRelativePath = fileRel,
                    Severity = DiagnosticSeverity.Error,
                    Message = $"{fileRel}: invalid front-matter — {ex.Message}",
                });
                continue;
            }

            var firstH1 = ExtractFirstH1(content);

            var outcome = _resolver.Resolve(fileRel, fm, options.CliNotebook, firstH1);

            if (outcome.Target is null)
            {
                if (outcome.Diagnostic?.Severity == DiagnosticSeverity.Error)
                {
                    report.RecordError(outcome.Diagnostic);
                }
                else if (outcome.Diagnostic is not null)
                {
                    report.RecordSkipped(outcome.Diagnostic);
                }
                continue;
            }

            var markdown = FrontMatterParser.StripFrontMatter(content);
            resolved.Add(new ResolvedEntry(fileRel, outcome.Target, markdown, fullPath, outcome.Diagnostic));
        }

        // Pass 2 — detect collisions by grouping on target key.
        var groups = resolved.GroupBy(r => TargetKey(r.Target)).ToList();
        var publishable = new List<ResolvedEntry>();
        foreach (var group in groups)
        {
            var list = group.ToList();
            if (list.Count > 1)
            {
                var files = string.Join(", ", list.Select(r => r.FileRel));
                foreach (var entry in list)
                {
                    report.RecordError(new PublishDiagnostic
                    {
                        FileRelativePath = entry.FileRel,
                        Severity = DiagnosticSeverity.Error,
                        Message = $"Collision: {files} all resolve to {group.Key}.",
                    });
                }
                continue;
            }
            publishable.Add(list[0]);
        }

        // Pass 3 — publish (or dry-run).
        foreach (var entry in publishable)
        {
            if (entry.PendingDiagnostic?.Severity == DiagnosticSeverity.Warning)
            {
                report.RecordWarning(entry.PendingDiagnostic);
            }

            if (options.DryRun)
            {
                report.RecordPublished(entry.FileRel);
                progress?.Report($"  [dry-run] {entry.FileRel} → {TargetKey(entry.Target)}  (title: {entry.Target.PageTitle})");
                continue;
            }

            try
            {
                await _publisher.PublishAsync(
                    entry.Target.Notebook,
                    entry.Target.SectionGroups,
                    entry.Target.Section,
                    entry.Target.PageTitle,
                    entry.Markdown,
                    entry.FullPath,
                    options.Collapsible);
                report.RecordPublished(entry.FileRel);
            }
            catch (Exception ex)
            {
                report.RecordError(new PublishDiagnostic
                {
                    FileRelativePath = entry.FileRel,
                    Severity = DiagnosticSeverity.Error,
                    Message = $"{entry.FileRel}: publish failed — {ex.Message}",
                });
            }
        }

        return report;
    }

    private static string? ExtractFirstH1(string content)
    {
        // Skip front-matter block before searching for H1.
        var body = content;
        if (content.StartsWith("---", StringComparison.Ordinal))
        {
            var end = content.IndexOf("\n---", 3, StringComparison.Ordinal);
            if (end >= 0)
            {
                body = content[(end + 4)..];
            }
        }

        var match = FirstH1Regex.Match(body);
        if (!match.Success) return null;
        var title = match.Groups["title"].Value;
        return string.IsNullOrWhiteSpace(title) ? null : title;
    }

    private static string TargetKey(ResolvedTarget t) =>
        string.Join('/', new[] { t.Notebook }.Concat(t.SectionGroups).Concat(new[] { t.Section, t.PageSlug }));
}
