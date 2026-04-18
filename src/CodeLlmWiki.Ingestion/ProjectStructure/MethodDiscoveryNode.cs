namespace CodeLlmWiki.Ingestion.ProjectStructure;

internal sealed record MethodDiscoveryNode(
    string Kind,
    string Name,
    string CanonicalName,
    string Accessibility,
    bool IsOverride,
    int Arity,
    string? ReturnTypeName,
    IReadOnlyList<MethodParameterDiscoveryNode> Parameters,
    string RelativeFilePath,
    int SourceLine,
    int SourceColumn);
