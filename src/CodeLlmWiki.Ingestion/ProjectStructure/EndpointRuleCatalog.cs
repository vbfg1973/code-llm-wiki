namespace CodeLlmWiki.Ingestion.ProjectStructure;

public sealed class EndpointRuleCatalog
{
    public EndpointRuleCatalog(
        string catalogVersion,
        IReadOnlyList<HandlerInterfaceRule> messageHandlerInterfaceRules,
        string cliVerbAttributeNamespace,
        string cliVerbAttributeTypeName,
        string cliVerbAttributeAssemblyName)
    {
        if (string.IsNullOrWhiteSpace(catalogVersion))
        {
            throw new ArgumentException("Catalog version is required.", nameof(catalogVersion));
        }

        CatalogVersion = catalogVersion;
        MessageHandlerInterfaceRules = messageHandlerInterfaceRules ?? throw new ArgumentNullException(nameof(messageHandlerInterfaceRules));
        CliVerbAttributeNamespace = cliVerbAttributeNamespace ?? throw new ArgumentNullException(nameof(cliVerbAttributeNamespace));
        CliVerbAttributeTypeName = cliVerbAttributeTypeName ?? throw new ArgumentNullException(nameof(cliVerbAttributeTypeName));
        CliVerbAttributeAssemblyName = cliVerbAttributeAssemblyName ?? throw new ArgumentNullException(nameof(cliVerbAttributeAssemblyName));
    }

    public static EndpointRuleCatalog Default { get; } = new(
        catalogVersion: "1",
        messageHandlerInterfaceRules:
        [
            new HandlerInterfaceRule(
                RuleId: "dotnet.message-handler.interface-pattern",
                RuleVersion: "1",
                RuleSource: "catalog:default",
                MatchKind: HandlerInterfaceMatchKind.PrefixAndSuffix,
                MatchName: "I*Handler",
                Prefix: "I",
                Suffix: "Handler"),
            new HandlerInterfaceRule(
                RuleId: "dotnet.message-handler.interface-pattern",
                RuleVersion: "1",
                RuleSource: "catalog:default",
                MatchKind: HandlerInterfaceMatchKind.ExactName,
                MatchName: "IConsumer"),
        ],
        cliVerbAttributeNamespace: "CommandLine",
        cliVerbAttributeTypeName: "VerbAttribute",
        cliVerbAttributeAssemblyName: "CommandLine");

    public string CatalogVersion { get; }

    public IReadOnlyList<HandlerInterfaceRule> MessageHandlerInterfaceRules { get; }

    public string CliVerbAttributeNamespace { get; }

    public string CliVerbAttributeTypeName { get; }

    public string CliVerbAttributeAssemblyName { get; }

    public bool TryMatchHandlerInterface(string interfaceName, out HandlerInterfaceRule? matchedRule)
    {
        foreach (var rule in MessageHandlerInterfaceRules)
        {
            if (rule.IsMatch(interfaceName))
            {
                matchedRule = rule;
                return true;
            }
        }

        matchedRule = null;
        return false;
    }
}
