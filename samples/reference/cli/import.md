---
title: "--import"
onenote:
  notebook: "{{Notebook}}"
---

# --import

Imports one or more Markdown files into an existing OneNote notebook
and section. This page demonstrates a **2-level** section-group
nesting: the folder path `reference/cli/` resolves to a section group
`reference` containing a section group `cli`, with this file landing
as a section named `import`.

## Synopsis

```
OneNoteMarkdownExporter.exe --import "Notebook/Section" --file notes.md
```

## Flags

- `--file <path>...` — one or more Markdown files to import.
- `--no-collapse` — emit headings as siblings rather than nesting.
- `--create-missing` — auto-create the target section if missing.
- `--dry-run` — preview without calling OneNote.

See the full CLI surface in `docs/importer.md`.
