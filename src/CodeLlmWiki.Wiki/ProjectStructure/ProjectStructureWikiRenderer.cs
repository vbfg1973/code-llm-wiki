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

        pages.AddRange(model.Files
            .OrderBy(x => x.Path, StringComparer.Ordinal)
            .Select(file => RenderFilePage(model.Repository, file)));

        return pages;
    }

    private static WikiPage RenderRepositoryPage(ProjectStructureWikiModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Repository: {model.Repository.Name}");
        sb.AppendLine();
        sb.AppendLine($"- Path: `{model.Repository.Path}`");
        sb.AppendLine($"- Branch Snapshot: `{model.Repository.HeadBranch}`");
        sb.AppendLine($"- Mainline Branch: `{model.Repository.MainlineBranch}`");
        sb.AppendLine($"- Solutions: {model.Solutions.Count}");
        sb.AppendLine($"- Projects: {model.Projects.Count}");
        sb.AppendLine($"- Packages: {model.Packages.Count}");
        sb.AppendLine($"- Files: {model.Files.Count}");
        sb.AppendLine($"- Submodules: {model.Submodules.Count}");
        sb.AppendLine();
        sb.AppendLine("## Solutions");

        foreach (var solution in model.Solutions.OrderBy(x => x.Path, StringComparer.Ordinal))
        {
            sb.AppendLine($"- `{solution.Path}`");
        }

        sb.AppendLine();
        sb.AppendLine("## Opaque Submodule Dependencies");

        foreach (var submodule in model.Submodules.OrderBy(x => x.Path, StringComparer.Ordinal))
        {
            sb.AppendLine($"- `{submodule.Path}` ({submodule.Url})");
        }

        return new WikiPage(
            RelativePath: $"repositories/{ToSlug(model.Repository.Id.Value)}.md",
            Title: model.Repository.Name,
            Markdown: WithFrontMatter(model.Repository.Id.Value, "repository", model.Repository.Id.Value, sb.ToString().TrimEnd()));
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
            Markdown: WithFrontMatter(solution.Id.Value, "solution", string.Empty, sb.ToString().TrimEnd()));
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
            Markdown: WithFrontMatter(project.Id.Value, "project", string.Empty, sb.ToString().TrimEnd()));
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
            Markdown: WithFrontMatter(package.Id.Value, "package", string.Empty, sb.ToString().TrimEnd()));
    }

    private static WikiPage RenderFilePage(RepositoryNode repository, FileNode file)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# File: {file.Name}");
        sb.AppendLine();
        sb.AppendLine($"- Path: `{file.Path}`");
        sb.AppendLine($"- classification: {file.Classification}");
        sb.AppendLine($"- is_solution_member: {file.IsSolutionMember.ToString().ToLowerInvariant()}");
        sb.AppendLine($"- branch_snapshot: {repository.HeadBranch}");
        sb.AppendLine($"- mainline_branch: {repository.MainlineBranch}");
        sb.AppendLine();
        sb.AppendLine("## History Summary");
        sb.AppendLine($"- edit_count: {file.EditCount}");

        if (file.LastChange is not null)
        {
            sb.AppendLine($"- last_change_commit: `{file.LastChange.CommitSha}`");
            sb.AppendLine($"- last_changed_at_utc: `{file.LastChange.TimestampUtc}`");
            sb.AppendLine($"- last_changed_by: {file.LastChange.AuthorName} <{file.LastChange.AuthorEmail}>");
        }

        sb.AppendLine();
        sb.AppendLine("## Merge To Mainline");

        foreach (var merge in file.MergeToMainlineEvents)
        {
            sb.AppendLine($"- merge_commit: `{merge.MergeCommitSha}`");
            sb.AppendLine($"  merged_at_utc: `{merge.TimestampUtc}`");
            sb.AppendLine($"  author: {merge.AuthorName} <{merge.AuthorEmail}>");
            sb.AppendLine($"  target_branch: {merge.TargetBranch}");
            sb.AppendLine($"  source_branch_file_commit_count: {merge.SourceBranchFileCommitCount}");
        }

        return new WikiPage(
            RelativePath: $"files/{ToSlug(file.Id.Value)}.md",
            Title: file.Name,
            Markdown: WithFrontMatter(file.Id.Value, "file", repository.Id.Value, sb.ToString().TrimEnd()));
    }

    private static string ToSlug(string value)
    {
        return value.Replace(':', '-').Replace('/', '-');
    }

    private static string WithFrontMatter(string entityId, string entityType, string repositoryId, string body)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"entity_id: {entityId}");
        sb.AppendLine($"entity_type: {entityType}");

        if (!string.IsNullOrWhiteSpace(repositoryId))
        {
            sb.AppendLine($"repository_id: {repositoryId}");
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append(body);
        return sb.ToString();
    }
}
