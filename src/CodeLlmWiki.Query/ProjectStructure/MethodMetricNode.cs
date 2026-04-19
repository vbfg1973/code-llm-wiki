namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record MethodMetricNode(
    string CoverageStatus,
    int? CyclomaticComplexity,
    int? CognitiveComplexity,
    double? HalsteadVolume,
    double? MaintainabilityIndex)
{
    public static MethodMetricNode Empty { get; } = new(
        CoverageStatus: string.Empty,
        CyclomaticComplexity: null,
        CognitiveComplexity: null,
        HalsteadVolume: null,
        MaintainabilityIndex: null);
}
