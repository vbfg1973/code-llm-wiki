namespace CodeLlmWiki.Ingestion.ProjectStructure;

internal sealed record TypeDiscoveryNode(
    string NamespaceName,
    string TypeName,
    string Kind,
    string RelativeFilePath);
