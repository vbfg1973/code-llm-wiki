namespace CodeLlmWiki.Ingestion.Diagnostics;

public static class CallResolutionDiagnosticCodes
{
    public const string Aggregate = "method:call:resolution:failed";
    public const string SymbolUnresolved = "method:call:resolution:failed:symbol-unresolved";
    public const string MissingContainingType = "method:call:resolution:failed:missing-containing-type";
    public const string InternalTargetUnmatched = "method:call:internal-target-unmatched";

    public static readonly IReadOnlyList<string> All =
    [
        Aggregate,
        SymbolUnresolved,
        MissingContainingType,
        InternalTargetUnmatched,
    ];
}
