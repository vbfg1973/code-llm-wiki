using CodeLlmWiki.Contracts.Graph;
using CodeLlmWiki.Contracts.Identity;

namespace CodeLlmWiki.Ingestion.Tests;

public sealed class ContractsUsageTests
{
    [Fact]
    public void StableIdGenerator_IsDeterministicForSameNaturalKey()
    {
        var generator = new StableIdGenerator();
        var key = new EntityKey("project", "src/MyProject/MyProject.csproj");

        var first = generator.Create(key);
        var second = generator.Create(key);

        Assert.Equal(first, second);
    }

    [Fact]
    public void SemanticTriple_SupportsEntityAndLiteralNodes()
    {
        var subject = new EntityNode(new EntityId("ent:class-a"));
        var predicate = new PredicateId("core:contains");
        var obj = new LiteralNode("ClassA");

        var triple = new SemanticTriple(subject, predicate, obj);

        Assert.Equal(subject, triple.Subject);
        Assert.Equal(predicate, triple.Predicate);
        Assert.Equal(obj, triple.Object);
    }
}
