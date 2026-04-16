# Markdown → OneNote Importer

The importer is the reverse of the exporter: it takes a Markdown file and
creates a OneNote page in a target notebook/section using the same COM
interop (`UpdatePageContent`) that reads pages. The XML the converter emits
is shaped to mirror what OneNote produces when content is authored natively,
so the rendered page matches the visual style of hand-authored notes rather
than looking like an obvious import.

## Quick start

```powershell
# Import one Markdown file into a notebook/section
OneNoteMarkdownExporter.exe --import "MyNotebook/MySection" --file "notes.md"

# Import several files at once
OneNoteMarkdownExporter.exe --import "MyNotebook/MySection" `
    --file "notes.md" "appendix.md" "todo.md"

# Flat headings (no collapsible nesting under H2/H3)
OneNoteMarkdownExporter.exe --import "MyNotebook/MySection" --file "notes.md" --no-collapse

# Preview without writing to OneNote
OneNoteMarkdownExporter.exe --import "MyNotebook/MySection" --file "notes.md" --dry-run
```

The target notebook and section must already exist. `--import` always creates
a new page on each run — it does not overwrite.

## CLI flags

| Flag | Purpose |
|---|---|
| `--import <Notebook/Section>` | Target path. Must be exactly two segments separated by `/`. |
| `--file <path>...` | One or more Markdown files to import. |
| `--no-collapse` | Emit headings as siblings rather than nesting content inside a heading's `OEChildren`. |
| `--dry-run` | Parse and convert but do not call OneNote. |
| `--verbose` / `--quiet` | Standard logging flags. |

## Markdown → OneNote mapping

Everything on the rendered OneNote page is either the page `<Title>` or
an `<OE>` whose `quickStyleIndex` is `"1"` (the `p` QuickStyleDef).
Visual differentiation comes from inline `style` attributes on `<OE>`
or `<T>` and from `<span style='…'>` inside the CDATA. Only two
QuickStyleDefs are declared on the page: `PageTitle` and `p`.

| Markdown | OneNote emission |
|---|---|
| First `# H1` | Consumed as `<one:Title><one:OE quickStyleIndex="0"><one:T>…</one:T></one:OE></one:Title>`. Not duplicated into the body. |
| `## H2` | `<OE qSI="1"><T style="font-family:'Segoe UI';font-size:14.0pt;color:#201F1E"><![CDATA[<span style='font-weight:bold'>…</span>]]></T>[…OEChildren…]</OE>` |
| `### H3` | Same shape, size **12.0pt**. |
| `#### H4` | Same shape, size **11.0pt**. |
| `##### H5` / `###### H6` | Same shape, **11.0pt** + `font-style:italic` on the T style. |
| Paragraph | `<OE qSI="1" style="font-family:'Segoe UI';font-size:11.0pt"><T>…</T></OE>` |
| Bullet list item | Same as paragraph plus `<List><Bullet bullet="2" fontSize="11.0"/></List>`. |
| Numbered list item | Same plus `<List><Number numberSequence="0" numberFormat="##." fontSize="11.0" font="Segoe UI"/></List>`. |
| Nested list | Sub-list becomes an `<OEChildren>` inside the parent `<OE>`. |
| `**bold**` | `<span style='font-weight:bold'>…</span>` |
| `*italic*` | `<span style='font-style:italic'>…</span>` |
| `~~strike~~` | `<span style='text-decoration:line-through'>…</span>` |
| `` `inline code` `` | `<span style='font-family:Consolas;font-size:10.0pt'>…</span>` |
| `[text](url)` | `<a href="url">text</a>` |
| Fenced code block | Single-column `<Table bordersVisible="true" hasHeaderRow="true">`. Each line of code is its **own** `<OE>` inside the cell with `style="font-family:Consolas;font-size:9.0pt"`. |
| GFM table | `<Table bordersVisible="true" hasHeaderRow="true">` with one `<Column>` per markdown column. Header cells wrap text in `<span style='font-weight:bold'>`. Cell `<OE>`s carry `quickStyleIndex="1"`. |
| `> blockquote` | `<OE qSI="1" style="font-family:'Segoe UI';font-size:11.0pt;font-style:italic">…</OE>` — inline italic, no dedicated quote QuickStyleDef. |
| `---` (horizontal rule) | `<OE qSI="1" style="…"><T>---</T></OE>` — three literal dashes in body font. |
| Local `![alt](path/to/img.png)` | Inline `<Image><Data>base64…</Data></Image>` siblings emitted next to the paragraph. Paths resolve relative to the importing `.md` file. |
| Remote image URL | Rendered as placeholder text `(Image: <alt> - <url>)`. |
| Missing local image | Rendered as `(Image not found: <path>)` placeholder text. |

