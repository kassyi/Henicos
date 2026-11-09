using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Ir.Building;

/// <summary>Classifies a Roslyn conversion as an <see cref="IrConversionKind"/>.</summary>
internal static class IrConversionKinds
{
    /// <summary>The kind of <paramref name="conversion"/> from <paramref name="from"/> to <paramref
    /// name="to"/>.</summary>
    /// <param name="conversion">The conversion.</param>
    /// <param name="from">The operand type, or <see langword="null"/> when unknown.</param>
    /// <param name="to">The type converted to, or <see langword="null"/> when unknown.</param>
    /// <param name="operandIsConstant">Whether the operand is a constant expression.</param>
    public static IrConversionKind Of(CommonConversion conversion, ITypeSymbol? from, ITypeSymbol? to,
        bool operandIsConstant)
    {
        if (conversion.IsIdentity)
        {
            return IrConversionKind.Identity;
        }

        if (conversion.IsUserDefined)
        {
            return IrConversionKind.UserDefined;
        }

        if (conversion.IsNullable)
        {
            return IrConversionKind.Nullable;
        }

        if (from is { TypeKind: TypeKind.Enum } || to is { TypeKind: TypeKind.Enum })
        {
            return IrConversionKind.Enumeration;
        }

        if (conversion.IsNumeric)
        {
            return IrConversionKind.Numeric;
        }

        if (conversion.IsImplicit && operandIsConstant && IsNumeric(from) && IsNumeric(to))
        {
            return IrConversionKind.ImplicitConstant;
        }

        if (from is IPointerTypeSymbol || to is IPointerTypeSymbol)
        {
            return IrConversionKind.Pointer;
        }

        if (conversion.IsReference)
        {
            return IrConversionKind.Reference;
        }

        return (from, to) switch
        {
            ({ IsValueType: true }, { IsReferenceType: true }) => IrConversionKind.Boxing,
            ({ IsReferenceType: true }, { IsValueType: true }) => IrConversionKind.Unboxing,
            _ => IrConversionKind.Other
        };
    }

    /// <summary>Whether <paramref name="type"/> is a numeric type of C# (<c>char</c> included) or an enum.</summary>
    public static bool IsNumericOrEnum(ITypeSymbol? type) => type is { TypeKind: TypeKind.Enum } || IsNumeric(type);

    private static bool IsNumeric(ITypeSymbol? type) => type?.SpecialType is
        SpecialType.System_Char or SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or
        SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or
        SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Single or
        SpecialType.System_Double or SpecialType.System_Decimal or SpecialType.System_IntPtr or
        SpecialType.System_UIntPtr;
}
