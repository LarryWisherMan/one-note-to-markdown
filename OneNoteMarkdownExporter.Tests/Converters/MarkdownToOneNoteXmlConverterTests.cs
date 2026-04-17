using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Converters;

/// <summary>
/// Tests for the MarkdownToOneNoteXmlConverter - converts Markdown to OneNote page XML.
/// </summary>
public class MarkdownToOneNoteXmlConverterTests
{
    private static readonly XNamespace OneNs = "http://schemas.microsoft.com/office/onenote/2013/onenote";

    private readonly MarkdownToOneNoteXmlConverter _converter;

    public MarkdownToOneNoteXmlConverterTests()
    {
        _converter = new MarkdownToOneNoteXmlConverter();
    }

    private XDocument ParseResult(string xml) => XDocument.Parse(xml);

    #region Empty Document Tests

    [Fact]
    public void Convert_EmptyDocument_ReturnsValidPageXml()
    {
        // Arrange
        var markdown = "";
        var pageTitle = "Test Page";

        // Act
        var result = _converter.Convert(markdown, pageTitle: pageTitle);
        var doc = ParseResult(result);

        // Assert
        doc.Root.Should().NotBeNull();
        doc.Root!.Name.Should().Be(OneNs + "Page");
        doc.Root.Attribute("name")?.Value.Should().Be("Test Page");

        var title = doc.Root.Element(OneNs + "Title");
        title.Should().NotBeNull();
        var titleOe = title!.Element(OneNs + "OE");
        titleOe.Should().NotBeNull();
        var titleT = titleOe!.Element(OneNs + "T");
        titleT.Should().NotBeNull();

        var outline = doc.Root.Element(OneNs + "Outline");
        outline.Should().NotBeNull();
    }

    #endregion

    #region Paragraph Tests

    [Fact]
    public void Convert_SingleParagraph_CreatesOeWithText()
    {
        // Arrange
        var markdown = "Hello World";

        // Act
        var result = _converter.Convert(markdown, pageTitle: "Test");
        var doc = ParseResult(result);

        // Assert
        var outline = doc.Root!.Element(OneNs + "Outline");
        outline.Should().NotBeNull();

        var oeChildren = outline!.Element(OneNs + "OEChildren");
        oeChildren.Should().NotBeNull();

        var oes = oeChildren!.Elements(OneNs + "OE").ToList();
        oes.Should().NotBeEmpty();

        // Find an OE containing "Hello World" in a T element
        var hasHelloWorld = oes.Any(oe =>
        {
            var t = oe.Element(OneNs + "T");
            if (t == null) return false;
            var cdata = t.Nodes().OfType<XCData>().FirstOrDefault();
            return cdata?.Value?.Contains("Hello World") == true;
        });
        hasHelloWorld.Should().BeTrue("expected an OE > T with 'Hello World' text");
    }

    #endregion

    #region Heading Tests

    // H1 goes into the page <Title>, not into Outline OEs.
    // H2-H6 render as OEs with quickStyleIndex="1" and inline <one:T style="..."> styling,
    // with the text wrapped in <span style='font-weight:bold'>.

    [Theory]
    [InlineData("## Heading 2", 2, "14.0pt", false)]
    [InlineData("### Heading 3", 3, "12.0pt", false)]
    [InlineData("#### Heading 4", 4, "11.0pt", false)]
    [InlineData("##### Heading 5", 5, "11.0pt", true)]
    [InlineData("###### Heading 6", 6, "11.0pt", true)]
    public void Convert_OutlineHeading_UsesInlineSegoeUiStyle(
        string markdown, int level, string expectedSize, bool expectItalic)
    {
        var result = _converter.Convert(markdown, pageTitle: "Test");
        var doc = ParseResult(result);

        var outline = doc.Root!.Element(OneNs + "Outline")!;
        var oes = outline.Descendants(OneNs + "OE").ToList();

        var headingText = $"Heading {level}";
        var matches = oes.Where(oe =>
        {
            if (oe.Attribute("quickStyleIndex")?.Value != "1") return false;
            var t = oe.Element(OneNs + "T");
            var styleAttr = t?.Attribute("style")?.Value;
            if (styleAttr == null) return false;
            var cdata = t!.Nodes().OfType<XCData>().FirstOrDefault()?.Value;
            if (cdata == null || !cdata.Contains(headingText)) return false;

            return styleAttr.Contains("font-family:'Segoe UI'") &&
                   styleAttr.Contains($"font-size:{expectedSize}") &&
                   styleAttr.Contains("color:#201F1E") &&
                   (!expectItalic || styleAttr.Contains("font-style:italic")) &&
                   cdata.Contains("<span style='font-weight:bold'>");
        }).ToList();

        matches.Should().NotBeEmpty(
            $"expected an OE qSI=\"1\" with inline Segoe UI {expectedSize} #201F1E " +
            $"style and bold-span CDATA for '{headingText}'");
    }

