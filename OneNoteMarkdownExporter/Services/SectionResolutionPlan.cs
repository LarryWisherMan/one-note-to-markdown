using System.Collections.Generic;

namespace OneNoteMarkdownExporter.Services;

/// <summary>
/// Result of walking an existing OneNote hierarchy for a given
/// notebook / section-groups / section path. Consumers interpret the plan:
/// dry-run callers print the steps; live callers execute them.
///
/// Invariant: when <c>ExistingSectionId</c> is non-null, it is also non-empty.
/// The walker enforces this — a hierarchy <c>Section</c> without an <c>ID</c>
/// attribute is treated as corrupt input, not as a resolved target.
/// </summary>
/// <param name="ExistingSectionId">When non-null, the section already exists
/// and has this ID. <see cref="DeepestExistingAncestorId"/> and
/// <see cref="CreationSteps"/> are irrelevant when this is set.</param>
/// <param name="DeepestExistingAncestorId">ID of the deepest hierarchy node
/// that does exist — either the notebook or a section group. Serves as the
/// parent for the first creation step. Empty string when
/// <see cref="ExistingSectionId"/> is set.</param>
/// <param name="CreationSteps">Ordered creation steps that would produce the
/// target section starting from <see cref="DeepestExistingAncestorId"/>.
/// Empty when the section already exists, or when createMissing was false
/// and any link was missing.</param>
public sealed record SectionResolutionPlan(
    string? ExistingSectionId,
    string DeepestExistingAncestorId,
    IReadOnlyList<CreationStep> CreationSteps)
{
    /// <summary>
    /// True when the plan resolves to neither an existing section nor any
    /// creation steps. The walker produces this state when createMissing was
    /// false and the target section (or an intermediate link) was missing.
    /// </summary>
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
