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
}
