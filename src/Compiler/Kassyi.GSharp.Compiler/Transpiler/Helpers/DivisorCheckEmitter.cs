using Kassyi.GSharp.Compiler.Backends.Cpp;

namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// Plans the error site of a divisor check for the syntax transpiler (<see cref="DivisorCheck"/>).
/// </summary>
internal static class DivisorCheckEmitter
{
    /// <summary>
    /// The place of the division or compound assignment <paramref name="node"/> when <paramref name="helper"/> takes
    /// one; <see langword="null"/> for any other helper and for none.
    /// </summary>
    public static NullCheckSite? PlanSite(string? helper, ExpressionSyntax node, ref TranspileContext context) =>
        helper != null && IntegerSemantics.TakesErrorSite(helper)
            ? ArrayNullCheckEmitter.PlanSite(node, null, DivisorCheck.Message, DivisorCheck.TrapKind, ref context)
            : null;
}
