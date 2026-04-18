using CodeLlmWiki.Cli;
using CodeLlmWiki.Ingestion;
using CodeLlmWiki.Ontology;

var runner = new IngestionRunner(new OntologyLoader(), new NoOpIngestionPipeline());
var app = new CliApplication(runner);
var exitCode = await app.RunAsync(args, CancellationToken.None);

Environment.Exit(exitCode);
