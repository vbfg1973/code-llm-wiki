namespace CodeLlmWiki.Ingestion.ProjectStructure.CallResolution;

public readonly record struct SemanticContextInfo(
    SemanticContextMode Mode,
    string AssemblyName,
    string? ProjectPath);
