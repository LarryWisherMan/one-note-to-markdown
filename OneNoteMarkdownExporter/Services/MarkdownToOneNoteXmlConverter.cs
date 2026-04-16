using System.Text;
using System.Xml.Linq;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace OneNoteMarkdownExporter.Services;

/// <summary>
/// Converts Markdown to OneNote page XML using the Markdig AST.
/// This is the reverse of OneNoteXmlToMarkdownConverter.
/// </summary>
public class MarkdownToOneNoteXmlConverter
{
    private static readonly XNamespace OneNs = "http://schemas.microsoft.com/office/onenote/2013/onenote";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    /// <summary>
    /// Heading style definitions: level -> (size, bold, italic).
    /// </summary>
    private static readonly Dictionary<int, (string Size, bool Bold, bool Italic)> HeadingStyles = new()
    {
        { 1, ("20.0pt", true, false) },
        { 2, ("16.0pt", true, false) },
        { 3, ("13.0pt", true, false) },
        { 4, ("12.0pt", true, false) },
        { 5, ("11.0pt", true, false) },
        { 6, ("11.0pt", true, true) },
    };

    /// <summary>
    /// Converts Markdown text to OneNote page XML.
    /// </summary>
    /// <param name="markdown">The Markdown source text.</param>
    /// <param name="pageTitle">Optional page title. If null, extracted from first H1 or defaults to "Untitled".</param>
    /// <param name="collapsible">When true, content between headings nests inside heading OEChildren.</param>
    /// <param name="basePath">Optional base path for resolving relative image paths.</param>
    /// <returns>OneNote page XML string.</returns>
    public string Convert(string markdown, string? pageTitle = null, bool collapsible = true, string? basePath = null)
    {
        var document = Markdown.Parse(markdown, Pipeline);

        // Determine page title
        var resolvedTitle = pageTitle ?? ExtractFirstH1(document) ?? "Untitled";

        // Build content elements
        var contentElements = ConvertBlocks(document, collapsible);

        // Build the page XML
        var page = new XElement(OneNs + "Page",
            new XAttribute("name", resolvedTitle),
            new XElement(OneNs + "Title",
                new XElement(OneNs + "OE",
                    new XElement(OneNs + "T",
                        new XCData(resolvedTitle)))),
            new XElement(OneNs + "Outline",
                new XElement(OneNs + "OEChildren", contentElements)));

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            page);

