namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// How a user struct parameter, or the user struct element of an array-like parameter, crosses to one dialect's device:
/// as it is (<see cref="TransferClass.Identical"/>) or converted element by element through the native shadow struct.
/// </summary>
/// <param name="IsIdenticalStruct">Whether the parameter is a user struct whose layout is <see cref="TransferClass.Identical"/>.</param>
/// <param name="ConvertedElementAotStructPath">
/// The <c>AotContexts</c> path of the element of an array-like parameter whose element is a user struct that is not
/// <see cref="TransferClass.Identical"/>, so that the launcher converts it element by element through a shadow array;
/// otherwise <see langword="null"/>.
/// </param>
/// <param name="ConvertedElementMarshalMethodName"><c>AotContexts.Marshal_{key}</c> of that element, otherwise <see langword="null"/>.</param>
/// <param name="ConvertedElementUnmarshalMethodName"><c>AotContexts.Unmarshal_{key}</c> of that element, otherwise <see langword="null"/>.</param>
/// <param name="ConvertedElementHasArrayFields">
/// Whether the converted element has array fields (<see cref="TransferClassifier.TryFindArrayField"/>), which
/// <c>Marshal_</c> handles even for an <c>[Out]</c> array.
/// </param>
/// <param name="ConvertedElementCopyArraysMethodName">
/// <c>AotContexts.CopyArrays_{key}</c> of that element when it has array fields, on the CPU only (the CUDA text has no
/// such method), otherwise <see langword="null"/>.
/// </param>
internal readonly record struct ParameterTransfer(
    bool IsIdenticalStruct,
    string? ConvertedElementAotStructPath,
    string? ConvertedElementMarshalMethodName,
    string? ConvertedElementUnmarshalMethodName,
    bool ConvertedElementHasArrayFields,
    string? ConvertedElementCopyArraysMethodName)
{
    /// <summary>
    /// The transfer of <paramref name="marshaledType"/> (a user struct or an array-like type) under
    /// <paramref name="dialect"/>.
    /// </summary>
    public static ParameterTransfer Create(ITypeSymbol marshaledType, ITranspilerDialect dialect,
        INamedTypeSymbol? kernelClass)
    {
        if (HostShadowStructs.Has(marshaledType))
        {
            return new ParameterTransfer(
                !HostShadowStructs.IsConverted(marshaledType, dialect),
                null,
                null,
                null,
                false,
                null);
        }

        return marshaledType.TryGetArrayLikeElementType(out var element)
            ? CreateForElement(element, dialect, kernelClass)
            : default;
    }

    /// <summary>
    /// The transfer of the auto result array of a kernel returning <paramref name="returnType"/> under
    /// <paramref name="dialect"/>: converted element by element when <see cref="KernelParameterShape.IsConvertedReturn"/>,
    /// otherwise <see langword="default"/>.
    /// </summary>
    public static ParameterTransfer CreateForReturn(ITypeSymbol returnType, ITranspilerDialect dialect,
        INamedTypeSymbol? kernelClass) =>
        KernelParameterShape.IsConvertedReturn(returnType, dialect)
            ? CreateForElement(returnType, dialect, kernelClass)
            : default;

    // The transfer of the elements of an array of <element>: converted through a shadow array when IsConverted
    private static ParameterTransfer CreateForElement(ITypeSymbol element, ITranspilerDialect dialect,
        INamedTypeSymbol? kernelClass)
    {
        if (!KernelParameterShape.IsConverted(element, dialect))
        {
            return default;
        }

        var hasArrayFields = HostShadowStructs.HoldsArrayFields(element);
        return new ParameterTransfer(
            false,
            AotHostNames.GetAotStructPath(element, kernelClass),
            AotHostNames.GetQualifiedMarshalMethodName(element, kernelClass),
            AotHostNames.GetQualifiedUnmarshalMethodName(element, kernelClass),
            hasArrayFields,
            hasArrayFields && !dialect.IsCuda ? AotHostNames.GetQualifiedCopyArraysMethodName(element, kernelClass) : null);
    }
}
