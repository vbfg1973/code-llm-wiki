using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public interface IProjectScopedPackageAttributionResolver
{
    PackageAttributionResolution Resolve(EntityId sourceMethodId, string targetReferenceName);
}
