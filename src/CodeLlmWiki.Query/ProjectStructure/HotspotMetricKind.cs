namespace CodeLlmWiki.Query.ProjectStructure;

public enum HotspotMetricKind
{
    MethodCyclomaticComplexity = 0,
    MethodCognitiveComplexity = 1,
    MethodHalsteadVolume = 2,
    MethodMaintainabilityIndex = 3,
    TypeCboDeclaration = 4,
    TypeCboMethodBody = 5,
    TypeCboTotal = 6,
    ScopeAverageCyclomaticComplexity = 7,
    ScopeAverageCognitiveComplexity = 8,
    ScopeAverageMaintainabilityIndex = 9,
    ScopeAverageCboTotal = 10,
    Composite = 11,
}
