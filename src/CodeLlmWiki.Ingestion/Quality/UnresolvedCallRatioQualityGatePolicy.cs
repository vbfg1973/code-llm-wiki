namespace CodeLlmWiki.Ingestion.Quality;

public sealed record UnresolvedCallRatioQualityGatePolicy(double Threshold)
{
    public const double DefaultThreshold = 0.25d;

    public static readonly UnresolvedCallRatioQualityGatePolicy Default = new(DefaultThreshold);
}
