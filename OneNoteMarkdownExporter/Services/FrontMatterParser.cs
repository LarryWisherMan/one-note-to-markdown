using System;
using System.Collections.Generic;
using System.IO;
using OneNoteMarkdownExporter.Models;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace OneNoteMarkdownExporter.Services;

/// <summary>
/// Parses the minimal YAML front-matter subset needed for OneNote routing:
/// <c>title</c> plus an optional <c>onenote</c> block (or <c>onenote: true</c> /
/// <c>onenote: false</c> shorthand). Full FM schema is issue #3.
/// </summary>
public class FrontMatterParser
{
    public FrontMatter Parse(string fileContent)
    {
        var yaml = ExtractYamlBlock(fileContent);
        if (yaml is null)
        {
            return new FrontMatter();
        }

        YamlStream stream;
        try
        {
            stream = new YamlStream();
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException ex)
        {
            throw new FrontMatterParseException(ex.Message, ex);
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            return new FrontMatter();
        }

        var fm = new FrontMatter();
        foreach (var (keyNode, valueNode) in root.Children)
        {
            if (keyNode is not YamlScalarNode keyScalar) continue;

            switch (keyScalar.Value)
            {
                case "title":
                    if (valueNode is YamlScalarNode titleScalar)
                    {
                        fm.Title = titleScalar.Value;
                    }
                    break;

                case "onenote":
                    ApplyOneNoteNode(fm, valueNode);
                    break;
            }
        }

        return fm;
    }

    private static void ApplyOneNoteNode(FrontMatter fm, YamlNode node)
    {
        if (node is YamlScalarNode scalar)
        {
            if (scalar.Value is null || scalar.Value == string.Empty)
            {
                fm.OneNote = new OneNoteFrontMatter();
                return;
            }

            if (scalar.Value.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                fm.OneNote = new OneNoteFrontMatter();
                return;
            }

            if (scalar.Value.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                fm.OptOut = true;
                return;
            }
        }

        if (node is YamlMappingNode mapping)
        {
            var block = new OneNoteFrontMatter();
            foreach (var (keyNode, valueNode) in mapping.Children)
            {
                if (keyNode is not YamlScalarNode keyScalar) continue;

                switch (keyScalar.Value)
                {
                    case "notebook":
                        block.Notebook = (valueNode as YamlScalarNode)?.Value;
                        break;
                    case "section":
                        block.Section = (valueNode as YamlScalarNode)?.Value;
                        break;
                    case "section_groups":
                        if (valueNode is YamlSequenceNode seq)
                        {
                            var list = new List<string>();
                            foreach (var item in seq.Children)
                            {
                                if (item is YamlScalarNode itemScalar && itemScalar.Value is not null)
                                {
                                    list.Add(itemScalar.Value);
                                }
                            }
                            block.SectionGroups = list;
                        }
                        break;
                }
            }
            fm.OneNote = block;
        }
    }

    /// <summary>
    /// Returns the file content with the front-matter block stripped.
    /// If no front-matter is present, returns the original content unchanged.
    /// </summary>
    public static string StripFrontMatter(string content)
    {
        if (!content.StartsWith("---"))
        {
            return content;
        }

        using var reader = new StringReader(content);
        string? first = reader.ReadLine();
        if (first?.TrimEnd() != "---")
        {
            return content;
        }

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.TrimEnd() == "---")
            {
                // Return everything after the closing delimiter.
                return reader.ReadToEnd().TrimStart('\r', '\n');
            }
        }

        return content; // no closing delimiter — return as-is
    }

    private static string? ExtractYamlBlock(string content)
    {
        if (!content.StartsWith("---"))
        {
            return null;
        }

        using var reader = new StringReader(content);
        string? first = reader.ReadLine();
        if (first?.TrimEnd() != "---")
        {
            return null;
        }

        var yamlLines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.TrimEnd() == "---")
            {
                return string.Join('\n', yamlLines);
            }
            yamlLines.Add(line);
        }

        return null; // no closing delimiter — treat as no FM
    }
}

public class FrontMatterParseException : Exception
{
    public FrontMatterParseException(string message, Exception innerException)
        : base(message, innerException) { }
}
