namespace CodeLlmWiki.Ingestion.ProjectStructure;

public sealed class ProjectStructureIngestionPipeline : IIngestionPipeline
{
    private readonly IProjectStructureAnalyzer _analyzer;

    public ProjectStructureIngestionPipeline(IProjectStructureAnalyzer analyzer)
    {
        _analyzer = analyzer;
    }

    public async Task<IngestionPipelineResult> ExecuteAsync(IngestionExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await _analyzer.AnalyzeAsync(
            context.RepositoryPath,
            cancellationToken,
            new ProjectStructureAnalysisOptions(
                SemanticCallGraphMaxDegreeOfParallelism: context.SemanticCallGraphMaxDegreeOfParallelism));
        return new IngestionPipelineResult(result.Triples, result.Diagnostics);
    }
}
