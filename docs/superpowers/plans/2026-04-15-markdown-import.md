# Markdown Import to OneNote Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a CLI command (`--import "Notebook/Section" --file *.md`) that publishes Markdown files as new OneNote pages via COM Interop.

**Architecture:** Parse Markdown with Markdig into an AST, walk the tree to emit OneNote page XML, push via `CreateNewPage` + `UpdatePageContent` COM calls. New `ImportService` orchestrates the flow, mirroring the existing `ExportService` pattern.

**Tech Stack:** C# / .NET 10.0 / Markdig / System.CommandLine / OneNote COM Interop / xUnit + FluentAssertions

**Spec:** `docs/superpowers/specs/2026-04-15-markdown-import-design.md`

---

### Task 1: Add Markdig NuGet Package

**Files:**
- Modify: `OneNoteMarkdownExporter/OneNoteMarkdownExporter.csproj:22-27`

- [ ] **Step 1: Add Markdig package reference**

Add after the existing `PackageReference` items in the `<ItemGroup>` at line 22:

```xml
<PackageReference Include="Markdig" Version="0.38.0" />
```

The full `<ItemGroup>` should now be:

```xml
<ItemGroup>
  <PackageReference Include="HtmlAgilityPack" Version="1.12.4" />
  <PackageReference Include="Markdig" Version="0.38.0" />
  <PackageReference Include="ReverseMarkdown" Version="4.7.1" />
  <PackageReference Include="System.CommandLine" Version="2.0.0-beta4.22272.1" />
  <PackageReference Include="Interop.Microsoft.Office.Interop.OneNote" Version="1.1.0.2" />
</ItemGroup>
```

- [ ] **Step 2: Restore and verify build**

Run: `dotnet restore && dotnet build`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Commit**

```bash
git add OneNoteMarkdownExporter/OneNoteMarkdownExporter.csproj
git commit -m "chore: add Markdig NuGet package for Markdown parsing"
```

---

### Task 2: MarkdownToOneNoteXmlConverter — Paragraphs and Headings

**Files:**
- Create: `OneNoteMarkdownExporter/Services/MarkdownToOneNoteXmlConverter.cs`
- Create: `OneNoteMarkdownExporter.Tests/Converters/MarkdownToOneNoteXmlConverterTests.cs`

- [ ] **Step 1: Write failing tests for empty document and single paragraph**

Create `OneNoteMarkdownExporter.Tests/Converters/MarkdownToOneNoteXmlConverterTests.cs`:

```csharp
using System.Xml.Linq;
using FluentAssertions;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Converters;

public class MarkdownToOneNoteXmlConverterTests
{
    private readonly MarkdownToOneNoteXmlConverter _converter;
    private static readonly XNamespace OneNs = "http://schemas.microsoft.com/office/onenote/2013/onenote";

    public MarkdownToOneNoteXmlConverterTests()
    {
        _converter = new MarkdownToOneNoteXmlConverter();
    }

    private XDocument ParseResult(string xml) => XDocument.Parse(xml);

    #region Page Structure Tests

    [Fact]
    public void Convert_EmptyDocument_ReturnsValidPageXml()
    {
        var result = _converter.Convert("", pageTitle: "Empty Page");
        var doc = ParseResult(result);

        doc.Root.Should().NotBeNull();
        doc.Root!.Name.Should().Be(OneNs + "Page");
        doc.Root.Attribute("name")!.Value.Should().Be("Empty Page");
        doc.Descendants(OneNs + "Title").Should().HaveCount(1);
    }

    [Fact]
    public void Convert_SingleParagraph_CreatesOeWithText()
    {
        var result = _converter.Convert("Hello World", pageTitle: "Test");
        var doc = ParseResult(result);

        var oeChildren = doc.Descendants(OneNs + "Outline")
            .Descendants(OneNs + "OEChildren").First();
        var textElements = oeChildren.Descendants(OneNs + "T");
        textElements.Should().Contain(t => t.Value.Contains("Hello World"));
    }

    #endregion

    #region Heading Tests

    [Theory]
    [InlineData("# Heading 1", "20.0pt")]
    [InlineData("## Heading 2", "16.0pt")]
    [InlineData("### Heading 3", "13.0pt")]
    public void Convert_Heading_AppliesCorrectFontSize(string markdown, string expectedSize)
    {
        var result = _converter.Convert(markdown, pageTitle: "Test");
        var doc = ParseResult(result);

        var oes = doc.Descendants(OneNs + "OE")
            .Where(oe => oe.Attribute("style")?.Value.Contains(expectedSize) == true);
        oes.Should().NotBeEmpty();
    }

    [Fact]
    public void Convert_PageTitleFromH1_WhenNoTitleProvided()
    {
        var result = _converter.Convert("# My Title\n\nSome content");
        var doc = ParseResult(result);

        doc.Root!.Attribute("name")!.Value.Should().Be("My Title");
        doc.Descendants(OneNs + "Title")
            .Descendants(OneNs + "T")
            .First().Value.Should().Contain("My Title");
    }

    [Fact]
    public void Convert_PageTitleParam_OverridesH1()
    {
        var result = _converter.Convert("# H1 Title", pageTitle: "Override Title");
        var doc = ParseResult(result);

        doc.Root!.Attribute("name")!.Value.Should().Be("Override Title");
    }

    #endregion
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "MarkdownToOneNoteXmlConverterTests" --verbosity normal`
Expected: FAIL — `MarkdownToOneNoteXmlConverter` class does not exist

- [ ] **Step 3: Write minimal converter with paragraph and heading support**

