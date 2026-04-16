# Design: Markdown Import to OneNote (v1)

## Overview

Add a CLI command to publish Markdown files as new OneNote pages. Markdown is the source of truth; OneNote is the read-only output. This is a one-way publish workflow — no update/sync, no GUI.

## Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Use case | One-way publish | Markdown is source of truth, OneNote is consumption layer |
| Target structure | Flat with explicit path | `--import "Notebook/Section"` specifies where pages land |
| Update behavior | Always create new | No page matching or overwriting in v1 |
| GUI | CLI only | Workflow is script/CI-friendly; GUI deferred |
| Images | Local only | Resolve relative paths, base64 embed. No remote downloads. |
| Configuration | Hardcoded defaults | Segoe UI 11pt body, Consolas 9pt code. No settings file. |
| Converter approach | Markdig AST walker | Clean node-to-XML mapping, handles nesting correctly |

## Out of Scope (v1)

- GUI import tab
- Page update/matching (always creates new)
- Remote image downloads
- Font/code-block configuration file
- Frontmatter-based targeting
- File watcher / auto-sync
- Graph API support

## New Files

| File | Purpose |
|------|---------|
| `Services/MarkdownToOneNoteXmlConverter.cs` | Markdig AST to OneNote XML converter |
| `Services/ImportService.cs` | Import orchestration (analogous to ExportService) |
| `Services/ImportOptions.cs` | Import configuration model |
| `Services/ImportResult.cs` | Import result model |

## Modified Files

| File | Change |
|------|--------|
| `Services/OneNoteService.cs` | Add `CreatePage`, `FindSectionId` methods |
| `Services/CliHandler.cs` | Add `--import`, `--file`, `--no-collapse` options |
| `OneNoteMarkdownExporter.csproj` | Add Markdig NuGet package |

## Component Design

### MarkdownToOneNoteXmlConverter

**Location:** `OneNoteMarkdownExporter/Services/MarkdownToOneNoteXmlConverter.cs`

