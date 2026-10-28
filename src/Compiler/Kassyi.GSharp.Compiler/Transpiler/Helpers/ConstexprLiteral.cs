namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// Formats a constant value as both a C++ literal and a C# literal, independent of the current culture: a
/// <c>[GpuConstexpr]</c> parameter value (the kernel's <c>constexpr</c> local and the host launcher's variant dispatch),
/// a reference to a <c>const</c> field, an omitted argument and the default of a kernel parameter in the launcher.
/// </summary>
internal static class ConstexprLiteral
{
    private const string CppIntMinValue = "(-2147483647-1)";
    private const string CppLongMinValue = "(-9223372036854775807LL-1)";

    /// <summary>
    /// Formats <paramref name="value"/> of <paramref name="type"/> as a <c>(Cpp, CSharp)</c> literal pair using
    /// <see cref="CultureInfo.InvariantCulture"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="type"/> is not a supported constexpr parameter type, or <paramref name="value"/> is NaN or infinity.
    /// </exception>
    // ReSharper disable once UnusedMember.Global -- Consumed in A-10 Stage 2
    public static (string Cpp, string CSharp) Format(ITypeSymbol type, object value)
    {
        // char has a literal, but it is not a [GpuConstexpr] parameter type
        var specialType = GetPrimitiveType(type);
        if (!specialType.IsPrimitiveValue() || specialType == SpecialType.System_Char)
        {
            throw new ArgumentOutOfRangeException(
                nameof(type),
                type.SpecialType,
                $"Type '{type.SpecialType}' is not a supported [GpuConstexpr] parameter type.");
        }

        return FormatSupported(type, value);
    }

    /// <summary>
    /// Formats the value of a constant (a <c>const</c> field, an omitted argument, the default of a kernel parameter) as
    /// a <c>(Cpp, CSharp)</c> literal pair. Unlike <see cref="Format"/>, NaN and infinity are accepted: C++ reads them
    /// from the Prelude (<see cref="GSharpInternalNames.Prelude.NaNSingle"/> and its siblings, which are not
    /// <c>constexpr</c> on CUDA), C# from <c>float.NaN</c> and the like. A <c>Nullable&lt;T&gt;</c> is formatted as
    /// <c>T</c>. A negative C++ value is parenthesized (<c>(-5)</c>), so that it cannot merge with a <c>-</c> or <c>+</c>
    /// written before it into <c>--</c> or <c>++</c>.
    /// </summary>
    /// <returns><see langword="false"/> when <paramref name="type"/> has no literal on the device (<c>decimal</c>, <c>string</c>).</returns>
    public static bool TryFormatConstant(ITypeSymbol type, object value, out (string Cpp, string CSharp) literal)
    {
        type = UnwrapNullable(type);
        var specialType = GetPrimitiveType(type);
        if (!specialType.IsPrimitiveValue())
        {
            literal = default;
            return false;
        }

        literal = specialType switch
        {
            SpecialType.System_Single when Convert.ToSingle(value, CultureInfo.InvariantCulture) is var single &&
                                           (float.IsNaN(single) || float.IsInfinity(single)) =>
                FormatNonFinite(single, "float", GSharpInternalNames.Prelude.NaNSingle, GSharpInternalNames.Prelude.InfinitySingle),
            SpecialType.System_Double when Convert.ToDouble(value, CultureInfo.InvariantCulture) is var number &&
                                           (double.IsNaN(number) || double.IsInfinity(number)) =>
                FormatNonFinite(number, "double", GSharpInternalNames.Prelude.NaNDouble, GSharpInternalNames.Prelude.InfinityDouble),
            _ => FormatSupported(type, value)
        };
        literal.Cpp = ParenthesizeNegative(literal.Cpp);
        return true;
    }

    /// <summary>
    /// The C++ literal of a constant for the transpiler: <see cref="TryFormatConstant"/>, <c>nullptr</c> for
    /// <see langword="null"/>, and the invariant text of a value without a device literal (<c>decimal</c>, <c>string</c>),
    /// whose use the analyzer reports.
    /// </summary>
    public static string FormatCppConstant(ITypeSymbol type, object? value) => value switch
    {
        null => "nullptr",
        _ when TryFormatConstant(type, value, out var literal) => literal.Cpp,
        IFormattable formattable => ParenthesizeNegative(formattable.ToString(null, CultureInfo.InvariantCulture)),
        _ => value.ToString() ?? string.Empty
    };

    /// <summary>
    /// The C++ form of a C# <see langword="char"/>, <c>(char16_t)&lt;code unit&gt;</c>. A character literal would not do: the
    /// compiler may read the source in another code page, and <c>'é'</c> in UTF-8 is a multi-character <c>int</c>.
    /// </summary>
    public static string FormatCppChar(char value) =>
        "(char16_t)" + ((int)value).ToString(CultureInfo.InvariantCulture);

    // T of Nullable<T>, or the type itself
    private static ITypeSymbol UnwrapNullable(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments: [var underlying] }
            ? underlying
            : type;

    // -5 becomes (-5); a form that is already parenthesized ((-2147483647-1)) stays as it is
    private static string ParenthesizeNegative(string cpp) =>
        cpp.StartsWith("-", StringComparison.Ordinal) ? "(" + cpp + ")" : cpp;

