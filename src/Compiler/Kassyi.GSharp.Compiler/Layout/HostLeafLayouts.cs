namespace Kassyi.GSharp.Compiler.Layout;

/// <summary>
/// The host layouts of the leaf types, by the CLR's rules: the primitives and <c>System.Numerics.Vector2/3/4</c>
/// (a struct of floats, 4-byte aligned), and pointers.
/// </summary>
/// <remarks>
/// Independent of every dialect's type map, so a change to a device table never moves the host prediction. Structs
/// and enums are not leaves: <see cref="TypeLayoutCalculator.TryGetHostLayout"/> lays them out from their fields and
/// underlying type.
/// </remarks>
internal static class HostLeafLayouts
{
    /// <summary>A pointer, <c>nint</c> or <c>nuint</c> on a 64-bit host.</summary>
    public static TypeLayout Pointer => new(8, 8);

    /// <summary>The host layout of a leaf type; false for any other type (structs, enums, pointers).</summary>
    public static bool TryGet(ITypeSymbol type, out TypeLayout layout)
    {
        layout = type.SpecialType switch
        {
            SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte => new TypeLayout(1, 1),
            SpecialType.System_Char or SpecialType.System_Int16 or SpecialType.System_UInt16 => new TypeLayout(2, 2),
            SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Single => new TypeLayout(4, 4),
            SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Double => new TypeLayout(8, 8),
            _ => GetNumericsVectorLayout(type)
        };
        return layout.Size > 0;
    }

    // [WHY] Not IsSystemNumericsVector: that also takes the Kassyi.GSharp.Core vectors, whose host layout follows from
    // their fields
    private static TypeLayout GetNumericsVectorLayout(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol
            {
                TypeKind: TypeKind.Struct,
                Arity: 0,
                ContainingType: null,
                ContainingNamespace:
                {
                    Name: TypeConstants.NumericsNamespace,
                    ContainingNamespace:
                    { Name: TypeConstants.SystemNamespace, ContainingNamespace.IsGlobalNamespace: true }
                }
            } vector)
        {
            return default;
        }

        return vector.Name switch
        {
            TypeConstants.Vector2Type => new TypeLayout(8, 4),
            TypeConstants.Vector3Type => new TypeLayout(12, 4),
            TypeConstants.Vector4Type => new TypeLayout(16, 4),
            _ => default
        };
    }
}
