#nullable enable
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tests.Generators;

/// <summary>
/// Lists every static field and property of the compilation that holds a <c>DifferentialCase</c> (directly, or as a
/// collection of them) in a generated <c>DifferentialCaseRegistry.All</c>, so that tests that need every case (the
/// <c>KnownDivergences.yaml</c> integrity check) read them without reflection. The members are referenced by name in
/// the generated code, so a case the registry cannot reach (a private field, a field of a generic type) is a compile
/// error there instead of a case that is silently left out.
/// </summary>
[Generator]
public class DifferentialCaseRegistryGenerator : IIncrementalGenerator
{
    public const string DifferentialCaseMetadataName = "Kassyi.GSharp.Tests.Shared.Differential.DifferentialCase";
    public const string RegistryNamespace = "Kassyi.GSharp.Tests.Shared.Differential";
    public const string RegistryTypeName = "DifferentialCaseRegistry";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Every test project loads this assembly for its HNT analyzers; only the ones that compile DifferentialCase get
        // a registry (an empty one when they declare no case)
        var hasCaseType = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.GetTypeByMetadataName(DifferentialCaseMetadataName) is not null
        );

        var members = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => IsStaticMemberDeclaration(node),
                static (syntaxContext, cancellationToken) => CaseMembersOf(syntaxContext, cancellationToken)
            ).SelectMany(static (found, _) => found)
            .Collect();

        context.RegisterSourceOutput(hasCaseType.Combine(members), static (spc, input) =>
        {
            if (input.Left)
            {
                spc.AddSource(RegistryTypeName + ".g.cs", Emit(input.Right));
            }
        });
    }

    private static bool IsStaticMemberDeclaration(SyntaxNode node) =>
        node switch
        {
            FieldDeclarationSyntax field => field.Modifiers.Any(SyntaxKind.StaticKeyword),
            PropertyDeclarationSyntax property => property.Modifiers.Any(SyntaxKind.StaticKeyword),
            _ => false,
        };

    private static ImmutableArray<CaseMember> CaseMembersOf(
        GeneratorSyntaxContext syntaxContext,
        CancellationToken cancellationToken)
    {
        var model = syntaxContext.SemanticModel;
        if (model.Compilation.GetTypeByMetadataName(DifferentialCaseMetadataName) is not { } caseType)
        {
            return ImmutableArray<CaseMember>.Empty;
        }

        var found = ImmutableArray.CreateBuilder<CaseMember>();
        switch (syntaxContext.Node)
        {
            case FieldDeclarationSyntax field:
                foreach (var variable in field.Declaration.Variables)
                {
                    if (model.GetDeclaredSymbol(variable, cancellationToken) is IFieldSymbol symbol)
                    {
                        AddIfCase(found, symbol, symbol.Type, caseType);
                    }
                }

                break;
            case PropertyDeclarationSyntax property:
                if (model.GetDeclaredSymbol(property, cancellationToken) is { } propertySymbol)
                {
                    AddIfCase(found, propertySymbol, propertySymbol.Type, caseType);
                }

                break;
        }

        return found.ToImmutable();
    }

    private static void AddIfCase(
        ImmutableArray<CaseMember>.Builder found,
        ISymbol member,
        ITypeSymbol memberType,
        INamedTypeSymbol caseType)
    {
        bool isCollection;
        if (SymbolEqualityComparer.Default.Equals(memberType, caseType))
        {
            isCollection = false;
        }
        else if (IsCollectionOf(memberType, caseType))
        {
            isCollection = true;
        }
        else
        {
            return;
        }

        // ContainingType is set for a field or property; the null check only satisfies the compiler
        if (member.ContainingType is not { } containingType)
        {
            return;
        }

        string type = QualifiedName(containingType);
        found.Add(new CaseMember("global::" + type, member.Name, type + "." + member.Name, isCollection));
    }

    // Namespace and containing types joined by '.', from the symbols. Type arguments are left out on purpose: a case
    // in a generic type cannot be read without them, so the reference fails to compile instead of being skipped.
    private static string QualifiedName(INamedTypeSymbol type)
    {
        if (type.ContainingType is { } outer)
        {
            return QualifiedName(outer) + "." + type.Name;
        }

        return type.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? NamespaceName(ns) + "." + type.Name
            : type.Name;
    }

    private static string NamespaceName(INamespaceSymbol ns) =>
        ns.ContainingNamespace is { IsGlobalNamespace: false } parent ? NamespaceName(parent) + "." + ns.Name : ns.Name;

    // An array of cases or any type that is or implements IEnumerable<DifferentialCase>
    private static bool IsCollectionOf(ITypeSymbol type, INamedTypeSymbol caseType)
    {
        if (type is IArrayTypeSymbol array)
        {
            return SymbolEqualityComparer.Default.Equals(array.ElementType, caseType);
        }

        if (IsEnumerableOf(type, caseType))
        {
            return true;
        }

        foreach (var implemented in type.AllInterfaces)
        {
            if (IsEnumerableOf(implemented, caseType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEnumerableOf(ITypeSymbol type, INamedTypeSymbol caseType) =>
        type is INamedTypeSymbol { IsGenericType: true } named &&
        named.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T &&
        SymbolEqualityComparer.Default.Equals(named.TypeArguments[0], caseType);

    private static string Emit(ImmutableArray<CaseMember> members)
    {
        var sorted = members.Sort(static (a, b) => string.CompareOrdinal(a.DisplayName, b.DisplayName));
        var entries = new StringBuilder();
        foreach (var member in sorted)
        {
            string reference = member.ContainingType + "." + member.Name;
            entries.AppendLine(
                member.IsCollection
                    ? $"..Each(\"{member.DisplayName}\", {reference}),"
                    : $"(\"{member.DisplayName}\", {reference}),"
            );
        }

        return $$"""
            // <auto-generated/>
            #nullable enable
            namespace {{RegistryNamespace}};
            /// <summary>
            /// Every DifferentialCase held by a static field or property of this assembly, with the member that holds it
            /// (generated by DifferentialCaseRegistryGenerator).
            /// </summary>
            internal static class {{RegistryTypeName}}
            {
            public static global::System.Collections.Generic.IReadOnlyList<(string Member, global::{{DifferentialCaseMetadataName}} Case)> All { get; } =
            [
            {{entries}}];
            private static global::System.Collections.Generic.IEnumerable<(string Member, global::{{DifferentialCaseMetadataName}} Case)> Each(string member, global::System.Collections.Generic.IEnumerable<global::{{DifferentialCaseMetadataName}}> cases)
            {
            foreach (var testCase in cases)
            {
            yield return (member, testCase);
            }
            }
            }

            """;
    }

    private readonly record struct CaseMember(string ContainingType, string Name, string DisplayName, bool IsCollection);
}
