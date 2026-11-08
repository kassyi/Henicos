namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.Wmma;

/// <summary>
/// The shape, element type and layout combinations that the WMMA fragments and <c>Wmma.MultiplyAccumulate</c> support.
/// </summary>
internal static class WmmaFragmentTypes
{
    /// <summary>
    /// Whether a fragment type (<c>WmmaMatrixA</c>, <c>WmmaMatrixB</c> or <c>WmmaAccumulator</c>) has a supported
    /// shape, element type and, for the matrix operands, layout.
    /// </summary>
    public static bool IsSupported(INamedTypeSymbol fragmentType, [NotNullWhen(false)] out string? reason)
    {
        if (fragmentType.TypeArguments.Length < 2)
        {
            reason = "fragment is missing required type arguments";
            return false;
        }

        var shape = fragmentType.TypeArguments[0];
        var element = fragmentType.TypeArguments[1];

        return fragmentType.Name is TypeConstants.WmmaTypes.MatrixA or TypeConstants.WmmaTypes.MatrixB
            ? IsSupportedMatrixOperand(fragmentType, shape, element, out reason)
            : IsSupportedAccumulator(shape, element, out reason);
    }

    /// <summary>
    /// Whether <c>Wmma.MultiplyAccumulate</c> supports the shape with these input and accumulator elements.
    /// </summary>
    public static bool IsSupportedMultiplyAccumulate(
        ITypeSymbol shape,
        ITypeSymbol inputElement,
        ITypeSymbol accumulatorElement,
        [NotNullWhen(false)] out string? reason)
    {
        if (IsInt8Shape(shape) &&
            inputElement.SpecialType is SpecialType.System_SByte or SpecialType.System_Byte &&
            accumulatorElement.SpecialType == SpecialType.System_Int32)
        {
            reason = null;
            return true;
        }

        if (IsDoubleShape(shape) &&
            inputElement.SpecialType == SpecialType.System_Double &&
            accumulatorElement.SpecialType == SpecialType.System_Double)
        {
            reason = null;
            return true;
        }

        reason = $"shape '{shape.Name}' with input element '{inputElement.Name}' " +
                 $"and accumulator element '{accumulatorElement.Name}' is not supported";
        return false;
    }

    private static bool IsSupportedMatrixOperand(
        INamedTypeSymbol fragmentType,
        ITypeSymbol shape,
        ITypeSymbol element,
        [NotNullWhen(false)] out string? reason)
    {
        if (fragmentType.TypeArguments.Length < 3 ||
            !WmmaTypeSyntaxHelper.TryGetLayoutCppName(fragmentType.TypeArguments[2], out _))
        {
            reason = "matrix layout must be WmmaRowMajor or WmmaColumnMajor";
            return false;
        }

        if (IsInt8Shape(shape))
        {
            reason = element.SpecialType is SpecialType.System_SByte or SpecialType.System_Byte
                ? null
                : $"shape '{shape.Name}' requires element type 'sbyte' or 'byte' for {fragmentType.Name}, " +
                  $"not '{element.Name}'";
            return reason is null;
        }

        if (IsDoubleShape(shape))
        {
            reason = element.SpecialType == SpecialType.System_Double
                ? null
                : $"shape '{shape.Name}' requires element type 'double' for {fragmentType.Name}, not '{element.Name}'";
            return reason is null;
        }

        reason = UnsupportedShape(shape);
        return false;
    }

    private static bool IsSupportedAccumulator(
        ITypeSymbol shape,
        ITypeSymbol element,
        [NotNullWhen(false)] out string? reason)
    {
        if (IsInt8Shape(shape))
        {
            reason = element.SpecialType == SpecialType.System_Int32
                ? null
                : $"shape '{shape.Name}' requires accumulator element type 'int', not '{element.Name}'";
            return reason is null;
        }

        if (IsDoubleShape(shape))
        {
            reason = element.SpecialType == SpecialType.System_Double
                ? null
                : $"shape '{shape.Name}' requires accumulator element type 'double', not '{element.Name}'";
            return reason is null;
        }

        reason = UnsupportedShape(shape);
        return false;
    }

    private static string UnsupportedShape(ITypeSymbol shape) =>
        $"shape '{shape.Name}' is not a supported WMMA shape marker";

    private static bool IsInt8Shape(ITypeSymbol shape) =>
        shape is INamedTypeSymbol
        {
            Name: TypeConstants.WmmaTypes.M16N16K16
                or TypeConstants.WmmaTypes.M32N8K16
                or TypeConstants.WmmaTypes.M8N32K16
        } && shape.ContainingNamespace.IsCoreNamespace();

    private static bool IsDoubleShape(ITypeSymbol shape) =>
        shape is INamedTypeSymbol { Name: TypeConstants.WmmaTypes.M8N8K4 } &&
        shape.ContainingNamespace.IsCoreNamespace();
}
