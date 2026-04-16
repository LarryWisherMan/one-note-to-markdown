# Spec: Fork one-note-to-markdown, Add Markdown Import

## Goal

Fork [segunak/one-note-to-markdown](https://github.com/segunak/one-note-to-markdown) and add the reverse direction: import Markdown files into OneNote as native pages. The existing repo exports OneNote pages to Markdown via COM Interop. This fork adds import so the tool handles both directions.

The use case: documentation authors write in Markdown (version controlled, portable, AI-friendly). The team consumes content in OneNote. This tool bridges that gap.

## Repo

- Fork from: `https://github.com/segunak/one-note-to-markdown`
- Language: C# / .NET / WPF
- License: MIT
- The existing codebase uses COM Interop (`Microsoft.Office.Interop.OneNote`) to connect to the OneNote desktop app. No Azure App Registration or Graph API required.

## Existing Architecture

The current repo has this structure:

```
OneNoteMarkdownExporter/
  Services/
    OneNoteXmlToMarkdownConverter.cs   # XML -> HTML -> Markdown
    OneNoteInteropService.cs           # COM Interop wrapper
    MarkdownLintService.cs             # Post-export linting
  ViewModels/                          # WPF GUI
  Models/
```

Key patterns already established:

- `GetPageContent()` returns OneNote page XML (the `one:` namespace)
- `OneNoteXmlToMarkdownConverter.cs` parses that XML into intermediate HTML, then converts to Markdown via ReverseMarkdown
- The XML uses the namespace `http://schemas.microsoft.com/office/onenote/2013/onenote`
- Content is structured as `Outline` > `OEChildren` > `OE` elements
- Text lives in `OE` > `T` elements wrapped in CDATA containing inline HTML
- Tables use `Table` > `Row` > `Cell` elements
- Lists use `OE` > `List` > `Bullet` or `Number` children
- Images use `Image` > `Data` (base64) elements
- Indentation/nesting uses nested `OEChildren` blocks

## What to Build

### New Service: `MarkdownToOneNoteXmlConverter.cs`

Location: `OneNoteMarkdownExporter/Services/MarkdownToOneNoteXmlConverter.cs`

This service converts a Markdown string into OneNote page XML suitable for `UpdatePageContent()`.

#### Dependencies to Add

- [Markdig](https://www.nuget.org/packages/Markdig) - Markdown parser for .NET. Produces an AST (abstract syntax tree) that can be walked to generate XML. Preferred over string-based conversion because it handles edge cases (nested lists, code blocks with special characters, tables with pipes in content).

#### Conversion Mapping

Each Markdown construct maps to a specific OneNote XML pattern:

**Headings** (`# H1`, `## H2`, `### H3`)

```xml
<one:OE style="font-family:Segoe UI;font-size:20.0pt;font-weight:bold">
  <one:T><![CDATA[Heading Text]]></one:T>
</one:OE>
```

Font sizes by level: H1 = 20pt, H2 = 16pt, H3 = 13pt, H4 = 12pt bold, H5 = 11pt bold, H6 = 11pt bold italic. The `quickStyleIndex` attribute can also be used if quick styles are defined in the page XML header.

**Paragraphs**

```xml
<one:OE>
  <one:T><![CDATA[Paragraph text with <b>bold</b> and <i>italic</i>]]></one:T>
</one:OE>
```

Inline formatting (bold, italic, strikethrough, inline code) stays as HTML inside the CDATA. OneNote renders it.

**Inline code** (backticks)

```xml
<one:T><![CDATA[Use the <span style='font-family:Consolas;font-size:9pt'>command</span> here]]></one:T>
```

**Fenced code blocks**

```xml
<one:OE>
  <one:T><![CDATA[<span style='font-family:Consolas;font-size:9pt'>line 1<br/>line 2<br/>line 3</span>]]></one:T>
</one:OE>
```

Alternatively, wrap in a single-cell table for visual separation:

```xml
<one:Table bordersVisible="true">
  <one:Columns><one:Column index="0" width="600"/></one:Columns>
  <one:Row>
    <one:Cell>
      <one:OEChildren>
        <one:OE>
          <one:T><![CDATA[<span style='font-family:Consolas;font-size:9pt'>code content</span>]]></one:T>
        </one:OE>
      </one:OEChildren>
    </one:Cell>
  </one:Row>
</one:Table>
```

Use the table approach. It matches how we handle code blocks in the HTML converter and gives a clean visual boundary.

**Bullet lists**

```xml
<one:OE>
  <one:List><one:Bullet bullet="2" fontSize="11.0"/></one:List>
  <one:T><![CDATA[List item text]]></one:T>
</one:OE>
```

**Numbered lists**

```xml
<one:OE>
  <one:List><one:Number numberSequence="0" fontSize="11.0"/></one:List>
  <one:T><![CDATA[Numbered item text]]></one:T>
</one:OE>
```

**Nested lists**

Nested items go inside a child `OEChildren` under the parent `OE`:

```xml
<one:OE>
  <one:List><one:Bullet bullet="2" fontSize="11.0"/></one:List>
  <one:T><![CDATA[Parent item]]></one:T>
  <one:OEChildren>
    <one:OE>
      <one:List><one:Bullet bullet="2" fontSize="11.0"/></one:List>
      <one:T><![CDATA[Child item]]></one:T>
    </one:OE>
  </one:OEChildren>
</one:OE>
```

**Tables**

```xml
<one:Table bordersVisible="true">
  <one:Columns>
    <one:Column index="0" width="200"/>
    <one:Column index="1" width="200"/>
  </one:Columns>
  <one:Row>
    <one:Cell>
      <one:OEChildren>
        <one:OE><one:T><![CDATA[<b>Header 1</b>]]></one:T></one:OE>
      </one:OEChildren>
    </one:Cell>
    <one:Cell>
      <one:OEChildren>
        <one:OE><one:T><![CDATA[<b>Header 2</b>]]></one:T></one:OE>
      </one:OEChildren>
    </one:Cell>
  </one:Row>
  <one:Row>
    <one:Cell>
      <one:OEChildren>
        <one:OE><one:T><![CDATA[Cell 1]]></one:T></one:OE>
      </one:OEChildren>
    </one:Cell>
    <one:Cell>
      <one:OEChildren>
        <one:OE><one:T><![CDATA[Cell 2]]></one:T></one:OE>
      </one:OEChildren>
    </one:Cell>
  </one:Row>
</one:Table>
```

Header row cells use `<b>` in their CDATA to render bold. Calculate column widths by dividing available page width evenly across columns, or estimate based on content length.

**Blockquotes**

Render as indented content using nested `OEChildren`:

```xml
<one:OEChildren>
  <one:OE>
    <one:T><![CDATA[<i>Quoted text</i>]]></one:T>
  </one:OE>
</one:OEChildren>
```

**Horizontal rules**

Insert an empty `OE` with a line of dashes or simply skip (OneNote has no native HR).

**Links**

```xml
<one:T><![CDATA[<a href="https://example.com">Link text</a>]]></one:T>
```

**Images**

For images referenced by URL:

```xml
<one:Image>
  <one:Size width="400" height="300"/>
  <one:Data>BASE64_ENCODED_IMAGE_DATA</one:Data>
</one:Image>
```

For local file references, read the file, base64 encode it, and embed it. For remote URLs, download the image, base64 encode, and embed. If download fails, insert a link to the image instead.

**Collapsible sections (indentation)**

Content under a heading should be nested in `OEChildren` so OneNote treats it as collapsible:

```xml
<!-- H2 heading -->
<one:OE style="font-family:Segoe UI;font-size:16.0pt;font-weight:bold">
  <one:T><![CDATA[Section Heading]]></one:T>
  <one:OEChildren>
    <!-- All content under this heading, indented -->
    <one:OE>
      <one:T><![CDATA[Paragraph under the heading]]></one:T>
    </one:OE>
    <!-- H3 sub-heading with its own nested content -->
    <one:OE style="font-family:Segoe UI;font-size:13.0pt;font-weight:bold">
      <one:T><![CDATA[Sub Heading]]></one:T>
      <one:OEChildren>
        <one:OE>
          <one:T><![CDATA[Content under sub-heading]]></one:T>
        </one:OE>
      </one:OEChildren>
    </one:OE>
  </one:OEChildren>
</one:OE>
```

This nesting is what produces the collapsible toggle behavior in OneNote. The heading `OE` contains an `OEChildren` block with all content that belongs under it.

#### Full Page XML Structure

The converter must produce a complete page XML document:

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

When creating a new page, omit the `ID` attribute. OneNote assigns one. When updating an existing page, include the `ID` from a prior `GetPageContent()` call.

#### Method Signature

```csharp
public class MarkdownToOneNoteXmlConverter
{
    /// <summary>
    /// Convert a Markdown string to OneNote page XML.
    /// </summary>
    /// <param name="markdown">The Markdown content.</param>
    /// <param name="pageTitle">Page title. If null, extracted from first H1.</param>
    /// <param name="existingPageId">If updating, the page ID. Null for new pages.</param>
    /// <param name="collapsible">Nest content under headings for collapsible sections.</param>
    /// <returns>OneNote page XML string suitable for UpdatePageContent().</returns>
    public string Convert(
        string markdown,
        string? pageTitle = null,
        string? existingPageId = null,
        bool collapsible = true
    );
}
```

### Extend OneNoteInteropService

Add methods for pushing content back to OneNote:

```csharp
/// <summary>
/// Create a new page in the specified section.
/// </summary>
public string CreatePage(string sectionId, string pageXml);

/// <summary>
/// Update an existing page's content.
/// </summary>
public void UpdatePage(string pageXml);

/// <summary>
/// Find a section by notebook name and section name.
/// </summary>
public string? FindSectionId(string notebookName, string sectionName);

/// <summary>
/// Find a page by name within a section.
/// </summary>
public string? FindPageId(string sectionId, string pageName);
```

The COM Interop calls:

```csharp
// Create a new page in a section
onenote.CreateNewPage(sectionId, out string newPageId, NewPageStyle.OneNoteDefault);

// Push content to a page (new or existing)
onenote.UpdatePageContent(pageXml, DateTime.MinValue, XMLSchema.xs2013);
```

### CLI Interface

Add an `--import` command to the existing CLI:

```
# Import a Markdown file as a new page
OneNoteMarkdownExporter.exe --import "Notebook/Section" --file notes.md

# Import and update an existing page (matches by title)
OneNoteMarkdownExporter.exe --import "Notebook/Section" --file notes.md --update

# Import multiple files
OneNoteMarkdownExporter.exe --import "Notebook/Section" --file *.md

# Import without collapsible sections
OneNoteMarkdownExporter.exe --import "Notebook/Section" --file notes.md --no-collapse

# Dry run (show what would happen)
OneNoteMarkdownExporter.exe --import "Notebook/Section" --file notes.md --dry-run
```

#### New CLI Parameters

| Option                | Description                                                              |
| --------------------- | ------------------------------------------------------------------------ |
| `--import <path>`     | Target notebook/section path (e.g., "Work Notes/Server")                 |
| `--file <path>`       | Markdown file(s) to import. Supports glob patterns.                      |
| `--update`            | Update existing page if title matches. Without this, always creates new. |
| `--no-collapse`       | Do not nest content under headings for collapsible sections.             |
| `--page-title <text>` | Override page title (default: first H1 or filename).                     |

### GUI Changes

Add an "Import" tab or button to the WPF interface:

1. A file picker for selecting one or more `.md` files.
2. A tree view for selecting the target notebook/section (reuse the existing tree view component).
3. A checkbox for "Collapsible sections" (default: on).
4. A checkbox for "Update existing pages" (default: off).
5. An "Import" button.

This is lower priority than the CLI. The CLI is the primary interface for a write-in-markdown workflow.

## Configuration

### Fonts

Make fonts configurable via a settings file or CLI flags:

```json
{
  "bodyFont": "Segoe UI",
  "bodyFontSize": "11.0",
  "codeFont": "Consolas",
  "codeFontSize": "9.0",
  "headingFont": "Segoe UI"
}
```

Default to these values. Store in a `settings.json` next to the executable.

### Code Block Style

Support two modes via config:

- `"codeBlockStyle": "table"` (default) - code in a bordered single-cell table with Consolas
- `"codeBlockStyle": "inline"` - code as styled text without a table wrapper

## Implementation Order

1. **Add Markdig NuGet package** to the project.
2. **Build `MarkdownToOneNoteXmlConverter.cs`** with the conversion logic. Start with paragraphs, headings, bold/italic, then add tables, lists, code blocks, images.
3. **Add import methods to `OneNoteInteropService.cs`** (`CreatePage`, `UpdatePage`, `FindSectionId`, `FindPageId`).
4. **Add CLI import command** to the existing argument parser.
5. **Write tests** mirroring the existing test project structure. Test the XML output for each Markdown construct against expected patterns.
6. **Add GUI import tab** (lower priority).

## Testing Strategy

### Unit Tests

Test `MarkdownToOneNoteXmlConverter` in isolation:

- Input: Markdown string
- Output: Parse the XML, assert structure

Test cases:

- Simple paragraph
- Heading levels 1-6
- Bold, italic, strikethrough, inline code
- Bullet list (flat and nested)
- Numbered list (flat and nested)
- Table with header row
- Fenced code block (with and without language tag)
- Link (inline and reference style)
- Image (local file reference)
- Mixed content (heading, paragraph, list, code block, table in sequence)
- Collapsible nesting (verify OEChildren hierarchy)
- Empty document
- Document with only a title

### Integration Tests

- Create a page in a test notebook via COM Interop, then read it back with `GetPageContent()` and verify the content rendered.
- Round-trip test: export an existing page to Markdown, import it back, compare.

## OneNote XML Reference

The full XML schema is documented at:

- [MS-ONE: OneNote File Format](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-one/73d22548-a613-4350-8c23-07d15576be50)
- [OneNote COM API: UpdatePageContent](https://learn.microsoft.com/en-us/office/client-developer/onenote/application-interface-onenote#updatepagecontent-method)
- [OneNote COM API: Page Content Schema](https://learn.microsoft.com/en-us/office/client-developer/onenote/onenote-developer-reference)

The existing `OneNoteXmlToMarkdownConverter.cs` in the repo is the best reference for the XML patterns OneNote actually produces. Build the reverse mappings from those patterns.

## Out of Scope (Future Work)

- **Graph API support** - push to OneNote Online via Microsoft Graph instead of COM Interop. Would remove the requirement for the OneNote desktop app but requires Azure App Registration.
- **File watcher / auto-sync** - watch a folder of Markdown files and automatically push changes to OneNote.
- **Bidirectional sync** - detect changes on both sides and merge. Complex conflict resolution required.
- **Mermaid diagram rendering** - render Mermaid code blocks as images before import.
