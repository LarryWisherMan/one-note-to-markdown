using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace OneNoteMarkdownExporter.Services;

/// <summary>
/// Pure walker over a OneNote hierarchy XML (from
/// <c>Application.GetHierarchy(hsSections)</c>) that resolves a
/// notebook → section-groups → section path and produces a
/// <see cref="SectionResolutionPlan"/> describing what to do.
/// </summary>
/// <remarks>
/// <see cref="CreationStep.TargetPath"/> is a <b>relative</b> name:
/// a section-group folder name (e.g. <c>"Backend"</c>) or a section
/// file name (e.g. <c>"auth-spec.one"</c>). When the executor calls
/// <c>OpenHierarchy(targetPath, parentId, ...)</c> with a non-rooted
/// path, OneNote resolves it relative to the parent's actual storage
/// location. This form works for local notebooks and OneDrive-synced
/// notebooks alike; concatenating the notebook's <c>path</c> attribute
/// would mangle OneDrive URLs (e.g. <c>https://d.docs.live.net/…</c>)
/// into invalid filesystem paths.
/// </remarks>
public static class SectionHierarchyWalker
{
    private static readonly XNamespace OneNs =
        "http://schemas.microsoft.com/office/onenote/2013/onenote";

    public static SectionResolutionPlan Plan(
        string hierarchyXml,
        string notebookName,
        IReadOnlyList<string> sectionGroups,
        string sectionName,
        bool createMissing)
    {
        var doc = XDocument.Parse(hierarchyXml);

        var notebook = doc.Descendants(OneNs + "Notebook")
            .FirstOrDefault(n => NameEquals(n, notebookName))
            ?? throw new NotebookNotFoundException(notebookName);

        var cursor = notebook;
        var creations = new List<CreationStep>();
        var sawMissing = false;

        foreach (var sgName in sectionGroups)
        {
            if (sawMissing)
            {
                creations.Add(new CreationStep(
                    CreationKind.SectionGroup, sgName, sgName));
                continue;
            }

            var child = cursor.Elements(OneNs + "SectionGroup")
                .FirstOrDefault(sg => NameEquals(sg, sgName));

            if (child is null)
            {
                if (!createMissing)
                    return Unresolved();

                sawMissing = true;
                creations.Add(new CreationStep(
                    CreationKind.SectionGroup, sgName, sgName));
            }
            else
            {
                cursor = child;
            }
        }

        if (!sawMissing)
        {
            var existing = cursor.Elements(OneNs + "Section")
                .FirstOrDefault(s => NameEquals(s, sectionName));

            if (existing is not null)
            {
                // Invariant: ExistingSectionId is non-null AND non-empty.
                // Throw on corrupt hierarchy rather than swallow it.
                var sectionId = existing.Attribute("ID")?.Value
                    ?? throw new InvalidOperationException(
                        $"Hierarchy entry for section '{sectionName}' has no ID attribute " +
                        "— this is malformed OneNote GetHierarchy output.");
                return new SectionResolutionPlan(
                    ExistingSectionId: sectionId,
                    DeepestExistingAncestorId: "",
                    CreationSteps: Array.Empty<CreationStep>());
            }
        }

        if (!createMissing) return Unresolved();

        creations.Add(new CreationStep(
            CreationKind.Section, sectionName, sectionName + ".one"));

        return new SectionResolutionPlan(
            ExistingSectionId: null,
            DeepestExistingAncestorId: cursor.Attribute("ID")?.Value ?? "",
            CreationSteps: creations);

        static SectionResolutionPlan Unresolved() =>
            new(null, "", Array.Empty<CreationStep>());
    }

    private static bool NameEquals(XElement element, string name) =>
        string.Equals(
            element.Attribute("name")?.Value, name,
            StringComparison.OrdinalIgnoreCase);
}
