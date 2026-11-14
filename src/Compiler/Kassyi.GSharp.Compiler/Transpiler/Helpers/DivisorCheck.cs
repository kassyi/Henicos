namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// The place that the divisor check of an integer division or remainder reports (#718): the Prelude functions that
/// <see cref="IntegerSemantics.TakesErrorSite"/> names (<c>IntegerDivision.hpp</c>) take it as their last argument, a
/// site of the kernel's error site table or, in a device function, a message that names the place, as
/// <see cref="ArrayNullCheck"/> passes the place of a null array read.
/// </summary>
internal static class DivisorCheck
{
    /// <summary>The text of the error, as .NET words <see cref="DivideByZeroException"/>.</summary>
    public const string Message = "Attempted to divide by zero.";

    // The trap kind of the site ID, which tells the site apart from another trap at the same place
    private const string TrapKind = "DivideByZero";

    /// <summary>
    /// The place of the division or compound assignment <paramref name="node"/> when <paramref name="helper"/> takes
    /// one; <see langword="null"/> for any other helper and for none.
    /// </summary>
    public static NullCheckSite? PlanSite(string? helper, ExpressionSyntax node, ref TranspileContext context) =>
        helper != null && IntegerSemantics.TakesErrorSite(helper)
            ? ArrayNullCheck.PlanSite(node, null, Message, TrapKind, ref context)
            : null;

    /// <summary>Writes <c>,&lt;site&gt;</c>, the last argument of the helper, when there is a site.</summary>
    public static void AppendSiteArgument(SourceWriter writer, NullCheckSite? site)
    {
        if (site is not { } value)
        {
            return;
        }

        writer.Append(',');
        ArrayNullCheck.AppendSite(writer, value);
    }
}
