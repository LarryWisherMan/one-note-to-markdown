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

    [Fact]
    public void Plan_MissingIntermediateSectionGroup_CreateMissing_AddsTwoCreationSteps()
    {
        var xml = LoadFixture("missing-intermediate.xml");

        var plan = SectionHierarchyWalker.Plan(
            xml,
            notebookName: "Work Notes",
            sectionGroups: new[] { "Backend", "API" },
            sectionName: "auth-spec",
            createMissing: true);

        plan.ExistingSectionId.Should().BeNull();
        plan.DeepestExistingAncestorId.Should().Be("{SG-B}{1}{B0}");
        plan.CreationSteps.Should().HaveCount(2);
        plan.CreationSteps[0].Kind.Should().Be(CreationKind.SectionGroup);
        plan.CreationSteps[0].Name.Should().Be("API");
        plan.CreationSteps[1].Kind.Should().Be(CreationKind.Section);
        plan.CreationSteps[1].Name.Should().Be("auth-spec");
    }

    [Fact]
    public void Plan_MissingAllIntermediates_CreateMissing_AddsThreeCreationSteps()
    {
        var xml = LoadFixture("missing-all-intermediates.xml");

        var plan = SectionHierarchyWalker.Plan(
            xml,
            notebookName: "Work Notes",
            sectionGroups: new[] { "Backend", "API" },
            sectionName: "auth-spec",
            createMissing: true);

        plan.ExistingSectionId.Should().BeNull();
        plan.DeepestExistingAncestorId.Should().Be("{NB}{1}{B0}");
        plan.CreationSteps.Should().HaveCount(3);
        plan.CreationSteps[0].Kind.Should().Be(CreationKind.SectionGroup);
        plan.CreationSteps[0].Name.Should().Be("Backend");
        plan.CreationSteps[1].Kind.Should().Be(CreationKind.SectionGroup);
        plan.CreationSteps[1].Name.Should().Be("API");
        plan.CreationSteps[2].Kind.Should().Be(CreationKind.Section);
        plan.CreationSteps[2].Name.Should().Be("auth-spec");
    }

    [Fact]
    public void Plan_MissingNotebook_ThrowsNotebookNotFoundException()
    {
        var xml = LoadFixture("missing-notebook.xml");

        var act = () => SectionHierarchyWalker.Plan(
            xml,
            notebookName: "Work Notes",
            sectionGroups: new[] { "Backend" },
            sectionName: "auth-spec",
            createMissing: true);

        act.Should()
            .Throw<NotebookNotFoundException>()
            .Which.NotebookName.Should().Be("Work Notes");
    }

    [Fact]
    public void Plan_MissingLeafSection_CreateMissingFalse_ReturnsUnresolved()
    {
        var xml = LoadFixture("missing-leaf-section.xml");

        var plan = SectionHierarchyWalker.Plan(
            xml,
            notebookName: "Work Notes",
            sectionGroups: new[] { "Backend", "API" },
            sectionName: "auth-spec",
            createMissing: false);

        plan.IsUnresolved.Should().BeTrue();
        plan.ExistingSectionId.Should().BeNull();
        plan.CreationSteps.Should().BeEmpty();
    }
}
