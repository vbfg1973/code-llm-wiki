using CodeLlmWiki.Cli;
using CodeLlmWiki.Cli.Features.Ingest;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Ontology;

var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
var pipeline = new ProjectStructureIngestionPipeline(analyzer);
var runner = new IngestionRunner(new OntologyLoader(), pipeline);
var artifactPublisher = new IngestionArtifactPublisher();
var app = new CliApplication(runner, artifactPublisher);
var exitCode = await app.RunAsync(args, CancellationToken.None);

Environment.Exit(exitCode);
