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

    [Theory]
    [InlineData("# Heading 1", 1, "1")]
    [InlineData("## Heading 2", 2, "2")]
    [InlineData("### Heading 3", 3, "3")]
    [InlineData("#### Heading 4", 4, "4")]
    [InlineData("##### Heading 5", 5, "5")]
    [InlineData("###### Heading 6", 6, "6")]
    public void Convert_Heading_AppliesQuickStyleIndex(string markdown, int level, string expectedIndex)
    {
        // Arrange & Act
        var result = _converter.Convert(markdown, pageTitle: "Test");
        var doc = ParseResult(result);

        // Assert
        var outline = doc.Root!.Element(OneNs + "Outline");
        outline.Should().NotBeNull();

        var oeChildren = outline!.Element(OneNs + "OEChildren");
        oeChildren.Should().NotBeNull();

        var oes = oeChildren!.Elements(OneNs + "OE").ToList();

        // Each heading OE references its corresponding QuickStyleDef (h1 -> index 1, etc.)
        // so OneNote renders it natively as "Heading N" with proper font, color and spacing.
        var headingText = $"Heading {level}";
        var hasCorrectHeading = oes.Any(oe =>
        {
            var t = oe.Element(OneNs + "T");
            if (t == null) return false;
            var cdata = t.Nodes().OfType<XCData>().FirstOrDefault();
            if (cdata == null) return false;
            return cdata.Value.Contains(headingText) &&
                   oe.Attribute("quickStyleIndex")?.Value == expectedIndex;
        });
        hasCorrectHeading.Should().BeTrue(
            $"expected an OE with quickStyleIndex=\"{expectedIndex}\" and CDATA containing '{headingText}'");
    }

    [Fact]
    public void Convert_Page_DefinesQuickStyleDefs()
    {
        var result = _converter.Convert("# Anything", pageTitle: "Test");
        var doc = ParseResult(result);

        var defs = doc.Root!.Elements(OneNs + "QuickStyleDef").ToList();
        defs.Should().NotBeEmpty();

        var names = defs.Select(d => d.Attribute("name")?.Value).ToList();
        names.Should().Contain("PageTitle");
        names.Should().Contain("h1");
        names.Should().Contain("p");
        names.Should().Contain("code");
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
        rows.Should().HaveCount(3);
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

    #region Blockquote and HR Tests

    [Fact]
    public void Convert_Blockquote_UsesQuoteQuickStyle()
    {
        var markdown = "> This is a quote";
        var result = _converter.Convert(markdown, pageTitle: "Test");
        var doc = ParseResult(result);

        // Blockquote OE uses quickStyleIndex="8" (the "quote" style, italic).
        var quoteOes = doc.Descendants(OneNs + "OE")
            .Where(oe => oe.Attribute("quickStyleIndex")?.Value == "8")
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

    #region Collapsible Nesting Tests

    // A heading OE is identified by its quickStyleIndex value.
    private static IEnumerable<XElement> FindHeadingOesByQuickStyle(XDocument doc, string quickStyleIndex)
    {
        return doc.Descendants(OneNs + "OE")
            .Where(oe => oe.Attribute("quickStyleIndex")?.Value == quickStyleIndex);
    }

    [Fact]
    public void Convert_CollapsibleEnabled_NestsContentUnderHeadings()
    {
        var markdown = "## Section\n\nParagraph under section\n\n### Sub\n\nParagraph under sub";
        var result = _converter.Convert(markdown, pageTitle: "Test", collapsible: true);
        var doc = ParseResult(result);

        // H2 -> quickStyleIndex "2"
        var h2Oes = FindHeadingOesByQuickStyle(doc, "2");
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

        var h2Oes = FindHeadingOesByQuickStyle(doc, "2");
        h2Oes.Should().NotBeEmpty();

        var h2Oe = h2Oes.First();
        h2Oe.Elements(OneNs + "OEChildren").Should().BeEmpty(
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
    public void DumpSampleXml_ForManualInspection()
    {
        var samplesDir = Path.Combine(FindRepoRoot(), "samples");
        var outputDir = Path.Combine(samplesDir, "output");
        Directory.CreateDirectory(outputDir);

        foreach (var file in Directory.GetFiles(samplesDir, "*.md"))
        {
            var markdown = File.ReadAllText(file);
            var xml = _converter.Convert(markdown, basePath: samplesDir);
            var outputPath = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(file) + ".xml");
            File.WriteAllText(outputPath, xml);
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
}
