using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using CodeLlmWiki.Contracts.Graph;

namespace CodeLlmWiki.Cli.Features.Ingest;

public static class GraphMlSerializer
{
    public static GraphMlSerializationResult Serialize(IReadOnlyList<SemanticTriple> triples)
    {
        var nodes = BuildNodes(triples);
        var nodeIdByKey = nodes
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select((node, index) => new { node.Key, NodeId = $"n{index + 1:D6}" })
            .ToDictionary(x => x.Key, x => x.NodeId, StringComparer.Ordinal);

        var edgeRecords = triples
            .Select((triple, index) => new
            {
                Index = index,
                SubjectKey = ToNodeKey(triple.Subject),
                Predicate = triple.Predicate.Value,
                ObjectKey = ToNodeKey(triple.Object),
            })
            .OrderBy(x => x.SubjectKey, StringComparer.Ordinal)
            .ThenBy(x => x.Predicate, StringComparer.Ordinal)
            .ThenBy(x => x.ObjectKey, StringComparer.Ordinal)
            .ThenBy(x => x.Index)
            .ToArray();

        XNamespace ns = "http://graphml.graphdrawing.org/xmlns";

        var graphElement = new XElement(ns + "graph", new XAttribute("edgedefault", "directed"));

        foreach (var node in nodes.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            graphElement.Add(new XElement(
                ns + "node",
                new XAttribute("id", nodeIdByKey[node.Key]),
                new XElement(ns + "data", new XAttribute("key", "d0"), node.Kind),
                new XElement(ns + "data", new XAttribute("key", "d1"), node.Label)));
        }

        foreach (var edge in edgeRecords.Select((value, index) => new { value, index }))
        {
            graphElement.Add(new XElement(
                ns + "edge",
                new XAttribute("id", $"e{edge.index + 1:D6}"),
                new XAttribute("source", nodeIdByKey[edge.value.SubjectKey]),
                new XAttribute("target", nodeIdByKey[edge.value.ObjectKey]),
                new XElement(ns + "data", new XAttribute("key", "d2"), edge.value.Predicate)));
        }

        var document = new XDocument(
            new XElement(
                ns + "graphml",
                new XElement(
                    ns + "key",
                    new XAttribute("id", "d0"),
                    new XAttribute("for", "node"),
                    new XAttribute("attr.name", "kind"),
                    new XAttribute("attr.type", "string")),
                new XElement(
                    ns + "key",
                    new XAttribute("id", "d1"),
                    new XAttribute("for", "node"),
                    new XAttribute("attr.name", "label"),
                    new XAttribute("attr.type", "string")),
                new XElement(
                    ns + "key",
                    new XAttribute("id", "d2"),
                    new XAttribute("for", "edge"),
                    new XAttribute("attr.name", "predicate"),
                    new XAttribute("attr.type", "string")),
                graphElement));

        return new GraphMlSerializationResult(
            document.ToString(SaveOptions.DisableFormatting),
            nodes.Count,
            edgeRecords.Length);
    }

    private static IReadOnlyList<GraphMlNode> BuildNodes(IReadOnlyList<SemanticTriple> triples)
    {
        var byKey = new Dictionary<string, GraphMlNode>(StringComparer.Ordinal);

        foreach (var triple in triples)
        {
            var subjectNode = ToNode(triple.Subject);
            if (!byKey.ContainsKey(subjectNode.Key))
            {
                byKey[subjectNode.Key] = subjectNode;
            }

            var objectNode = ToNode(triple.Object);
            if (!byKey.ContainsKey(objectNode.Key))
            {
                byKey[objectNode.Key] = objectNode;
            }
        }

        return byKey.Values.ToArray();
    }

    private static GraphMlNode ToNode(GraphNode node)
    {
        return node switch
        {
            EntityNode entity => new GraphMlNode(
                Key: ToNodeKey(node),
                Kind: "entity",
                Label: entity.Id.Value),
            LiteralNode literal => new GraphMlNode(
                Key: ToNodeKey(node),
                Kind: "literal",
                Label: literal.Value?.ToString() ?? string.Empty),
            _ => new GraphMlNode(
                Key: ToNodeKey(node),
                Kind: "unknown",
                Label: node.ToString() ?? string.Empty),
        };
    }

    private static string ToNodeKey(GraphNode node)
    {
        return node switch
        {
            EntityNode entity => $"entity:{entity.Id.Value}",
            LiteralNode literal => "literal:" + Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(literal.Value?.ToString() ?? string.Empty))),
            _ => $"unknown:{node}",
        };
    }

    private sealed record GraphMlNode(string Key, string Kind, string Label);
}

public sealed record GraphMlSerializationResult(
    string GraphMl,
    int NodeCount,
    int EdgeCount);
