using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public interface IDeclarationDependencyUsageProjector
{
    IReadOnlyDictionary<EntityId, PackageDeclarationDependencyUsageCatalog> Project(DeclarationDependencyUsageProjectionRequest request);
}
