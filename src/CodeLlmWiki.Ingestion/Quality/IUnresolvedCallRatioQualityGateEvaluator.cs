namespace CodeLlmWiki.Ingestion.Quality;

public interface IUnresolvedCallRatioQualityGateEvaluator
{
    UnresolvedCallRatioQualityGateEvaluation Evaluate(UnresolvedCallRatioQualityGateRequest request);
}
