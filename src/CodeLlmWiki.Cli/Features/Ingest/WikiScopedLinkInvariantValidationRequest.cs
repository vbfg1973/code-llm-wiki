using CodeLlmWiki.Query.ProjectStructure;
using CodeLlmWiki.Wiki.ProjectStructure;

namespace CodeLlmWiki.Cli.Features.Ingest;

public sealed record WikiScopedLinkInvariantValidationRequest(
    ProjectStructureWikiModel Model,
    IReadOnlyList<WikiPage> Pages);
