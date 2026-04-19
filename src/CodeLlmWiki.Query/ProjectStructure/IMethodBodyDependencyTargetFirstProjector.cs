using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public interface IMethodBodyDependencyTargetFirstProjector
{
    IReadOnlyDictionary<EntityId, PackageMethodBodyDependencyTargetFirstCatalog> Project(
        MethodBodyDependencyUsageProjectionRequest request);
}
