using System.Text.Json;
using System.Text.Json.Serialization;
using CodeLlmWiki.Contracts.Graph;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion;
using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;

namespace CodeLlmWiki.Cli.Features.Ingest;

public sealed class IngestionArtifactPublisher : IIngestionArtifactPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<IngestionArtifactPublishResult> PublishAsync(
        IngestionArtifactPublishRequest request,
        CancellationToken cancellationToken)
    {
        var runId = request.CompletedAtUtc.UtcDateTime.ToString("yyyyMMddTHHmmssfffZ");
        var outputRoot = Path.GetFullPath(request.OutputRootPath);
        var runsRoot = Path.Combine(outputRoot, "runs");
        var runDirectory = Path.Combine(runsRoot, runId);
        var manifestPath = Path.Combine(runDirectory, "manifest.json");

        Directory.CreateDirectory(runDirectory);

        string? wikiDirectory = null;
        string? graphMlPath = null;
        var wikiPageCount = 0;
        var graphNodeCount = 0;
        var graphEdgeCount = 0;

        try
        {
            if (request.RunResult.RepositoryId != default && request.RunResult.Triples.Count > 0)
            {
                var query = new ProjectStructureQueryService(request.RunResult.Triples);
                var model = query.GetModel(request.RunResult.RepositoryId);
                var renderer = new ProjectStructureWikiRenderer();
                var pages = renderer.Render(model)
                    .OrderBy(x => x.RelativePath, StringComparer.Ordinal)
                    .ToArray();

                wikiPageCount = pages.Length;
                wikiDirectory = Path.Combine(runDirectory, "wiki");
                WriteWikiPages(wikiDirectory, pages);
            }

            var graph = GraphMlSerializer.Serialize(request.RunResult.Triples);
            graphNodeCount = graph.NodeCount;
            graphEdgeCount = graph.EdgeCount;

            graphMlPath = Path.Combine(runDirectory, "graph", "graph.graphml");
            Directory.CreateDirectory(Path.GetDirectoryName(graphMlPath)!);
            await File.WriteAllTextAsync(graphMlPath, graph.GraphMl, cancellationToken);

            var shouldPromoteLatest = request.RunResult.Status != IngestionRunStatus.Failed;
            var manifest = BuildManifest(
                request,
                runId,
                latestPromoted: false,
                wikiPageCount,
                graphNodeCount,
                graphEdgeCount,
                wikiDirectory,
                graphMlPath);

            await WriteManifestAsync(manifestPath, manifest, cancellationToken);

            var latestPromoted = false;
            if (shouldPromoteLatest)
            {
                PromoteLatest(outputRoot, runDirectory);
                latestPromoted = true;

                manifest = manifest with { LatestPromoted = true };
                await WriteManifestAsync(manifestPath, manifest, cancellationToken);
                var latestManifestPath = Path.Combine(outputRoot, "latest", "manifest.json");
                await WriteManifestAsync(latestManifestPath, manifest, cancellationToken);
            }

            return new IngestionArtifactPublishResult(
                Succeeded: true,
                LatestPromoted: latestPromoted,
                RunId: runId,
                RunDirectory: runDirectory,
                ManifestPath: manifestPath,
                WikiDirectory: wikiDirectory,
                GraphMlPath: graphMlPath,
                FailureReason: null);
        }
        catch (Exception ex)
        {
            return new IngestionArtifactPublishResult(
                Succeeded: false,
                LatestPromoted: false,
                RunId: runId,
                RunDirectory: runDirectory,
                ManifestPath: manifestPath,
                WikiDirectory: wikiDirectory,
                GraphMlPath: graphMlPath,
                FailureReason: ex.Message);
        }
    }

    private static RunManifest BuildManifest(
        IngestionArtifactPublishRequest request,
        string runId,
        bool latestPromoted,
        int wikiPageCount,
        int graphNodeCount,
        int graphEdgeCount,
        string? wikiDirectory,
        string? graphMlPath)
    {
        var duration = request.CompletedAtUtc - request.StartedAtUtc;

        var diagnosticsSummary = request.RunResult.Diagnostics
            .GroupBy(x => x.Code, StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new DiagnosticSummary(x.Key, x.Count()))
            .ToArray();

        var headBranch = GetRepositoryBranch(request.RunResult.Triples, request.RunResult.RepositoryId, CorePredicates.HeadBranch);
        var mainlineBranch = GetRepositoryBranch(request.RunResult.Triples, request.RunResult.RepositoryId, CorePredicates.MainlineBranch);

        return new RunManifest(
            RunId: runId,
            Status: request.RunResult.Status.ToString(),
            ExitCode: request.RunResult.ExitCode,
            StartedAtUtc: request.StartedAtUtc,
            CompletedAtUtc: request.CompletedAtUtc,
            DurationMs: Math.Max(0, (long)duration.TotalMilliseconds),
            RepositoryPath: Path.GetFullPath(request.RepositoryPath),
            HeadBranch: headBranch,
            MainlineBranch: mainlineBranch,
            TripleCount: request.RunResult.Triples.Count,
            DiagnosticCount: request.RunResult.Diagnostics.Count,
            WikiPageCount: wikiPageCount,
            GraphMlNodeCount: graphNodeCount,
            GraphMlEdgeCount: graphEdgeCount,
            DiagnosticsSummary: diagnosticsSummary,
            Artifacts: new ArtifactReferences(
                Wiki: wikiDirectory is null ? null : "wiki",
                GraphMl: graphMlPath is null ? null : "graph/graph.graphml",
                Manifest: "manifest.json"),
            LatestPromoted: latestPromoted);
    }

    private static string GetRepositoryBranch(
        IReadOnlyList<SemanticTriple> triples,
        EntityId repositoryId,
        PredicateId predicate)
    {
        var value = triples
            .Where(x => x.Predicate == predicate)
            .Where(x => x.Subject is EntityNode entity && entity.Id == repositoryId)
            .Select(x => x.Object as LiteralNode)
            .Where(x => x is not null)
            .Select(x => x!.Value?.ToString())
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
    }

    private static async Task WriteManifestAsync(
        string manifestPath,
        RunManifest manifest,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        await File.WriteAllTextAsync(manifestPath, json + Environment.NewLine, cancellationToken);
    }

    private static void WriteWikiPages(string wikiRoot, IReadOnlyList<WikiPage> pages)
    {
        Directory.CreateDirectory(wikiRoot);

        foreach (var page in pages)
        {
            var fullPath = Path.Combine(wikiRoot, page.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var parent = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.WriteAllText(fullPath, page.Markdown + Environment.NewLine);
        }
    }

    private static void PromoteLatest(string outputRoot, string runDirectory)
    {
        var latestPath = Path.Combine(outputRoot, "latest");
        var stagingPath = Path.Combine(outputRoot, $".latest-staging-{Guid.NewGuid():N}");
        var backupPath = Path.Combine(outputRoot, $".latest-backup-{Guid.NewGuid():N}");

        CopyDirectory(runDirectory, stagingPath);

        var hadLatest = Directory.Exists(latestPath);

        if (hadLatest)
        {
            Directory.Move(latestPath, backupPath);
        }

        try
        {
            Directory.Move(stagingPath, latestPath);
            if (hadLatest && Directory.Exists(backupPath))
            {
                Directory.Delete(backupPath, recursive: true);
            }
        }
        catch
        {
            if (Directory.Exists(stagingPath))
            {
                Directory.Delete(stagingPath, recursive: true);
            }

            if (hadLatest && Directory.Exists(backupPath) && !Directory.Exists(latestPath))
            {
                Directory.Move(backupPath, latestPath);
            }

            throw;
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var targetFile = Path.Combine(destination, relative);
            var targetDirectory = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            File.Copy(file, targetFile, overwrite: true);
        }
    }

    private sealed record RunManifest(
        string RunId,
        string Status,
        int ExitCode,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset CompletedAtUtc,
        long DurationMs,
        string RepositoryPath,
        string HeadBranch,
        string MainlineBranch,
        int TripleCount,
        int DiagnosticCount,
        int WikiPageCount,
        int GraphMlNodeCount,
        int GraphMlEdgeCount,
        IReadOnlyList<DiagnosticSummary> DiagnosticsSummary,
        ArtifactReferences Artifacts,
        bool LatestPromoted);

    private sealed record ArtifactReferences(string? Wiki, string? GraphMl, string Manifest);

    private sealed record DiagnosticSummary(string Code, int Count);
}
