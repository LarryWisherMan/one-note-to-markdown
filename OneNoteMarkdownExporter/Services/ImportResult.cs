using System.Collections.Generic;

namespace OneNoteMarkdownExporter.Services
{
    public class ImportResult
    {
        public int TotalFiles { get; set; }
        public int ImportedPages { get; set; }
        public int FailedPages { get; set; }
        public List<string> Errors { get; set; } = new();
        public bool Success => FailedPages == 0;
    }
}
