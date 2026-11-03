namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Scalar type kinds supported by the differential kernel fuzzer
/// (8 integer types, <see langword="bool"/>, <see langword="char"/>, and 4 device enum types; no floating-point).
/// </summary>
public enum FuzzScalarKind
{
    SByte,
    Byte,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Boolean,
    Char,
    EnumByte,
    EnumShort,
    EnumInt,
    EnumULong,
}

/// <summary>
/// Metadata and C# semantic rules for a <see cref="FuzzScalarKind"/>.
/// </summary>
public sealed record FuzzTypeInfo(
    FuzzScalarKind Kind,
    string Keyword,
    int BitWidth,
    bool IsSigned,
    bool IsIntegral,
    bool IsEnum,
    FuzzScalarKind UnderlyingKind,
    string LiteralToken = "LIT_INT")
{
    /// <summary>Gets whether this type is narrower than 32-bit <see langword="int"/> and promotes to <see langword="int"/> in binary arithmetic.</summary>
    public bool IsNarrowIntegral => IsIntegral && !IsEnum && BitWidth < 32;

    /// <summary>Gets the ordered catalog of all supported scalar types.</summary>
    public static IReadOnlyList<FuzzTypeInfo> All { get; } =
    [
        new(FuzzScalarKind.SByte, "sbyte", 8, IsSigned: true, IsIntegral: true, IsEnum: false, FuzzScalarKind.SByte, "LIT_SBYTE"),
        new(FuzzScalarKind.Byte, "byte", 8, IsSigned: false, IsIntegral: true, IsEnum: false, FuzzScalarKind.Byte, "LIT_BYTE"),
        new(FuzzScalarKind.Int16, "short", 16, IsSigned: true, IsIntegral: true, IsEnum: false, FuzzScalarKind.Int16, "LIT_SHORT"),
        new(FuzzScalarKind.UInt16, "ushort", 16, IsSigned: false, IsIntegral: true, IsEnum: false, FuzzScalarKind.UInt16, "LIT_USHORT"),
        new(FuzzScalarKind.Int32, "int", 32, IsSigned: true, IsIntegral: true, IsEnum: false, FuzzScalarKind.Int32, "LIT_INT"),
        new(FuzzScalarKind.UInt32, "uint", 32, IsSigned: false, IsIntegral: true, IsEnum: false, FuzzScalarKind.UInt32, "LIT_UINT"),
        new(FuzzScalarKind.Int64, "long", 64, IsSigned: true, IsIntegral: true, IsEnum: false, FuzzScalarKind.Int64, "LIT_LONG"),
        new(FuzzScalarKind.UInt64, "ulong", 64, IsSigned: false, IsIntegral: true, IsEnum: false, FuzzScalarKind.UInt64, "LIT_ULONG"),
        new(FuzzScalarKind.Boolean, "bool", 1, IsSigned: false, IsIntegral: false, IsEnum: false, FuzzScalarKind.Boolean, "LIT_BOOL"),
        new(FuzzScalarKind.Char, "char", 16, IsSigned: false, IsIntegral: true, IsEnum: false, FuzzScalarKind.Char, "LIT_CHAR"),
        new(FuzzScalarKind.EnumByte, "FuzzEnumByte", 8, IsSigned: false, IsIntegral: false, IsEnum: true, FuzzScalarKind.Byte, "LIT_ENUM"),
        new(FuzzScalarKind.EnumShort, "FuzzEnumShort", 16, IsSigned: true, IsIntegral: false, IsEnum: true, FuzzScalarKind.Int16, "LIT_ENUM"),
        new(FuzzScalarKind.EnumInt, "FuzzEnumInt", 32, IsSigned: true, IsIntegral: false, IsEnum: true, FuzzScalarKind.Int32, "LIT_ENUM"),
        new(FuzzScalarKind.EnumULong, "FuzzEnumULong", 64, IsSigned: false, IsIntegral: false, IsEnum: true, FuzzScalarKind.UInt64, "LIT_ENUM"),
    ];

    /// <summary>Gets the subset of integral numeric types (excluding <see langword="bool"/> and enums).</summary>
    public static IReadOnlyList<FuzzTypeInfo> IntegralTypes { get; } =
    [
        All[(int)FuzzScalarKind.SByte],
        All[(int)FuzzScalarKind.Byte],
        All[(int)FuzzScalarKind.Int16],
        All[(int)FuzzScalarKind.UInt16],
        All[(int)FuzzScalarKind.Int32],
        All[(int)FuzzScalarKind.UInt32],
        All[(int)FuzzScalarKind.Int64],
        All[(int)FuzzScalarKind.UInt64],
        All[(int)FuzzScalarKind.Char],
    ];

    /// <summary>Gets the subset of enum types.</summary>
    public static IReadOnlyList<FuzzTypeInfo> EnumTypes { get; } =
    [
        All[(int)FuzzScalarKind.EnumByte],
        All[(int)FuzzScalarKind.EnumShort],
        All[(int)FuzzScalarKind.EnumInt],
        All[(int)FuzzScalarKind.EnumULong],
    ];

    /// <summary>Returns the <see cref="FuzzTypeInfo"/> for <paramref name="kind"/>.</summary>
    public static FuzzTypeInfo Get(FuzzScalarKind kind) => All[(int)kind];

    /// <summary>Looks up the canonical literal token (e.g. <c>LIT_SBYTE</c>) for a C# scalar keyword.</summary>
    public static string? TryGetLiteralTokenByKeyword(string keyword)
    {
        for (int i = 0; i < IntegralTypes.Count; i++)
        {
            var info = IntegralTypes[i];
            if (string.Equals(info.Keyword, keyword, StringComparison.Ordinal))
            {
                return info.LiteralToken;
            }
        }

        return null;
    }

    /// <summary>Returns the canonical literal token for a parsed Roslyn literal <paramref name="value"/>.</summary>
    public static string GetLiteralTokenForValue(object? value) => value switch
    {
        sbyte => Get(FuzzScalarKind.SByte).LiteralToken,
        byte => Get(FuzzScalarKind.Byte).LiteralToken,
        short => Get(FuzzScalarKind.Int16).LiteralToken,
        ushort => Get(FuzzScalarKind.UInt16).LiteralToken,
        int => Get(FuzzScalarKind.Int32).LiteralToken,
        uint => Get(FuzzScalarKind.UInt32).LiteralToken,
        long => Get(FuzzScalarKind.Int64).LiteralToken,
        ulong => Get(FuzzScalarKind.UInt64).LiteralToken,
        bool => Get(FuzzScalarKind.Boolean).LiteralToken,
        char => Get(FuzzScalarKind.Char).LiteralToken,
        float => "LIT_FLOAT",
        double => "LIT_DOUBLE",
        _ => Get(FuzzScalarKind.Int32).LiteralToken,
    };

    /// <summary>
    /// Returns the C# unary numeric promotion result type for <paramref name="kind"/>
    /// (<c>sbyte</c>, <c>byte</c>, <c>short</c>, <c>ushort</c>, and <c>char</c> promote to <c>int</c>).
    /// </summary>
    public static FuzzScalarKind PromoteUnary(FuzzScalarKind kind) => kind switch
    {
        FuzzScalarKind.SByte or FuzzScalarKind.Byte or FuzzScalarKind.Int16 or FuzzScalarKind.UInt16 or FuzzScalarKind.Char or FuzzScalarKind.Int32 =>
            FuzzScalarKind.Int32,
        FuzzScalarKind.UInt32 => FuzzScalarKind.UInt32,
        FuzzScalarKind.Int64 => FuzzScalarKind.Int64,
        FuzzScalarKind.UInt64 => FuzzScalarKind.UInt64,
        _ => kind,
    };
}
