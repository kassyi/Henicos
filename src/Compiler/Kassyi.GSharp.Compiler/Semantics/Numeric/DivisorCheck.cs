namespace Kassyi.GSharp.Compiler.Semantics;

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

    /// <summary>The trap kind of the site ID, which tells the site apart from another trap at the same place.</summary>
    internal const string TrapKind = "DivideByZero";
}
