namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// The numeric types whose generic math interface members the BCL implements explicitly with the C# operators and
/// constants, and how those operators differ from the operators C# applies to the type directly.
/// </summary>
internal static class BuiltinNumerics
{
    /// <summary>Whether <paramref name="type"/> is an integer, floating point or <c>char</c> type of C#.</summary>
    public static bool IsBuiltinNumeric(ITypeSymbol type) =>
        type.SpecialType is SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or
            SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or
            SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_IntPtr or
            SpecialType.System_UIntPtr or SpecialType.System_Single or SpecialType.System_Double or
            SpecialType.System_Char;

    /// <summary>
    /// Whether C# promotes <paramref name="type"/> to <c>int</c> before an arithmetic or bitwise operator
    /// (<c>sbyte</c>, <c>byte</c>, <c>short</c>, <c>ushort</c>, <c>char</c>), where the generic math operator returns
    /// the type itself: <c>(byte)(left + right)</c>.
    /// </summary>
    public static bool IsPromoted(ITypeSymbol type) =>
        type.SpecialType is SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or
            SpecialType.System_UInt16 or SpecialType.System_Char;

    /// <summary>
    /// Whether the C# unary minus does not return <paramref name="type"/> itself: it promotes <c>uint</c> to
    /// <c>long</c> and the small types to <c>int</c>, and has no <c>ulong</c> or <c>nuint</c> form, where the generic
    /// math negation is <c>0 - value</c> in the type.
    /// </summary>
    public static bool NegationLeavesType(ITypeSymbol type) =>
        IsPromoted(type) || type.SpecialType is SpecialType.System_UInt32 or SpecialType.System_UInt64 or
            SpecialType.System_UIntPtr;

    /// <summary>
    /// The mask the generic math shift operators of a promoted type apply to the shift count: its width in bits minus
    /// one (7 for <c>byte</c>), where the C# shift of the promoted <c>int</c> masks with 31.
    /// </summary>
    public static int GetShiftMask(ITypeSymbol type) =>
        type.SpecialType is SpecialType.System_SByte or SpecialType.System_Byte ? 7 : 15;

    /// <summary>Whether a promoted type is signed (<c>sbyte</c>, <c>short</c>).</summary>
    public static bool IsSignedPromoted(ITypeSymbol type) =>
        type.SpecialType is SpecialType.System_SByte or SpecialType.System_Int16;

    /// <summary>The bits of a promoted type within an <c>int</c>: <c>0xFF</c> or <c>0xFFFF</c>.</summary>
    public static int GetWidthMask(ITypeSymbol type) => GetShiftMask(type) == 7 ? 0xFF : 0xFFFF;

    /// <summary>
    /// How far a signed promoted type is shifted left and back to sign-extend its low bits within an <c>int</c>: 24 for
    /// <c>sbyte</c>, 16 for <c>short</c>.
    /// </summary>
    public static int GetSignShift(ITypeSymbol type) => GetShiftMask(type) == 7 ? 24 : 16;

    /// <summary>
    /// The value of <c>Zero</c> and <c>AdditiveIdentity</c> (0) or <c>One</c> and <c>MultiplicativeIdentity</c> (1).
    /// </summary>
    public static bool TryGetIdentity(string propertyName, out int value)
    {
        switch (propertyName)
        {
            case "Zero" or "AdditiveIdentity":
                value = 0;
                return true;
            case "One" or "MultiplicativeIdentity":
                value = 1;
                return true;
            default:
                value = 0;
                return false;
        }
    }
}
