namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>Which fields of a user struct the generated launcher can name on the struct itself.</summary>
internal static class HostFieldAccess
{
    /// <summary>
    /// Whether code generated into <paramref name="launcherAssembly"/> can write <c>value.<paramref name="field"/></c>:
    /// the field is public, or internal in the launcher's own assembly.
    /// </summary>
    /// <remarks>
    /// A struct cannot declare a protected field (CS0666), so the others are private fields and internal fields of
    /// another assembly. <c>InternalsVisibleTo</c> is not consulted: a field treated as unnameable goes through the
    /// host mirror, which is correct either way.
    /// </remarks>
    internal static bool CanName(IFieldSymbol field, IAssemblySymbol launcherAssembly) =>
        field.DeclaredAccessibility == Accessibility.Public ||
        (field.DeclaredAccessibility is Accessibility.Internal or Accessibility.ProtectedOrInternal &&
         field.ContainingAssembly.IsEqualTo(launcherAssembly));

    /// <summary>
    /// Whether the launcher reads and writes <paramref name="member"/> only through the host mirror: the hidden field of
    /// a captured primary constructor parameter, the backing field of a property whose accessors use the <c>field</c>
    /// keyword, or a field or fixed buffer that <see cref="CanName"/> rejects.
    /// </summary>
    internal static bool ReadsThroughMirror(StructAbiMember member, IAssemblySymbol launcherAssembly) =>
        member.Kind is StructAbiMemberKind.CapturedParameter or StructAbiMemberKind.FieldBackedProperty ||
        (member is { Kind: StructAbiMemberKind.Field or StructAbiMemberKind.FixedBuffer, Symbol: IFieldSymbol field } &&
         !CanName(field, launcherAssembly));

    /// <summary>
    /// Whether the shadow struct and the host mirror declare <paramref name="member"/> as the underlying type of its
    /// enum type: an enum member that <see cref="ReadsThroughMirror"/>.
    /// </summary>
    internal static bool IsStoredAsUnderlyingType(StructAbiMember member, IAssemblySymbol launcherAssembly) =>
        member.Type.TypeKind == TypeKind.Enum && ReadsThroughMirror(member, launcherAssembly);

    /// <summary>
    /// The type that the shadow struct and the host mirror declare for <paramref name="member"/>: an enum that only the
    /// mirror reads is its underlying type, which generated code can always name; any other member keeps its type.
    /// </summary>
    /// <remarks>
    /// The enum type of such a member may be one the launcher cannot name, but then the member is only read through the
    /// mirror: a field that the launcher can name has a type at least as accessible as itself (CS0052), and the
    /// kernel's class sees the field's struct. The value is the same: an enum is its underlying value (ECMA-335
    /// II.14.3), so the copy needs no cast.
    /// </remarks>
    internal static ITypeSymbol GetStorageType(StructAbiMember member, IAssemblySymbol launcherAssembly) =>
        IsStoredAsUnderlyingType(member, launcherAssembly) &&
        member.Type is INamedTypeSymbol { EnumUnderlyingType: { } underlying }
            ? underlying
            : member.Type;

    /// <summary>
    /// Whether the launcher can check the offset of <paramref name="member"/> on the struct itself with
    /// <c>Unsafe.ByteOffset</c>: an ordinary field that <see cref="CanName"/> accepts and whose type is not a pointer,
    /// which cannot be a type argument of <c>Unsafe.As</c>.
    /// </summary>
    internal static bool CanCheckOffset(StructAbiMember member, IAssemblySymbol launcherAssembly) =>
        member is { Kind: StructAbiMemberKind.Field, Symbol: IFieldSymbol field } &&
        !IsPointer(field.Type) &&
        CanName(field, launcherAssembly);

    /// <summary>
    /// Whether <paramref name="type"/> is a pointer, which cannot be a type argument of <c>Unsafe.As</c>.
    /// </summary>
    internal static bool IsPointer(ITypeSymbol type) => type is IPointerTypeSymbol or IFunctionPointerTypeSymbol;
}
