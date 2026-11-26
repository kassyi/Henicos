using Kassyi.GSharp.Compiler.Backends.Cpp;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Bridges legacy <see cref="ITranspilerDialect"/> type and parameter emission extension calls over
/// <see cref="TranspileContext"/> to <see cref="CppTypeWriter"/>.
/// </summary>
internal static class DialectTranspilerExtensions
{
    internal static CppTypeContext<TranspileTypeServices> CreateTypeContext(ref TranspileContext context) =>
        CreateTypeContext(ref context, context.Emitter.Dialect);

    private static CppTypeContext<TranspileTypeServices> CreateTypeContext(
        ref TranspileContext context,
        ITranspilerDialect dialect) =>
        new(
            context.Emitter.Writer,
            dialect,
            new TranspileTypeServices(
                context.Reporter,
                context.Emitter.CurrentNode,
                context.Emitter.CurrentStructType,
                context.Query.SemanticModel.Compilation,
                context.Query.UsedStructs));

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
