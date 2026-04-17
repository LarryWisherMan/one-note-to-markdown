---
title: "--publish"
onenote:
  notebook: "{{Notebook}}"
---

# --publish

Walks a Markdown source tree and publishes every file with `onenote:`
front-matter (or every file when `--notebook` bulk mode is used) to
OneNote.

## Synopsis

```
OneNoteMarkdownExporter.exe --publish ./notes [--dry-run] [--notebook "Target"]
```

## Flags

- `--dry-run` — preview without writing to OneNote.
- `--notebook <name>` — bulk mode: publish every `.md` into this
  notebook regardless of front-matter (front-matter still wins
  per-file if present).
- `--no-create-missing` — disable the default auto-create behavior.

## Resolution rule

The full resolution rule (folder path + front-matter + CLI flag →
target notebook / section / page) is documented in
`docs/superpowers/specs/2026-04-16-folder-tree-mapping-design.md`.
