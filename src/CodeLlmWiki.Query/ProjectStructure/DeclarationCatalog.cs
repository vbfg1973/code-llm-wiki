namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record DeclarationCatalog(
    IReadOnlyList<NamespaceDeclarationNode> Namespaces,
    IReadOnlyList<TypeDeclarationNode> Types,
    IReadOnlyList<MemberDeclarationNode> Members)
{
    public MethodCatalog Methods { get; init; } = MethodCatalog.Empty;

    public static DeclarationCatalog Empty { get; } = new([], [], []);
}
