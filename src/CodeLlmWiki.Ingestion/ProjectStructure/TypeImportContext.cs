namespace CodeLlmWiki.Ingestion.ProjectStructure;

internal sealed record TypeImportContext(
    IReadOnlyList<string> Namespaces,
    IReadOnlyDictionary<string, string> Aliases)
{
    public static TypeImportContext Empty { get; } = new([], new Dictionary<string, string>(StringComparer.Ordinal));
}