Create `OneNoteMarkdownExporter/Services/MarkdownToOneNoteXmlConverter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace OneNoteMarkdownExporter.Services
{
    public class MarkdownToOneNoteXmlConverter
    {
        private static readonly XNamespace OneNs = "http://schemas.microsoft.com/office/onenote/2013/onenote";
        private string? _basePath;

        private static readonly Dictionary<int, (string size, bool bold, bool italic)> HeadingStyles = new()
        {
            { 1, ("20.0pt", true, false) },
            { 2, ("16.0pt", true, false) },
            { 3, ("13.0pt", true, false) },
            { 4, ("12.0pt", true, false) },
            { 5, ("11.0pt", true, false) },
            { 6, ("11.0pt", true, true) },
        };

        public string Convert(
            string markdown,
            string? pageTitle = null,
            bool collapsible = true,
            string? basePath = null)
        {
            _basePath = basePath;

            var pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .Build();

            var document = Markdig.Markdown.Parse(markdown, pipeline);

            // Extract title from first H1 if not provided
            if (pageTitle == null)
            {
                var firstH1 = document.OfType<HeadingBlock>().FirstOrDefault(h => h.Level == 1);
                pageTitle = firstH1 != null ? GetInlineText(firstH1.Inline) : "Untitled";
            }

            var contentElements = new List<XElement>();

            if (collapsible)
            {
                contentElements = ConvertBlocksCollapsible(document);
            }
            else
            {
                foreach (var block in document)
                {
                    var element = ConvertBlock(block);
                    if (element != null) contentElements.Add(element);
                }
            }

            return BuildPageXml(pageTitle, contentElements);
        }

        private string BuildPageXml(string title, List<XElement> contentElements)
        {
            var page = new XElement(OneNs + "Page",
                new XAttribute("name", title),
                new XAttribute(XNamespace.Xmlns + "one", OneNs.NamespaceName),
                new XElement(OneNs + "Title",
                    new XElement(OneNs + "OE",
                        new XElement(OneNs + "T", new XCData(title))
                    )
                ),
                new XElement(OneNs + "Outline",
                    new XElement(OneNs + "OEChildren", contentElements)
                )
            );

            return new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                page
            ).ToString();
        }

        private List<XElement> ConvertBlocksCollapsible(MarkdownDocument document)
        {
            var result = new List<XElement>();
            var headingStack = new Stack<(int level, XElement oe, List<XElement> children)>();

            foreach (var block in document)
            {
                if (block is HeadingBlock heading)
                {
                    // Pop headings of same or higher level
                    while (headingStack.Count > 0 && headingStack.Peek().level >= heading.Level)
                    {
                        var popped = headingStack.Pop();
                        if (popped.children.Count > 0)
                        {
                            popped.oe.Add(new XElement(OneNs + "OEChildren", popped.children));
                        }
                    }

                    var headingOe = CreateHeadingElement(heading);
                    var childrenList = new List<XElement>();
                    headingStack.Push((heading.Level, headingOe, childrenList));
                }
                else
                {
                    var element = ConvertBlock(block);
                    if (element != null)
                    {
                        if (headingStack.Count > 0)
                        {
                            headingStack.Peek().children.Add(element);
                        }
                        else
                        {
                            result.Add(element);
                        }
                    }
                }
            }

            // Flush remaining headings
            while (headingStack.Count > 0)
            {
                var popped = headingStack.Pop();
                if (popped.children.Count > 0)
                {
                    popped.oe.Add(new XElement(OneNs + "OEChildren", popped.children));
                }

                if (headingStack.Count > 0)
                {
                    headingStack.Peek().children.Add(popped.oe);
                }
                else
                {
                    result.Add(popped.oe);
                }
            }

            return result;
        }

        private XElement? ConvertBlock(Block block)
        {
            return block switch
            {
                HeadingBlock heading => CreateHeadingElement(heading),
                ParagraphBlock paragraph => CreateParagraphElement(paragraph),
                _ => CreateParagraphFromPlainText(block)
            };
        }

        private XElement CreateHeadingElement(HeadingBlock heading)
        {
            var (size, bold, italic) = HeadingStyles.GetValueOrDefault(heading.Level, ("11.0pt", true, false));
            var style = $"font-family:Segoe UI;font-size:{size}";
            if (bold) style += ";font-weight:bold";
            if (italic) style += ";font-style:italic";

            return new XElement(OneNs + "OE",
                new XAttribute("style", style),
                new XElement(OneNs + "T", new XCData(RenderInlineHtml(heading.Inline)))
            );
        }

        private XElement CreateParagraphElement(ParagraphBlock paragraph)
        {
            return new XElement(OneNs + "OE",
                new XElement(OneNs + "T", new XCData(RenderInlineHtml(paragraph.Inline)))
            );
        }

        private XElement? CreateParagraphFromPlainText(Block block)
        {
            var text = block.ToString();
            if (string.IsNullOrWhiteSpace(text)) return null;
            return new XElement(OneNs + "OE",
                new XElement(OneNs + "T", new XCData(text))
            );
        }

        private string RenderInlineHtml(ContainerInline? inlines)
        {
            if (inlines == null) return "";
            var sb = new StringBuilder();
            foreach (var inline in inlines)
            {
                sb.Append(RenderSingleInline(inline));
            }
            return sb.ToString();
        }

        private string RenderSingleInline(Inline inline)
        {
            return inline switch
            {
                LiteralInline literal => System.Net.WebUtility.HtmlEncode(literal.Content.ToString()),
                EmphasisInline emphasis => RenderEmphasis(emphasis),
                CodeInline code => $"<span style='font-family:Consolas;font-size:9pt'>{System.Net.WebUtility.HtmlEncode(code.Content)}</span>",
                LinkInline link when link.IsImage => RenderImage(link),
                LinkInline link => $"<a href=\"{link.Url}\">{RenderInlineChildren(link)}</a>",
                LineBreakInline => "<br/>",
                _ => inline.ToString() ?? ""
            };
        }

        private string RenderEmphasis(EmphasisInline emphasis)
        {
            var content = RenderInlineChildren(emphasis);
            if (emphasis.DelimiterChar is '*' or '_')
            {
                return emphasis.DelimiterCount == 2
                    ? $"<b>{content}</b>"
                    : $"<i>{content}</i>";
            }
            if (emphasis.DelimiterChar == '~' && emphasis.DelimiterCount == 2)
            {
                return $"<del>{content}</del>";
            }
            return content;
        }

        private string RenderInlineChildren(ContainerInline container)
        {
            var sb = new StringBuilder();
            foreach (var child in container)
            {
                sb.Append(RenderSingleInline(child));
            }
            return sb.ToString();
        }

        private string RenderImage(LinkInline image)
        {
            var altText = RenderInlineChildren(image);
            var url = image.Url ?? "";

            if (_basePath == null || url.StartsWith("http://") || url.StartsWith("https://"))
            {
                return $"[Image: {altText} ({url})]";
            }

            var fullPath = Path.Combine(_basePath, url.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                return $"[Image not found: {url}]";
            }

            // Base64 embedding added in Task 8 — return placeholder for now
            return $"[Image: {altText}]";
        }

        internal static string GetInlineText(ContainerInline? inlines)
        {
            if (inlines == null) return "";
            var sb = new StringBuilder();
            foreach (var inline in inlines)
            {
                if (inline is LiteralInline literal)
                    sb.Append(literal.Content.ToString());
                else if (inline is ContainerInline container)
                    sb.Append(GetInlineText(container));
            }
            return sb.ToString();
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "MarkdownToOneNoteXmlConverterTests" --verbosity normal`
Expected: All 5 tests PASS

- [ ] **Step 5: Commit**

```bash
git add OneNoteMarkdownExporter/Services/MarkdownToOneNoteXmlConverter.cs OneNoteMarkdownExporter.Tests/Converters/MarkdownToOneNoteXmlConverterTests.cs
git commit -m "feat: add MarkdownToOneNoteXmlConverter with paragraph and heading support"
```

---

### Task 3: Converter — Inline Formatting

**Files:**
- Modify: `OneNoteMarkdownExporter.Tests/Converters/MarkdownToOneNoteXmlConverterTests.cs`
- Modify: `OneNoteMarkdownExporter/Services/MarkdownToOneNoteXmlConverter.cs` (already handles these — tests validate)

- [ ] **Step 1: Write failing tests for inline formatting**

Add to `MarkdownToOneNoteXmlConverterTests.cs`:

