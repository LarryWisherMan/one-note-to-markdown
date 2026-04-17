using System.IO;
using FluentAssertions;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Services;

public class SectionHierarchyWalkerTests
{
    private static string LoadFixture(string name) =>
        File.ReadAllText(Path.Combine("Fixtures", "hierarchy", name));

    [Fact]
    public void Plan_ExistingSection_ReturnsExistingSectionId()
    {
        var xml = LoadFixture("existing-section.xml");

        var plan = SectionHierarchyWalker.Plan(
            xml,
            notebookName: "Work Notes",
            sectionGroups: new[] { "Backend", "API" },
            sectionName: "auth-spec",
            createMissing: true);

        plan.ExistingSectionId.Should().Be("{SEC}{1}{B0}");
        plan.CreationSteps.Should().BeEmpty();
        plan.IsUnresolved.Should().BeFalse();
    }

    [Fact]
    public void Plan_MissingLeafSection_CreateMissing_AddsOneCreationStep()
    {
        var xml = LoadFixture("missing-leaf-section.xml");

        var plan = SectionHierarchyWalker.Plan(
            xml,
            notebookName: "Work Notes",
            sectionGroups: new[] { "Backend", "API" },
            sectionName: "auth-spec",
            createMissing: true);

        plan.ExistingSectionId.Should().BeNull();
        plan.DeepestExistingAncestorId.Should().Be("{SG-A}{1}{B0}");
        plan.CreationSteps.Should().HaveCount(1);
        plan.CreationSteps[0].Kind.Should().Be(CreationKind.Section);
        plan.CreationSteps[0].Name.Should().Be("auth-spec");
        plan.CreationSteps[0].TargetPath.Should().EndWith("API\\auth-spec.one");
    }
}
