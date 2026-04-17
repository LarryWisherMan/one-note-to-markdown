using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OneNoteMarkdownExporter.Models;

namespace OneNoteMarkdownExporter.Services
{
    /// <summary>
    /// Handles command-line interface parsing and execution.
    /// </summary>
    public static class CliHandler
    {
        /// <summary>
        /// Parses command-line arguments and runs in CLI mode.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Exit code (0 for success, non-zero for failure).</returns>
        public static async Task<int> RunAsync(string[] args)
        {
            var rootCommand = BuildRootCommand();
            return await rootCommand.InvokeAsync(args);
        }

        /// <summary>
        /// Checks if CLI mode should be activated based on arguments.
        /// </summary>
        public static bool ShouldRunCli(string[] args)
        {
            // If there are any command-line arguments, run in CLI mode
            // Exceptions: arguments that VS/Windows might pass when launching GUI
            if (args.Length == 0) return false;

            // Check for known CLI flags
            var cliFlags = new[]
            {
                "--all", "--notebook", "--section", "--page", "--output", "-o",
                "--overwrite", "--no-lint", "--lint-config",
                "--list", "--dry-run", "--verbose", "-v", "--quiet", "-q",
                "--import", "--file", "--no-collapse", "--publish",
                "--create-missing", "--no-create-missing",
                "--help", "-h", "-?", "--version"
            };

            return args.Any(arg => cliFlags.Any(flag => 
                arg.StartsWith(flag, StringComparison.OrdinalIgnoreCase)));
        }

