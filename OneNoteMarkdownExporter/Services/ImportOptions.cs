using System.Collections.Generic;

namespace OneNoteMarkdownExporter.Services
{
    public class ImportOptions
    {
        public string NotebookName { get; set; } = string.Empty;
        public string SectionName { get; set; } = string.Empty;
        public List<string> FilePaths { get; set; } = new();
        public bool Collapsible { get; set; } = true;
        public bool DryRun { get; set; } = false;
        public bool Verbose { get; set; } = false;
        public bool Quiet { get; set; } = false;

        /// <summary>
        /// When true and the target section does not exist, create missing
        /// section groups and the leaf section before importing. Default false —
        /// <c>--import</c> is surgical; a missing target is usually a typo.
        /// </summary>
        public bool CreateMissing { get; set; } = false;
    }
}
