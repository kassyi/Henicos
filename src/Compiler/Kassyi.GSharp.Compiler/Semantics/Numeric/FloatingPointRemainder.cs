using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Semantics;

/// <summary>
/// The C function that gives the C# result of the <see langword="float"/> and <see langword="double"/> remainder
/// operator <c>%</c> and its compound assignment <c>%=</c> (#727): C++ has no <c>%</c> for floating-point operands.
/// </summary>
/// <remarks>
/// C# defines <c>x % y</c> of floating-point operands as the C <c>fmod</c> result (C# spec §12.10.4): the sign of
/// <c>x</c>, NaN when <c>x</c> is infinite or <c>y</c> is zero, <c>x</c> when <c>y</c> is infinite and <c>x</c> finite,
/// and an exact result. <c>fmodf</c> and <c>fmod</c> are exact in the CUDA math library (0 ulp) and in the C library,
/// so every path gives the C# value. The rules write the function as they write the integer helpers of
/// <see cref="IntegerSemantics"/>: <c>fmodf(a,b)</c>, the compound assignment <c>x=fmodf(x,y)</c>, with the operands in
/// temporaries where they interfere.
/// </remarks>
internal static class FloatingPointRemainder
{
    // The C library spellings, the same in device code and in the native C++ build
    private const string SingleRemainder = "fmodf";
    private const string DoubleRemainder = "fmod";

    /// <summary>
    /// <c>fmodf</c> or <c>fmod</c> for a <see langword="float"/> or <see langword="double"/> <c>%</c> of kind
    /// <paramref name="kind"/> (<paramref name="isBuiltIn"/>, <paramref name="type"/>); null for any other operator.
    /// Shared by the transpiler rules and the IR decision pass.
    /// </summary>
    internal static string? GetHelper(BinaryOperatorKind kind, bool isBuiltIn, ITypeSymbol? type) =>
        kind == BinaryOperatorKind.Remainder && isBuiltIn ? ForType(type) : null;

    /// <summary>
    /// <c>fmodf</c> or <c>fmod</c> for a predefined <see langword="float"/> or <see langword="double"/> <c>%</c>
    /// (<paramref name="operation"/>); null for any other operator.
    /// </summary>
    public static string? GetHelper(IBinaryOperation operation) =>
        GetHelper(operation.OperatorKind, operation is { OperatorMethod: null, IsLifted: false }, operation.Type);

    /// <summary>
    /// <c>fmodf</c> or <c>fmod</c> for a <see langword="float"/> or <see langword="double"/> <c>%=</c> of kind
    /// <paramref name="kind"/> (<paramref name="isBuiltIn"/>, <paramref name="targetType"/>); null for any other
    /// compound assignment. Shared by the transpiler rules and the IR decision pass.
    /// </summary>
    internal static string? GetCompoundHelper(BinaryOperatorKind kind, bool isBuiltIn, ITypeSymbol? targetType) =>
        kind == BinaryOperatorKind.Remainder && isBuiltIn ? ForType(targetType) : null;

    /// <summary>
    /// <c>fmodf</c> or <c>fmod</c> for a predefined <see langword="float"/> or <see langword="double"/> <c>%=</c>
    /// (<paramref name="operation"/>); null for any other compound assignment.
    /// </summary>
    /// <remarks>
    /// The operator has the type of the target: C# accepts <c>x %= y</c> only when <c>y</c> converts implicitly to the
    /// type of <c>x</c> (§12.21.4), so an integer target never meets a floating-point operator, and a <c>float</c> target
    /// never a <c>double</c> one.
    /// </remarks>
    public static string? GetCompoundHelper(ICompoundAssignmentOperation operation) =>
        GetCompoundHelper(operation.OperatorKind, operation is { OperatorMethod: null, IsLifted: false },
            operation.Target.Type);

    private static string? ForType(ITypeSymbol? type) => type?.SpecialType switch
    {
        SpecialType.System_Single => SingleRemainder,
        SpecialType.System_Double => DoubleRemainder,
        _ => null
    };
}