        private static RootCommand BuildRootCommand()
        {
            var rootCommand = new RootCommand("OneNote to Markdown Exporter - Export OneNote pages to Markdown files.")
            {
                TreatUnmatchedTokensAsErrors = true
            };

            // Options
            var allOption = new Option<bool>(
                "--all",
                "Export all notebooks");

            var notebookOption = new Option<string[]>(
                "--notebook",
                "Export specific notebook(s) by name")
            {
                AllowMultipleArgumentsPerToken = false
            };

            var sectionOption = new Option<string[]>(
                "--section",
                "Export section(s) by path (e.g., 'Notebook/Section')")
            {
                AllowMultipleArgumentsPerToken = false
            };

            var pageOption = new Option<string[]>(
                "--page",
                "Export page(s) by ID")
            {
                AllowMultipleArgumentsPerToken = false
            };

            var outputOption = new Option<string>(
                aliases: new[] { "--output", "-o" },
                description: "Output directory for exported files",
                getDefaultValue: ExportOptions.GetDefaultOutputPath);

            var overwriteOption = new Option<bool>(
                "--overwrite",
                "Overwrite existing files instead of creating numbered copies");

            var noLintOption = new Option<bool>(
                "--no-lint",
                "Disable Markdown linting (markdownlint-cli)");

            var lintConfigOption = new Option<string?>(
                "--lint-config",
                "Path to custom markdownlint configuration file");

            var listOption = new Option<bool>(
                "--list",
                "List available notebooks, sections, and pages without exporting");

            var dryRunOption = new Option<bool>(
                "--dry-run",
                "Preview what would be exported without actually exporting");

            var verboseOption = new Option<bool>(
                aliases: new[] { "--verbose", "-v" },
                "Show detailed output");

            var quietOption = new Option<bool>(
                aliases: new[] { "--quiet", "-q" },
                "Show only errors");

            var importOption = new Option<string?>(
                "--import",
                "Import Markdown file(s) to OneNote. Specify target as 'Notebook/Section'.");

            var fileOption = new Option<string[]?>(
                "--file",
                "Markdown file(s) to import")
            {
                AllowMultipleArgumentsPerToken = true
            };

            var noCollapseOption = new Option<bool>(
                "--no-collapse",
                "Disable collapsible heading nesting for import");

            var publishOption = new Option<string?>(
                "--publish",
                "Walk a Markdown source tree and publish every opt-in file to OneNote.");

            var createMissingOption = new Option<bool>(
                "--create-missing",
                "Auto-create missing target sections and section groups.");

            var noCreateMissingOption = new Option<bool>(
                "--no-create-missing",
                "Disable auto-create of missing sections/section groups.");

            // Add options to command
            rootCommand.AddOption(allOption);
            rootCommand.AddOption(notebookOption);
            rootCommand.AddOption(sectionOption);
            rootCommand.AddOption(pageOption);
            rootCommand.AddOption(outputOption);
            rootCommand.AddOption(overwriteOption);
            rootCommand.AddOption(noLintOption);
            rootCommand.AddOption(lintConfigOption);
            rootCommand.AddOption(listOption);
            rootCommand.AddOption(dryRunOption);
            rootCommand.AddOption(verboseOption);
            rootCommand.AddOption(quietOption);
            rootCommand.AddOption(importOption);
            rootCommand.AddOption(fileOption);
            rootCommand.AddOption(noCollapseOption);
            rootCommand.AddOption(publishOption);
            rootCommand.AddOption(createMissingOption);
            rootCommand.AddOption(noCreateMissingOption);

            rootCommand.AddValidator(commandResult =>
            {
                var on = commandResult.GetValueForOption(createMissingOption);
                var off = commandResult.GetValueForOption(noCreateMissingOption);
                if (on && off)
                {
                    commandResult.ErrorMessage =
                        "--create-missing and --no-create-missing are mutually exclusive.";
                }
            });

            rootCommand.SetHandler(async (context) =>
            {
                var result = context.ParseResult;

                static bool ResolveCreateMissing(bool on, bool off, bool subcommandDefault)
                {
                    if (on) return true;
                    if (off) return false;
                    return subcommandDefault;
                }

                var publishSource = result.GetValueForOption(publishOption);
                if (!string.IsNullOrEmpty(publishSource))
                {
                    var notebooks = result.GetValueForOption(notebookOption);
                    var cliNotebook = notebooks is { Length: > 0 } ? notebooks[0] : null;

                    var createMissing = ResolveCreateMissing(
                        on: result.GetValueForOption(createMissingOption),
                        off: result.GetValueForOption(noCreateMissingOption),
                        subcommandDefault: true);

                    var exitCode = await ExecutePublishTreeAsync(
                        publishSource,
                        cliNotebook,
                        collapsible: !result.GetValueForOption(noCollapseOption),
                        dryRun: result.GetValueForOption(dryRunOption),
                        verbose: result.GetValueForOption(verboseOption),
                        quiet: result.GetValueForOption(quietOption),
                        createMissing: createMissing);
                    context.ExitCode = exitCode;
                    return;
                }

                var importTarget = result.GetValueForOption(importOption);
                var importFiles = result.GetValueForOption(fileOption);

                if (!string.IsNullOrEmpty(importTarget))
                {
                    var createMissing = ResolveCreateMissing(
                        on: result.GetValueForOption(createMissingOption),
                        off: result.GetValueForOption(noCreateMissingOption),
                        subcommandDefault: false);

                    var exitCode = await ExecuteImportAsync(
                        importTarget,
                        importFiles,
                        !result.GetValueForOption(noCollapseOption),
                        result.GetValueForOption(dryRunOption),
                        result.GetValueForOption(verboseOption),
                        result.GetValueForOption(quietOption),
                        createMissing: createMissing,
                        cancellationToken: context.GetCancellationToken());
                    context.ExitCode = exitCode;
                    return;
                }

                var options = new ExportOptions
                {
                    ExportAll = result.GetValueForOption(allOption),
                    NotebookNames = result.GetValueForOption(notebookOption)?.ToList(),
                    SectionPaths = result.GetValueForOption(sectionOption)?.ToList(),
                    PageIds = result.GetValueForOption(pageOption)?.ToList(),
                    OutputPath = result.GetValueForOption(outputOption) ?? ExportOptions.GetDefaultOutputPath(),
                    Overwrite = result.GetValueForOption(overwriteOption),
                    ApplyLinting = !result.GetValueForOption(noLintOption),
                    LintConfigPath = result.GetValueForOption(lintConfigOption),
                    DryRun = result.GetValueForOption(dryRunOption),
                    Verbose = result.GetValueForOption(verboseOption),
                    Quiet = result.GetValueForOption(quietOption)
                };

                var listMode = result.GetValueForOption(listOption);
                var exitCode2 = await ExecuteAsync(options, listMode, context.GetCancellationToken());
                context.ExitCode = exitCode2;
            });

            return rootCommand;
        }

