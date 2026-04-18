using System.Text;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Query.ProjectStructure;

namespace CodeLlmWiki.Wiki.ProjectStructure;

public sealed class ProjectStructureWikiRenderer : IProjectStructureWikiRenderer
{
    public IReadOnlyList<WikiPage> Render(ProjectStructureWikiModel model)
    {
        var resolver = new WikiPathResolver();
        resolver.RegisterRepository(model.Repository);

        var orderedSolutions = model.Solutions.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        foreach (var solution in orderedSolutions)
        {
            resolver.RegisterSolution(solution);
        }

        var orderedProjects = model.Projects.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        foreach (var project in orderedProjects)
        {
            resolver.RegisterProject(project);
        }

        var orderedPackages = model.Packages.OrderBy(x => x.Name, StringComparer.Ordinal).ToArray();
        foreach (var package in orderedPackages)
        {
            resolver.RegisterPackage(package);
        }

        var orderedFiles = model.Files.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        foreach (var file in orderedFiles)
        {
            resolver.RegisterFile(file);
        }

        var indexPath = resolver.RegisterIndex();
        var packageById = model.Packages.ToDictionary(x => x.Id, x => x);
        var projectById = model.Projects.ToDictionary(x => x.Id, x => x);

        var pages = new List<WikiPage>
        {
            RenderRepositoryPage(model, resolver),
        };

        pages.AddRange(orderedSolutions.Select(solution => RenderSolutionPage(solution, projectById, resolver)));
        pages.AddRange(orderedProjects.Select(project => RenderProjectPage(project, packageById, resolver)));
        pages.AddRange(orderedPackages.Select(package => RenderPackagePage(package, resolver)));
        pages.AddRange(orderedFiles.Select(file => RenderFilePage(model.Repository, file, resolver)));

        pages.Add(RenderIndexPage(model, resolver, indexPath));

        return pages
            .OrderBy(x => x.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static WikiPage RenderRepositoryPage(ProjectStructureWikiModel model, WikiPathResolver resolver)
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
        sb.AppendLine($"- Index: {ToWikiLink("index/repository-index", "Repository Index")}");
        sb.AppendLine();
        sb.AppendLine("## Solutions");

        foreach (var solution in model.Solutions.OrderBy(x => x.Path, StringComparer.Ordinal))
        {
            sb.AppendLine($"- {resolver.ToWikiLink(solution.Id, solution.Name)}");
        }

        sb.AppendLine();
        sb.AppendLine("## Opaque Submodule Dependencies");

        foreach (var submodule in model.Submodules.OrderBy(x => x.Path, StringComparer.Ordinal))
        {
            sb.AppendLine($"- `{submodule.Path}` ({submodule.Url})");
        }

        return new WikiPage(
            RelativePath: resolver.GetPath(model.Repository.Id),
            Title: model.Repository.Name,
            Markdown: WithFrontMatter(model.Repository.Id.Value, "repository", model.Repository.Id.Value, sb.ToString().TrimEnd()));
    }

    private static WikiPage RenderSolutionPage(
        SolutionNode solution,
        IReadOnlyDictionary<EntityId, ProjectNode> projectById,
        WikiPathResolver resolver)
    {
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
                sb.AppendLine($"- {resolver.ToWikiLink(project.Id, project.Name)}");
            }
        }

        return new WikiPage(
            RelativePath: resolver.GetPath(solution.Id),
            Title: solution.Name,
            Markdown: WithFrontMatter(solution.Id.Value, "solution", string.Empty, sb.ToString().TrimEnd()));
    }

    private static WikiPage RenderProjectPage(
        ProjectNode project,
        IReadOnlyDictionary<EntityId, PackageNode> packageById,
        WikiPathResolver resolver)
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

            sb.AppendLine($"- {resolver.ToWikiLink(package.Id, package.Name)}");
        }

        return new WikiPage(
            RelativePath: resolver.GetPath(project.Id),
            Title: project.Name,
            Markdown: WithFrontMatter(project.Id.Value, "project", string.Empty, sb.ToString().TrimEnd()));
    }

    private static WikiPage RenderPackagePage(PackageNode package, WikiPathResolver resolver)
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
            RelativePath: resolver.GetPath(package.Id),
            Title: package.Name,
            Markdown: WithFrontMatter(package.Id.Value, "package", string.Empty, sb.ToString().TrimEnd()));
    }

    private static WikiPage RenderFilePage(
        RepositoryNode repository,
        FileNode file,
        WikiPathResolver resolver)
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
            RelativePath: resolver.GetPath(file.Id),
            Title: file.Name,
            Markdown: WithFrontMatter(file.Id.Value, "file", repository.Id.Value, sb.ToString().TrimEnd()));
    }

    private static WikiPage RenderIndexPage(
        ProjectStructureWikiModel model,
        WikiPathResolver resolver,
        string indexPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Repository Index");
        sb.AppendLine();

        AppendTable(
            sb,
            "Repositories",
            [new IndexRow(model.Repository.Name, model.Repository.Path, model.Repository.Id.Value, resolver.ToWikiLink(model.Repository.Id, model.Repository.Name))]);

        AppendTable(
            sb,
            "Solutions",
            model.Solutions
                .OrderBy(x => x.Path, StringComparer.Ordinal)
                .Select(x => new IndexRow(x.Name, x.Path, x.Id.Value, resolver.ToWikiLink(x.Id, x.Name)))
                .ToArray());

        AppendTable(
            sb,
            "Projects",
            model.Projects
                .OrderBy(x => x.Path, StringComparer.Ordinal)
                .Select(x => new IndexRow(x.Name, x.Path, x.Id.Value, resolver.ToWikiLink(x.Id, x.Name)))
                .ToArray());

        AppendTable(
            sb,
            "Packages",
            model.Packages
                .OrderBy(x => x.Name, StringComparer.Ordinal)
                .Select(x => new IndexRow(x.Name, x.Name, x.Id.Value, resolver.ToWikiLink(x.Id, x.Name)))
                .ToArray());

        AppendTable(
            sb,
            "Files",
            model.Files
                .OrderBy(x => x.Path, StringComparer.Ordinal)
                .Select(x => new IndexRow(x.Name, x.Path, x.Id.Value, resolver.ToWikiLink(x.Id, x.Path)))
                .ToArray());

        return new WikiPage(
            RelativePath: indexPath,
            Title: "Repository Index",
            Markdown: sb.ToString().TrimEnd());
    }

    private static void AppendTable(StringBuilder sb, string title, IReadOnlyList<IndexRow> rows)
    {
        sb.AppendLine($"## {title}");
        sb.AppendLine("| name | path | entity_id | page_link |");
        sb.AppendLine("| --- | --- | --- | --- |");

        foreach (var row in rows)
        {
            sb.AppendLine($"| {EscapePipes(row.Name)} | {EscapePipes(row.Path)} | `{row.EntityId}` | {row.PageLink} |");
        }

        sb.AppendLine();
    }

    private static string EscapePipes(string value)
    {
        return value.Replace("|", "\\|", StringComparison.Ordinal);
    }

    private static string ToWikiLink(string target, string alias)
    {
        return $"[[{target}|{alias}]]";
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

    private sealed record IndexRow(string Name, string Path, string EntityId, string PageLink);
}
