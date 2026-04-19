namespace CodeLlmWiki.Ingestion.Telemetry;

public static class IngestionStageIds
{
    public const string ProjectDiscovery = "project_discovery";
    public const string SourceSnapshot = "source_snapshot";
    public const string DeclarationScan = "declaration_scan";
    public const string SemanticCallGraph = "semantic_call_graph";
    public const string EndpointExtraction = "endpoint_extraction";
    public const string QueryProjection = "query_projection";
    public const string WikiRender = "wiki_render";
    public const string GraphMlSerialize = "graphml_serialize";

    public static readonly IReadOnlyList<string> All =
    [
        ProjectDiscovery,
        SourceSnapshot,
        DeclarationScan,
        SemanticCallGraph,
        EndpointExtraction,
        QueryProjection,
        WikiRender,
        GraphMlSerialize,
    ];
}
