namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record EndpointDiagnosticCountNode(
    string Family,
    string Reason,
    int Count);
