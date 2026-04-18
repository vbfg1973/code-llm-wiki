namespace CodeLlmWiki.Ingestion.ProjectStructure;

internal sealed record NamespaceDiscoveryNode(
    string Name,
    string? ParentName,
    IReadOnlyList<string> DeclarationFilePaths,
    IReadOnlyList<DeclarationSourceLocation> DeclarationLocations);