**Dependency:** [Markdig](https://www.nuget.org/packages/Markdig) NuGet package

```csharp
public class MarkdownToOneNoteXmlConverter
{
    public string Convert(
        string markdown,
        string? pageTitle = null,
        bool collapsible = true,
        string? basePath = null
    );
}
```

- `pageTitle`: If null, extracted from first H1. If no H1, caller provides filename-derived title.
- `collapsible`: When true, content between headings nests inside the preceding heading's `OEChildren`, producing collapsible sections in OneNote. Stack-based algorithm: push headings, pop when a same-or-higher level heading appears.
- `basePath`: Directory of the source `.md` file. Used to resolve relative image paths.

#### AST Node Mapping

| Markdig AST Node | OneNote XML Output |
|------------------|--------------------|
| `HeadingBlock` | `OE` with font-size styling (H1=20pt, H2=16pt, H3=13pt, H4=12pt bold, H5=11pt bold, H6=11pt bold italic). If collapsible, wraps following content in `OEChildren`. |
| `ParagraphBlock` | `OE > T` with inline HTML in CDATA |
| `EmphasisInline` (single) | `<i>text</i>` in CDATA |
| `EmphasisInline` (double) | `<b>text</b>` in CDATA |
| `StrikethroughInline` | `<del>text</del>` in CDATA |
| `CodeInline` | `<span style='font-family:Consolas;font-size:9pt'>text</span>` in CDATA |
| `FencedCodeBlock` | Single-cell bordered `Table` with Consolas 9pt styling. Lines joined with `<br/>`. |
| `ListBlock` (bullet) | `OE` with `List > Bullet bullet="2" fontSize="11.0"` |
| `ListBlock` (ordered) | `OE` with `List > Number numberSequence="0" fontSize="11.0"` |
| Nested `ListBlock` | Child items inside parent `OE > OEChildren` |
| `Table` | `Table bordersVisible="true"` with `Columns`, `Row`, `Cell` elements. Header row cells use `<b>` in CDATA. Column widths divided evenly. |
| `QuoteBlock` | Indented `OEChildren` with `<i>` styling |
| `ThematicBreakBlock` | `OE` with a line of dashes |
| `LinkInline` | `<a href="url">text</a>` in CDATA |
| `ImageInline` | See Image Handling section |
| Unrecognized nodes | Fall back to plain text rendering via Markdig. Never crash, never drop content. |

#### Full Page XML Envelope

```xml
<?xml version="1.0" encoding="utf-8"?>
<one:Page xmlns:one="http://schemas.microsoft.com/office/onenote/2013/onenote"
          ID="{page-id}"
          name="Page Title">
  <one:Title>
    <one:OE>
      <one:T><![CDATA[Page Title]]></one:T>
    </one:OE>
  </one:Title>
  <one:Outline>
    <one:OEChildren>
      <!-- All converted content goes here -->
    </one:OEChildren>
  </one:Outline>
</one:Page>
```

### Image Handling

When the AST walker encounters an `ImageInline` node:

1. If `basePath` is null or the path is not a local file reference — emit alt text as plain text with URL in parentheses
2. Resolve the relative path against `basePath`
3. If file exists — read bytes, base64 encode, emit `one:Image > one:Data`
4. If file doesn't exist — emit placeholder: `[Image not found: path]`

Supported formats: PNG, JPG, GIF, BMP. No format conversion. No explicit `one:Size` — let OneNote auto-size.

### OneNoteService Changes

**File:** `OneNoteMarkdownExporter/Services/OneNoteService.cs`

`UpdatePageContent(string xml)` already exists. Add two methods:

```csharp
/// Create a new page in a section. Returns the new page ID.
public string CreatePage(string sectionId);

/// Find a section ID by navigating "Notebook/Section" path.
/// Returns null if not found. Case-insensitive match.
public string? FindSectionId(string notebookName, string sectionName);
```

- `CreatePage` calls `onenote.CreateNewPage(sectionId, out string newPageId)`, returns `newPageId`.
- `FindSectionId` calls `GetHierarchy()`, walks the XML for matching notebook then section by name.

### ImportService

**New file:** `OneNoteMarkdownExporter/Services/ImportService.cs`

Orchestrates the import flow — analogous to `ExportService`.

```csharp
public class ImportService
{
    public ImportService(OneNoteService oneNoteService, MarkdownToOneNoteXmlConverter converter);

    public Task<ImportResult> ImportAsync(
        ImportOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken
    );
}
```

#### ImportOptions

```csharp
public class ImportOptions
{
    public string NotebookName { get; set; }
    public string SectionName { get; set; }
    public List<string> FilePaths { get; set; }
    public bool Collapsible { get; set; } = true;
    public bool DryRun { get; set; } = false;
    public bool Verbose { get; set; } = false;
    public bool Quiet { get; set; } = false;
}
```

#### ImportResult

```csharp
public class ImportResult
{
    public int TotalFiles { get; set; }
    public int ImportedPages { get; set; }
    public int FailedPages { get; set; }
    public List<string> Errors { get; set; }
    public bool Success => FailedPages == 0;
}
```

#### Import Flow

**Once, before processing files:**

1. Call `oneNoteService.FindSectionId(notebookName, sectionName)` — fail with clear error if not found

**Per file:**

2. Read `.md` file from disk
3. Derive page title: first H1, fall back to filename (without extension)
4. Call `converter.Convert(markdown, pageTitle, collapsible, basePath: directory of .md file)`
5. Call `oneNoteService.CreatePage(sectionId)` — get new page ID
6. Insert page ID into the XML
7. Call `oneNoteService.UpdatePageContent(xml)`
8. Report progress

**Dry run:** Steps 2-4 execute normally (validates Markdown parses and converts). Steps 1, 5-7 are skipped. Reports what would have been created.

### CLI Changes

**File:** `OneNoteMarkdownExporter/Services/CliHandler.cs`

New options added to the existing `RootCommand`:

| Option | Type | Description |
|--------|------|-------------|
| `--import <path>` | `string` | Target as `"Notebook/Section"` |
| `--file <paths>` | `string[]` | Markdown file(s) to import |
| `--no-collapse` | `bool` | Disable collapsible heading nesting |

Reuses existing options: `--dry-run`, `--verbose`, `-v`, `--quiet`, `-q`

**Routing logic** when `--import` is present:

1. Validate `--import` contains exactly one `/` separator
2. Split into notebook name and section name
3. Validate `--file` is provided and resolves to at least one existing `.md` file
4. Build `ImportOptions`, create `ImportService`, call `ImportAsync`
5. Report results (respecting `--verbose`/`--quiet`)

**`ShouldRunCli` update:** Add `--import` and `--file` to the set of flags that trigger CLI mode.

**Example usage:**

```
OneNoteMarkdownExporter.exe --import "Work Notes/Docs" --file notes.md
OneNoteMarkdownExporter.exe --import "Work Notes/Docs" --file *.md --dry-run
OneNoteMarkdownExporter.exe --import "Work Notes/Docs" --file doc.md --no-collapse -v
```

## Testing Strategy

### Unit Tests: MarkdownToOneNoteXmlConverter

**New file:** `OneNoteMarkdownExporter.Tests/Converters/MarkdownToOneNoteXmlConverterTests.cs`

Each test: Markdown string in, parse output XML, assert structure.

Test cases:
- Empty document
- Single paragraph
- Heading levels 1-6 (verify font sizes and bold/italic styling)
- Inline formatting: bold, italic, strikethrough, inline code
- Fenced code block (verify bordered table wrapping, Consolas font)
- Bullet list (flat)
- Numbered list (flat)
- Nested lists (2+ levels deep)
- Table with header row (verify bold headers, column count)
- Links
- Blockquotes (verify indentation via OEChildren)
- Horizontal rule
- Local image with valid file (verify base64 Data element)
- Local image with missing file (verify placeholder text)
- Image with no basePath (verify graceful fallback)
- Collapsible nesting: H1 > content > H2 > content (verify OEChildren hierarchy)
- Collapsible disabled: same input, flat structure
- Page title from H1
- Page title from parameter overriding H1
- Mixed content (heading, paragraph, list, code, table in sequence)

### Unit Tests: ImportService

**New file:** `OneNoteMarkdownExporter.Tests/Services/ImportServiceTests.cs`

- `FindSectionId` — mock hierarchy XML, verify correct ID returned, verify null on not-found
- `ImportAsync` — mock OneNoteService and converter, verify orchestration flow
- Dry run skips COM calls
- Progress reported correctly
- Errors collected per file

### CLI Tests

**Extend:** `OneNoteMarkdownExporter.Tests/Cli/CliHandlerTests.cs`

- `ShouldRunCli_WithImportFlag_ReturnsTrue`
- `ShouldRunCli_WithFileFlag_ReturnsTrue`
- `ShouldRunCli_WithNoCollapseFlag_ReturnsTrue`

### No Integration Tests in v1

Integration tests require OneNote desktop running. The converter is pure logic, fully testable in isolation.

## OneNote XML Reference

- [MS-ONE: OneNote File Format](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-one/73d22548-a613-4350-8c23-07d15576be50)
- [OneNote COM API: UpdatePageContent](https://learn.microsoft.com/en-us/office/client-developer/onenote/application-interface-onenote#updatepagecontent-method)
- The existing `OneNoteXmlToMarkdownConverter.cs` in this repo is the best reference for XML patterns OneNote actually produces. Build reverse mappings from those patterns.
