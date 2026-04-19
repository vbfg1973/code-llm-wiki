namespace CodeLlmWiki.Ingestion.ProjectStructure;

internal sealed record DeclarationSourceLocation(
    string RelativeFilePath,
    int Line,
    int Column);
