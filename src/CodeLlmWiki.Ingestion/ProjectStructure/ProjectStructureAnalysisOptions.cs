namespace CodeLlmWiki.Ingestion.ProjectStructure;

public sealed record ProjectStructureAnalysisOptions(
    int? SemanticCallGraphMaxDegreeOfParallelism = null)
{
    public static readonly ProjectStructureAnalysisOptions Default = new();

    public int EffectiveSemanticCallGraphMaxDegreeOfParallelism =>
        Math.Max(1, SemanticCallGraphMaxDegreeOfParallelism ?? 1);
}
