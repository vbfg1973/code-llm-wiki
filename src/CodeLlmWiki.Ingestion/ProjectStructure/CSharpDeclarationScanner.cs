using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeLlmWiki.Ingestion.ProjectStructure;

internal static class CSharpDeclarationScanner
{
    public static NamespaceDiscoveryResult Discover(
        string repositoryRoot,
        IReadOnlyList<string> relativeSourcePaths,
        CancellationToken cancellationToken)
    {
        var declaredNamespaceFiles = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var discoveredTypes = new List<TypeDiscoveryNode>();

        foreach (var relativePath in relativeSourcePaths)
        {
            var fullPath = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            var source = File.ReadAllText(fullPath);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, path: relativePath, cancellationToken: cancellationToken);
            var root = syntaxTree.GetRoot(cancellationToken) as CompilationUnitSyntax;
            if (root is null)
            {
                continue;
            }

            VisitMembers(root.Members, currentNamespace: null, relativePath, declaredNamespaceFiles, discoveredTypes);
        }

        var allNamespaceNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var namespaceName in declaredNamespaceFiles.Keys)
        {
            AddNamespaceAndParents(allNamespaceNames, namespaceName);
        }

        var namespaceNodes = allNamespaceNames
            .Select(name => new NamespaceDiscoveryNode(
                name,
                GetParentNamespace(name),
                declaredNamespaceFiles.TryGetValue(name, out var files)
                    ? files.OrderBy(x => x, StringComparer.Ordinal).ToArray()
                    : []))
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ToArray();

        var typeNodes = discoveredTypes
            .OrderBy(x => x.NamespaceName, StringComparer.Ordinal)
            .ThenBy(x => x.TypeName, StringComparer.Ordinal)
            .ThenBy(x => x.RelativeFilePath, StringComparer.Ordinal)
            .ToArray();

        return new NamespaceDiscoveryResult(namespaceNodes, typeNodes);
    }

    private static void VisitMembers(
        SyntaxList<MemberDeclarationSyntax> members,
        string? currentNamespace,
        string relativePath,
        Dictionary<string, HashSet<string>> declaredNamespaceFiles,
        List<TypeDiscoveryNode> discoveredTypes)
    {
        foreach (var member in members)
        {
            if (member is BaseNamespaceDeclarationSyntax namespaceDeclaration)
            {
                var declaredName = namespaceDeclaration.Name.ToString().Trim();
                if (string.IsNullOrWhiteSpace(declaredName))
                {
                    continue;
                }

                var namespaceName = CombineNamespace(currentNamespace, declaredName);
                RegisterDeclarationFile(declaredNamespaceFiles, namespaceName, relativePath);

                VisitMembers(namespaceDeclaration.Members, namespaceName, relativePath, declaredNamespaceFiles, discoveredTypes);
                continue;
            }

            if (string.IsNullOrWhiteSpace(currentNamespace))
            {
                continue;
            }

            switch (member)
            {
                case ClassDeclarationSyntax classDeclaration:
                    discoveredTypes.Add(new TypeDiscoveryNode(currentNamespace, classDeclaration.Identifier.Text, "class", relativePath));
                    break;
                case InterfaceDeclarationSyntax interfaceDeclaration:
                    discoveredTypes.Add(new TypeDiscoveryNode(currentNamespace, interfaceDeclaration.Identifier.Text, "interface", relativePath));
                    break;
                case StructDeclarationSyntax structDeclaration:
                    discoveredTypes.Add(new TypeDiscoveryNode(currentNamespace, structDeclaration.Identifier.Text, "struct", relativePath));
                    break;
                case EnumDeclarationSyntax enumDeclaration:
                    discoveredTypes.Add(new TypeDiscoveryNode(currentNamespace, enumDeclaration.Identifier.Text, "enum", relativePath));
                    break;
                case RecordDeclarationSyntax recordDeclaration:
                    discoveredTypes.Add(new TypeDiscoveryNode(currentNamespace, recordDeclaration.Identifier.Text, "record", relativePath));
                    break;
                case DelegateDeclarationSyntax delegateDeclaration:
                    discoveredTypes.Add(new TypeDiscoveryNode(currentNamespace, delegateDeclaration.Identifier.Text, "delegate", relativePath));
                    break;
            }
        }
    }

    private static void RegisterDeclarationFile(
        Dictionary<string, HashSet<string>> declaredNamespaceFiles,
        string namespaceName,
        string relativePath)
    {
        if (!declaredNamespaceFiles.TryGetValue(namespaceName, out var files))
        {
            files = new HashSet<string>(StringComparer.Ordinal);
            declaredNamespaceFiles[namespaceName] = files;
        }

        files.Add(relativePath);
    }

    private static void AddNamespaceAndParents(HashSet<string> names, string namespaceName)
    {
        var parts = namespaceName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return;
        }

        for (var i = 1; i <= parts.Length; i++)
        {
            names.Add(string.Join('.', parts.Take(i)));
        }
    }

    private static string? GetParentNamespace(string namespaceName)
    {
        var index = namespaceName.LastIndexOf('.');
        return index > 0
            ? namespaceName[..index]
            : null;
    }

    private static string CombineNamespace(string? currentNamespace, string declaredName)
    {
        if (string.IsNullOrWhiteSpace(currentNamespace))
        {
            return declaredName;
        }

        return declaredName.StartsWith(currentNamespace + ".", StringComparison.Ordinal)
            ? declaredName
            : $"{currentNamespace}.{declaredName}";
    }
}
