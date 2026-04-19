namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record StructuralMetricStatistics(
    int MethodMetricCount,
    double AverageCyclomaticComplexity,
    double AverageCognitiveComplexity,
    double AverageHalsteadVolume,
    double AverageMaintainabilityIndex,
    int TypeMetricCount,
    double AverageCboDeclaration,
    double AverageCboMethodBody,
    double AverageCboTotal);
