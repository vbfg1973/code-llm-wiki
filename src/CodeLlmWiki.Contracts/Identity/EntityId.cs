namespace CodeLlmWiki.Contracts.Identity;

public readonly record struct EntityId(string Value)
{
    public override string ToString() => Value;
}
