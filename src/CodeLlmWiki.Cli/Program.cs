using CodeLlmWiki.Cli;
using CodeLlmWiki.Contracts.Identity;
using CodeLlmWiki.Ingestion;
using CodeLlmWiki.Ingestion.ProjectStructure;
using CodeLlmWiki.Ontology;

var analyzer = new ProjectStructureAnalyzer(new StableIdGenerator());
var pipeline = new ProjectStructureIngestionPipeline(analyzer);
var runner = new IngestionRunner(new OntologyLoader(), pipeline);
var app = new CliApplication(runner);
var exitCode = await app.RunAsync(args, CancellationToken.None);

Environment.Exit(exitCode);
