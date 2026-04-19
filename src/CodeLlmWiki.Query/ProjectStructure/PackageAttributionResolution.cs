using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record PackageAttributionResolution(
    PackageAttributionStatus Status,
    EntityId? SourceProjectId,
    EntityId? PackageId,
    PackageAttributionReason Reason,
    IReadOnlyList<EntityId> OrderedCandidatePackageIds);
