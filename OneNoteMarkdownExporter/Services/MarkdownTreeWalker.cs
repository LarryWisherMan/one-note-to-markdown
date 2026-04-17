using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OneNoteMarkdownExporter.Services;

/// <summary>
/// Enumerates .md files under a source root, returning stable relative
/// paths. Skips hidden directories (dotfiles) and non-.md files. Does
/// not follow symlinks.
/// </summary>
public class MarkdownTreeWalker
{
    public IEnumerable<string> Walk(string sourceRoot)
    {
        var fullRoot = Path.GetFullPath(sourceRoot);
        var results = new List<string>();
        WalkDirectory(fullRoot, fullRoot, results);
        results.Sort(System.StringComparer.Ordinal);
        return results;
    }

    private static void WalkDirectory(string rootFullPath, string currentDir, List<string> results)
    {
        foreach (var file in Directory.EnumerateFiles(currentDir))
        {
            if (!file.EndsWith(".md", System.StringComparison.OrdinalIgnoreCase)) continue;
            var fileName = Path.GetFileName(file);
            if (fileName.StartsWith('.')) continue;
            results.Add(Path.GetRelativePath(rootFullPath, file));
        }

        foreach (var dir in Directory.EnumerateDirectories(currentDir))
        {
            var dirName = Path.GetFileName(dir);
            if (dirName.StartsWith('.')) continue;
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) continue;
            WalkDirectory(rootFullPath, dir, results);
        }
    }
}
