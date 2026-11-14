namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules;

/// <summary>
/// Writes <c>Math.Abs</c> of <c>int</c>, <c>long</c>, <c>short</c> and <c>sbyte</c> as a call of a Prelude function that
/// raises <see cref="OverflowException"/> for the minimum value, as C# does (#947): <c>__gsharp_abs_i32(&lt;value&gt;,
/// &lt;site&gt;)</c>. C++ <c>abs</c> of the minimum is undefined, and CUDA gives the minimum back.
/// </summary>
internal sealed class MathAbsTranspilerRule : IInvocationTranspilerRule
{
    /// <summary>Whether <paramref name="symbol"/> is one of the signed integer overloads of <c>Math.Abs</c>.</summary>
    public static bool Claims(IMethodSymbol symbol) =>
        symbol is
        {
            Name: "Abs",
            Parameters: [{ Type.SpecialType: SpecialType.System_Int32 or SpecialType.System_Int64 or
                SpecialType.System_Int16 or SpecialType.System_SByte }],
            ContainingType: { Name: "Math", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } }
        };

    /// <inheritdoc />
    public bool TryTranspile(InvocationExpressionSyntax node, ref TranspileContext context, IMethodSymbol symbol)
    {
        if (!Claims(symbol) || node.ArgumentList.Arguments is not [var argument])
        {
            return false;
        }

        var parameterType = symbol.Parameters[0].Type;
        var helper = GetHelper(parameterType.SpecialType);
        var site = AbsoluteValueCheck.PlanSite(node, ref context);
        var writer = context.Emitter.Writer;
        writer.Append(helper);
        using (writer.Parentheses())
        {
            ArithmeticConversions.AppendArgument(argument, parameterType, ref context);
            writer.Append(',');
            ArrayNullCheck.AppendSite(writer, site);
        }

        return true;
    }

    private static string GetHelper(SpecialType type) => type switch
    {
        SpecialType.System_Int32 => GSharpInternalNames.IntegerSemantics.AbsInt32,
        SpecialType.System_Int64 => GSharpInternalNames.IntegerSemantics.AbsInt64,
        SpecialType.System_Int16 => GSharpInternalNames.IntegerSemantics.AbsInt16,
        _ => GSharpInternalNames.IntegerSemantics.AbsSByte
    };
}
