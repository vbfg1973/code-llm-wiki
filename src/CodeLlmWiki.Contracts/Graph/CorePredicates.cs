namespace CodeLlmWiki.Contracts.Graph;

public static class CorePredicates
{
    public static readonly PredicateId Contains = new("core:contains");
    public static readonly PredicateId ReferencesPackage = new("core:referencesPackage");
    public static readonly PredicateId EntityType = new("core:entityType");
    public static readonly PredicateId HasName = new("core:hasName");
    public static readonly PredicateId HasPath = new("core:hasPath");
    public static readonly PredicateId DiscoveryMethod = new("core:discoveryMethod");
    public static readonly PredicateId HasDeclaredVersion = new("core:hasDeclaredVersion");
    public static readonly PredicateId HasResolvedVersion = new("core:hasResolvedVersion");
    public static readonly PredicateId FileKind = new("core:fileKind");
    public static readonly PredicateId IsSolutionMember = new("core:isSolutionMember");
    public static readonly PredicateId HeadBranch = new("core:headBranch");
    public static readonly PredicateId MainlineBranch = new("core:mainlineBranch");
    public static readonly PredicateId HasHistoryEvent = new("core:hasHistoryEvent");
    public static readonly PredicateId CommitSha = new("core:commitSha");
    public static readonly PredicateId CommittedAtUtc = new("core:committedAtUtc");
    public static readonly PredicateId AuthorName = new("core:authorName");
    public static readonly PredicateId AuthorEmail = new("core:authorEmail");
    public static readonly PredicateId EditCount = new("core:editCount");
    public static readonly PredicateId LastChangeCommitSha = new("core:lastChangeCommitSha");
    public static readonly PredicateId LastChangedAtUtc = new("core:lastChangedAtUtc");
    public static readonly PredicateId LastChangedBy = new("core:lastChangedBy");
    public static readonly PredicateId IsMergeToMainline = new("core:isMergeToMainline");
    public static readonly PredicateId TargetBranch = new("core:targetBranch");
    public static readonly PredicateId SourceBranchFileCommitCount = new("core:sourceBranchFileCommitCount");
    public static readonly PredicateId HasSubmodule = new("core:hasSubmodule");
    public static readonly PredicateId SubmoduleUrl = new("core:submoduleUrl");
}