        private static async Task<int> ExecuteAsync(ExportOptions options, bool listMode, CancellationToken cancellationToken)
        {
            var exportService = new ExportService();

            // Console progress reporter
            var progress = new Progress<string>(message =>
            {
                if (!options.Quiet || message.Contains("Error") || message.Contains("failed"))
                {
                    Console.WriteLine(message);
                }
            });

            try
            {
                // List mode - just show hierarchy
                if (listMode)
                {
                    return ListHierarchy(exportService, options.Verbose);
                }

                // Validate that we have selection criteria
                if (!options.HasSelectionCriteria())
                {
                    Console.Error.WriteLine("Error: No selection criteria specified.");
                    Console.Error.WriteLine("Use --all, --notebook, --section, or --page to specify what to export.");
                    Console.Error.WriteLine("Use --list to see available items.");
                    Console.Error.WriteLine("Use --help for more information.");
                    return 1;
                }

                // Report configuration
                if (!options.Quiet)
                {
                    Console.WriteLine("OneNote to Markdown Exporter");
                    Console.WriteLine("============================");
                    Console.WriteLine($"Output directory: {options.OutputPath}");
                    Console.WriteLine($"Overwrite: {(options.Overwrite ? "Yes" : "No")}");
                    Console.WriteLine($"Linting: {(options.ApplyLinting ? "Enabled (markdownlint-cli)" : "Disabled")}");
                    if (options.DryRun) Console.WriteLine("Mode: DRY RUN (no files will be created)");
                    Console.WriteLine();
                }

                // Run export
                var result = await exportService.ExportAsync(options, progress, cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    return 130; // Standard exit code for Ctrl+C
                }

                if (!string.IsNullOrEmpty(result.Error))
                {
                    Console.Error.WriteLine($"Export error: {result.Error}");
                    return 1;
                }

                // Summary
                if (!options.Quiet && !options.DryRun)
                {
                    Console.WriteLine();
                    Console.WriteLine("Export Summary:");
                    Console.WriteLine($"  Pages exported: {result.ExportedPages}");
                    if (result.FailedPages > 0)
                    {
                        Console.WriteLine($"  Pages failed: {result.FailedPages}");
                    }
                }

                return result.FailedPages > 0 ? 1 : 0;
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                Console.Error.WriteLine($"OneNote COM error: {ex.Message}");
                Console.Error.WriteLine("Make sure OneNote is installed and not running in a protected mode.");
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                if (options.Verbose)
                {
                    Console.Error.WriteLine(ex.StackTrace);
                }
                return 1;
            }
        }

        private static async Task<int> ExecutePublishTreeAsync(
            string sourceDir,
            string? cliNotebook,
            bool collapsible,
            bool dryRun,
            bool verbose,
            bool quiet,
            bool createMissing)
        {
            if (!Directory.Exists(sourceDir))
            {
                Console.Error.WriteLine($"Error: source directory not found: {sourceDir}");
                return 1;
            }

            var options = new PublishTreeOptions
            {
                SourceRoot = Path.GetFullPath(sourceDir),
                CliNotebook = cliNotebook,
                Collapsible = collapsible,
                DryRun = dryRun,
                Verbose = verbose,
                Quiet = quiet,
                CreateMissing = createMissing,
            };

            if (!quiet)
            {
                Console.WriteLine("OneNote Markdown Tree Publisher");
                Console.WriteLine("===============================");
                Console.WriteLine($"Source: {options.SourceRoot}");
                if (cliNotebook is not null) Console.WriteLine($"Notebook (CLI): {cliNotebook}");
                if (dryRun) Console.WriteLine("Mode: DRY RUN");
                Console.WriteLine();
            }

            try
            {
                var oneNoteService = new OneNoteService();
                var converter = new MarkdownToOneNoteXmlConverter();
                var service = new PublishTreeService(
                    new MarkdownTreeWalker(),
                    new FrontMatterParser(),
                    new OneNoteTargetResolver(),
                    new OneNoteTreePublisher(oneNoteService, converter));

                // Use a direct IProgress implementation instead of Progress<T>,
                // which marshals via SynchronizationContext and gets swallowed
                // when the WPF app runs in CLI mode.
                IProgress<string>? progress = quiet ? null : new DirectProgress(Console.WriteLine);

                var report = await service.PublishAsync(options, progress);

                foreach (var diag in report.Diagnostics)
                {
                    var shouldShow =
                        diag.Severity == DiagnosticSeverity.Error ||
                        diag.Severity == DiagnosticSeverity.Warning ||
                        (diag.Severity == DiagnosticSeverity.Info && verbose);
                    if (!shouldShow) continue;
                    var prefix = diag.Severity switch
                    {
                        DiagnosticSeverity.Error => "error",
                        DiagnosticSeverity.Warning => "warn",
                        _ => "info",
                    };
                    Console.WriteLine($"  [{prefix}] {diag.Message}");
                }

                Console.WriteLine();
                Console.WriteLine(report.RenderSummary());
                return report.Success ? 0 : 1;
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                Console.Error.WriteLine($"OneNote COM error: {ex.Message}");
                Console.Error.WriteLine("Make sure OneNote is installed and not running in a protected mode.");
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                if (verbose)
                {
                    Console.Error.WriteLine(ex.StackTrace);
                }
                return 1;
            }
        }