    // The base type of an enum, or the type itself
    private static SpecialType GetPrimitiveType(ITypeSymbol type) =>
        type is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlyingType }
            ? underlyingType.SpecialType
            : type.SpecialType;

    // NaN, (-)infinity: <prelude name> / (-<prelude name>) in C++, <keyword>.NaN / .PositiveInfinity / .NegativeInfinity in C#
    private static (string Cpp, string CSharp) FormatNonFinite(double value, string keyword, string nan, string infinity) =>
        double.IsNaN(value) ? (nan, keyword + ".NaN")
        : value > 0 ? (infinity, keyword + ".PositiveInfinity")
        : ("(-" + infinity + ")", keyword + ".NegativeInfinity");

    private static (string Cpp, string CSharp) FormatSupported(ITypeSymbol type, object value)
    {
        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlyingType })
        {
            var (underlyingCpp, underlyingCSharp) = FormatPrimitive(underlyingType.SpecialType, value);
            var enumTypeName = type.GetFullyQualifiedCsharpName();
            var csharpCast = underlyingCSharp.StartsWith("-", StringComparison.Ordinal)
                ? $"({enumTypeName})({underlyingCSharp})"
                : $"({enumTypeName}){underlyingCSharp}";
            return (underlyingCpp, csharpCast);
        }

        return FormatPrimitive(type.SpecialType, value);
    }

    private static (string Cpp, string CSharp) FormatPrimitive(SpecialType specialType, object value)
    {
        switch (specialType)
        {
            case SpecialType.System_Boolean:
                {
                    var text = Convert.ToBoolean(value, CultureInfo.InvariantCulture) ? "true" : "false";
                    return (text, text);
                }

            case SpecialType.System_Char:
                {
                    var character = Convert.ToChar(value, CultureInfo.InvariantCulture);
                    return (FormatCppChar(character), SymbolDisplay.FormatLiteral(character, true));
                }

            case SpecialType.System_SByte:
                {
                    var text = Convert.ToSByte(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                    return (text, text);
                }

            case SpecialType.System_Int16:
                {
                    var text = Convert.ToInt16(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                    return (text, text);
                }

            case SpecialType.System_Int32:
                {
                    var intValue = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                    var csharp = intValue.ToString(CultureInfo.InvariantCulture);
                    var cpp = intValue == int.MinValue ? CppIntMinValue : csharp;
                    return (cpp, csharp);
                }

            case SpecialType.System_Byte:
                {
                    var text = Convert.ToByte(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                    return (text, text);
                }

            case SpecialType.System_UInt16:
                {
                    var text = Convert.ToUInt16(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                    return (text, text);
                }

            case SpecialType.System_UInt32:
                {
                    var text = Convert.ToUInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "U";
                    return (text, text);
                }

            case SpecialType.System_Int64:
                {
                    var longValue = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                    var digits = longValue.ToString(CultureInfo.InvariantCulture);
                    var cpp = longValue == long.MinValue ? CppLongMinValue : digits + "LL";
                    return (cpp, digits + "L");
                }

            case SpecialType.System_UInt64:
                {
                    var digits = Convert.ToUInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                    return (digits + "ULL", digits + "UL");
                }

            case SpecialType.System_Single:
                return FormatSingle(Convert.ToSingle(value, CultureInfo.InvariantCulture));

            case SpecialType.System_Double:
                return FormatDouble(Convert.ToDouble(value, CultureInfo.InvariantCulture));

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(specialType),
                    specialType,
                    $"Type '{specialType}' is not a supported [GpuConstexpr] parameter type.");
        }
    }

    private static (string Cpp, string CSharp) FormatSingle(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Constexpr floating-point literal must be finite.");
        }

        var core = GetShortestRoundTripSingle(value);
        var cppCore = ContainsDecimalOrExponent(core) ? core : core + ".0";
        return (cppCore + "f", core + "F");
    }

    private static (string Cpp, string CSharp) FormatDouble(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Constexpr floating-point literal must be finite.");
        }

        var core = GetShortestRoundTripDouble(value);
        var cpp = ContainsDecimalOrExponent(core) ? core : core + ".0";
        return (cpp, core + "D");
    }

    private static string GetShortestRoundTripSingle(float value)
    {
        if (value == 0.0f)
        {
            return IsNegativeZero(value) ? "-0" : "0";
        }

        for (var precision = 1; precision < 9; precision++)
        {
            var candidate = FormatGeneralLowerExponent(value, precision);
            if (float.TryParse(candidate, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
                parsed == value)
            {
                return candidate;
            }
        }

        return FormatGeneralLowerExponent(value, 9);
    }

    private static string GetShortestRoundTripDouble(double value)
    {
        if (value == 0.0)
        {
            return IsNegativeZero(value) ? "-0" : "0";
        }

        for (var precision = 1; precision < 17; precision++)
        {
            var candidate = FormatGeneralLowerExponent(value, precision);
            if (double.TryParse(candidate, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
                parsed == value)
            {
                return candidate;
            }
        }

        return FormatGeneralLowerExponent(value, 17);
    }

    private static string FormatGeneralLowerExponent(float value, int precision) =>
        value.ToString("g" + precision.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private static string FormatGeneralLowerExponent(double value, int precision) =>
        value.ToString("g" + precision.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private static bool ContainsDecimalOrExponent(string text) =>
        text.IndexOf('.') >= 0 || text.IndexOf('e') >= 0 || text.IndexOf('E') >= 0;

    private static bool IsNegativeZero(double value) =>
        BitConverter.DoubleToInt64Bits(value) < 0;
}