```csharp
#region Inline Formatting Tests

[Fact]
public void Convert_BoldText_RendersHtmlBold()
{
    var result = _converter.Convert("This is **bold** text", pageTitle: "Test");
    var doc = ParseResult(result);

    var texts = doc.Descendants(OneNs + "T").Select(t => t.Value);
    texts.Should().Contain(t => t.Contains("<b>bold</b>"));
}

[Fact]
public void Convert_ItalicText_RendersHtmlItalic()
{
    var result = _converter.Convert("This is *italic* text", pageTitle: "Test");
    var doc = ParseResult(result);

    var texts = doc.Descendants(OneNs + "T").Select(t => t.Value);
    texts.Should().Contain(t => t.Contains("<i>italic</i>"));
}

[Fact]
public void Convert_StrikethroughText_RendersHtmlDel()
{
    var result = _converter.Convert("This is ~~struck~~ text", pageTitle: "Test");
    var doc = ParseResult(result);

    var texts = doc.Descendants(OneNs + "T").Select(t => t.Value);
    texts.Should().Contain(t => t.Contains("<del>struck</del>"));
}

[Fact]
public void Convert_InlineCode_RendersConsolas()
{
    var result = _converter.Convert("Use `myCommand` here", pageTitle: "Test");
    var doc = ParseResult(result);

    var texts = doc.Descendants(OneNs + "T").Select(t => t.Value);
    texts.Should().Contain(t => t.Contains("font-family:Consolas") && t.Contains("myCommand"));
}

[Fact]
public void Convert_Link_RendersHtmlAnchor()
{
    var result = _converter.Convert("[Click here](https://example.com)", pageTitle: "Test");
    var doc = ParseResult(result);

    var texts = doc.Descendants(OneNs + "T").Select(t => t.Value);
    texts.Should().Contain(t => t.Contains("href=\"https://example.com\"") && t.Contains("Click here"));
}

[Fact]
public void Convert_BoldAndItalic_RendersBothTags()
{
    var result = _converter.Convert("This is ***bold italic*** text", pageTitle: "Test");
    var doc = ParseResult(result);

    var texts = doc.Descendants(OneNs + "T").Select(t => t.Value);
    texts.Should().Contain(t => (t.Contains("<b>") && t.Contains("<i>")) || t.Contains("<b><i>"));
}

#endregion
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test --filter "MarkdownToOneNoteXmlConverterTests" --verbosity normal`
Expected: All 11 tests PASS (the inline rendering logic is already in the converter from Task 2)

- [ ] **Step 3: Commit**

```bash
git add OneNoteMarkdownExporter.Tests/Converters/MarkdownToOneNoteXmlConverterTests.cs
git commit -m "test: add inline formatting tests for Markdown-to-OneNote converter"
```

---

### Task 4: Converter — Fenced Code Blocks

**Files:**
- Modify: `OneNoteMarkdownExporter.Tests/Converters/MarkdownToOneNoteXmlConverterTests.cs`
- Modify: `OneNoteMarkdownExporter/Services/MarkdownToOneNoteXmlConverter.cs`

- [ ] **Step 1: Write failing tests for code blocks**

Add to `MarkdownToOneNoteXmlConverterTests.cs`:

```csharp
#region Code Block Tests

[Fact]
public void Convert_FencedCodeBlock_CreatesTableWithConsolas()
{
    var markdown = "```\nvar x = 1;\nvar y = 2;\n```";
    var result = _converter.Convert(markdown, pageTitle: "Test");
    var doc = ParseResult(result);

    var tables = doc.Descendants(OneNs + "Table");
    tables.Should().NotBeEmpty();
    tables.First().Attribute("bordersVisible")!.Value.Should().Be("true");

    var text = doc.Descendants(OneNs + "T").Select(t => t.Value);
    text.Should().Contain(t => t.Contains("Consolas") && t.Contains("var x = 1;"));
}

[Fact]
public void Convert_FencedCodeBlockWithLanguage_CreatesTable()
{
    var markdown = "```csharp\nConsole.WriteLine(\"hi\");\n```";
    var result = _converter.Convert(markdown, pageTitle: "Test");
    var doc = ParseResult(result);

    var tables = doc.Descendants(OneNs + "Table");
    tables.Should().NotBeEmpty();
}

[Fact]
public void Convert_FencedCodeBlock_PreservesMultipleLines()
{
    var markdown = "```\nline1\nline2\nline3\n```";
    var result = _converter.Convert(markdown, pageTitle: "Test");
    var doc = ParseResult(result);

    var text = doc.Descendants(OneNs + "T").Select(t => t.Value);
    text.Should().Contain(t => t.Contains("line1") && t.Contains("line2") && t.Contains("line3"));
}

#endregion
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "Convert_FencedCodeBlock" --verbosity normal`
Expected: FAIL — code blocks fall through to the default plain text handler

- [ ] **Step 3: Add code block conversion to the converter**

In `MarkdownToOneNoteXmlConverter.cs`, add this method:

```csharp
private XElement CreateCodeBlockElement(FencedCodeBlock codeBlock)
{
    var lines = new List<string>();
    foreach (var line in codeBlock.Lines)
    {
        var text = line.ToString();
        if (text != null)
            lines.Add(System.Net.WebUtility.HtmlEncode(text));
    }

    var codeHtml = $"<span style='font-family:Consolas;font-size:9pt'>{string.Join("<br/>", lines)}</span>";

    return new XElement(OneNs + "Table",
        new XAttribute("bordersVisible", "true"),
        new XElement(OneNs + "Columns",
            new XElement(OneNs + "Column",
                new XAttribute("index", "0"),
                new XAttribute("width", "600")
            )
        ),
        new XElement(OneNs + "Row",
            new XElement(OneNs + "Cell",
                new XElement(OneNs + "OEChildren",
                    new XElement(OneNs + "OE",
                        new XElement(OneNs + "T", new XCData(codeHtml))
                    )
                )
            )
        )
    );
}
```

Update the `ConvertBlock` switch to add the case before the default:

```csharp
private XElement? ConvertBlock(Block block)
{
    return block switch
    {
        HeadingBlock heading => CreateHeadingElement(heading),
        ParagraphBlock paragraph => CreateParagraphElement(paragraph),
        FencedCodeBlock codeBlock => CreateCodeBlockElement(codeBlock),
        _ => CreateParagraphFromPlainText(block)
    };
}
```

Add to the using statements at the top if not already present:

```csharp
using Markdig.Syntax;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "MarkdownToOneNoteXmlConverterTests" --verbosity normal`
Expected: All 14 tests PASS

- [ ] **Step 5: Commit**

```bash
git add OneNoteMarkdownExporter/Services/MarkdownToOneNoteXmlConverter.cs OneNoteMarkdownExporter.Tests/Converters/MarkdownToOneNoteXmlConverterTests.cs
git commit -m "feat: add fenced code block support (bordered table with Consolas)"
```

---

### Task 5: Converter — Lists (Flat and Nested)

**Files:**
- Modify: `OneNoteMarkdownExporter.Tests/Converters/MarkdownToOneNoteXmlConverterTests.cs`
- Modify: `OneNoteMarkdownExporter/Services/MarkdownToOneNoteXmlConverter.cs`

- [ ] **Step 1: Write failing tests for lists**

Add to `MarkdownToOneNoteXmlConverterTests.cs`:

```csharp
#region List Tests

[Fact]
public void Convert_BulletList_CreatesBulletElements()
{
    var markdown = "- Item one\n- Item two\n- Item three";
    var result = _converter.Convert(markdown, pageTitle: "Test");
    var doc = ParseResult(result);

    var bullets = doc.Descendants(OneNs + "Bullet");
    bullets.Should().HaveCount(3);

    var texts = doc.Descendants(OneNs + "T").Select(t => t.Value);
    texts.Should().Contain(t => t.Contains("Item one"));
    texts.Should().Contain(t => t.Contains("Item two"));
}

[Fact]
public void Convert_NumberedList_CreatesNumberElements()
{
    var markdown = "1. First\n2. Second\n3. Third";
    var result = _converter.Convert(markdown, pageTitle: "Test");
    var doc = ParseResult(result);

    var numbers = doc.Descendants(OneNs + "Number");
    numbers.Should().HaveCount(3);
}

