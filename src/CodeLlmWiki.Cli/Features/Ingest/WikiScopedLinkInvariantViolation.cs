namespace CodeLlmWiki.Cli.Features.Ingest;

public sealed record WikiScopedLinkInvariantViolation(
    string PageRelativePath,
    string SectionPath,
    int LineNumber,
    string LineText,
    string Message);
