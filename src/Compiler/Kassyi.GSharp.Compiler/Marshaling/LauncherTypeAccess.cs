namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Whether the launcher, generated into the kernel's class, can name the type of every field it copies.
/// </summary>
/// <remarks>
/// The launcher names the type of each field in the shadow struct, in the <c>Marshal_</c> methods and layout guards
/// of a nested struct and in the host mirror of a struct with fields it cannot name. A field whose type the class
/// cannot see (a private type nested in the struct) would give CS0122 in generated code. The analyzer reports it as
/// GSH0108 and the generator writes a launcher that throws instead (#808).
/// </remarks>
internal static class LauncherTypeAccess
{
    private const int MaxDepth = 16;

    /// <summary>
    /// The first field, in a parameter or the return value of <paramref name="kernel"/>, whose type the kernel's class
    /// cannot access, or <see langword="null"/>.
    /// </summary>
    public static InaccessibleFieldType? Find(IMethodSymbol kernel, Compilation compilation)
    {
        foreach (var parameter in kernel.Parameters)
        {
            if (Find(parameter.Type, kernel.ContainingType, compilation) is { } found)
            {
                return found;
            }
        }

        return kernel.ReturnType.SpecialType == SpecialType.System_Void
            ? null
            : Find(kernel.ReturnType, kernel.ContainingType, compilation);
    }

    /// <summary>
    /// The first field of the user struct of <paramref name="type"/> (the type itself, or the element of an
    /// array-like type), its nested structs included, whose type <paramref name="launcherClass"/> cannot access.
    /// </summary>
    public static InaccessibleFieldType? Find(ITypeSymbol type, INamedTypeSymbol launcherClass,
        Compilation compilation) =>
        UnwrapArrayLikeElements(type) is INamedTypeSymbol structType && TypeLayoutCalculator.IsUserStruct(structType)
            ? FindInStruct(structType, launcherClass, compilation, depth: 0)
            : null;

    private static InaccessibleFieldType? FindInStruct(INamedTypeSymbol structType, INamedTypeSymbol launcherClass,
        Compilation compilation, int depth)
    {
        if (depth > MaxDepth)
        {
            return null;
        }

        foreach (var member in StructAbiMembers.Get(structType))
        {
            if (FindInMember(member, launcherClass, compilation, depth) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    // The member's own type (after its array-like elements), or a field of it when it is a nested user struct
    private static InaccessibleFieldType? FindInMember(StructAbiMember member, INamedTypeSymbol launcherClass,
        Compilation compilation, int depth)
    {
        // GSH0078 reports a member the shadow struct leaves out
        if (NativeStructEmitter.IsOmittedFromShadow(member))
        {
            return null;
        }

        var type = UnwrapArrayLikeElements(member.Type);
        if (!compilation.IsSymbolAccessibleWithin(type, launcherClass))
        {
            return new InaccessibleFieldType(type, member.Name);
        }

        return type is INamedTypeSymbol nested && TypeLayoutCalculator.IsUserStruct(nested) &&
               FindInStruct(nested, launcherClass, compilation, depth + 1) is { } inner
            ? inner with { FieldPath = member.Name + "." + inner.FieldPath }
            : null;
    }

    // T[][] -> T, Span<T> -> T, S?[] -> S (the shadow of a Nullable<S> names S); any other type as it is
    private static ITypeSymbol UnwrapArrayLikeElements(ITypeSymbol type)
    {
        type = (type.UnwrapNullableArrayLike() ?? type).UnwrapNullable();
        while (type.TryGetArrayLikeElementType(out var element))
        {
            type = element.UnwrapNullable();
        }

        return type;
    }
}
