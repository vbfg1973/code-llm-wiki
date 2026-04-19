using CodeLlmWiki.Contracts.Graph;

namespace CodeLlmWiki.Query.ProjectStructure;

public interface IHotspotRankingProjector
{
    HotspotRankingCatalog Project(HotspotRankingProjectionRequest request);
}
