namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record EndpointCatalog(
    IReadOnlyList<EndpointGroupNode> Groups,
    IReadOnlyList<EndpointNode> Endpoints)
{
    public static EndpointCatalog Empty { get; } = new([], []);
}
