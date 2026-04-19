using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public interface IMethodBodyDependencyUsageProjector
{
    IReadOnlyDictionary<EntityId, PackageMethodBodyDependencyUsageCatalog> Project(MethodBodyDependencyUsageProjectionRequest request);
}
