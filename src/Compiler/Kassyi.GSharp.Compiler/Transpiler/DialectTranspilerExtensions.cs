using Kassyi.GSharp.Compiler.Backends.Cpp;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Bridges legacy <see cref="ITranspilerDialect"/> type and parameter emission extension calls over
/// <see cref="TranspileContext"/> to <see cref="CppTypeWriter"/>.
/// </summary>
internal static class DialectTranspilerExtensions
{
    private static readonly Func<ITypeSymbol?, INamedTypeSymbol?, Compilation?, ITypeSymbol?>
        s_substituteTypeParameters = StructTypeSubstitution.SubstituteTypeParameters;

    private static readonly Action<ITypeSymbol?, INamedTypeSymbol?, Compilation, HashSet<INamedTypeSymbol>>
        s_registerUsedStruct = StructOrdering.RegisterUsedStructIfApplicable;

    private static readonly Action<Location, object?> s_reportUnsubstitutedTypeParameter =
        static (location, failureSink) =>
        {
            if (failureSink is Action<TranspileFailure> sink)
            {
                sink(new TranspileFailure(GSharpDiagnostics.UnsupportedSyntax, location,
                    ["unsubstituted type parameter"]));
            }
        };

    internal static CppTypeContext CreateTypeContext(ref TranspileContext context) =>
        CreateTypeContext(ref context, context.Emitter.Dialect);

    private static CppTypeContext CreateTypeContext(ref TranspileContext context, ITranspilerDialect dialect) =>
        new(context.Emitter.Writer, dialect)
        {
            Compilation = context.Query.SemanticModel.Compilation,
            CurrentStructType = context.Emitter.CurrentStructType,
            UsedStructs = context.Query.UsedStructs,
            CurrentLocation = context.Emitter.CurrentNode is { } currentNode
                ? context.Query.SemanticModel.GetReportLocation(currentNode)
                : null,
            FailureSink = context.Reporter.FailureSink,
            SubstituteTypeParameters = s_substituteTypeParameters,
            RegisterUsedStruct = s_registerUsedStruct,
            TryGetUserTypeName = CppNameFormatterForwarders.TryGetUserTypeName,
            ReportUnsubstitutedTypeParameter = s_reportUnsubstitutedTypeParameter,
        };

    /// <summary>
    /// Emits the target C++ type representation for <paramref name="typeSymbol"/> through <see cref="CppTypeWriter"/>.
    /// </summary>
    public static void AppendType(
        this ITranspilerDialect dialect,
        ref TranspileContext context,
        ITypeSymbol? typeSymbol)
    {
        var typeContext = CreateTypeContext(ref context, dialect);
        CppTypeWriter.AppendType(in typeContext, typeSymbol);
    }

    /// <summary>
    /// Emits the target C++ parameter type through <see cref="CppTypeWriter"/>.
    /// </summary>
    public static void AppendParameter(
        this ITranspilerDialect dialect,
        ref TranspileContext context,
        ParameterSyntax p,
        ITypeSymbol? typeSymbol,
        IParameterSymbol? parameterSymbol)
    {
        var isPassedByAddress = parameterSymbol is not null
            ? context.Emitter.IsParameterPassedByAddress(parameterSymbol)
            : p.Modifiers.Any(SyntaxKind.RefKeyword) ||
              p.Modifiers.Any(SyntaxKind.OutKeyword) ||
              p.Modifiers.Any(SyntaxKind.InKeyword);

        var typeContext = CreateTypeContext(ref context, dialect);
        CppTypeWriter.AppendParameter(in typeContext, typeSymbol, parameterSymbol, isPassedByAddress);
    }
}
