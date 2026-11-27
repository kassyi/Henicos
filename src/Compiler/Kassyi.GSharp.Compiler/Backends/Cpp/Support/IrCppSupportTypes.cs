namespace Kassyi.GSharp.Compiler.Backends.Cpp.Support;

/// <summary>
/// The types the C++ writer cannot spell, for which the legacy route reports a diagnostic.
/// </summary>
internal static class IrCppSupportTypes
{
    /// <summary>
    /// The shape of <paramref name="type"/> when the C++ writer cannot spell it (<c>String</c>,
    /// <c>FunctionPointer</c>, <c>TypeParameter</c>, <c>Pointer</c>); <see langword="null"/> when it can.
    /// </summary>
    /// <remarks>
    /// These are the types for which the legacy route reports a diagnostic while it spells a declaration or a type;
    /// the IR writer reports none, so a body with one of them stays on the legacy route.
    /// </remarks>
    public static string? UnspellableShape(ITypeSymbol type)
    {
        if (GpuCodeConstraints.IsString(type))
        {
            return "String";
        }

        if (GpuCodeConstraints.IsFunctionPointer(type))
        {
            return "FunctionPointer";
        }

        if (IsOrContainsTypeParameter(type))
        {
            return "TypeParameter";
        }

        return type is IPointerTypeSymbol ? "Pointer" : null;
    }

    /// <summary>The <c>Type:&lt;shape&gt;</c> answer for an unspellable <paramref name="shape"/>.</summary>
    public static IrSupport Unspelled(string shape) => IrSupport.Unspelled("Type:" + shape);

    private static bool IsOrContainsTypeParameter(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol => true,
        IArrayTypeSymbol array => IsOrContainsTypeParameter(array.ElementType),
        IPointerTypeSymbol pointer => IsOrContainsTypeParameter(pointer.PointedAtType),
        INamedTypeSymbol named => named.TypeArguments
            .AsStackEnumerable()
            .Any(static argument => IsOrContainsTypeParameter(argument)),
        _ => false
    };
}
