using Microsoft.CodeAnalysis;

namespace CodeLlmWiki.Ingestion.ProjectStructure.CallResolution;

public sealed record ProjectScopedCompilationRequest(
    string RepositoryRoot,
    IReadOnlyList<string> SourceFiles,
    IReadOnlyDictionary<string, string> SourceTextByRelativePath,
    IReadOnlyDictionary<string, string> ProjectAssemblyNameByPath,
    IReadOnlyList<string> ProjectPaths,
    IReadOnlyList<MetadataReference> References);
