namespace CodeLlmWiki.Query.ProjectStructure;

public interface IStructuralMetricRollupProjector
{
    StructuralMetricRollupCatalog Project(StructuralMetricRollupProjectionRequest request);
}
