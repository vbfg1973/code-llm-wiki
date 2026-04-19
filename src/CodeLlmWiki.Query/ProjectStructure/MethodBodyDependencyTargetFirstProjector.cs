using CodeLlmWiki.Contracts.Graph;
using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed class MethodBodyDependencyTargetFirstProjector : IMethodBodyDependencyTargetFirstProjector
{
    public IReadOnlyDictionary<EntityId, PackageMethodBodyDependencyTargetFirstCatalog> Project(MethodBodyDependencyUsageProjectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var metadataById = BuildMetadata(request.Triples);
        var methodById = request.Methods.ToDictionary(x => x.Id, x => x);
        var typeById = request.Types.ToDictionary(x => x.Id, x => x);
        var packageById = request.Packages.ToDictionary(x => x.Id, x => x);
        var packageAttributionResolver = new ProjectScopedPackageAttributionResolver(
            request.Projects,
            request.Packages,
            request.Files,
            request.Methods);

        var rows = request.Triples
            .Where(x => x.Predicate == CorePredicates.DependsOnTypeInMethodBody)
            .Where(x => x.Subject is EntityNode && x.Object is EntityNode)
            .Select(x => (MethodId: ((EntityNode)x.Subject).Id, TargetTypeId: ((EntityNode)x.Object).Id))
            .Where(x => methodById.ContainsKey(x.MethodId))
            .Select(x => BuildUsageRow(
                x.MethodId,
                x.TargetTypeId,
                methodById,
                typeById,
                packageById,
                packageAttributionResolver,
                metadataById))
            .Where(x => x is not null)
            .Select(x => x!)
            .ToArray();

        return rows
            .GroupBy(x => x.PackageId)
            .ToDictionary(
                x => x.Key,
                x =>
                {
                    var externalTypes = x
                        .GroupBy(v => (v.ExternalTypeId, v.ExternalTypeDisplayName))
                        .Select(target =>
                        {
                            var internalMethods = target
                                .GroupBy(v => (v.InternalMethodId, v.InternalMethodDisplayName))
                                .Select(internalMethod => new PackageMethodBodyInternalMethodUsageNode(
                                    InternalMethodId: internalMethod.Key.InternalMethodId,
                                    InternalMethodDisplayName: internalMethod.Key.InternalMethodDisplayName,
                                    UsageCount: internalMethod.Sum(v => v.UsageCount)))
                                .OrderBy(v => v.InternalMethodDisplayName, StringComparer.Ordinal)
                                .ThenBy(v => v.InternalMethodId.Value, StringComparer.Ordinal)
                                .ToArray();

                            return new PackageMethodBodyExternalTypeUsageNode(
                                ExternalTypeId: target.Key.ExternalTypeId,
                                ExternalTypeDisplayName: target.Key.ExternalTypeDisplayName,
                                UsageCount: target.Sum(v => v.UsageCount),
                                InternalMethods: internalMethods);
                        })
                        .OrderBy(v => v.ExternalTypeDisplayName, StringComparer.Ordinal)
                        .ThenBy(v => v.ExternalTypeId.Value, StringComparer.Ordinal)
                        .ToArray();

                    return new PackageMethodBodyDependencyTargetFirstCatalog(
                        UsageCount: x.Sum(v => v.UsageCount),
                        ExternalTypes: externalTypes);
                });
    }

    private static UsageRow? BuildUsageRow(
        EntityId methodId,
        EntityId targetTypeId,
        IReadOnlyDictionary<EntityId, MethodDeclarationNode> methodById,
        IReadOnlyDictionary<EntityId, TypeDeclarationNode> typeById,
        IReadOnlyDictionary<EntityId, PackageNode> packageById,
        IProjectScopedPackageAttributionResolver packageAttributionResolver,
        IReadOnlyDictionary<EntityId, ProjectionMetadata> metadataById)
    {
        if (!methodById.TryGetValue(methodId, out var method)
            || !typeById.TryGetValue(method.DeclaringTypeId, out _)
            || !metadataById.TryGetValue(targetTypeId, out var targetTypeMetadata))
        {
            return null;
        }

        var attribution = packageAttributionResolver.Resolve(methodId, targetTypeMetadata.Name);
        if (attribution.Status != PackageAttributionStatus.Attributed
            || attribution.PackageId is null
            || !packageById.TryGetValue(attribution.PackageId.Value, out var package))
        {
            return null;
        }

        var externalTypeName = string.IsNullOrWhiteSpace(targetTypeMetadata.Name)
            ? targetTypeId.Value
            : targetTypeMetadata.Name;

        return new UsageRow(
            PackageId: package.Id,
            ExternalTypeId: targetTypeId,
            ExternalTypeDisplayName: externalTypeName,
            InternalMethodId: method.Id,
            InternalMethodDisplayName: method.Signature,
            UsageCount: 1);
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
            else if (triple.Predicate == CorePredicates.HasPath)
            {
                metadata.Path = value;
            }
            else if (triple.Predicate == CorePredicates.EntityType)
            {
                metadata.EntityType = value;
            }
        }

        return metadataById;
    }

    private sealed class ProjectionMetadata
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
    }

    private sealed record UsageRow(
        EntityId PackageId,
        EntityId ExternalTypeId,
        string ExternalTypeDisplayName,
        EntityId InternalMethodId,
        string InternalMethodDisplayName,
        int UsageCount);
}
