namespace CodeLlmWiki.Query.ProjectStructure;

public sealed record FileHistoryEntryNode(
    string CommitSha,
    string TimestampUtc,
    string AuthorName,
    string AuthorEmail);
