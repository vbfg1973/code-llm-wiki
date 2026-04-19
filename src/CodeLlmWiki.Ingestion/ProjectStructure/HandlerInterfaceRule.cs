namespace CodeLlmWiki.Ingestion.ProjectStructure;

public sealed record HandlerInterfaceRule(
    string RuleId,
    string RuleVersion,
    string RuleSource,
    HandlerInterfaceMatchKind MatchKind,
    string MatchName,
    string? Prefix = null,
    string? Suffix = null)
{
    public bool IsMatch(string interfaceName)
    {
        if (string.IsNullOrWhiteSpace(interfaceName))
        {
            return false;
        }

        return MatchKind switch
        {
            HandlerInterfaceMatchKind.ExactName => string.Equals(interfaceName, MatchName, StringComparison.Ordinal),
            HandlerInterfaceMatchKind.PrefixAndSuffix => !string.IsNullOrWhiteSpace(Prefix)
                && !string.IsNullOrWhiteSpace(Suffix)
                && interfaceName.StartsWith(Prefix, StringComparison.Ordinal)
                && interfaceName.EndsWith(Suffix, StringComparison.Ordinal),
            _ => false,
        };
    }
}
