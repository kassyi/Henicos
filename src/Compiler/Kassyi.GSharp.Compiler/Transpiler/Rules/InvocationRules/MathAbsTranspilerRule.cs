using Kassyi.GSharp.Compiler.Backends.Cpp;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules;

/// <summary>
/// Writes <c>Math.Abs</c> of <c>int</c>, <c>long</c>, <c>short</c>, <c>sbyte</c> and <c>nint</c> as a call of a
/// Prelude function that raises <see cref="OverflowException"/> for the minimum value, as C# does (#947):
/// <c>__gsharp_abs_i32(&lt;value&gt;, &lt;site&gt;)</c>. C++ <c>abs</c> of the minimum is undefined, and CUDA gives the
/// minimum back.
/// </summary>
/// <remarks>
/// The place of the call is planned as the place of a divisor check is (<see cref="DivisorCheck"/>): a site of the
/// kernel's error site table or, in a device function, a message that names it.
/// </remarks>
internal sealed class MathAbsTranspilerRule : IInvocationTranspilerRule
{
    // The text of the error, as .NET words the OverflowException of Math.Abs
    private const string Message = "Negating the minimum value of a twos complement number is invalid.";

    // The trap kind of the site ID, which tells the site apart from another trap at the same place
    private const string TrapKind = "Overflow";

    /// <summary>Whether <paramref name="symbol"/> is one of the signed integer overloads of <c>Math.Abs</c>.</summary>
    public static bool Claims(IMethodSymbol symbol) =>
        symbol is
        {
            Name: "Abs",
            Parameters: [{ Type.SpecialType: SpecialType.System_Int32 or SpecialType.System_Int64 or
                SpecialType.System_Int16 or SpecialType.System_SByte or SpecialType.System_IntPtr }]
        } &&
        IsSystemMath(symbol.ContainingType);

    private static bool IsSystemMath(INamedTypeSymbol? type) =>
        type is { Name: "Math", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } };

    /// <inheritdoc />
    public bool TryTranspile(InvocationExpressionSyntax node, ref TranspileContext context, IMethodSymbol symbol)
    {
        if (!Claims(symbol) || node.ArgumentList.Arguments is not [var argument])
        {
            return false;
        }

        var parameterType = symbol.Parameters[0].Type;
        var site = ArrayNullCheckEmitter.PlanSite(node, null, Message, TrapKind, ref context);
        var writer = context.Emitter.Writer;
        writer.Append(GetHelper(parameterType.SpecialType));
        using (writer.Parentheses())
        {
            ArithmeticConversionsEmitter.AppendArgument(argument, parameterType, ref context);
            writer.Append(',');
            NullCheckSiteWriter.AppendSite(writer, site);
        }

        return true;
    }

    private static string GetHelper(SpecialType type) => type switch
    {
        SpecialType.System_Int32 => GSharpInternalNames.IntegerSemantics.AbsInt32,
        SpecialType.System_Int64 or SpecialType.System_IntPtr => GSharpInternalNames.IntegerSemantics.AbsInt64,
        SpecialType.System_Int16 => GSharpInternalNames.IntegerSemantics.AbsInt16,
        _ => GSharpInternalNames.IntegerSemantics.AbsSByte
    };
}
