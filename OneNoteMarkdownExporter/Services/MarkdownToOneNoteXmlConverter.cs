using System.IO;
using System.Text;
using System.Xml.Linq;
using Markdig;
using Markdig.Extensions.Tables;
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
    /// Base path used to resolve relative image paths during a conversion call.
    /// </summary>
    private string? _basePath;

    /// <summary>
    /// Accumulates image XElements produced while rendering inlines inside a paragraph.
    /// Non-null only during <see cref="CreateParagraphElement"/> execution.
    /// </summary>
    private List<XElement>? _pendingImageElements;

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
        _basePath = basePath;
        var document = Markdown.Parse(markdown, Pipeline);

        // Determine page title
        var resolvedTitle = pageTitle ?? ExtractFirstH1(document) ?? "Untitled";

        // Build content elements
        var contentElements = ConvertBlocks(document, collapsible);

        // Build the page XML with explicit one: prefix (required by OneNote COM API)
        var page = new XElement(OneNs + "Page",
            new XAttribute(XNamespace.Xmlns + "one", OneNs.NamespaceName),
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

        return doc.Declaration + "\n" + doc.Root!.ToString();
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
    /// Adds a converted element to a list, unwrapping OEChildren containers
    /// so their children become direct siblings instead of nested containers.
    /// </summary>
    private void AddElement(List<XElement> target, XElement? element)
    {
        if (element == null) return;

        // If the element is a bare OEChildren, unwrap it — its children
        // should be siblings in the parent OEChildren, not nested.
        if (element.Name == OneNs + "OEChildren" && element.Parent == null)
        {
            foreach (var child in element.Elements().ToList())
            {
                child.Remove();
                target.Add(child);
            }
        }
        else
        {
            target.Add(element);
        }
    }

    /// <summary>
    /// Wraps an element (like Table) inside an OE, as required by OneNote XML schema.
    /// </summary>
    private XElement WrapInOe(XElement element)
    {
        return new XElement(OneNs + "OE", element);
    }

    /// <summary>
    /// Same as AddElement but for an XElement target (e.g., an OEChildren container).
    /// </summary>
    private void AddElementToXContainer(XElement target, XElement? element)
    {
        if (element == null) return;

        if (element.Name == OneNs + "OEChildren" && element.Parent == null)
        {
            foreach (var child in element.Elements().ToList())
            {
                child.Remove();
                target.Add(child);
            }
        }
        else
        {
            target.Add(element);
        }
    }

    /// <summary>
    /// Flat conversion: each block becomes a top-level OE.
    /// </summary>
    private List<XElement> ConvertBlocksFlat(MarkdownDocument document)
    {
        var elements = new List<XElement>();
        foreach (var block in document)
        {
            AddElement(elements, ConvertBlock(block));
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
                        // Unwrap OEChildren so list items become siblings, not nested containers
                        AddElementToXContainer(stack.Peek().Children, el);
                    }
                    else
                    {
                        AddElement(topLevel, el);
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
                FencedCodeBlock codeBlock => WrapInOe(CreateCodeBlockElement(codeBlock)),
                ListBlock listBlock => CreateListElements(listBlock),
                Markdig.Extensions.Tables.Table table => WrapInOe(CreateTableElement(table)),
                QuoteBlock quoteBlock => CreateBlockquoteElement(quoteBlock),
                ThematicBreakBlock => CreateHorizontalRuleElement(),
                // Skip metadata blocks that have no visual content
                LinkReferenceDefinitionGroup => null,
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

        return new XElement(OneNs + "OE",
            new XAttribute("style", style),
            new XElement(OneNs + "T",
                new XCData(text)));
    }

    /// <summary>
    /// Creates a paragraph OE from a paragraph block.
    /// When the paragraph contains embedded local images, the image XElements are
    /// collected via <see cref="_pendingImageElements"/> and returned alongside (or
    /// instead of) the text OE.
    /// </summary>
    private XElement CreateParagraphOe(ParagraphBlock paragraph)
    {
        return CreateParagraphElement(paragraph);
    }

    /// <summary>
    /// Core paragraph rendering that handles pending image elements.
    /// </summary>
    private XElement CreateParagraphElement(ParagraphBlock paragraph)
    {
        _pendingImageElements = new List<XElement>();
        var html = RenderInlineHtml(paragraph.Inline);
        var pending = _pendingImageElements;
        _pendingImageElements = null;

        // Wrap each image element in an OE (OneNote schema requires Image to be inside OE)
        if (pending.Count > 0 && string.IsNullOrWhiteSpace(html))
        {
            if (pending.Count == 1) return new XElement(OneNs + "OE", pending[0]);
            var container = new XElement(OneNs + "OEChildren");
            foreach (var img in pending) container.Add(new XElement(OneNs + "OE", img));
            return container;
        }

        var oe = new XElement(OneNs + "OE",
            new XElement(OneNs + "T", new XCData(html))
        );

        if (pending.Count > 0)
        {
            var container = new XElement(OneNs + "OEChildren", oe);
            foreach (var img in pending) container.Add(new XElement(OneNs + "OE", img));
            return container;
        }

        return oe;
    }

    /// <summary>
    /// Creates a bordered single-cell table for a fenced code block, using Consolas font.
    /// </summary>
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

    /// <summary>
    /// Creates a wrapper OE containing all list items as children.
    /// Top-level lists return a single OE with list items inside OEChildren,
    /// which is valid as a direct child of another OEChildren.
    /// </summary>
    private XElement CreateListElements(ListBlock listBlock)
    {
        // Build list items as OE elements
        var items = new List<XElement>();
        foreach (var item in listBlock)
        {
            if (item is ListItemBlock listItem)
            {
                items.Add(CreateListItemElement(listItem, listBlock.IsOrdered));
            }
        }

        // Wrap in an OEChildren so this can be placed inside an OE (for nesting)
        // or directly as content
        return new XElement(OneNs + "OEChildren", items);
    }

    /// <summary>
    /// Returns individual list item OE elements (for embedding in an existing OEChildren).
    /// </summary>
    private List<XElement> CreateListItemElements(ListBlock listBlock)
    {
        var items = new List<XElement>();
        foreach (var item in listBlock)
        {
            if (item is ListItemBlock listItem)
            {
                items.Add(CreateListItemElement(listItem, listBlock.IsOrdered));
            }
        }
        return items;
    }

    /// <summary>
    /// Creates a single list item OE, with optional nested OEChildren for sub-lists.
    /// </summary>
    private XElement CreateListItemElement(ListItemBlock listItem, bool isOrdered)
    {
        var oe = new XElement(OneNs + "OE");

        if (isOrdered)
        {
            oe.Add(new XElement(OneNs + "List",
                new XElement(OneNs + "Number",
                    new XAttribute("numberSequence", "0"),
                    new XAttribute("numberFormat", "##."),
                    new XAttribute("fontSize", "11.0"),
                    new XAttribute("font", "Segoe UI")
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

        foreach (var child in listItem)
        {
            if (child is ParagraphBlock paragraph)
            {
                oe.Add(new XElement(OneNs + "T", new XCData(RenderInlineHtml(paragraph.Inline))));
            }
            else if (child is ListBlock nestedList)
            {
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

    #region Blockquote and HR Support

    /// <summary>
    /// Creates an OEChildren container for a blockquote, rendering each paragraph as italic.
    /// </summary>
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

    /// <summary>
    /// Creates a horizontal rule OE containing dashes.
    /// </summary>
    private XElement CreateHorizontalRuleElement()
    {
        return new XElement(OneNs + "OE",
            new XElement(OneNs + "T", new XCData("---"))
        );
    }

    #endregion

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
    /// Instance method so image rendering can access <see cref="_basePath"/> and
    /// <see cref="_pendingImageElements"/>.
    /// </summary>
    private string RenderInlineHtml(ContainerInline? container)
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
    /// Instance method so image rendering can access instance state.
    /// </summary>
    private string RenderSingleInline(Inline inline)
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
    private string RenderEmphasis(EmphasisInline emphasis)
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
    /// Renders a link inline. For image links, delegates to <see cref="RenderImage"/>.
    /// </summary>
    private string RenderLink(LinkInline link)
    {
        if (link.IsImage)
        {
            return RenderImage(link);
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
    /// Renders an image inline.
    /// - No base path or http/https URL: returns informational placeholder text.
    /// - File not found: returns "[Image not found: {url}]".
    /// - File found: reads bytes, base64-encodes, enqueues a one:Image element in
    ///   <see cref="_pendingImageElements"/> and returns an empty string.
    /// </summary>
    private string RenderImage(LinkInline link)
    {
        var url = link.Url ?? "";
        var altText = GetInlineText(link) ?? "image";

        // No base path or remote URL — return informational placeholder
        if (_basePath == null ||
            url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return $"(Image: {System.Net.WebUtility.HtmlEncode(altText)} - {System.Net.WebUtility.HtmlEncode(url)})";
        }

        var fullPath = Path.Combine(_basePath, url);
        if (!File.Exists(fullPath))
        {
            return $"(Image not found: {System.Net.WebUtility.HtmlEncode(url)})";
        }

        var bytes = File.ReadAllBytes(fullPath);
        var base64 = System.Convert.ToBase64String(bytes);

        var imageElement = new XElement(OneNs + "Image",
            new XElement(OneNs + "Data", base64)
        );

        _pendingImageElements?.Add(imageElement);
        return "";  // image element added separately
    }

    /// <summary>
    /// Creates a OneNote Table element from a Markdig table block.
    /// </summary>
    private XElement CreateTableElement(Markdig.Extensions.Tables.Table table)
    {
        var columnCount = 0;
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
