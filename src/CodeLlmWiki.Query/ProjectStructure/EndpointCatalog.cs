namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record EndpointCatalog(
    IReadOnlyList<EndpointGroupNode> Groups,
    IReadOnlyList<EndpointNode> Endpoints,
    IReadOnlyList<EndpointDiagnosticCountNode> Diagnostics)
{
    public static EndpointCatalog Empty { get; } = new([], [], []);
}
