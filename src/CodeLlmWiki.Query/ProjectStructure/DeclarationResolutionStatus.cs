namespace CodeLlmWiki.Query.ProjectStructure;

public enum DeclarationResolutionStatus
{
    Unknown = 0,
    Resolved,
    SourceTextFallback,
    Unresolved,
}
