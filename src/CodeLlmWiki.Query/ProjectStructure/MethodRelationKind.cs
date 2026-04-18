namespace CodeLlmWiki.Query.ProjectStructure;

public enum MethodRelationKind
{
    Unknown = 0,
    ImplementsMethod = 1,
    OverridesMethod = 2,
    Calls = 3,
    ReadsProperty = 4,
    WritesProperty = 5,
    ReadsField = 6,
    WritesField = 7,
}
