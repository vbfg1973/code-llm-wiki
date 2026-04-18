namespace CodeLlmWiki.Contracts.Identity;

public interface IStableIdGenerator
{
    EntityId Create(EntityKey key);
}
