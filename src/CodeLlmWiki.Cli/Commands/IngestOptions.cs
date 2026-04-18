using CommandLine;

namespace CodeLlmWiki.Cli.Commands;

[Verb("ingest", HelpText = "Ingest repository structure into graph contracts.")]
public sealed class IngestOptions
{
    [Option('p', "path", Required = true, HelpText = "Path to the repository root.")]
    public string RepositoryPath { get; init; } = string.Empty;

    [Option('c', "config", Required = false, HelpText = "Path to JSON config file.")]
    public string? ConfigPath { get; init; }

    [Option('o', "ontology", Required = false, HelpText = "Path to ontology yaml file.")]
    public string? OntologyPath { get; init; }

    [Option('r', "output-root", Required = false, HelpText = "Directory for run-scoped generated artifacts.")]
    public string? OutputRoot { get; init; }

    [Option('a', "allow-partial-success", Required = false, HelpText = "Overrides partial-success exit policy.")]
    public string? AllowPartialSuccess { get; init; }

    [Option('m', "max-merge-entries-per-file", Required = false, HelpText = "Caps merge-to-mainline entries per file in wiki output.")]
    public int? MaxMergeEntriesPerFile { get; init; }
}
