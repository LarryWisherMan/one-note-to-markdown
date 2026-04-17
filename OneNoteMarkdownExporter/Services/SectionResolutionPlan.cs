using System.Collections.Generic;

namespace OneNoteMarkdownExporter.Services;

/// <summary>
/// Result of walking an existing OneNote hierarchy for a given
/// notebook / section-groups / section path. Consumers interpret the plan:
/// dry-run callers print the steps; live callers execute them.
/// </summary>
public sealed record SectionResolutionPlan(
    /// <summary>When non-null, the section already exists and has this ID.
    /// <see cref="DeepestExistingAncestorId"/> and <see cref="CreationSteps"/>
    /// are irrelevant when this is set.</summary>
    string? ExistingSectionId,

    /// <summary>ID of the deepest hierarchy node that does exist — either the
    /// notebook or a section group. Serves as the parent for the first
    /// creation step. Empty string when <see cref="ExistingSectionId"/> is set.</summary>
    string DeepestExistingAncestorId,

    /// <summary>Ordered creation steps that would produce the target section
    /// starting from <see cref="DeepestExistingAncestorId"/>. Empty when the
    /// section already exists, or when createMissing was false and any link
    /// was missing.</summary>
    IReadOnlyList<CreationStep> CreationSteps)
{
    /// <summary>True when createMissing was false and the target section
    /// could not be resolved against the existing hierarchy.</summary>
    public bool IsUnresolved =>
        ExistingSectionId is null && CreationSteps.Count == 0;
}

public sealed record CreationStep(
    CreationKind Kind,
    string Name,
    string TargetPath);

public enum CreationKind
{
    SectionGroup,
    Section,
}
