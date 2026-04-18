using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;

namespace CodeLlmWiki.Cli.Features.Ingest;

public sealed class WikiScopedLinkInvariantValidator : IWikiScopedLinkInvariantValidator
{
    private const string NamespaceSectionPath = "## Contained Types";
    private const string FileMethodsSectionPath = "## Declared Symbols > ### Methods";

    public WikiScopedLinkInvariantValidationResult Validate(WikiScopedLinkInvariantValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Model);
        ArgumentNullException.ThrowIfNull(request.Pages);

        var pageByEntityId = BuildPageIndex(request.Pages);
        var methodCountByFileId = request.Model.Declarations.Methods.Declarations
            .SelectMany(method => method.DeclarationFileIds.Select(fileId => (fileId, method.Id)))
            .GroupBy(x => x.fileId)
            .ToDictionary(
                x => x.Key,
                x => x.Select(v => v.Id)
                    .Distinct()
                    .Count());

        var violations = new List<WikiScopedLinkInvariantViolation>();

        foreach (var namespaceDeclaration in request.Model.Declarations.Namespaces)
        {
            if (namespaceDeclaration.ContainedTypeIds.Count == 0)
            {
                continue;
            }

            if (!TryGetEntityPage(pageByEntityId, namespaceDeclaration.Id, "namespace", out var page))
            {
                violations.Add(new WikiScopedLinkInvariantViolation(
                    PageRelativePath: $"missing:{namespaceDeclaration.Id.Value}",
                    SectionPath: NamespaceSectionPath,
                    LineNumber: 1,
                    LineText: "-",
                    Message: "Missing namespace page for invariant validation."));
                continue;
            }

            ValidateSectionBullets(page, NamespaceSectionPath, parentHeading: null, childHeading: "## Contained Types", violations);
        }

        foreach (var file in request.Model.Files)
        {
            if (!methodCountByFileId.TryGetValue(file.Id, out var methodCount) || methodCount == 0)
            {
                continue;
            }

            if (!TryGetEntityPage(pageByEntityId, file.Id, "file", out var page))
            {
                violations.Add(new WikiScopedLinkInvariantViolation(
                    PageRelativePath: $"missing:{file.Id.Value}",
                    SectionPath: FileMethodsSectionPath,
                    LineNumber: 1,
                    LineText: "-",
                    Message: "Missing file page for invariant validation."));
                continue;
            }

            ValidateSectionBullets(page, FileMethodsSectionPath, parentHeading: "## Declared Symbols", childHeading: "### Methods", violations);
        }

        var ordered = violations
            .OrderBy(x => x.PageRelativePath, StringComparer.Ordinal)
            .ThenBy(x => x.SectionPath, StringComparer.Ordinal)
            .ThenBy(x => x.LineNumber)
            .ThenBy(x => x.LineText, StringComparer.Ordinal)
            .ToArray();

