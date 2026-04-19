namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record TypeMetricNode(
    bool HasCbo,
    int CboDeclaration,
    int CboMethodBody,
    int CboTotal,
    int MethodMetricCount,
    double AverageCyclomaticComplexity,
    double AverageCognitiveComplexity,
    double AverageHalsteadVolume,
    double AverageMaintainabilityIndex)
{
    public static TypeMetricNode Empty { get; } = new(
        HasCbo: false,
        CboDeclaration: 0,
        CboMethodBody: 0,
        CboTotal: 0,
        MethodMetricCount: 0,
        AverageCyclomaticComplexity: 0d,
        AverageCognitiveComplexity: 0d,
        AverageHalsteadVolume: 0d,
        AverageMaintainabilityIndex: 0d);
}
