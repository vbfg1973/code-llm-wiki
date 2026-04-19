namespace CodeLlmWiki.Query.ProjectStructure;

public static class DeclarationOrderingRules
{
    public static string GetDeterministicSortKey(
        string namespaceName,
        string displayName,
        string path,
        string stableId)
    {
        return string.Join(
            "|",
            Normalize(namespaceName),
            Normalize(displayName),
            Normalize(path),
            Normalize(stableId));
    }

    private static string Normalize(string value)
    {
        return (value ?? string.Empty).Trim();
    }
}
