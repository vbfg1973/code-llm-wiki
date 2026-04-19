namespace CodeLlmWiki.Ingestion.ProjectStructure;

public interface IProjectStructureAnalyzer
{
    Task<ProjectStructureAnalysisResult> AnalyzeAsync(string repositoryPath, CancellationToken cancellationToken);
}
