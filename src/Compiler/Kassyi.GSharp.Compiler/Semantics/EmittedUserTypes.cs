namespace Kassyi.GSharp.Compiler.Semantics;

/// <summary>
/// Classifies user-defined struct types whose C++ definitions G# emits itself.
/// </summary>
internal static class EmittedUserTypes
{
    /// <summary>
    /// Whether <paramref name="type"/> is a user struct whose definition G# emits itself. Other types
    /// (<c>System_Nullable</c>, shared memory) are defined by the prelude under their legacy spelling, and every
    /// array-like type, <c>ImmutableArray&lt;T&gt;</c> included, is the prelude's <c>GSharpArray&lt;T&gt;</c>.
    /// </summary>
    /// <remarks>
    /// Used for the definition: collecting the structs to define and naming the definition. References use
    /// <c>GlobalCppNames.IsEncodedUserType</c>, which also gives way to the dialect's type map. The definition side
    /// does not consult the map, so a mapped user struct would still be defined under its encoded name; no mapped user
    /// struct exists today.
    /// </remarks>
    public static bool IsEmittedUserType(INamedTypeSymbol type) =>
        type.TypeKind == TypeKind.Struct && !type.ContainingNamespace.IsSystemOrNumerics() && !type.IsArrayLikeType() &&
        !type.IsSharedMemoryType() && !type.IsWmmaFragmentType() && !type.IsWmmaMarkerType();
}
