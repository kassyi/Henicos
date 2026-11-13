using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis.Checks;

/// <summary>
/// The <see cref="IntegerRange"/> of an operand bound by Roslyn: the fact of the syntax path, which the check rules
/// (<see cref="DivisionCheckRule"/>) and the overflow analysis of <see cref="IntegerSemantics"/> read.
/// </summary>
internal static class OperationRanges
{
    /// <summary>
    /// The values <paramref name="operand"/> can take: its constant, the range of what an integer conversion converts
    /// (when it fits the target type, the conversion keeps the value), the <c>Length</c> of an array or a span (never
    /// negative), a launch index or dimension of G# (<see cref="OfLaunchIntrinsic"/>), or the range of its type (an
    /// enum by its underlying type); null for an operand that is not an integer.
    /// </summary>
    /// <remarks>
    /// An int or long operand of an operation of its own type spans the type, which never proves anything; the narrower
    /// types and an int or uint in a long operation (<c>(long)i * i</c>) do.
    /// </remarks>
    public static IntegerRange? Of(IOperation operand)
    {
        if (operand.ConstantValue is { HasValue: true, Value: { } value } &&
            IntegerSemantics.TryGetInteger(value, out var constant))
        {
            return IntegerRange.Constant(constant);
        }

        if (IntegerRange.OfType(UnderlyingOrSelf(operand.Type)?.SpecialType ?? SpecialType.None) is not { } range)
        {
            return null;
        }

        if (operand is IConversionOperation { OperatorMethod: null } conversion &&
            Of(conversion.Operand) is { } inner && inner.Min >= range.Min && inner.Max <= range.Max)
        {
            return inner;
        }

        if (IsLength(operand))
        {
            return new IntegerRange(0, range.Max);
        }

        if (OfLaunchIntrinsic(operand) is { } launch)
        {
            return launch;
        }

        return range;
    }

    /// <summary>
    /// The values of <c>ThreadIndex</c>, <c>BlockIndex</c>, <c>BlockDimension</c> and <c>GridDimension</c> of G#: a
    /// block holds at most 1024 threads on every path (the launch limits that the CPU paths enforce as the GPU does),
    /// so a thread index is 0 to 1023 and a block dimension 1 to 1024; a block index is not negative and a grid
    /// dimension is at least 1. Null for any other operand.
    /// </summary>
    public static IntegerRange? OfLaunchIntrinsic(IOperation operand)
    {
        if (operand is not IPropertyReferenceOperation
            {
                Property: { IsStatic: true, Name: "X" or "Y" or "Z", Type.SpecialType: SpecialType.System_Int32 },
                Property.ContainingType: { } type
            } ||
            !type.ContainingNamespace.IsCoreNamespace())
        {
            return null;
        }

        return type.Name switch
        {
            "ThreadIndex" => new IntegerRange(0, MaxThreadsPerBlock - 1),
            "BlockDimension" => new IntegerRange(1, MaxThreadsPerBlock),
            "BlockIndex" => new IntegerRange(0, int.MaxValue),
            "GridDimension" => new IntegerRange(1, int.MaxValue),
            _ => null
        };
    }

    // The most threads a block holds on every GPU G# targets, and on the CPU paths
    private const int MaxThreadsPerBlock = 1024;

    // a.Length of an array (System.Array) or of Span<T> / ReadOnlySpan<T>
    private static bool IsLength(IOperation operand) =>
        operand is IPropertyReferenceOperation { Property: { Name: "Length", IsStatic: false } property } &&
        property.Type.SpecialType == SpecialType.System_Int32 &&
        (property.ContainingType.SpecialType == SpecialType.System_Array ||
         property.ContainingType is { Name: "Span" or "ReadOnlySpan", IsGenericType: true } span &&
         span.ContainingNamespace.IsSystemNamespace());

    private static ITypeSymbol? UnderlyingOrSelf(ITypeSymbol? type) =>
        type is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlying } ? underlying : type;
}
