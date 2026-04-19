namespace CodeLlmWiki.Ingestion.Diagnostics;

public static class CallResolutionDiagnosticCodes
{
    public const string Aggregate = "method:call:resolution:failed";
    public const string SymbolUnresolved = "method:call:resolution:failed:symbol-unresolved";
    public const string MissingContainingType = "method:call:resolution:failed:missing-containing-type";
}
