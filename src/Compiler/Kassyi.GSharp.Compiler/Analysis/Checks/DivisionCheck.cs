namespace Kassyi.GSharp.Compiler.Analysis.Checks;

/// <summary>
/// What the divisor check of an integer <c>x / y</c> or <c>x % y</c> does (<see cref="DivisionCheckRule"/>).
/// </summary>
internal enum DivisionCheck : byte
{
    /// <summary>
    /// The C++ operator: the divisor cannot be 0, nor -1 against a dividend that may be the minimum of a signed type.
    /// </summary>
    None,

    /// <summary>
    /// The Prelude function: <see cref="DivideByZeroException"/> when the divisor is 0; for a signed type the native
    /// build also gives the GPU result of <c>MinValue / -1</c> (MinValue) and <c>MinValue % -1</c> (0).
    /// </summary>
    Divisor
}
