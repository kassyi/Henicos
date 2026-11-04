namespace Kassyi.GSharp.Compiler.Transpiler.Managed;

using System;
using System.Collections.Immutable;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// Collects site keys of <c>SharedMemory&lt;T&gt;</c> creations written directly inside generic struct constructors.
/// Disambiguates shared memory slots across distinct closed generic struct types (#543).
/// </summary>
internal static class TypeKeyedSharedMemoryCollector
{
    public const string MethodName = CallerInfoSiteKey.ConstructorMemberName;

    /// <summary>
    /// Checks whether the syntax node is a candidate generic struct declaration.
    /// </summary>
    public static bool IsCandidate(SyntaxNode node) =>
        node is StructDeclarationSyntax { TypeParameterList.Parameters.Count: > 0 }
        or RecordDeclarationSyntax
        {
            ClassOrStructKeyword.RawKind: (int)SyntaxKind.StructKeyword,
            TypeParameterList.Parameters.Count: > 0
        };

    /// <summary>
    /// Collects site keys of direct shared memory creations inside constructors of a generic struct.
    /// </summary>
    public static EquatableArray<ulong> Collect(GeneratorSyntaxContext context)
    {
        if (context.Node is not TypeDeclarationSyntax typeDecl)
        {
            return default;
        }

        var model = context.SemanticModel;
        if (model.GetDeclaredSymbol(typeDecl) is not INamedTypeSymbol { TypeKind: TypeKind.Struct, IsGenericType: true })
        {
            return default;
        }

        var builder = ImmutableArray.CreateBuilder<ulong>();
        foreach (var member in typeDecl.Members)
        {
            if (member is ConstructorDeclarationSyntax ctor && !ctor.Modifiers.Any(SyntaxKind.StaticKeyword))
            {
                CollectFromConstructor(ctor, model, builder);
            }
        }

        return builder.Count == 0 ? default : builder.ToImmutable().AsEquatableArray();
    }

    private static void CollectFromConstructor(
        ConstructorDeclarationSyntax ctor,
        SemanticModel model,
        ImmutableArray<ulong>.Builder builder)
    {
        var body = (SyntaxNode?)ctor.Body ?? ctor.ExpressionBody;
        if (body == null)
        {
            return;
        }

        var resolver = model.Compilation.Options.SourceReferenceResolver;
        foreach (var node in body.DescendantNodes())
        {
            if (node is BaseObjectCreationExpressionSyntax creation &&
                IsDirectlyInConstructor(creation, ctor) &&
                model.GetTypeSymbol(creation).IsSharedMemoryType())
            {
                builder.Add(CallerInfoSiteKey.ComputeConstructorSiteKey(
                    creation.SyntaxTree,
                    creation.Span,
                    resolver));
            }
        }
    }

    private static bool IsDirectlyInConstructor(BaseObjectCreationExpressionSyntax creation, ConstructorDeclarationSyntax ctor)
    {
        for (SyntaxNode? current = creation.Parent; current != null && current != ctor; current = current.Parent)
        {
            if (current is LocalFunctionStatementSyntax
                or AnonymousFunctionExpressionSyntax
                or BaseMethodDeclarationSyntax
                or TypeDeclarationSyntax)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Computes the site key for a <c>SharedMemory&lt;T&gt;</c> creation in a constructor using FNV-1a 64.
    /// Matches <c>SharedMemory.ComputeSiteKey(filePath, lineNumber, ".ctor")</c> at runtime.
    /// </summary>
    public static ulong ComputeSiteKey(string filePath, int lineNumber) =>
        CallerInfoSiteKey.ComputeConstructorSiteKey(filePath, lineNumber);
}
