namespace CodeLlmWiki.Query.ProjectStructure;

public enum PackageAttributionReason
{
    None = 0,
    SourceMethodNotFound = 1,
    SourceProjectNotFound = 2,
    NoProjectScopedMatch = 3,
    AmbiguousProjectScopedMatch = 4,
}
