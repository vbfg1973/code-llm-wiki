using CodeLlmWiki.Contracts.Graph;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record DeclarationDependencyUsageProjectionRequest(
    IReadOnlyList<SemanticTriple> Triples,
    IReadOnlyList<ProjectNode> Projects,
    IReadOnlyList<PackageNode> Packages,
    IReadOnlyList<FileNode> Files,
    IReadOnlyList<NamespaceDeclarationNode> Namespaces,
    IReadOnlyList<TypeDeclarationNode> Types,
    IReadOnlyList<MethodDeclarationNode> Methods);
