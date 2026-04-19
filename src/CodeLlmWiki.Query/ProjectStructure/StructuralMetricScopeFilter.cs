namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record StructuralMetricScopeFilter(
    bool IncludeProduction,
    bool IncludeTest,
    bool IncludeGenerated,
    bool ExcludeInsufficientDataFromRanking = true)
{
    public static StructuralMetricScopeFilter ProductionDefault { get; } = new(
        IncludeProduction: true,
        IncludeTest: false,
        IncludeGenerated: false);

    public static StructuralMetricScopeFilter AllCodeKinds { get; } = new(
        IncludeProduction: true,
        IncludeTest: true,
        IncludeGenerated: true);

    public bool IncludesCodeKind(StructuralMetricCodeKind codeKind)
    {
        return codeKind switch
        {
            StructuralMetricCodeKind.Production => IncludeProduction,
            StructuralMetricCodeKind.Test => IncludeTest,
            StructuralMetricCodeKind.Generated => IncludeGenerated,
            _ => false,
        };
    }
}
