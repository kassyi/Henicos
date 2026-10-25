namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// Formats a <c>[GpuConstexpr]</c> parameter value as both a C++ literal (for the kernel's <c>constexpr</c> local) and
/// a C# literal (for the host launcher's variant dispatch).
/// </summary>
// ReSharper disable once UnusedType.Global -- Consumed in A-10 Stage 2
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
