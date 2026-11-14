namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// The place that the overflow check of <c>Math.Abs</c> of a signed integer reports (#947): the Prelude functions
/// (<c>__gsharp_abs_i32</c> and the others of <c>IntegerDivision.hpp</c>) take it as their last argument, a site of the
/// kernel's error site table or, in a device function, a message that names the place, as <see cref="DivisorCheck"/>
/// passes the place of a division.
/// </summary>
internal static class AbsoluteValueCheck
{
    /// <summary>The text of the error, as .NET words the <see cref="OverflowException"/> of <c>Math.Abs</c>.</summary>
    public const string Message = "Negating the minimum value of a twos complement number is invalid.";

    // The trap kind of the site ID, which tells the site apart from another trap at the same place
    private const string TrapKind = "Overflow";

    /// <summary>The place of the call <paramref name="node"/> of <c>Math.Abs</c>.</summary>
    public static NullCheckSite PlanSite(ExpressionSyntax node, ref TranspileContext context) =>
        ArrayNullCheck.PlanSite(node, null, Message, TrapKind, ref context);
}
