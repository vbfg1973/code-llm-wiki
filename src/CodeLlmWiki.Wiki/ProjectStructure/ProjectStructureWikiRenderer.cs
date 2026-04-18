using System.Text;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Query.ProjectStructure;

namespace CodeLlmWiki.Wiki.ProjectStructure;

public sealed class ProjectStructureWikiRenderer : IProjectStructureWikiRenderer
{
    public IReadOnlyList<WikiPage> Render(ProjectStructureWikiModel model)
    {
        var packageById = model.Packages.ToDictionary(x => x.Id, x => x);

        var pages = new List<WikiPage>
        {
            RenderRepositoryPage(model),
        };

        pages.AddRange(model.Solutions
            .OrderBy(x => x.Path, StringComparer.Ordinal)
            .Select(solution => RenderSolutionPage(solution, model.Projects)));

        pages.AddRange(model.Projects
            .OrderBy(x => x.Path, StringComparer.Ordinal)
            .Select(project => RenderProjectPage(project, packageById)));

        pages.AddRange(model.Packages
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .Select(RenderPackagePage));

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
        sb.AppendLine($"- Packages: {model.Packages.Count}");
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

    private static WikiPage RenderProjectPage(ProjectNode project, IReadOnlyDictionary<EntityId, PackageNode> packageById)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Project: {project.Name}");
        sb.AppendLine();
        sb.AppendLine($"- Path: `{project.Path}`");
        sb.AppendLine($"- Discovery: `{project.DiscoveryMethod}`");
        sb.AppendLine();
        sb.AppendLine("## Packages");

        foreach (var packageId in project.PackageIds)
        {
            if (!packageById.TryGetValue(packageId, out var package))
            {
                continue;
            }

            sb.AppendLine($"- [{package.Name}](../packages/{ToSlug(package.Id.Value)}.md)");
        }

        return new WikiPage(
            RelativePath: $"projects/{ToSlug(project.Id.Value)}.md",
            Title: project.Name,
            Markdown: sb.ToString().TrimEnd());
    }

    private static WikiPage RenderPackagePage(PackageNode package)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Package: {package.Name}");
        sb.AppendLine();
        sb.AppendLine("## Declared Versions");

        foreach (var declared in package.DeclaredVersions.OrderBy(x => x, StringComparer.Ordinal))
        {
            sb.AppendLine($"- `{declared}`");
        }

        sb.AppendLine();
        sb.AppendLine("## Resolved Versions");
        foreach (var resolved in package.ResolvedVersions.OrderBy(x => x, StringComparer.Ordinal))
        {
            sb.AppendLine($"- `{resolved}`");
        }

        return new WikiPage(
            RelativePath: $"packages/{ToSlug(package.Id.Value)}.md",
            Title: package.Name,
            Markdown: sb.ToString().TrimEnd());
    }

    private static string ToSlug(string value)
    {
        return value.Replace(':', '-').Replace('/', '-');
    }
}
