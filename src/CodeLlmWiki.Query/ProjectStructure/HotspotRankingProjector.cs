using System.Globalization;
using CodeLlmWiki.Contracts.Graph;
using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed class HotspotRankingProjector : IHotspotRankingProjector
{
    private static readonly IReadOnlyDictionary<HotspotMetricKind, double> DefaultCompositeWeights =
        new Dictionary<HotspotMetricKind, double>
        {
            [HotspotMetricKind.MethodCyclomaticComplexity] = 1d,
            [HotspotMetricKind.MethodCognitiveComplexity] = 1d,
            [HotspotMetricKind.MethodHalsteadVolume] = 0.7d,
            [HotspotMetricKind.MethodMaintainabilityIndex] = 1.2d,
            [HotspotMetricKind.TypeCboDeclaration] = 0.8d,
            [HotspotMetricKind.TypeCboMethodBody] = 0.8d,
            [HotspotMetricKind.TypeCboTotal] = 1d,
            [HotspotMetricKind.ScopeAverageCyclomaticComplexity] = 0.8d,
            [HotspotMetricKind.ScopeAverageCognitiveComplexity] = 0.8d,
            [HotspotMetricKind.ScopeAverageMaintainabilityIndex] = 1d,
            [HotspotMetricKind.ScopeAverageCboTotal] = 0.8d,
            [HotspotMetricKind.Composite] = 1d,
        };

    private static readonly IReadOnlyDictionary<HotspotMetricKind, HotspotSeverityThresholds> DefaultThresholds =
        Enum.GetValues<HotspotMetricKind>().ToDictionary(
            key => key,
            _ => new HotspotSeverityThresholds(
                Low: 0.25d,
                Medium: 0.50d,
                High: 0.75d,
                Critical: 0.90d));

    public HotspotRankingCatalog Project(HotspotRankingProjectionRequest request)
    {
        var effectiveTopN = NormalizeTopN(request.Options.TopN);
        var effectiveWeights = ResolveWeights(request.Options.CompositeWeightOverrides);
        var effectiveThresholds = ResolveThresholds(request.Options.ThresholdOverrides);
        var subjectsByKind = BuildSubjectsByKind(request.Triples, request.Declarations, request.StructuralMetrics);

        var primaryRankings = new List<HotspotMetricRankingNode>();
        var compositeRankings = new List<HotspotCompositeRankingNode>();

        foreach (var targetGroup in subjectsByKind.OrderBy(x => x.Key))
        {
            var targetKind = targetGroup.Key;
            var subjects = targetGroup.Value;
            if (subjects.Count == 0)
            {
                continue;
            }

            var metricKinds = subjects
                .SelectMany(subject => subject.Metrics.Keys)
                .Distinct()
                .OrderBy(x => x)
                .ToArray();
            if (metricKinds.Length == 0)
            {
                continue;
            }

            var normalizedByMetricByEntityId = new Dictionary<HotspotMetricKind, IReadOnlyDictionary<EntityId, double>>();
            foreach (var metricKind in metricKinds)
            {
                var rows = BuildMetricRows(
                    subjects,
                    metricKind,
                    effectiveThresholds[metricKind],
                    effectiveTopN,
                    request.Options.Unbounded,
                    out var normalizedByEntityId);

                normalizedByMetricByEntityId[metricKind] = normalizedByEntityId;
                primaryRankings.Add(new HotspotMetricRankingNode(targetKind, metricKind, rows));
            }

            var compositeRows = BuildCompositeRows(
                subjects,
                metricKinds,
                normalizedByMetricByEntityId,
                effectiveWeights,
                effectiveThresholds[HotspotMetricKind.Composite],
                effectiveTopN,
                request.Options.Unbounded);
            compositeRankings.Add(new HotspotCompositeRankingNode(targetKind, compositeRows));
        }

        return new HotspotRankingCatalog(
            new HotspotRankingEffectiveConfig(
                EffectiveTopN: effectiveTopN,
                Unbounded: request.Options.Unbounded,
                CompositeWeights: effectiveWeights,
                Thresholds: effectiveThresholds),
            primaryRankings
                .OrderBy(x => x.TargetKind)
                .ThenBy(x => x.MetricKind)
                .ToArray(),
            compositeRankings
                .OrderBy(x => x.TargetKind)
                .ToArray());
    }

    private static IReadOnlyList<HotspotRankingRow> BuildMetricRows(
        IReadOnlyList<HotspotSubject> subjects,
        HotspotMetricKind metricKind,
        HotspotSeverityThresholds thresholds,
        int effectiveTopN,
        bool unbounded,
        out IReadOnlyDictionary<EntityId, double> normalizedByEntityId)
    {
        var subjectsWithMetric = subjects
            .Where(subject => subject.Metrics.ContainsKey(metricKind))
            .ToArray();
        if (subjectsWithMetric.Length == 0)
        {
            normalizedByEntityId = new Dictionary<EntityId, double>();
            return [];
        }

        var values = subjectsWithMetric.Select(subject => subject.Metrics[metricKind]).ToArray();
        var min = values.Min();
        var max = values.Max();
        var direction = GetDirection(metricKind);

        var normalizedRows = subjectsWithMetric
            .Select(subject =>
            {
                var rawValue = subject.Metrics[metricKind];
                var normalized = Normalize(rawValue, min, max, direction);
                return new
                {
                    Subject = subject,
                    RawValue = rawValue,
                    Normalized = normalized,
                    Severity = GetSeverity(normalized, thresholds),
                };
            })
            .OrderByDescending(x => x.Normalized)
            .ThenByDescending(x => (int)x.Severity)
            .ThenBy(x => x.Subject.Path, StringComparer.Ordinal)
            .ThenBy(x => x.Subject.DisplayName, StringComparer.Ordinal)
            .ThenBy(x => x.Subject.Id.Value, StringComparer.Ordinal)
            .ToArray();

        normalizedByEntityId = normalizedRows.ToDictionary(x => x.Subject.Id, x => x.Normalized);

        var rows = normalizedRows
            .Select((row, index) => new HotspotRankingRow(
                row.Subject.Id,
                row.Subject.DisplayName,
                row.Subject.Path,
                row.RawValue,
                row.Normalized,
                row.Severity,
                Rank: index + 1))
            .ToArray();

        return ApplyBudget(rows, effectiveTopN, unbounded);
    }

    private static IReadOnlyList<HotspotCompositeRankingRow> BuildCompositeRows(
        IReadOnlyList<HotspotSubject> subjects,
        IReadOnlyList<HotspotMetricKind> metricKinds,
        IReadOnlyDictionary<HotspotMetricKind, IReadOnlyDictionary<EntityId, double>> normalizedByMetricByEntityId,
        IReadOnlyDictionary<HotspotMetricKind, double> effectiveWeights,
        HotspotSeverityThresholds compositeThresholds,
        int effectiveTopN,
        bool unbounded)
    {
        var scored = new List<(HotspotSubject Subject, double Score, HotspotSeverityBand Severity)>();
        foreach (var subject in subjects)
        {
            var weightedSum = 0d;
            var totalWeight = 0d;
            foreach (var metricKind in metricKinds)
            {
                if (!normalizedByMetricByEntityId.TryGetValue(metricKind, out var normalizedByEntity))
                {
                    continue;
                }

                if (!normalizedByEntity.TryGetValue(subject.Id, out var normalized))
                {
                    continue;
                }

                if (!effectiveWeights.TryGetValue(metricKind, out var weight) || weight <= 0d)
                {
                    continue;
                }

                weightedSum += normalized * weight;
                totalWeight += weight;
            }

            if (totalWeight <= 0d)
            {
                continue;
            }

            var score = weightedSum / totalWeight;
            scored.Add((subject, score, GetSeverity(score, compositeThresholds)));
        }

        var ordered = scored
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => (int)x.Severity)
            .ThenBy(x => x.Subject.Path, StringComparer.Ordinal)
            .ThenBy(x => x.Subject.DisplayName, StringComparer.Ordinal)
            .ThenBy(x => x.Subject.Id.Value, StringComparer.Ordinal)
            .Select((x, index) => new HotspotCompositeRankingRow(
                x.Subject.Id,
                x.Subject.DisplayName,
                x.Subject.Path,
                x.Score,
                x.Severity,
                Rank: index + 1))
            .ToArray();

        return ApplyBudget(ordered, effectiveTopN, unbounded);
    }

    private static IReadOnlyList<T> ApplyBudget<T>(IReadOnlyList<T> rows, int effectiveTopN, bool unbounded)
    {
        return unbounded
            ? rows
            : rows.Take(effectiveTopN).ToArray();
    }

    private static HotspotSeverityBand GetSeverity(double normalizedScore, HotspotSeverityThresholds thresholds)
    {
        if (normalizedScore >= thresholds.Critical)
        {
            return HotspotSeverityBand.Critical;
        }

        if (normalizedScore >= thresholds.High)
        {
            return HotspotSeverityBand.High;
        }

        if (normalizedScore >= thresholds.Medium)
        {
            return HotspotSeverityBand.Medium;
        }

        if (normalizedScore >= thresholds.Low)
        {
            return HotspotSeverityBand.Low;
        }

        return HotspotSeverityBand.None;
    }

    private static double Normalize(double rawValue, double min, double max, MetricDirection direction)
    {
        if (Math.Abs(max - min) < 0.000000001d)
        {
            return Math.Abs(rawValue) < 0.000000001d ? 0d : 1d;
        }

        var normalized = direction == MetricDirection.HigherIsWorse
            ? (rawValue - min) / (max - min)
            : (max - rawValue) / (max - min);

        return Math.Clamp(normalized, 0d, 1d);
    }

    private static MetricDirection GetDirection(HotspotMetricKind metricKind)
    {
        return metricKind switch
        {
            HotspotMetricKind.MethodMaintainabilityIndex => MetricDirection.LowerIsWorse,
            HotspotMetricKind.ScopeAverageMaintainabilityIndex => MetricDirection.LowerIsWorse,
            _ => MetricDirection.HigherIsWorse,
        };
    }

    private static int NormalizeTopN(int? configuredTopN)
    {
        if (!configuredTopN.HasValue || configuredTopN.Value <= 0)
        {
            return 25;
        }

        return configuredTopN.Value;
    }

    private static IReadOnlyDictionary<HotspotMetricKind, double> ResolveWeights(
        IReadOnlyDictionary<HotspotMetricKind, double> overrides)
    {
        var resolved = DefaultCompositeWeights.ToDictionary(x => x.Key, x => x.Value);
        foreach (var item in overrides.OrderBy(x => x.Key))
        {
            resolved[item.Key] = item.Value;
        }

        return resolved;
    }

    private static IReadOnlyDictionary<HotspotMetricKind, HotspotSeverityThresholds> ResolveThresholds(
        IReadOnlyDictionary<HotspotMetricKind, HotspotSeverityThresholds> overrides)
    {
        var resolved = DefaultThresholds.ToDictionary(x => x.Key, x => x.Value);
        foreach (var item in overrides.OrderBy(x => x.Key))
        {
            resolved[item.Key] = item.Value;
        }

        return resolved;
    }

    private static IReadOnlyDictionary<HotspotTargetKind, IReadOnlyList<HotspotSubject>> BuildSubjectsByKind(
        IReadOnlyList<SemanticTriple> triples,
        DeclarationCatalog declarations,
        StructuralMetricRollupCatalog structuralMetrics)
    {
        var methodCoverageById = BuildStringMetricMap(triples, CorePredicates.MetricCoverageStatus);
        var cyclomaticById = BuildIntMetricMap(triples, CorePredicates.CyclomaticComplexity);
        var cognitiveById = BuildIntMetricMap(triples, CorePredicates.CognitiveComplexity);
        var halsteadVolumeById = BuildDoubleMetricMap(triples, CorePredicates.HalsteadVolume);
        var miById = BuildDoubleMetricMap(triples, CorePredicates.MaintainabilityIndex);
        var cboDeclarationByTypeId = BuildIntMetricMap(triples, CorePredicates.CboDeclaration);
        var cboMethodBodyByTypeId = BuildIntMetricMap(triples, CorePredicates.CboMethodBody);
        var cboTotalByTypeId = BuildIntMetricMap(triples, CorePredicates.CboTotal);

        var byKind = new Dictionary<HotspotTargetKind, IReadOnlyList<HotspotSubject>>();

        var methodSubjects = declarations.Methods.Declarations
            .Where(method => methodCoverageById.TryGetValue(method.Id, out var coverage)
                && coverage.Equals("analyzable", StringComparison.OrdinalIgnoreCase))
            .Select(method =>
            {
                var metrics = new Dictionary<HotspotMetricKind, double>();
                if (cyclomaticById.TryGetValue(method.Id, out var cyclomatic))
                {
                    metrics[HotspotMetricKind.MethodCyclomaticComplexity] = cyclomatic;
                }

                if (cognitiveById.TryGetValue(method.Id, out var cognitive))
                {
                    metrics[HotspotMetricKind.MethodCognitiveComplexity] = cognitive;
                }

                if (halsteadVolumeById.TryGetValue(method.Id, out var halsteadVolume))
                {
                    metrics[HotspotMetricKind.MethodHalsteadVolume] = halsteadVolume;
                }

                if (miById.TryGetValue(method.Id, out var maintainabilityIndex))
                {
                    metrics[HotspotMetricKind.MethodMaintainabilityIndex] = maintainabilityIndex;
                }

                return new HotspotSubject(
                    method.Id,
                    method.Signature,
                    method.Signature,
                    metrics);
            })
            .Where(subject => subject.Metrics.Count > 0)
            .OrderBy(subject => subject.Path, StringComparer.Ordinal)
            .ThenBy(subject => subject.Id.Value, StringComparer.Ordinal)
            .ToArray();
        byKind[HotspotTargetKind.Method] = methodSubjects;

        var typeSubjects = declarations.Types
            .Select(type =>
            {
                var metrics = new Dictionary<HotspotMetricKind, double>();
                if (cboDeclarationByTypeId.TryGetValue(type.Id, out var cboDeclaration))
                {
                    metrics[HotspotMetricKind.TypeCboDeclaration] = cboDeclaration;
                }

                if (cboMethodBodyByTypeId.TryGetValue(type.Id, out var cboMethodBody))
                {
                    metrics[HotspotMetricKind.TypeCboMethodBody] = cboMethodBody;
                }

                if (cboTotalByTypeId.TryGetValue(type.Id, out var cboTotal))
                {
                    metrics[HotspotMetricKind.TypeCboTotal] = cboTotal;
                }

                return new HotspotSubject(type.Id, type.DisplayName, type.Path, metrics);
            })
            .Where(subject => subject.Metrics.Count > 0)
            .OrderBy(subject => subject.Path, StringComparer.Ordinal)
            .ThenBy(subject => subject.Id.Value, StringComparer.Ordinal)
            .ToArray();
        byKind[HotspotTargetKind.Type] = typeSubjects;

        byKind[HotspotTargetKind.File] = structuralMetrics.Files
            .Where(file => file.Rollup.IncludedInRanking)
            .Select(file => new HotspotSubject(
                file.FileId,
                file.Name,
                file.Path,
                BuildScopeMetrics(file.Rollup)))
            .Where(subject => subject.Metrics.Count > 0)
            .OrderBy(subject => subject.Path, StringComparer.Ordinal)
            .ThenBy(subject => subject.Id.Value, StringComparer.Ordinal)
            .ToArray();

        byKind[HotspotTargetKind.Namespace] = structuralMetrics.Namespaces
            .Where(@namespace => @namespace.Recursive.IncludedInRanking)
            .Select(@namespace => new HotspotSubject(
                @namespace.NamespaceId,
                @namespace.Name,
                @namespace.Path,
                BuildScopeMetrics(@namespace.Recursive)))
            .Where(subject => subject.Metrics.Count > 0)
            .OrderBy(subject => subject.Path, StringComparer.Ordinal)
            .ThenBy(subject => subject.Id.Value, StringComparer.Ordinal)
            .ToArray();

        byKind[HotspotTargetKind.Project] = structuralMetrics.Projects
            .Where(project => project.Rollup.IncludedInRanking)
            .Select(project => new HotspotSubject(
                project.ProjectId,
                project.Name,
                project.Path,
                BuildScopeMetrics(project.Rollup)))
            .Where(subject => subject.Metrics.Count > 0)
            .OrderBy(subject => subject.Path, StringComparer.Ordinal)
            .ThenBy(subject => subject.Id.Value, StringComparer.Ordinal)
            .ToArray();

        var repositorySubjects = new List<HotspotSubject>();
        if (structuralMetrics.Repository.Rollup.IncludedInRanking)
        {
            var repositoryMetrics = BuildScopeMetrics(structuralMetrics.Repository.Rollup);
            if (repositoryMetrics.Count > 0)
            {
                repositorySubjects.Add(new HotspotSubject(
                    structuralMetrics.Repository.RepositoryId,
                    "Repository",
                    ".",
                    repositoryMetrics));
            }
        }

        byKind[HotspotTargetKind.Repository] = repositorySubjects;

        return byKind;
    }

    private static Dictionary<HotspotMetricKind, double> BuildScopeMetrics(StructuralMetricScopeRollup rollup)
    {
        var metrics = new Dictionary<HotspotMetricKind, double>();
        if (rollup.Metrics.MethodMetricCount > 0)
        {
            metrics[HotspotMetricKind.ScopeAverageCyclomaticComplexity] = rollup.Metrics.AverageCyclomaticComplexity;
            metrics[HotspotMetricKind.ScopeAverageCognitiveComplexity] = rollup.Metrics.AverageCognitiveComplexity;
            metrics[HotspotMetricKind.ScopeAverageMaintainabilityIndex] = rollup.Metrics.AverageMaintainabilityIndex;
        }

        if (rollup.Metrics.TypeMetricCount > 0)
        {
            metrics[HotspotMetricKind.ScopeAverageCboTotal] = rollup.Metrics.AverageCboTotal;
        }

        return metrics;
    }

    private static Dictionary<EntityId, string> BuildStringMetricMap(
        IReadOnlyList<SemanticTriple> triples,
        PredicateId predicate)
    {
        return triples
            .Where(triple => triple.Predicate == predicate)
            .Where(triple => triple.Subject is EntityNode && triple.Object is LiteralNode)
            .Select(triple => (
                SubjectId: ((EntityNode)triple.Subject).Id,
                Value: ((LiteralNode)triple.Object).Value?.ToString() ?? string.Empty))
            .GroupBy(item => item.SubjectId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(x => x.Value).OrderBy(x => x, StringComparer.Ordinal).First());
    }

    private static Dictionary<EntityId, int> BuildIntMetricMap(IReadOnlyList<SemanticTriple> triples, PredicateId predicate)
    {
        return triples
            .Where(triple => triple.Predicate == predicate)
            .Where(triple => triple.Subject is EntityNode && triple.Object is LiteralNode)
            .Select(triple =>
            {
                var subjectId = ((EntityNode)triple.Subject).Id;
                var literalValue = ((LiteralNode)triple.Object).Value?.ToString() ?? string.Empty;
                return (SubjectId: subjectId, Value: int.TryParse(literalValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : (int?)null);
            })
            .Where(item => item.Value.HasValue)
            .GroupBy(item => item.SubjectId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(x => x.Value!.Value).OrderBy(x => x).First());
    }

    private static Dictionary<EntityId, double> BuildDoubleMetricMap(IReadOnlyList<SemanticTriple> triples, PredicateId predicate)
    {
        return triples
            .Where(triple => triple.Predicate == predicate)
            .Where(triple => triple.Subject is EntityNode && triple.Object is LiteralNode)
            .Select(triple =>
            {
                var subjectId = ((EntityNode)triple.Subject).Id;
                var literalValue = ((LiteralNode)triple.Object).Value?.ToString() ?? string.Empty;
                return (SubjectId: subjectId, Value: double.TryParse(literalValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : (double?)null);
            })
            .Where(item => item.Value.HasValue)
            .GroupBy(item => item.SubjectId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(x => x.Value!.Value).OrderBy(x => x).First());
    }

    private sealed record HotspotSubject(
        EntityId Id,
        string DisplayName,
        string Path,
        IReadOnlyDictionary<HotspotMetricKind, double> Metrics);

    private enum MetricDirection
    {
        HigherIsWorse = 0,
        LowerIsWorse = 1,
    }
}
