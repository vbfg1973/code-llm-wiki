namespace CodeLlmWiki.Ingestion.Diagnostics;

public sealed record CallResolutionFailureContext(
    CallResolutionFailureCause Cause,
    string SourceMethodId,
    string InvocationText);