[Fact]
public void Convert_NestedBulletList_UsesOEChildren()
{
    var markdown = "- Parent\n  - Child\n  - Child 2\n- Parent 2";
    var result = _converter.Convert(markdown, pageTitle: "Test");
    var doc = ParseResult(result);

    // The parent OE should contain an OEChildren with the nested items
    var parentOes = doc.Descendants(OneNs + "OE")
        .Where(oe => oe.Elements(OneNs + "List").Any());

    var parentWithChildren = parentOes
        .Where(oe => oe.Elements(OneNs + "OEChildren").Any());
    parentWithChildren.Should().NotBeEmpty("nested list items should be inside OEChildren");
}

[Fact]
public void Convert_NestedNumberedList_UsesOEChildren()
{
    var markdown = "1. Parent\n   1. Child A\n   2. Child B\n2. Parent 2";
    var result = _converter.Convert(markdown, pageTitle: "Test");
    var doc = ParseResult(result);

    var numbers = doc.Descendants(OneNs + "Number");
    numbers.Should().HaveCountGreaterOrEqualTo(4);
}

#endregion
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "Convert_BulletList|Convert_NumberedList|Convert_NestedBulletList|Convert_NestedNumberedList" --verbosity normal`
Expected: FAIL — `ListBlock` falls through to default handler

- [ ] **Step 3: Add list conversion logic**

Add these methods to `MarkdownToOneNoteXmlConverter.cs`:

```csharp
private XElement CreateListElements(ListBlock listBlock)
{
    // A list block wraps its items in a container element.
    // We return an OEChildren containing individual OE items.
    var container = new XElement(OneNs + "OEChildren");

    foreach (var item in listBlock)
    {
        if (item is ListItemBlock listItem)
        {
            var oe = CreateListItemElement(listItem, listBlock.IsOrdered);
            container.Add(oe);
        }
    }

    return container;
}

