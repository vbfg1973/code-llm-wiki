using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public interface IProjectStructureQueryService
{
    ProjectStructureWikiModel GetModel(EntityId repositoryId);
}
