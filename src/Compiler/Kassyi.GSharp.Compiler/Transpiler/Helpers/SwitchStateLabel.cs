namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// Writes the C++ label of a <c>case</c> that <c>goto case</c> jumps to (<c>__gsharp_switch_&lt;id&gt;_state_&lt;value&gt;</c>),
/// with the suffix taken from the constant value of the case, not from its emitted text (#859, #939, #709, #896).
/// </summary>
/// <remarks>
/// [WHY] The C++ of a case value is an expression (<c>(unsigned char)1</c>, <c>(-1)</c>, <c>(char16_t)97</c>), which is
/// no part of an identifier. The value is: <c>1</c>, <c>m1</c> for -1, <c>97</c> for <c>'a'</c>, the number of an enum
/// member. The case label and the <c>goto case</c> get the same suffix for the same value, whatever its type: C#
/// converts the value of <c>goto case</c> to the type of the switch.
/// </remarks>
internal static class SwitchStateLabel
{
    /// <summary>The construct in the GSH0003 message for a case value that names no label (<c>case 1.5f:</c>).</summary>
    public const string UnsupportedCaseValue =
        "case value that is not an integer, char, bool or enum constant (it cannot name the C++ case label)";

    /// <summary>
    /// Writes the label of the case whose value is <paramref name="value"/>, in the switch being transpiled; reports a
    /// value that names no label (a constant that is not an integer, <c>char</c>, <c>bool</c> or enum).
    /// </summary>
    public static void Append(ExpressionSyntax value, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        writer.Append($"{GSharpInternalNames.Lowering.SwitchStatePrefix}{context.Emitter.CurrentSwitchId}_state_");
        if (GetConstant(value, context.Query.SemanticModel) is not { } constant || GetSuffix(constant) is not { } suffix)
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, value, UnsupportedCaseValue);
            context.Visitor.AppendUnsupportedPlaceholder(value);
            return;
        }

        writer.Append(suffix);
    }

    // The bound constant; a case that lowering wrote (the states of a CPU state machine) is a literal outside the
    // compilation, which has the value of its token
    private static object? GetConstant(ExpressionSyntax value, SemanticModel model) =>
        model.GetSafeConstantValue(value) is { HasValue: true, Value: { } constant }
            ? constant
            : value is LiteralExpressionSyntax literal
                ? literal.Token.Value
                : null;

    // The digits of an integral value, with m for a minus sign; null for a value that is not integral
    private static string? GetSuffix(object constant)
    {
        var digits = constant switch
        {
            char character => ((int)character).ToString(CultureInfo.InvariantCulture),
            bool flag => flag ? "1" : "0",
            sbyte or byte or short or ushort or int or uint or long or ulong =>
                Convert.ToString(constant, CultureInfo.InvariantCulture),
            _ => null
        };
        return digits != null && digits.StartsWith("-", StringComparison.Ordinal) ? "m" + digits.Substring(1) : digits;
    }
}
