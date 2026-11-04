using System.Collections.Immutable;
using Kassyi.GSharp.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Builds and updates Roslyn <see cref="CSharpCompilation"/> and <see cref="SemanticModel"/> instances
/// using the G# <c>Core</c> and runtime metadata references, and resolves expression and symbol types
/// via <see cref="SemanticModel.GetTypeInfo(SyntaxNode, System.Threading.CancellationToken)"/>.
/// </summary>
internal static class MetamorphicTypeResolver
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> s_baseReferences = new(CreateBaseReferences);
    private static readonly Lazy<SyntaxTree> s_auxiliaryFuzzTypesTree = new(CreateAuxiliaryFuzzTypesTree);

    private static readonly CSharpCompilationOptions s_compilationOptions =
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithAllowUnsafe(true)
            .WithOptimizationLevel(OptimizationLevel.Release);

    internal static CSharpCompilation CreateCompilation(
        string source,
        out CompilationUnitSyntax root,
        out SemanticModel semanticModel)
    {
        var mainTree = CSharpSyntaxTree.ParseText(source, FuzzSyntaxHelpers.ParseOptions, path: "MetamorphicSubject.cu.cs");
        root = (CompilationUnitSyntax)mainTree.GetRoot();

        bool declaresFuzzTypes = ContainsTypeDeclarationNamed(root, "FuzzEnumByte") ||
                                 ContainsTypeDeclarationNamed(root, "FuzzStructL1");
        SyntaxTree[] syntaxTrees = declaresFuzzTypes
            ? [mainTree]
            : [mainTree, s_auxiliaryFuzzTypesTree.Value];

        var compilation = CSharpCompilation.Create(
            "MetamorphicCompilation",
            syntaxTrees,
            s_baseReferences.Value,
            s_compilationOptions);

        semanticModel = compilation.GetSemanticModel(mainTree);
        return compilation;
    }

    internal static CSharpCompilation ReplaceSyntaxTree(
        CSharpCompilation compilation,
        SyntaxTree oldTree,
        CompilationUnitSyntax newRoot,
        out CompilationUnitSyntax updatedRoot,
        out SemanticModel updatedModel)
    {
        string rendered = FuzzSyntaxHelpers.RenderFullNode(newRoot);
        var newTree = CSharpSyntaxTree.ParseText(rendered, FuzzSyntaxHelpers.ParseOptions, path: oldTree.FilePath);
        updatedRoot = (CompilationUnitSyntax)newTree.GetRoot();
        var updatedCompilation = compilation.ReplaceSyntaxTree(oldTree, newTree);
        updatedModel = updatedCompilation.GetSemanticModel(newTree);
        return updatedCompilation;
    }

    internal static bool HasSemanticErrorsInMethod(MethodDeclarationSyntax method, SemanticModel semanticModel)
    {
        foreach (var diag in semanticModel.GetDiagnostics(method.Span))
        {
            if (diag.Severity == DiagnosticSeverity.Error)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool TryResolveExpressionType(
        ExpressionSyntax expr,
        SemanticModel semanticModel,
        [NotNullWhen(true)] out ITypeSymbol? typeSymbol)
    {
        var typeInfo = semanticModel.GetTypeInfo(expr);
        var candidate = typeInfo.Type ?? typeInfo.ConvertedType;
        if (candidate is null || candidate.TypeKind == TypeKind.Error)
        {
            typeSymbol = null;
            return false;
        }

        typeSymbol = candidate;
        return true;
    }

    internal static bool TryResolveSupportedValueType(
        ExpressionSyntax expr,
        SemanticModel semanticModel,
        [NotNullWhen(true)] out ITypeSymbol? typeSymbol,
        [NotNullWhen(true)] out string? typeSyntaxText)
    {
        var typeInfo = semanticModel.GetTypeInfo(expr);
        var type = typeInfo.Type;
        if (type is null || !TryFormatSupportedScalarOrEnumType(type, out typeSyntaxText))
        {
            typeSymbol = null;
            typeSyntaxText = null;
            return false;
        }

        typeSymbol = type;
        return true;
    }

    internal static bool TryResolveSupportedSymbolValueType(
        ISymbol? symbol,
        [NotNullWhen(true)] out ITypeSymbol? typeSymbol,
        [NotNullWhen(true)] out string? typeSyntaxText)
    {
        ITypeSymbol? candidate = symbol switch
        {
            ILocalSymbol { IsRef: false } local => local.Type,
            IParameterSymbol { RefKind: RefKind.None } param => param.Type,
            _ => null,
        };

        if (candidate is null || !TryFormatSupportedScalarOrEnumType(candidate, out typeSyntaxText))
        {
            typeSymbol = null;
            typeSyntaxText = null;
            return false;
        }

        typeSymbol = candidate;
        return true;
    }

    internal static bool IsSupportedScalarOrEnumValueType(ITypeSymbol? type) =>
        type is not null && TryFormatSupportedScalarOrEnumType(type, out _);

    internal static bool Is32Or64BitIntegerType(ITypeSymbol? type) =>
        type is not null &&
        type.SpecialType is SpecialType.System_Int32
            or SpecialType.System_UInt32
            or SpecialType.System_Int64
            or SpecialType.System_UInt64;

    internal static bool IsFloatingPointType(ITypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        if (type.SpecialType is SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal)
        {
            return true;
        }

        return string.Equals(type.Name, "Half", StringComparison.Ordinal) &&
               type.ContainingNamespace is { Name: "System", ContainingNamespace.IsGlobalNamespace: true };
    }

    internal static string FormatCSharpType(ITypeSymbol type) =>
        TryFormatSupportedScalarOrEnumType(type, out string? formatted) ? formatted : type.Name;

    private static bool TryFormatSupportedScalarOrEnumType(
        ITypeSymbol type,
        [NotNullWhen(true)] out string? typeSyntaxText)
    {
        if (type.TypeKind == TypeKind.Error)
        {
            typeSyntaxText = null;
            return false;
        }

        if (type.TypeKind == TypeKind.Enum && !string.IsNullOrEmpty(type.Name))
        {
            typeSyntaxText = type.Name;
            return true;
        }

        typeSyntaxText = type.SpecialType switch
        {
            SpecialType.System_SByte => "sbyte",
            SpecialType.System_Byte => "byte",
            SpecialType.System_Int16 => "short",
            SpecialType.System_UInt16 => "ushort",
            SpecialType.System_Int32 => "int",
            SpecialType.System_UInt32 => "uint",
            SpecialType.System_Int64 => "long",
            SpecialType.System_UInt64 => "ulong",
            SpecialType.System_Boolean => "bool",
            SpecialType.System_Char => "char",
            _ => null,
        };

        return typeSyntaxText is not null;
    }

    private static bool ContainsTypeDeclarationNamed(CompilationUnitSyntax root, string typeName)
    {
        foreach (var node in root.DescendantNodes())
        {
            if (node is BaseTypeDeclarationSyntax typeDecl &&
                string.Equals(typeDecl.Identifier.ValueText, typeName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static SyntaxTree CreateAuxiliaryFuzzTypesTree()
    {
        using var writer = new SourceWriter();
        FuzzCaseEmitter.AppendKernelsFileHeader(writer, "Kassyi.GSharp.Tests.Shared.Differential");
        ScalarFuzzGenerator.AppendEnumDeclarations(writer);
        writer.AppendLine();
        FuzzCompositeModel.AppendCompositeStructDeclarations(writer);
        return CSharpSyntaxTree.ParseText(writer.ToString(), FuzzSyntaxHelpers.ParseOptions, path: "MetamorphicAuxFuzzTypes.g.cs");
    }

    private static ImmutableArray<MetadataReference> CreateBaseReferences()
    {
        string trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty;
        var list = new List<MetadataReference>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int start = 0;
        for (int i = 0; i <= trusted.Length; i++)
        {
            if (i == trusted.Length || trusted[i] == Path.PathSeparator)
            {
                if (i > start)
                {
                    string path = trusted[start..i];
                    if (File.Exists(path) && seenPaths.Add(path))
                    {
                        list.Add(MetadataReference.CreateFromFile(path));
                    }
                }

                start = i + 1;
            }
        }

        string corePath = typeof(Gpu).Assembly.Location;
        if (!string.IsNullOrWhiteSpace(corePath) && File.Exists(corePath) && seenPaths.Add(corePath))
        {
            list.Add(MetadataReference.CreateFromFile(corePath));
        }

        return [.. list];
    }
}
