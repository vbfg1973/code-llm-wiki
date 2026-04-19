namespace CodeLlmWiki.Cli.Features.Ingest;

public interface IWikiScopedLinkInvariantValidator
{
    WikiScopedLinkInvariantValidationResult Validate(WikiScopedLinkInvariantValidationRequest request);
}
