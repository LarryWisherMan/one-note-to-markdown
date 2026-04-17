using System;

namespace OneNoteMarkdownExporter.Services;

/// <summary>
/// Thrown when a resolved publish target references a notebook that does not
/// exist in OneNote. Notebook-level auto-create is tracked by issue #19.
/// </summary>
public class NotebookNotFoundException : Exception
{
    public NotebookNotFoundException(string notebookName)
        : base(
            $"Notebook not found: {notebookName}. " +
            "Notebook-level auto-create is not yet supported — " +
            "see https://github.com/LarryWisherMan/one-note-to-markdown/issues/19. " +
            "Create the notebook in OneNote and retry.")
    {
        NotebookName = notebookName;
    }

    public string NotebookName { get; }
}
