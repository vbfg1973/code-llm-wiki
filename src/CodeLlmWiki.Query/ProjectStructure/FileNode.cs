using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record FileNode(
    EntityId Id,
    string Name,
    string Path,
    string Classification,
    bool IsSolutionMember,
    int EditCount,
    FileHistoryEntryNode? LastChange,
    IReadOnlyList<FileHistoryEntryNode> History,
    IReadOnlyList<FileMergeEventNode> MergeToMainlineEvents);
