using Kassyi.GSharp.Compiler.Backends.Cpp;

namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.Literals;

/// <summary>
/// Writes a constant expression as a C++ literal of the type chosen by <see cref="ConstantAtUse"/> (#859).
/// </summary>
internal static class ConstantAtUseEmitter
{
    /// <summary>
    /// Writes <paramref name="value"/>, the constant of <paramref name="expression"/>, as the C++ literal of
    /// <see cref="ConstantAtUse.GetTypeAtUse(ExpressionSyntax, ITypeSymbol, SemanticModel)"/>.
    /// </summary>
    public static void Append(ExpressionSyntax expression, ITypeSymbol ownType, object? value,
        ref TranspileContext context)
    {
        var type = ConstantAtUse.GetTypeAtUse(expression, ownType, context.Query.SemanticModel);
        context.Emitter.Writer.Append(ConstexprLiteral.FormatCppConstant(type, value));
    }
}