        private static async Task<int> ExecuteImportAsync(
            string importTarget,
            string[]? files,
            bool collapsible,
            bool dryRun,
            bool verbose,
            bool quiet,
            bool createMissing,
            CancellationToken cancellationToken)
        {
            var parts = importTarget.Split('/');
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            {
                Console.Error.WriteLine("Error: --import must be in format 'Notebook/Section'.");
                return 1;
            }

            if (files == null || files.Length == 0)
            {
                Console.Error.WriteLine("Error: --file is required with --import.");
                return 1;
            }

            var resolvedFiles = new List<string>();
            foreach (var file in files)
            {
                var fullPath = Path.GetFullPath(file);
                if (!File.Exists(fullPath))
                {
                    Console.Error.WriteLine($"Error: File not found: {file}");
                    return 1;
                }
                if (!fullPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine($"Warning: Skipping non-Markdown file: {file}");
                    continue;
                }
                resolvedFiles.Add(fullPath);
            }

            if (resolvedFiles.Count == 0)
            {
                Console.Error.WriteLine("Error: No valid Markdown files found.");
                return 1;
            }

            var options = new ImportOptions
            {
                NotebookName = parts[0].Trim(),
                SectionName = parts[1].Trim(),
                FilePaths = resolvedFiles,
                Collapsible = collapsible,
                DryRun = dryRun,
                Verbose = verbose,
                Quiet = quiet,
                CreateMissing = createMissing
            };

            var progress = new Progress<string>(message =>
            {
                if (!quiet || message.Contains("Error") || message.Contains("failed"))
                {
                    Console.WriteLine(message);
                }
            });

            try
            {
                if (!quiet)
                {
                    Console.WriteLine("OneNote Markdown Importer");
                    Console.WriteLine("========================");
                    Console.WriteLine($"Target: {options.NotebookName}/{options.SectionName}");
                    Console.WriteLine($"Files: {resolvedFiles.Count}");
                    Console.WriteLine($"Collapsible: {(collapsible ? "Yes" : "No")}");
                    if (dryRun) Console.WriteLine("Mode: DRY RUN");
                    Console.WriteLine();
                }

                var oneNoteService = new OneNoteService();
                var converter = new MarkdownToOneNoteXmlConverter();
                var importService = new ImportService(oneNoteService, converter);

                var result = await importService.ImportAsync(options, progress, cancellationToken);

                if (!quiet && !dryRun)
                {
                    Console.WriteLine();
                    Console.WriteLine("Import Summary:");
                    Console.WriteLine($"  Pages imported: {result.ImportedPages}");
                    if (result.FailedPages > 0)
                    {
                        Console.WriteLine($"  Pages failed: {result.FailedPages}");
                    }
                }

                return result.Success ? 0 : 1;
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                Console.Error.WriteLine($"OneNote COM error: {ex.Message}");
                Console.Error.WriteLine("Make sure OneNote is installed and not running in a protected mode.");
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                if (verbose)
                {
                    Console.Error.WriteLine(ex.StackTrace);
                }
                return 1;
            }
        }

        private static int ListHierarchy(ExportService exportService, bool verbose)
        {
            try
            {
                Console.WriteLine("OneNote Hierarchy");
                Console.WriteLine("=================");
                Console.WriteLine();

                var notebooks = exportService.GetNotebookHierarchy();

                if (notebooks.Count == 0)
                {
                    Console.WriteLine("No notebooks found.");
                    return 0;
                }

                foreach (var notebook in notebooks)
                {
                    PrintItem(notebook, "", verbose);
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error listing hierarchy: {ex.Message}");
                return 1;
            }
        }

        private static void PrintItem(OneNoteItem item, string indent, bool verbose)
        {
            var typeIcon = item.Type switch
            {
                OneNoteItemType.Notebook => "📓",
                OneNoteItemType.SectionGroup => "📁",
                OneNoteItemType.Section => "📄",
                OneNoteItemType.Page => "📝",
                _ => "❓"
            };

            var typeLabel = item.Type switch
            {
                OneNoteItemType.Notebook => "[Notebook]",
                OneNoteItemType.SectionGroup => "[SectionGroup]",
                OneNoteItemType.Section => "[Section]",
                OneNoteItemType.Page => "[Page]",
                _ => "[Unknown]"
            };

            if (verbose)
            {
                Console.WriteLine($"{indent}{typeIcon} {item.Name} {typeLabel}");
                Console.WriteLine($"{indent}   ID: {item.Id}");
            }
            else
            {
                Console.WriteLine($"{indent}{typeIcon} {item.Name}");
            }

            foreach (var child in item.Children)
            {
                PrintItem(child, indent + "  ", verbose);
            }
        }
    }

    /// <summary>
    /// IProgress that calls the handler directly on the current thread,
    /// avoiding the SynchronizationContext marshaling of <see cref="Progress{T}"/>
    /// which gets swallowed in WPF CLI mode.
    /// </summary>
    internal sealed class DirectProgress : IProgress<string>
    {
        private readonly Action<string> _handler;
        public DirectProgress(Action<string> handler) => _handler = handler;
        public void Report(string value) => _handler(value);
    }
}
