namespace CodeLlmWiki.Query.ProjectStructure;

public static class DeclarationIdentityRules
{
    public static string CreateNamespaceNaturalKey(string namespaceName)
    {
        var normalized = Normalize(namespaceName);
        return string.IsNullOrEmpty(normalized)
            ? "namespace::<global>"
            : $"namespace::{normalized}";
    }

    public static string CreateTypeNaturalKey(string assemblyName, string namespaceName, string typeSignature)
    {
        return $"type::{Normalize(assemblyName)}::{Normalize(namespaceName)}::{Normalize(typeSignature)}";
    }

    public static string CreateMemberNaturalKey(string typeNaturalKey, MemberDeclarationKind memberKind, string memberSignature)
    {
        return $"member::{Normalize(typeNaturalKey)}::{memberKind.ToString().ToLowerInvariant()}::{Normalize(memberSignature)}";
    }

    public static string CreateMethodNaturalKey(
        string assemblyName,
        string declaringTypeNaturalKey,
        string methodName,
        IReadOnlyList<string> orderedParameterTypeSignatures,
        int genericArity)
    {
        var normalizedParameters = orderedParameterTypeSignatures
            .Select(Normalize)
            .ToArray();

        return $"method::{Normalize(assemblyName)}::{Normalize(declaringTypeNaturalKey)}::{Normalize(methodName)}::{genericArity}::{string.Join("|", normalizedParameters)}";
    }

    private static string Normalize(string value)
    {
        return (value ?? string.Empty).Trim();
    }
}
