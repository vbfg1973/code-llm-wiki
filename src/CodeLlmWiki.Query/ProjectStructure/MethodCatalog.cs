namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record MethodCatalog(
    IReadOnlyList<MethodDeclarationNode> Declarations,
    IReadOnlyList<MethodRelationNode> Relations)
{
    public static MethodCatalog Empty { get; } = new([], []);
}
