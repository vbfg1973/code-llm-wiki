using System.Security.Cryptography;
using System.Text;

namespace CodeLlmWiki.Contracts.Identity;

public sealed class StableIdGenerator : IStableIdGenerator
{
    public EntityId Create(EntityKey key)
    {
        var raw = $"{key.EntityType}:{key.NaturalKey}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        var id = Convert.ToHexString(bytes).ToLowerInvariant();
        return new EntityId($"{key.EntityType}:{id}");
    }
}
