using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OneNoteMarkdownExporter.Services
{
    public class ImportService
    {
        private readonly OneNoteService _oneNoteService;
        private readonly MarkdownToOneNoteXmlConverter _converter;

        public ImportService(OneNoteService oneNoteService, MarkdownToOneNoteXmlConverter converter)
        {
            _oneNoteService = oneNoteService;
            _converter = converter;
        }

        public async Task<ImportResult> ImportAsync(
            ImportOptions options,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new ImportResult { TotalFiles = options.FilePaths.Count };

            string? sectionId = null;
            if (!options.DryRun)
            {
                sectionId = _oneNoteService.FindSectionId(options.NotebookName, options.SectionName);
                if (sectionId == null)
                {
                    var error = $"Section not found: {options.NotebookName}/{options.SectionName}";
                    result.Errors.Add(error);
                    result.FailedPages = result.TotalFiles;
                    progress?.Report($"Error: {error}");
                    return result;
                }
            }

            await Task.Run(() =>
            {
                foreach (var filePath in options.FilePaths)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    try
                    {
                        ImportFile(filePath, sectionId, options, result, progress);
                    }
                    catch (Exception ex)
                    {
                        result.FailedPages++;
                        var error = $"Failed to import '{Path.GetFileName(filePath)}': {ex.Message}";
                        result.Errors.Add(error);
                        progress?.Report($"Error: {error}");
                    }
                }
            }, cancellationToken);

            return result;
        }

        private void ImportFile(
            string filePath,
            string? sectionId,
            ImportOptions options,
            ImportResult result,
            IProgress<string>? progress)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var basePath = Path.GetDirectoryName(filePath);

            if (!options.Quiet)
            {
                progress?.Report($"Importing: {fileName}");
            }

            var markdown = File.ReadAllText(filePath);

            var pageXml = _converter.Convert(
                markdown,
                pageTitle: null,
                collapsible: options.Collapsible,
                basePath: basePath
            );

            if (options.DryRun)
            {
                progress?.Report($"  [dry-run] Would create page: {fileName}");
                result.ImportedPages++;
                return;
            }

            var newPageId = _oneNoteService.CreatePage(sectionId!);

            var xmlWithId = pageXml.Replace("<one:Page ", $"<one:Page ID=\"{newPageId}\" ");

            _oneNoteService.UpdatePageContent(xmlWithId);
            result.ImportedPages++;

            if (options.Verbose)
            {
                progress?.Report($"  Created page: {fileName} (ID: {newPageId})");
            }
        }
    }
}
