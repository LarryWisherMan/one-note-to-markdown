using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace OneNoteMarkdownExporter.Services;

/// <summary>
/// Real-COM implementation of <see cref="IOneNotePublisher"/>. Resolves the
/// section id via <c>FindSectionIdByPath</c>, creates a page, converts Markdown,
/// and uploads the XML.
/// </summary>
public class OneNoteTreePublisher : IOneNotePublisher
{
    private readonly OneNoteService _oneNoteService;
    private readonly MarkdownToOneNoteXmlConverter _converter;

    public OneNoteTreePublisher(OneNoteService oneNoteService, MarkdownToOneNoteXmlConverter converter)
    {
        _oneNoteService = oneNoteService;
        _converter = converter;
    }

    public Task PublishAsync(
        string notebook,
        IReadOnlyList<string> sectionGroups,
        string section,
        string pageTitle,
        string markdownContent,
        string sourceFileFullPath,
        bool collapsible)
    {
        return Task.Run(() =>
        {
            var sectionId = _oneNoteService.FindSectionIdByPath(notebook, sectionGroups, section)
                ?? throw new InvalidOperationException(
                    $"Section not found: {notebook}/{string.Join('/', sectionGroups)}/{section}".Replace("//", "/"));

            var pageXml = _converter.Convert(
                markdownContent,
                pageTitle: pageTitle,
                collapsible: collapsible,
                basePath: Path.GetDirectoryName(sourceFileFullPath));

            var pageId = _oneNoteService.CreatePage(sectionId);
            var xmlWithId = pageXml.Replace("<one:Page ", $"<one:Page ID=\"{pageId}\" ");
            _oneNoteService.UpdatePageContent(xmlWithId);
        });
    }
}
