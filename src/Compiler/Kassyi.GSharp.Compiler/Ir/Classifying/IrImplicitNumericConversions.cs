namespace Kassyi.GSharp.Compiler.Ir.Classifying;

/// <summary>
/// The implicit numeric conversions of C# (C# spec §10.2.3), which never overflow, so a checked context cannot make
/// them throw. Roslyn classifies a cast that does one (<c>(long)i</c>) as the same implicit conversion.
/// </summary>
internal static class IrImplicitNumericConversions
{
    /// <summary>
    /// Whether C# has an implicit numeric conversion from <paramref name="from"/> to <paramref name="to"/>.
    /// </summary>
    public static bool Exists(ITypeSymbol? from, ITypeSymbol? to) =>
        (from?.SpecialType ?? SpecialType.None, to?.SpecialType ?? SpecialType.None) switch
        {
            (SpecialType.System_SByte, var target) => target is SpecialType.System_Int16 or
                SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_IntPtr || IsReal(target),
            (SpecialType.System_Byte, var target) => target is SpecialType.System_Int16 or
                SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or
                SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_IntPtr or
                SpecialType.System_UIntPtr || IsReal(target),
            (SpecialType.System_Int16, var target) => target is SpecialType.System_Int32 or
                SpecialType.System_Int64 or SpecialType.System_IntPtr || IsReal(target),
            (SpecialType.System_UInt16 or SpecialType.System_Char, var target) => target is
                SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or
                SpecialType.System_UInt64 or SpecialType.System_IntPtr or SpecialType.System_UIntPtr ||
                (target == SpecialType.System_UInt16 && from?.SpecialType == SpecialType.System_Char) ||
                IsReal(target),
            (SpecialType.System_Int32, var target) => target is SpecialType.System_Int64 or
                SpecialType.System_IntPtr || IsReal(target),
            (SpecialType.System_UInt32, var target) => target is SpecialType.System_Int64 or
                SpecialType.System_UInt64 or SpecialType.System_UIntPtr || IsReal(target),
            (SpecialType.System_Int64 or SpecialType.System_UInt64, var target) => IsReal(target),
            (SpecialType.System_IntPtr, var target) => target == SpecialType.System_Int64 || IsReal(target),
            (SpecialType.System_UIntPtr, var target) => target == SpecialType.System_UInt64 || IsReal(target),
            (SpecialType.System_Single, SpecialType.System_Double) => true,
            _ => false
        };

    // float, double and decimal, which every integral type converts to implicitly
    private static bool IsReal(SpecialType type) =>
        type is SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;
}
