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
    [InlineData("# Heading 1", 1, "20.0pt")]
    [InlineData("## Heading 2", 2, "16.0pt")]
    [InlineData("### Heading 3", 3, "13.0pt")]
    public void Convert_Heading_AppliesCorrectFontSize(string markdown, int level, string expectedSize)
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

        // Find the OE that has a T with the heading text and correct font-size style
        var headingText = $"Heading {level}";
        var hasCorrectHeading = oes.Any(oe =>
        {
            var t = oe.Element(OneNs + "T");
            if (t == null) return false;
            var cdata = t.Nodes().OfType<XCData>().FirstOrDefault();
            if (cdata == null) return false;
            return cdata.Value.Contains(headingText) &&
                   cdata.Value.Contains($"font-size:{expectedSize}") &&
                   cdata.Value.Contains("font-weight:bold");
        });
        hasCorrectHeading.Should().BeTrue(
            $"expected an OE > T with '{headingText}' styled at font-size:{expectedSize} and font-weight:bold");
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
}
