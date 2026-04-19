namespace CodeLlmWiki.Ingestion.ProjectStructure.CallResolution;

public interface IProjectScopedCompilationProvider
{
    IProjectScopedSemanticContext Build(ProjectScopedCompilationRequest request, List<IngestionDiagnostic> diagnostics);
}
