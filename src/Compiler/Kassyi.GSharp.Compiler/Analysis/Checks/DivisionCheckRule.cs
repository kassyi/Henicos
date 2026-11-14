using System.Numerics;

namespace Kassyi.GSharp.Compiler.Analysis.Checks;

/// <summary>
/// Decides the divisor check of an integer division or remainder from the type the operator works in and the ranges of
/// its operands (#718): C# throws <see cref="DivideByZeroException"/> for a divisor of 0, and G# gives the GPU result
/// for <c>MinValue / -1</c>, which x86 <c>idiv</c> cannot compute. The answer is the one definition that the transpiler
/// (<see cref="IntegerSemantics"/>, which writes the Prelude function) and the evaluation order
/// (<see cref="ExpressionTraps"/>, which orders a check that may throw against writes) both read.
/// </summary>
/// <remarks>
/// [WHY] The rule takes values only, no syntax, model or operation, so that the IR can make the same facts and call the
/// same rule.
/// </remarks>
internal static class DivisionCheckRule
{
    /// <summary>
    /// The check of a division or remainder whose operator works in <paramref name="operatorType"/> (int, uint, long or
    /// ulong after the numeric promotions), with the operands in <paramref name="dividend"/> and
    /// <paramref name="divisor"/>; <see cref="DivisionCheck.None"/> for any other type.
    /// </summary>
    public static DivisionCheck Decide(SpecialType operatorType, IntegerRange dividend, IntegerRange divisor)
    {
        if (MinimumOfSigned(operatorType) is not { } minimum)
        {
            return operatorType is SpecialType.System_UInt32 or SpecialType.System_UInt64 && divisor.Contains(0)
                ? DivisionCheck.Divisor
                : DivisionCheck.None;
        }

        return divisor.Contains(0) || MayDivideMinimumByMinusOne(operatorType, dividend, divisor)
            ? DivisionCheck.Divisor
            : DivisionCheck.None;
    }

    /// <summary>
    /// Whether the signed division or remainder in <paramref name="operatorType"/> (int or long) may divide the minimum
    /// of the type by -1, whose GPU result (MinValue, and 0 for the remainder) G# gives where the CPU would not: x86
    /// <c>idiv</c> raises a hardware exception and .NET throws <see cref="OverflowException"/> (the managed path
    /// rewrites such a division, <c>ManagedDivision</c>).
    /// </summary>
    public static bool MayDivideMinimumByMinusOne(SpecialType operatorType, IntegerRange dividend,
        IntegerRange divisor) =>
        MinimumOfSigned(operatorType) is { } minimum &&
        divisor.Contains(BigInteger.MinusOne) &&
        dividend.Contains(minimum);

    /// <summary>Whether the division may raise a run-time error (<see cref="ExpressionTraps"/>).</summary>
    public static bool MayThrow(DivisionCheck check) => check != DivisionCheck.None;

    private static BigInteger? MinimumOfSigned(SpecialType type) => type switch
    {
        SpecialType.System_Int32 => int.MinValue,
        SpecialType.System_Int64 => long.MinValue,
        _ => null
    };
}
