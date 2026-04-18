using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeLlmWiki.Ingestion.ProjectStructure;

internal static class CSharpDeclarationScanner
{
    private const string GlobalNamespaceName = "<global>";

    public static NamespaceDiscoveryResult Discover(
        string repositoryRoot,
        IReadOnlyList<string> relativeSourcePaths,
        CancellationToken cancellationToken)
    {
        var declaredNamespaceFiles = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var declaredNamespaceLocations = new Dictionary<string, HashSet<DeclarationSourceLocation>>();
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

            var rootImportContext = BuildImportContext(root.Usings);

            VisitMembers(
                root.Members,
                currentNamespace: null,
                currentDeclaringTypeQualifiedName: null,
                relativePath,
                rootImportContext,
                declaredNamespaceFiles,
                declaredNamespaceLocations,
                discoveredTypes);
        }

        var allNamespaceNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var namespaceName in declaredNamespaceFiles.Keys)
        {
            AddNamespaceAndParents(allNamespaceNames, namespaceName);
        }

        foreach (var type in discoveredTypes.Where(x => x.NamespaceName == GlobalNamespaceName))
        {
            allNamespaceNames.Add(GlobalNamespaceName);
        }

        var namespaceNodes = allNamespaceNames
            .Select(name => new NamespaceDiscoveryNode(
                name,
                GetParentNamespace(name),
                declaredNamespaceFiles.TryGetValue(name, out var files)
                    ? files.OrderBy(x => x, StringComparer.Ordinal).ToArray()
                    : [],
                declaredNamespaceLocations.TryGetValue(name, out var locations)
                    ? locations
                        .OrderBy(x => x.RelativeFilePath, StringComparer.Ordinal)
                        .ThenBy(x => x.Line)
                        .ThenBy(x => x.Column)
                        .ToArray()
                    : []))
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ToArray();

        var typeNodes = discoveredTypes
            .OrderBy(x => x.NamespaceName, StringComparer.Ordinal)
            .ThenBy(x => x.QualifiedName, StringComparer.Ordinal)
            .ThenBy(x => x.RelativeFilePath, StringComparer.Ordinal)
            .ToArray();

        return new NamespaceDiscoveryResult(namespaceNodes, typeNodes);
    }

    private static void VisitMembers(
        SyntaxList<MemberDeclarationSyntax> members,
        string? currentNamespace,
        string? currentDeclaringTypeQualifiedName,
        string relativePath,
        TypeImportContext importContext,
        Dictionary<string, HashSet<string>> declaredNamespaceFiles,
        Dictionary<string, HashSet<DeclarationSourceLocation>> declaredNamespaceLocations,
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
                RegisterDeclarationLocation(
                    declaredNamespaceLocations,
                    namespaceName,
                    ToSourceLocation(namespaceDeclaration.Name.GetLocation(), relativePath));

                var namespaceImportContext = MergeImportContexts(importContext, BuildImportContext(namespaceDeclaration.Usings));

                VisitMembers(
                    namespaceDeclaration.Members,
                    namespaceName,
                    currentDeclaringTypeQualifiedName: null,
                    relativePath,
                    namespaceImportContext,
                    declaredNamespaceFiles,
                    declaredNamespaceLocations,
                    discoveredTypes);
                continue;
            }

            AddTypeIfPresent(
                member,
                currentNamespace,
                currentDeclaringTypeQualifiedName,
                relativePath,
                importContext,
                declaredNamespaceFiles,
                declaredNamespaceLocations,
                discoveredTypes);
        }
    }

    private static void AddTypeIfPresent(
        MemberDeclarationSyntax member,
        string? currentNamespace,
        string? currentDeclaringTypeQualifiedName,
        string relativePath,
        TypeImportContext importContext,
        Dictionary<string, HashSet<string>> declaredNamespaceFiles,
        Dictionary<string, HashSet<DeclarationSourceLocation>> declaredNamespaceLocations,
        List<TypeDiscoveryNode> discoveredTypes)
    {
        var namespaceName = ResolveNamespace(currentNamespace);

        switch (member)
        {
            case ClassDeclarationSyntax classDeclaration:
                AddType(
                    namespaceName,
                    currentDeclaringTypeQualifiedName,
                    classDeclaration.Identifier.Text,
                    classDeclaration.TypeParameterList?.Parameters.Count ?? 0,
                    "class",
                    ParseAccessibility(classDeclaration.Modifiers),
                    IsPartial(classDeclaration.Modifiers),
                    ParseGenericParameters(classDeclaration.TypeParameterList),
                    ParseGenericConstraints(classDeclaration.ConstraintClauses),
                    ParseDirectRelationships(classDeclaration),
                    ParseDeclaredMembers(classDeclaration.Members, relativePath),
                    relativePath,
                    ToSourceLocation(classDeclaration.Identifier.GetLocation(), relativePath),
                    classDeclaration.Members,
                    importContext,
                    declaredNamespaceFiles,
                    declaredNamespaceLocations,
                    discoveredTypes);
                break;
            case InterfaceDeclarationSyntax interfaceDeclaration:
                AddType(
                    namespaceName,
                    currentDeclaringTypeQualifiedName,
                    interfaceDeclaration.Identifier.Text,
                    interfaceDeclaration.TypeParameterList?.Parameters.Count ?? 0,
                    "interface",
                    ParseAccessibility(interfaceDeclaration.Modifiers),
                    IsPartial(interfaceDeclaration.Modifiers),
                    ParseGenericParameters(interfaceDeclaration.TypeParameterList),
                    ParseGenericConstraints(interfaceDeclaration.ConstraintClauses),
                    ParseDirectRelationships(interfaceDeclaration),
                    ParseDeclaredMembers(interfaceDeclaration.Members, relativePath),
                    relativePath,
                    ToSourceLocation(interfaceDeclaration.Identifier.GetLocation(), relativePath),
                    interfaceDeclaration.Members,
                    importContext,
                    declaredNamespaceFiles,
                    declaredNamespaceLocations,
                    discoveredTypes);
                break;
            case StructDeclarationSyntax structDeclaration:
                AddType(
                    namespaceName,
                    currentDeclaringTypeQualifiedName,
                    structDeclaration.Identifier.Text,
                    structDeclaration.TypeParameterList?.Parameters.Count ?? 0,
                    "struct",
                    ParseAccessibility(structDeclaration.Modifiers),
                    IsPartial(structDeclaration.Modifiers),
                    ParseGenericParameters(structDeclaration.TypeParameterList),
                    ParseGenericConstraints(structDeclaration.ConstraintClauses),
                    ParseDirectRelationships(structDeclaration),
                    ParseDeclaredMembers(structDeclaration.Members, relativePath),
                    relativePath,
                    ToSourceLocation(structDeclaration.Identifier.GetLocation(), relativePath),
                    structDeclaration.Members,
                    importContext,
                    declaredNamespaceFiles,
                    declaredNamespaceLocations,
                    discoveredTypes);
                break;
            case RecordDeclarationSyntax recordDeclaration:
                AddType(
                    namespaceName,
                    currentDeclaringTypeQualifiedName,
                    recordDeclaration.Identifier.Text,
                    recordDeclaration.TypeParameterList?.Parameters.Count ?? 0,
                    "record",
                    ParseAccessibility(recordDeclaration.Modifiers),
                    IsPartial(recordDeclaration.Modifiers),
                    ParseGenericParameters(recordDeclaration.TypeParameterList),
                    ParseGenericConstraints(recordDeclaration.ConstraintClauses),
                    ParseDirectRelationships(recordDeclaration),
                    ParseDeclaredMembers(recordDeclaration.Members, relativePath)
                        .Concat(ParseRecordPrimaryMembers(recordDeclaration, relativePath))
                        .OrderBy(x => x.Kind, StringComparer.Ordinal)
                        .ThenBy(x => x.Name, StringComparer.Ordinal)
                        .ToArray(),
                    relativePath,
                    ToSourceLocation(recordDeclaration.Identifier.GetLocation(), relativePath),
                    recordDeclaration.Members,
                    importContext,
                    declaredNamespaceFiles,
                    declaredNamespaceLocations,
                    discoveredTypes);
                break;
            case EnumDeclarationSyntax enumDeclaration:
                AddType(
                    namespaceName,
                    currentDeclaringTypeQualifiedName,
                    enumDeclaration.Identifier.Text,
                    0,
                    "enum",
                    ParseAccessibility(enumDeclaration.Modifiers),
                    false,
                    [],
                    [],
                    ([], []),
                    ParseEnumMembers(enumDeclaration, relativePath),
                    relativePath,
                    ToSourceLocation(enumDeclaration.Identifier.GetLocation(), relativePath),
                    members: default,
                    importContext,
                    declaredNamespaceFiles,
                    declaredNamespaceLocations,
                    discoveredTypes);
                break;
            case DelegateDeclarationSyntax delegateDeclaration:
                AddType(
                    namespaceName,
                    currentDeclaringTypeQualifiedName,
                    delegateDeclaration.Identifier.Text,
                    delegateDeclaration.TypeParameterList?.Parameters.Count ?? 0,
                    "delegate",
                    ParseAccessibility(delegateDeclaration.Modifiers),
                    false,
                    ParseGenericParameters(delegateDeclaration.TypeParameterList),
                    ParseGenericConstraints(delegateDeclaration.ConstraintClauses),
                    ([], []),
                    [],
                    relativePath,
                    ToSourceLocation(delegateDeclaration.Identifier.GetLocation(), relativePath),
                    members: default,
                    importContext,
                    declaredNamespaceFiles,
                    declaredNamespaceLocations,
                    discoveredTypes);
                break;
        }
    }

    private static void AddType(
        string namespaceName,
        string? currentDeclaringTypeQualifiedName,
        string typeName,
        int arity,
        string kind,
        string accessibility,
        bool isPartialDeclaration,
        IReadOnlyList<string> genericParameters,
        IReadOnlyList<string> genericConstraints,
        (IReadOnlyList<string> Bases, IReadOnlyList<string> Interfaces) relationships,
        IReadOnlyList<MemberDiscoveryNode> discoveredMembers,
        string relativePath,
        DeclarationSourceLocation sourceLocation,
        SyntaxList<MemberDeclarationSyntax> members,
        TypeImportContext importContext,
        Dictionary<string, HashSet<string>> declaredNamespaceFiles,
        Dictionary<string, HashSet<DeclarationSourceLocation>> declaredNamespaceLocations,
        List<TypeDiscoveryNode> discoveredTypes)
    {
        var qualifiedName = BuildQualifiedTypeName(namespaceName, currentDeclaringTypeQualifiedName, typeName, arity);

        discoveredTypes.Add(new TypeDiscoveryNode(
            namespaceName,
            qualifiedName,
            typeName,
            kind,
            accessibility,
            isPartialDeclaration,
            arity,
            genericParameters,
            genericConstraints,
            currentDeclaringTypeQualifiedName,
            relationships.Bases,
            relationships.Interfaces,
            importContext.Namespaces,
            importContext.Aliases,
            discoveredMembers,
            relativePath,
            sourceLocation.Line,
            sourceLocation.Column));

        if (namespaceName == GlobalNamespaceName)
        {
            RegisterDeclarationFile(declaredNamespaceFiles, GlobalNamespaceName, relativePath);
            RegisterDeclarationLocation(
                declaredNamespaceLocations,
                GlobalNamespaceName,
                sourceLocation);
        }

        if (members.Count == 0)
        {
            return;
        }

        VisitMembers(
            members,
            namespaceName,
            qualifiedName,
            relativePath,
            importContext,
            declaredNamespaceFiles,
            declaredNamespaceLocations,
            discoveredTypes);
    }

    private static (IReadOnlyList<string> Bases, IReadOnlyList<string> Interfaces) ParseDirectRelationships(BaseTypeDeclarationSyntax declaration)
    {
        var baseList = declaration.BaseList?.Types
            .Select(x => NormalizeTypeReference(x.Type.ToString()))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray()
            ?? [];

        if (baseList.Length == 0)
        {
            return ([], []);
        }

        if (declaration is ClassDeclarationSyntax or RecordDeclarationSyntax)
        {
            var interfaces = new List<string>();
            string? baseType = null;

            foreach (var candidate in baseList)
            {
                if (baseType is null && !LooksLikeInterfaceName(candidate))
                {
                    baseType = candidate;
                    continue;
                }

                interfaces.Add(candidate);
            }

            return (
                baseType is null ? [] : [baseType],
                interfaces.ToArray());
        }

        if (declaration is StructDeclarationSyntax)
        {
            return ([], baseList);
        }

        if (declaration is InterfaceDeclarationSyntax)
        {
            return (baseList, []);
        }

        return ([], []);
    }

    private static string ParseAccessibility(SyntaxTokenList modifiers)
    {
        var hasPublic = modifiers.Any(x => x.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PublicKeyword));
        var hasInternal = modifiers.Any(x => x.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.InternalKeyword));
        var hasProtected = modifiers.Any(x => x.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ProtectedKeyword));
        var hasPrivate = modifiers.Any(x => x.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PrivateKeyword));

        if (hasPublic)
        {
            return "public";
        }

        if (hasProtected && hasInternal)
        {
            return "protectedinternal";
        }

        if (hasPrivate && hasProtected)
        {
            return "privateprotected";
        }

        if (hasProtected)
        {
            return "protected";
        }

        if (hasPrivate)
        {
            return "private";
        }

        if (hasInternal)
        {
            return "internal";
        }

        return "internal";
    }

    private static IReadOnlyList<MemberDiscoveryNode> ParseDeclaredMembers(
        SyntaxList<MemberDeclarationSyntax> members,
        string relativePath)
    {
        var discoveredMembers = new List<MemberDiscoveryNode>();

        foreach (var member in members)
        {
            switch (member)
            {
                case FieldDeclarationSyntax fieldDeclaration:
                {
                    var declaredType = NormalizeTypeReference(fieldDeclaration.Declaration.Type.ToString());
                    var accessibility = ParseAccessibility(fieldDeclaration.Modifiers);

                    foreach (var variable in fieldDeclaration.Declaration.Variables)
                    {
                        var sourceLocation = ToSourceLocation(variable.Identifier.GetLocation(), relativePath);
                        discoveredMembers.Add(new MemberDiscoveryNode(
                            "field",
                            variable.Identifier.ValueText,
                            accessibility,
                            declaredType,
                            variable.Initializer?.Value.ToString(),
                            relativePath,
                            sourceLocation.Line,
                            sourceLocation.Column));
                    }

                    break;
                }
                case PropertyDeclarationSyntax propertyDeclaration:
                    var propertyLocation = ToSourceLocation(propertyDeclaration.Identifier.GetLocation(), relativePath);
                    discoveredMembers.Add(new MemberDiscoveryNode(
                        "property",
                        propertyDeclaration.Identifier.ValueText,
                        ParseAccessibility(propertyDeclaration.Modifiers),
                        NormalizeTypeReference(propertyDeclaration.Type.ToString()),
                        null,
                        relativePath,
                        propertyLocation.Line,
                        propertyLocation.Column));
                    break;
            }
        }

        return discoveredMembers
            .OrderBy(x => x.Kind, StringComparer.Ordinal)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<MemberDiscoveryNode> ParseEnumMembers(EnumDeclarationSyntax enumDeclaration, string relativePath)
    {
        var members = new List<MemberDiscoveryNode>();
        long? lastIntegralValue = null;

        foreach (var member in enumDeclaration.Members)
        {
            string? constantValue;

            if (member.EqualsValue is not null)
            {
                var initializerText = member.EqualsValue.Value.ToString().Trim();
                if (TryParseIntegralConstant(initializerText, out var parsedValue))
                {
                    constantValue = parsedValue.ToString();
                    lastIntegralValue = parsedValue;
                }
                else
                {
                    constantValue = initializerText;
                    lastIntegralValue = null;
                }
            }
            else if (lastIntegralValue is null)
            {
                constantValue = "0";
                lastIntegralValue = 0;
            }
            else
            {
                var nextValue = lastIntegralValue.Value + 1;
                constantValue = nextValue.ToString();
                lastIntegralValue = nextValue;
            }

            var memberLocation = ToSourceLocation(member.Identifier.GetLocation(), relativePath);
            members.Add(new MemberDiscoveryNode(
                "enum-member",
                member.Identifier.ValueText,
                "public",
                null,
                constantValue,
                relativePath,
                memberLocation.Line,
                memberLocation.Column));
        }

        return members
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<MemberDiscoveryNode> ParseRecordPrimaryMembers(RecordDeclarationSyntax recordDeclaration, string relativePath)
    {
        if (recordDeclaration.ParameterList is null)
        {
            return [];
        }

        return recordDeclaration.ParameterList.Parameters
            .Select(parameter =>
            {
                var parameterLocation = ToSourceLocation(parameter.Identifier.GetLocation(), relativePath);
                return new MemberDiscoveryNode(
                    "record-parameter",
                    parameter.Identifier.ValueText,
                    "public",
                    parameter.Type is null ? null : NormalizeTypeReference(parameter.Type.ToString()),
                    null,
                    relativePath,
                    parameterLocation.Line,
                    parameterLocation.Column);
            })
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsPartial(SyntaxTokenList modifiers)
    {
        return modifiers.Any(x => x.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword));
    }

    private static IReadOnlyList<string> ParseGenericParameters(TypeParameterListSyntax? typeParameterList)
    {
        if (typeParameterList is null)
        {
            return [];
        }

        return typeParameterList.Parameters
            .Select(x => x.Identifier.ValueText)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> ParseGenericConstraints(SyntaxList<TypeParameterConstraintClauseSyntax> clauses)
    {
        if (clauses.Count == 0)
        {
            return [];
        }

        return clauses
            .Select(clause =>
            {
                var parameterName = clause.Name.Identifier.ValueText;
                var constraints = clause.Constraints
                    .Select(x => x.ToString().Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToArray();

                return constraints.Length == 0
                    ? parameterName
                    : $"{parameterName}:{string.Join("&", constraints)}";
            })
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
    }

    private static string NormalizeTypeReference(string typeReference)
    {
        var value = typeReference.Trim();
        if (value.StartsWith("global::", StringComparison.Ordinal))
        {
            value = value[8..];
        }

        var genericIndex = value.IndexOf('<');
        if (genericIndex >= 0)
        {
            value = value[..genericIndex];
        }

        return value.Trim();
    }

    private static bool TryParseIntegralConstant(string value, out long parsed)
    {
        var normalized = value.Replace("_", string.Empty, StringComparison.Ordinal).Trim();

        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return long.TryParse(normalized[2..], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out parsed);
        }

        if (normalized.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                parsed = Convert.ToInt64(normalized[2..], 2);
                return true;
            }
            catch
            {
                parsed = default;
                return false;
            }
        }

        return long.TryParse(
            normalized,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out parsed);
    }

    private static bool LooksLikeInterfaceName(string typeName)
    {
        return typeName.Length >= 2
               && typeName[0] == 'I'
               && char.IsUpper(typeName[1]);
    }

    private static string BuildQualifiedTypeName(
        string namespaceName,
        string? declaringTypeQualifiedName,
        string typeName,
        int arity)
    {
        var withArity = arity > 0 ? $"{typeName}`{arity}" : typeName;

        if (!string.IsNullOrWhiteSpace(declaringTypeQualifiedName))
        {
            return $"{declaringTypeQualifiedName}.{withArity}";
        }

        if (namespaceName == GlobalNamespaceName)
        {
            return withArity;
        }

        return $"{namespaceName}.{withArity}";
    }

    private static TypeImportContext BuildImportContext(SyntaxList<UsingDirectiveSyntax> usingDirectives)
    {
        if (usingDirectives.Count == 0)
        {
            return TypeImportContext.Empty;
        }

        var namespaces = new HashSet<string>(StringComparer.Ordinal);
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var usingDirective in usingDirectives)
        {
            if (usingDirective.Name is null)
            {
                continue;
            }

            var target = NormalizeTypeReference(usingDirective.Name.ToString());
            if (string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            if (usingDirective.Alias is not null)
            {
                aliases[usingDirective.Alias.Name.Identifier.ValueText] = target;
                continue;
            }

            if (usingDirective.StaticKeyword != default)
            {
                continue;
            }

            namespaces.Add(target);
        }

        return new TypeImportContext(
            namespaces.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            aliases);
    }

    private static TypeImportContext MergeImportContexts(TypeImportContext parent, TypeImportContext child)
    {
        if (child.Namespaces.Count == 0 && child.Aliases.Count == 0)
        {
            return parent;
        }

        var namespaces = parent.Namespaces
            .Concat(child.Namespaces)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var aliases = new Dictionary<string, string>(parent.Aliases, StringComparer.Ordinal);
        foreach (var pair in child.Aliases)
        {
            aliases[pair.Key] = pair.Value;
        }

        return new TypeImportContext(namespaces, aliases);
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

    private static void RegisterDeclarationLocation(
        Dictionary<string, HashSet<DeclarationSourceLocation>> declaredNamespaceLocations,
        string namespaceName,
        DeclarationSourceLocation sourceLocation)
    {
        if (!declaredNamespaceLocations.TryGetValue(namespaceName, out var locations))
        {
            locations = new HashSet<DeclarationSourceLocation>();
            declaredNamespaceLocations[namespaceName] = locations;
        }

        locations.Add(sourceLocation);
    }

    private static DeclarationSourceLocation ToSourceLocation(Location location, string relativePath)
    {
        var lineSpan = location.GetLineSpan();
        return new DeclarationSourceLocation(
            relativePath,
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character + 1);
    }

    private static void AddNamespaceAndParents(HashSet<string> names, string namespaceName)
    {
        if (namespaceName == GlobalNamespaceName)
        {
            names.Add(namespaceName);
            return;
        }

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
        if (namespaceName == GlobalNamespaceName)
        {
            return null;
        }

        var index = namespaceName.LastIndexOf('.');
        return index > 0
            ? namespaceName[..index]
            : null;
    }

    private static string CombineNamespace(string? currentNamespace, string declaredName)
    {
        if (string.IsNullOrWhiteSpace(currentNamespace) || currentNamespace == GlobalNamespaceName)
        {
            return declaredName;
        }

        return declaredName.StartsWith(currentNamespace + ".", StringComparison.Ordinal)
            ? declaredName
            : $"{currentNamespace}.{declaredName}";
    }

    private static string ResolveNamespace(string? currentNamespace)
    {
        return string.IsNullOrWhiteSpace(currentNamespace)
            ? GlobalNamespaceName
            : currentNamespace;
    }
}
