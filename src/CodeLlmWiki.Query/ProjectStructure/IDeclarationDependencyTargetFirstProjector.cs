using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public interface IDeclarationDependencyTargetFirstProjector
{
    IReadOnlyDictionary<EntityId, PackageDeclarationDependencyTargetFirstCatalog> Project(
        DeclarationDependencyUsageProjectionRequest request);
}
