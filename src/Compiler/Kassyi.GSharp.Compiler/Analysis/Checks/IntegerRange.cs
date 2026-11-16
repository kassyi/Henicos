using System.Numerics;

namespace Kassyi.GSharp.Compiler.Analysis.Checks;

/// <summary>
/// The values an integer operand can take, both ends included: a fact that the check rules
/// (<see cref="DivisionCheckRule"/>) decide from, made from an <c>IOperation</c> (<see cref="OperationRanges"/>) or,
/// later, from the IR. It holds no syntax, model or operation, so both producers give the rules the same input.
/// </summary>
/// <param name="Min">The smallest value.</param>
/// <param name="Max">The largest value.</param>
internal readonly record struct IntegerRange(BigInteger Min, BigInteger Max)
{
    /// <summary>Whether <paramref name="value"/> lies in the range.</summary>
    public bool Contains(BigInteger value) => Min <= value && value <= Max;

    /// <summary>The range of one value.</summary>
    public static IntegerRange Constant(BigInteger value) => new(value, value);

    /// <summary>
    /// Every value of an integer type (<c>sbyte</c> to <c>ulong</c> and <c>char</c>); null for any other type.
    /// </summary>
    public static IntegerRange? OfType(SpecialType type) => type switch
    {
        SpecialType.System_SByte => new IntegerRange(sbyte.MinValue, sbyte.MaxValue),
        SpecialType.System_Byte => new IntegerRange(byte.MinValue, byte.MaxValue),
        SpecialType.System_Int16 => new IntegerRange(short.MinValue, short.MaxValue),
        SpecialType.System_UInt16 or SpecialType.System_Char => new IntegerRange(ushort.MinValue, ushort.MaxValue),
        SpecialType.System_Int32 => new IntegerRange(int.MinValue, int.MaxValue),
        SpecialType.System_UInt32 => new IntegerRange(uint.MinValue, uint.MaxValue),
        SpecialType.System_Int64 => new IntegerRange(long.MinValue, long.MaxValue),
        SpecialType.System_UInt64 => new IntegerRange(ulong.MinValue, ulong.MaxValue),
        _ => null
    };
}
