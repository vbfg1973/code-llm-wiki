namespace CodeLlmWiki.Contracts.Graph;

public readonly record struct PredicateId(string Value)
{
    public override string ToString() => Value;
}