        return new WikiScopedLinkInvariantValidationResult(ordered);
    }

    private static Dictionary<string, IndexedWikiPage> BuildPageIndex(IReadOnlyList<WikiPage> pages)
    {
        var index = new Dictionary<string, IndexedWikiPage>(StringComparer.Ordinal);

        foreach (var page in pages)
        {
            if (!TryParseFrontMatter(page.Markdown, out var frontMatter))
            {
                continue;
            }

            if (!frontMatter.TryGetValue("entity_id", out var entityId) || string.IsNullOrWhiteSpace(entityId))
            {
                continue;
            }

            frontMatter.TryGetValue("entity_type", out var entityType);
            index[entityId] = new IndexedWikiPage(page.RelativePath, page.Markdown, entityType ?? string.Empty);
        }

        return index;
    }

    private static bool TryGetEntityPage(
        IReadOnlyDictionary<string, IndexedWikiPage> pageByEntityId,
        EntityId entityId,
        string expectedEntityType,
        out IndexedWikiPage page)
    {
        if (pageByEntityId.TryGetValue(entityId.Value, out page)
            && page.EntityType.Equals(expectedEntityType, StringComparison.Ordinal))
        {
            return true;
        }

        page = default;
        return false;
    }

    private static void ValidateSectionBullets(
        IndexedWikiPage page,
        string sectionPath,
        string? parentHeading,
        string childHeading,
        ICollection<WikiScopedLinkInvariantViolation> violations)
    {
        var lines = SplitLines(page.Markdown);
        var sectionRange = FindSectionRange(lines, parentHeading, childHeading);
        if (sectionRange is null)
        {
            violations.Add(new WikiScopedLinkInvariantViolation(
                PageRelativePath: page.RelativePath,
                SectionPath: sectionPath,
                LineNumber: 1,
                LineText: "-",
                Message: "Scoped section heading not found."));
            return;
        }

        var (start, end) = sectionRange.Value;
        var sectionHasBullets = false;

        for (var index = start + 1; index < end; index++)
        {
            var line = lines[index];
            var trimmed = line.TrimStart();

            if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                continue;
            }

            sectionHasBullets = true;
            if (!trimmed.StartsWith("- [[", StringComparison.Ordinal))
            {
                violations.Add(new WikiScopedLinkInvariantViolation(
                    PageRelativePath: page.RelativePath,
                    SectionPath: sectionPath,
                    LineNumber: index + 1,
                    LineText: line,
                    Message: "Expected wiki link bullet for resolvable target."));
            }
        }

        if (!sectionHasBullets)
        {
            violations.Add(new WikiScopedLinkInvariantViolation(
                PageRelativePath: page.RelativePath,
                SectionPath: sectionPath,
                LineNumber: start + 1,
                LineText: childHeading,
                Message: "Scoped section contains no bullet entries."));
        }
    }

    private static (int Start, int End)? FindSectionRange(
        IReadOnlyList<string> lines,
        string? parentHeading,
        string childHeading)
    {
        if (parentHeading is null)
        {
            var child = FindHeading(lines, 0, lines.Count, childHeading);
            if (child < 0)
            {
                return null;
            }

            return (child, FindNextHeading(lines, child + 1, "## "));
        }

        var parent = FindHeading(lines, 0, lines.Count, parentHeading);
        if (parent < 0)
        {
            return null;
        }

        var parentEnd = FindNextHeading(lines, parent + 1, "## ");
        var childInParent = FindHeading(lines, parent + 1, parentEnd, childHeading);
        if (childInParent < 0)
        {
            return null;
        }

        var childEnd = FindNextHeading(lines, childInParent + 1, "### ", "## ");
        return (childInParent, childEnd);
    }

    private static int FindHeading(IReadOnlyList<string> lines, int start, int end, string heading)
    {
        for (var index = start; index < end; index++)
        {
            if (lines[index].Trim().Equals(heading, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindNextHeading(IReadOnlyList<string> lines, int start, params string[] prefixes)
    {
        for (var index = start; index < lines.Count; index++)
        {
            var trimmed = lines[index].TrimStart();
            if (prefixes.Any(prefix => trimmed.StartsWith(prefix, StringComparison.Ordinal)))
            {
                return index;
            }
        }

        return lines.Count;
    }

    private static IReadOnlyList<string> SplitLines(string markdown)
    {
        return markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
    }

    private static bool TryParseFrontMatter(string markdown, out Dictionary<string, string> values)
    {
        values = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = SplitLines(markdown);
        if (lines.Count < 3 || !lines[0].Trim().Equals("---", StringComparison.Ordinal))
        {
            return false;
        }

        for (var index = 1; index < lines.Count; index++)
        {
            var line = lines[index].Trim();
            if (line.Equals("---", StringComparison.Ordinal))
            {
                return values.Count > 0;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Length == 0)
            {
                continue;
            }

            values[key] = value;
        }

        return false;
    }

    private readonly record struct IndexedWikiPage(string RelativePath, string Markdown, string EntityType);
}