### Blank-line spacing

OneNote draws no vertical margin between OEs when the `p` QuickStyleDef has
`spaceBefore="0.0"`/`spaceAfter="0.0"`, which is the shape the reference
page uses. To get the same visual rhythm, the converter emits an empty OE
after every non-heading content block (paragraph, list, table, code block,
blockquote, HR). These render as blank lines. Remove them and everything
collapses into a wall of text; see the before/after in
`docs/reference-page/Results_1.png` vs `Results_2.png`.

### Collapsible headings

By default (`--import` without `--no-collapse`), content between headings
is nested inside the heading's `<OEChildren>`. This lets you collapse a
whole section in OneNote by clicking the caret on the heading. The
nesting is stack-based: a heading of level N pops any headings of level
≥ N off the stack before pushing itself.

## Known limitations

- **Only the first H1 becomes the page title.** Subsequent `# H1`s in
  the body render at H2 scale (14pt) since the page has one title slot.
- **No OneNote tag / checkbox roundtrip.** Markdown task lists
  (`- [ ]`, `- [x]`) render as plain bullets; OneNote to-do tags are not
  emitted.
- **Images must be local files.** Remote URLs are flagged in the page
  as placeholders rather than downloaded and embedded.
- **Fenced code blocks ignore the language tag.** The `csharp` in
  ` ```csharp ` is parsed but not used for syntax highlighting — OneNote
  has no native code renderer.
- **Tables assume uniform column widths.** The converter divides 600pt
  evenly across columns; there is no per-column sizing hint from
  Markdown to carry over.
- **GitVersion / release workflow collision.** The existing
  `.github/workflows/release.yml` tags releases with a naive
  patch-increment rule that will conflict with GitVersion once CI is
  re-enabled. See `CONTRIBUTING.md` for the reconciliation note.

## Tree publish (folder-tree → OneNote)

In addition to `--import` (single file / list), the CLI can walk an entire
Markdown source tree and publish every file that opts in:

```powershell
# Walk ./notes, publish each .md that has an `onenote:` front-matter key.
OneNoteMarkdownExporter.exe --publish ./notes

# Bulk mode: publish every .md under ./notes into "Work Notes".
OneNoteMarkdownExporter.exe --publish ./notes --notebook "Work Notes"

# Preview what would publish — no OneNote calls.
OneNoteMarkdownExporter.exe --publish ./notes --dry-run --verbose
```

The resolution rule (folder path + front-matter + CLI flag → target notebook /
section / page) is documented in detail in
`docs/superpowers/specs/2026-04-16-folder-tree-mapping-design.md`. Short version:

- **Folders** express hierarchy. `Work Notes/Architecture/overview.md` publishes to
  notebook `Work Notes`, section `Architecture`, page `overview`.
- **Dots in filename stems** also count as hierarchy. `backend.api.auth.md`
  resolves the same as `backend/api/auth.md`.
- **Front-matter** overrides folder inference per-field:

  ```yaml
  ---
  title: "My Page"
  onenote:
    notebook: "Work Notes"
    section: "Architecture"
    section_groups: ["Backend", "API"]
  ---
  ```

- **`onenote: true`** opts a file in when you want folder inference to do all the
  work. **`onenote: false`** explicitly excludes a file when using `--notebook`
  bulk mode.

Files without an `onenote:` key and without a `--notebook` flag are silently
skipped.

## Reference material

`docs/reference-page/` contains the goldens we tune the converter against:

| File | Purpose |
|---|---|
| `OneNote_VisualRef1.png` | Screenshot of the target rendering in OneNote. |
| `Reference-page.xml` | XML exported from that page via `GetPageContent` — the structural target. |
| `MarkDow_VisualRef1.md` | Markdown that, when converted and imported, should reproduce the target. Used by the `Convert_ReferenceMarkdown_MatchesReferenceShape` golden-file test. |
| `Results_1.png` / `Results_2.png` | Before/after screenshots during the spacer-OE work. |

`docs/reference-page/MarkDow_VisualRef1.converted.xml` is regenerated on
every test run by `DumpSampleXml_ForManualInspection` (and gitignored) —
open it alongside `Reference-page.xml` when you need to diff emission by
eye.
