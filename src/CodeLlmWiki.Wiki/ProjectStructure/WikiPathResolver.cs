using System.Text;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Query.ProjectStructure;

namespace CodeLlmWiki.Wiki.ProjectStructure;

internal sealed class WikiPathResolver
{
    private readonly Dictionary<EntityId, string> _pathByEntityId = new();
    private readonly HashSet<string> _usedPaths = new(StringComparer.OrdinalIgnoreCase);

    public string RegisterRepository(RepositoryNode repository)
    {
        return RegisterNamedPath("repositories", repository.Id, repository.Name, repository.Path);
    }

    public string RegisterSolution(SolutionNode solution)
    {
        return RegisterNamedPath("solutions", solution.Id, solution.Name, solution.Path);
    }

    public string RegisterProject(ProjectNode project)
    {
        return RegisterNamedPath("projects", project.Id, project.Name, project.Path);
    }

    public string RegisterPackage(PackageNode package)
    {
        return RegisterNamedPath("packages", package.Id, package.Name, package.Name);
    }

    public string RegisterNamespace(NamespaceDeclarationNode namespaceDeclaration)
    {
        var namespacePath = NormalizePath(namespaceDeclaration.Path.Replace('.', '/'));
        var candidate = $"namespaces/{namespacePath}.md";
        var path = ReserveWithCounter(candidate);
        _pathByEntityId[namespaceDeclaration.Id] = path;
        return path;
    }

    public string RegisterType(TypeDeclarationNode typeDeclaration)
    {
        var typePath = NormalizePath(typeDeclaration.Path.Replace('.', '/'));
        var candidate = $"types/{typePath}.md";
        var path = ReserveWithCounter(candidate);
        _pathByEntityId[typeDeclaration.Id] = path;
        return path;
    }

    public string RegisterMethod(MethodDeclarationNode methodDeclaration, TypeDeclarationNode declaringType)
    {
        var typePath = NormalizePath(declaringType.Path.Replace('.', '/'));
        var methodStem = BuildMethodStem(methodDeclaration);
        var candidate = $"methods/{typePath}/{methodStem}.md";
        var path = ReserveWithCounter(candidate);
        _pathByEntityId[methodDeclaration.Id] = path;
        return path;
    }

    public string RegisterFile(FileNode file)
    {
        var normalizedPath = NormalizePath(file.Path);
        var candidate = $"files/{normalizedPath}.md";
        var path = ReserveWithCounter(candidate);
        _pathByEntityId[file.Id] = path;
        return path;
    }

    public string RegisterIndex()
    {
        return ReserveWithCounter("index/repository-index.md");
    }

    public string GetPath(EntityId entityId)
    {
        return _pathByEntityId.TryGetValue(entityId, out var value)
            ? value
            : throw new InvalidOperationException($"No page path registered for entity '{entityId.Value}'.");
    }

    public string ToWikiLink(EntityId entityId, string? alias = null)
    {
        var path = GetPath(entityId);
        var target = ToWikiTarget(path);
        var safeAlias = alias?.Trim();

        return string.IsNullOrWhiteSpace(safeAlias)
            ? $"[[{target}]]"
            : $"[[{target}|{safeAlias}]]";
    }

    private string RegisterNamedPath(string directory, EntityId entityId, string preferredName, string context)
    {
        var preferredStem = SanitizePathSegment(preferredName);
        if (string.IsNullOrWhiteSpace(preferredStem))
        {
            preferredStem = "entity";
        }

        var initial = $"{directory}/{preferredStem}.md";
        if (_usedPaths.Add(initial))
        {
            _pathByEntityId[entityId] = initial;
            return initial;
        }

        var contextSuffix = SanitizePathSegment(context.Replace('/', '-').Replace('\\', '-'));
        if (string.IsNullOrWhiteSpace(contextSuffix))
        {
            contextSuffix = "context";
        }

        var contextual = $"{directory}/{preferredStem}--{contextSuffix}.md";
        var reserved = ReserveWithCounter(contextual);

        _pathByEntityId[entityId] = reserved;
        return reserved;
    }

    private string ReserveWithCounter(string candidate)
    {
        if (_usedPaths.Add(candidate))
        {
            return candidate;
        }

        var extension = Path.GetExtension(candidate);
        var withoutExtension = candidate[..^extension.Length];

        var counter = 2;
        while (true)
        {
            var withCounter = $"{withoutExtension}--{counter}{extension}";
            if (_usedPaths.Add(withCounter))
            {
                return withCounter;
            }

            counter++;
        }
    }

    private static string NormalizePath(string value)
    {
        var normalized = value.Replace('\\', '/').TrimStart('/');
        var segments = normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizePathSegment)
            .ToArray();

        return segments.Length == 0
            ? "file"
            : string.Join('/', segments);
    }

    private static string SanitizePathSegment(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(trimmed.Length);
        var previousWasDash = false;

        foreach (var c in trimmed)
        {
            if (char.IsLetterOrDigit(c) || c is '.' or '_' or '-')
            {
                builder.Append(c);
                previousWasDash = false;
                continue;
            }

            if (!previousWasDash)
            {
                builder.Append('-');
                previousWasDash = true;
            }
        }

        return builder
            .ToString()
            .Trim('-');
    }

    private static string ToWikiTarget(string relativePath)
    {
        return relativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? relativePath[..^3]
            : relativePath;
    }

    private static string BuildMethodStem(MethodDeclarationNode methodDeclaration)
    {
        var prefix = methodDeclaration.Kind == MethodDeclarationKind.Constructor
            ? "ctor"
            : methodDeclaration.Name;

        var parameterTokens = methodDeclaration.Parameters
            .OrderBy(x => x.Ordinal)
            .Select(x => x.Type?.DisplayText ?? "unknown")
            .Select(SanitizePathSegment)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        var suffix = parameterTokens.Length == 0
            ? "no-params"
            : string.Join("--", parameterTokens);

        var genericSuffix = methodDeclaration.Arity > 0
            ? $"--g{methodDeclaration.Arity}"
            : string.Empty;

        return SanitizePathSegment($"{prefix}--{suffix}{genericSuffix}");
    }
}
