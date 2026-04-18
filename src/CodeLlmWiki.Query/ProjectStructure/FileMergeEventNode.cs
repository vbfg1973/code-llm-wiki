namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record FileMergeEventNode(
    string MergeCommitSha,
    string TimestampUtc,
    string AuthorName,
    string AuthorEmail,
    string TargetBranch,
    int SourceBranchFileCommitCount);
