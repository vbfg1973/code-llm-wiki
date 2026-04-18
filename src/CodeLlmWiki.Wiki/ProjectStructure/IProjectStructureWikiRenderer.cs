using CodeLlmWiki.Query.ProjectStructure;

namespace CodeLlmWiki.Wiki.ProjectStructure;

public interface IProjectStructureWikiRenderer
{
    IReadOnlyList<WikiPage> Render(ProjectStructureWikiModel model);
}
