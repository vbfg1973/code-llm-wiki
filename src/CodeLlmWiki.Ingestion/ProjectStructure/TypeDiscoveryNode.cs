namespace CodeLlmWiki.Ingestion.ProjectStructure;

internal sealed record TypeDiscoveryNode(
    string NamespaceName,
    string QualifiedName,
    string TypeName,
    string Kind,
    string Accessibility,
    bool IsPartialDeclaration,
    int Arity,
    IReadOnlyList<string> GenericParameters,
    IReadOnlyList<string> GenericConstraints,
    string? DeclaringTypeQualifiedName,
    IReadOnlyList<string> DirectBaseTypeNames,
    IReadOnlyList<string> DirectInterfaceTypeNames,
    IReadOnlyList<string> ImportedNamespaces,
    IReadOnlyDictionary<string, string> ImportedAliases,
    string RelativeFilePath);
