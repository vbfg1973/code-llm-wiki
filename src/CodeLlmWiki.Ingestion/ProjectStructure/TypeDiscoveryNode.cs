namespace CodeLlmWiki.Ingestion.ProjectStructure;

internal sealed record TypeDiscoveryNode(
    string NamespaceName,
    string QualifiedName,
    string TypeName,
    string Kind,
    string Accessibility,
    int Arity,
    string? DeclaringTypeQualifiedName,
    IReadOnlyList<string> DirectBaseTypeNames,
    IReadOnlyList<string> DirectInterfaceTypeNames,
    string RelativeFilePath);
