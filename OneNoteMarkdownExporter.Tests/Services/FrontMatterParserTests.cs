using FluentAssertions;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Services;

public class FrontMatterParserTests
{
    private readonly FrontMatterParser _parser = new();

    [Fact]
    public void Parse_NoFrontMatter_ReturnsEmptyFrontMatter()
    {
        var content = "# Just a heading\n\nBody.";
        var fm = _parser.Parse(content);

        fm.Title.Should().BeNull();
        fm.OneNote.Should().BeNull();
        fm.OptOut.Should().BeFalse();
    }

    [Fact]
    public void Parse_EmptyFrontMatter_ReturnsEmptyFrontMatter()
    {
        var content = "---\n---\nBody.";
        var fm = _parser.Parse(content);

        fm.Title.Should().BeNull();
        fm.OneNote.Should().BeNull();
    }

    [Fact]
    public void Parse_OneNoteTrue_MarksPublishableWithEmptyBlock()
    {
        var content = "---\nonenote: true\n---\nBody.";
        var fm = _parser.Parse(content);

        fm.OneNote.Should().NotBeNull();
        fm.OneNote!.Notebook.Should().BeNull();
        fm.OptOut.Should().BeFalse();
    }

    [Fact]
    public void Parse_OneNoteFalse_MarksOptOut()
    {
        var content = "---\nonenote: false\n---\nBody.";
        var fm = _parser.Parse(content);

        fm.OptOut.Should().BeTrue();
        fm.OneNote.Should().BeNull();
    }

    [Fact]
    public void Parse_OneNoteNull_TreatedAsPublishable()
    {
        // YAML: `onenote:` with no value parses as null — treat as opt-in.
        var content = "---\nonenote:\n---\nBody.";
        var fm = _parser.Parse(content);

        fm.OneNote.Should().NotBeNull();
        fm.OptOut.Should().BeFalse();
    }

    [Fact]
    public void Parse_FullOneNoteBlock_PopulatesAllFields()
    {
        var content = "---\ntitle: My Page\nonenote:\n  notebook: Work Notes\n  section: Architecture\n  section_groups:\n    - Backend\n    - API\n---\nBody.";
        var fm = _parser.Parse(content);

        fm.Title.Should().Be("My Page");
        fm.OneNote.Should().NotBeNull();
        fm.OneNote!.Notebook.Should().Be("Work Notes");
        fm.OneNote.Section.Should().Be("Architecture");
        fm.OneNote.SectionGroups.Should().BeEquivalentTo(new[] { "Backend", "API" });
    }

    [Fact]
    public void Parse_TitleOnly_SetsTitleLeavesOneNoteNull()
    {
        var content = "---\ntitle: Only Title\n---\nBody.";
        var fm = _parser.Parse(content);

        fm.Title.Should().Be("Only Title");
        fm.OneNote.Should().BeNull();
    }

    [Fact]
    public void Parse_MalformedYaml_ThrowsWithFilePointer()
    {
        var content = "---\ntitle: [unclosed\n---\nBody.";
        var act = () => _parser.Parse(content);

        act.Should().Throw<FrontMatterParseException>();
    }
}