    [Fact]
    public void Convert_H1_GoesIntoPageTitleNotOutline()
    {
        var result = _converter.Convert("# Heading 1\n\nBody", pageTitle: null);
        var doc = ParseResult(result);

        var titleCdata = doc.Root!.Element(OneNs + "Title")!
            .Element(OneNs + "OE")!.Element(OneNs + "T")!
            .Nodes().OfType<XCData>().First().Value;
        titleCdata.Should().Be("Heading 1");

        // No OE in the Outline should carry an inline heading style or bold-span-only
        // CDATA with the H1 text — H1 is not duplicated into the body.
        var outlineCdata = doc.Root.Element(OneNs + "Outline")!
            .Descendants(OneNs + "T")
            .SelectMany(t => t.Nodes().OfType<XCData>())
            .Select(c => c.Value);
        outlineCdata.Should().NotContain(v => v.Contains("Heading 1"));
    }

    [Fact]
    public void Convert_Page_DefinesOnlyPageTitleAndParagraphQuickStyleDefs()
    {
        var result = _converter.Convert("# Anything\n\n## Section\n\nBody", pageTitle: "Test");
        var doc = ParseResult(result);

        var defs = doc.Root!.Elements(OneNs + "QuickStyleDef").ToList();
        defs.Should().HaveCount(2);

        defs[0].Attribute("index")!.Value.Should().Be("0");
        defs[0].Attribute("name")!.Value.Should().Be("PageTitle");
        defs[0].Attribute("font")!.Value.Should().Be("Calibri Light");
        defs[0].Attribute("fontSize")!.Value.Should().Be("20.0");

        defs[1].Attribute("index")!.Value.Should().Be("1");
        defs[1].Attribute("name")!.Value.Should().Be("p");
        defs[1].Attribute("font")!.Value.Should().Be("Calibri");
        defs[1].Attribute("fontSize")!.Value.Should().Be("11.0");
    }

    #endregion

    #region Page Title Tests

    [Fact]
    public void Convert_PageTitleFromH1_WhenNoTitleProvided()
    {
        // Arrange
        var markdown = "# My Title\n\nContent";

        // Act
        var result = _converter.Convert(markdown);
        var doc = ParseResult(result);

        // Assert
        doc.Root!.Attribute("name")?.Value.Should().Be("My Title");

        var title = doc.Root.Element(OneNs + "Title");
        var titleT = title!.Element(OneNs + "OE")!.Element(OneNs + "T");
        var cdata = titleT!.Nodes().OfType<XCData>().FirstOrDefault();
        cdata?.Value.Should().Contain("My Title");
    }

    [Fact]
    public void Convert_PageTitleParam_OverridesH1()
    {
        // Arrange
        var markdown = "# My Title\n\nContent";
        var explicitTitle = "Override Title";

        // Act
        var result = _converter.Convert(markdown, pageTitle: explicitTitle);
        var doc = ParseResult(result);

        // Assert
        doc.Root!.Attribute("name")?.Value.Should().Be("Override Title");

        var title = doc.Root.Element(OneNs + "Title");
        var titleT = title!.Element(OneNs + "OE")!.Element(OneNs + "T");
        var cdata = titleT!.Nodes().OfType<XCData>().FirstOrDefault();
        cdata?.Value.Should().Contain("Override Title");
    }

    #endregion

    #region Inline Formatting Tests

