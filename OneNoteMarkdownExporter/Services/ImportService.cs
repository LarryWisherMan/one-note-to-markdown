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

            // First: recursive lookup (legacy behavior — find the section
            // anywhere under the notebook, regardless of section-group depth).
            string? sectionId = options.DryRun
                ? null
                : _oneNoteService.FindSectionId(options.NotebookName, options.SectionName);

            // Second: if not found and --create-missing, fall through to the
            // path-based ensure (creates the section as a direct child of the
            // notebook).
            if (sectionId is null && options.CreateMissing)
            {
                try
                {
                    sectionId = _oneNoteService.EnsureSectionIdByPath(
                        options.NotebookName,
                        sectionGroups: Array.Empty<string>(),
                        options.SectionName,
                        createMissing: true,
                        dryRun: options.DryRun,
                        progress: progress);
                }
                catch (NotebookNotFoundException ex)
                {
                    result.Errors.Add(ex.Message);
                    result.FailedPages = result.TotalFiles;
                    progress?.Report($"Error: {ex.Message}");
                    return result;
                }
            }

            // Finally: error on miss (unless dry-run).
            if (sectionId is null && !options.DryRun)
            {
                var error = $"Section not found: {options.NotebookName}/{options.SectionName}. " +
                            "Pass --create-missing to create it automatically.";
                result.Errors.Add(error);
                result.FailedPages = result.TotalFiles;
                progress?.Report($"Error: {error}");
                return result;
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

            var newPageId = RetryComCall(() => _oneNoteService.CreatePage(sectionId!));

            // Insert page ID — the XML uses one: prefix from the converter
            var xmlWithId = pageXml.Replace("<one:Page ", $"<one:Page ID=\"{newPageId}\" ");

            RetryComCall(() => { _oneNoteService.UpdatePageContent(xmlWithId); return 0; });
            result.ImportedPages++;

            if (options.Verbose)
            {
                progress?.Report($"  Created page: {fileName} (ID: {newPageId})");
            }
        }

        /// <summary>
        /// Retries a COM call when OneNote returns RPC_E_SERVERCALL_RETRYLATER (0x8001010A),
        /// which indicates OneNote is temporarily busy.
        /// </summary>
        private static T RetryComCall<T>(Func<T> call, int maxAttempts = 5)
        {
            const int busyHResult = unchecked((int)0x8001010A);
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    return call();
                }
                catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == busyHResult && attempt < maxAttempts)
                {
                    System.Threading.Thread.Sleep(200 * attempt);
                }
            }
            return call();
        }
    }
}