private XElement CreateListItemElement(ListItemBlock listItem, bool isOrdered)
{
    var oe = new XElement(OneNs + "OE");

    // Add list marker
    if (isOrdered)
    {
        oe.Add(new XElement(OneNs + "List",
            new XElement(OneNs + "Number",
                new XAttribute("numberSequence", "0"),
                new XAttribute("fontSize", "11.0")
            )
        ));
    }
    else
    {
        oe.Add(new XElement(OneNs + "List",
            new XElement(OneNs + "Bullet",
                new XAttribute("bullet", "2"),
                new XAttribute("fontSize", "11.0")
            )
        ));
    }

    // Process child blocks of the list item
    foreach (var child in listItem)
    {
        if (child is ParagraphBlock paragraph)
        {
            oe.Add(new XElement(OneNs + "T", new XCData(RenderInlineHtml(paragraph.Inline))));
        }
        else if (child is ListBlock nestedList)
        {
            // Nested list goes into OEChildren
            oe.Add(CreateListElements(nestedList));
        }
        else
        {
            var converted = ConvertBlock(child);
            if (converted != null)
            {
                oe.Add(new XElement(OneNs + "OEChildren", converted));
            }
        }
    }

    return oe;
}
```

Update the `ConvertBlock` switch:

```csharp
private XElement? ConvertBlock(Block block)
{
    return block switch
    {
        HeadingBlock heading => CreateHeadingElement(heading),
        ParagraphBlock paragraph => CreateParagraphElement(paragraph),
        FencedCodeBlock codeBlock => CreateCodeBlockElement(codeBlock),
        ListBlock listBlock => CreateListElements(listBlock),
        _ => CreateParagraphFromPlainText(block)
    };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "MarkdownToOneNoteXmlConverterTests" --verbosity normal`
Expected: All 18 tests PASS

- [ ] **Step 5: Commit**

```bash
git add OneNoteMarkdownExporter/Services/MarkdownToOneNoteXmlConverter.cs OneNoteMarkdownExporter.Tests/Converters/MarkdownToOneNoteXmlConverterTests.cs
git commit -m "feat: add bullet and numbered list support with nesting"
```

---

### Task 6: Converter — Tables

**Files:**
- Modify: `OneNoteMarkdownExporter.Tests/Converters/MarkdownToOneNoteXmlConverterTests.cs`
- Modify: `OneNoteMarkdownExporter/Services/MarkdownToOneNoteXmlConverter.cs`

- [ ] **Step 1: Write failing tests for tables**

Add to `MarkdownToOneNoteXmlConverterTests.cs`:

```csharp
#region Table Tests

[Fact]
public void Convert_SimpleTable_CreatesTableElement()
{
    var markdown = "| Name | Age |\n|------|-----|\n| Alice | 30 |\n| Bob | 25 |";
    var result = _converter.Convert(markdown, pageTitle: "Test");
    var doc = ParseResult(result);

    var tables = doc.Descendants(OneNs + "Table");
    tables.Should().HaveCount(1);
    tables.First().Attribute("bordersVisible")!.Value.Should().Be("true");

    var columns = doc.Descendants(OneNs + "Column");
    columns.Should().HaveCount(2);

    var rows = doc.Descendants(OneNs + "Row");
    rows.Should().HaveCount(3); // header + 2 data rows
}

[Fact]
public void Convert_TableHeaderRow_RendersAsBold()
{
    var markdown = "| Header1 | Header2 |\n|---------|----------|\n| Cell1 | Cell2 |";
    var result = _converter.Convert(markdown, pageTitle: "Test");
    var doc = ParseResult(result);

    var firstRowCells = doc.Descendants(OneNs + "Row").First()
        .Descendants(OneNs + "T");
    firstRowCells.Should().Contain(t => t.Value.Contains("<b>"));
}

#endregion
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "Convert_SimpleTable|Convert_TableHeaderRow" --verbosity normal`
Expected: FAIL

- [ ] **Step 3: Add table conversion logic**

Add this using directive at the top of `MarkdownToOneNoteXmlConverter.cs`:

```csharp
using Markdig.Extensions.Tables;
```

Add these methods:

```csharp
private XElement CreateTableElement(Markdig.Extensions.Tables.Table table)
{
    var columnCount = 0;
    // Determine column count from first row
    foreach (var row in table.OfType<TableRow>())
    {
        columnCount = Math.Max(columnCount, row.Count);
        break;
    }

    if (columnCount == 0) columnCount = 1;
    var columnWidth = Math.Max(100, 600 / columnCount);

    var tableElement = new XElement(OneNs + "Table",
        new XAttribute("bordersVisible", "true"));

    var columnsElement = new XElement(OneNs + "Columns");
    for (int i = 0; i < columnCount; i++)
    {
        columnsElement.Add(new XElement(OneNs + "Column",
            new XAttribute("index", i.ToString()),
            new XAttribute("width", columnWidth.ToString())
        ));
    }
    tableElement.Add(columnsElement);

    foreach (var row in table.OfType<TableRow>())
    {
        var rowElement = new XElement(OneNs + "Row");

        foreach (var cell in row.OfType<TableCell>())
        {
            var cellContent = "";
            foreach (var block in cell)
            {
                if (block is ParagraphBlock p)
                {
                    cellContent += RenderInlineHtml(p.Inline);
                }
            }

            if (row.IsHeader)
            {
                cellContent = $"<b>{cellContent}</b>";
            }

            rowElement.Add(new XElement(OneNs + "Cell",
                new XElement(OneNs + "OEChildren",
                    new XElement(OneNs + "OE",
                        new XElement(OneNs + "T", new XCData(cellContent))
                    )
                )
            ));
        }

        tableElement.Add(rowElement);
    }

    return tableElement;
}
```

Update the `ConvertBlock` switch:

```csharp
private XElement? ConvertBlock(Block block)
{
    return block switch
    {
        HeadingBlock heading => CreateHeadingElement(heading),
        ParagraphBlock paragraph => CreateParagraphElement(paragraph),
        FencedCodeBlock codeBlock => CreateCodeBlockElement(codeBlock),
        ListBlock listBlock => CreateListElements(listBlock),
        Markdig.Extensions.Tables.Table table => CreateTableElement(table),
        _ => CreateParagraphFromPlainText(block)
    };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "MarkdownToOneNoteXmlConverterTests" --verbosity normal`
Expected: All 20 tests PASS

- [ ] **Step 5: Commit**

```bash
git add OneNoteMarkdownExporter/Services/MarkdownToOneNoteXmlConverter.cs OneNoteMarkdownExporter.Tests/Converters/MarkdownToOneNoteXmlConverterTests.cs
git commit -m "feat: add table support with bold headers and auto column widths"
```

---

### Task 7: Converter — Blockquotes, Horizontal Rules, and Collapsible Nesting

**Files:**
- Modify: `OneNoteMarkdownExporter.Tests/Converters/MarkdownToOneNoteXmlConverterTests.cs`
- Modify: `OneNoteMarkdownExporter/Services/MarkdownToOneNoteXmlConverter.cs`

- [ ] **Step 1: Write failing tests**

Add to `MarkdownToOneNoteXmlConverterTests.cs`:

```csharp
#region Blockquote and HR Tests

[Fact]
public void Convert_Blockquote_RendersAsIndentedItalic()
{
    var markdown = "> This is a quote";
    var result = _converter.Convert(markdown, pageTitle: "Test");
    var doc = ParseResult(result);

    var texts = doc.Descendants(OneNs + "T").Select(t => t.Value);
    texts.Should().Contain(t => t.Contains("<i>") && t.Contains("This is a quote"));
}

[Fact]
public void Convert_HorizontalRule_CreatesOeWithDashes()
{
    var markdown = "Before\n\n---\n\nAfter";
    var result = _converter.Convert(markdown, pageTitle: "Test");
    var doc = ParseResult(result);

    var texts = doc.Descendants(OneNs + "T").Select(t => t.Value);
    texts.Should().Contain(t => t.Contains("---"));
}

#endregion

#region Collapsible Nesting Tests

[Fact]
public void Convert_CollapsibleEnabled_NestsContentUnderHeadings()
{
    var markdown = "## Section\n\nParagraph under section\n\n### Sub\n\nParagraph under sub";
    var result = _converter.Convert(markdown, pageTitle: "Test", collapsible: true);
    var doc = ParseResult(result);

    // The H2 heading OE should contain an OEChildren
    var h2Oes = doc.Descendants(OneNs + "OE")
        .Where(oe => oe.Attribute("style")?.Value.Contains("16.0pt") == true);
    h2Oes.Should().NotBeEmpty();

    var h2Oe = h2Oes.First();
    h2Oe.Elements(OneNs + "OEChildren").Should().NotBeEmpty(
        "content after H2 should be nested inside it as OEChildren");
}

[Fact]
public void Convert_CollapsibleDisabled_FlatStructure()
{
    var markdown = "## Section\n\nParagraph under section";
    var result = _converter.Convert(markdown, pageTitle: "Test", collapsible: false);
    var doc = ParseResult(result);

    // The H2 heading OE should NOT contain OEChildren
    var h2Oes = doc.Descendants(OneNs + "OE")
        .Where(oe => oe.Attribute("style")?.Value.Contains("16.0pt") == true);
    h2Oes.Should().NotBeEmpty();

    var h2Oe = h2Oes.First();
    h2Oe.Elements(OneNs + "OEChildren").Should().BeEmpty(
        "collapsible is disabled, so content should be flat siblings");
}

#endregion
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "Convert_Blockquote|Convert_HorizontalRule" --verbosity normal`
Expected: FAIL for blockquote and horizontal rule (collapsible tests may already pass from Task 2)

- [ ] **Step 3: Add blockquote and horizontal rule support**

Add these methods to `MarkdownToOneNoteXmlConverter.cs`:

```csharp
private XElement CreateBlockquoteElement(QuoteBlock quoteBlock)
{
    var children = new XElement(OneNs + "OEChildren");
    foreach (var block in quoteBlock)
    {
        if (block is ParagraphBlock paragraph)
        {
            children.Add(new XElement(OneNs + "OE",
                new XElement(OneNs + "T",
                    new XCData($"<i>{RenderInlineHtml(paragraph.Inline)}</i>"))
            ));
        }
        else
        {
            var converted = ConvertBlock(block);
            if (converted != null) children.Add(converted);
        }
    }
    return children;
}

private XElement CreateHorizontalRuleElement()
{
    return new XElement(OneNs + "OE",
        new XElement(OneNs + "T", new XCData("---"))
    );
}
```

Add the `using` if needed:

```csharp
using Markdig.Syntax;
```

Update the `ConvertBlock` switch:

```csharp
private XElement? ConvertBlock(Block block)
{
    return block switch
    {
        HeadingBlock heading => CreateHeadingElement(heading),
        ParagraphBlock paragraph => CreateParagraphElement(paragraph),
        FencedCodeBlock codeBlock => CreateCodeBlockElement(codeBlock),
        ListBlock listBlock => CreateListElements(listBlock),
        Markdig.Extensions.Tables.Table table => CreateTableElement(table),
        QuoteBlock quoteBlock => CreateBlockquoteElement(quoteBlock),
        ThematicBreakBlock => CreateHorizontalRuleElement(),
        _ => CreateParagraphFromPlainText(block)
    };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "MarkdownToOneNoteXmlConverterTests" --verbosity normal`
Expected: All 24 tests PASS

- [ ] **Step 5: Commit**

```bash
git add OneNoteMarkdownExporter/Services/MarkdownToOneNoteXmlConverter.cs OneNoteMarkdownExporter.Tests/Converters/MarkdownToOneNoteXmlConverterTests.cs
git commit -m "feat: add blockquote, horizontal rule, and collapsible nesting tests"
```

---

### Task 8: Converter — Image Embedding

**Files:**
- Modify: `OneNoteMarkdownExporter.Tests/Converters/MarkdownToOneNoteXmlConverterTests.cs`
- Modify: `OneNoteMarkdownExporter/Services/MarkdownToOneNoteXmlConverter.cs`

- [ ] **Step 1: Write failing tests for images**

Add to `MarkdownToOneNoteXmlConverterTests.cs`:

```csharp
#region Image Tests

[Fact]
public void Convert_LocalImage_EmbedsBase64Data()
{
    // Create a temp directory with a tiny PNG (1x1 pixel)
    var tempDir = Path.Combine(Path.GetTempPath(), "onenote_test_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDir);
    try
    {
        // Minimal valid PNG: 1x1 red pixel
        var pngBytes = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG signature
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, // IHDR chunk
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
            0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41, // IDAT chunk
            0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
            0x00, 0x00, 0x02, 0x00, 0x01, 0xE2, 0x21, 0xBC,
            0x33, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, // IEND chunk
            0x44, 0xAE, 0x42, 0x60, 0x82
        };
        File.WriteAllBytes(Path.Combine(tempDir, "test.png"), pngBytes);

        var markdown = "![Alt text](test.png)";
        var result = _converter.Convert(markdown, pageTitle: "Test", basePath: tempDir);
        var doc = ParseResult(result);

        var images = doc.Descendants(OneNs + "Image");
        images.Should().NotBeEmpty();
        var data = doc.Descendants(OneNs + "Data");
        data.Should().NotBeEmpty();
        data.First().Value.Should().NotBeNullOrEmpty();
    }
    finally
    {
        Directory.Delete(tempDir, true);
    }
}

[Fact]
public void Convert_MissingImage_RendersPlaceholder()
{
    var tempDir = Path.Combine(Path.GetTempPath(), "onenote_test_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDir);
    try
    {
        var markdown = "![Alt](missing.png)";
        var result = _converter.Convert(markdown, pageTitle: "Test", basePath: tempDir);

        result.Should().Contain("[Image not found: missing.png]");
    }
    finally
    {
        Directory.Delete(tempDir, true);
    }
}

[Fact]
public void Convert_ImageWithNoBasePath_RendersFallbackText()
{
    var markdown = "![My diagram](assets/diagram.png)";
    var result = _converter.Convert(markdown, pageTitle: "Test", basePath: null);

    result.Should().Contain("Image:");
    result.Should().Contain("assets/diagram.png");
}

#endregion
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "Convert_LocalImage|Convert_MissingImage|Convert_ImageWithNoBasePath" --verbosity normal`
Expected: `Convert_LocalImage` FAILS — `RenderImage` returns placeholder text, not an `Image` XML element

- [ ] **Step 3: Update image rendering to embed base64 data**

Replace the `RenderImage` method in `MarkdownToOneNoteXmlConverter.cs`. Since images are block-level XML elements (`one:Image`) but `RenderSingleInline` returns a string, we need to handle images differently. Add a field to collect image elements that need to be inserted:

Add a field:

```csharp
private List<XElement>? _pendingImageElements;
```

Update `RenderImage`:

```csharp
private string RenderImage(LinkInline image)
{
    var altText = RenderInlineChildren(image);
    var url = image.Url ?? "";

    if (_basePath == null || url.StartsWith("http://") || url.StartsWith("https://"))
    {
        return $"[Image: {altText} ({url})]";
    }

    var fullPath = Path.Combine(_basePath, url.Replace('/', Path.DirectorySeparatorChar));
    if (!File.Exists(fullPath))
    {
        return $"[Image not found: {url}]";
    }

    var bytes = File.ReadAllBytes(fullPath);
    var base64 = System.Convert.ToBase64String(bytes);

    var imageElement = new XElement(OneNs + "Image",
        new XElement(OneNs + "Data", base64)
    );

    _pendingImageElements?.Add(imageElement);

    // Return empty string — the image element is added separately
    return "";
}
```

Update `CreateParagraphElement` to handle pending images:

```csharp
private XElement CreateParagraphElement(ParagraphBlock paragraph)
{
    _pendingImageElements = new List<XElement>();
    var html = RenderInlineHtml(paragraph.Inline);
    var pending = _pendingImageElements;
    _pendingImageElements = null;

    if (pending.Count > 0 && string.IsNullOrWhiteSpace(html))
    {
        // Paragraph was only an image — return the image element directly
        if (pending.Count == 1) return pending[0];
        var container = new XElement(OneNs + "OEChildren");
        foreach (var img in pending) container.Add(img);
        return container;
    }

    var oe = new XElement(OneNs + "OE",
        new XElement(OneNs + "T", new XCData(html))
    );

    if (pending.Count > 0)
    {
        // Mixed text and images — images follow the text
        var container = new XElement(OneNs + "OEChildren", oe);
        foreach (var img in pending) container.Add(img);
        return container;
    }

    return oe;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "MarkdownToOneNoteXmlConverterTests" --verbosity normal`
Expected: All 27 tests PASS

- [ ] **Step 5: Commit**

```bash
git add OneNoteMarkdownExporter/Services/MarkdownToOneNoteXmlConverter.cs OneNoteMarkdownExporter.Tests/Converters/MarkdownToOneNoteXmlConverterTests.cs
git commit -m "feat: add local image embedding as base64 in OneNote XML"
```

---

### Task 9: Converter — Mixed Content Integration Test

**Files:**
- Modify: `OneNoteMarkdownExporter.Tests/Converters/MarkdownToOneNoteXmlConverterTests.cs`

- [ ] **Step 1: Write a mixed-content integration test**

Add to `MarkdownToOneNoteXmlConverterTests.cs`:

```csharp
#region Integration Tests

[Fact]
public void Convert_MixedContent_ProducesValidXml()
{
    var markdown = @"# Main Title

Some introductory text with **bold** and *italic*.

## Section One

- Bullet A
- Bullet B
  - Nested bullet

### Code Example

```csharp
var x = 42;
```

## Section Two

| Col A | Col B |
|-------|-------|
| 1     | 2     |

> A wise quote

---

[A link](https://example.com)
";

    var result = _converter.Convert(markdown);
    var doc = ParseResult(result);

    // Valid XML with correct structure
    doc.Root!.Name.Should().Be(OneNs + "Page");
    doc.Root.Attribute("name")!.Value.Should().Be("Main Title");
    doc.Descendants(OneNs + "Title").Should().HaveCount(1);
    doc.Descendants(OneNs + "Outline").Should().HaveCount(1);

    // Contains expected elements
    doc.Descendants(OneNs + "Bullet").Should().NotBeEmpty();
    doc.Descendants(OneNs + "Table").Should().NotBeEmpty();
    doc.Descendants(OneNs + "T").Select(t => t.Value)
        .Should().Contain(t => t.Contains("href=\"https://example.com\""));
}

#endregion
```

- [ ] **Step 2: Run test to verify it passes**

Run: `dotnet test --filter "Convert_MixedContent" --verbosity normal`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add OneNoteMarkdownExporter.Tests/Converters/MarkdownToOneNoteXmlConverterTests.cs
git commit -m "test: add mixed-content integration test for converter"
```

---

### Task 10: ImportOptions, ImportResult, and ImportService

**Files:**
- Create: `OneNoteMarkdownExporter/Services/ImportOptions.cs`
- Create: `OneNoteMarkdownExporter/Services/ImportResult.cs`
- Create: `OneNoteMarkdownExporter/Services/ImportService.cs`
- Create: `OneNoteMarkdownExporter.Tests/Services/ImportServiceTests.cs`

- [ ] **Step 1: Write failing tests for ImportService**

Create `OneNoteMarkdownExporter.Tests/Services/ImportServiceTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Services;

public class ImportOptionsTests
{
    [Fact]
    public void NotebookName_DefaultsToEmptyString()
    {
        var options = new ImportOptions();
        options.NotebookName.Should().Be(string.Empty);
    }

    [Fact]
    public void Collapsible_DefaultsToTrue()
    {
        var options = new ImportOptions();
        options.Collapsible.Should().BeTrue();
    }

    [Fact]
    public void DryRun_DefaultsToFalse()
    {
        var options = new ImportOptions();
        options.DryRun.Should().BeFalse();
    }
}

public class ImportResultTests
{
    [Fact]
    public void Success_ReturnsTrueWhenNoFailures()
    {
        var result = new ImportResult { TotalFiles = 2, ImportedPages = 2, FailedPages = 0 };
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Success_ReturnsFalseWhenFailuresExist()
    {
        var result = new ImportResult { TotalFiles = 2, ImportedPages = 1, FailedPages = 1 };
        result.Success.Should().BeFalse();
    }

    [Fact]
    public void Errors_InitializesAsEmptyList()
    {
        var result = new ImportResult();
        result.Errors.Should().NotBeNull();
        result.Errors.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "ImportOptionsTests|ImportResultTests" --verbosity normal`
Expected: FAIL — `ImportOptions` and `ImportResult` don't exist

- [ ] **Step 3: Create ImportOptions**

Create `OneNoteMarkdownExporter/Services/ImportOptions.cs`:

```csharp
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
    }
}
```

- [ ] **Step 4: Create ImportResult**

Create `OneNoteMarkdownExporter/Services/ImportResult.cs`:

```csharp
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
```

- [ ] **Step 5: Create ImportService**

Create `OneNoteMarkdownExporter/Services/ImportService.cs`:

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OneNoteMarkdownExporter.Services
{
    public class ImportService
    {
        private readonly OneNoteService _oneNoteService;
        private readonly MarkdownToOneNoteXmlConverter _converter;

        public ImportService(OneNoteService oneNoteService, MarkdownToOneNoteXmlConverter converter)
        {
            _oneNoteService = oneNoteService;
            _converter = converter;
        }

        public async Task<ImportResult> ImportAsync(
            ImportOptions options,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new ImportResult { TotalFiles = options.FilePaths.Count };

            // Find the target section
            string? sectionId = null;
            if (!options.DryRun)
            {
                sectionId = _oneNoteService.FindSectionId(options.NotebookName, options.SectionName);
                if (sectionId == null)
                {
                    var error = $"Section not found: {options.NotebookName}/{options.SectionName}";
                    result.Errors.Add(error);
                    result.FailedPages = result.TotalFiles;
                    progress?.Report($"Error: {error}");
                    return result;
                }
            }

            await Task.Run(() =>
            {
                foreach (var filePath in options.FilePaths)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    try
                    {
                        ImportFile(filePath, sectionId, options, result, progress);
                    }
                    catch (Exception ex)
                    {
                        result.FailedPages++;
                        var error = $"Failed to import '{Path.GetFileName(filePath)}': {ex.Message}";
                        result.Errors.Add(error);
                        progress?.Report($"Error: {error}");
                    }
                }
            }, cancellationToken);

            return result;
        }

        private void ImportFile(
            string filePath,
            string? sectionId,
            ImportOptions options,
            ImportResult result,
            IProgress<string>? progress)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var basePath = Path.GetDirectoryName(filePath);

            if (!options.Quiet)
            {
                progress?.Report($"Importing: {fileName}");
            }

            var markdown = File.ReadAllText(filePath);

            // Convert markdown to OneNote XML
            var pageXml = _converter.Convert(
                markdown,
                pageTitle: null, // Let converter extract from H1 or use filename
                collapsible: options.Collapsible,
                basePath: basePath
            );

            if (options.DryRun)
            {
                progress?.Report($"  [dry-run] Would create page: {fileName}");
                result.ImportedPages++;
                return;
            }

            // Create the page in OneNote
            var newPageId = _oneNoteService.CreatePage(sectionId!);

            // Insert the page ID into the XML and update
            var xmlWithId = pageXml.Contains("name=")
                ? pageXml.Replace("<one:Page ", $"<one:Page ID=\"{newPageId}\" ")
                : pageXml;

            _oneNoteService.UpdatePageContent(xmlWithId);
            result.ImportedPages++;

            if (options.Verbose)
            {
                progress?.Report($"  Created page: {fileName} (ID: {newPageId})");
            }
        }
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test --filter "ImportOptionsTests|ImportResultTests" --verbosity normal`
Expected: All 6 tests PASS

- [ ] **Step 7: Commit**

```bash
git add OneNoteMarkdownExporter/Services/ImportOptions.cs OneNoteMarkdownExporter/Services/ImportResult.cs OneNoteMarkdownExporter/Services/ImportService.cs OneNoteMarkdownExporter.Tests/Services/ImportServiceTests.cs
git commit -m "feat: add ImportOptions, ImportResult, and ImportService"
```

---

### Task 11: OneNoteService — CreatePage and FindSectionId

**Files:**
- Modify: `OneNoteMarkdownExporter/Services/OneNoteService.cs:153-197`

- [ ] **Step 1: Add CreatePage and FindSectionId methods**

Add before the closing `}` of the `OneNoteService` class in `OneNoteService.cs` (before line 198):

```csharp
/// <summary>
/// Creates a new page in the specified section.
/// </summary>
/// <param name="sectionId">The OneNote ID of the target section.</param>
/// <returns>The ID of the newly created page.</returns>
public string CreatePage(string sectionId)
{
    string newPageId;
    _oneNoteApp.CreateNewPage(sectionId, out newPageId, NewPageStyle.BlankPageWithTitle);
    return newPageId;
}

/// <summary>
/// Finds a section ID by notebook name and section name.
/// Case-insensitive match.
/// </summary>
/// <param name="notebookName">The notebook name to search for.</param>
/// <param name="sectionName">The section name within the notebook.</param>
/// <returns>The section ID, or null if not found.</returns>
public string? FindSectionId(string notebookName, string sectionName)
{
    string xml;
    _oneNoteApp.GetHierarchy(null, HierarchyScope.hsSections, out xml);

    var doc = XDocument.Parse(xml);
    if (doc.Root == null) return null;

    var ns = doc.Root.Name.Namespace;

    var notebook = doc.Descendants(ns + "Notebook")
        .FirstOrDefault(n => string.Equals(
            n.Attribute("name")?.Value, notebookName, StringComparison.OrdinalIgnoreCase));

    if (notebook == null) return null;

    var section = FindSectionRecursive(notebook, ns, sectionName);
    return section?.Attribute("ID")?.Value;
}

private XElement? FindSectionRecursive(XElement parent, XNamespace ns, string sectionName)
{
    foreach (var child in parent.Elements())
    {
        if (child.Name.LocalName == "Section" &&
            string.Equals(child.Attribute("name")?.Value, sectionName, StringComparison.OrdinalIgnoreCase))
        {
            return child;
        }

        if (child.Name.LocalName == "SectionGroup")
        {
            var found = FindSectionRecursive(child, ns, sectionName);
            if (found != null) return found;
        }
    }
    return null;
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Commit**

```bash
git add OneNoteMarkdownExporter/Services/OneNoteService.cs
git commit -m "feat: add CreatePage and FindSectionId to OneNoteService"
```

---

### Task 12: CLI — Add Import Options

**Files:**
- Modify: `OneNoteMarkdownExporter/Services/CliHandler.cs`
- Modify: `OneNoteMarkdownExporter.Tests/Cli/CliHandlerTests.cs`

- [ ] **Step 1: Write failing tests for new CLI flags**

Add to `CliHandlerTests.cs`:

```csharp
#region Import CLI Flag Tests

[Fact]
public void ShouldRunCli_WithImportFlag_ReturnsTrue()
{
    var args = new[] { "--import", "Notebook/Section" };
    var result = CliHandler.ShouldRunCli(args);
    result.Should().BeTrue();
}

[Fact]
public void ShouldRunCli_WithFileFlag_ReturnsTrue()
{
    var args = new[] { "--file", "notes.md" };
    var result = CliHandler.ShouldRunCli(args);
    result.Should().BeTrue();
}

[Fact]
public void ShouldRunCli_WithNoCollapseFlag_ReturnsTrue()
{
    var args = new[] { "--no-collapse" };
    var result = CliHandler.ShouldRunCli(args);
    result.Should().BeTrue();
}

#endregion
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "ShouldRunCli_WithImportFlag|ShouldRunCli_WithFileFlag|ShouldRunCli_WithNoCollapseFlag" --verbosity normal`
Expected: FAIL — these flags aren't in the `cliFlags` array

- [ ] **Step 3: Add import flags to ShouldRunCli**

In `CliHandler.cs`, update the `cliFlags` array (around line 39-46) to include the new flags:

```csharp
var cliFlags = new[]
{
    "--all", "--notebook", "--section", "--page", "--output", "-o",
    "--overwrite", "--no-lint", "--lint-config",
    "--list", "--dry-run", "--verbose", "-v", "--quiet", "-q",
    "--help", "-h", "-?", "--version",
    "--import", "--file", "--no-collapse"
};
```

- [ ] **Step 4: Add import options and handler to BuildRootCommand**

In `CliHandler.cs`, inside `BuildRootCommand()`, add these option declarations after the existing ones (after `quietOption` around line 115):

```csharp
var importOption = new Option<string?>(
    "--import",
    "Import Markdown file(s) to OneNote. Specify target as 'Notebook/Section'.");

var fileOption = new Option<string[]?>(
    "--file",
    "Markdown file(s) to import")
{
    AllowMultipleArgumentsPerToken = true
};

var noCollapseOption = new Option<bool>(
    "--no-collapse",
    "Disable collapsible heading nesting for import");
```

Add them to the root command (after `rootCommand.AddOption(quietOption);`):

```csharp
rootCommand.AddOption(importOption);
rootCommand.AddOption(fileOption);
rootCommand.AddOption(noCollapseOption);
```

Update the `SetHandler` lambda to detect import mode. Replace the existing handler (lines 131-154) with:

```csharp
rootCommand.SetHandler(async (context) =>
{
    var result = context.ParseResult;

    var importTarget = result.GetValueForOption(importOption);
    var importFiles = result.GetValueForOption(fileOption);

    if (!string.IsNullOrEmpty(importTarget))
    {
        var exitCode = await ExecuteImportAsync(
            importTarget,
            importFiles,
            !result.GetValueForOption(noCollapseOption),
            result.GetValueForOption(dryRunOption),
            result.GetValueForOption(verboseOption),
            result.GetValueForOption(quietOption),
            context.GetCancellationToken());
        context.ExitCode = exitCode;
        return;
    }

    var options = new ExportOptions
    {
        ExportAll = result.GetValueForOption(allOption),
        NotebookNames = result.GetValueForOption(notebookOption)?.ToList(),
        SectionPaths = result.GetValueForOption(sectionOption)?.ToList(),
        PageIds = result.GetValueForOption(pageOption)?.ToList(),
        OutputPath = result.GetValueForOption(outputOption) ?? ExportOptions.GetDefaultOutputPath(),
        Overwrite = result.GetValueForOption(overwriteOption),
        ApplyLinting = !result.GetValueForOption(noLintOption),
        LintConfigPath = result.GetValueForOption(lintConfigOption),
        DryRun = result.GetValueForOption(dryRunOption),
        Verbose = result.GetValueForOption(verboseOption),
        Quiet = result.GetValueForOption(quietOption)
    };

    var listMode = result.GetValueForOption(listOption);

    var exitCode2 = await ExecuteAsync(options, listMode, context.GetCancellationToken());
    context.ExitCode = exitCode2;
});
```

Add the `ExecuteImportAsync` method to `CliHandler`:

```csharp
private static async Task<int> ExecuteImportAsync(
    string importTarget,
    string[]? files,
    bool collapsible,
    bool dryRun,
    bool verbose,
    bool quiet,
    CancellationToken cancellationToken)
{
    // Validate import target format
    var parts = importTarget.Split('/');
    if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
    {
        Console.Error.WriteLine("Error: --import must be in format 'Notebook/Section'.");
        return 1;
    }

    // Validate files
    if (files == null || files.Length == 0)
    {
        Console.Error.WriteLine("Error: --file is required with --import.");
        return 1;
    }

    // Resolve file paths
    var resolvedFiles = new List<string>();
    foreach (var file in files)
    {
        var fullPath = Path.GetFullPath(file);
        if (!File.Exists(fullPath))
        {
            Console.Error.WriteLine($"Error: File not found: {file}");
            return 1;
        }
        if (!fullPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Warning: Skipping non-Markdown file: {file}");
            continue;
        }
        resolvedFiles.Add(fullPath);
    }

    if (resolvedFiles.Count == 0)
    {
        Console.Error.WriteLine("Error: No valid Markdown files found.");
        return 1;
    }

    var options = new ImportOptions
    {
        NotebookName = parts[0].Trim(),
        SectionName = parts[1].Trim(),
        FilePaths = resolvedFiles,
        Collapsible = collapsible,
        DryRun = dryRun,
        Verbose = verbose,
        Quiet = quiet
    };

    var progress = new Progress<string>(message =>
    {
        if (!quiet || message.Contains("Error") || message.Contains("failed"))
        {
            Console.WriteLine(message);
        }
    });

    try
    {
        if (!quiet)
        {
            Console.WriteLine("OneNote Markdown Importer");
            Console.WriteLine("========================");
            Console.WriteLine($"Target: {options.NotebookName}/{options.SectionName}");
            Console.WriteLine($"Files: {resolvedFiles.Count}");
            Console.WriteLine($"Collapsible: {(collapsible ? "Yes" : "No")}");
            if (dryRun) Console.WriteLine("Mode: DRY RUN");
            Console.WriteLine();
        }

        var oneNoteService = new OneNoteService();
        var converter = new MarkdownToOneNoteXmlConverter();
        var importService = new ImportService(oneNoteService, converter);

        var result = await importService.ImportAsync(options, progress, cancellationToken);

        if (!quiet && !dryRun)
        {
            Console.WriteLine();
            Console.WriteLine("Import Summary:");
            Console.WriteLine($"  Pages imported: {result.ImportedPages}");
            if (result.FailedPages > 0)
            {
                Console.WriteLine($"  Pages failed: {result.FailedPages}");
            }
        }

        return result.Success ? 0 : 1;
    }
    catch (System.Runtime.InteropServices.COMException ex)
    {
        Console.Error.WriteLine($"OneNote COM error: {ex.Message}");
        Console.Error.WriteLine("Make sure OneNote is installed and not running in a protected mode.");
        return 2;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        if (verbose)
        {
            Console.Error.WriteLine(ex.StackTrace);
        }
        return 1;
    }
}
```

- [ ] **Step 5: Build and run tests**

Run: `dotnet build && dotnet test --verbosity normal`
Expected: Build succeeded, all tests PASS (including 3 new CLI tests)

- [ ] **Step 6: Commit**

```bash
git add OneNoteMarkdownExporter/Services/CliHandler.cs OneNoteMarkdownExporter.Tests/Cli/CliHandlerTests.cs
git commit -m "feat: add --import, --file, --no-collapse CLI options with import handler"
```

---

### Task 13: Full Build Verification and Final Test Run

**Files:** None (verification only)

- [ ] **Step 1: Clean build**

Run: `dotnet clean && dotnet build`
Expected: Build succeeded, 0 errors, 0 warnings

- [ ] **Step 2: Run all tests**

Run: `dotnet test --verbosity normal`
Expected: All tests PASS (original 100 + new tests)

- [ ] **Step 3: Verify CLI help includes import options**

Run: `dotnet run --project OneNoteMarkdownExporter -- --help`
Expected: Output should list `--import`, `--file`, and `--no-collapse` alongside existing options

- [ ] **Step 4: Commit any final fixes if needed, then tag**

```bash
git add -A
git commit -m "chore: final verification pass for Markdown import feature"
```
