using Microsoft.CodeAnalysis;

namespace CodeLlmWiki.Ingestion.ProjectStructure.CallResolution;

public interface IProjectScopedSemanticContext
{
    bool TryGetSemanticModel(string relativeSourceFilePath, out SemanticModel semanticModel, out SemanticContextInfo contextInfo);
}