        return doc.ToString();
    }

    /// <summary>
    /// Extracts the plain text from the first H1 heading in the document.
    /// </summary>
    private static string? ExtractFirstH1(MarkdownDocument document)
    {
        foreach (var block in document)
        {
            if (block is HeadingBlock heading && heading.Level == 1)
            {
                return GetInlineText(heading.Inline);
            }
        }
        return null;
    }

    /// <summary>
    /// Converts all blocks in the document to OneNote OE elements.
    /// </summary>
    private List<XElement> ConvertBlocks(MarkdownDocument document, bool collapsible)
    {
        if (!collapsible)
        {
            return ConvertBlocksFlat(document);
        }

        return ConvertBlocksCollapsible(document);
    }

    /// <summary>
    /// Flat conversion: each block becomes a top-level OE.
    /// </summary>
    private List<XElement> ConvertBlocksFlat(MarkdownDocument document)
    {
        var elements = new List<XElement>();
        foreach (var block in document)
        {
            var el = ConvertBlock(block);
            if (el != null)
            {
                elements.Add(el);
            }
        }
        return elements;
    }

    /// <summary>
    /// Collapsible conversion: content between headings nests inside heading OEChildren.
    /// Uses a stack-based approach where headings push onto the stack and pop when
    /// a same-or-higher level heading appears.
    /// </summary>
    private List<XElement> ConvertBlocksCollapsible(MarkdownDocument document)
    {
        var topLevel = new List<XElement>();
        // Stack of (headingLevel, headingOE, childrenContainer)
        var stack = new Stack<(int Level, XElement Oe, XElement Children)>();

        foreach (var block in document)
        {
            if (block is HeadingBlock heading)
            {
                var headingOe = CreateHeadingOe(heading);
                var children = new XElement(OneNs + "OEChildren");

                // Pop headings of same or lower priority (same or higher level number)
                while (stack.Count > 0 && stack.Peek().Level >= heading.Level)
                {
                    var popped = stack.Pop();
                    // Only add OEChildren if it has content
                    if (popped.Children.HasElements)
                    {
                        popped.Oe.Add(popped.Children);
                    }
                }

                // Add this heading to current parent
                if (stack.Count > 0)
                {
                    stack.Peek().Children.Add(headingOe);
                }
                else
                {
                    topLevel.Add(headingOe);
                }

                stack.Push((heading.Level, headingOe, children));
            }
            else
            {
                var el = ConvertBlock(block);
                if (el != null)
                {
                    if (stack.Count > 0)
                    {
                        stack.Peek().Children.Add(el);
                    }
                    else
                    {
                        topLevel.Add(el);
                    }
                }
            }
        }

        // Flush remaining stack
        while (stack.Count > 0)
        {
            var popped = stack.Pop();
            if (popped.Children.HasElements)
            {
                popped.Oe.Add(popped.Children);
            }
        }

        return topLevel;
    }

    /// <summary>
    /// Converts a single Markdown block to a OneNote OE element.
    /// </summary>
    private XElement? ConvertBlock(Block block)
    {
        try
        {
            return block switch
            {
                HeadingBlock heading => CreateHeadingOe(heading),
                ParagraphBlock paragraph => CreateParagraphOe(paragraph),
                // Fall back to plain text for unrecognized block types
                _ => CreatePlainTextOe(block)
            };
        }
        catch
        {
            // Never crash on unrecognized block types
            return CreatePlainTextOe(block);
        }
    }

    /// <summary>
    /// Creates a heading OE with appropriate font styling.
    /// </summary>
    private XElement CreateHeadingOe(HeadingBlock heading)
    {
        var text = RenderInlineHtml(heading.Inline);
        var style = GetHeadingStyle(heading.Level);

        var styledText = $"<span style='{style}'>{text}</span>";

        return new XElement(OneNs + "OE",
            new XElement(OneNs + "T",
                new XCData(styledText)));
    }

    /// <summary>
    /// Creates a paragraph OE from a paragraph block.
    /// </summary>
    private XElement CreateParagraphOe(ParagraphBlock paragraph)
    {
        var html = RenderInlineHtml(paragraph.Inline);

        return new XElement(OneNs + "OE",
            new XElement(OneNs + "T",
                new XCData(html)));
    }

    /// <summary>
    /// Creates a plain-text OE fallback for unrecognized block types.
    /// </summary>
    private XElement CreatePlainTextOe(Block block)
    {
        // Try to extract any text content from the block
        string text;
        if (block is LeafBlock leaf)
        {
            text = GetInlineText(leaf.Inline) ?? "";
        }
        else
        {
            text = block.ToString() ?? "";
        }

        return new XElement(OneNs + "OE",
            new XElement(OneNs + "T",
                new XCData(System.Net.WebUtility.HtmlEncode(text))));
    }

    /// <summary>
    /// Builds the CSS style string for a heading level.
    /// </summary>
    private static string GetHeadingStyle(int level)
    {
        if (!HeadingStyles.TryGetValue(level, out var style))
        {
            style = ("11.0pt", true, false); // default fallback
        }

        var sb = new StringBuilder();
        sb.Append($"font-family:Segoe UI;font-size:{style.Size};font-weight:bold");
        if (style.Italic)
        {
            sb.Append(";font-style:italic");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Renders inline content as HTML suitable for OneNote CDATA sections.
    /// </summary>
    private static string RenderInlineHtml(ContainerInline? container)
    {
        if (container == null) return "";

        var sb = new StringBuilder();
        foreach (var inline in container)
        {
            sb.Append(RenderSingleInline(inline));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Renders a single inline element to HTML.
    /// </summary>
    private static string RenderSingleInline(Inline inline)
    {
        return inline switch
        {
            LiteralInline literal => System.Net.WebUtility.HtmlEncode(literal.ToString()),
            EmphasisInline emphasis => RenderEmphasis(emphasis),
            CodeInline code => $"<span style='font-family:Consolas;font-size:9pt'>{System.Net.WebUtility.HtmlEncode(code.Content)}</span>",
            LinkInline link => RenderLink(link),
            LineBreakInline => "<br/>",
            HtmlInline html => html.Tag,
            // Strikethrough from Markdig extensions
            _ when inline.GetType().Name == "SmartyPant" => inline.ToString() ?? "",
            _ => System.Net.WebUtility.HtmlEncode(inline.ToString() ?? "")
        };
    }

    /// <summary>
    /// Renders emphasis (bold/italic) inline.
    /// </summary>
    private static string RenderEmphasis(EmphasisInline emphasis)
    {
        var inner = new StringBuilder();
        foreach (var child in emphasis)
        {
            inner.Append(RenderSingleInline(child));
        }

        var content = inner.ToString();

        // DelimiterChar == '*' or '_', DelimiterCount determines bold vs italic
        if (emphasis.DelimiterChar is '*' or '_')
        {
            if (emphasis.DelimiterCount == 2)
            {
                return $"<b>{content}</b>";
            }
            return $"<i>{content}</i>";
        }

        // Strikethrough uses '~'
        if (emphasis.DelimiterChar == '~')
        {
            return $"<del>{content}</del>";
        }

        return content;
    }

    /// <summary>
    /// Renders a link inline. Images produce placeholder text for now.
    /// </summary>
    private static string RenderLink(LinkInline link)
    {
        if (link.IsImage)
        {
            var alt = GetInlineText(link) ?? "image";
            return $"[Image: {System.Net.WebUtility.HtmlEncode(alt)}]";
        }

        var text = new StringBuilder();
        foreach (var child in link)
        {
            text.Append(RenderSingleInline(child));
        }

        var url = link.Url ?? "";
        return $"<a href=\"{System.Net.WebUtility.HtmlEncode(url)}\">{text}</a>";
    }

    /// <summary>
    /// Extracts plain text from an inline container, stripping all formatting.
    /// </summary>
    internal static string? GetInlineText(ContainerInline? container)
    {
        if (container == null) return null;

        var sb = new StringBuilder();
        foreach (var inline in container)
        {
            AppendInlineText(inline, sb);
        }

        var result = sb.ToString();
        return string.IsNullOrEmpty(result) ? null : result;
    }

    /// <summary>
    /// Recursively appends plain text from an inline element.
    /// </summary>
    private static void AppendInlineText(Inline inline, StringBuilder sb)
    {
        switch (inline)
        {
            case LiteralInline literal:
                sb.Append(literal.ToString());
                break;
            case ContainerInline container:
                foreach (var child in container)
                {
                    AppendInlineText(child, sb);
                }
                break;
            case CodeInline code:
                sb.Append(code.Content);
                break;
            case LineBreakInline:
                sb.Append(' ');
                break;
            default:
                sb.Append(inline.ToString());
                break;
        }
    }
}
