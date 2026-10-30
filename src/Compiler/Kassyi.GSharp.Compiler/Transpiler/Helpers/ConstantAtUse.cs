using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// Writes the value of a constant expression (a <c>const</c> field, a negated literal, a converted operand) as a C++
/// literal of the type C# gives it where it is used (#859).
/// </summary>
/// <remarks>
/// A narrow constant keeps its type in C++ (<c>(unsigned char)255</c>, <see cref="ConstexprLiteral"/>) so that
/// <c>auto</c> takes the C# type. Where C# has already converted the constant implicitly (<c>Math.Max(ByteMax, x)</c>
/// calls <c>Max(int, int)</c>), the C++ is the converted type: a template that deduces from the argument
/// (<c>max(T, T)</c>) would otherwise see a type C# never uses there.
/// </remarks>
internal static class ConstantAtUse
{
    /// <summary>
    /// The type C# gives the constant <paramref name="expression"/> where it is used: the target of an implicit numeric
    /// or constant conversion applied to it when that is a primitive value type, otherwise <paramref name="ownType"/> (no
    /// conversion, an enum or user-defined conversion, or a node whose binding cannot be found).
    /// </summary>
    private static ITypeSymbol GetTypeAtUse(ExpressionSyntax expression, ITypeSymbol ownType, SemanticModel model)
    {
        // C# binds no operation for parentheses: the conversion of (ByteMax) is the parent of the field reference
        return model.GetSafeOperation(expression)?.Parent is IConversionOperation
               {
                   IsImplicit: true,
                   OperatorMethod: null,
                   Type: { } target
               } conversion &&
               target.SpecialType.IsPrimitiveValue() &&
               conversion.GetConversion() is { IsNumeric: true } or { IsConstantExpression: true }
            ? target
            : ownType;
    }

    /// <summary>
    /// Writes <paramref name="value"/>, the constant of <paramref name="expression"/>, as the C++ literal of
    /// <see cref="GetTypeAtUse"/>.
    /// </summary>
    public static void Append(ExpressionSyntax expression, ITypeSymbol ownType, object? value,
        ref TranspileContext context)
    {
        var type = GetTypeAtUse(expression, ownType, context.Query.SemanticModel);
        context.Emitter.Writer.Append(ConstexprLiteral.FormatCppConstant(type, value));
    }
}
