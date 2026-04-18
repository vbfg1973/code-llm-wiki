namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record DeclarationLocationNode(
    string FilePath,
    int Line,
    int Column);
