namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record MethodParameterNode(
    string Name,
    int Ordinal,
    TypeReferenceNode? Type);
