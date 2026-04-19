namespace CodeLlmWiki.Query.ProjectStructure;

public interface IUnknownDependencyUsageProjector
{
    UnknownDependencyUsageCatalog Project(UnknownDependencyUsageProjectionRequest request);
}
