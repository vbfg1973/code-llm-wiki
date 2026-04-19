using System.Text;
using CodeLlmWiki.Contracts.Graph;
using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed class UnknownDependencyUsageProjector : IUnknownDependencyUsageProjector
{
    public UnknownDependencyUsageCatalog Project(UnknownDependencyUsageProjectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var metadataById = BuildMetadata(request.Triples);
        var methodById = request.Methods.ToDictionary(x => x.Id, x => x);
        var typeById = request.Types.ToDictionary(x => x.Id, x => x);
        var namespaceById = request.Namespaces.ToDictionary(x => x.Id, x => x);
        var packageAttributionResolver = new ProjectScopedPackageAttributionResolver(
            request.Projects,
            request.Packages,
            request.Files,
            request.Methods);

        var rows = request.Triples
            .Where(x => x.Predicate == request.DependencyPredicate)
            .Where(x => x.Subject is EntityNode && x.Object is EntityNode)
            .Select(x => (MethodId: ((EntityNode)x.Subject).Id, TargetTypeId: ((EntityNode)x.Object).Id))
            .Where(x => methodById.ContainsKey(x.MethodId))
            .Select(x => BuildUsageRow(
                x.MethodId,
                x.TargetTypeId,
                methodById,
                typeById,
                namespaceById,
                request.Packages,
                metadataById,
                packageAttributionResolver))
            .Where(x => x is not null)
            .Select(x => x!)
            .ToArray();

        var namespaces = rows
            .GroupBy(x => (x.NamespaceId, x.NamespaceName))
            .Select(ns =>
            {
                var types = ns
                    .GroupBy(x => (x.TypeId, x.TypeName))
                    .Select(type =>
                    {
                        var methods = type
                            .GroupBy(x => (
                                x.MethodId,
                                x.MethodSignature,
                                x.TargetTypeName,
                                x.AttributionReason,
                                x.TargetResolutionReason))
                            .Select(method => new UnknownDependencyMethodUsageNode(
                                MethodId: method.Key.MethodId,
                                MethodSignature: method.Key.MethodSignature,
                                TargetTypeName: method.Key.TargetTypeName,
                                AttributionReason: method.Key.AttributionReason,
                                TargetResolutionReason: method.Key.TargetResolutionReason,
                                UsageCount: method.Sum(x => x.UsageCount)))
                            .OrderByDescending(x => x.UsageCount)
                            .ThenBy(x => x.MethodSignature, StringComparer.Ordinal)
                            .ThenBy(x => x.TargetTypeName, StringComparer.Ordinal)
                            .ThenBy(x => x.AttributionReason, StringComparer.Ordinal)
                            .ThenBy(x => x.TargetResolutionReason, StringComparer.Ordinal)
                            .ThenBy(x => x.MethodId.Value, StringComparer.Ordinal)
                            .ToArray();

                        return new UnknownDependencyTypeUsageNode(
                            TypeId: type.Key.TypeId,
                            TypeName: type.Key.TypeName,
                            UsageCount: type.Sum(x => x.UsageCount),
                            Methods: methods);
                    })
                    .OrderByDescending(x => x.UsageCount)
                    .ThenBy(x => x.TypeName, StringComparer.Ordinal)
                    .ThenBy(x => x.TypeId.Value, StringComparer.Ordinal)
                    .ToArray();

                return new UnknownDependencyNamespaceUsageNode(
                    NamespaceId: ns.Key.NamespaceId,
                    NamespaceName: ns.Key.NamespaceName,
                    UsageCount: ns.Sum(x => x.UsageCount),
                    Types: types);
            })
            .OrderByDescending(x => x.UsageCount)
            .ThenBy(x => x.NamespaceName, StringComparer.Ordinal)
            .ThenBy(x => x.NamespaceId?.Value ?? string.Empty, StringComparer.Ordinal)
            .ToArray();

        return new UnknownDependencyUsageCatalog(
            UsageCount: rows.Sum(x => x.UsageCount),
            Namespaces: namespaces);
    }

    private static UsageRow? BuildUsageRow(
        EntityId methodId,
        EntityId targetTypeId,
        IReadOnlyDictionary<EntityId, MethodDeclarationNode> methodById,
        IReadOnlyDictionary<EntityId, TypeDeclarationNode> typeById,
        IReadOnlyDictionary<EntityId, NamespaceDeclarationNode> namespaceById,
        IReadOnlyList<PackageNode> packages,
        IReadOnlyDictionary<EntityId, ProjectionMetadata> metadataById,
        IProjectScopedPackageAttributionResolver packageAttributionResolver)
    {
        if (!methodById.TryGetValue(methodId, out var method)
            || !typeById.TryGetValue(method.DeclaringTypeId, out var declaringType)
            || !metadataById.TryGetValue(targetTypeId, out var targetTypeMeta))
        {
            return null;
        }

        var attribution = packageAttributionResolver.Resolve(methodId, targetTypeMeta.Name);
        if (attribution.Status == PackageAttributionStatus.Attributed)
        {
            return null;
        }

        if (!ShouldIncludeUnknown(targetTypeMeta, attribution, packages))
        {
            return null;
        }

        var namespaceName = declaringType.NamespaceId is { } namespaceId && namespaceById.TryGetValue(namespaceId, out var namespaceDeclaration)
            ? namespaceDeclaration.Name
            : "<global>";

        return new UsageRow(
            NamespaceId: declaringType.NamespaceId,
            NamespaceName: namespaceName,
            TypeId: declaringType.Id,
            TypeName: declaringType.Name,
            MethodId: method.Id,
            MethodSignature: method.Signature,
            TargetTypeName: targetTypeMeta.Name,
            AttributionReason: ToSnakeCase(attribution.Reason.ToString()),
            TargetResolutionReason: targetTypeMeta.ResolutionReason,
            UsageCount: 1);
    }

    private static bool ShouldIncludeUnknown(
        ProjectionMetadata targetTypeMeta,
        PackageAttributionResolution attribution,
        IReadOnlyList<PackageNode> packages)
    {
        if (attribution.Reason == PackageAttributionReason.AmbiguousProjectScopedMatch)
        {
            return true;
        }

        if (string.Equals(targetTypeMeta.EntityType, "unresolved-type-reference", StringComparison.Ordinal))
        {
            return true;
        }

        if (attribution.Reason != PackageAttributionReason.NoProjectScopedMatch)
        {
            return false;
        }

        return packages.Any(package => PackageMatchesReference(package, targetTypeMeta.Name));
    }

    private static bool PackageMatchesReference(PackageNode package, string referenceName)
    {
        if (string.IsNullOrWhiteSpace(referenceName))
        {
            return false;
        }

        return referenceName.Equals(package.Name, StringComparison.OrdinalIgnoreCase)
               || referenceName.StartsWith(package.Name + ".", StringComparison.OrdinalIgnoreCase)
               || referenceName.Equals(package.CanonicalKey, StringComparison.OrdinalIgnoreCase)
               || referenceName.StartsWith(package.CanonicalKey + ".", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<EntityId, ProjectionMetadata> BuildMetadata(IReadOnlyList<SemanticTriple> triples)
    {
        var metadataById = new Dictionary<EntityId, ProjectionMetadata>();

        foreach (var triple in triples)
        {
            if (triple.Subject is not EntityNode subject || triple.Object is not LiteralNode literal)
            {
                continue;
            }

            if (!metadataById.TryGetValue(subject.Id, out var metadata))
            {
                metadata = new ProjectionMetadata();
                metadataById[subject.Id] = metadata;
            }

            var value = literal.Value?.ToString() ?? string.Empty;
            if (triple.Predicate == CorePredicates.HasName)
            {
                metadata.Name = value;
            }
            else if (triple.Predicate == CorePredicates.EntityType)
            {
                metadata.EntityType = value;
            }
            else if (triple.Predicate == CorePredicates.ResolutionReason)
            {
                metadata.ResolutionReason = value;
            }
        }

        return metadataById;
    }

    private static string ToSnakeCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                {
                    sb.Append('_');
                }

                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private sealed class ProjectionMetadata
    {
        public string Name { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public string ResolutionReason { get; set; } = string.Empty;
    }

    private sealed record UsageRow(
        EntityId? NamespaceId,
        string NamespaceName,
        EntityId TypeId,
        string TypeName,
        EntityId MethodId,
        string MethodSignature,
        string TargetTypeName,
        string AttributionReason,
        string TargetResolutionReason,
        int UsageCount);
}
