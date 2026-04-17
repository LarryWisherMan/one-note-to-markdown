---
title: "Collapsible Sections"
onenote:
  notebook: "{{Notebook}}"
---

# Project Documentation

This is the top-level introduction. Everything below should nest under its heading.

## Getting Started

Follow these steps to set up the project.

1. Clone the repository
2. Run `dotnet restore`
3. Run `dotnet build`

### Prerequisites

- .NET 10.0 SDK
- Windows 10 or 11
- OneNote desktop app

### Configuration

No configuration needed for basic usage. The defaults are:

| Setting | Default |
|---------|---------|
| Body font | Segoe UI 11pt |
| Code font | Consolas 9pt |
| Collapsible | Enabled |

## API Reference

### ImportService

The `ImportService` class orchestrates the import flow.

```csharp
var service = new ImportService(oneNoteService, converter);
var result = await service.ImportAsync(options, progress, token);
```

### MarkdownToOneNoteXmlConverter

Converts Markdown to OneNote page XML.

- Input: Markdown string
- Output: OneNote XML string
- Supports: headings, lists, tables, code blocks, images

## Troubleshooting

> If OneNote is not responding, try restarting the desktop app.

Common issues:

1. **COM error** - OneNote must be running
2. **Section not found** - Check notebook/section names are correct
3. **DLP blocked** - This tool bypasses DLP by using `GetPageContent` directly
