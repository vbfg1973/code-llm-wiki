namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record StructuralMetricCoverage(
    int TotalMethods,
    int AnalyzableMethods,
    int NonAnalyzableMethods,
    int TotalTypes,
    int TypesWithCbo);