    [Fact]
    public void Convert_BoldText_RendersSpanFontWeightBold()
    {
        var result = _converter.Convert("This is **bold** text", pageTitle: "Test");
        var doc = ParseResult(result);
        var texts = doc.Descendants(OneNs + "T").Select(t => t.Value);
        texts.Should().Contain(t => t.Contains("<span style='font-weight:bold'>bold</span>"));
    }

    [Fact]
    public void Convert_ItalicText_RendersSpanFontStyleItalic()
    {
        var result = _converter.Convert("This is *italic* text", pageTitle: "Test");
        var doc = ParseResult(result);
        var texts = doc.Descendants(OneNs + "T").Select(t => t.Value);
        texts.Should().Contain(t => t.Contains("<span style='font-style:italic'>italic</span>"));
    }

    [Fact]
    public void Convert_StrikethroughText_RendersSpanTextDecorationLineThrough()
    {
        var result = _converter.Convert("This is ~~struck~~ text", pageTitle: "Test");
        var doc = ParseResult(result);
        var texts = doc.Descendants(OneNs + "T").Select(t => t.Value);
        texts.Should().Contain(t => t.Contains("<span style='text-decoration:line-through'>struck</span>"));
    }

    [Fact]
    public void Convert_InlineCode_RendersConsolas10pt()
    {
        var result = _converter.Convert("Use `myCommand` here", pageTitle: "Test");
        var doc = ParseResult(result);
        var texts = doc.Descendants(OneNs + "T").Select(t => t.Value);
        texts.Should().Contain(t =>
            t.Contains("font-family:Consolas") &&
            t.Contains("font-size:10.0pt") &&
            t.Contains("myCommand"));
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
    public void Convert_BoldAndItalic_RendersNestedSpans()
    {
        var result = _converter.Convert("This is ***bold italic*** text", pageTitle: "Test");
        var doc = ParseResult(result);
        var texts = doc.Descendants(OneNs + "T").Select(t => t.Value);
        texts.Should().Contain(t =>
            t.Contains("font-weight:bold") && t.Contains("font-style:italic"));
    }

    #endregion

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

    #region Code Block Tests

    [Fact]
    public void Convert_FencedCodeBlock_CreatesTableWithConsolaPerLineOes()
    {
        var markdown = "```\nvar x = 1;\nvar y = 2;\n```";
        var result = _converter.Convert(markdown, pageTitle: "Test");
        var doc = ParseResult(result);

        var tables = doc.Descendants(OneNs + "Table").ToList();
        tables.Should().NotBeEmpty();
        var table = tables.First();
        table.Attribute("bordersVisible")!.Value.Should().Be("true");
        table.Attribute("hasHeaderRow")!.Value.Should().Be("true");

        // Each code line is its own OE with Consolas 9pt style on the OE itself.
        var cellOes = table.Descendants(OneNs + "Cell").First()
            .Descendants(OneNs + "OE").ToList();
        cellOes.Should().HaveCountGreaterOrEqualTo(2);
        cellOes.Should().OnlyContain(oe =>
            oe.Attribute("style") != null &&
            oe.Attribute("style")!.Value.Contains("font-family:Consolas") &&
            oe.Attribute("style")!.Value.Contains("font-size:9.0pt"));

        var cdataValues = cellOes
            .SelectMany(oe => oe.Elements(OneNs + "T"))
            .SelectMany(t => t.Nodes().OfType<XCData>())
            .Select(c => c.Value)
            .ToList();
        cdataValues.Should().Contain(v => v == "var x = 1;");
        cdataValues.Should().Contain(v => v == "var y = 2;");
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
    public void Convert_FencedCodeBlock_PreservesMultipleLinesAsSeparateOEs()
    {
        var markdown = "```\nline1\nline2\nline3\n```";
        var result = _converter.Convert(markdown, pageTitle: "Test");
        var doc = ParseResult(result);

        var cellOes = doc.Descendants(OneNs + "Cell").First()
            .Descendants(OneNs + "OE").ToList();
        var cdataValues = cellOes
            .SelectMany(oe => oe.Elements(OneNs + "T"))
            .SelectMany(t => t.Nodes().OfType<XCData>())
            .Select(c => c.Value)
            .ToList();

        cdataValues.Should().Contain("line1");
        cdataValues.Should().Contain("line2");
        cdataValues.Should().Contain("line3");
    }

    #endregion

    #region Table Tests

    [Fact]
    public void Convert_SimpleTable_CreatesTableElement()
    {
        var markdown = "| Name | Age |\n|------|-----|\n| Alice | 30 |\n| Bob | 25 |";
        var result = _converter.Convert(markdown, pageTitle: "Test");
        var doc = ParseResult(result);

        var tables = doc.Descendants(OneNs + "Table");
        tables.Should().HaveCount(1);
        var table = tables.First();
        table.Attribute("bordersVisible")!.Value.Should().Be("true");
        table.Attribute("hasHeaderRow")!.Value.Should().Be("true");

        var columns = doc.Descendants(OneNs + "Column");
        columns.Should().HaveCount(2);

        var rows = doc.Descendants(OneNs + "Row");
        rows.Should().HaveCount(3);
    }

    [Fact]
    public void Convert_TableHeaderRow_WrapsTextInBoldSpan()
    {
        var markdown = "| Header1 | Header2 |\n|---------|----------|\n| Cell1 | Cell2 |";
        var result = _converter.Convert(markdown, pageTitle: "Test");
        var doc = ParseResult(result);

        var firstRowCells = doc.Descendants(OneNs + "Row").First()
            .Descendants(OneNs + "T");
        firstRowCells.Should().Contain(t => t.Value.Contains("<span style='font-weight:bold'>"));
    }

    #endregion

    #region Blockquote and HR Tests

    [Fact]
    public void Convert_Blockquote_UsesInlineItalicStyle()
    {
        var markdown = "> This is a quote";
        var result = _converter.Convert(markdown, pageTitle: "Test");
        var doc = ParseResult(result);

        // Blockquote OE: quickStyleIndex="1" with an inline italic style attribute.
        var quoteOes = doc.Descendants(OneNs + "OE")
            .Where(oe =>
                oe.Attribute("quickStyleIndex")?.Value == "1" &&
                oe.Attribute("style") is { } s &&
                s.Value.Contains("font-style:italic"))
            .ToList();
        quoteOes.Should().NotBeEmpty();

        var cdataValues = quoteOes
            .Elements(OneNs + "T")
            .SelectMany(t => t.Nodes().OfType<XCData>())
            .Select(c => c.Value);
        cdataValues.Should().Contain(v => v.Contains("This is a quote"));
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

    #region Spacing Tests

    // Helper — an empty/spacer OE is a qSI="1" OE whose single <one:T> CDATA is
    // empty or whitespace.
    private static bool IsSpacerOe(XElement oe)
    {
        if (oe.Attribute("quickStyleIndex")?.Value != "1") return false;
        var t = oe.Element(OneNs + "T");
        if (t == null) return false;
        // Reject any OE that also carries a List (list item), Table, or child OEChildren
        // — those are content, not spacers.
        if (oe.Element(OneNs + "List") != null) return false;
        if (oe.Element(OneNs + "Table") != null) return false;
        if (oe.Element(OneNs + "OEChildren") != null) return false;
        var cdata = t.Nodes().OfType<XCData>().FirstOrDefault()?.Value ?? "";
        return string.IsNullOrWhiteSpace(cdata);
    }

    [Fact]
    public void Convert_ParagraphFollowedByParagraph_EmitsSpacerOeBetween()
    {
        var markdown = "## Section\n\nFirst paragraph.\n\nSecond paragraph.";
        var result = _converter.Convert(markdown, pageTitle: "Test", collapsible: true);
        var doc = ParseResult(result);

        var section = FindHeadingOesBySize(doc, "14.0pt").First();
        var children = section.Element(OneNs + "OEChildren")!
            .Elements(OneNs + "OE").ToList();

        // Expect at least: first paragraph, spacer, second paragraph, spacer.
        children.Should().HaveCountGreaterOrEqualTo(4);
        IsSpacerOe(children[1]).Should().BeTrue("a blank spacer OE should follow the first paragraph");
    }

    [Fact]
    public void Convert_TableAndCodeBlock_FollowedBySpacerOe()
    {
        var markdown = "## Section\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\n```\ncode\n```\n\nTrailing.";
        var result = _converter.Convert(markdown, pageTitle: "Test", collapsible: true);
        var doc = ParseResult(result);

        var section = FindHeadingOesBySize(doc, "14.0pt").First();
        var children = section.Element(OneNs + "OEChildren")!
            .Elements(OneNs + "OE").ToList();

        // The OE that wraps the GFM Table and the OE that wraps the code-block
        // Table should both be followed by a spacer.
        for (int i = 0; i < children.Count - 1; i++)
        {
            if (children[i].Element(OneNs + "Table") != null)
            {
                IsSpacerOe(children[i + 1]).Should().BeTrue(
                    $"child at index {i} wraps a Table and should be followed by a spacer");
            }
        }
    }

    [Fact]
    public void Convert_HeadingOe_IsNotFollowedByStrayTopLevelSpacer()
    {
        // Heading OEs themselves should NOT emit a spacer after them at the
        // sibling level — content nests inside the heading's OEChildren, and
        // headings already have visual lead-in from the preceding block's spacer.
        var markdown = "## First\n\nContent A\n\n## Second\n\nContent B";
        var result = _converter.Convert(markdown, pageTitle: "Test", collapsible: true);
        var doc = ParseResult(result);

        var outline = doc.Root!.Element(OneNs + "Outline")!.Element(OneNs + "OEChildren")!;
        var topLevel = outline.Elements(OneNs + "OE").ToList();

        // Expect both headings to still be present and no spacer OE directly
        // between them — the spacer lives inside the first heading's children.
        topLevel.Where(oe => !IsSpacerOe(oe)).Should().HaveCount(2);
    }

    #endregion

    #region Collapsible Nesting Tests

    // A heading OE is identified by the inline font-size on its <one:T style="...">.
    // H2 -> 14.0pt, H3 -> 12.0pt, H4-H6 -> 11.0pt.
    private static IEnumerable<XElement> FindHeadingOesBySize(XDocument doc, string fontSize)
    {
        return doc.Descendants(OneNs + "OE").Where(oe =>
        {
            var t = oe.Element(OneNs + "T");
            var style = t?.Attribute("style")?.Value;
            return style != null &&
                   style.Contains("font-family:'Segoe UI'") &&
                   style.Contains($"font-size:{fontSize}") &&
                   style.Contains("color:#201F1E");
        });
    }

    [Fact]
    public void Convert_CollapsibleEnabled_NestsContentUnderHeadings()
    {
        var markdown = "## Section\n\nParagraph under section\n\n### Sub\n\nParagraph under sub";
        var result = _converter.Convert(markdown, pageTitle: "Test", collapsible: true);
        var doc = ParseResult(result);

        var h2Oe = FindHeadingOesBySize(doc, "14.0pt").FirstOrDefault();
        h2Oe.Should().NotBeNull();
        h2Oe!.Elements(OneNs + "OEChildren").Should().NotBeEmpty(
            "content after H2 should be nested inside it as OEChildren");
    }

    [Fact]
    public void Convert_CollapsibleDisabled_FlatStructure()
    {
        var markdown = "## Section\n\nParagraph under section";
        var result = _converter.Convert(markdown, pageTitle: "Test", collapsible: false);
        var doc = ParseResult(result);

        var h2Oe = FindHeadingOesBySize(doc, "14.0pt").FirstOrDefault();
        h2Oe.Should().NotBeNull();
        h2Oe!.Elements(OneNs + "OEChildren").Should().BeEmpty(
            "collapsible is disabled, so content should be flat siblings");
    }

    #endregion

    #region Image Tests

    [Fact]
    public void Convert_LocalImage_EmbedsBase64Data()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "onenote_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var pngBytes = new byte[]
            {
                0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
                0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
                0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
                0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
                0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41,
                0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
                0x00, 0x00, 0x02, 0x00, 0x01, 0xE2, 0x21, 0xBC,
                0x33, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
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

            result.Should().Contain("(Image not found: missing.png)");
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

    #region Sample File Tests

    [Theory]
    [InlineData("basic-formatting.md", "Basic Formatting Test")]
    [InlineData("lists-and-tables.md", "Lists and Tables")]
    [InlineData("code-and-quotes.md", "Code Blocks and Quotes")]
    [InlineData("collapsible-sections.md", "Project Documentation")]
    public void Convert_SampleFile_ProducesValidXml(string filename, string expectedTitle)
    {
        var samplesDir = Path.Combine(FindRepoRoot(), "samples");
        var filePath = Path.Combine(samplesDir, filename);

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Sample file not found: {filePath}");

        var markdown = File.ReadAllText(filePath);
        var result = _converter.Convert(markdown, basePath: samplesDir);
        var doc = ParseResult(result);

        doc.Root!.Name.Should().Be(OneNs + "Page");
        doc.Root.Attribute("name")!.Value.Should().Be(expectedTitle);
        doc.Descendants(OneNs + "Title").Should().HaveCount(1);
        doc.Descendants(OneNs + "Outline").Should().HaveCount(1);
        doc.Descendants(OneNs + "T").Should().NotBeEmpty();
    }

    [Fact]
    public void Convert_SampleWithImage_EmbedsImageData()
    {
        var samplesDir = Path.Combine(FindRepoRoot(), "samples");
        var filePath = Path.Combine(samplesDir, "with-image.md");

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Sample file not found: {filePath}");

        var markdown = File.ReadAllText(filePath);
        var result = _converter.Convert(markdown, basePath: samplesDir);
        var doc = ParseResult(result);

        // Should have at least one embedded image
        doc.Descendants(OneNs + "Image").Should().NotBeEmpty();
        doc.Descendants(OneNs + "Data").Should().NotBeEmpty();

        // Should have placeholder for missing image
        doc.Descendants(OneNs + "T").Select(t => t.Value)
            .Should().Contain(t => t.Contains("(Image not found:"));
    }

    [Fact]
    public void Convert_ReferenceMarkdown_MatchesReferenceShape()
    {
        // Golden-file test: structural shape, not byte equality.
        // See docs/reference-page/Reference-page.xml for the target OneNote rendering.
        var refDir = Path.Combine(FindRepoRoot(), "docs", "reference-page");
        var mdPath = Path.Combine(refDir, "MarkDow_VisualRef1.md");
        if (!File.Exists(mdPath))
            throw new FileNotFoundException($"Reference markdown not found: {mdPath}");

        var markdown = File.ReadAllText(mdPath);
        var xml = _converter.Convert(markdown, basePath: refDir);
        var doc = ParseResult(xml);

        // 1. Exactly two QuickStyleDefs: PageTitle (0) and p (1).
        var defs = doc.Root!.Elements(OneNs + "QuickStyleDef").ToList();
        defs.Should().HaveCount(2);
        defs[0].Attribute("name")!.Value.Should().Be("PageTitle");
        defs[1].Attribute("name")!.Value.Should().Be("p");

        // 2. Page title taken from the leading H1.
        doc.Root.Attribute("name")!.Value.Should()
            .Be("Migrating AD Accounts from RC4 to AES Kerberos Encryption");

        // 3. Every section heading (H2) is an OE with qSI="1" and inline
        //    Segoe UI 14pt #201F1E style on <one:T>, wrapping text in a bold span.
        // Distinctive ASCII fragments that should survive HTML-encoding intact
        // (the converter encodes apostrophes etc., so we avoid them here).
        var expectedSectionFragments = new[]
        {
            "Happening",
            "SupportedEncryptionTypes",
            "How to Migrate an Account",
            "Finding Accounts That Need Attention",
            "Monitoring After Changes",
            "If Something Breaks",
            "References"
        };
        foreach (var fragment in expectedSectionFragments)
        {
            var matches = FindHeadingOesBySize(doc, "14.0pt").Where(oe =>
            {
                var cdata = oe.Element(OneNs + "T")!
                    .Nodes().OfType<XCData>().First().Value;
                return cdata.Contains(fragment) &&
                       cdata.Contains("<span style='font-weight:bold'>");
            });
            matches.Should().NotBeEmpty(
                $"expected a 14pt Segoe UI bold heading containing '{fragment}'");
        }

        // 4. All fenced code blocks become one-column Tables with per-line
        //    Consolas-9pt OEs inside the cell.
        var codeTables = doc.Descendants(OneNs + "Table").Where(t =>
            t.Element(OneNs + "Columns")?.Elements(OneNs + "Column").Count() == 1).ToList();
        codeTables.Should().NotBeEmpty("markdown has fenced code blocks");
        foreach (var table in codeTables)
        {
            var cellOes = table.Descendants(OneNs + "Cell").First()
                .Descendants(OneNs + "OE");
            cellOes.Should().OnlyContain(oe =>
                oe.Attribute("style") != null &&
                oe.Attribute("style")!.Value.Contains("font-family:Consolas") &&
                oe.Attribute("style")!.Value.Contains("font-size:9.0pt"));
        }

        // 5. Markdown tables keep hasHeaderRow and bold-span header cells.
        var gfmTables = doc.Descendants(OneNs + "Table").Where(t =>
            (t.Element(OneNs + "Columns")?.Elements(OneNs + "Column").Count() ?? 0) > 1).ToList();
        gfmTables.Should().NotBeEmpty("markdown has 2-column and 3-column tables");
        foreach (var table in gfmTables)
        {
            table.Attribute("hasHeaderRow")!.Value.Should().Be("true");
            var headerRow = table.Elements(OneNs + "Row").First();
            var headerCdata = headerRow.Descendants(OneNs + "T")
                .SelectMany(t => t.Nodes().OfType<XCData>())
                .Select(c => c.Value);
            headerCdata.Should().OnlyContain(v => v.Contains("<span style='font-weight:bold'>"));
        }

        // 6. Blockquote rendered as inline italic style, not qSI="8".
        doc.Descendants(OneNs + "OE")
            .Where(oe => oe.Attribute("quickStyleIndex")?.Value == "8")
            .Should().BeEmpty("no legacy 'quote' QuickStyleDef should be referenced");

        // 7. Reference links render as anchors.
        var anchorTexts = doc.Descendants(OneNs + "T")
            .SelectMany(t => t.Nodes().OfType<XCData>())
            .Select(c => c.Value)
            .Where(v => v.Contains("<a href=\""))
            .ToList();
        anchorTexts.Should().HaveCountGreaterOrEqualTo(5,
            "References section has 5 links");
    }

    [Fact]
    public void DumpSampleXml_ForManualInspection()
    {
        var repoRoot = FindRepoRoot();
        var samplesDir = Path.Combine(repoRoot, "samples");
        var outputDir = Path.Combine(samplesDir, "output");
        Directory.CreateDirectory(outputDir);

        foreach (var file in Directory.GetFiles(samplesDir, "*.md"))
        {
            var markdown = File.ReadAllText(file);
            var xml = _converter.Convert(markdown, basePath: samplesDir);
            var outputPath = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(file) + ".xml");
            File.WriteAllText(outputPath, xml);
        }

        // Also emit a converted version of the visual reference so the dev can
        // diff it against docs/reference-page/Reference-page.xml by eye.
        var refDir = Path.Combine(repoRoot, "docs", "reference-page");
        var refMd = Path.Combine(refDir, "MarkDow_VisualRef1.md");
        if (File.Exists(refMd))
        {
            var xml = _converter.Convert(File.ReadAllText(refMd), basePath: refDir);
            File.WriteAllText(
                Path.Combine(refDir, "MarkDow_VisualRef1.converted.xml"), xml);
        }

        Directory.GetFiles(outputDir, "*.xml").Length.Should().BeGreaterThan(0);
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "samples")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        // Fallback: walk up from current directory
        dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "samples")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Could not find repo root with samples/ directory");
    }

    #endregion

    #region Spell-check suppression

    [Fact]
    public void Convert_EmitsLangYoOnPage_ToSuppressSpellCheck()
    {
        var result = _converter.Convert("body", pageTitle: "Test");
        var doc = ParseResult(result);

        doc.Root!.Attribute("lang")?.Value.Should().Be("yo");
    }

    #endregion
}
