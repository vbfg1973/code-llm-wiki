namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record DependencyAttributionCatalog(
    UnknownDependencyUsageCatalog DeclarationUnknown,
    UnknownDependencyUsageCatalog MethodBodyUnknown)
{
    public static DependencyAttributionCatalog Empty { get; } = new(
        UnknownDependencyUsageCatalog.Empty,
        UnknownDependencyUsageCatalog.Empty);
}
