namespace CodeLlmWiki.Ingestion.ProjectStructure;

internal sealed record MemberDiscoveryNode(
    string Kind,
    string Name,
    string Accessibility,
    string? DeclaredTypeName,
    string? ConstantValue,
    string RelativeFilePath,
    int SourceLine,
    int SourceColumn);
