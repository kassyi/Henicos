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
}
