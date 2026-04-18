using System.Text;
using CodeLlmWiki.Query.ProjectStructure;

namespace CodeLlmWiki.Wiki.ProjectStructure;

public sealed class ProjectStructureWikiRenderer : IProjectStructureWikiRenderer
{
    public IReadOnlyList<WikiPage> Render(ProjectStructureWikiModel model)
    {
        var pages = new List<WikiPage>
        {
            RenderRepositoryPage(model),
        };

        pages.AddRange(model.Solutions
            .OrderBy(x => x.Path, StringComparer.Ordinal)
            .Select(solution => RenderSolutionPage(solution, model.Projects)));

        pages.AddRange(model.Projects
            .OrderBy(x => x.Path, StringComparer.Ordinal)
            .Select(RenderProjectPage));

        return pages;
    }

    private static WikiPage RenderRepositoryPage(ProjectStructureWikiModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Repository: {model.Repository.Name}");
        sb.AppendLine();
        sb.AppendLine($"- Path: `{model.Repository.Path}`");
        sb.AppendLine($"- Solutions: {model.Solutions.Count}");
        sb.AppendLine($"- Projects: {model.Projects.Count}");
        sb.AppendLine();
        sb.AppendLine("## Solutions");

        foreach (var solution in model.Solutions.OrderBy(x => x.Path, StringComparer.Ordinal))
        {
            sb.AppendLine($"- `{solution.Path}`");
        }

        return new WikiPage(
            RelativePath: $"repositories/{ToSlug(model.Repository.Id.Value)}.md",
            Title: model.Repository.Name,
            Markdown: sb.ToString().TrimEnd());
    }

    private static WikiPage RenderSolutionPage(SolutionNode solution, IReadOnlyList<ProjectNode> projects)
    {
        var projectById = projects.ToDictionary(x => x.Id, x => x);

        var sb = new StringBuilder();
        sb.AppendLine($"# Solution: {solution.Name}");
        sb.AppendLine();
        sb.AppendLine($"- Path: `{solution.Path}`");
        sb.AppendLine($"- Projects: {solution.ProjectIds.Count}");
        sb.AppendLine();
        sb.AppendLine("## Projects");

        foreach (var projectId in solution.ProjectIds)
        {
            if (projectById.TryGetValue(projectId, out var project))
            {
                sb.AppendLine($"- `{project.Path}`");
            }
        }

        return new WikiPage(
            RelativePath: $"solutions/{ToSlug(solution.Id.Value)}.md",
            Title: solution.Name,
            Markdown: sb.ToString().TrimEnd());
    }

    private static WikiPage RenderProjectPage(ProjectNode project)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Project: {project.Name}");
        sb.AppendLine();
        sb.AppendLine($"- Path: `{project.Path}`");
        sb.AppendLine($"- Discovery: `{project.DiscoveryMethod}`");

        return new WikiPage(
            RelativePath: $"projects/{ToSlug(project.Id.Value)}.md",
            Title: project.Name,
            Markdown: sb.ToString().TrimEnd());
    }

    private static string ToSlug(string value)
    {
        return value.Replace(':', '-').Replace('/', '-');
    }
}
